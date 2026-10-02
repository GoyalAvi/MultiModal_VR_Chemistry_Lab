using System;
using UnityEngine;
using MMI.Fusion;

/// <summary>Self-check: every sentence below must resolve to its interaction in a real, configured FusionEngine.</summary>
// Run from InputTest's context menu, in Edit or Play mode. The README phrasing table lists the same sentences.
public static class PhrasingSelfCheck
{
    /// <summary>(interaction, sentence); the first sentence of each interaction is its main phrase.</summary>
    public static readonly (string action, string sentence)[] Cases =
    {
        ("select", "select that"), ("select", "pick the red beaker"), ("select", "choose this flask"),
        ("select", "grab that"), ("select", "hold the blue beaker"), ("select", "catch that test tube"),

        ("create", "create a beaker"), ("create", "spawn a flask"), ("create", "make a red beaker"),
        ("create", "build two test tubes"), ("create", "generate a flask"), ("create", "give me a beaker"),
        ("create", "add a new beaker"), ("create", "make three red beakers"), ("create", "create a beaker there"),

        ("highlight", "highlight that"), ("highlight", "show the red beaker"), ("highlight", "make it glow"),
        ("highlight", "make the flask blink"), ("highlight", "spotlight that beaker"), ("highlight", "outline the test tube"),

        ("move", "move it there"), ("move", "drag that there"), ("move", "place the beaker there"),
        ("move", "bring it here"), ("move", "carry the flask there"), ("move", "slide it there"),
        ("moveToBurner", "move it to the burner"), ("moveToBurner", "bring it to the burner"),

        ("rotate", "rotate left"), ("rotate", "spin it right"), ("rotate", "turn it around"),
        ("rotate", "swivel the beaker left"), ("rotate", "twirl it"), ("rotate", "revolve that"),
        ("rotate", "rotate ninety degrees right"), ("rotate", "turn this toward there"),

        ("scaleUp", "make it bigger"), ("scaleUp", "enlarge that"), ("scaleUp", "increase the beaker"),
        ("scaleUp", "make the red beaker larger"), ("scaleUp", "expand it"), ("scaleUp", "scale up the flask"),
        ("scaleUp", "make the red beaker bigger"),

        ("scaleDown", "make it smaller"), ("scaleDown", "shrink that"), ("scaleDown", "reduce the beaker"),
        ("scaleDown", "make it tinier"), ("scaleDown", "scale down the flask"), ("scaleDown", "make the flask smaller"),

        ("resizeTo", "make it this big"), ("resizeTo", "make it this tall"), ("resizeTo", "make it this wide"),
        ("resizeTo", "make it this size"), ("resizeTo", "make it this long"), ("resizeTo", "make it this high"),
        ("resizeTo", "make that this big"), ("resizeTo", "make the red beaker this big"),

        ("delete", "delete that"), ("delete", "remove the beaker"), ("delete", "destroy it"),
        ("delete", "erase that"), ("delete", "trash the flask"), ("delete", "get rid of that"), ("delete", "throw it away"),

        ("deleteAll", "delete all test tubes"), ("deleteAll", "remove all beakers"), ("deleteAll", "destroy all flasks"),
        ("deleteAll", "erase all red beakers"), ("deleteAll", "delete everything"), ("deleteAll", "erase everything"),

        ("changeColor", "change it to blue"), ("changeColor", "recolor that red"), ("changeColor", "paint the beaker green"),
        ("changeColor", "color it yellow"), ("changeColor", "tint that orange"), ("changeColor", "dye the flask violet"),
        ("changeColor", "make it red"),

        ("addChemical", "add hydrochloric acid"), ("addChemical", "pour in the sodium chloride"),
        ("addChemical", "fill it with hydrochloric acid"), ("addChemical", "sprinkle some salt"),
        ("addChemical", "insert copper sulfate"), ("addChemical", "dissolve sodium chloride"),

        ("mix", "mix the chemicals"), ("mix", "stir this"), ("mix", "combine them"), ("mix", "blend the contents"),
        ("mix", "agitate it"), ("mix", "shake it"), ("mix", "swirl that"),

        ("deleteSolution", "empty it"), ("deleteSolution", "drain the beaker"), ("deleteSolution", "dump that"),
        ("deleteSolution", "pour it out"), ("deleteSolution", "wash out the flask"), ("deleteSolution", "tip it out"),

        ("burnerOn", "turn on the burner"), ("burnerOn", "burner on"), ("burnerOn", "turn the burner on"),
        ("burnerOn", "switch on the burner"), ("burnerOn", "light the burner"), ("burnerOn", "start the burner"),
        ("burnerOn", "ignite the burner"), ("burnerOn", "fire it up"),

        ("burnerOff", "turn off the burner"), ("burnerOff", "burner off"), ("burnerOff", "switch off the burner"),
        ("burnerOff", "stop the burner"), ("burnerOff", "shut the burner down"), ("burnerOff", "douse the flame"),
        ("burnerOff", "cut the heat"),

        ("putThere", "put that there"), ("putThere", "drop this here"), ("putThere", "set that down"),
        ("putThere", "leave it there"), ("putThere", "position it there"), ("putThere", "stick it here"),

        ("tutorialNext", "next experiment"), ("tutorialNext", "skip this experiment"), ("tutorialNext", "another experiment"),
        ("tutorialNext", "following experiment"), ("tutorialNext", "advance the experiment"), ("tutorialNext", "new experiment"),

        ("tutorialPrevious", "previous experiment"), ("tutorialPrevious", "last experiment"), ("tutorialPrevious", "earlier experiment"),
        ("tutorialPrevious", "prior experiment"), ("tutorialPrevious", "preceding experiment"), ("tutorialPrevious", "the experiment before"),

        ("tutorialShow", "show experiment two"), ("tutorialShow", "open experiment one"), ("tutorialShow", "start experiment three"),
        ("tutorialShow", "begin experiment four"), ("tutorialShow", "load experiment two"), ("tutorialShow", "launch experiment one"),

        // Context-sensitive interaction
        ("filterSet", "consider only beakers"), ("filterSet", "only red ones"), ("filterSet", "focus on flasks"),
        ("filterSet", "just the test tubes"), ("filterSet", "only look at beakers"), ("filterSet", "work with red beakers only"),
        ("filterSet", "focus on the burners"),

        ("filterClear", "consider everything"), ("filterClear", "clear the filter"), ("filterClear", "all objects"),
        ("filterClear", "reset the filter"), ("filterClear", "show everything"), ("filterClear", "no filter"),
        ("filterClear", "remove the filter"), ("filterClear", "delete the filter"),

        ("repeat", "again"), ("repeat", "do that again"), ("repeat", "one more time"),
        ("repeat", "repeat that"), ("repeat", "same again"), ("repeat", "once more"),

        // "them" stays with the normal command, which then uses the last group
        ("changeColor", "make them blue"), ("delete", "delete them"), ("move", "move them there"),
        ("rotate", "rotate them left"), ("scaleUp", "make them bigger"), ("mix", "combine them"),
        ("deleteAll", "delete all of them"), ("deleteAll", "delete all objects"), ("select", "select only the red one"),
        ("rotate", "rotate it again"), ("create", "create one more beaker"),

        // Basics board
        ("basicsShow", "show basics"), ("basicsShow", "show the tutorial"), ("basicsShow", "how does it work"),
        ("basicsShow", "teach me"), ("basicsShow", "show instructions"), ("basicsShow", "open basics"),
        ("basicsHide", "hide basics"), ("basicsHide", "close the tutorial"), ("basicsHide", "hide instructions"),
        ("basicsHide", "hide the tutorial"), ("basicsHide", "close basics"), ("basicsHide", "close instructions"),
        ("lessonNext", "next lesson"), ("lessonNext", "skip lesson"), ("lessonNext", "following lesson"),
        ("lessonNext", "another lesson"), ("lessonNext", "advance the lesson"), ("lessonNext", "skip this lesson"),
        ("lessonPrevious", "previous lesson"), ("lessonPrevious", "last lesson"), ("lessonPrevious", "earlier lesson"),
        ("lessonPrevious", "prior lesson"), ("lessonPrevious", "preceding lesson"), ("lessonPrevious", "the lesson before"),
        ("lessonRestart", "restart lesson"), ("lessonRestart", "reset the lesson"), ("lessonRestart", "redo the lesson"),
        ("lessonRestart", "repeat the lesson"), ("lessonRestart", "retry the lesson"), ("lessonRestart", "lesson again"),
        ("lessonShow", "show lesson three"), ("lessonShow", "open lesson one"), ("lessonShow", "start lesson to"),
        ("lessonShow", "begin lesson for"), ("lessonShow", "load lesson eight"), ("lessonShow", "launch lesson 5"),
        ("boardNext", "next"), ("boardNext", "continue"), ("boardNext", "skip"), ("boardNext", "forward"),
        ("boardNext", "go on"), ("boardNext", "next step"),
        ("boardPrevious", "previous"), ("boardPrevious", "earlier"), ("boardPrevious", "prior"), ("boardPrevious", "before"),
        ("boardPrevious", "one before"), ("boardPrevious", "the one before"), ("boardPrevious", "last step"),
        ("tutorialPrevious", "last experiment"), ("lessonPrevious", "the lesson before"),
        ("boardRestart", "restart"), ("boardRestart", "reset"), ("boardRestart", "redo"), ("boardRestart", "start over"),
        ("boardRestart", "begin again"), ("boardRestart", "try again"),
        ("help", "help"), ("help", "what can I say"), ("help", "what can I do"), ("help", "show commands"),
        ("help", "list commands"), ("help", "commands"),
        ("helpClose", "close help"), ("helpClose", "hide help"), ("helpClose", "exit help"), ("helpClose", "close commands"),
        ("helpClose", "hide commands"), ("helpClose", "done"), ("helpClose", "return"), ("changeColor", "make it black"),

        // Clash checks
        ("create", "create a beaker next to that"), ("move", "move it on the table"), ("filterClear", "reset the filter"),
        ("tutorialShow", "start experiment two"), ("burnerOn", "start the burner"), ("repeat", "again"),
        ("highlight", "show the red beaker"),

        // Moving onto the burner; BurnerCases below checks who moves where
        ("moveToBurner", "move to burner"), ("moveToBurner", "move it to the burner"), ("moveToBurner", "put it on the burner"),
        ("moveToBurner", "carry it to the burner"), ("moveToBurner", "place the beaker on the burner"),
        ("moveToBurner", "move the red beaker to the burner"), ("moveToBurner", "move the burner there"),
        ("moveToBurner", "put the burner there"), ("move", "move that there"), ("putThere", "put that there"),
        ("burnerOn", "turn on the burner"), ("burnerOff", "turn off the burner"),

        // The burner as a target must not be taken by the burner commands
        ("select", "select the burner"), ("select", "pick the burner"), ("select", "select that burner"),
        ("select", "choose this burner"), ("move", "move it there"), ("moveToBurner", "drag the burner here"),
        ("putThere", "put that here"), ("burnerOn", "switch on the burner"), ("burnerOff", "switch off the burner"),
        ("deleteAll", "delete everything"),
    };

