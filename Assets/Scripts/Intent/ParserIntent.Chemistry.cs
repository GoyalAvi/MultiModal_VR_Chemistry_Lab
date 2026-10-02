// Chemistry and burner commands; the work is done by LabContainer and BunsenBurner.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MMI.Fusion;

public partial class ParserIntent
{
    void AddChemicalToContainer(GameObject target, ChemicalDatabase.Chemical chemical)
    {
        if (chemical == null)
        {
            Refuse("[ParserIntent] AddChemical: no chemical detected in command.");
            return;
        }
        GameObject container = target != null ? target : _selectedObject;
        if (container == null) { Refuse("[ParserIntent] AddChemical: no container targeted or selected."); return; }

        LabContainer lab = container.GetComponent<LabContainer>();
        if (lab == null) { Refuse($"[ParserIntent] {container.name} has no LabContainer component."); return; }

        lab.AddChemical(chemical);
        StartCoroutine(FlashHighlight(container, 0.3f));
    }

    void MixContainers(GameObject source, GameObject destination)
    {
        if (source == null) { Refuse("[ParserIntent] Mix: no source — select a container first."); return; }
        if (destination == null) { Refuse("[ParserIntent] Mix: no destination — point at a container while speaking."); return; }

        LabContainer sourceLab = source.GetComponent<LabContainer>();
        LabContainer destLab   = destination.GetComponent<LabContainer>();

        if (sourceLab == null || destLab == null) { Refuse("[ParserIntent] Mix: missing LabContainer on one or both objects."); return; }
        // Checked here as well so these count as refusals, not just LabContainer warnings
        if (sourceLab == destLab) { Refuse("[ParserIntent] Mix: refused — can't mix a container into itself."); return; }
        if (sourceLab.IsEmpty) { Refuse($"[ParserIntent] Mix: refused — {source.name} is empty."); return; }

        destLab.MixFrom(sourceLab);
        StartCoroutine(FlashHighlight(destination, 0.5f));

        if (source == _selectedObject) DeselectCurrent();
    }

    void DeleteSolution(GameObject target)
    {
        GameObject container = target != null ? target : _selectedObject;
        if (container == null) { Refuse("[ParserIntent] DeleteSolution: no container targeted or selected."); return; }

        LabContainer lab = container.GetComponent<LabContainer>();
        if (lab == null) { Refuse($"[ParserIntent] {container.name} has no LabContainer component."); return; }

        lab.ClearContents();
    }

    // Target: the burner pointed at, else the selected one, else the closest. Turning on a burner that's
    // already on just confirms.
    void SetBurnerState(RecognizedIntent intent, bool on)
    {
        string command = on ? "burner on" : "burner off";
        BunsenBurner burner = ResolveBurner(intent, out string rule);
        if (burner == null) { Refuse($"[ParserIntent] {command}: no BunsenBurner found in scene."); return; }

        float sentenceEnd = _speechTiming != null && _speechTiming.HasUtterance ? _speechTiming.UtteranceEndTime : -1f;
        string timing = sentenceEnd >= 0f ? $", {(Time.time - sentenceEnd) * 1000f:F0} ms after sentence end" : "";
        Debug.Log($"[ParserIntent] {command}: {burner.name} (rule: {rule}){timing}");

        bool changed = on ? burner.TurnOn(sentenceEnd) : burner.TurnOff(sentenceEnd);
        if (!changed) Debug.Log($"[ParserIntent] {command}: {burner.name} is already {(on ? "on" : "off")} — nothing restarted.");
    }

    private BunsenBurner ResolveBurner(RecognizedIntent intent, out string rule)
    {
        // 1. Pointed: the engine slot, else the ray at sentence end (near-miss tolerant)
        GameObject pointed = null;
        if (intent.PointingTargets.Count > 0) pointed = intent.PointingTargets[0].HitObject;
        if (pointed == null && pointingSource != null && _speechTiming != null && _speechTiming.HasUtterance
            && pointingSource.TryGetLatestRayBefore(_speechTiming.UtteranceEndTime, pointingTimeTolerance, out PointingRay ray, out _))
        {
            pointed = ray.HitObject;
            if (pointed == null || pointed.GetComponentInParent<BunsenBurner>() == null)
            {
                VRObject near = NearestSelectableToRay(ray.Origin, ray.Direction, out _);
                if (near != null && near.GetComponent<BunsenBurner>() != null) pointed = near.gameObject;
            }
        }
        BunsenBurner burner = pointed != null ? pointed.GetComponentInParent<BunsenBurner>() : null;
        if (burner != null) { rule = "pointed"; return burner; }

        // 2. Selected
        burner = _selectedObject != null ? _selectedObject.GetComponentInParent<BunsenBurner>() : null;
        if (burner != null) { rule = "selected"; return burner; }

        // 3. Closest to the user
        Transform viewer = ViewerTransform();
        float best = float.MaxValue;
        foreach (BunsenBurner b in FindObjectsByType<BunsenBurner>(FindObjectsSortMode.None))
        {
            float d = viewer != null ? (b.transform.position - viewer.position).sqrMagnitude : 0f;
            if (d < best) { best = d; burner = b; }
        }
        rule = "closest to user";
        return burner;
    }
}
