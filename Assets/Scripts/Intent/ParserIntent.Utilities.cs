// Selection highlight, delete, change colour, highlight, plus small helpers for logs, colours and scaling.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;
using MMI.Semantics;

public partial class ParserIntent
{
    // "red test tubes", "beaker", "objects" for log lines
    private static string DescribeObjects(string color, string obj, int count)
    {
        string noun = string.IsNullOrEmpty(obj) ? "object" : (obj.ToLower() == "testtube" ? "test tube" : obj);
        if (count != 1) noun += "s";
        return string.IsNullOrEmpty(color) ? noun : $"{color} {noun}";
    }

    // Persistent selection with a tinted material that stays until something else is selected
    void SelectObject(GameObject target, string color, string obj, Vector3? pointedHit = null)
    {
        if (target == null) return;
        if (!GuardTarget(target, "select")) return; // previous selection stays as it is

        // Selecting the same object again must not take the tinted material as the "original" or leak the old copy
        bool reselectingSame = (_selectedObject == target);

        if (_selectedObject != null && !reselectingSame)
            DeselectCurrent();

        _selectedObject = target;
        if (pointingSource != null) pointingSource.SetIgnoreObject(target);

        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            // Capture the real original only once; when reselecting, rend.material is already our copy
            if (!reselectingSame || _originalSelectedMaterial == null)
                _originalSelectedMaterial = rend.material;

            DestroySelectionMaterialInstance();

            // Runtime copy so the shared material asset isn't modified
            Material selMat = new Material(_originalSelectedMaterial);

            // Shaders name their colours differently (_EmissionColor in URP, _EMISSION_COLOR / _BASE_COLOR in the
            // burner's Arnold shader graph). Reading .color on a shader without _Color logs an error, so never do that.
            string emission = FirstColorProperty(selMat, "_EmissionColor", "_EMISSION_COLOR");
            string albedo   = FirstColorProperty(selMat, "_BaseColor", "_BASE_COLOR", "_Color");
            if (emission != null)
            {
                selMat.EnableKeyword("_EMISSION");
                selMat.SetColor(emission, new Color(0f, 0.4f, 0.6f) * 0.8f);
            }
            else if (albedo != null)
            {
                // No emission: tint the albedo slightly blue-white
                selMat.SetColor(albedo, Color.Lerp(selMat.GetColor(albedo), Color.cyan, 0.35f));
            }
            else Debug.Log($"[ParserIntent] select: {target.name}'s shader has no known colour property — selected without a tint.");

            rend.material = selMat;
            _currentSelectionMaterialInstance = selMat;
        }

        Debug.Log($"[ParserIntent] Selected and holding: {target.name}");
        SoundFeedback.PlayAt(SoundFeedback.Sound.Select, target.transform.position);

