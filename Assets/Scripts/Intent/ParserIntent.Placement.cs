// Placement engine shared by create and move: which pointing sample to use, the placement cases,
// surface probes, overlap nudging, head clearance and Float/Drop.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;

public partial class ParserIntent
{
    // The engine slot combined with the current ray direction, else the last buffered ray before the
    // sentence ended: where the user pointed when they finished speaking, not while sweeping mid-sentence.
    // False only when there's no pointing data at all.
    private bool TryGetCreateRay(RecognizedIntent intent, out PointingRay ray, out string source, out float sampleTime)
    {
        sampleTime = Time.time;
        if (intent.PointingTargets.Count > 0)
        {
            PointingSample s = intent.PointingTargets[0];
            if (pointingSource != null && pointingSource.TryGetCurrentRay(out PointingRay current))
            {
                float distance = s.HitObject != null ? Vector3.Distance(current.Origin, s.HitPoint) : 0f;
                ray = new PointingRay(current.Origin, current.Direction, s.HitObject, s.HitPoint, distance);
                source = "slot";
                return true;
            }
            Transform viewer = ViewerTransform();
            if (s.HitObject != null && viewer != null)
            {
                Vector3 toHit = s.HitPoint - viewer.position;
                ray = new PointingRay(viewer.position, toHit.normalized, s.HitObject, s.HitPoint, toHit.magnitude);
                source = "slot";
                return true;
            }
        }

        if (pointingSource != null && _speechTiming != null && _speechTiming.HasUtterance
            && pointingSource.TryGetLatestRayBefore(_speechTiming.UtteranceEndTime, pointingTimeTolerance, out ray, out sampleTime))
        {
            source = "buffered";
            return true;
        }

        ray = default;
        source = "fallback";
        return false;
    }

    // Only the air landings need anything
    private void ApplyLandingPhysics(GameObject go, string state)
    {
        if (go == null) return;
        if (state == "airDrop") _dropRoutines[go] = StartCoroutine(DropWithPhysics(go));
        else if (state == "airFloat" || state == "edgeFloat") MakeFloat(go);
    }

    private readonly Dictionary<GameObject, Coroutine> _dropRoutines = new Dictionary<GameObject, Coroutine>();

    private readonly HashSet<Rigidbody> _tempDropBodies = new HashSet<Rigidbody>();

    private static readonly RaycastHit[] _placementHits = new RaycastHit[16];

    private static readonly Collider[]   _overlapHits   = new Collider[32];

    private const float SurfaceEpsilon = 0.002f;

    private Transform ViewerTransform()
    {
        Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        return cam != null ? cam.transform : null;
    }

    // Where and how an object is placed, plus what was cast (for the gizmos)
    private struct SpawnPlan
    {
        public Vector3 Point;        // bounds centre target (XZ; also Y when not on a surface)
        public bool    OnSurface;    // bottom rests on SurfaceY
        public float   SurfaceY;
        public Collider Surface;     // null = nominal table height
        public bool    RequireSurface; // nudging must stay on the same surface
        public string  Case;         // surface / hitNoSurface / farClamped / airPlane / airRay / fallback
        public string  Landing;      // surface / snapped / dropSurface / airFloat / airDrop / fallback
        public bool    DropSnapped;  // placed via the downward cast from an air point
        public string  Detail;

        public bool    HasRay;
        public Vector3 RayOrigin, RayDirection;
        public float   RayLength;
        public bool    HasDrop;
        public Vector3 DropFrom;
        public float   DropLength;
    }

