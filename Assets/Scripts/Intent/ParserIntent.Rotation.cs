// Yaw-only rotation: animated voice rotation and twist mode (controller roll while the trigger is held).

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using MMI.Semantics;

public partial class ParserIntent
{
    // Everything turns around world up through the bounds centre, so objects stay upright and spin in
    // place instead of orbiting their pivot

    private struct ActiveRotation
    {
        public Coroutine Routine;
        public Vector3 EndPosition;
        public Quaternion EndRotation;
    }

    private readonly Dictionary<GameObject, ActiveRotation> _activeRotations = new Dictionary<GameObject, ActiveRotation>();

    private static void YawAround(Vector3 pivot, Vector3 position, Quaternion rotation, float degrees,
                                  out Vector3 newPosition, out Quaternion newRotation)
    {
        Quaternion yaw = Quaternion.AngleAxis(degrees, Vector3.up);
        newPosition = pivot + yaw * (position - pivot);
        newRotation = yaw * rotation;
    }

    private void StartRotate(GameObject go, float degrees)
    {
        FinishActiveMove(go); // also snaps an unfinished rotation to its end
        Physics.SyncTransforms();
        Vector3 pivot = GetPlacementBounds(go).center;
        Vector3 startPos = go.transform.position;
        Quaternion startRot = go.transform.rotation;
        YawAround(pivot, startPos, startRot, degrees, out Vector3 endPos, out Quaternion endRot);

        _activeRotations[go] = new ActiveRotation
        {
            Routine = StartCoroutine(RotateRoutine(go, pivot, startPos, startRot, degrees, endPos, endRot)),
            EndPosition = endPos,
            EndRotation = endRot,
        };
        Debug.Log($"[ParserIntent] rotate (voice) {go.name}: {degrees:+0;-0}° | yaw {startRot.eulerAngles.y:F0}° → {endRot.eulerAngles.y:F0}°");
    }