        // Selected by pointing: hop a little toward the viewer as acknowledgement
        if (pointedHit.HasValue && Camera.main != null)
        {
            Vector3 towardViewer = (Camera.main.transform.position - target.transform.position).normalized;
            // A burner only hops horizontally and takes the containers on its stand along
            bool isBurner = target.GetComponent<BunsenBurner>() != null;
            if (isBurner) towardViewer = Vector3.ProjectOnPlane(towardViewer, Vector3.up).normalized;
            Vector3 offset = towardViewer * selectNudgeDistance;
            FinishActiveMove(target);
            if (isBurner) MoveRiders(CollectBurnerRiders(target), offset, selectNudgeSpeed);
            StartMove(target, target.transform.position + offset, selectNudgeSpeed, null);
        }
    }

    private static string FirstColorProperty(Material m, params string[] names)
    {
        foreach (string n in names)
            if (m.HasColor(n)) return n;
        return null;
    }

    void DeselectCurrent()
    {
        if (_selectedObject == null) return;
        Renderer rend = _selectedObject.GetComponentInChildren<Renderer>();
        if (rend != null && _originalSelectedMaterial != null)
            rend.material = _originalSelectedMaterial;

        DestroySelectionMaterialInstance();
        _originalSelectedMaterial = null;
        _selectedObject = null;
        if (pointingSource != null) pointingSource.SetIgnoreObject(null);
    }

    void DestroySelectionMaterialInstance()
    {
        if (_currentSelectionMaterialInstance == null) return;
        Destroy(_currentSelectionMaterialInstance);
        _currentSelectionMaterialInstance = null;
    }

    // Swaps in highlightMaterial for a moment, then restores the original
    void HighlightObject(GameObject target, string color, string obj)
    {
        if (target == null) return;
        StartCoroutine(FlashHighlight(target, highlightDuration));
    }

    // Set by "delete all" so it plays one sound in total
    private bool _suppressDeleteSound;

    /// <summary>Deletes objects without sound or feedback (self-check clean-up).</summary>
    public void DeleteQuietly(IEnumerable<GameObject> objects)
    {
        _suppressDeleteSound = true;
        try { foreach (GameObject go in objects) if (go != null) DeleteObject(go); }
        finally { _suppressDeleteSound = false; }
    }

    void DeleteObject(GameObject target)
    {
        if (target == null) return;
        if (!GuardTarget(target, "delete")) return;
        if (target == _twistTarget) EndTwist("object deleted");
        FinishActiveRotation(target);
        Debug.Log($"[ParserIntent] Deleting {target.name}");

        // Otherwise _selectedObject points at a destroyed object and later selection fallbacks fail silently
        if (target == _selectedObject)
        {
            DestroySelectionMaterialInstance();
            _originalSelectedMaterial = null;
            _selectedObject = null;
            if (pointingSource != null) pointingSource.SetIgnoreObject(null);
        }

        // Unregister now rather than in OnDisable at the end of the frame, so no query this frame returns an
        // object that is about to be destroyed. ContainerLabel.OnDestroy removes the label.
        VRObject vro = target.GetComponent<VRObject>();
        if (vro != null) SemanticRegistry.Unregister(vro);

        if (_activeMoves.TryGetValue(target, out ActiveMove move) && move.Routine != null) StopCoroutine(move.Routine);
        _activeMoves.Remove(target);
        if (_dropRoutines.TryGetValue(target, out Coroutine drop) && drop != null) StopCoroutine(drop);
        _dropRoutines.Remove(target);

        _context.RemoveFromGroup(target);
        if (!_suppressDeleteSound) SoundFeedback.PlayAt(SoundFeedback.Sound.Delete, target.transform.position);
        Destroy(target);
    }

    // Sets the colour and the VRObject metadata, however the target was found
    void ChangeColorObject(GameObject target, string color, string obj)
    {
        if (target == null)
        {
            Refuse("[ParserIntent] ChangeColor: no target found.");
            return;
        }
        if (string.IsNullOrEmpty(color))
        {
            Refuse("[ParserIntent] ChangeColor: no color detected in command.");
            return;
        }

        if (!GuardTarget(target, "change color")) return;

        Color unityColor = NameToColor(color);
        bool isSelected  = (target == _selectedObject);

        // Restore the original material first so the base gets coloured, not the tinted copy
        if (isSelected && _originalSelectedMaterial != null)
        {
            Renderer r = target.GetComponentInChildren<Renderer>();
            if (r != null) r.material = _originalSelectedMaterial;
            DestroySelectionMaterialInstance();
            _originalSelectedMaterial = null;
        }

        VRObject vro = target.GetComponent<VRObject>();
        if (vro != null)
            vro.SetColor(color, unityColor);
        else
            ApplyColor(target, color);

        Debug.Log($"[ParserIntent] Changed color of {target.name} → {color}");

        // Re-apply the selection tint
        if (isSelected)
            SelectObject(target, color, obj);
    }

    IEnumerator FlashHighlight(GameObject go, float duration)
    {
        if (highlightMaterial == null || go == null) yield break;

        Renderer rend = go.GetComponentInChildren<Renderer>();
        if (rend == null) yield break;

        Material orig = rend.material;
        rend.material = highlightMaterial;
        yield return new WaitForSeconds(duration);
        if (rend != null) rend.material = orig;
    }

    IEnumerator LerpToScale(GameObject go, Vector3 target, float speed)
    {
        if (go == null) yield break;
        while (go != null && (go.transform.localScale - target).sqrMagnitude > 0.0001f)
        {
            go.transform.localScale = Vector3.MoveTowards(
                go.transform.localScale, target, speed * Time.deltaTime);
            yield return null;
        }
        if (go != null) go.transform.localScale = target;
    }

    // For objects without a VRObject
    private void ApplyColor(GameObject go, string colorName)
    {
        Renderer rend = go.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        Color c = NameToColor(colorName);
        // Standard shaders use _Color, URP uses _BaseColor
        if (rend.material.HasProperty("_Color"))
            rend.material.color = c;
        else if (rend.material.HasProperty("_BaseColor"))
            rend.material.SetColor("_BaseColor", c);
    }

    private Color NameToColor(string name)
    {
        switch (name.ToLower())
        {
            case "red":    return Color.red;
            case "blue":   return Color.blue;
            case "green":  return Color.green;
            case "yellow": return Color.yellow;
            case "black":  return Color.black;
            case "white":  return Color.white;
            case "orange": return new Color(1f, 0.5f, 0f);
            case "violet": return new Color(0.56f, 0.0f, 1.0f);
            default:       return Color.white;
        }
    }
}
