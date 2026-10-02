// "make it this big": speech + the hand distance (GestureSample from HandDistanceSource).

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;

public partial class ParserIntent
{
    private const float MinResizeHandDistance = 0.03f;   // hands closer than this count as "together"
    private const float MinResizeRatio = 0.2f;           // same limits as BimanualScaler, relative to the original scale
    private const float MaxResizeRatio = 3.0f;
    private const float ResizeDuration = 0.35f;

    private readonly Dictionary<GameObject, Coroutine> _resizeRoutines = new Dictionary<GameObject, Coroutine>();

    // Scales uniformly so the chosen dimension (big = largest, tall = height, wide = width) equals the hand
    // distance, 0.2-3x the original scale. Target: pointed before "this big", else named, else the selection.
    void HandleResizeTo(RecognizedIntent intent)
    {
        string axis = intent.GetParameter("sizeAxis") ?? "big";
        GameObject target = ResolveResizeTarget(intent, out string rule);
        if (target == null) return; // already refused by ResolveResizeTarget
        if (!GuardTarget(target, "resize")) return;

        float hands = intent.Gesture.HasValue && intent.Gesture.Value.Kind == HandDistanceSource.Kind ? intent.Gesture.Value.Value : -1f;
        if (hands < MinResizeHandDistance)
        {
            Refuse(hands < 0f
                ? "[ParserIntent] resize refused: hands weren't tracked while \"this\" was spoken."
                : $"[ParserIntent] resize refused: hands only {hands * 100f:F1} cm apart (min {MinResizeHandDistance * 100f:F0} cm).");
            return;
        }

        FinishActiveResize(target);
        Physics.SyncTransforms();
        Bounds b = GetPlacementBounds(target);
        float before = axis == "tall" ? b.size.y
                     : axis == "wide" ? Mathf.Max(b.size.x, b.size.z)
                     : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (before < 1e-4f)
        {
            Refuse($"[ParserIntent] resize refused: {target.name} has no measurable size.");
            return;
        }

        Vector3 current = target.transform.localScale;
        Vector3 goal = current * (hands / before);
        VRObject vro = target.GetComponent<VRObject>();
        Vector3 original = vro != null ? vro.OriginalScale : current;
        float ratio = original.magnitude > 1e-6f ? goal.magnitude / original.magnitude : 1f;
        float clampedRatio = Mathf.Clamp(ratio, MinResizeRatio, MaxResizeRatio);
        bool clamped = !Mathf.Approximately(ratio, clampedRatio);
        if (clamped) goal *= clampedRatio / ratio;
        float after = before * (goal.magnitude / current.magnitude);

        _resizeRoutines[target] = StartCoroutine(ResizeRoutine(target, goal));
        Debug.Log($"[ParserIntent] resize {target.name} (rule: {rule}): axis {axis} | hands {hands * 100f:F1} cm"
                  + $" | size {before * 100f:F1} → {after * 100f:F1} cm | clamped: {(clamped ? $"yes ({ratio:F2}× original → {clampedRatio:F2}×)" : "no")}");
    }

    private GameObject ResolveResizeTarget(RecognizedIntent intent, out string rule)
    {
        string color = intent.GetParameter("color");
        string obj   = intent.GetParameter("objectType");
        bool propertySpoken = !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(obj);

        // A pointing word before "this big" ("make that this big"): the ray when that word was spoken
        GameObject pointed = null;
        bool pointingSpoken = false;
        if (_speechTiming != null)
        {
            IReadOnlyList<string> words = _speechTiming.UtteranceWords;
            int marker = InteractionSetup.FindSizeMarker(words);
            int ordinal = -1;
            for (int i = 0; i < marker; i++)
                if (System.Array.IndexOf(InteractionSetup.PointingTriggerWords, words[i]) >= 0) ordinal++;
            if (ordinal >= 0)
            {
                pointingSpoken = true;
                IReadOnlyList<float> times = _speechTiming.PointingWordTimes;
                if (pointingSource != null && ordinal < times.Count
                    && pointingSource.TryGetLatestRayBefore(times[ordinal], pointingTimeTolerance, out PointingRay ray, out _))
                {
                    PointingRay picked = pointingSource.Recast(ray.Origin, ray.Direction, null); // may pick the selection too
                    VRObject vro = IsTarget(picked.HitObject);
                    if (vro == null) vro = NearestSelectableToRay(picked.Origin, picked.Direction, out _);
                    if (vro != null) pointed = vro.gameObject;
                }
            }
        }

        if (pointed != null || propertySpoken)
        {
            GameObject target = ResolveTarget(pointed, color, obj);
            rule = pointed != null ? "pointed" : "named";
            if (target == null) Refuse($"[ParserIntent] resize: no target — nothing matches {DescribeObjects(color, obj, 1)}.");
            return target;
        }
        if (pointingSpoken)
        {
            // Pointing at the wall resizes nothing; don't fall back to the selection
            rule = "pointed";
            Refuse("[ParserIntent] resize: nothing selectable at the pointing word.", warning: false);
            return null;
        }

        rule = "selection";
        if (_selectedObject == null) Refuse("[ParserIntent] resize: no target — select something, point at it (\"make that this big\") or name it.");
        return _selectedObject;
    }

    private void FinishActiveResize(GameObject go)
    {
        if (!_resizeRoutines.TryGetValue(go, out Coroutine running)) return;
        if (running != null) StopCoroutine(running);
        _resizeRoutines.Remove(go);
    }

    // Keeps the bottom centre in place so the object doesn't sink into or lift off its surface
    private IEnumerator ResizeRoutine(GameObject go, Vector3 goal)
    {
        Vector3 start = go.transform.localScale;
        Bounds b = GetPlacementBounds(go);
        Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
        for (float t = 0f; go != null; )
        {
            t = Mathf.Min(1f, t + Time.deltaTime / ResizeDuration);
            go.transform.localScale = Vector3.Lerp(start, goal, Mathf.SmoothStep(0f, 1f, t));
            Physics.SyncTransforms();
            Bounds now = GetPlacementBounds(go);
            go.transform.position += bottom - new Vector3(now.center.x, now.min.y, now.center.z);
            if (t >= 1f) break;
            yield return null;
        }
        if (go != null) _resizeRoutines.Remove(go);
    }
}
