using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Burner flame and light, plus the stand transform containers are placed on.</summary>
public class BunsenBurner : MonoBehaviour
{
    [Header("Flame")]
    [Tooltip("Particle system representing the flame. Only plays while the burner is on.")]
    [SerializeField] private ParticleSystem flame;
    [Tooltip("Optional light that switches on with the flame for extra glow.")]
    [SerializeField] private Light flameLight;

    [Header("Placement")]
    [Tooltip("Where a container is moved to on 'move to burner' — drag the stand GameObject here.")]
    [SerializeField] private Transform stand;

    public bool IsOn { get; private set; }

    /// <summary>Raised on every on/off change, including the initial "off" in Start. Listener errors are caught.</summary>
    public static event System.Action<BunsenBurner, bool> StateChanged;
    public Transform Stand => stand;

    private Coroutine _visibleLog;

    void Awake()
    {
        // The flame must show the moment it's turned on: no start delay, and a looping flame is prewarmed
        // so it doesn't grow in from nothing
        if (flame == null) return;
        flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // module edits below need a stopped system
        ParticleSystem.MainModule main = flame.main;
        main.startDelay = 0f;
        main.playOnAwake = false;
        if (main.loop) main.prewarm = true;
    }

    void Start()
    {
        // Start cold; the flame only appears after "turn on"
        ApplyState(false, force: true);
    }

    [ContextMenu("Test: Turn On")]
    public void TurnOn()  => TurnOn(-1f);
    [ContextMenu("Test: Turn Off")]
    public void TurnOff() => TurnOff(-1f);

    /// <summary>Turns the flame on; false if it was already on (nothing restarted).</summary>
    // sentenceEndTime (Time.time, or &lt; 0 if unknown) is only used for the timing log
    public bool TurnOn(float sentenceEndTime)  => ApplyState(true, false, sentenceEndTime);
    /// <summary>Turns the flame off; false if it was already off.</summary>
    public bool TurnOff(float sentenceEndTime) => ApplyState(false, false, sentenceEndTime);

    private bool ApplyState(bool on, bool force, float sentenceEndTime = -1f)
    {
        if (!force && on == IsOn) return false;
        IsOn = on;

        if (flame != null)
        {
            if (on)
            {
                if (!flame.gameObject.activeSelf) flame.gameObject.SetActive(true);
                flame.Play(true); // no Stop/Clear first, it was cleared when turned off
            }
            else flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (flameLight != null)
            flameLight.enabled = on;

        Debug.Log($"[BunsenBurner] {name} turned {(on ? "ON" : "OFF")}.");

        if (_visibleLog != null) { StopCoroutine(_visibleLog); _visibleLog = null; }
        if (on && !force && flame != null && isActiveAndEnabled)
            _visibleLog = StartCoroutine(LogFlameVisible(sentenceEndTime, Time.realtimeSinceStartup));

        if (StateChanged != null)
        {
            try { StateChanged(this, on); } catch (System.Exception ex) { Debug.LogException(ex); }
        }
        return true;
    }

    // Logs sentence end -> handler -> first frame with flame particles, in ms
    private IEnumerator LogFlameVisible(float sentenceEndTime, float handlerRealtime)
    {
        string toHandler = sentenceEndTime >= 0f ? $"{(Time.time - sentenceEndTime) * 1000f:F0} ms" : "n/a";
        float timeout = handlerRealtime + 2f;
        while (flame != null && flame.particleCount == 0 && Time.realtimeSinceStartup < timeout)
            yield return null;
        _visibleLog = null;
        if (flame == null) yield break;
        string toVisible = flame.particleCount > 0
            ? $"{(Time.realtimeSinceStartup - handlerRealtime) * 1000f:F0} ms"
            : "not visible after 2000 ms";
        Debug.Log($"[BunsenBurner] {name} timing: sentence end → handler {toHandler}, handler → flame visible {toVisible}.");
    }

    private static readonly List<Collider> _colliders = new List<Collider>();
    private static readonly List<Renderer> _renderers = new List<Renderer>();
    private static readonly List<Bounds>   _parts     = new List<Bounds>();

    /// <summary>Bounds of the burner body only (no flame, particles, light, label or containers on it).</summary>
    // baseCentre is the centre of the foot, which is what should sit on a surface, not the pivot.
    public bool TryGetSolidBounds(out Bounds bounds, out Vector3 baseCentre)
    {
        _parts.Clear();
        GetComponentsInChildren(false, _colliders);
        foreach (Collider c in _colliders)
            if (c.enabled && !c.isTrigger && IsSolidPart(c.transform)) _parts.Add(c.bounds);
        if (_parts.Count == 0)
        {
            GetComponentsInChildren(false, _renderers);
            foreach (Renderer r in _renderers)
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && IsSolidPart(r.transform)) _parts.Add(r.bounds);
        }

        if (_parts.Count == 0)
        {
            bounds = new Bounds(transform.position, Vector3.zero);
            baseCentre = transform.position;
            return false;
        }

        bounds = _parts[0];
        for (int i = 1; i < _parts.Count; i++) bounds.Encapsulate(_parts[i]);

        // Foot = every part reaching (almost) down to the lowest point
        Bounds foot = default;
        bool hasFoot = false;
        foreach (Bounds p in _parts)
        {
            if (p.min.y > bounds.min.y + 0.01f) continue;
            if (hasFoot) foot.Encapsulate(p); else { foot = p; hasFoot = true; }
        }
        baseCentre = new Vector3(foot.center.x, bounds.min.y, foot.center.z);
        return true;
    }

    private bool IsSolidPart(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (flame != null && p == flame.transform) return false;
            if (p.GetComponent<ParticleSystem>() != null || p.GetComponent<Light>() != null
                || p.GetComponent<Canvas>() != null || p.GetComponent<TMPro.TMP_Text>() != null
                || p.GetComponent<ContainerLabel>() != null && p != transform)
                return false;
            if (p == transform) return true;
            if (p.GetComponent<VRObject>() != null) return false; // a lab object parented under the burner
        }
        return true;
    }
}
