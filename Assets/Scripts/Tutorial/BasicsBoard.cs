using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using MMI.Fusion;

/// <summary>Basics board: eight practice lessons for the core commands, independent of the experiment board.</summary>
// A step ticks when its command succeeds (CommandExecuted, so any phrasing counts), current step only;
// a refused command shows a tip. Same √ ► ○ symbols as the experiment board (the font lacks ✔/▶).
public class BasicsBoard : MonoBehaviour
{
    [Header("Scene Refs (optional — default: this object and its TextMeshPro child)")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text text;

    [Header("Flow")]
    [SerializeField] private bool startVisible = true;
    [Tooltip("Seconds \"Great job!\" stays up before the next lesson.")]
    [SerializeField] private float advanceDelay = 2f;
    [Tooltip("Seconds a tip for a refused command stays up.")]
    [SerializeField] private float tipDuration = 4f;
    [Tooltip("Plain \"next\"/\"previous\"/\"restart\" go to the board within this angle of where the user looks.")]
    [SerializeField] private float gazeAngle = 30f;

    private class Step
    {
        public string Phrase;       // shown in the accent colour
        public string Hint;
        public string Needs;        // what to create first when the command fails for lack of an object
        public Func<string, IReadOnlyList<GameObject>, bool> Matches; // (interaction, targets) of a successful command
        public bool ByBurnerStand;  // ticks on LabContainer.ReachedBurnerStand instead
    }

    private class Lesson
    {
        public string Title;
        public Step[] Steps;
    }

    private Lesson[] _lessons;

    private int _lesson;           // 0-based
    private int _step;             // == Steps.Length when the lesson is finished
    private bool _allDone;
    private bool _helpOpen;
    private string _tip;
    private Coroutine _advance, _tipRoutine;
    private TutorialManager _experiments;
    private ParserIntent _parser;

    private enum Board { Basics, Experiment }
    private Board _lastUsed = Board.Basics;

    private const string Done    = "#66BB6A";   // same palette as the experiment board
    private const string Current = "#FFD54F";
    private const string Pending = "#8A8A8A";
    private const string Command = "#4FC3F7";
    private const string TipColor = "#FFB74D";

    private static string Cmd(string phrase) => $"<color={Command}>\"{phrase}\"</color>";

    // Read by the self-check
    public int LessonNumber => _lesson + 1;
    public int StepIndex => _step;
    public int StepCount => _lessons[_lesson].Steps.Length;
    public bool Visible => panel != null && panel.activeSelf;
    public bool HelpOpen => _helpOpen;

    void Awake()
    {
        _lessons = BuildLessons();
        if (panel == null) panel = gameObject;
        if (text == null) text = GetComponentInChildren<TMP_Text>(true);
    }

    void OnEnable()
    {
        ParserIntent.CommandExecuted    += OnCommandExecuted;
        LabContainer.ReachedBurnerStand += OnReachedBurnerStand;
    }

    void OnDisable()
    {
        ParserIntent.CommandExecuted    -= OnCommandExecuted;
        LabContainer.ReachedBurnerStand -= OnReachedBurnerStand;
    }

    void Start()
    {
        if (_parser == null) _parser = FindFirstObjectByType<ParserIntent>();
        if (_experiments == null) _experiments = FindFirstObjectByType<TutorialManager>();
        SetVisible(startVisible);
        ShowLesson(1);
    }

    /// <summary>No board in the scene: copies the experiment board and places it beside it.</summary>
    // sideOffset is metres to the side as the user sees it (negative = left), at least the board's width.
    public static BasicsBoard CreateNextTo(TutorialManager experiments, float sideOffset, float yawTowardUser)
    {
        GameObject source = experiments != null ? experiments.GuidePanel : null;
        if (source == null)
        {
            Debug.LogWarning("[Basics] No experiment board (TutorialManager.guidePanel) to copy — no basics board.");
            return null;
        }

        Transform s = source.transform;
        GameObject clone = Instantiate(source, s.position, s.rotation, s.parent);
        clone.name = "BasicsBoard (runtime copy)";
        foreach (TutorialManager tm in clone.GetComponentsInChildren<TutorialManager>(true)) DestroyImmediate(tm); // never react twice

        // At least the board's own width, so the two never overlap
        float width = 0f;
        foreach (Renderer r in source.GetComponentsInChildren<Renderer>())
            width = Mathf.Max(width, Mathf.Abs(r.bounds.size.x * s.right.x) + Mathf.Abs(r.bounds.size.z * s.right.z));
        float sign = sideOffset < 0f ? -1f : 1f;
        float side = sign * Mathf.Max(Mathf.Abs(sideOffset), width * 1.1f);

        // The user faces the text side (its +Z points away from them), so +right is the user's right and a
        // positive yaw turns a right-hand board toward them
        clone.transform.position = s.position + s.right * side;
        clone.transform.rotation = Quaternion.AngleAxis(sign * yawTowardUser, Vector3.up) * s.rotation;

        BasicsBoard board = clone.AddComponent<BasicsBoard>();
        board.panel = clone;
        board.text = clone.GetComponentInChildren<TMP_Text>(true);
        Debug.Log($"[Basics] Created the basics board beside the experiment board ({side:+0.00;-0.00} m, {sign * yawTowardUser:+0;-0}°).");
        return board;
    }

    /// <summary>Binds the basics-board and plain-navigation commands.</summary>
    public void RegisterHandlers(FusionEngine engine, TutorialManager experiments, ParserIntent parser)
    {
        _experiments = experiments;
        _parser = parser;

        engine.Register("basicsShow", _ => { SetVisible(true); _lastUsed = Board.Basics; Ok("show basics"); });
        engine.Register("basicsHide", _ => { SetVisible(false); Ok("hide basics"); });
        engine.Register("lessonNext", _ => Navigate(Board.Basics, +1, "next lesson (named)"));
        engine.Register("lessonPrevious", _ => Navigate(Board.Basics, -1, "previous lesson (named)"));
        engine.Register("lessonRestart", _ => Navigate(Board.Basics, 0, "restart lesson (named)"));
        engine.Register("lessonShow", intent =>
        {
            if (int.TryParse(intent.GetParameter("lessonNumber"), out int n) && n >= 1 && n <= _lessons.Length)
            {
                SetVisible(true);
                _lastUsed = Board.Basics;
                ShowLesson(n);
                Ok($"show lesson {n}");
            }
            else Refuse(int.TryParse(intent.GetParameter("lessonNumber"), out int bad)
                        ? $"there is no lesson {bad} (1–{_lessons.Length})" : "no lesson number heard (\"show lesson three\")");
        });
        engine.Register("boardNext",     intent => Navigate(ChooseBoard(intent, out string why), +1, why));
        engine.Register("boardPrevious", intent => Navigate(ChooseBoard(intent, out string why), -1, why));
        engine.Register("boardRestart",  intent => Navigate(ChooseBoard(intent, out string why), 0, why));
        engine.Register("help", _ =>
        {
            SetVisible(true);
            _helpOpen = true;
            Render();
            Ok("help card");
        });
        engine.Register("helpClose", _ =>
        {
            if (!_helpOpen) { Refuse("no help card open (say \"help\")"); return; }
            _helpOpen = false;
            Render();
            Ok("help closed — back to the lesson");
        });

        // The experiment board's own commands make it the board "used last"
        engine.IntentRecognized += intent =>
        {
            if (intent.Action.StartsWith("tutorial")) _lastUsed = Board.Experiment;
        };
    }

    // delta +1 = next, -1 = previous, 0 = restart, on the chosen board only
    private void Navigate(Board board, int delta, string why)
    {
        string what = delta > 0 ? "next" : delta < 0 ? "previous" : "restart";
        if (board == Board.Experiment && _experiments != null)
        {
            if (delta > 0) _experiments.Next();
            else if (delta < 0) _experiments.Previous();
            else _experiments.RestartCurrent();
            _lastUsed = Board.Experiment;
            Ok($"{what} → experiment board ({why}): experiment {_experiments.CurrentExperimentNumber}");
            return;
        }

        SetVisible(true);
        _lastUsed = Board.Basics;
        _helpOpen = false;
        int target = delta == 0 ? _lesson + 1 : Mathf.Clamp(_lesson + 1 + delta, 1, _lessons.Length);
        ShowLesson(target);
        Ok($"{what} → basics board ({why}): lesson {target}");
    }

    // A board named in the sentence, else the one the head looks at, else the one used last
    private Board ChooseBoard(RecognizedIntent intent, out string why)
    {
        string named = intent.GetParameter("boardName");
        if (named == "experiment" && _experiments != null) { why = "named \"experiment\""; return Board.Experiment; }
        if (named == "basics") { why = "named \"lesson\""; return Board.Basics; }

        Camera cam = Camera.main;
        float basics = cam != null && Visible ? AngleTo(cam.transform, panel) : float.MaxValue;
        float experiment = cam != null && _experiments != null && _experiments.GuidePanel != null && _experiments.GuidePanel.activeInHierarchy
            ? AngleTo(cam.transform, _experiments.GuidePanel) : float.MaxValue;
        float best = Mathf.Min(basics, experiment);
        if (best <= gazeAngle)
        {
            why = $"looking at it ({best:F0}°)";
            return basics <= experiment ? Board.Basics : Board.Experiment;
        }
        why = $"used last (looking at neither: basics {Fmt(basics)}, experiment {Fmt(experiment)})";
        return _experiments != null ? _lastUsed : Board.Basics;
    }

    private static string Fmt(float angle) => angle == float.MaxValue ? "n/a" : $"{angle:F0}°";

    private static float AngleTo(Transform head, GameObject board)
    {
        Renderer r = board.GetComponentInChildren<Renderer>();
        Vector3 centre = r != null ? r.bounds.center : board.transform.position;
        return Vector3.Angle(head.forward, centre - head.position);
    }

    private static void Ok(string log)
    {
        Debug.Log($"[Basics] {log}");
        SoundFeedback.Play(SoundFeedback.Sound.CommandOk);
    }

    private static void Refuse(string log)
    {
        Debug.LogWarning($"[Basics] refused — {log}");
        SoundFeedback.Play(SoundFeedback.Sound.CommandRefused);
    }

    public void SetVisible(bool visible)
    {
        if (panel != null) panel.SetActive(visible);
        if (visible) Render();
    }

    /// <summary>Shows a lesson (1-based) from its first step.</summary>
    public void ShowLesson(int number)
    {
        if (_advance != null) { StopCoroutine(_advance); _advance = null; }
        _lesson = Mathf.Clamp(number, 1, _lessons.Length) - 1;
        _step = 0;
        _allDone = false;
        ClearTip();
        Debug.Log($"[Basics] Lesson {_lesson + 1}: {_lessons[_lesson].Title}");
        Render();
    }

    private bool StepActive => Visible && !_allDone && _step < _lessons[_lesson].Steps.Length;

    private void OnCommandExecuted(string action, CommandResult result, IReadOnlyList<GameObject> targets)
    {
        if (!StepActive) return;
        Step step = _lessons[_lesson].Steps[_step];
        if (result == CommandResult.Ok)
        {
            if (!step.ByBurnerStand && step.Matches != null && step.Matches(action, targets)) CompleteStep();
            return;
        }
        ShowTip(TipFor(ParserIntent.LastRefusalMessage, step));
    }

    private void OnReachedBurnerStand(LabContainer container)
    {
        if (StepActive && _lessons[_lesson].Steps[_step].ByBurnerStand) CompleteStep();
    }

    private void CompleteStep()
    {
        Debug.Log($"[Basics] Lesson {_lesson + 1}, step {_step + 1} done.");
        _step++;
        ClearTip();
        Render();
        if (_step < _lessons[_lesson].Steps.Length)
        {
            SoundFeedback.Play(SoundFeedback.Sound.TutorialStep);
            return;
        }
        SoundFeedback.Play(SoundFeedback.Sound.ExperimentComplete); // "Great job!"
        _advance = StartCoroutine(AdvanceAfterDelay());
    }

    private IEnumerator AdvanceAfterDelay()
    {
        yield return new WaitForSeconds(advanceDelay);
        _advance = null;
        if (_lesson < _lessons.Length - 1) ShowLesson(_lesson + 2);
        else { _allDone = true; Render(); }
    }

    private static string TipFor(string refusal, Step step)
    {
        string m = refusal ?? "";
        if (m.Contains("filtered out")) return $"Say {Cmd("consider everything")} to see all objects.";
        if (m.Contains("hands")) return "Hold both controllers up and apart while you say \"this big\".";
        if (m.Contains("Could not find") || m.Contains("nothing selected") || m.Contains("no container")
            || m.Contains("select something") || m.Contains("needs a current selection") || m.Contains("no target pointed at or selected"))
            return $"First say: {Cmd(step.Needs ?? "create a beaker")}";
        if (m.Contains("nothing selectable")) return "Point at a beaker, flask or test tube.";
        if (m.Contains("no target")) return "Point at an object while you say \"that\".";
        return $"That didn't work — try {Cmd(step.Phrase)} again.";
    }

    private void ShowTip(string tip)
    {
        if (_tipRoutine != null) StopCoroutine(_tipRoutine);
        _tip = tip;
        Render();
        _tipRoutine = StartCoroutine(ClearTipLater());
    }

    private IEnumerator ClearTipLater()
    {
        yield return new WaitForSeconds(tipDuration);
        _tipRoutine = null;
        _tip = null;
        Render();
    }

    private void ClearTip()
    {
        if (_tipRoutine != null) { StopCoroutine(_tipRoutine); _tipRoutine = null; }
        _tip = null;
    }

    private void Render()
    {
        if (text == null || _lessons == null || !Visible) return;
        text.text = _helpOpen ? HelpCard() : LessonCard();
    }

    private string LessonCard()
    {
        Lesson lesson = _lessons[_lesson];
        var sb = new StringBuilder();
        sb.AppendLine($"<size=80%><color={Pending}>Basics – Lesson {_lesson + 1} of {_lessons.Length}</color></size>");
        sb.AppendLine($"<size=120%><b>{lesson.Title}</b></size>");
        sb.AppendLine();
        for (int i = 0; i < lesson.Steps.Length; i++)
        {
            Step s = lesson.Steps[i];
            if (i < _step)       sb.AppendLine($"<color={Done}>√</color>  <color={Pending}>Say \"{s.Phrase}\"</color>");
            else if (i == _step) sb.AppendLine($"<color={Current}>►</color>  <b>Say {Cmd(s.Phrase)}</b>\n     <size=75%><color={Pending}>{s.Hint}</color></size>");
            else                 sb.AppendLine($"<color={Pending}>○  Say \"{s.Phrase}\"</color>");
        }
        if (_tip != null)
        {
            sb.AppendLine();
            sb.AppendLine($"<color={TipColor}>Tip: {_tip}</color>");
        }
        if (_step >= lesson.Steps.Length)
        {
            sb.AppendLine();
            sb.AppendLine($"<color={Done}><b>Great job!</b></color>");
            if (_allDone) sb.AppendLine($"<b>You're ready! Try the experiments.</b>  Say {Cmd("show experiment one")}.");
        }
        sb.AppendLine();
        sb.Append($"<size=70%><color={Pending}>Say {Cmd("next lesson")}, {Cmd("previous lesson")}, {Cmd("show lesson three")} or {Cmd("help")}.</color></size>");
        return sb.ToString();
    }

    private static string HelpCard()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<size=120%><b>What you can say</b></size>");
        sb.AppendLine();
        void Line(string what, string example) => sb.AppendLine($"<b>{what}</b>  {Cmd(example)}");
        Line("Create", "create three red beakers there");
        Line("Select", "select that");
        Line("Move", "move it there  /  put that there");
        Line("Rotate", "rotate left  /  turn it around");
        Line("Size", "make it bigger  /  make it this big");
        Line("Colour", "make it blue");
        Line("Delete", "delete this  /  delete all test tubes");
        Line("Chemistry", "add water  /  mix this");
        Line("Burner", "move to burner  /  turn on the burner");
        Line("Focus", "consider only beakers  /  consider everything");
        Line("Repeat", "again");
        Line("Boards", "next lesson  /  next experiment  /  next");
        sb.AppendLine();
        sb.Append($"<size=70%><color={Pending}>Say {Cmd("close help")} or {Cmd("done")} to return to the lesson.</color></size>");
        return sb.ToString();
    }

