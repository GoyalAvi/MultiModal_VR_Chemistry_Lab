// Tutorial and board interactions: experiments, basics lessons, plain navigation and help.
// Defined after putThere and before repeat.
using MMI.Fusion;

public static partial class InteractionSetup
{
    private static void DefineTutorialInteractions(FusionEngine engine)
    {
        // Two-word triggers, so "show" alone stays highlight and "next" alone goes to the board commands
        engine.DefineInteraction(new InteractionDefinition("tutorialNext", new[]
        {
            new[] { "next", "experiment" }, new[] { "skip", "experiment" }, new[] { "another", "experiment" },
            new[] { "following", "experiment" }, new[] { "advance", "experiment" }, new[] { "new", "experiment" },
        }, parameterSlots: new[] { "experimentWord" }));
        engine.DefineInteraction(new InteractionDefinition("tutorialPrevious", new[]
        {
            new[] { "previous", "experiment" }, new[] { "last", "experiment" }, new[] { "earlier", "experiment" },
            new[] { "prior", "experiment" }, new[] { "preceding", "experiment" }, new[] { "experiment", "before" },
        }, parameterSlots: new[] { "experimentWord" }));
        engine.DefineInteraction(new InteractionDefinition("tutorialShow", new[]
        {
            new[] { "show", "experiment" }, new[] { "open", "experiment" }, new[] { "start", "experiment" },
            new[] { "begin", "experiment" }, new[] { "load", "experiment" }, new[] { "launch", "experiment" },
        }, parameterSlots: new[] { "experimentNumber" }));

        // Basics commands only touch the basics board, experiment commands only the experiment board.
        // Every trigger names "lesson"/"basics"/..., so the two never tie.
        engine.DefineInteraction(new InteractionDefinition("basicsShow", new[]
        {
            new[] { "show", "basics" }, new[] { "show", "tutorial" }, new[] { "show", "instructions" },
            new[] { "open", "basics" }, new[] { "open", "tutorial" }, new[] { "how", "work" }, new[] { "teach", "me" },
        }, parameterSlots: new[] { "boardWord" }));
        engine.DefineInteraction(new InteractionDefinition("basicsHide", new[]
        {
            new[] { "hide", "basics" }, new[] { "hide", "tutorial" }, new[] { "hide", "instructions" },
            new[] { "close", "basics" }, new[] { "close", "tutorial" }, new[] { "close", "instructions" },
        }, parameterSlots: new[] { "boardWord" }));
        engine.DefineInteraction(new InteractionDefinition("lessonNext", new[]
        {
            new[] { "next", "lesson" }, new[] { "skip", "lesson" }, new[] { "following", "lesson" },
            new[] { "another", "lesson" }, new[] { "advance", "lesson" }, new[] { "forward", "lesson" },
        }, parameterSlots: new[] { "lessonWord" }));
        engine.DefineInteraction(new InteractionDefinition("lessonPrevious", new[]
        {
            new[] { "previous", "lesson" }, new[] { "last", "lesson" }, new[] { "earlier", "lesson" },
            new[] { "prior", "lesson" }, new[] { "preceding", "lesson" }, new[] { "lesson", "before" },
        }, parameterSlots: new[] { "lessonWord" }));
        // {repeat, lesson} and {lesson, again} beat repeat's one-word triggers
        engine.DefineInteraction(new InteractionDefinition("lessonRestart", new[]
        {
            new[] { "restart", "lesson" }, new[] { "reset", "lesson" }, new[] { "redo", "lesson" },
            new[] { "repeat", "lesson" }, new[] { "retry", "lesson" }, new[] { "lesson", "again" },
        }, parameterSlots: new[] { "lessonWord" }));
        engine.DefineInteraction(new InteractionDefinition("lessonShow", new[]
        {
            new[] { "show", "lesson" }, new[] { "open", "lesson" }, new[] { "start", "lesson" },
            new[] { "begin", "lesson" }, new[] { "load", "lesson" }, new[] { "launch", "lesson" },
        }, parameterSlots: new[] { "lessonNumber" }));
        // Plain navigation goes to the board the user looks at, else the one used last.
        // No {move, on}: it would beat move in "move it on the table".
        engine.DefineInteraction(new InteractionDefinition("boardNext", new[]
        {
            new[] { "next" }, new[] { "continue" }, new[] { "skip" }, new[] { "forward" },
            new[] { "go", "on" }, new[] { "next", "step" },
        }, parameterSlots: new[] { "navWord", "boardName" }));
        // No "back": the recognizer mixes it up with "black" ("make it black").
        // {one, before} and {last, step} are two words but share nothing with the experiment/lesson triggers.
        engine.DefineInteraction(new InteractionDefinition("boardPrevious", new[]
        {
            new[] { "previous" }, new[] { "earlier" }, new[] { "prior" }, new[] { "before" },
            new[] { "one", "before" }, new[] { "last", "step" },
        }, parameterSlots: new[] { "navWord", "boardName" }));
        // {begin, again} / {try, again} beat repeat's "again"; "reset the filter" ties {reset} with
        // {filter} and stays filterClear (defined first)
        engine.DefineInteraction(new InteractionDefinition("boardRestart", new[]
        {
            new[] { "restart" }, new[] { "reset" }, new[] { "redo" },
            new[] { "start", "over" }, new[] { "begin", "again" }, new[] { "try", "again" },
        }, parameterSlots: new[] { "navWord", "boardName" }));
        engine.DefineInteraction(new InteractionDefinition("help", new[]
        {
            new[] { "help" }, new[] { "commands" }, new[] { "what", "can" }, new[] { "what", "say" },
            new[] { "show", "commands" }, new[] { "list", "commands" },
        }, parameterSlots: new[] { "helpWord" }));
        // No "back" here either, or "make it black" could close the help card
        engine.DefineInteraction(new InteractionDefinition("helpClose", new[]
        {
            new[] { "done" }, new[] { "return" },
            new[] { "close", "help" }, new[] { "hide", "help" }, new[] { "exit", "help" },
            new[] { "close", "commands" }, new[] { "hide", "commands" },
        }, parameterSlots: new[] { "helpWord" }));
    }
}
