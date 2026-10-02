// Configures a FusionEngine with this app's vocabulary and interactions. Word tables and the
// interaction groups live in the other InteractionSetup.*.cs files.
using MMI.Fusion;

/// <summary>Registers all vocabulary and interactions on the engine. Pure data; the package knows none of these words.</summary>
public static partial class InteractionSetup
{
    public static void DefineInteractions(FusionEngine engine)
    {
        engine.SetPointingTriggerWords(PointingTriggerWords);
        // "create three red test tubes there" is 6 words; the default of 5 would flush mid-sentence
        engine.MaxBufferSize = 8;

        RegisterSlotVocabulary(engine);

        // Ties: the longest fully matched trigger phrase wins; on equal length the interaction defined
        // first wins, so the order below matters. Every phrasing is also listed in PhrasingSelfCheck.
        // Words left out because the recognizer confuses them with command words: take (make), twist (this),
        // flash (flask), mark (make), clean (green), back (black), play (place), large (larger), grow (glow).

        // Definition order = tie-break order. Don't reorder these calls or move definitions between them.
        DefineObjectInteractions(engine);  // select ... changeColor
        DefineChemistryInteractions(engine); // addChemical, mix, deleteSolution
        DefineFilterInteractions(engine);    // filterSet, filterClear
        DefineBurnerInteractions(engine);    // burnerOn, burnerOff
        DefinePutThereInteraction(engine);   // putThere
        DefineTutorialInteractions(engine);  // experiments, basics board, board navigation, help
        DefineRepeatInteraction(engine);  // must stay last

        // Two-hand scaling isn't a speech interaction; BimanualScaler handles it directly.
    }
}
