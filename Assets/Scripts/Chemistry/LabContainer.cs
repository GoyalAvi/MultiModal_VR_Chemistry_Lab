using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Contents of a test tube, beaker or flask: liquid colour, label and reaction effects.</summary>
public class LabContainer : MonoBehaviour
{
    [Header("Identity")]
    public string containerType = "testtube";   // testtube | beaker | flask

    [Header("Liquid Visual")]
    [Tooltip("The child GameObject that represents the liquid inside (a scaled inner mesh).")]
    [SerializeField] private Renderer liquidRenderer;
    [Tooltip("Fill level 0–1. Purely visual for now.")]
    [Range(0f, 1f)]
    public float fillLevel = 0f;

    [Header("Label")]
    [Tooltip("Floating label with the live contents, so containers aren't identified by colour alone.")]
    [SerializeField] private bool showLabel = true;
    [Tooltip("Per-prefab tweak of where the label floats, relative to the container's top centre.")]
    [SerializeField] private Vector3 labelOffset = new Vector3(0f, 0.15f, 0f);

    [Header("Reaction Effects")]
    [Tooltip("Shown while a precipitate-forming pair is present. A GameObject, not particles: the beaker's collider blocked emission.")]
    [SerializeField] private GameObject precipitateVisual;
    [Tooltip("Plays while a fuming pair has been heated long enough, or while a self-heating pair boils off.")]
    [SerializeField] private ParticleSystem fumeEffect;
    [Tooltip("Seconds of continuous heat on the burner before a fuming reaction triggers.")]
    [SerializeField] private float fumeHeatDuration = 8f;
    [Tooltip("How close to the burner's stand counts as 'on the burner' for heating.")]
    [SerializeField] private float heatRadius = 0.3f;
    [Tooltip("Seconds for a self-heating pair (CaO + H2O) to boil the liquid away.")]
    [SerializeField] private float exothermicBoilDuration = 5f;

    private List<string> _contentIds = new List<string>(); // chemical ids inside
    private Color        _currentColor = Color.clear;
    private bool         _isEmpty => _contentIds.Count == 0;
    private ContainerLabel _label;
    private BunsenBurner  _burner;
    private float         _heatTimer = 0f;
    private bool          _isFuming = false;
    private bool          _hasPrecipitated = false;
    private bool          _hasEvaporated = false;
    private bool          _wasOnStand = false;

    // Events for observers (tutorial, sound). Raised after the container has done its work, and every
    // listener call is wrapped, so a failing listener can't change what the container does.

    /// <summary>A chemical id was added (not raised for a skipped duplicate).</summary>
    public static event System.Action<LabContainer, string> ChemicalAdded;
    /// <summary>A mix succeeded (destination, source); the source is already empty.</summary>
    public static event System.Action<LabContainer, LabContainer> Mixed;
    public static event System.Action<LabContainer> PrecipitateFormed;
    /// <summary>A container came within heatRadius of the burner stand.</summary>
    public static event System.Action<LabContainer> ReachedBurnerStand;
    public static event System.Action<LabContainer> FumesStarted;
    /// <summary>A fuming pair finished fuming and the container was emptied.</summary>
    public static event System.Action<LabContainer> FumesFinished;
    /// <summary>A self-heating pair finished boiling its water away.</summary>
    public static event System.Action<LabContainer> EvaporationFinished;

    private static void Notify<T>(System.Action<T> handler, T arg)
    {
        if (handler == null) return;
        try { handler(arg); } catch (System.Exception ex) { Debug.LogException(ex); }
    }

    private static void Notify<T1, T2>(System.Action<T1, T2> handler, T1 a, T2 b)
    {
        if (handler == null) return;
        try { handler(a, b); } catch (System.Exception ex) { Debug.LogException(ex); }
    }

    public bool   IsEmpty    => _isEmpty;
    /// <summary>Liquid colour (clear when empty), shown as a dot on the label.</summary>
    public Color  LiquidColor => _currentColor;

    /// <summary>Within heatRadius of the burner stand, whether the burner is on or not.</summary>
    public bool IsOnBurnerStand => _burner != null && _burner.Stand != null &&
        Vector3.Distance(transform.position, _burner.Stand.position) <= heatRadius;
    public string ContentsLog => _contentIds.Count == 0
        ? "empty"
        : string.Join(" + ", _contentIds);

    /// <summary>Label text: the reactants ("HCl + NaOH"), or the product formula once a registered pair is present.</summary>
    public string ContentsDisplay
    {
        get
        {
            if (_contentIds.Count == 0) return "empty";

            string product = ChemicalDatabase.GetProductLabel(_contentIds);
            if (product != null) return product;

            List<string> names = new List<string>();
            foreach (string id in _contentIds)
            {
                ChemicalDatabase.Chemical c = ChemicalDatabase.GetChemical(id);
                names.Add(c != null ? c.displayName : id);
            }
            return string.Join(" + ", names);
        }
    }

