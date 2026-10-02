// Dialogue context: the filter with its badge and dimming, "them" (last row) and "again".
// The state itself lives in DialogueContext.

using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;
using MMI.Semantics;

public partial class ParserIntent
{
    private readonly DialogueContext _context = new DialogueContext();
    private FilterBadge _filterBadge;

    /// <summary>Filter, last group and last command.</summary>
    public DialogueContext Context => _context;

    // IsSelectable plus the filter: the check for every command target. Destinations never go through it.
    private VRObject IsTarget(GameObject go)
    {
        VRObject vro = IsSelectable(go);
        return vro != null && _context.Matches(vro) ? vro : null;
    }

    private void LogFilteredOut(string command, GameObject skipped) =>
        Refuse($"[ParserIntent] {command}: refused — {(skipped != null ? skipped.name + " is " : "target ")}filtered out by: only {_context.Describe()}");

    // Self-check hook: a fixed ray instead of the live controller ray
    private PointingRay? _targetRayOverride;

    private bool TryGetTargetRay(out PointingRay ray)
    {
        if (_targetRayOverride.HasValue) { ray = _targetRayOverride.Value; return true; }
        if (pointingSource != null && pointingSource.TryGetCurrentRay(out ray)) return true;
        ray = default;
        return false;
    }

    void HandleFilterSet(RecognizedIntent intent)
    {
        string type  = intent.GetParameter("objectType");
        string color = intent.GetParameter("color");
        if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(color))
        {
            Refuse("[ParserIntent] filter: refused — no object type or colour heard (e.g. \"consider only beakers\", \"only red ones\").");
            return;
        }
        _context.SetFilter(type, color);
        Debug.Log($"[ParserIntent] filter: only {_context.Describe()}");
        RefreshContextVisuals();
    }

    void HandleFilterClear(RecognizedIntent intent)
    {
        bool had = _context.HasFilter;
        _context.ClearFilter();
        Debug.Log($"[ParserIntent] filter: cleared{(had ? "" : " (none was active)")} — considering everything.");
        RefreshContextVisuals();
    }

    private readonly HashSet<VRObject> _dimmedObjects = new HashSet<VRObject>();
    private readonly List<Renderer> _dimRenderers = new List<Renderer>();
    private readonly List<VRObject> _dimScan = new List<VRObject>();
    private MaterialPropertyBlock _dimBlock;

    // Runs after every command, so new and recoloured objects are dimmed too; a selection outside the
    // filter is deselected
    private void RefreshContextVisuals()
    {
        if (_filterBadge != null) _filterBadge.Show(_context.HasFilter ? _context.Describe() : null);

        _dimScan.Clear();
        foreach (ISemanticEntity entity in SemanticRegistry.All)
            if (entity is VRObject vro && vro != null) _dimScan.Add(vro);
        foreach (VRObject vro in _dimScan)
            SetDimmed(vro, _context.HasFilter && IsSelectable(vro.gameObject) == vro && !_context.Matches(vro));
        _dimmedObjects.RemoveWhere(v => v == null);

        if (_selectedObject != null && IsTarget(_selectedObject) == null)
        {
            Debug.Log($"[ParserIntent] filter: deselected {_selectedObject.name} — outside \"only {_context.Describe()}\".");
            DeselectCurrent();
        }
    }

    // Dims through a MaterialPropertyBlock, so the materials are never touched and clearing the block
    // restores the exact original look
    private void SetDimmed(VRObject vro, bool dim)
    {
        if (dim == _dimmedObjects.Contains(vro)) return;
        if (dim) _dimmedObjects.Add(vro); else _dimmedObjects.Remove(vro);
        if (_dimBlock == null) _dimBlock = new MaterialPropertyBlock();

        vro.GetComponentsInChildren(false, _dimRenderers);
        foreach (Renderer r in _dimRenderers)
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (!dim) { r.SetPropertyBlock(null); continue; }

            Material m = r.sharedMaterial;
            string prop = m == null ? null : m.HasProperty("_BaseColor") ? "_BaseColor" : m.HasProperty("_Color") ? "_Color" : null;
            if (prop == null) continue;
            Color c = m.GetColor(prop);
            float grey = c.grayscale;
            Color d = Color.Lerp(c, new Color(grey, grey, grey, c.a), 0.7f) * 0.6f;
            d.a = c.a * 0.5f;
            _dimBlock.Clear();
            _dimBlock.SetColor(prop, d);
            r.SetPropertyBlock(_dimBlock);
        }

        ContainerLabel label = vro.GetComponent<ContainerLabel>();
        if (label != null) label.SetDimmed(dim);
    }

    private readonly List<GameObject> _groupTargets = new List<GameObject>();

    // True if the sentence said "them" and a last group exists (members outside the filter are skipped;
    // an empty list means all were filtered out and the command was refused). False = act normally.
    private bool TryGetGroupTargets(RecognizedIntent intent, string command, out List<GameObject> targets)
    {
        targets = _groupTargets;
        _groupTargets.Clear();
        if (string.IsNullOrEmpty(intent.GetParameter("groupRef")) || !_context.HasGroup) return false;

        int skipped = 0;
        foreach (GameObject member in _context.Group)
        {
            if (IsTarget(member) != null) _groupTargets.Add(member);
            else skipped++;
        }
        if (_groupTargets.Count == 0)
            Refuse($"[ParserIntent] {command} them: refused — every object of the last group is filtered out by: only {_context.Describe()}");
        else
            Debug.Log($"[ParserIntent] {command} them: last group, {_groupTargets.Count} object(s){(skipped > 0 ? $" ({skipped} filtered out)" : "")}");
        return true;
    }

    // The row keeps its layout, centred on the pointed spot
    private void MoveGroupToPointed(List<GameObject> members, RecognizedIntent intent, string command)
    {
        if (!TryGetCreateRay(intent, out PointingRay ray, out string source, out float sampleTime))
        {
            Refuse($"[ParserIntent] {command} them: no target — no pointing data.");
            return;
        }

        Physics.SyncTransforms();
        Bounds group = GetPlacementBounds(members[0]);
        foreach (GameObject m in members) group.Encapsulate(GetPlacementBounds(m));

        PointingRay destination = pointingSource != null ? pointingSource.Recast(ray.Origin, ray.Direction, members[0].transform) : ray;
        SpawnPlan plan = PlanFromRay(destination, HorizontalRadius(group), members[0]);

        bool first = true;
        foreach (GameObject m in members)
        {
            FinishActiveMove(m);
            PausePhysicsForMove(m);
            Vector3 start = m.transform.position;
            Bounds b = GetPlacementBounds(m);
            Vector3 offset = b.center - group.center;
            if (plan.OnSurface)
                SetOnSurface(m, plan.Point + new Vector3(offset.x, 0f, offset.z), plan.SurfaceY);
            else
                m.transform.position += plan.Point + offset - b.center;
            Vector3 end = m.transform.position;
            m.transform.position = start;
            Physics.SyncTransforms();

            bool playSound = first;
            GameObject member = m;
            StartMove(m, end, moveSpeed, () => { if (playSound && member != null) SoundFeedback.PlayAt(SoundFeedback.Sound.Move, member.transform.position); });
            first = false;
        }
        Debug.Log($"[ParserIntent] {command} them: {members.Count} object(s) → {plan.Point.ToString("F2")} ({source} + {plan.Case})");
    }

    void HandleRepeat(RecognizedIntent intent)
    {
        if (_context.LastAction == null)
        {
            Refuse("[ParserIntent] again: refused — nothing to repeat yet.");
            return;
        }
        if (!DialogueContext.IsRepeatable(_context.LastAction) || _context.LastRepeatable == null)
        {
            Refuse($"[ParserIntent] again: refused — \"{_context.LastAction}\" can't be repeated (create, delete all and filter commands aren't).");
            return;
        }
        Debug.Log($"[ParserIntent] again: repeating {_context.LastRepeatable.Action} (last target: {_context.LastTargets})");
        _context.LastRepeatableHandler(_context.LastRepeatable);
    }
}
