// One handler per action: read the parameters and pointing, resolve the target, run the command.

using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;
using MMI.Semantics;

public partial class ParserIntent
{
    void HandleSelect(RecognizedIntent intent)
    {
        string color = intent.GetParameter("color");
        string obj   = intent.GetParameter("objectType");
        GameObject pointedObject = Pointed(intent);
        GameObject target = ResolveTarget(pointedObject, color, obj);

        Vector3? pointedHit = (pointedObject != null && target == pointedObject) ? PointedHit(intent) : null;
        SelectObject(target, color, obj, pointedHit);
    }

    void HandleCreate(RecognizedIntent intent)
    {
        HidePreviewForCurrentUtterance();
        bool hasRay = TryGetCreateRay(intent, out PointingRay ray, out string source, out float sampleTime);
        string sampleInfo = "";
        if (hasRay)
        {
            string dt = _speechTiming != null && _speechTiming.HasUtterance
                ? $"{sampleTime - _speechTiming.UtteranceEndTime:+0.00;-0.00}s vs sentence end"
                : "no sentence timing";
            sampleInfo = $" | sample {dt}, ray hit: {(ray.HitObject != null ? ray.HitObject.name : "none")}";
        }
        int count = ParseCreateCount(intent.GetParameter("count"));
        CreateObjects(intent.GetParameter("color"), intent.GetParameter("objectType"), count,
                      hasRay ? ray : (PointingRay?)null, source, sampleInfo);
    }

    void HandleHighlight(RecognizedIntent intent)
    {
        GameObject target = ResolveWithSelectionFallback(intent);
        HighlightObject(target, intent.GetParameter("color"), intent.GetParameter("objectType"));
    }

    void HandleMove(RecognizedIntent intent)
    {
        string color = intent.GetParameter("color");
        string obj   = intent.GetParameter("objectType");

        // "move them there": the last group keeps its row layout
        if (TryGetGroupTargets(intent, "move", out List<GameObject> group))
        {
            if (group.Count > 0) MoveGroupToPointed(group, intent, "move");
            return;
        }

        // "move that there": first pointing word picks the object, second the destination
        if (_speechTiming != null && _speechTiming.PointingWordTimes.Count >= 2)
        {
            MoveTwoPoint(intent, "move");
            return;
        }

        bool propertySpoken = !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(obj);
        GameObject target = propertySpoken ? FindObjectByColorAndTag(color, obj) : _selectedObject;
        if (target == null)
        {
            Refuse("[ParserIntent] move: nothing selected or named to move.");
            return;
        }

        MoveToPointed(target, intent, "move");
    }

    // See BurnerMoveRule. The target is pointed, else named, else the selection, all within the filter.
    void HandleMoveToBurner(RecognizedIntent intent)
    {
        string color = intent.GetParameter("color");
        string obj   = intent.GetParameter("objectType");
        bool pointing = intent.PointingTargets.Count > 0;
        bool selectionIsBurner = _selectedObject != null && _selectedObject.GetComponent<BunsenBurner>() != null;
        BurnerMoveRule.Decision d = BurnerMoveRule.Decide(obj, color,
            !string.IsNullOrEmpty(intent.GetParameter("burnerDest")), !string.IsNullOrEmpty(intent.GetParameter("spatialDest")),
            !string.IsNullOrEmpty(intent.GetParameter("burnerSelf")), pointing, _selectedObject != null, selectionIsBurner);

        if (d.Refused)
        {
            Refuse($"[ParserIntent] moveToBurner: refused — {d.Reason} (target rule: {d.Rule}).");
            return;
        }

        GameObject target =
            d.Rule == BurnerMoveRule.TargetRule.Pointed  ? Pointed(intent) :
            d.Rule == BurnerMoveRule.TargetRule.Named    ? FindObjectByColorAndTag(color, d.TargetType) :
                                                           _selectedObject;
        if (target == null)
        {
            Refuse($"[ParserIntent] moveToBurner: no target (rule: {d.Rule}).");
            return;
        }

        if (d.Destination == BurnerMoveRule.Destination.PointedSpot)
        {
            Debug.Log($"[ParserIntent] moveToBurner: target {target.name} (rule: {d.Rule}) → destination: pointed spot");
            MoveToPointed(target, intent, "move");
            return;
        }
        if (target.GetComponent<BunsenBurner>() != null)
        {
            Refuse($"[ParserIntent] moveToBurner: refused — can't move the burner onto itself ({target.name}, rule: {d.Rule}).");
            return;
        }
        MoveToBurner(target, d.Rule.ToString());
    }

