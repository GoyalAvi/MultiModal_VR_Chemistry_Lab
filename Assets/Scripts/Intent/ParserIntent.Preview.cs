// Create preview (discs where objects will spawn while the sentence is spoken) and Scene-view gizmos.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MMI.Fusion;
using MMI.Semantics;

public partial class ParserIntent
{
    private const float PreviewRadius = 0.05f;

    private GameObject[] _previewPool;   // one disc per possible row member, created once

    private int _previewDoneUtteranceId = -1;

    private float _lastRowSpacing = 0.1f; // the preview doesn't know the prefab yet, so it reuses the last spacing

    private SpawnPlan _lastPlan;

    private bool _hasLastPlan;

    private Vector3 _lastPlanFinal;

    // From the first speech event of a create sentence until the objects spawn: one disc per object,
    // laid out like the row. Pooled, no per-frame allocation.
    private void UpdateSpawnPreview()
    {
        bool show = showSpawnPreview && _speechTiming != null
                    && _speechTiming.UtteranceId != _previewDoneUtteranceId
                    && Time.time - _speechTiming.LastSpeechEventTime < previewTimeout
                    && _speechTiming.CurrentUtteranceContains(InteractionSetup.CreateTriggers)
                    && !InteractionSetup.IsResizeCommand(_speechTiming.UtteranceWordsWithHypothesis);
        if (!show)
        {
            SetPreviewCount(0);
            return;
        }

        int cap = Mathf.Max(1, maxCreateCount);
        int count = Mathf.Clamp(CreateCommandParser.ParseCount(_speechTiming.UtteranceWordsWithHypothesis), 1, cap);

        SpawnPlan plan = pointingSource != null && pointingSource.TryGetCurrentRay(out PointingRay ray)
            ? PlanFromRay(ray, PreviewRadius, null)
            : PlanFallback(null);
        Vector3 rowDir = RowDirection(plan);

        EnsurePreviewPool(cap);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = plan.Point + rowDir * RowOffset(i, count, _lastRowSpacing);
            if (plan.OnSurface)
            {
                TryRowMemberSurface(plan, pos, null, out float y);
                pos.y = y + 0.003f;
            }
            _previewPool[i].transform.position = pos;
        }
        SetPreviewCount(count);

        _lastPlan = plan;
        _hasLastPlan = true;
        _lastPlanFinal = plan.Point;
    }

    private void SetPreviewCount(int n)
    {
        if (_previewPool == null) return;
        for (int i = 0; i < _previewPool.Length; i++)
        {
            bool active = i < n;
            if (_previewPool[i] != null && _previewPool[i].activeSelf != active) _previewPool[i].SetActive(active);
        }
    }

    // A command fired for this utterance, so hide the preview for it
    private void HidePreviewForCurrentUtterance()
    {
        if (_speechTiming != null) _previewDoneUtteranceId = _speechTiming.UtteranceId;
        SetPreviewCount(0);
    }

    // Created once; again only if maxCreateCount is raised at runtime
    private void EnsurePreviewPool(int size)
    {
        if (_previewPool != null && _previewPool.Length >= size) return;
        GameObject[] pool = new GameObject[size];
        for (int i = 0; i < size; i++)
            pool[i] = _previewPool != null && i < _previewPool.Length ? _previewPool[i] : CreatePreviewDisc(i);
        _previewPool = pool;
    }

    private GameObject CreatePreviewDisc(int index)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = $"CreateSpawnPreview_{index}";
        disc.layer = 2; // built-in Ignore Raycast
        // Must not block the pointing ray or count in overlap checks
        DestroyImmediate(disc.GetComponent<Collider>());
        disc.transform.localScale = new Vector3(previewSize, 0.002f, previewSize);

        Renderer r = disc.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        Material m = previewMaterial != null ? previewMaterial : highlightMaterial;
        if (m != null) r.sharedMaterial = m;
        disc.SetActive(false);
        return disc;
    }

    // Scene view: ray (cyan), drop-cast (yellow), chosen point (green on a surface, magenta in the air)
    void OnDrawGizmos()
    {
        if (!_hasLastPlan) return;
        SpawnPlan p = _lastPlan;
        if (p.HasRay)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p.RayOrigin, p.RayOrigin + p.RayDirection * p.RayLength);
        }
        if (p.HasDrop)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p.DropFrom, p.DropFrom + Vector3.down * p.DropLength);
        }
        Gizmos.color = p.OnSurface ? Color.green : Color.magenta;
        Gizmos.DrawWireSphere(p.Point, 0.03f);
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(_lastPlanFinal, 0.02f);
    }
}
