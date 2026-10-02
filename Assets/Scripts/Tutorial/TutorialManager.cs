using System;
using System.Collections;
using System.Text;
using UnityEngine;
using TMPro;
using MMI.Fusion;

/// <summary>Experiment board: one guided experiment at a time, with steps that tick from lab events.</summary>
// Only the current step counts, and only on containers used earlier in the experiment. The font has no
// ✔/▶ or subscript digits, hence √ ► ○ and &lt;sub&gt; tags.
public class TutorialManager : MonoBehaviour
{
    [Header("Scene Refs")]
    [Tooltip("Panel holding the guide text — kept active for the whole session.")]
    [SerializeField] private GameObject guidePanel;
    [SerializeField] private TMP_Text stepText;

    [Header("Flow")]
    [Tooltip("Seconds \"Well done!\" stays up before the next experiment is shown.")]
    [SerializeField] private float advanceDelay = 2f;

    // Events the steps react to

    private enum Kind { ChemicalAdded, Mixed, PrecipitateFormed, ReachedBurnerStand, BurnerOn, FumesFinished, EvaporationFinished }

    private struct LabEvent
    {
        public Kind Kind;
        public LabContainer Container; // destination for Mixed
        public LabContainer Other;     // source for Mixed
        public string Chemical;        // for ChemicalAdded
    }

    private class Step
    {
        public string Text;
        public Func<LabEvent, bool> Matches;   // true = this event completes the step
        public Func<bool> AlreadyDone;         // optional: already true when the step becomes current
    }

    private class Experiment
    {
        public string Title;
        public string Formula;
        public Step[] Steps;
    }

    private Experiment[] _experiments;
    private int _current;
    private int _step;             // == Steps.Length when finished
    private LabContainer _a, _b;   // containers used by the current experiment
    private bool _aAssigned, _bAssigned;
    private bool _allDone;
    private Coroutine _advance;
    private BunsenBurner _burner;

    private const string Done    = "#66BB6A";
    private const string Current = "#FFD54F";
    private const string Pending = "#8A8A8A";
    private const string Command = "#4FC3F7";

    private static string Cmd(string phrase) => $"<color={Command}>\"{phrase}\"</color>";

    // Read by the basics board (copying the look, gaze routing, self-check)
    public GameObject GuidePanel => guidePanel;
    public int ExperimentCount => _experiments != null ? _experiments.Length : 0;
    public int CurrentExperimentNumber => _current + 1;
    /// <summary>Current step (== step count once the experiment is finished).</summary>
    public int CurrentStepIndex => _step;
    /// <summary>Starts the shown experiment over.</summary>
    public void RestartCurrent() => ShowExperiment(_current);

    void Awake() => _experiments = BuildExperiments();

    void OnEnable()
    {
        LabContainer.ChemicalAdded       += OnChemicalAdded;
        LabContainer.Mixed               += OnMixed;
        LabContainer.PrecipitateFormed   += OnPrecipitateFormed;
        LabContainer.ReachedBurnerStand  += OnReachedBurnerStand;
        LabContainer.FumesFinished       += OnFumesFinished;
        LabContainer.EvaporationFinished += OnEvaporationFinished;
        BunsenBurner.StateChanged        += OnBurnerStateChanged;
    }

    void OnDisable()
    {
        LabContainer.ChemicalAdded       -= OnChemicalAdded;
        LabContainer.Mixed               -= OnMixed;
        LabContainer.PrecipitateFormed   -= OnPrecipitateFormed;
        LabContainer.ReachedBurnerStand  -= OnReachedBurnerStand;
        LabContainer.FumesFinished       -= OnFumesFinished;
        LabContainer.EvaporationFinished -= OnEvaporationFinished;
        BunsenBurner.StateChanged        -= OnBurnerStateChanged;
    }

    void Start()
    {
        if (guidePanel != null) guidePanel.SetActive(true);
        _burner = FindFirstObjectByType<BunsenBurner>();
        ShowExperiment(0);
    }

    /// <summary>Binds "next/previous/show experiment" to this board.</summary>
    public void RegisterHandlers(FusionEngine engine)
    {
        engine.Register("tutorialNext",     _ => { Next();     SoundFeedback.Play(SoundFeedback.Sound.CommandOk); });
        engine.Register("tutorialPrevious", _ => { Previous(); SoundFeedback.Play(SoundFeedback.Sound.CommandOk); });
        engine.Register("tutorialShow", intent =>
        {
            if (int.TryParse(intent.GetParameter("experimentNumber"), out int n) && n >= 1 && n <= _experiments.Length)
            {
                ShowNumber(n);
                SoundFeedback.Play(SoundFeedback.Sound.CommandOk);
            }
            else
            {
                if (int.TryParse(intent.GetParameter("experimentNumber"), out int bad)) ShowNumber(bad); // logs the range warning
                else Debug.LogWarning("[Tutorial] show experiment: no experiment number heard.");
                SoundFeedback.Play(SoundFeedback.Sound.CommandRefused);
            }
        });
    }

