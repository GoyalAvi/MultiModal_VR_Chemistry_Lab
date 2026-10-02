// Dialogue-context interactions: the object filter and "again".
// Two methods because repeat has to be defined last of all.
using MMI.Fusion;

public static partial class InteractionSetup
{
    private static void DefineFilterInteractions(FusionEngine engine)
    {
        // The filter restricts every target choice until cleared; destinations are never filtered.
        // Defined after the object commands so a one-word tie ("select only the red one") stays select,
        // and before the burner so "focus on the burners" isn't read as {burners, on}.
        engine.DefineInteraction(new InteractionDefinition("filterSet", new[]
        {
            new[] { "only" }, new[] { "just" }, new[] { "focus" },
            new[] { "consider", "only" }, new[] { "focus", "on" },
        }, parameterSlots: new[] { "color", "objectType" }));
        // {remove, filter} / {delete, filter} are two words, so they beat plain "delete".
        // "delete all objects" ties {delete, all} with {all, objects} and stays deleteAll (defined first).
        engine.DefineInteraction(new InteractionDefinition("filterClear", new[]
        {
            new[] { "filter" }, new[] { "consider", "everything" }, new[] { "all", "objects" },
            new[] { "show", "everything" }, new[] { "remove", "filter" }, new[] { "delete", "filter" },
        }, parameterSlots: new[] { "filterWord" }));
    }

    private static void DefineRepeatInteraction(FusionEngine engine)
    {
        // Defined last, so "rotate it again" stays rotate. {once, more} and {more, time} are two words,
        // so "create one more beaker" doesn't match.
        engine.DefineInteraction(new InteractionDefinition("repeat", new[]
        {
            new[] { "again" }, new[] { "repeat" }, new[] { "more", "time" }, new[] { "once", "more" },
        }, parameterSlots: new[] { "repeatWord" }));
    }
}
