// Creating objects: count, prefab lookup, tagging, and laying out a row on the planned point.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MMI.Fusion;
using MMI.Semantics;

public partial class ParserIntent
{
    // The count slot already holds digits; default 1, capped at maxCreateCount
    private int ParseCreateCount(string spoken)
    {
        if (!int.TryParse(spoken, out int n) || n < 1) n = 1;
        int cap = Mathf.Max(1, maxCreateCount);
        if (n > cap)
        {
            Debug.LogWarning($"[ParserIntent] Create: {n} requested, capped at maxCreateCount = {cap}.");
            n = cap;
        }
        return n;
    }

    // Spawns a straight row centred on the pointed spot (else in front of the user), running horizontally
    // and perpendicular to the pointing direction
    void CreateObjects(string color, string obj, int count, PointingRay? ray, string source, string sampleInfo = "")
    {
        if (string.IsNullOrEmpty(obj))
        {
            Refuse("[ParserIntent] create: no object type heard — nothing created.");
            return;
        }

        GameObject prefab = FindSpawnPrefab(obj);
        if (prefab == null)
        {
            Refuse($"[ParserIntent] No prefab found for '{obj}'.");
            return;
        }

        // The first instance measures the object and plans the row; the rest are spawned one by one so the
        // overlap checks only see siblings already in place
        Vector3 roughPos = ray.HasValue ? ray.Value.HitPoint : Vector3.zero;
        GameObject first = SpawnTagged(prefab, color, obj, roughPos);

        Physics.SyncTransforms();
        Bounds firstBounds = GetPlacementBounds(first);
        SpawnPlan plan = ray.HasValue ? PlanFromRay(ray.Value, HorizontalRadius(firstBounds), first) : PlanFallback(first);
        Vector3 rowDir = RowDirection(plan);
        float spacing = RowWidth(firstBounds, rowDir) + rowGap;
        _lastRowSpacing = spacing;

        var positions = new StringBuilder();
        var created = new List<GameObject>(count);
        for (int i = 0; i < count; i++)
        {
            GameObject go = i == 0 ? first : SpawnTagged(prefab, color, obj, roughPos);
            created.Add(go);
            _commandTargets.Add(go);
            Vector3 pos = plan.Point + rowDir * RowOffset(i, count, spacing);
            string state = PlaceMember(go, plan, pos);
            if (faceUserOnCreate) FaceUser(go);
            ApplyLandingPhysics(go, state);
            SoundFeedback.PlayAt(SoundFeedback.Sound.Create, go.transform.position, i * 0.07f); // staggered for rows
            positions.Append(i == 0 ? "" : "; ").Append('#').Append(i + 1).Append(' ')
                     .Append(go.transform.position.ToString("F2")).Append(' ').Append(state);
            if (i == count / 2) _lastPlanFinal = GetPlacementBounds(go).center;
        }
        _lastPlan = plan;
        _hasLastPlan = true;

        // A single new object becomes the selection ("it"); a row becomes the last group ("them")
        if (count == 1)
        {
            if (IsTarget(first) != null) SelectObject(first, color, obj);
            else Debug.Log($"[ParserIntent] Created {first.name} is outside \"only {_context.Describe()}\" — not selected.");
        }
        else
        {
            _context.SetGroup(created);
        }

        string placement = ray.HasValue
            ? (plan.Case == plan.Landing ? $"{source} + {plan.Case}" : $"{source} + {plan.Case} → {plan.Landing}")
            : "fallback";
        Debug.Log($"[ParserIntent] Created {count}x {DescribeObjects(color, obj, count)} — centre: {placement}{plan.Detail}"
                  + $" | row dir {rowDir.ToString("F2")}, spacing {spacing:F2} m | drop-snap: {(plan.DropSnapped ? "yes" : "no")}{sampleInfo}"
                  + $" | positions: {positions}");
    }

    // Prefabs are named after their type
    private GameObject FindSpawnPrefab(string obj)
    {
        if (spawnablePrefabs == null) return null;
        string searchKey = obj.ToLower().Replace(" ", "").Replace("_", "");
        foreach (var p in spawnablePrefabs)
        {
            if (p == null) continue;
            string prefabKey = p.name.ToLower().Replace(" ", "").Replace("_", "");
            if (prefabKey.Contains(searchKey) || searchKey.Contains(prefabKey))
                return p;
        }
        return null;
    }

