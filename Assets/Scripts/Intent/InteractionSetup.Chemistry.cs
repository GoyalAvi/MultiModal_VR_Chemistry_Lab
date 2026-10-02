// Chemistry and burner interactions. Two methods because the filter interactions sit between
// them in the tie-break order.
using MMI.Fusion;

public static partial class InteractionSetup
{
    private static void DefineChemistryInteractions(FusionEngine engine)
    {
        // "add a new beaker" goes to create instead: {add, new} beats "add"
        engine.DefineInteraction(new InteractionDefinition("addChemical", new[] { "add", "pour", "fill", "sprinkle", "insert", "dissolve" },
            parameterSlots: new[] { "chemical", "color", "objectType" }, maxPointingSlots: 1));
        engine.DefineInteraction(new InteractionDefinition("mix", new[] { "mix", "combine", "stir", "blend", "agitate", "shake", "swirl" },
            parameterSlots: new[] { "color", "objectType" }, maxPointingSlots: 1));
        // {pour, out} beats addChemical's "pour" in "pour it out"
        engine.DefineInteraction(new InteractionDefinition("deleteSolution", new[]
        {
            new[] { "empty" }, new[] { "drain" }, new[] { "dump" },
            new[] { "pour", "out" }, new[] { "wash", "out" }, new[] { "tip", "out" },
        },
            parameterSlots: new[] { "color", "objectType" }, maxPointingSlots: 1));
    }

    private static void DefineBurnerInteractions(FusionEngine engine)
    {
        // All burner triggers are two words, so a shared word like "heat" ("heat it up" / "cut the heat")
        // can't cross-fire, and "switch" or "start" can't trigger an unrelated command.
        engine.DefineInteraction(new InteractionDefinition("burnerOn", BurnerOnTriggers,
            parameterSlots: new[] { "burnerWord" }));
        engine.DefineInteraction(new InteractionDefinition("burnerOff", BurnerOffTriggers,
            parameterSlots: new[] { "burnerWord" }));
    }
}