    /// <summary>"move ... burner" sentences with a scene situation (what's selected, pointing or not) and the expected decision.</summary>
    public static readonly (string sentence, string selection, bool pointing, string expected)[] BurnerCases =
    {
        ("move to burner",                    "beaker", false, "moveToBurner: target Selection → BurnerStand"),
        ("move the red beaker to the burner", "none",   false, "moveToBurner: target Named (beaker) → BurnerStand"),
        ("put it on the burner",              "beaker", false, "moveToBurner: target Selection → BurnerStand"),
        ("move the burner there",             "beaker", true,  "moveToBurner: target Named (burner) → PointedSpot"),
        ("move to burner",                    "burner", false, "moveToBurner: refused (can't move the burner onto itself)"),
        ("move the burner to the burner",     "beaker", false, "moveToBurner: refused (can't move the burner onto itself)"),
        ("move that to the burner",           "none",   true,  "moveToBurner: target Pointed → BurnerStand"),
        ("move burner",                       "beaker", false, "moveToBurner: target Selection → BurnerStand"), // "to" not heard
        ("carry the red one to the burner",   "beaker", false, "moveToBurner: target Named (by colour) → BurnerStand"),
        ("move the burner there",             "burner", true,  "moveToBurner: target Named (burner) → PointedSpot"),
        ("move the burner there",             "none",   true,  "moveToBurner: target Named (burner) → PointedSpot"),
        ("put the burner here",               "burner", true,  "moveToBurner: target Named (burner) → PointedSpot"),
        ("move to the burner there",          "burner", true,  "moveToBurner: target Selection → PointedSpot"),   // "the" heard as "to"
        ("move to burner there",              "beaker", true,  "moveToBurner: target Pointed → BurnerStand"),
        ("put it on the burner",              "burner", false, "moveToBurner: refused (can't move the burner onto itself)"),
        ("move it to the burner",             "flask",  false, "moveToBurner: target Selection → BurnerStand"),
    };