    private bool UtteranceHasWord(string word)
    {
        if (_speechTiming == null) return false;
        foreach (string w in _speechTiming.UtteranceWords)
            if (w == word) return true;
        return false;
    }

    // With a direction or angle it's a voice rotation; without ("rotate this") it starts twist mode
    void HandleRotate(RecognizedIntent intent)
    {
        string direction = intent.GetParameter("direction");
        string angle     = intent.GetParameter("angle");

        // "rotate them left": each member turns in place
        if (TryGetGroupTargets(intent, "rotate", out List<GameObject> group))
        {
            if (group.Count == 0) return;
            if (string.IsNullOrEmpty(direction) && string.IsNullOrEmpty(angle))
            {
                Refuse("[ParserIntent] rotate them: refused — twist mode works on one object; say a direction (\"rotate them left\").");
                return;
            }
            float groupDegrees = VoiceRotationDegrees(direction, angle);
            foreach (GameObject member in group) StartRotate(member, groupDegrees);
            return;
        }

        GameObject target = ResolveWithSelectionFallback(intent);
        if (target == null || !GuardTarget(target, "rotate")) return;

        if (string.IsNullOrEmpty(direction) && string.IsNullOrEmpty(angle))
        {
            // "turn ... burner" with on/off misheard is almost certainly a burner command, not a twist
            if (intent.GetParameter("objectType") == "burner" && UtteranceHasWord("turn") && intent.PointingTargets.Count == 0)
            {
                Refuse("[ParserIntent] rotate refused: heard \"turn … burner\" without on/off — say \"turn on the burner\" (or \"rotate the burner\" to twist it).");
                return;
            }
            StartTwist(target);
            return;
        }

        StartRotate(target, VoiceRotationDegrees(direction, angle));
    }

    // "around" = 180, a spoken angle, else voiceRotateStep; left is negative
    private float VoiceRotationDegrees(string direction, string angle)
    {
        if (direction == "around") return 180f;
        float degrees = float.TryParse(angle, out float spoken) ? spoken : voiceRotateStep;
        return direction == "left" ? -degrees : degrees; // positive yaw = clockwise from above = right
    }

    void HandleScaleUp(RecognizedIntent intent) => HandleScaleStep(intent, scaleStepFactor, "scale up");

    void HandleScaleDown(RecognizedIntent intent) => HandleScaleStep(intent, 1f / scaleStepFactor, "scale down");

    // One scale step on the target, or on every member for "make them bigger"
    private void HandleScaleStep(RecognizedIntent intent, float factor, string command)
    {
        if (TryGetGroupTargets(intent, command, out List<GameObject> group))
        {
            foreach (GameObject member in group) ScaleStep(member, factor, command);
            return;
        }
        GameObject target = ResolveWithSelectionFallback(intent);
        if (target == null || !GuardTarget(target, command)) return;
        ScaleStep(target, factor, command);
    }

    private void ScaleStep(GameObject target, float factor, string command)
    {
        Vector3 current = target.transform.localScale;
        float newSize = Mathf.Clamp(current.x * factor, minVoiceScale, maxVoiceScale);
        Vector3 goal = Vector3.one * newSize;

        StartCoroutine(LerpToScale(target, goal, scaleSpeed));
        Debug.Log($"[ParserIntent] {command} {target.name} → {goal}");
    }

    void HandleDelete(RecognizedIntent intent)
    {
        if (TryGetGroupTargets(intent, "delete", out List<GameObject> group))
        {
            DeleteGroup(group);
            return;
        }
        GameObject target = ResolveWithSelectionFallback(intent);
        DeleteObject(target);
    }

    // One delete sound for the whole group
    private void DeleteGroup(List<GameObject> members)
    {
        if (members.Count == 0) return;
        var copy = new List<GameObject>(members); // DeleteObject edits the context's group
        Vector3 soundPosition = copy[0].transform.position;
        _suppressDeleteSound = true;
        try { foreach (GameObject member in copy) DeleteObject(member); }
        finally { _suppressDeleteSound = false; }
        SoundFeedback.PlayAt(SoundFeedback.Sound.Delete, soundPosition);
        Debug.Log($"[ParserIntent] delete them: deleted {copy.Count} object(s) of the last group.");
    }

