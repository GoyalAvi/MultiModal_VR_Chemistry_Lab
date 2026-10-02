// Self-check for create counts and "show experiment N". Run from InputTest's context menu.
using System;
using UnityEngine;
using MMI.Fusion;

/// <summary>Each sentence must give the expected count both in CreateCommandParser and in a real engine.</summary>
public static class CreateCountSelfCheck
{
    private static readonly (string sentence, int expected)[] Cases =
    {
        ("create to test tubes", 2),
        ("create for red beakers", 4),
        ("create a beaker next to that", 1),
        ("create a test tube for mixing", 1),
    };

    public static bool Run()
    {
        bool allPassed = true;
        foreach (var (sentence, expected) in Cases)
        {
            string[] words = sentence.Split(' ');
            int parsed = CreateCommandParser.ParseCount(words);
            int fused = RunThroughEngine(words);
            bool ok = parsed == expected && fused == expected;
            allPassed &= ok;
            string line = $"[CreateCountSelfCheck] \"{sentence}\" → parser {parsed}, engine {fused}, expected {expected}: {(ok ? "PASS" : "FAIL")}";
            if (ok) Debug.Log(line); else Debug.LogError(line);
        }
        foreach (var (sentence, expected) in ExperimentCases)
        {
            int got = RunExperimentThroughEngine(sentence.Split(' '));
            bool ok = got == expected;
            allPassed &= ok;
            string line = $"[CreateCountSelfCheck] \"{sentence}\" → experiment {got}, expected {expected}: {(ok ? "PASS" : "FAIL")}";
            if (ok) Debug.Log(line); else Debug.LogError(line);
        }

        Debug.Log($"[CreateCountSelfCheck] {(allPassed ? "all passed" : "FAILURES — see above")}");
        return allPassed;
    }

    private static readonly (string sentence, int expected)[] ExperimentCases =
    {
        ("show experiment two", 2),
        ("show experiment to", 2),
        ("show experiment for", 4),
        ("show experiment 3", 3),
    };

    private static int RunExperimentThroughEngine(string[] words)
    {
        var speech = new FakeSpeech();
        var engine = new FusionEngine(speech, new FakePointing());
        InteractionSetup.DefineInteractions(engine);

        int result = -1;
        engine.Register("tutorialShow", intent =>
            result = int.TryParse(intent.GetParameter("experimentNumber"), out int n) ? n : 0);
        foreach (string w in words) speech.Emit(w);
        engine.Tick(engine.BufferTimeoutSeconds + 0.01f);
        return result;
    }

    private static int RunThroughEngine(string[] words)
    {
        var speech = new FakeSpeech();
        var pointing = new FakePointing();
        var engine = new FusionEngine(speech, pointing);
        InteractionSetup.DefineInteractions(engine);

        int result = -1;
        engine.Register("create", intent =>
            result = int.TryParse(intent.GetParameter("count"), out int n) ? n : 1);

        foreach (string w in words) speech.Emit(w);
        engine.Tick(engine.BufferTimeoutSeconds + 0.01f);         // end of sentence; a longer tick would also expire the pointing wait
        pointing.Emit(new PointingSample(null, Vector3.zero));    // answers "that"/"there" if the command waits for pointing
        return result;
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
}