    /// <summary>Runs all cases and logs PASS/FAIL per case plus a summary. Returns the number of failures.</summary>
    public static int Run()
    {
        int failed = 0;
        foreach (var (expected, sentence) in Cases)
        {
            string got = Resolve(sentence);
            bool ok = got == expected;
            if (!ok) failed++;
            string line = $"[PhrasingSelfCheck] \"{sentence}\" → {got ?? "nothing"} (expected {expected}): {(ok ? "PASS" : "FAIL")}";
            if (ok) Debug.Log(line); else Debug.LogError(line);
        }
        int burnerFailed = 0;
        foreach (var (sentence, selection, pointed, expected) in BurnerCases)
        {
            RecognizedIntent intent = ResolveIntent(sentence);
            string got = intent == null ? "nothing" : intent.Action + ": " + BurnerMoveRule.Decide(
                intent.GetParameter("objectType"), intent.GetParameter("color"),
                !string.IsNullOrEmpty(intent.GetParameter("burnerDest")), !string.IsNullOrEmpty(intent.GetParameter("spatialDest")),
                !string.IsNullOrEmpty(intent.GetParameter("burnerSelf")), pointed && intent.PointingTargets.Count > 0,
                selection != "none", selection == "burner");
            bool ok = got == expected;
            if (!ok) burnerFailed++;
            string line = $"[PhrasingSelfCheck] burner move \"{sentence}\" (selected: {selection}) → {got} (expected {expected}): {(ok ? "PASS" : "FAIL")}";
            if (ok) Debug.Log(line); else Debug.LogError(line);
        }
        failed += burnerFailed;

        int total = Cases.Length + BurnerCases.Length;
        string summary = $"[PhrasingSelfCheck] {total - failed}/{total} passed ({Cases.Length - (failed - burnerFailed)}/{Cases.Length} phrasings, {BurnerCases.Length - burnerFailed}/{BurnerCases.Length} burner moves)";
        if (failed == 0) Debug.Log(summary); else Debug.LogError(summary);
        return failed;
    }

