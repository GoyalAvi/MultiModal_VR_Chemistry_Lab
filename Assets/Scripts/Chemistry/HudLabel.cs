using TMPro;
using UnityEngine;

/// <summary>Floating HUD label in the object-label style (hand-distance readout, filter badge).</summary>
// The caller positions Root; Face() turns it toward the viewer.
public class HudLabel
{
    private static readonly Color TextColor  = new Color(0.95f, 0.96f, 0.98f, 1f);   // same palette as ContainerLabel
    private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.80f);
    public static readonly Color AccentColor = new Color(0.31f, 0.76f, 0.97f, 1f);
    private const float WorldScale = 0.01f;

    public Transform Root { get; }
    private readonly TextMeshPro _text;
    private readonly SpriteRenderer _panel, _border;

    public HudLabel(string name, Transform parent, Color borderColor)
    {
        var root = new GameObject(name);
        Root = root.transform;
        if (parent != null) Root.SetParent(parent, false);
        Root.localScale = Vector3.one * WorldScale;

        _border = CreatePanel("Border", 4000, borderColor);
        _panel  = CreatePanel("Panel", 4001, PanelColor);

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(Root, false);
        _text = textObj.AddComponent<TextMeshPro>();
        _text.fontSize = 26f;
        _text.color = TextColor;
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.rectTransform.sizeDelta = new Vector2(60f, 10f);
        Material mat = _text.fontMaterial;
        if (mat != null) mat.renderQueue = 4002;
    }

    public TextMeshPro Text => _text;

    public void SetText(string text)
    {
        _text.SetText(text);
        Resize();
    }

    /// <summary>Re-fits the panel after Text.SetText(format, value), which avoids allocations.</summary>
    public void Resize()
    {
        _text.ForceMeshUpdate();
        Vector2 size = _text.GetRenderedValues(false) + new Vector2(2.2f, 1.4f);
        _panel.size = size;
        _border.size = size + Vector2.one * 0.24f;
    }

    // TextMeshPro reads correctly when its +Z points away from the viewer
    public void Face(Vector3 viewerPosition)
    {
        Vector3 away = Root.position - viewerPosition;
        if (away.sqrMagnitude > 1e-6f) Root.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    private SpriteRenderer CreatePanel(string objName, int renderQueue, Color color)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(Root, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.01f * (4002 - renderQueue)); // slightly behind the text
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ContainerLabel.RoundedSprite.Get();
        sr.drawMode = SpriteDrawMode.Sliced;
        Material m = ContainerLabel.RoundedSprite.GetMaterial(renderQueue);
        if (m != null) sr.sharedMaterial = m;
        sr.color = color;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }
}
