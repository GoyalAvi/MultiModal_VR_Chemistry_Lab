// Word rules for create (spoken count, missing object type), shared by the preview, the flush delay
// and the self-check. Same count rule as the one registered in InteractionSetup.
using System;
using System.Collections.Generic;

/// <summary>Word-level rules for the create command. No Unity or engine state.</summary>
public static class CreateCommandParser
{
    /// <summary>Count spoken between a create verb and a noun or colour ("make for red beakers" = 4), else 1.</summary>
    // Called every frame by the preview, so no allocations
    public static int ParseCount(IReadOnlyList<string> words)
    {
        for (int i = 0; i + 2 < words.Count; i++)
        {
            if (!Contains(InteractionSetup.CreateTriggers, words[i])) continue;
            int countIndex = IndexOf(InteractionSetup.CountWords, words[i + 1]);
            if (countIndex < 0) continue;
            string next = words[i + 2];
            if (InteractionSetup.ObjectTypeAliases.ContainsKey(next) || Contains(InteractionSetup.Colors, next))
                return InteractionSetup.CountValues[countIndex];
        }
        return 1;
    }

    /// <summary>True if there's a create verb with no object noun after it yet.</summary>
    public static bool IsCreateMissingType(IReadOnlyList<string> words)
    {
        int verb = -1;
        for (int i = 0; i < words.Count; i++)
            if (Contains(InteractionSetup.CreateTriggers, words[i])) verb = i;
        if (verb < 0) return false;
        for (int j = verb + 1; j < words.Count; j++)
            if (InteractionSetup.ObjectTypeAliases.ContainsKey(words[j])) return false;
        return true;
    }

    private static bool Contains(string[] list, string word) => IndexOf(list, word) >= 0;

    private static int IndexOf(string[] list, string word)
    {
        for (int i = 0; i < list.Length; i++)
            if (string.Equals(list[i], word, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
