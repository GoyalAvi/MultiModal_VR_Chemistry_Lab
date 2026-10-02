// Moving objects: single-destination moves, the word-timed "put that there", move animations and
// "move to burner".

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;

public partial class ParserIntent
{
    // Rays from when "that" and "there" were heard. The object ray ignores nothing, so the selection
    // can be picked too; the destination ray ignores the moved object.
    private void MoveTwoPoint(RecognizedIntent intent, string command)
    {
        IReadOnlyList<float> times = _speechTiming.PointingWordTimes;
        if (pointingSource == null
            || !pointingSource.TryGetLatestRayBefore(times[0], pointingTimeTolerance, out PointingRay objectRay, out float objectTime)
            || !pointingSource.TryGetLatestRayBefore(times[1], pointingTimeTolerance, out PointingRay destRay, out float destTime))
        {
            Refuse($"[ParserIntent] {command}: no target — no pointing recorded at the pointing words.");
            return;
        }

        PointingRay picked = pointingSource.Recast(objectRay.Origin, objectRay.Direction, null);
        VRObject vro = IsTarget(picked.HitObject);
        if (vro == null && IsSelectable(picked.HitObject) != null)
            Debug.Log($"[ParserIntent] {command}: skipped {picked.HitObject.name} — outside \"only {_context.Describe()}\"");
        if (vro == null)
        {
            vro = NearestSelectableToRay(picked.Origin, picked.Direction, out float angle);
            if (vro != null)
                Debug.Log($"[ParserIntent] {command}: \"that\" hit {(picked.HitObject != null ? picked.HitObject.name : "nothing")} — near-miss pick {vro.name} ({angle:F1}°)");
        }
        if (vro == null && _context.HasFilter && (IsSelectable(picked.HitObject) != null
                            || NearestSelectableToRay(picked.Origin, picked.Direction, out _, applyFilter: false) != null))
        {
            LogFilteredOut(command, IsSelectable(picked.HitObject) != null ? IsSelectable(picked.HitObject).gameObject : null);
            return;
        }
        if (vro == null)
        {
            // "that" on the wall does nothing; don't move the selection instead
            Refuse($"[ParserIntent] {command}: nothing selectable (hit: {(picked.HitObject != null ? picked.HitObject.name : "none")})", warning: false);
            return;
        }
        GameObject target = vro.gameObject;

        PointingRay destination = pointingSource.Recast(destRay.Origin, destRay.Direction, target.transform);
        MoveWithPlacement(target, destination, $"word-timed (object {objectTime - times[0]:+0.00;-0.00}s @\"{picked.HitObject?.name ?? "none"}\")",
                          destTime, times[1], command);
    }

    // Same sample choice as create (engine slot, else the buffered ray at sentence end), re-cast past the
    // moved object. No pointing, no move.
    private void MoveToPointed(GameObject target, RecognizedIntent intent, string command)
    {
        if (!TryGetCreateRay(intent, out PointingRay ray, out string source, out float sampleTime))
        {
            Refuse($"[ParserIntent] {command}: no target — no pointing data.");
            return;
        }

        PointingRay destination = pointingSource != null
            ? pointingSource.Recast(ray.Origin, ray.Direction, target.transform)
            : ray;
        float sentenceEnd = _speechTiming != null && _speechTiming.HasUtterance ? _speechTiming.UtteranceEndTime : sampleTime;
        MoveWithPlacement(target, destination, source, sampleTime, sentenceEnd, command);
    }

