// Object interactions (select ... changeColor, plus putThere). Order inside and between the
// methods is the tie-break order.
using System.Collections.Generic;
using MMI.Fusion;

public static partial class InteractionSetup
{
    private static void DefineObjectInteractions(FusionEngine engine)
    {
        engine.DefineInteraction(new InteractionDefinition("select", new[] { "select", "pick", "choose", "grab", "hold", "catch" },
            parameterSlots: new[] { "color", "objectType" }, maxPointingSlots: 1));

        // Defined before create: "make the red beaker bigger" ties {make, beaker} with {make, bigger},
        // and the tie has to go to scaling
        engine.DefineInteraction(new InteractionDefinition("scaleUp", new[]
        {
            new[] { "bigger" }, new[] { "enlarge" }, new[] { "increase" },
            new[] { "larger" }, new[] { "expand" }, new[] { "scale", "up" },
            new[] { "make", "bigger" }, new[] { "make", "larger" },
        }, parameterSlots: new[] { "color", "objectType", "groupRef" }, maxPointingSlots: 1));
        engine.DefineInteraction(new InteractionDefinition("scaleDown", new[]
        {
            new[] { "smaller" }, new[] { "shrink" }, new[] { "reduce" },
            new[] { "tinier" }, new[] { "scale", "down" },
            new[] { "make", "smaller" }, new[] { "make", "tinier" },
        }, parameterSlots: new[] { "color", "objectType", "groupRef" }, maxPointingSlots: 1));

        // Needs a gesture: the engine waits for the hand distance HandDistanceSource recorded at "this".
        // No pointing slot, or the "this" in "this big" would make it wait for pointing; a pointed target
        // ("make that this big") comes from the ray history instead. Before create for the same tie reason.
        engine.RegisterVocabulary("sizeAxis", new Dictionary<string, string>
        {
            { "big", "big" }, { "size", "big" }, { "long", "big" }, { "tall", "tall" }, { "high", "tall" }, { "wide", "wide" },
        });
        var resizeTriggers = new List<string[]>();
        foreach (string w in SizeWords) resizeTriggers.Add(new[] { "this", w });
        engine.DefineInteraction(new InteractionDefinition("resizeTo", resizeTriggers,
            parameterSlots: new[] { "sizeAxis", "color", "objectType" }, requiresGesture: true));

        // Before create: "make the flask blink" ties {make, flask} with {make, blink}
        engine.DefineInteraction(new InteractionDefinition("highlight", new[]
        {
            new[] { "highlight" }, new[] { "show" }, new[] { "glow" }, new[] { "blink" }, new[] { "spotlight" }, new[] { "outline" },
            new[] { "make", "glow" }, new[] { "make", "blink" },
        }, parameterSlots: new[] { "color", "objectType" }, maxPointingSlots: 1));

        // {make, <object>} exists so "make a red beaker" ties with changeColor's {make, red} and create
        // wins (defined first); "make it red" has no object and stays a recolor. {add, new} beats "add".
        var createTriggers = new List<string[]>();
        foreach (string verb in CreateTriggers) createTriggers.Add(new[] { verb });
        createTriggers.Add(new[] { "give", "me" });
        createTriggers.Add(new[] { "add", "new" });
        foreach (string noun in ObjectTypeAliases.Keys) createTriggers.Add(new[] { "make", noun });
        engine.DefineInteraction(new InteractionDefinition("create", createTriggers,
            parameterSlots: new[] { "count", "color", "objectType" }, maxPointingSlots: 1));

        engine.DefineInteraction(new InteractionDefinition("move", new[] { "move", "drag", "place", "bring", "carry", "slide" },
            parameterSlots: new[] { "color", "objectType", "groupRef" }, maxPointingSlots: 1));
        // {verb, burner} beats plain move/put, and {verb, on, burner} beats burnerOn's {burner, on} in
        // "put it on the burner" (by length). BurnerMoveRule decides who actually moves.
        var moveToBurnerTriggers = new List<string[]>();
        foreach (string verb in MoveVerbs)
            foreach (string b in BurnerWords)
            {
                moveToBurnerTriggers.Add(new[] { verb, b });
                moveToBurnerTriggers.Add(new[] { verb, "on", b });
                moveToBurnerTriggers.Add(new[] { verb, "to", b });
            }
        engine.DefineInteraction(new InteractionDefinition("moveToBurner", moveToBurnerTriggers,
            parameterSlots: new[] { "color", "objectType", "burnerDest", "burnerSelf", "spatialDest" }, maxPointingSlots: 1));

        // Without a direction or angle ("rotate this") it becomes twist mode
        engine.DefineInteraction(new InteractionDefinition("rotate", new[] { "rotate", "spin", "turn", "swivel", "twirl", "revolve" },
            parameterSlots: new[] { "direction", "angle", "color", "objectType", "groupRef" }, maxPointingSlots: 1));

        engine.DefineInteraction(new InteractionDefinition("delete", new[]
        {
            new[] { "delete" }, new[] { "remove" }, new[] { "destroy" }, new[] { "erase" }, new[] { "trash" },
            new[] { "get", "rid" }, new[] { "throw", "away" },
        }, parameterSlots: new[] { "color", "objectType", "groupRef" }, maxPointingSlots: 1));
        // Two-word triggers beat plain "delete", so "delete this" still goes to delete. No pointing slot.
        engine.DefineInteraction(new InteractionDefinition("deleteAll", new[]
        {
            new[] { "delete", "all" }, new[] { "remove", "all" }, new[] { "destroy", "all" }, new[] { "erase", "all" },
            new[] { "delete", "everything" }, new[] { "remove", "everything" }, new[] { "destroy", "everything" }, new[] { "erase", "everything" },
        }, parameterSlots: new[] { "color", "objectType", "groupRef" }));
        // {make, <colour>} beats create's "make". No {turn, <colour>}: it would beat rotate in
        // "turn the red beaker around".
        var changeColorTriggers = new List<string[]>();
        foreach (string w in new[] { "change", "recolor", "paint", "color", "tint", "dye" }) changeColorTriggers.Add(new[] { w });
        foreach (string color in Colors) changeColorTriggers.Add(new[] { "make", color });
        engine.DefineInteraction(new InteractionDefinition("changeColor", changeColorTriggers,
            parameterSlots: new[] { "color", "objectType", "groupRef" }, maxPointingSlots: 1));
    }

    private static void DefinePutThereInteraction(FusionEngine engine)
    {
        // Only one engine slot: the engine collects pointing after the sentence, and a second slot
        // would need the ray to move to another object. Both "that" and "there" come from the ray
        // history at the time each word was spoken (SpeechSourceAdapter.PointingWordTimes).
        engine.DefineInteraction(new InteractionDefinition("putThere", new[] { "put", "drop", "set", "leave", "position", "stick" },
            maxPointingSlots: 1));
    }
}
