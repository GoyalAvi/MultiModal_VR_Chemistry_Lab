using UnityEngine;

/// <summary>HUD badge "Only: red beakers" while a filter is active. Added at runtime by ParserIntent.</summary>
// Sits low in the view so it doesn't cover what the user points at, and follows the head on yaw only
// so it doesn't jitter.
public class FilterBadge : MonoBehaviour
{
    private const float Distance = 0.9f;     // m in front of the head
    private const float Below = 0.38f;       // m below eye height
    private const float FollowSpeed = 4f;    // higher = snappier

    private HudLabel _label;
    private GameObject _root;
    private Vector3 _position;
    private bool _placed;

    /// <summary>Shows the badge with this text, or hides it for null.</summary>
    public void Show(string filterDescription)
    {
        if (string.IsNullOrEmpty(filterDescription))
        {
            if (_root != null) _root.SetActive(false);
            _placed = false;
            return;
        }
        if (_root == null)
        {
            _root = new GameObject("FilterBadge");
            _label = new HudLabel("Label", _root.transform, HudLabel.AccentColor);
        }
        _label.SetText("Only: " + filterDescription);
        _root.SetActive(true);
    }

    void LateUpdate()
    {
        if (_root == null || !_root.activeSelf) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Transform head = cam.transform;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
        Vector3 target = head.position + forward.normalized * Distance + Vector3.down * Below;

        _position = _placed ? Vector3.Lerp(_position, target, 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime)) : target;
        _placed = true;
        _label.Root.position = _position;
        _label.Face(head.position);
    }

    void OnDestroy()
    {
        if (_root != null) Destroy(_root);
    }
}