    // Uses the create placement rules and keeps the rotation. Physics is paused during the move; Float/Drop
    // applies on arrival.
    private void MoveWithPlacement(GameObject go, PointingRay ray, string source, float sampleTime, float referenceTime, string command)
    {
        if (!GuardTarget(go, command)) return;
        FinishActiveMove(go);
        PausePhysicsForMove(go);

        Vector3 start = go.transform.position;
        Physics.SyncTransforms();
        // A burner carries the containers on its stand: they're ignored by its overlap nudging and move
        // by the same offset
        List<GameObject> riders = CollectBurnerRiders(go);
        SpawnPlan plan;
        string state;
        _overlapIgnore.Clear();
        foreach (GameObject rider in riders) _overlapIgnore.Add(rider.transform);
        try
        {
            plan = PlanFromRay(ray, HorizontalRadius(GetPlacementBounds(go)), go);
            state = PlaceMember(go, plan, plan.Point);       // place it to compute the final spot...
        }
        finally { _overlapIgnore.Clear(); }
        Vector3 end = go.transform.position;
        LogBurnerPlacement(go, ray, plan, riders.Count);
        go.transform.position = start;                       // ...then animate from where it was
        Physics.SyncTransforms();
        MoveRiders(riders, end - start, moveSpeed);

        _lastPlan = plan;
        _hasLastPlan = true;
        _lastPlanFinal = end;

        StartMove(go, end, moveSpeed, () =>
        {
            ApplyLandingPhysics(go, state);
            if (go != null) SoundFeedback.PlayAt(SoundFeedback.Sound.Move, go.transform.position);
        });
        string placement = plan.Case == state ? plan.Case : $"{plan.Case} → {state}";
        Debug.Log($"[ParserIntent] {command} {go.name}: {source} + {placement}{plan.Detail}"
                  + $" | ray hit: {(ray.HitObject != null ? ray.HitObject.name : "none")}"
                  + $" | sample {sampleTime - referenceTime:+0.00;-0.00}s"
                  + $" | {start.ToString("F2")} → {end.ToString("F2")}");
    }

    // One animation per object: a new move first snaps the running one to its end, so two coroutines
    // never fight over the same transform

    private struct ActiveMove
    {
        public Coroutine Routine;
        public Vector3 Target;
    }

    private readonly Dictionary<GameObject, ActiveMove> _activeMoves = new Dictionary<GameObject, ActiveMove>();

    private void StartMove(GameObject go, Vector3 target, float speed, System.Action onArrive)
    {
        FinishActiveMove(go);
        _activeMoves[go] = new ActiveMove { Routine = StartCoroutine(MoveRoutine(go, target, speed, onArrive)), Target = target };
    }

    private void FinishActiveMove(GameObject go)
    {
        FinishActiveRotation(go);
        if (go == null || !_activeMoves.TryGetValue(go, out ActiveMove move)) return;
        if (move.Routine != null) StopCoroutine(move.Routine);
        go.transform.position = move.Target;
        _activeMoves.Remove(go);
    }

    private IEnumerator MoveRoutine(GameObject go, Vector3 target, float speed, System.Action onArrive)
    {
        while (go != null && (go.transform.position - target).sqrMagnitude > 1e-6f)
        {
            go.transform.position = Vector3.MoveTowards(go.transform.position, target, speed * Time.deltaTime);
            yield return null;
        }
        if (go == null) yield break;
        go.transform.position = target; // end exactly on the planned position
        _activeMoves.Remove(go);
        onArrive?.Invoke();
    }