    private static bool AnyTarget(IReadOnlyList<GameObject> targets, Func<VRObject, bool> test)
    {
        foreach (GameObject go in targets)
        {
            VRObject v = go != null ? go.GetComponent<VRObject>() : null;
            if (v != null && test(v)) return true;
        }
        return false;
    }

    private static int CountTargets(IReadOnlyList<GameObject> targets, string type)
    {
        int n = 0;
        foreach (GameObject go in targets)
        {
            VRObject v = go != null ? go.GetComponent<VRObject>() : null;
            if (v != null && v.objectType == type) n++;
        }
        return n;
    }

    private static Func<string, IReadOnlyList<GameObject>, bool> Is(string action) => (a, _) => a == action;

    private Lesson[] BuildLessons() => new[]
    {
        new Lesson { Title = "Create", Steps = new[]
        {
            new Step { Phrase = "create a beaker", Hint = "It appears on the table in front of you.",
                       Matches = (a, t) => a == "create" && AnyTarget(t, v => v.objectType == "beaker") },
            new Step { Phrase = "create three test tubes there", Hint = "Point at a free spot on the table while you say \"there\".",
                       Matches = (a, t) => a == "create" && CountTargets(t, "testtube") >= 2 },
        }},
        new Lesson { Title = "Select", Steps = new[]
        {
            new Step { Phrase = "select that", Hint = "Point at a beaker, flask or test tube while you say \"that\".",
                       Needs = "create a beaker", Matches = Is("select") },
            new Step { Phrase = "select the red beaker", Hint = "No pointing needed — colour and type find it.",
                       Needs = "create a red beaker",
                       Matches = (a, t) => a == "select" && AnyTarget(t, v => v.objectType == "beaker" && v.objectColor == "red") },
        }},
        new Lesson { Title = "Move", Steps = new[]
        {
            new Step { Phrase = "move it there", Hint = "\"It\" is the selected object — point where it should go.",
                       Needs = "select that", Matches = Is("move") },
            new Step { Phrase = "put that there", Hint = "Point at an object on \"that\", then at the new spot on \"there\".",
                       Needs = "create a beaker", Matches = Is("putThere") },
        }},
        new Lesson { Title = "Rotate & resize", Steps = new[]
        {
            new Step { Phrase = "rotate left", Hint = "The selected object turns 45°.", Needs = "select that", Matches = Is("rotate") },
            new Step { Phrase = "make it this big", Hint = "Hold your hands apart while you say \"this big\".",
                       Needs = "select that", Matches = Is("resizeTo") },
        }},
        new Lesson { Title = "Colour & delete", Steps = new[]
        {
            new Step { Phrase = "make it blue", Hint = "Colours the selected object.", Needs = "select that", Matches = Is("changeColor") },
            new Step { Phrase = "delete this", Hint = "Point at the object while you say \"this\".", Needs = "create a beaker", Matches = Is("delete") },
            new Step { Phrase = "delete all test tubes", Hint = "Removes every test tube you created.",
                       Needs = "create three test tubes", Matches = Is("deleteAll") },
        }},
        new Lesson { Title = "Chemistry", Steps = new[]
        {
            new Step { Phrase = "add water", Hint = "Select a beaker first — its label shows what's inside.",
                       Needs = "select that", Matches = Is("addChemical") },
            new Step { Phrase = "mix this", Hint = "Point at a second container while you say \"this\" — the selected one is poured in.",
                       Needs = "create a beaker", Matches = Is("mix") },
        }},
        new Lesson { Title = "Burner", Steps = new[]
        {
            new Step { Phrase = "move to burner", Hint = "The selected container goes onto the burner's stand.",
                       Needs = "select that", ByBurnerStand = true },
            new Step { Phrase = "turn on the burner", Hint = "Watch the flame.", Matches = Is("burnerOn") },
            new Step { Phrase = "turn off the burner", Hint = "The flame goes out.", Matches = Is("burnerOff") },
        }},
        new Lesson { Title = "Focus", Steps = new[]
        {
            new Step { Phrase = "consider only beakers", Hint = "Everything else fades; pointing only picks beakers.",
                       Matches = (a, _) => a == "filterSet" && _parser != null && _parser.Context.FilterType == "beaker" },
            new Step { Phrase = "consider everything", Hint = "Everything is back to normal.", Matches = Is("filterClear") },
        }},
    };
}
