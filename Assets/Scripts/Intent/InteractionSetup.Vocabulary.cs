// All word tables and the slot/filler registration.
// Keep the table order: the burner trigger initialisers read tables declared above them.
using System;
using System.Collections.Generic;
using MMI.Fusion;

public static partial class InteractionSetup
{
    public static readonly string[] Colors =
        { "red", "violet", "blue", "yellow", "green", "black", "white", "orange" };

    /// <summary>Create verbs; ParserIntent also reads them for the spawn preview.</summary>
    public static readonly string[] CreateTriggers = { "create", "spawn", "make", "build", "generate" };

    // Words that can come right before a count: the create verbs plus "give me"
    private static readonly string[] CountVerbs = { "create", "spawn", "make", "build", "generate", "me" };

    /// <summary>Spoken counts, as words or digits.</summary>
    // "to/too/for" are what the recognizer usually hears for "two/four". 6-10 exist only so an
    // over-limit request is heard and capped. Parallel arrays so the preview can scan them without allocating.
    public static readonly string[] CountWords =
    {
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
        "to", "too", "for",
    };
    public static readonly int[] CountValues =
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
        2, 2, 4,
    };

    // Numbers only count directly after "experiment", same position rule as create counts
    public static readonly string[] ExperimentNumberWords = { "one", "two", "three", "four", "1", "2", "3", "4", "to", "too", "for" };
    public static readonly int[]    ExperimentNumberValues = { 1, 2, 3, 4, 1, 2, 3, 4, 2, 2, 4 };

    // 1-8, only directly after "lesson"
    public static readonly string[] LessonNumberWords =
        { "one", "two", "three", "four", "five", "six", "seven", "eight", "1", "2", "3", "4", "5", "6", "7", "8", "to", "too", "for" };
    public static readonly int[]    LessonNumberValues = { 1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 6, 7, 8, 2, 2, 4 };

    /// <summary>Spoken object nouns (incl. plurals) to object type.</summary>
    public static readonly Dictionary<string, string> ObjectTypeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "burner",   "burner" },
            { "banner",   "burner" },  // common mishearing of "burner"
            { "testtube", "testtube" },
            { "test",     "testtube" },  // "test" or "tube" alone still means testtube
            { "tube",     "testtube" },
            { "beaker",   "beaker" },
            { "flask",    "flask" },
            { "burners",   "burner" },
            { "testtubes", "testtube" },
            { "tubes",     "testtube" },
            { "beakers",   "beaker" },
            { "flasks",    "flask" },
        };

    // "banner" is a common mishearing. "burn" is left out on purpose: it's one sound from "turn", so
    // "turn it around" would start coming out as "burn it around".
    public static readonly string[] BurnerWords = { "burner", "burners", "banner" };

    public static readonly string[] MoveVerbs = { "move", "put", "place", "carry", "bring", "drag", "slide", "set", "position", "stick" };

    // "on top of" is covered by "of" ("top" sounds like "stop", a burner-off word).
    // No "onto": "turn on the burner" could then be heard as "turn onto the burner".
    public static readonly string[] BurnerPrepositions = { "to", "on", "of", "over", "next to", "toward", "towards" };

    // All two words, so they beat rotate's "turn" in "turn on the burner". Word order doesn't matter,
    // so {burner, on} also covers "turn the burner on" and "switch on the burner".
    public static readonly string[][] BurnerOnTriggers = BuildBurnerTriggers(
        new[] { new[] { "turn", "on" }, new[] { "switch", "on" }, new[] { "start", "flame" }, new[] { "fire", "up" }, new[] { "heat", "up" } },
        "on", "ignite", "light", "start");
    public static readonly string[][] BurnerOffTriggers = BuildBurnerTriggers(
        new[] { new[] { "turn", "off" }, new[] { "switch", "off" }, new[] { "douse", "flame" }, new[] { "cut", "heat" }, new[] { "end", "heating" } },
        "off", "shut", "stop");

    // The size word has to directly follow "this". "pick" isn't added as a mishearing of "big":
    // it's a select trigger, so "make it this pick" would become a select.
    public static readonly string[] SizeWords = { "big", "tall", "wide", "size", "long", "high" };

    /// <summary>Words that ask for a pointing sample. Set explicitly so the speech adapter can time them.</summary>
    public static readonly string[] PointingTriggerWords = { "this", "that", "there", "here" };

    private static void RegisterSlotVocabulary(FusionEngine engine)
    {
        engine.RegisterVocabulary("color", Colors);

        engine.RegisterVocabulary("objectType", ObjectTypeAliases);

        // Counts only count in "<create verb> <count> <noun|colour>", registered as three-word aliases,
        // so a stray "to"/"for" ("next to that", "for mixing") is never a number. CreateCommandParser
        // applies the same rule for the preview.
        var counts = new Dictionary<string, string>();
        foreach (string verb in CountVerbs)
            for (int i = 0; i < CountWords.Length; i++)
            {
                foreach (string noun in ObjectTypeAliases.Keys) counts[$"{verb} {CountWords[i]} {noun}"] = CountValues[i].ToString();
                foreach (string color in Colors)                counts[$"{verb} {CountWords[i]} {color}"] = CountValues[i].ToString();
            }
        engine.RegisterVocabulary("count", counts);

        // The colour slot takes the first colour word, so in "change the red beaker to blue" it would get
        // "red". "to/into <colour>" is the new colour and, being multi-word, is matched first.
        var newColor = new Dictionary<string, string>();
        foreach (string color in Colors)
        {
            newColor[$"to {color}"]   = color;
            newColor[$"into {color}"] = color;
        }
        engine.RegisterVocabulary("color", newColor);

        engine.RegisterVocabulary("direction", new Dictionary<string, string>
        {
            { "left", "left" }, { "right", "right" }, { "around", "around" },
        });
        engine.RegisterVocabulary("angle", new Dictionary<string, string>
        {
            { "fifteen", "15" }, { "thirty", "30" }, { "forty five", "45" }, { "forty", "40" }, { "sixty", "60" },
            { "ninety", "90" }, { "one hundred eighty", "180" }, { "hundred eighty", "180" },
            { "one hundred thirty five", "135" }, { "two hundred seventy", "270" }, { "three hundred sixty", "360" },
            { "15", "15" }, { "30", "30" }, { "40", "40" }, { "45", "45" }, { "60", "60" }, { "90", "90" },
            { "120", "120" }, { "135", "135" }, { "180", "180" }, { "270", "270" }, { "360", "360" },
        });

        var experimentNumbers = new Dictionary<string, string>();
        for (int i = 0; i < ExperimentNumberWords.Length; i++)
            experimentNumbers[$"experiment {ExperimentNumberWords[i]}"] = ExperimentNumberValues[i].ToString();
        engine.RegisterVocabulary("experimentNumber", experimentNumbers);
        // Only there so "next experiment" counts as complete and gets the quick flush; value unused
        engine.RegisterVocabulary("experimentWord", new Dictionary<string, string> { { "experiment", "experiment" } });

        var lessonNumbers = new Dictionary<string, string>();
        for (int i = 0; i < LessonNumberWords.Length; i++)
            lessonNumbers[$"lesson {LessonNumberWords[i]}"] = LessonNumberValues[i].ToString();
        engine.RegisterVocabulary("lessonNumber", lessonNumbers);
        // Completeness words for the quick flush; only boardName's value is used (routes plain "next" to a board)
        engine.RegisterVocabulary("lessonWord", new Dictionary<string, string> { { "lesson", "lesson" } });
        engine.RegisterVocabulary("boardWord", new Dictionary<string, string>
        {
            { "basics", "basics" }, { "tutorial", "basics" }, { "instructions", "basics" }, { "work", "basics" }, { "me", "basics" },
        });
        engine.RegisterVocabulary("boardName", new Dictionary<string, string>
        {
            { "experiment", "experiment" }, { "lesson", "basics" }, { "basics", "basics" },
        });
        var navWords = new Dictionary<string, string>();
        foreach (string w in new[] { "next", "continue", "on", "skip", "forward", "step", "previous", "earlier", "prior",
                                     "before", "restart", "over", "reset", "redo", "again" })
            navWords[w] = "nav";
        engine.RegisterVocabulary("navWord", navWords);
        engine.RegisterVocabulary("helpWord", new Dictionary<string, string>
        {
            { "help", "help" }, { "commands", "help" }, { "say", "help" }, { "can", "help" }, { "done", "help" }, { "return", "help" },
        });

        engine.RegisterVocabulary("chemical", ChemicalDatabase.GetAliases());

        // Every burner trigger contains one of these, so burner commands get the quick flush; value unused
        var burnerWords = new Dictionary<string, string>();
        foreach (string w in BurnerWords) burnerWords[w] = "burner";
        foreach (string w in new[] { "on", "off", "flame", "up", "heat", "heating" }) burnerWords[w] = "burner";
        engine.RegisterVocabulary("burnerWord", burnerWords);

        // Used by BurnerMoveRule to decide whether the burner is the destination, the target, or both
        var burnerDest = new Dictionary<string, string>();
        var burnerSelf = new Dictionary<string, string>();
        foreach (string prep in BurnerPrepositions)
            foreach (string b in BurnerWords)
            {
                burnerDest[$"{prep} the {b}"] = "yes";
                burnerDest[$"{prep} {b}"] = "yes";
                foreach (string first in BurnerWords)
                {
                    burnerSelf[$"{first} {prep} the {b}"] = "yes";
                    burnerSelf[$"{first} {prep} {b}"] = "yes";
                }
            }
        engine.RegisterVocabulary("burnerDest", burnerDest);
        engine.RegisterVocabulary("burnerSelf", burnerSelf);
        engine.RegisterVocabulary("spatialDest", new Dictionary<string, string> { { "there", "yes" }, { "here", "yes" } });

        // "them" / "those" = the last row created; value unused
        engine.RegisterVocabulary("groupRef", new Dictionary<string, string>
        {
            { "them", "group" }, { "those", "group" }, { "all of them", "group" },
        });
        // Completeness words for the quick flush
        engine.RegisterVocabulary("filterWord", new Dictionary<string, string>
        {
            { "filter", "clear" }, { "everything", "clear" }, { "objects", "clear" },
        });
        // Completeness words for the quick flush
        engine.RegisterVocabulary("repeatWord", new Dictionary<string, string>
        {
            { "again", "again" }, { "repeat", "again" }, { "time", "again" }, { "more", "again" },
        });

        // A constrained recognizer maps any word outside its vocabulary to the nearest allowed one, e.g.
        // "move it to burner" came out as "move it tube burner". Filler words keep those harmless.
        engine.RegisterFillerWords(new[]
        {
            "to", "the", "a", "an", "it", "at", "into",
            "acid", "bit", "of", "size", "exactly", "over", "together",
            "contents", "some", "chemicals", "them", "with", "degrees", "degree",
            "toward", "towards",
            // "only red ones", "work with beakers only", "same again"
            "ones", "look", "work", "clear", "reset", "same",
            // "how does it work", "what can I say"
            "does", "i",
        });
    }
}