    // Priority: 1. surface hit within maxPointDistance -> on top (beside a hit lab object);
    // 2. farther hit -> on the ray at maxPointDistance; 3. no hit -> the table-height plane if pointing
    // down within reach, else airSpawnDistance along the ray. Then drop-cast for a surface or stay in the air.
    private SpawnPlan PlanFromRay(PointingRay ray, float radius, GameObject self)
    {
        Vector3 dir = ray.Direction.sqrMagnitude > 1e-6f ? ray.Direction.normalized : Vector3.forward;
        SpawnPlan plan = new SpawnPlan { HasRay = true, RayOrigin = ray.Origin, RayDirection = dir, Detail = "" };
        Vector3 point;

        if (ray.HitObject != null && ray.HitDistance <= maxPointDistance)
        {
            plan.RayLength = ray.HitDistance;
            if (TryPlanOnHitSurface(ray, radius, self, ref plan)) return plan;

            // Close hit with no surface below (a wall): back off along the ray so the object isn't inside it
            point = ray.HitPoint - dir * (radius + spawnClearance);
            plan.Case = "hitNoSurface";
        }
        else if (ray.HitObject != null)
        {
            point = ray.Origin + dir * maxPointDistance;
            plan.Case = "farClamped";
            plan.RayLength = maxPointDistance;
        }
        else
        {
            float t = dir.y < -0.01f ? (fallbackTableHeight - ray.Origin.y) / dir.y : -1f;
            if (t > 0f && t <= maxPointDistance)
            {
                point = ray.Origin + dir * t;
                plan.Case = "airPlane";
                plan.RayLength = t;
            }
            else
            {
                float d = Mathf.Clamp(airSpawnDistance, 0.4f, Mathf.Max(0.4f, maxPointDistance));
                point = ray.Origin + dir * d;
                plan.Case = "airRay";
                plan.RayLength = d;
            }
        }

        point = KeepAwayFromHead(point, radius);
        plan.Point = point;

        // Float stays at the air point and only snaps onto a surface practically touching it (e.g. an airPlane
        // point on the table top). Drop searches maxDropDistance below and lands there.
        float searchAbove = airSpawnMode == AirSpawnMode.Float ? snapTolerance : 0.05f;
        float searchBelow = airSpawnMode == AirSpawnMode.Float ? snapTolerance : maxDropDistance;
        plan.HasDrop = true;
        plan.DropFrom = point + Vector3.up * searchAbove;
        plan.DropLength = searchAbove + searchBelow;
        if (TryFindSurfaceBelow(plan.DropFrom, plan.DropLength, self, out float surfaceY, out Collider snapSurface))
        {
            plan.Surface = snapSurface;
            plan.OnSurface = true;
            plan.RequireSurface = true;
            plan.SurfaceY = surfaceY;
            plan.DropSnapped = true;
            plan.Landing = airSpawnMode == AirSpawnMode.Float ? "snapped" : "dropSurface";
            plan.DropLength = plan.DropFrom.y - surfaceY;
        }
        else
        {
            plan.Landing = airSpawnMode == AirSpawnMode.Float ? "airFloat" : "airDrop";
        }
        return plan;
    }

    // No hit normal, so find the surface with a short down-probe from above the hit; this also turns a hit
    // on the table's side into a spot on its top. A hit lab object gets the new one placed beside it.
    private bool TryPlanOnHitSurface(PointingRay ray, float radius, GameObject self, ref SpawnPlan plan)
    {
        Vector3 target = ray.HitPoint;
        float probeTop = ray.HitPoint.y + surfaceProbeHeight;
        float probeDistance = surfaceProbeHeight + surfaceProbeDepth;

        VRObject anchor = ray.HitObject.GetComponentInParent<VRObject>();
        if (anchor != null && (self == null || anchor.gameObject != self))
        {
            Bounds a = GetPlacementBounds(anchor.gameObject);
            Vector3 side = FlatDirection(ViewerTransform(), useRight: true);
            target = a.center + side * (HorizontalRadius(a) + radius + spawnClearance);
            probeTop = a.max.y + surfaceProbeHeight;
            probeDistance = a.size.y + surfaceProbeHeight + surfaceProbeDepth;
            plan.Detail = $", placed beside {anchor.name}";
        }

        Vector3 probe = new Vector3(target.x, probeTop, target.z);
        plan.HasDrop = true;
        plan.DropFrom = probe;
        plan.DropLength = probeDistance;
        // Without a lab-object anchor the surface must be the hit object itself, not e.g. a cube below a wall hit
        if (!TryFindSurfaceBelow(probe, probeDistance, self, out float surfaceY, out Collider surface)
            || (anchor == null && !IsSameObject(surface.transform, ray.HitObject.transform)))
        {
            plan.Detail = "";
            return false;
        }

        plan.Point = target;
        plan.OnSurface = true;
        plan.RequireSurface = true;
        plan.SurfaceY = surfaceY;
        plan.Surface = surface;
        plan.DropLength = probeTop - surfaceY;
        plan.Case = "surface";
        plan.Landing = "surface";
        return true;
    }

    private static bool IsSameObject(Transform a, Transform b) => a == b || a.IsChildOf(b) || b.IsChildOf(a);