    void Awake()
    {
        if (!showLabel) return;
        _label = GetComponent<ContainerLabel>();
        if (_label == null) _label = gameObject.AddComponent<ContainerLabel>();
        _label.SetOffset(labelOffset);
        RefreshLabel();
    }

    void Start()
    {
        _burner = FindFirstObjectByType<BunsenBurner>();
    }

    // Heating is distance-based instead of a trigger collider, so no scene or prefab changes are needed
    void Update()
    {
        if (_burner == null || _burner.Stand == null) return;

        bool onStand = IsOnBurnerStand;
        if (onStand && !_wasOnStand) Notify(ReachedBurnerStand, this);
        _wasOnStand = onStand;

        bool isHeating = _burner.IsOn && onStand;

        if (!isHeating)
        {
            _heatTimer = 0f;
            return;
        }

        _heatTimer += Time.deltaTime;
        if (!_isFuming && _heatTimer >= fumeHeatDuration &&
            ChemicalDatabase.HasEffect(_contentIds, ChemicalDatabase.ReactionEffect.Fumes))
        {
            StartCoroutine(FumeAndClear());
        }
    }

    /// <summary>Adds a chemical (duplicates are skipped) and updates colour, label and effects.</summary>
    public void AddChemical(ChemicalDatabase.Chemical chemical)
    {
        if (chemical == null) return;

        if (_contentIds.Contains(chemical.id))
        {
            Debug.Log($"[LabContainer] {name} already contains {chemical.displayName} — skipping duplicate.");
            return;
        }

        _contentIds.Add(chemical.id);
        fillLevel = Mathf.Clamp01(fillLevel + 0.3f);
        RefreshColor();
        RefreshLabel();
        CheckPrecipitate();
        CheckExothermic();

        Debug.Log($"[LabContainer] Added {chemical.displayName} to {name}. Contents: {ContentsLog}");
        Notify(ChemicalAdded, this, chemical.id);
    }

    /// <summary>Pours all of source into this container; source ends up empty.</summary>
    public void MixFrom(LabContainer source)
    {
        if (source == null || source.IsEmpty)
        {
            Debug.LogWarning("[LabContainer] Mix failed — source is null or empty.");
            return;
        }
        if (source == this)
        {
            Debug.LogWarning("[LabContainer] Can't mix a container into itself.");
            return;
        }

        Debug.Log($"[LabContainer] Mixing {source.ContentsLog} into {name} ({ContentsLog})");

        foreach (string id in source._contentIds)
            if (!_contentIds.Contains(id))
                _contentIds.Add(id);

        fillLevel = Mathf.Clamp01(fillLevel + source.fillLevel);
        RefreshColor();
        RefreshLabel();
        CheckPrecipitate();
        CheckExothermic();

        source.ClearContents();

        Debug.Log($"[LabContainer] Result in {name}: {ContentsLog}");
        Notify(Mixed, this, source);
    }

    /// <summary>Empties the container.</summary>
    public void ClearContents()
    {
        _contentIds.Clear();
        fillLevel = 0f;
        _currentColor = Color.clear;
        _hasPrecipitated = false;
        _hasEvaporated = false;
        if (precipitateVisual != null) precipitateVisual.SetActive(false);
        ApplyColorToRenderer(Color.clear);
        RefreshLabel();
        Debug.Log($"[LabContainer] {name} is now empty.");
    }

    private void RefreshLabel()
    {
        if (_label == null) return;
        string title = string.IsNullOrEmpty(containerType)
            ? name
            : char.ToUpper(containerType[0]) + containerType.Substring(1);
        _label.SetLabel($"{title}\n{ContentsDisplay}");
    }

    // Colour of the average pH of everything inside
    private void RefreshColor()
    {
        if (_contentIds.Count == 0)
        {
            _currentColor = Color.clear;
        }
        else
        {
            float totalPH = 0f;
            foreach (string id in _contentIds)
            {
                ChemicalDatabase.Chemical c = ChemicalDatabase.GetChemical(id);
                totalPH += c != null ? c.phValue : 7f;
            }
            _currentColor = ChemicalDatabase.ColorForPH(totalPH / _contentIds.Count);

            // A precipitate clouds the liquid, so desaturate toward white
            if (ChemicalDatabase.HasEffect(_contentIds, ChemicalDatabase.ReactionEffect.Precipitate))
                _currentColor = Color.Lerp(_currentColor, Color.white, 0.6f);
        }

        ApplyColorToRenderer(_currentColor);
    }

    private void CheckPrecipitate()
    {
        if (_hasPrecipitated) return;
        if (!ChemicalDatabase.HasEffect(_contentIds, ChemicalDatabase.ReactionEffect.Precipitate)) return;

        _hasPrecipitated = true;
        if (precipitateVisual != null) precipitateVisual.SetActive(true);
        Debug.Log($"[LabContainer] {name} — precipitate formed ({ContentsLog}).");
        Notify(PrecipitateFormed, this);
    }

