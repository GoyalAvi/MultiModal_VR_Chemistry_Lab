// Which object a command acts on: the lab-object check, pointed targets with near-miss help,
// property queries and the pick among several matches.

using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;
using MMI.Semantics;

public partial class ParserIntent
{
    /// <summary>The lab object (beaker, flask, test tube, burner) go belongs to, or null.</summary>
    // Walks up the parents, so a child collider or label counts. Walls, table and floor return null and
    // stay usable as destinations only.
    public static VRObject IsSelectable(GameObject go)
    {
        if (go == null) return null;
        VRObject vro = go.GetComponentInParent<VRObject>();
        if (vro == null || string.IsNullOrEmpty(vro.objectType)) return null;
        return InteractionSetup.ObjectTypeAliases.ContainsKey(vro.objectType) ? vro : null;
    }

    // Repeated in every handler that changes an object: refuses anything that isn't a lab object root
    private bool GuardTarget(GameObject go, string action)
    {
        VRObject vro = IsSelectable(go);
        if (vro != null && vro.gameObject == go) return true;
        Refuse($"[ParserIntent] {action}: refused — {go.name} is not a lab object.");
        return false;
    }

    // Near-miss help: the lab object with the smallest angle to the ray, within selectAngleTolerance and
    // maxPointDistance
    private VRObject NearestSelectableToRay(Vector3 origin, Vector3 direction, out float bestAngle, bool applyFilter = true)
    {
        VRObject best = null;
        bestAngle = float.MaxValue;
        foreach (ISemanticEntity entity in SemanticRegistry.All)
        {
            if (!(entity is VRObject vro) || vro == null || IsSelectable(vro.gameObject) != vro) continue;
            if (applyFilter && !_context.Matches(vro)) continue; // only objects inside the filter are candidates
            Vector3 toCenter = GetPlacementBounds(vro.gameObject).center - origin;
            if (toCenter.magnitude > maxPointDistance) continue;
            float angle = Vector3.Angle(direction, toCenter);
            if (angle <= selectAngleTolerance && angle < bestAngle)
            {
                bestAngle = angle;
                best = vro;
            }
        }
        return best;
    }

    // The hit if it's a target, else the near-miss pick along the ray, else null (refused). The slot sample
    // arrives in the frame the handler runs, so the current ray is the one that produced it.
    private GameObject Pointed(RecognizedIntent intent, int index = 0)
    {
        if (intent.PointingTargets.Count <= index) return null;
        GameObject hit = intent.PointingTargets[index].HitObject;

        VRObject hitLabObject = IsSelectable(hit);
        if (hitLabObject != null && _context.Matches(hitLabObject)) return hitLabObject.gameObject;
        if (hitLabObject != null) Debug.Log($"[ParserIntent] {intent.Action}: skipped {hitLabObject.name} — outside \"only {_context.Describe()}\"");

        bool filteredCandidate = hitLabObject != null;
        if (TryGetTargetRay(out PointingRay ray))
        {
            VRObject near = NearestSelectableToRay(ray.Origin, ray.Direction, out float angle);
            if (near != null)
            {
                Debug.Log($"[ParserIntent] {intent.Action}: ray hit {(hit != null ? hit.name : "nothing")} — near-miss pick {near.name} ({angle:F1}°)");
                return near.gameObject;
            }
            if (_context.HasFilter && NearestSelectableToRay(ray.Origin, ray.Direction, out _, applyFilter: false) != null)
                filteredCandidate = true;
        }

        if (filteredCandidate)
        {
            LogFilteredOut(intent.Action, hitLabObject != null ? hitLabObject.gameObject : null);
            return null;
        }
        Refuse($"[ParserIntent] {intent.Action}: nothing selectable (hit: {(hit != null ? hit.name : "none")})", warning: false);
        return null;
    }

    private static Vector3? PointedHit(RecognizedIntent intent, int index = 0) =>
        intent.PointingTargets.Count > index ? intent.PointingTargets[index].HitPoint : (Vector3?)null;