    // No pointing data: fallbackForwardDistance in front of the viewer (pitch ignored), on the table if found
    private SpawnPlan PlanFallback(GameObject self)
    {
        SpawnPlan plan = new SpawnPlan { Case = "fallback", Landing = "fallback", OnSurface = true, SurfaceY = fallbackTableHeight };
        Transform viewer = ViewerTransform();
        if (viewer == null)
        {
            plan.Detail = " (no camera found)";
            return plan;
        }

        Vector3 target = viewer.position + FlatDirection(viewer, useRight: false) * fallbackForwardDistance;
        float lowestAccepted = fallbackTableHeight - fallbackSnapTolerance;
        plan.HasDrop = true;
        plan.DropFrom = new Vector3(target.x, viewer.position.y, target.z);
        plan.DropLength = Mathf.Max(0.1f, viewer.position.y - lowestAccepted);

        bool snapped = TryFindSurfaceBelow(plan.DropFrom, plan.DropLength, self, out float surfaceY, out Collider fallbackSurface)
                       && Mathf.Abs(surfaceY - fallbackTableHeight) <= fallbackSnapTolerance;
        if (snapped) plan.Surface = fallbackSurface;
        plan.Point = target;
        plan.SurfaceY = snapped ? surfaceY : fallbackTableHeight;
        plan.RequireSurface = snapped;
        plan.Detail = snapped ? " (snapped to surface in front of user)" : " (table height, no surface found)";
        return plan;
    }

    private Vector3 KeepAwayFromHead(Vector3 point, float radius)
    {
        Transform head = ViewerTransform();
        if (head == null) return point;
        Vector3 away = point - head.position;
        float min = minHeadDistance + radius;
        if (away.magnitude >= min) return point;
        Vector3 dir = away.sqrMagnitude > 1e-6f ? away.normalized : FlatDirection(head, useRight: false);
        return head.position + dir * min;
    }

    // Last check after nudging: push the object horizontally away from the head if it's too close
    private void EnsureHeadClearance(GameObject go)
    {
        Transform head = ViewerTransform();
        if (head == null) return;
        Physics.SyncTransforms();
        Bounds b = GetPlacementBounds(go);
        float min = minHeadDistance + HorizontalRadius(b);
        Vector3 away = b.center - head.position;
        if (away.magnitude >= min) return;

        Vector3 flat = Vector3.ProjectOnPlane(away, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f) flat = FlatDirection(head, useRight: false);
        go.transform.position += flat.normalized * (min - away.magnitude);
        Debug.Log($"[ParserIntent] Create: pushed {go.name} away from the head to keep {minHeadDistance} m clearance.");
    }

    // The lab prefabs have no Rigidbody, so they float anyway; one that has one is made kinematic
    private static void MakeFloat(GameObject go)
    {
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) return;
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // Adds a temporary Rigidbody if needed and removes (or restores) it once settled, so later moves behave
    // as usual. Rotation is frozen so the object lands upright.
    private IEnumerator DropWithPhysics(GameObject go)
    {
        Rigidbody rb = go.GetComponent<Rigidbody>();
        bool added = rb == null;
        if (added)
        {
            rb = go.AddComponent<Rigidbody>();
            _tempDropBodies.Add(rb);
        }

        bool wasKinematic = rb.isKinematic;
        bool hadGravity = rb.useGravity;
        RigidbodyConstraints oldConstraints = rb.constraints;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        yield return new WaitForFixedUpdate();
        float elapsed = 0f;
        while (go != null && rb != null && elapsed < 4f && !rb.IsSleeping())
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (go != null) _dropRoutines.Remove(go);
        if (go == null || rb == null) yield break;

        if (added)
        {
            _tempDropBodies.Remove(rb);
            Destroy(rb);
        }
        else
        {
            rb.isKinematic = wasKinematic;
            rb.useGravity = hadGravity;
            rb.constraints = oldConstraints;
        }
    }

    private static Vector3 FlatDirection(Transform viewer, bool useRight)
    {
        if (viewer == null) return useRight ? Vector3.right : Vector3.forward;
        Vector3 dir = Vector3.ProjectOnPlane(useRight ? viewer.right : viewer.forward, Vector3.up);
        if (!useRight && dir.sqrMagnitude < 1e-4f) // looking straight up or down
            dir = Vector3.ProjectOnPlane(viewer.up, Vector3.up);
        return dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;
    }