    // Self-heating pair (CaO + H2O): boil the liquid away right away, no burner needed
    private void CheckExothermic()
    {
        if (_hasEvaporated) return;
        if (!ChemicalDatabase.HasEffect(_contentIds, ChemicalDatabase.ReactionEffect.Exothermic)) return;

        _hasEvaporated = true;
        StartCoroutine(EvaporateDry());
    }

    // Unlike FumeAndClear the container isn't emptied: the solid product stays behind as a dry residue,
    // like in the real reaction
    private IEnumerator EvaporateDry()
    {
        Debug.Log($"[LabContainer] {name} — exothermic reaction, self-heating ({ContentsLog}).");
        if (_label != null) _label.SetLabel("Reacting…\nCaO + H₂O → Ca(OH)₂ + heat");
        if (fumeEffect != null) fumeEffect.Play();

        float startFill = fillLevel;
        float elapsed = 0f;
        while (elapsed < exothermicBoilDuration)
        {
            elapsed += Time.deltaTime;
            fillLevel = Mathf.Lerp(startFill, 0f, elapsed / exothermicBoilDuration);
            ApplyColorToRenderer(fillLevel > 0f ? _currentColor : Color.clear);
            yield return null;
        }

        if (fumeEffect != null) fumeEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        Debug.Log($"[LabContainer] {name} — water boiled off, dry residue remains ({ContentsLog}).");
        RefreshLabel();
        Notify(EvaporationFinished, this);
    }

    // NH4Cl decomposes into gases: fumes, the liquid drains away, then the container is emptied
    private IEnumerator FumeAndClear()
    {
        _isFuming = true;
        if (fumeEffect != null) fumeEffect.Play();
        Debug.Log($"[LabContainer] {name} — fuming ({ContentsLog}).");
        Notify(FumesStarted, this);

        if (_label != null) _label.SetLabel("Reacting…\nNH₄Cl ⇌ HCl(g) + NH₃(g)");

        float duration = 8f;
        float startFill = fillLevel;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            fillLevel = Mathf.Lerp(startFill, 0f, elapsed / duration);
            ApplyColorToRenderer(fillLevel > 0f ? _currentColor : Color.clear);
            yield return null;
        }

        // Let the smoke already emitted drift off instead of cutting it
        if (fumeEffect != null) fumeEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        ClearContents();
        Notify(FumesFinished, this);

        // Hold a completion message for a moment so it reads as "finished", not as the contents vanishing
        if (_label != null)
        {
            _label.SetLabel("Reaction complete\nHCl(g) + NH₃(g) released");
            yield return new WaitForSeconds(4f);
            RefreshLabel();
        }

        _isFuming = false;
        _heatTimer = 0f;
    }

    // Manual test hooks: right-click the component header in Play mode
    [ContextMenu("Test: Add AgNO3")] private void Test_AddAgNO3() => AddChemical(ChemicalDatabase.GetChemical("agno3"));
    [ContextMenu("Test: Add NaCl")]  private void Test_AddNaCl()  => AddChemical(ChemicalDatabase.GetChemical("nacl"));
    [ContextMenu("Test: Add HCl")]   private void Test_AddHCl()   => AddChemical(ChemicalDatabase.GetChemical("hcl"));
    [ContextMenu("Test: Add NH3")]   private void Test_AddNH3()   => AddChemical(ChemicalDatabase.GetChemical("nh3"));
    [ContextMenu("Test: Add NaOH")]  private void Test_AddNaOH()  => AddChemical(ChemicalDatabase.GetChemical("naoh"));
    [ContextMenu("Test: Add CaO")]   private void Test_AddCaO()   => AddChemical(ChemicalDatabase.GetChemical("cao"));
    [ContextMenu("Test: Add Water")] private void Test_AddWater() => AddChemical(ChemicalDatabase.GetChemical("h2o"));
    [ContextMenu("Test: Clear")]     private void Test_Clear()    => ClearContents();

    private bool _blendModeFixed = false;

    private void ApplyColorToRenderer(Color c)
    {
        if (liquidRenderer == null) return;

        liquidRenderer.enabled = (c != Color.clear && fillLevel > 0f);
        if (!liquidRenderer.enabled) return;

        Material mat = liquidRenderer.material; // Unity clones the material per instance on first access

        // The shared glass material uses additive blending, which barely shows a tint. Switch this instance
        // to alpha blending once so the colour is visible, without touching the material asset.
        if (!_blendModeFixed)
        {
            if (mat.HasProperty("_Blend"))     mat.SetFloat("_Blend", 0f);        // alpha
            if (mat.HasProperty("_SrcBlend"))  mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))  mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _blendModeFixed = true;
        }

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", c);
        else if (mat.HasProperty("_Color"))
            mat.color = c;
    }
}