    public void Next()     => ShowExperiment(Mathf.Min(_current + 1, _experiments.Length - 1));
    public void Previous() => ShowExperiment(Mathf.Max(_current - 1, 0));

    /// <summary>1-based, as spoken ("show experiment three").</summary>
    public void ShowNumber(int number)
    {
        if (number < 1 || number > _experiments.Length)
        {
            Debug.LogWarning($"[Tutorial] There is no experiment {number} (1–{_experiments.Length}).");
            return;
        }
        ShowExperiment(number - 1);
    }

    // Progress on the experiment starts over
    private void ShowExperiment(int index)
    {
        if (_advance != null) { StopCoroutine(_advance); _advance = null; }
        _allDone = false;
        _current = index;
        _step = 0;
        _a = _b = null;
        _aAssigned = _bAssigned = false;
        Debug.Log($"[Tutorial] Experiment {_current + 1}: {_experiments[_current].Title}");
        CheckAlreadyDone();
        Render();
    }

    private void OnChemicalAdded(LabContainer c, string id)     => Handle(new LabEvent { Kind = Kind.ChemicalAdded, Container = c, Chemical = id });
    private void OnMixed(LabContainer dest, LabContainer src)   => Handle(new LabEvent { Kind = Kind.Mixed, Container = dest, Other = src });
    private void OnPrecipitateFormed(LabContainer c)            => Handle(new LabEvent { Kind = Kind.PrecipitateFormed, Container = c });
    private void OnReachedBurnerStand(LabContainer c)           => Handle(new LabEvent { Kind = Kind.ReachedBurnerStand, Container = c });
    private void OnFumesFinished(LabContainer c)                => Handle(new LabEvent { Kind = Kind.FumesFinished, Container = c });
    private void OnEvaporationFinished(LabContainer c)          => Handle(new LabEvent { Kind = Kind.EvaporationFinished, Container = c });
    private void OnBurnerStateChanged(BunsenBurner b, bool on)
    {
        if (on) Handle(new LabEvent { Kind = Kind.BurnerOn });
    }

    private void Handle(LabEvent e)
    {
        if (_experiments == null || _allDone) return;
        Experiment exp = _experiments[_current];
        if (_step >= exp.Steps.Length) return; // finished, waiting to advance

        // A container the experiment relies on was deleted: start over
        if ((_aAssigned && _a == null) || (_bAssigned && _b == null))
        {
            Debug.Log("[Tutorial] A container used by this experiment is gone — starting the experiment over.");
            ShowExperiment(_current);
            return;
        }

        if (!exp.Steps[_step].Matches(e)) return;
        CompleteStep();
    }

    private void CompleteStep()
    {
        Experiment exp = _experiments[_current];
        Debug.Log($"[Tutorial] Experiment {_current + 1}, step {_step + 1} done.");
        _step++;
        CheckAlreadyDone();
        Render();

        if (_step >= exp.Steps.Length)
        {
            SoundFeedback.Play(SoundFeedback.Sound.ExperimentComplete); // "Well done!"
            _advance = StartCoroutine(AdvanceAfterDelay());
        }
        else
        {
            SoundFeedback.Play(SoundFeedback.Sound.TutorialStep);
        }
    }

    // Ticks steps that are already satisfied when they become current (e.g. burner already on)
    private void CheckAlreadyDone()
    {
        Experiment exp = _experiments[_current];
        while (_step < exp.Steps.Length && exp.Steps[_step].AlreadyDone != null && exp.Steps[_step].AlreadyDone())
        {
            Debug.Log($"[Tutorial] Experiment {_current + 1}, step {_step + 1} already satisfied.");
            _step++;
        }
    }

    private IEnumerator AdvanceAfterDelay()
    {
        yield return new WaitForSeconds(advanceDelay);
        _advance = null;
        if (_current < _experiments.Length - 1)
        {
            ShowExperiment(_current + 1);
        }
        else
        {
            _allDone = true;
            Render();
        }
    }

    private bool RememberA(LabContainer c) { _a = c; _aAssigned = true; return true; }
    private bool RememberB(LabContainer c) { _b = c; _bAssigned = true; return true; }
    private bool IsA(LabContainer c) => _aAssigned && c != null && c == _a;

    private bool BurnerOnAndAOnStand() =>
        _burner != null && _burner.IsOn && _aAssigned && _a != null && _a.IsOnBurnerStand;