    // Nearest real surface straight down (table, floor, shelf): skips the object itself, lab objects and
    // anything the pointing ray ignores
    private bool TryFindSurfaceBelow(Vector3 origin, float distance, GameObject self, out float surfaceY, out Collider surface)
    {
        surfaceY = 0f;
        surface = null;
        Physics.SyncTransforms();
        int mask = pointingSource != null ? pointingSource.EffectiveMask : Physics.DefaultRaycastLayers;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _placementHits, distance, mask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = _placementHits[i].collider;
            if (self != null && c.transform.IsChildOf(self.transform)) continue;
            if (c.GetComponentInParent<VRObject>() != null) continue;
            if (pointingSource != null && pointingSource.IsPlayerCollider(c)) continue;
            if (_placementHits[i].distance < best)
            {
                best = _placementHits[i].distance;
                surfaceY = _placementHits[i].point.y;
                surface = c;
            }
        }
        return surface != null;
    }

    // A burner is aligned by the centre of its foot, not its pivot or full bounds (the stand arm is off-centre)
    private static void SetOnSurface(GameObject go, Vector3 target, float surfaceY)
    {
        Physics.SyncTransforms();
        Bounds b = GetPlacementBounds(go);
        Vector3 anchor = b.center;
        BunsenBurner burner = go.GetComponent<BunsenBurner>();
        if (burner != null && burner.TryGetSolidBounds(out _, out Vector3 baseCentre)) anchor = baseCentre;
        go.transform.position += new Vector3(target.x - anchor.x, surfaceY + SurfaceEpsilon - b.min.y, target.z - anchor.z);
    }

    // Tries 8 directions in up to 2 rings; with requireSurface the spot must be on the same surface height
    private void NudgeOutOfOverlap(GameObject go, float surfaceY, bool requireSurface)
    {
        Physics.SyncTransforms();
        Bounds b = GetPlacementBounds(go);
        if (!Overlaps(go, b, b.center)) return;

        float step = HorizontalRadius(b) * 2f + spawnClearance;
        for (int ring = 1; ring <= 2; ring++)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 45f * i, 0f) * Vector3.forward * (step * ring);
                Vector3 center = b.center + offset;
                if (Overlaps(go, b, center)) continue;
                if (requireSurface)
                {
                    Vector3 probe = new Vector3(center.x, surfaceY + surfaceProbeHeight, center.z);
                    if (!TryFindSurfaceBelow(probe, surfaceProbeHeight + 0.05f, go, out float y, out _) || Mathf.Abs(y - surfaceY) > 0.03f)
                        continue;
                }
                go.transform.position += offset;
                Debug.Log($"[ParserIntent] Create: nudged {go.name} by {offset} to avoid overlapping another object.");
                return;
            }
        }
        Debug.LogWarning($"[ParserIntent] Create: {go.name} overlaps another object and no free spot was found nearby.");
    }

    // Extra objects the overlap check ignores: the containers riding along during a burner move
    private static readonly List<Transform> _overlapIgnore = new List<Transform>();

    // Shrunk slightly so touching neighbours don't count as overlapping
    private static bool Overlaps(GameObject go, Bounds b, Vector3 center)
    {
        int count = Physics.OverlapBoxNonAlloc(center, b.extents * 0.9f, _overlapHits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform t = _overlapHits[i].transform;
            if (t.IsChildOf(go.transform) || IsOverlapIgnored(t)) continue;
            return true;
        }
        return false;
    }

    private static bool IsOverlapIgnored(Transform t)
    {
        foreach (Transform ignored in _overlapIgnore)
            if (ignored != null && t.IsChildOf(ignored)) return true;
        return false;
    }

    private static float HorizontalRadius(Bounds b) => Mathf.Max(b.extents.x, b.extents.z);

    private static readonly List<Collider> _boundsColliders = new List<Collider>();

    private static readonly List<Renderer> _boundsRenderers = new List<Renderer>();

    // Enabled non-trigger colliders, else renderers. A burner uses only its solid body.
    private static Bounds GetPlacementBounds(GameObject go)
    {
        BunsenBurner burner = go.GetComponent<BunsenBurner>();
        if (burner != null && burner.TryGetSolidBounds(out Bounds solid, out _)) return solid;

        bool has = false;
        Bounds result = new Bounds(go.transform.position, Vector3.zero);
        go.GetComponentsInChildren(false, _boundsColliders);
        foreach (Collider c in _boundsColliders)
        {
            if (!c.enabled || c.isTrigger) continue;
            if (has) result.Encapsulate(c.bounds); else { result = c.bounds; has = true; }
        }
        if (has) return result;
        go.GetComponentsInChildren(false, _boundsRenderers);
        foreach (Renderer r in _boundsRenderers)
        {
            if (has) result.Encapsulate(r.bounds); else { result = r.bounds; has = true; }
        }
        return result;
    }
}