    // Tags type/colour/origin right away so queries find the object. Keeps the prefab's rotation: some
    // prefabs (testtube, Beaker) bake a corrective rotation to stand upright.
    private GameObject SpawnTagged(GameObject prefab, string color, string obj, Vector3 position)
    {
        GameObject go = Instantiate(prefab, position, prefab.transform.rotation);
        go.name = $"{color}_{obj}_{go.GetInstanceID()}";

        VRObject vro = go.GetComponent<VRObject>();
        if (vro != null)
        {
            vro.objectType = obj;
            vro.isSpawned = true;
            if (!string.IsNullOrEmpty(color))
                vro.SetColor(color, NameToColor(color));
        }
        else if (!string.IsNullOrEmpty(color))
        {
            ApplyColor(go, color);
        }
        return go;
    }

    private static float RowOffset(int i, int count, float spacing) => (i - (count - 1) * 0.5f) * spacing;

    // Perpendicular to the pointing direction (the viewer's facing for the fallback)
    private Vector3 RowDirection(SpawnPlan plan)
    {
        Vector3 forward = plan.HasRay ? Vector3.ProjectOnPlane(plan.RayDirection, Vector3.up) : Vector3.zero;
        if (forward.sqrMagnitude < 1e-4f) // no ray, or pointing straight down
            forward = FlatDirection(ViewerTransform(), useRight: false);
        return Vector3.Cross(Vector3.up, forward.normalized).normalized;
    }

    private static float RowWidth(Bounds b, Vector3 dir) =>
        2f * (Mathf.Abs(b.extents.x * dir.x) + Mathf.Abs(b.extents.z * dir.z));

    // The row member must land on the same surface as the row centre, at about the same height;
    // false = past that surface's edge
    private bool TryRowMemberSurface(SpawnPlan plan, Vector3 pos, GameObject self, out float y)
    {
        y = plan.SurfaceY;
        if (plan.Surface == null) return true; // fallback at table height, nothing to check
        Vector3 probe = new Vector3(pos.x, plan.SurfaceY + surfaceProbeHeight, pos.z);
        if (!TryFindSurfaceBelow(probe, surfaceProbeHeight + 0.05f, self, out float hitY, out Collider c)) return false;
        if (!IsSameObject(c.transform, plan.Surface.transform) || Mathf.Abs(hitY - plan.SurfaceY) > 0.03f) return false;
        y = hitY;
        return true;
    }

    // Past the edge of the surface a member floats at the surface height instead of dropping. Float/Drop
    // is applied separately (ApplyLandingPhysics) so a move can apply it after its animation.
    private string PlaceMember(GameObject go, SpawnPlan plan, Vector3 pos)
    {
        string state;
        if (plan.OnSurface)
        {
            bool supported = TryRowMemberSurface(plan, pos, go, out float y);
            SetOnSurface(go, pos, y);
            NudgeOutOfOverlap(go, y, plan.RequireSurface && supported);
            state = supported ? plan.Landing : "edgeFloat";
        }
        else
        {
            Physics.SyncTransforms();
            Bounds b = GetPlacementBounds(go);
            go.transform.position += pos - b.center;
            NudgeOutOfOverlap(go, 0f, requireSurface: false);
            state = plan.Landing;
        }

        EnsureHeadClearance(go);
        return state;
    }

    // Optional (faceUserOnCreate). "Front" is the forward axis, or the up axis for prefabs whose
    // corrective rotation tips forward vertical.
    private void FaceUser(GameObject go)
    {
        Transform head = ViewerTransform();
        if (head == null) return;
        Physics.SyncTransforms();
        Vector3 pivot = GetPlacementBounds(go).center;

        Vector3 toUser = Vector3.ProjectOnPlane(head.position - pivot, Vector3.up);
        Vector3 front = Vector3.ProjectOnPlane(go.transform.forward, Vector3.up);
        if (front.sqrMagnitude < 0.09f) front = Vector3.ProjectOnPlane(go.transform.up, Vector3.up);
        if (toUser.sqrMagnitude < 1e-4f || front.sqrMagnitude < 1e-4f) return;

        float degrees = Vector3.SignedAngle(front, toUser, Vector3.up);
        YawAround(pivot, go.transform.position, go.transform.rotation, degrees, out Vector3 p, out Quaternion r);
        go.transform.SetPositionAndRotation(p, r);
    }
}