    private Experiment[] BuildExperiments() => new[]
    {
        new Experiment
        {
            Title = "Acid–Base Neutralization",
            Formula = "HCl + NaOH → NaCl + H<sub>2</sub>O",
            Steps = new[]
            {
                new Step { Text = $"Select a container and say {Cmd("add HCl")}.",
                           Matches = e => e.Kind == Kind.ChemicalAdded && e.Chemical == "hcl" && RememberA(e.Container) },
                new Step { Text = $"Select another container and say {Cmd("add NaOH")}.",
                           Matches = e => e.Kind == Kind.ChemicalAdded && e.Chemical == "naoh" && e.Container != _a && RememberB(e.Container) },
                new Step { Text = $"Select the HCl container, then point at the NaOH container and say {Cmd("mix this")} — it turns green (neutral, pH 7).",
                           Matches = e => e.Kind == Kind.Mixed &&
                                          ((IsA(e.Other) && e.Container == _b) || (IsA(e.Container) && e.Other == _b)) },
            },
        },
        new Experiment
        {
            Title = "Ammonium Chloride Smoke",
            Formula = "HCl + NH<sub>3</sub> → NH<sub>4</sub>Cl  → (heat) →  HCl(g) + NH<sub>3</sub>(g)",
            Steps = new[]
            {
                new Step { Text = $"Select a container and say {Cmd("add HCl")}.",
                           Matches = e => e.Kind == Kind.ChemicalAdded && e.Chemical == "hcl" && RememberA(e.Container) },
                new Step { Text = $"Say {Cmd("add ammonia")} — the label shows NH<sub>4</sub>Cl.",
                           Matches = e => e.Kind == Kind.ChemicalAdded && e.Chemical == "nh3" && IsA(e.Container) },
                new Step { Text = $"With that container selected, say {Cmd("move to burner")}.",
                           Matches = e => e.Kind == Kind.ReachedBurnerStand && IsA(e.Container),
                           AlreadyDone = () => _aAssigned && _a != null && _a.IsOnBurnerStand },
                new Step { Text = $"Say {Cmd("turn on the burner")}.",
                           Matches = e => e.Kind == Kind.BurnerOn && _aAssigned && _a != null && _a.IsOnBurnerStand,
                           AlreadyDone = BurnerOnAndAOnStand },
                new Step { Text = "Wait about 8 seconds — the solution fumes and empties.",
                           Matches = e => e.Kind == Kind.FumesFinished && IsA(e.Container) },
            },
        },
        new Experiment
        {
            Title = "Silver Chloride Precipitate",
            Formula = "AgNO<sub>3</sub> + NaCl → AgCl(s)↓ + NaNO<sub>3</sub>",
            Steps = new[]
            {
                new Step { Text = $"Select a container and say {Cmd("add silver nitrate")}.",
                           Matches = e => e.Kind == Kind.ChemicalAdded && e.Chemical == "agno3" && RememberA(e.Container) },
                new Step { Text = $"Say {Cmd("add salt")} — a white precipitate forms instantly.",
                           Matches = e => e.Kind == Kind.PrecipitateFormed && IsA(e.Container) },
            },
        },
        new Experiment
        {
            Title = "Slaking Quicklime",
            Formula = "CaO + H<sub>2</sub>O → Ca(OH)<sub>2</sub> + heat",
            Steps = new[]
            {
                new Step { Text = $"Select a container and say {Cmd("add quicklime")}.",
                           Matches = e => e.Kind == Kind.ChemicalAdded && e.Chemical == "cao" && RememberA(e.Container) },
                new Step { Text = $"Say {Cmd("add water")} — Ca(OH)<sub>2</sub> settles out and the reaction's own heat boils the water away in about 5 seconds (no burner needed).",
                           Matches = e => e.Kind == Kind.EvaporationFinished && IsA(e.Container) },
            },
        },
    };

    private void Render()
    {
        if (stepText == null) return;
        Experiment exp = _experiments[_current];
        var text = new StringBuilder();

        text.AppendLine($"<size=80%><color={Pending}>Experiment {_current + 1} of {_experiments.Length}</color></size>");
        text.AppendLine($"<size=120%><b>{exp.Title}</b></size>");
        text.AppendLine(exp.Formula);
        text.AppendLine();

        for (int i = 0; i < exp.Steps.Length; i++)
        {
            if (i < _step)       text.AppendLine($"<color={Done}>√</color>  <color={Pending}>{exp.Steps[i].Text}</color>");
            else if (i == _step) text.AppendLine($"<color={Current}>►</color>  <b>{exp.Steps[i].Text}</b>");
            else                 text.AppendLine($"<color={Pending}>○  {exp.Steps[i].Text}</color>");
        }

        if (_step >= exp.Steps.Length)
        {
            text.AppendLine();
            text.AppendLine($"<color={Done}><b>Well done!</b></color>");
            if (_allDone)
                text.AppendLine($"All {_experiments.Length} experiments complete. Say {Cmd("show experiment one")} to start again.");
        }

        text.AppendLine();
        text.Append($"<size=70%><color={Pending}>Say {Cmd("next experiment")}, {Cmd("previous experiment")} or {Cmd("show experiment three")}.</color></size>");
        stepText.text = text.ToString();
    }
}