    /// <summary>The interaction the engine fires for the sentence, or null.</summary>
    public static string Resolve(string sentence) => ResolveIntent(sentence)?.Action;

    /// <summary>The intent the engine fires for the sentence (pointing/gesture answered with fake samples), or null.</summary>
    public static RecognizedIntent ResolveIntent(string sentence)
    {
        var speech = new FakeSpeech();
        var pointing = new FakePointing();
        var gesture = new FakeGesture();
        var engine = new FusionEngine(speech, pointing, gesture);
        InteractionSetup.DefineInteractions(engine);

        RecognizedIntent fired = null;
        engine.IntentRecognized += intent => { if (fired == null) fired = intent; };

        foreach (string w in sentence.Split(' ')) speech.Emit(w);
        engine.Tick(engine.BufferTimeoutSeconds + 0.01f);           // end of sentence; longer would also expire the pointing/gesture wait
        pointing.Emit(new PointingSample(null, Vector3.zero));       // in case it waits for pointing
        gesture.Emit(new GestureSample("handDistance", 0.2f)); // in case it waits for the hand distance
        return fired;
    }

    private class FakeSpeech : ISpeechInputSource
    {
        public event Action<SpeechWord> WordRecognized;
        public void Emit(string word) => WordRecognized?.Invoke(new SpeechWord(word));
    }

    private class FakePointing : IPointingInputSource
    {
        public event Action<PointingSample> PointingChanged;
        public void Emit(PointingSample sample) => PointingChanged?.Invoke(sample);
    }

    private class FakeGesture : IGestureInputSource
    {
        public event Action<GestureSample> GestureChanged;
        public void Emit(GestureSample sample) => GestureChanged?.Invoke(sample);
    }
}
