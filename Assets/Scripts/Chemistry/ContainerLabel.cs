using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>Floating label above a container: name, contents in chemistry notation and a colour dot. Built at runtime.</summary>
// Not parented to the container, so it never inherits its rotation or scale. One LabelUpdater places
// all labels in LateUpdate (billboard, distance scale and fade, overlap lifting).
public class ContainerLabel : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Offset from the default (0, 0.15, 0). Set from LabContainer; edits here are lost (added at runtime).")]
    [SerializeField] private Vector3 offset = DefaultOffset;
    [Tooltip("Gap between the top of the container's bounds and the bottom of the label.")]
    [SerializeField] private float verticalGap = 0.03f;

    [Header("Look")]
    [SerializeField] private float fontSize = 26f;
    [Tooltip("World scale at 1 m. A small scale with a large font keeps the text crisp.")]
    [SerializeField] private float baseWorldScale = 0.01f;
    [SerializeField] private Color textColor = new Color(0.95f, 0.96f, 0.98f, 1f);
    [SerializeField] private Color mutedColor = new Color(0.62f, 0.64f, 0.68f, 1f);
    [SerializeField] private Color panelColor = new Color(0.07f, 0.08f, 0.10f, 0.80f);
    [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color accentColor = new Color(0.31f, 0.76f, 0.97f, 1f);
    [Tooltip("Panel padding around the text, in text units (x = sides, y = top/bottom).")]
    [SerializeField] private Vector2 padding = new Vector2(1.1f, 0.7f);

    [Header("Distance")]
    [Tooltip("How much the label grows with distance: scale = 1 + (distance - 1) * distanceScaling, clamped.")]
    [SerializeField] private float distanceScaling = 0.3f;
    [SerializeField] private float minScale = 0.85f;
    [SerializeField] private float maxScale = 1.6f;
    [SerializeField] private float fadeStart = 2.5f;
    [SerializeField] private float fadeEnd = 4f;

    private static readonly Vector3 DefaultOffset = new Vector3(0f, 0.15f, 0f);
    private const float BorderWidth = 0.22f; // text units

    // Not parented to the container
    private GameObject _root;
    private Transform _rootTransform;
    private TextMeshPro _text;
    private SpriteRenderer _panel, _border;

    private Renderer[] _sourceRenderers;   // cached; particles excluded
    private LabContainer _container;
    private string _lastRaw;
    private Vector2 _panelSize = new Vector2(8f, 3f);
    private bool _selected;
    private float _alpha = -1f;

    // Written by LabelUpdater each frame
    private Vector3 _basePosition;
    private Vector3 _position;
    private float _scale;
    private bool _visible;
    private int _order;

    void Awake()
    {
        _container = GetComponent<LabContainer>();
        CacheSourceRenderers();
        BuildLabelObjects();
        _order = GetInstanceID();
        LabelUpdater.Register(this);

        // A new component gets its first LateUpdate a frame late, so place and face the viewer right away
        Camera cam = LabelUpdater.ViewerCamera();
        if (cam != null)
        {
            ComputeLayout(cam.transform.position);
            _position = _basePosition;
            ApplyLayout(cam.transform.position);
        }
    }

    void OnEnable()
    {
        if (_root != null) _root.SetActive(true);
        LabelUpdater.Register(this);
    }

    void OnDisable()
    {
        if (_root != null) _root.SetActive(false);
        LabelUpdater.Unregister(this);
    }

    void OnDestroy()
    {
        LabelUpdater.Unregister(this);
        if (_root != null) Destroy(_root); // not a child, so clean up explicitly
    }

    /// <summary>First line = name (bold), the rest = contents in chemistry notation; "empty" is shown muted.</summary>
    public void SetLabel(string text)
    {
        if (_text == null || text == _lastRaw) return;
        _lastRaw = text;
        _text.text = Format(text);
        ResizePanel();
    }

    /// <summary>Placement offset, set by LabContainer from its own Inspector value.</summary>
    public void SetOffset(Vector3 newOffset) => offset = newOffset;

    private const float DimmedAlpha = 0.3f;
    private bool _dimmed;

    /// <summary>Fades the label while its object is outside the dialogue filter.</summary>
    public void SetDimmed(bool dimmed) => _dimmed = dimmed;

    private string Format(string raw)
    {
        string[] lines = raw.Split('\n');
        var sb = new StringBuilder();
        sb.Append("<b><size=112%>").Append(PrettyTitle(lines[0])).Append("</size></b>");

        string body = lines.Length > 1 ? string.Join("\n", lines, 1, lines.Length - 1).Trim() : "";
        if (body.Length > 0)
        {
            sb.Append('\n');
            if (body.Equals("empty", System.StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(mutedColor)).Append(">Empty</color>");
            }
            else
            {
                if (_container != null && !_container.IsEmpty && _container.LiquidColor.a > 0f)
                    sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_container.LiquidColor)).Append(">●</color> ");
                sb.Append(ChemistryNotation(body));
            }
        }
        return sb.ToString();
    }

    private static string PrettyTitle(string title) =>
        title.Equals("Testtube", System.StringComparison.OrdinalIgnoreCase) ? "Test tube" : title;

    // The default TMP font has no subscript digits or ⇌, so use &lt;sub&gt; tags and ↔ instead
    private static string ChemistryNotation(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        bool inSub = false;
        foreach (char c in s)
        {
            bool sub = c >= '₀' && c <= '₉';
            if (sub && !inSub) { sb.Append("<sub>"); inSub = true; }
            if (!sub && inSub) { sb.Append("</sub>"); inSub = false; }
            if (sub) sb.Append((char)('0' + (c - '₀')));
            else if (c == '⇌') sb.Append('↔');
            else sb.Append(c);
        }
        if (inSub) sb.Append("</sub>");
        return sb.ToString();
    }

    private void ResizePanel()
    {
        _text.ForceMeshUpdate();
        Vector2 textSize = _text.GetRenderedValues(false);
        if (textSize.x <= 0f || float.IsNaN(textSize.x)) textSize = new Vector2(6f, 2f);
        _panelSize = textSize + padding * 2f;
        _panel.size = _panelSize;
        _border.size = _panelSize + Vector2.one * (BorderWidth * 2f);
    }

    private void CacheSourceRenderers()
    {
        var list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (!(r is ParticleSystemRenderer)) list.Add(r); // particles would stretch the bounds
        _sourceRenderers = list.ToArray();
    }

    private void BuildLabelObjects()
    {
        _root = new GameObject($"Label ({name})");
        _rootTransform = _root.transform;
        _rootTransform.localScale = Vector3.one * baseWorldScale;

        // Draw after the transparent glass so the container's own wall never hides the label: border < panel < text
        _border = CreatePanel("Border", 4000);
        _panel  = CreatePanel("Panel", 4001);
        _border.color = borderColor;
        _panel.color = panelColor;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(_rootTransform, false);
        _text = textObj.AddComponent<TextMeshPro>();
        _text.fontSize = fontSize;
        _text.color = textColor;
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.richText = true;
        _text.rectTransform.sizeDelta = new Vector2(40f, 10f);
        Material mat = _text.fontMaterial;
        if (mat != null) mat.renderQueue = 4002;

        _text.text = "";
        ResizePanel();
    }

    private SpriteRenderer CreatePanel(string objName, int renderQueue)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(_rootTransform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.01f * (4002 - renderQueue)); // slightly behind the text
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RoundedSprite.Get();
        sr.drawMode = SpriteDrawMode.Sliced;
        Material mat = RoundedSprite.GetMaterial(renderQueue);
        if (mat != null) sr.sharedMaterial = mat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    // Base position above the bounds, distance scale, fade and selection accent
    private void ComputeLayout(Vector3 camPos)
    {
        bool has = false;
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in _sourceRenderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (has) b.Encapsulate(r.bounds); else { b = r.bounds; has = true; }
        }

        Vector3 tweak = offset - DefaultOffset;
        _basePosition = new Vector3(b.center.x, b.max.y + verticalGap, b.center.z) + tweak;

        float distance = Vector3.Distance(camPos, _basePosition);
        _scale = baseWorldScale * Mathf.Clamp(1f + (distance - 1f) * distanceScaling, minScale, maxScale);
        // The panel pivots at its centre, so lift by half its height
        _basePosition.y += _panelSize.y * 0.5f * _scale;

        float alpha = (1f - Mathf.InverseLerp(fadeStart, fadeEnd, distance)) * (_dimmed ? DimmedAlpha : 1f);
        SetAlpha(alpha);
        _visible = alpha > 0.001f;

        bool selected = LabelUpdater.SelectedObject() == gameObject;
        if (selected != _selected)
        {
            _selected = selected;
            ApplyColors();
        }
    }

    private void ApplyLayout(Vector3 camPos)
    {
        _rootTransform.position = _position;
        _rootTransform.localScale = Vector3.one * _scale;

        // Yaw-only billboard. TextMeshPro reads correctly when its +Z points away from the viewer.
        Vector3 away = _position - camPos;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-6f)
            _rootTransform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    private void SetAlpha(float alpha)
    {
        if (Mathf.Abs(alpha - _alpha) < 0.01f) return;
        _alpha = alpha;
        bool on = alpha > 0.001f;
        _text.enabled = on;
        _panel.enabled = on;
        _border.enabled = on;
        if (!on) return;
        _text.alpha = alpha;
        ApplyColors();
    }

    private void ApplyColors()
    {
        float a = Mathf.Max(_alpha, 0f);
        Color panel = panelColor; panel.a *= a;
        Color border = _selected ? accentColor : borderColor;
        border.a *= a;
        _panel.color = panel;
        _border.color = border;
    }

    // Runs in LateUpdate, after moves, rotations and scaling for the frame. Because it already exists,
    // it also covers labels created earlier in the same frame.
    private class LabelUpdater : MonoBehaviour
    {
        private static LabelUpdater _instance;
        private static readonly List<ContainerLabel> Labels = new List<ContainerLabel>();
        private static readonly System.Comparison<ContainerLabel> ByOrder = (a, b) => a._order.CompareTo(b._order);
        private static Camera _camera;
        private static ParserIntent _parserIntent;
        private static bool _parserIntentLooked;

        public static void Register(ContainerLabel label)
        {
            if (!Labels.Contains(label)) Labels.Add(label);
            if (_instance == null)
            {
                var go = new GameObject("ContainerLabel Updater");
                go.hideFlags = HideFlags.HideInHierarchy;
                _instance = go.AddComponent<LabelUpdater>();
            }
        }

        public static void Unregister(ContainerLabel label) => Labels.Remove(label);

        // Cached head camera, looked up again only if lost
        public static Camera ViewerCamera()
        {
            if (_camera != null && _camera.isActiveAndEnabled) return _camera;
            _camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            return _camera;
        }

        public static GameObject SelectedObject()
        {
            if (!_parserIntentLooked || _parserIntent == null)
            {
                _parserIntent = FindFirstObjectByType<ParserIntent>();
                _parserIntentLooked = true;
            }
            return _parserIntent != null ? _parserIntent.SelectedObject : null;
        }

        void LateUpdate()
        {
            Camera cam = ViewerCamera();
            if (cam == null || Labels.Count == 0) return;
            Vector3 camPos = cam.transform.position;
            Vector3 right = cam.transform.right; right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();

            Labels.Sort(ByOrder);
            for (int i = 0; i < Labels.Count; i++) Labels[i].ComputeLayout(camPos);

            // A label overlapping an earlier one (as seen from the viewer) is lifted above it. Recomputed from the
            // base every frame, so nothing drifts.
            for (int i = 0; i < Labels.Count; i++)
            {
                ContainerLabel li = Labels[i];
                li._position = li._basePosition;
                if (!li._visible) continue;
                Vector2 si = li._panelSize * li._scale;

                for (int pass = 0; pass < 4; pass++)
                {
                    bool moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        ContainerLabel lj = Labels[j];
                        if (!lj._visible) continue;
                        Vector2 sj = lj._panelSize * lj._scale;
                        Vector3 d = li._position - lj._position;
                        float dx = Mathf.Abs(Vector3.Dot(d, right));
                        float dy = Mathf.Abs(d.y);
                        if (dx < (si.x + sj.x) * 0.5f && dy < (si.y + sj.y) * 0.5f)
                        {
                            li._position.y = lj._position.y + (si.y + sj.y) * 0.5f + 0.005f;
                            moved = true;
                        }
                    }
                    if (!moved) break;
                }
            }

            for (int i = 0; i < Labels.Count; i++) Labels[i].ApplyLayout(camPos);
        }
    }

    // Rounded panel sprite, generated at runtime

    internal static class RoundedSprite
    {
        private static Sprite _sprite;
        private static readonly Dictionary<int, Material> Materials = new Dictionary<int, Material>();

        /// <summary>White rounded rectangle, 9-sliced so the corners keep their radius at any size.</summary>
        public static Sprite Get()
        {
            if (_sprite != null) return _sprite;
            const int size = 64, radius = 20;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "LabelRoundedRect",
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance outside the rounded rect, for an anti-aliased edge
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - dist + 0.5f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            // 40 px per unit = corner radius of 0.5 text units
            _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 40f, 0,
                                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            _sprite.name = "LabelRoundedRect";
            return _sprite;
        }

        public static Material GetMaterial(int renderQueue)
        {
            if (Materials.TryGetValue(renderQueue, out Material m) && m != null) return m;
            Shader shader = Shader.Find("Sprites/Default");
            m = shader != null ? new Material(shader) : null;
            if (m != null) m.renderQueue = renderQueue;
            Materials[renderQueue] = m;
            return m;
        }
    }
}
