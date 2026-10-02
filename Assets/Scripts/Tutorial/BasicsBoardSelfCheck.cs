using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;
using MMI.Semantics;

/// <summary>Play-mode self-check for the basics board, using the real handlers with fake speech and pointing.</summary>
// The "next experiment" case moves the experiment board and then shows the original experiment again,
// which restarts its steps.
public static class BasicsBoardSelfCheck
{
    private class Speech : ISpeechInputSource
    {
        public event Action<SpeechWord> WordRecognized;
        public void Emit(string word) => WordRecognized?.Invoke(new SpeechWord(word));
    }

    private class Pointing : IPointingInputSource
    {
        public event Action<PointingSample> PointingChanged;
        public void Emit(PointingSample sample) => PointingChanged?.Invoke(sample);
    }

    public static IEnumerator Run(ParserIntent parser, TutorialManager experiments, BasicsBoard board)
    {
        if (parser == null || experiments == null || board == null)
        {
            Debug.LogError("[BasicsSelfCheck] needs ParserIntent, TutorialManager and a BasicsBoard in Play mode.");
            yield break;
        }

        var speech = new Speech();
        var pointing = new Pointing();
        var engine = new FusionEngine(speech, pointing);
        InteractionSetup.DefineInteractions(engine);
        parser.RegisterHandlers(engine);
        experiments.RegisterHandlers(engine);
        board.RegisterHandlers(engine, experiments, parser);

        // Objects that exist now belong to the user; everything spawned later is removed at the end
        var before = new HashSet<GameObject>();
        foreach (ISemanticEntity e in SemanticRegistry.All) if (e.Owner != null) before.Add(e.Owner);

        int passed = 0, total = 0;
        void Check(string name, bool ok, string detail)
        {
            total++;
            if (ok) passed++;
            string line = $"[BasicsSelfCheck] {name}: {(ok ? "PASS" : "FAIL")} — {detail}";
            if (ok) Debug.Log(line); else Debug.LogError(line);
        }
        Camera cam = Camera.main;
        Vector3 ahead = cam != null ? cam.transform.position + cam.transform.forward * 1.2f : Vector3.forward;
        // Answers a pointing slot with a sample on pointedAt, or free space ahead
        void Say(string sentence, GameObject pointedAt = null)
        {
            foreach (string w in sentence.Split(' ')) speech.Emit(w);
            engine.Tick(engine.BufferTimeoutSeconds + 0.01f); // end of sentence; longer would expire the pointing wait
            Vector3 point = pointedAt != null ? pointedAt.transform.position + Vector3.right * 0.25f : ahead;
            pointing.Emit(new PointingSample(pointedAt, point));
        }

        int expNumber = experiments.CurrentExperimentNumber, expStep = experiments.CurrentStepIndex;
        board.SetVisible(true);

        // Lesson 1 ticks
        board.ShowLesson(1);
        Say("create a beaker");
        bool firstTicked = board.LessonNumber == 1 && board.StepIndex == 1;
        Say("create three test tubes there");
        Check("lesson 1 ticks", firstTicked && board.LessonNumber == 1 && board.StepIndex == board.StepCount,
              $"lesson {board.LessonNumber}, steps done {board.StepIndex}/{board.StepCount}");

        // Lesson 3 ticks (the beaker from lesson 1 is selected)
        GameObject marker = null;
        foreach (ISemanticEntity e in SemanticRegistry.All)
            if (e.Owner != null && !before.Contains(e.Owner) && e.Owner != parser.SelectedObject) { marker = e.Owner; break; }
        board.ShowLesson(3);
        Say("move it there", marker);
        bool moveTicked = board.StepIndex == 1;
        Say("put that there", marker);
        Check("lesson 3 ticks", moveTicked && board.LessonNumber == 3 && board.StepIndex == board.StepCount,
              $"lesson {board.LessonNumber}, steps done {board.StepIndex}/{board.StepCount}");

        // A wrong command doesn't tick
        board.ShowLesson(2);
        Say("rotate left");
        Check("wrong command doesn't tick", board.LessonNumber == 2 && board.StepIndex == 0, $"lesson 2 step {board.StepIndex}");

        // Basics lessons leave the experiment board alone
        Check("basics lessons don't change experiment progress",
              experiments.CurrentExperimentNumber == expNumber && experiments.CurrentStepIndex == expStep,
              $"experiment {experiments.CurrentExperimentNumber} step {experiments.CurrentStepIndex} (was {expNumber} step {expStep})");

        // "next lesson" only moves the basics board
        int lesson = board.LessonNumber;
        Say("next lesson");
        Check("\"next lesson\" moves only the basics board",
              board.LessonNumber == lesson + 1 && experiments.CurrentExperimentNumber == expNumber && experiments.CurrentStepIndex == expStep,
              $"lesson {lesson} → {board.LessonNumber}, experiment {experiments.CurrentExperimentNumber}");

        // "next experiment" only moves the experiment board ("previous" if it's on the last one)
        lesson = board.LessonNumber;
        bool atLast = expNumber >= experiments.ExperimentCount;
        Say(atLast ? "previous experiment" : "next experiment");
        int expected = atLast ? expNumber - 1 : expNumber + 1;
        Check($"\"{(atLast ? "previous" : "next")} experiment\" moves only the experiment board",
              experiments.CurrentExperimentNumber == expected && board.LessonNumber == lesson,
              $"experiment {expNumber} → {experiments.CurrentExperimentNumber}, lesson {board.LessonNumber}");
        experiments.ShowNumber(expNumber);

        // Clean up: spawned objects, basics back to lesson 1
        yield return null;
        var spawned = new List<GameObject>();
        foreach (ISemanticEntity e in SemanticRegistry.All) if (e.Owner != null && !before.Contains(e.Owner)) spawned.Add(e.Owner);
        parser.DeleteQuietly(spawned);
        board.ShowLesson(1);

        string summary = $"[BasicsSelfCheck] {passed}/{total} passed";
        if (passed == total) Debug.Log(summary); else Debug.LogError(summary);
    }
}
