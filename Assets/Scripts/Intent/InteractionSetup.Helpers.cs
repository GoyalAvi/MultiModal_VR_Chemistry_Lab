// Word-level checks on a sentence (resize marker, burner command) and the burner trigger builder.
using System;
using System.Collections.Generic;

public static partial class InteractionSetup
{
    private static string[][] BuildBurnerTriggers(string[][] fixedPhrases, params string[] withBurnerWord)
    {
        var phrases = new List<string[]>(fixedPhrases);
        foreach (string w in withBurnerWord)
            foreach (string burner in BurnerWords)
                phrases.Add(new[] { burner, w });
        return phrases.ToArray();
    }

    /// <summary>Index of "this" in "this big/tall/wide/...", or -1. Called every frame, so no allocations.</summary>
    public static int FindSizeMarker(IReadOnlyList<string> words)
    {
        for (int i = 0; i + 1 < words.Count; i++)
            if (words[i] == "this" && Array.IndexOf(SizeWords, words[i + 1]) >= 0) return i;
        return -1;
    }

    public static bool IsResizeCommand(IReadOnlyList<string> words) => FindSizeMarker(words) >= 0;

    /// <summary>True once the words contain a complete burner on/off trigger, so the host can flush right away.</summary>
    public static bool IsBurnerCommand(IReadOnlyList<string> words) =>
        ContainsPhrase(words, BurnerOnTriggers) || ContainsPhrase(words, BurnerOffTriggers);

    private static bool ContainsPhrase(IReadOnlyList<string> words, string[][] phrases)
    {
        foreach (string[] phrase in phrases)
        {
            bool all = true;
            foreach (string w in phrase)
            {
                bool found = false;
                for (int i = 0; i < words.Count && !found; i++) found = string.Equals(words[i], w, StringComparison.OrdinalIgnoreCase);
                if (!found) { all = false; break; }
            }
            if (all) return true;
        }
        return false;
    }
}
