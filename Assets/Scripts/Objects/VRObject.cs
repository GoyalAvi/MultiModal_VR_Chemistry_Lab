using UnityEngine;
using MMI.Semantics;

/// <summary>Type and colour of a lab object as data, found through SemanticQuery (ISemanticEntity).</summary>
public class VRObject : MonoBehaviour, ISemanticEntity
{
    [Header("Object Identity")]
    public string objectType;   // "beaker", "burner", "flask", "testtube"
    public string objectColor;
    [Tooltip("True for objects created by voice; scene fixtures stay false.")]
    public bool isSpawned;

    private Renderer _renderer;

    /// <summary>Scale when the object appeared; the reference for the 0.2-3x resize limits.</summary>
    public Vector3 OriginalScale { get; private set; } = Vector3.one;

    GameObject ISemanticEntity.Owner => gameObject;

    // "type", "color" and "origin" (spawned / scene); the query does the case-insensitive matching
    bool ISemanticEntity.TryGetProperty(string key, out string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "type":  value = objectType;  return !string.IsNullOrEmpty(value);
            case "color": value = objectColor; return !string.IsNullOrEmpty(value);
            case "origin": value = isSpawned ? "spawned" : "scene"; return true;
            default:      value = null;        return false;
        }
    }

    void Awake()
    {
        _renderer = GetComponentInChildren<Renderer>();
        OriginalScale = transform.localScale;
    }

    void OnEnable()  => SemanticRegistry.Register(this);
    void OnDisable() => SemanticRegistry.Unregister(this);

    /// <summary>Sets objectColor and tints the material. Use this instead of setting objectColor directly.</summary>
    public void SetColor(string newColor, Color unityColor)
    {
        objectColor = newColor;

        if (_renderer == null) return;
        if (_renderer.material.HasProperty("_Color"))
            _renderer.material.color = unityColor;
        else if (_renderer.material.HasProperty("_BaseColor"))
            _renderer.material.SetColor("_BaseColor", unityColor);
    }
}