    // Stops a Drop-mode fall and makes the Rigidbody kinematic so physics can't fight the move
    private void PausePhysicsForMove(GameObject go)
    {
        if (_dropRoutines.TryGetValue(go, out Coroutine drop))
        {
            if (drop != null) StopCoroutine(drop);
            _dropRoutines.Remove(go);
        }

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) return;
        rb.isKinematic = true;
        rb.useGravity = false;
        if (_tempDropBodies.Remove(rb)) Destroy(rb);
    }

    private readonly List<GameObject> _burnerRiders = new List<GameObject>();

    // Lab objects standing on the burner stand. Empty for anything that isn't a burner; the list is reused.
    private List<GameObject> CollectBurnerRiders(GameObject go)
    {
        _burnerRiders.Clear();
        BunsenBurner burner = go != null ? go.GetComponent<BunsenBurner>() : null;
        if (burner == null) return _burnerRiders;

        Physics.SyncTransforms();
        Bounds solid = GetPlacementBounds(go);
        Vector3 standPoint = burner.Stand != null ? burner.Stand.position : new Vector3(solid.center.x, solid.max.y, solid.center.z);
        float lowest = Mathf.Min(standPoint.y, solid.max.y) - 0.03f;
        float highest = Mathf.Max(standPoint.y, solid.max.y) + 0.06f;
        float reach = Mathf.Max(HorizontalRadius(solid), 0.06f);

        foreach (VRObject vro in FindObjectsByType<VRObject>(FindObjectsSortMode.None))
        {
            GameObject other = vro.gameObject;
            if (other == go || other.transform.IsChildOf(go.transform) || other.GetComponent<BunsenBurner>() != null) continue;
            Bounds b = GetPlacementBounds(other);
            Vector2 flat = new Vector2(b.center.x - standPoint.x, b.center.z - standPoint.z);
            if (b.min.y >= lowest && b.min.y <= highest && flat.magnitude <= reach) _burnerRiders.Add(other);
        }
        return _burnerRiders;
    }

    // Same speed as the burner, so they arrive together and stay on the stand
    private void MoveRiders(List<GameObject> riders, Vector3 offset, float speed)
    {
        foreach (GameObject rider in riders)
        {
            FinishActiveMove(rider);
            PausePhysicsForMove(rider);
            StartMove(rider, rider.transform.position + offset, speed, null);
            Debug.Log($"[ParserIntent] {rider.name} is standing on the burner — moved along by {offset.ToString("F3")}.");
        }
    }

    // Debug log: pointed spot vs. the burner base after placement
    private static void LogBurnerPlacement(GameObject go, PointingRay ray, SpawnPlan plan, int riderCount)
    {
        BunsenBurner burner = go.GetComponent<BunsenBurner>();
        if (burner == null || !burner.TryGetSolidBounds(out _, out Vector3 baseCentre)) return;
        Vector3 pointed = ray.HitObject != null ? ray.HitPoint : plan.Point;
        Vector3 target = plan.OnSurface ? new Vector3(plan.Point.x, plan.SurfaceY, plan.Point.z) : plan.Point;
        Vector3 flatOffset = Vector3.ProjectOnPlane(baseCentre - pointed, Vector3.up);
        Debug.Log($"[ParserIntent] burner placement {go.name}: pointed {pointed.ToString("F3")} | planned {target.ToString("F3")} ({plan.Case})"
                  + $" | base after placement {baseCentre.ToString("F3")} | offset {(baseCentre - pointed).ToString("F3")}"
                  + $" (horizontal {flatOffset.magnitude * 1000f:F0} mm) | riders: {riderCount}");
    }

    // Onto the stand of the nearest burner
    void MoveToBurner(GameObject go, string rule)
    {
        if (!GuardTarget(go, "move to burner")) return;

        BunsenBurner burner = null;
        float best = float.MaxValue;
        foreach (BunsenBurner b in FindObjectsByType<BunsenBurner>(FindObjectsSortMode.None))
        {
            if (b.Stand == null || b.gameObject == go) continue;
            float dist = (b.transform.position - go.transform.position).sqrMagnitude;
            if (dist < best) { best = dist; burner = b; }
        }
        if (burner == null)
        {
            Refuse("[ParserIntent] Move to burner: no BunsenBurner with a stand assigned found in scene.");
            return;
        }

        // The bottom of the object sits on the stand point, not its pivot
        FinishActiveMove(go);
        PausePhysicsForMove(go);
        Vector3 start = go.transform.position;
        SetOnSurface(go, burner.Stand.position, burner.Stand.position.y);
        Vector3 end = go.transform.position;
        go.transform.position = start;
        Physics.SyncTransforms();

        StartMove(go, end, moveSpeed, () =>
        {
            if (go != null) SoundFeedback.PlayAt(SoundFeedback.Sound.Move, go.transform.position);
        });
        Debug.Log($"[ParserIntent] moveToBurner: target {go.name} (rule: {rule}) → destination: stand of {burner.name} | {start.ToString("F2")} → {end.ToString("F2")}");
    }
}