    // Only voice-created objects (origin = spawned), so scene fixtures are never removed. That filter
    // also keeps the query non-empty, which makes "delete everything" work.
    void HandleDeleteAll(RecognizedIntent intent)
    {
        string color = intent.GetParameter("color");
        string obj   = intent.GetParameter("objectType");

        // "delete all of them": the last group, if there is one
        if (TryGetGroupTargets(intent, "delete all", out List<GameObject> group))
        {
            DeleteGroup(group);
            return;
        }

        // Collect first: deleting unregisters entities from the registry being iterated
        var targets = new List<GameObject>();
        foreach (ISemanticEntity entity in new SemanticQuery()
                     .Where("origin", "spawned")
                     .Where("type", obj)
                     .Where("color", color)
                     .Resolve())
        {
            if (entity.Owner == null) continue;
            if (IsSelectable(entity.Owner) != null && IsTarget(entity.Owner) == null) continue; // outside the dialogue filter
            // "delete everything" leaves burners alone, even voice-created ones
            if (string.IsNullOrEmpty(obj) && entity.TryGetProperty("type", out string type) && type == "burner") continue;
            targets.Add(entity.Owner);
        }

        Vector3 soundPosition = targets.Count > 0 ? targets[0].transform.position : Vector3.zero;
        _suppressDeleteSound = true;
        try { foreach (GameObject target in targets) DeleteObject(target); }
        finally { _suppressDeleteSound = false; }
        Debug.Log($"[ParserIntent] Delete all: deleted {targets.Count} {DescribeObjects(color, obj, targets.Count)}"
                  + $"{(_context.HasFilter ? $" (filter: only {_context.Describe()})" : "")}.");

        if (targets.Count > 0) SoundFeedback.PlayAt(SoundFeedback.Sound.Delete, soundPosition); // one sound in total
        else MarkRefused();
    }

    // The spoken colour is the new colour, not a filter. ResolveTarget would reject the pointed object
    // just because it isn't that colour yet.
    void HandleChangeColor(RecognizedIntent intent)
    {
        string color = intent.GetParameter("color");
        if (string.IsNullOrEmpty(color))
        {
            Refuse("[ParserIntent] ChangeColor: no color detected in command.");
            return;
        }

        // "make them blue": every member of the last group
        if (TryGetGroupTargets(intent, "change color", out List<GameObject> group))
        {
            foreach (GameObject member in group) ChangeColorObject(member, color, null);
            return;
        }

        // Pointed, else a named type (the selection if it matches, else the best match), else the selection
        string objTag = intent.GetParameter("objectType");
        GameObject target = Pointed(intent);
        if (target == null && intent.PointingTargets.Count > 0 && string.IsNullOrEmpty(objTag)) return; // pointed at something that isn't a lab object
        if (target == null && !string.IsNullOrEmpty(objTag)) target = FindObjectByColorAndTag(null, objTag);
        if (target == null) target = _selectedObject;
        if (target == null)
        {
            Refuse("[ParserIntent] ChangeColor: no target pointed at or selected.");
            return;
        }

        ChangeColorObject(target, color, intent.GetParameter("objectType"));
    }

    void HandleAddChemical(RecognizedIntent intent)
    {
        ChemicalDatabase.Chemical chemical = ChemicalDatabase.GetChemical(intent.GetParameter("chemical"));
        GameObject target = ResolveWithSelectionFallback(intent);
        AddChemicalToContainer(target, chemical);
    }

    void HandleMix(RecognizedIntent intent)
    {
        string color = intent.GetParameter("color");
        string obj   = intent.GetParameter("objectType");
        GameObject destination = Pointed(intent);
        if (destination == null && (!string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(obj)))
            destination = FindObjectByColorAndTag(color, obj);

        MixContainers(_selectedObject, destination);
    }

    void HandleDeleteSolution(RecognizedIntent intent)
    {
        GameObject target = ResolveWithSelectionFallback(intent);
        DeleteSolution(target);
    }

    // "put that there" picks the object at "that" and the destination at "there". With one pointing word
    // ("put it there") the selection moves to the pointed spot.
    void HandlePutThere(RecognizedIntent intent)
    {
        if (_speechTiming != null && _speechTiming.PointingWordTimes.Count >= 2)
        {
            MoveTwoPoint(intent, "put");
            return;
        }

        if (_selectedObject == null)
        {
            Refuse("[ParserIntent] put: needs a current selection, or two pointing words (\"put that there\").");
            return;
        }
        MoveToPointed(_selectedObject, intent, "put");
    }
}
