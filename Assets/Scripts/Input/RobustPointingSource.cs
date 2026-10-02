using System;
using UnityEngine;
using MMI.Fusion;

/// <summary>Pointing source with its own raycast; when nothing is hit it reports a point freeSpaceDistance along the ray.</summary>
// That's what lets "move it there" target empty space. The course's Pointing.cs only fires on an
// XRRayInteractor hit, so it can't do that. rayOrigin is a scene reference (the right controller).
public class RobustPointingSource : MonoBehaviour, IPointingInputSource
{
    [SerializeField] private Transform rayOrigin;
    [Tooltip("Real objects farther than this are ignored, same as a normal pointing ray's range.")]
    [SerializeField] private float maxDistance = 10f;
    [Tooltip("How far out to project a virtual destination point when the ray hits nothing.")]
    [SerializeField] private float freeSpaceDistance = 2f;
    [Tooltip("Layers that count as hits. Ignore Raycast is always excluded.")]
    [SerializeField] private LayerMask hitMask = ~0;
    [Tooltip("Extra hierarchies that never count as hits. The XR Origin is ignored automatically.")]
    [SerializeField] private Transform[] extraIgnoreRoots;

    private const int IgnoreRaycastLayer = 2;

    /// <summary>Layer mask of the pointing ray; placement casts reuse it.</summary>
    public int EffectiveMask => hitMask & ~(1 << IgnoreRaycastLayer);

    // The player's own rig (character controller, hands, interactors) must never catch the ray
    private Transform _rigRoot;

    void Awake()
    {
        var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null) _rigRoot = origin.transform;
        else Debug.LogWarning("[RobustPointingSource] No XROrigin found - player/hand colliders are only ignored via hitMask/extraIgnoreRoots.");
    }

    /// <summary>True for colliders of the XR rig or of extraIgnoreRoots.</summary>
    public bool IsPlayerCollider(Collider c)
    {
        Transform t = c.transform;
        if (_rigRoot != null && t.IsChildOf(_rigRoot)) return true;
        if (extraIgnoreRoots != null)
            foreach (Transform root in extraIgnoreRoots)
                if (root != null && t.IsChildOf(root)) return true;
        return false;
    }

    public event Action<PointingSample> PointingChanged;

    /// <summary>The pointing controller; its roll drives twist rotation.</summary>
    public Transform RayOrigin => rayOrigin;

    // The selection gets nudged toward the viewer, so it often sits between the controller and what the
    // user points at next (e.g. the container to mix into). The ray has to pass through it.
    private Transform _ignoreRoot;

    /// <summary>Ignore this object's colliders (null = ignore nothing).</summary>
    public void SetIgnoreObject(GameObject go) => _ignoreRoot = go != null ? go.transform : null;

    private static readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    // About 5 s of rays (256 x 0.02 s) so commands can use where the user pointed while speaking.
    // Sampled at a fixed interval so the span doesn't depend on the headset's frame rate.
    private const int HistoryCapacity = 256;
    [Tooltip("Seconds between entries in the pointing history ring buffer (256 entries).")]
    [SerializeField] private float historyInterval = 0.02f;

    private struct TimedRay
    {
        public float Time;
        public PointingRay Ray;
    }

    private readonly TimedRay[] _history = new TimedRay[HistoryCapacity];
    private int _historyHead;
    private int _historyCount;
    private float _nextHistoryTime;

    private PointingRay _current;
    private bool _hasCurrent;

    /// <summary>This frame's ray; false while the controller is inactive or untracked.</summary>
    public bool TryGetCurrentRay(out PointingRay ray)
    {
        ray = _current;
        return _hasCurrent;
    }

    /// <summary>Newest recorded ray at or before time (Time.time) and at most maxAge older. Misses are returned too.</summary>
    public bool TryGetLatestRayBefore(float time, float maxAge, out PointingRay ray, out float sampleTime)
    {
        ray = default;
        sampleTime = 0f;
        int best = -1;
        float bestTime = float.NegativeInfinity;
        for (int i = 0; i < _historyCount; i++)
        {
            float t = _history[i].Time;
            if (t > time || time - t > maxAge || t <= bestTime) continue;
            bestTime = t;
            best = i;
        }
        if (best < 0) return false;

        ray = _history[best].Ray;
        sampleTime = bestTime;
        return true;
    }

    private void RecordHistory(PointingRay ray)
    {
        if (Time.time < _nextHistoryTime) return;
        _nextHistoryTime = Time.time + historyInterval;

        _history[_historyHead] = new TimedRay { Time = Time.time, Ray = ray };
        _historyHead = (_historyHead + 1) % HistoryCapacity;
        if (_historyCount < HistoryCapacity) _historyCount++;
    }

    void Update()
    {
        _hasCurrent = false;
        if (rayOrigin == null || !rayOrigin.gameObject.activeInHierarchy) return;

        _current = Cast(rayOrigin.position, rayOrigin.forward, _ignoreRoot);
        _hasCurrent = true;
        RecordHistory(_current);
        PointingChanged?.Invoke(new PointingSample(_current.HitObject, _current.HitPoint));
    }

    /// <summary>Casts with the live pointer's rules but an explicit ignore root instead of the selection.</summary>
    // Used to re-aim a recorded ray past the object being moved, or to pick the selection itself.
    public PointingRay Recast(Vector3 origin, Vector3 direction, Transform ignore) => Cast(origin, direction, ignore);

    private PointingRay Cast(Vector3 origin, Vector3 direction, Transform ignore)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, maxDistance, EffectiveMask);
        int closestIndex = -1;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = _hitBuffer[i];
            if (ignore != null && candidate.collider.transform.IsChildOf(ignore)) continue;
            if (IsPlayerCollider(candidate.collider)) continue;
            if (candidate.distance < closestDistance)
            {
                closestDistance = candidate.distance;
                closestIndex = i;
            }
        }

        if (closestIndex < 0)
            return new PointingRay(origin, direction, null, origin + direction * freeSpaceDistance, 0f);

        RaycastHit hit = _hitBuffer[closestIndex];
        return new PointingRay(origin, direction, hit.collider.gameObject, hit.point, hit.distance);
    }
}

/// <summary>A pointing ray and its result. Unlike PointingSample it keeps origin and direction.</summary>
public readonly struct PointingRay
{
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;
    /// <summary>Null when nothing was hit within range.</summary>
    public readonly GameObject HitObject;
    /// <summary>Hit point, or the projected free-space point on a miss.</summary>
    public readonly Vector3 HitPoint;
    public readonly float HitDistance;

    public PointingRay(Vector3 origin, Vector3 direction, GameObject hitObject, Vector3 hitPoint, float hitDistance)
    {
        Origin = origin;
        Direction = direction;
        HitObject = hitObject;
        HitPoint = hitPoint;
        HitDistance = hitDistance;
    }
}