    private IEnumerator RotateRoutine(GameObject go, Vector3 pivot, Vector3 startPos, Quaternion startRot, float degrees,
                                      Vector3 endPos, Quaternion endRot)
    {
        float t = 0f;
        while (go != null && t < 1f)
        {
            t = rotateDuration > 0f ? Mathf.Min(1f, t + Time.deltaTime / rotateDuration) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, t);
            YawAround(pivot, startPos, startRot, degrees * eased, out Vector3 p, out Quaternion r);
            go.transform.SetPositionAndRotation(p, r);
            yield return null;
        }
        if (go == null) yield break;
        go.transform.SetPositionAndRotation(endPos, endRot); // end exactly
        _activeRotations.Remove(go);
        SoundFeedback.PlayAt(SoundFeedback.Sound.Rotate, go.transform.position);
    }

    private void FinishActiveRotation(GameObject go)
    {
        if (go == null || !_activeRotations.TryGetValue(go, out ActiveRotation rot)) return;
        if (rot.Routine != null) StopCoroutine(rot.Routine);
        go.transform.SetPositionAndRotation(rot.EndPosition, rot.EndRotation);
        _activeRotations.Remove(go);
    }

    private GameObject _twistTarget;

    private bool       _twistHolding;

    private float      _twistModeStartTime;

    private Quaternion _twistControllerStart;

    private Vector3    _twistPivot, _twistStartPos;

    private Quaternion _twistStartRot;

    private GameObject _twistRing;

    private InputAction _codeTwistTrigger;

    private BimanualScaler _bimanualScaler;

    private bool _bimanualScalerLooked;

    /// <summary>True while twist mode is active; BimanualScaler stays out meanwhile.</summary>
    public bool IsTwisting => _twistTarget != null;

    private bool TwistTriggerHeld()
    {
        InputAction action = twistTriggerAction != null && twistTriggerAction.action != null ? twistTriggerAction.action : _codeTwistTrigger;
        return action != null && action.IsPressed();
    }

    private bool BimanualScaling()
    {
        if (!_bimanualScalerLooked)
        {
            _bimanualScaler = FindFirstObjectByType<BimanualScaler>();
            _bimanualScalerLooked = true;
        }
        return _bimanualScaler != null && _bimanualScaler.IsScaling;
    }

    private void StartTwist(GameObject go)
    {
        if (pointingSource == null || pointingSource.RayOrigin == null)
        {
            Refuse("[ParserIntent] rotate: twist mode needs the pointing controller (RobustPointingSource.rayOrigin).");
            return;
        }
        FinishActiveMove(go);
        _twistTarget = go;
        _twistHolding = false;
        _twistModeStartTime = Time.time;
        ShowTwistRing(true);
        Debug.Log($"[ParserIntent] rotate (twist) {go.name}: hold the trigger and twist the controller.");
    }

    private void EndTwist(string reason)
    {
        if (_twistTarget == null) return;
        if (_twistHolding)
        {
            Debug.Log($"[ParserIntent] rotate (twist) {_twistTarget.name}: yaw {_twistStartRot.eulerAngles.y:F0}° → {_twistTarget.transform.eulerAngles.y:F0}° ({reason})");
            SoundFeedback.PlayAt(SoundFeedback.Sound.Rotate, _twistTarget.transform.position);
        }
        else
            Debug.Log($"[ParserIntent] rotate (twist) ended: {reason}");
        _twistTarget = null;
        _twistHolding = false;
        ShowTwistRing(false);
    }

    private void UpdateTwist()
    {
        if (_twistTarget == null) return;
        Transform controller = pointingSource != null ? pointingSource.RayOrigin : null;
        if (controller == null) { EndTwist("controller lost"); return; }

        bool held = TwistTriggerHeld();
        if (!_twistHolding)
        {
            if (Time.time - _twistModeStartTime > twistTimeout) { EndTwist("timeout"); return; }
            if (!held || BimanualScaling()) { UpdateTwistRing(); return; }

            // Trigger pressed: remember the reference poses
            Physics.SyncTransforms();
            _twistHolding = true;
            _twistControllerStart = controller.rotation;
            _twistPivot = GetPlacementBounds(_twistTarget).center;
            _twistStartPos = _twistTarget.transform.position;
            _twistStartRot = _twistTarget.transform.rotation;
        }
        else if (!held)
        {
            EndTwist("trigger released"); // object stays where it is
            return;
        }

        // Roll = twist about the controller's forward axis relative to the start (swing-twist decomposition)
        Quaternion delta = Quaternion.Inverse(_twistControllerStart) * controller.rotation;
        float roll = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(delta.z, delta.w) * Mathf.Rad2Deg);
        float effective = Mathf.Abs(roll) <= twistDeadzone ? 0f : roll - Mathf.Sign(roll) * twistDeadzone;
        float yaw = -effective * twistGain * (invertTwist ? -1f : 1f);

        YawAround(_twistPivot, _twistStartPos, _twistStartRot, yaw, out Vector3 p, out Quaternion r);
        _twistTarget.transform.SetPositionAndRotation(p, r);
        UpdateTwistRing();
    }

    // Thin ring under the object while twist mode is active; one reused object
    private void ShowTwistRing(bool show)
    {
        if (show && _twistRing == null)
        {
            _twistRing = CreatePreviewDisc(-1);
            _twistRing.name = "TwistModeRing";
        }
        if (_twistRing != null) _twistRing.SetActive(show);
        if (show) UpdateTwistRing();
    }

    private void UpdateTwistRing()
    {
        if (_twistRing == null || _twistTarget == null) return;
        Bounds b = GetPlacementBounds(_twistTarget);
        float diameter = HorizontalRadius(b) * 2.6f;
        _twistRing.transform.position = new Vector3(b.center.x, b.min.y + 0.003f, b.center.z);
        _twistRing.transform.localScale = new Vector3(diameter, 0.002f, diameter);
    }
}
