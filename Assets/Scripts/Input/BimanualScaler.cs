using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Two-hand scaling: hold the button on both controllers and move the hands to scale the selection.</summary>
// Releasing either button keeps the size it landed on. No speech involved.
public class BimanualScaler : MonoBehaviour
{
    [Header("Hands")]
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;

    [Header("Buttons")]
    [Tooltip("Left-controller button; hold with the right one to scale.")]
    [SerializeField] private InputActionReference leftScaleButton;
    [Tooltip("Right-controller button; hold with the left one to scale.")]
    [SerializeField] private InputActionReference rightScaleButton;

    [Header("Scale Limits")]
    [SerializeField] private float minScale = 0.2f;
    [SerializeField] private float maxScale = 3.0f;

    [Header("Selection")]
    [Tooltip("Scaling only applies to the current selection.")]
    [SerializeField] private ParserIntent parserIntent;

    private bool _scaling;
    private bool _blockedUntilRelease;

    public Transform LeftHand => leftHand;
    public Transform RightHand => rightHand;

    /// <summary>True while scaling; twist rotation won't start meanwhile.</summary>
    public bool IsScaling => _scaling;
    private Transform _target;
    private float _grabDistance;
    private float _baseScale;

    void Update()
    {
        if (leftHand == null || rightHand == null || parserIntent == null) return;

        bool held = IsHeld(leftScaleButton) && IsHeld(rightScaleButton);
        if (!held) _blockedUntilRelease = false;

        // During "make it this big" the hands show a size for the voice command, so stay out until the
        // buttons are released
        if (held && HandDistanceSource.SizeCommandInProgress) _blockedUntilRelease = true;
        if (_blockedUntilRelease)
        {
            _scaling = false;
            _target = null;
            return;
        }

        // Twist rotation owns the object while it's active
        if (!held || parserIntent.IsTwisting)
        {
            _scaling = false;
            _target = null;
            return;
        }

        if (!_scaling)
        {
            GameObject selected = parserIntent.SelectedObject;
            if (selected == null) return;

            _grabDistance = Vector3.Distance(leftHand.position, rightHand.position);
            if (_grabDistance < 0.001f) return; // avoid a divide-by-zero ratio

            _target = selected.transform;
            _baseScale = _target.localScale.x; // start from the current size, not a fixed one
            _scaling = true;
            return; // this frame only grabs the baseline
        }

        if (_target == null) { _scaling = false; return; }

        float currentDistance = Vector3.Distance(leftHand.position, rightHand.position);
        float ratio = currentDistance / _grabDistance;
        float newScale = Mathf.Clamp(_baseScale * ratio, minScale, maxScale);
        _target.localScale = Vector3.one * newScale;
    }

    private static bool IsHeld(InputActionReference actionRef) =>
        actionRef != null && actionRef.action != null && actionRef.action.IsPressed();
}