    // A spoken type/colour must match the pointed object, otherwise search the scene, so pointing at the
    // wrong thing doesn't select it. Not used for change colour, where the colour is the new value.
    private GameObject ResolveTarget(GameObject pointedObject, string color, string objTag)
    {
        if (pointedObject == null)
            return FindObjectByColorAndTag(color, objTag);

        bool propertySpoken = !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(objTag);
        if (!propertySpoken)
            return pointedObject;

        VRObject vro = pointedObject.GetComponent<VRObject>();
        if (vro == null)
            return pointedObject; // no metadata to check, trust the pointing

        bool colorMatches = string.IsNullOrEmpty(color)  || vro.objectColor.ToLower() == color.ToLower();
        bool typeMatches  = string.IsNullOrEmpty(objTag) || vro.objectType.ToLower()  == objTag.ToLower();
        if (colorMatches && typeMatches)
        {
            Debug.Log($"[ParserIntent] '{color} {objTag}' → {pointedObject.name} (rule: pointed at a matching object)");
            return pointedObject;
        }

        Debug.LogWarning($"[ParserIntent] Pointed object '{pointedObject.name}' doesn't match spoken property (color={color}, type={objTag}) — falling back to scene search.");
        return FindObjectByColorAndTag(color, objTag) ?? pointedObject;
    }

    // Property search through SemanticQuery; only lab objects inside the filter qualify
    private GameObject FindObjectByColorAndTag(string color, string objTag)
    {
        if (string.IsNullOrEmpty(color) && string.IsNullOrEmpty(objTag))
            return null;

        // Prefer the selection when it matches: with several objects of one type, a bare query can't know
        // which one is meant
        if (_selectedObject != null)
        {
            VRObject selectedVro = _selectedObject.GetComponent<VRObject>();
            if (selectedVro != null && _context.Matches(selectedVro))
            {
                bool selectedColorMatches = string.IsNullOrEmpty(color)  || selectedVro.objectColor.ToLower() == color.ToLower();
                bool selectedTypeMatches  = string.IsNullOrEmpty(objTag) || selectedVro.objectType.ToLower()  == objTag.ToLower();
                if (selectedColorMatches && selectedTypeMatches)
                {
                    Debug.Log($"[ParserIntent] '{color} {objTag}' → {_selectedObject.name} (rule: already selected and matches)");
                    return _selectedObject;
                }
            }
        }

        _queryMatches.Clear();
        int filteredOut = 0;
        foreach (ISemanticEntity entity in new SemanticQuery().Where("type", objTag).Where("color", color).Resolve())
        {
            if (entity.Owner == null || IsSelectable(entity.Owner) == null) continue;
            if (IsTarget(entity.Owner) != null) _queryMatches.Add(entity.Owner);
            else filteredOut++;
        }
        if (_queryMatches.Count > 0)
            return PickAmongMatches(_queryMatches, $"{color} {objTag}");
        if (filteredOut > 0)
        {
            LogFilteredOut($"find {DescribeObjects(color, objTag, 1)}", null);
            return null;
        }

        Refuse($"[ParserIntent] Could not find '{color} {objTag}' in scene.");
        return null;
    }

    private readonly List<GameObject> _queryMatches = new List<GameObject>();

    // Registry order is arbitrary, so pick by smallest angle to the pointing ray, else closest to the head
    private GameObject PickAmongMatches(List<GameObject> matches, string description)
    {
        GameObject best = matches[0];
        string rule = "only match";

        if (matches.Count > 1)
        {
            Transform head = ViewerTransform();
            if (TryGetTargetRay(out PointingRay ray))
            {
                rule = "smallest angle to pointing ray";
                float bestAngle = float.MaxValue;
                foreach (GameObject m in matches)
                {
                    float angle = Vector3.Angle(ray.Direction, GetPlacementBounds(m).center - ray.Origin);
                    if (angle < bestAngle) { bestAngle = angle; best = m; }
                }
            }
            else if (head != null)
            {
                rule = "closest to head";
                float bestDistance = float.MaxValue;
                foreach (GameObject m in matches)
                {
                    float d = (GetPlacementBounds(m).center - head.position).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = m; }
                }
            }
            else
            {
                rule = "first match (no pointing ray, no camera)";
            }
        }

        Debug.Log($"[ParserIntent] '{description.Trim()}': {matches.Count} match(es) → {best.name} (rule: {rule})");
        return best;
    }

    // Pointing or spoken properties, else the current selection
    private GameObject ResolveWithSelectionFallback(RecognizedIntent intent)
    {
        string color = intent.GetParameter("color");
        string obj   = intent.GetParameter("objectType");
        GameObject pointed = Pointed(intent);

        // "delete this" pointing at the wall must do nothing, not act on the selection
        bool pointingUsed   = intent.PointingTargets.Count > 0;
        bool propertySpoken = !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(obj);
        if (pointingUsed && pointed == null && !propertySpoken) return null;

        GameObject target = ResolveTarget(pointed, color, obj);
        if (target == null && _selectedObject != null)
        {
            Debug.Log("[ParserIntent] No target found — using current selection: " + _selectedObject.name);
            target = _selectedObject;
        }
        return target;
    }
}
