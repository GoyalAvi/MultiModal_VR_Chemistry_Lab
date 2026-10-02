using System;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;

/// <summary>What the dialogue remembers between sentences: the filter, the last row ("them") and the last command ("again").</summary>
public class DialogueContext
{
    public string FilterType { get; private set; }
    public string FilterColor { get; private set; }
    public bool HasFilter => !string.IsNullOrEmpty(FilterType) || !string.IsNullOrEmpty(FilterColor);

    /// <summary>Bumped on every filter change so the visuals know to refresh.</summary>
    public int FilterVersion { get; private set; }

    /// <summary>A spoken type replaces the filter; a colour on its own narrows the current type.</summary>
    // "only beakers" then "only red ones" = red beakers; "only flasks" after that = all flasks
    public void SetFilter(string type, string color)
    {
        if (!string.IsNullOrEmpty(type))
        {
            FilterType = type;
            FilterColor = string.IsNullOrEmpty(color) ? null : color;
        }
        else if (!string.IsNullOrEmpty(color))
        {
            FilterColor = color;
        }
        FilterVersion++;
    }

    public void ClearFilter()
    {
        FilterType = null;
        FilterColor = null;
        FilterVersion++;
    }

    /// <summary>True if the object passes the filter (always, when there is none).</summary>
    public bool Matches(VRObject vro)
    {
        if (!HasFilter) return true;
        if (vro == null) return false;
        bool typeOk  = string.IsNullOrEmpty(FilterType)  || string.Equals(vro.objectType,  FilterType,  StringComparison.OrdinalIgnoreCase);
        bool colorOk = string.IsNullOrEmpty(FilterColor) || string.Equals(vro.objectColor, FilterColor, StringComparison.OrdinalIgnoreCase);
        return typeOk && colorOk;
    }

    /// <summary>"red beakers", "flasks", "red objects": for logs and the badge.</summary>
    public string Describe()
    {
        if (!HasFilter) return "everything";
        string noun = string.IsNullOrEmpty(FilterType) ? "objects" : Plural(FilterType);
        return string.IsNullOrEmpty(FilterColor) ? noun : $"{FilterColor} {noun}";
    }

    private static string Plural(string type) => type == "testtube" ? "test tubes" : type + "s";

    private readonly List<GameObject> _group = new List<GameObject>();

    /// <summary>The last created row, minus members that were destroyed.</summary>
    public IReadOnlyList<GameObject> Group
    {
        get
        {
            _group.RemoveAll(g => g == null);
            return _group;
        }
    }

    public bool HasGroup => Group.Count > 0;

    public void SetGroup(IEnumerable<GameObject> members)
    {
        _group.Clear();
        _group.AddRange(members);
    }

    public void RemoveFromGroup(GameObject go) => _group.Remove(go);

    /// <summary>Last command that ran, repeatable or not.</summary>
    public string LastAction { get; private set; }
    public RecognizedIntent LastRepeatable { get; private set; }
    public Action<RecognizedIntent> LastRepeatableHandler { get; private set; }
    /// <summary>What the last repeatable command acted on, for the log.</summary>
    public string LastTargets { get; private set; }

    /// <summary>Everything except create, delete all, filter commands and repeat itself.</summary>
    public static bool IsRepeatable(string action) =>
        action != "create" && action != "deleteAll" && action != "filterSet" && action != "filterClear" && action != "repeat";

    public void RecordCommand(RecognizedIntent intent, Action<RecognizedIntent> handler, string targets)
    {
        if (intent.Action == "repeat") return; // "again" repeats the command before it
        LastAction = intent.Action;
        if (!IsRepeatable(intent.Action)) return;
        LastRepeatable = intent;
        LastRepeatableHandler = handler;
        LastTargets = targets;
    }
}
