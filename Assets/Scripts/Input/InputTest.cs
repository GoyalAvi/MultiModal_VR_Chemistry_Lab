using UnityEngine;
using hci.mmi.speech.SpeechRecognitionSystem;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using MMI.Fusion;

/// <summary>Composition root: builds the FusionEngine from the speech, pointing and hand-distance sources,
/// applies InteractionSetup, registers the handlers and ticks the engine.</summary>
public class InputTest : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private SpeechRecognitionSystem speechRecognitionSystem;
    [SerializeField] private RobustPointingSource pointingSource;
    [SerializeField] private ParserIntent parserIntent;

    [Header("Create")]
    [Tooltip("Wait this long after the last word when a create sentence has no object type yet.")]
    [SerializeField] private float createTypeWaitTime = 1.2f;

    [Header("Resize (\"make it this big\")")]
    [Tooltip("Optional — left hand/controller transform; defaults to BimanualScaler's.")]
    [SerializeField] private Transform leftHand;
    [Tooltip("Optional — right hand/controller transform; defaults to BimanualScaler's.")]
    [SerializeField] private Transform rightHand;

    [Header("Basics board (only used when the scene has no BasicsBoard)")]
    [Tooltip("Metres beside the experiment board as seen by the user (negative = left), at least the board width.")]
    [SerializeField] private float basicsSideOffset = 1f;
    [Tooltip("Degrees the copy is turned toward the user.")]
    [SerializeField] private float basicsYawTowardUser = 20f;

    [Header("Burner")]
    [Tooltip("Flush delay once a sentence contains a complete burner on/off or \"this big\" phrase.")]
    [SerializeField] private float burnerFlushDelay = 0.05f;

    [Header("UI")]
    [SerializeField] private TMP_Text speechText;

    private FusionEngine _engine;
    private HandDistanceSource _handDistance;
    private TutorialManager _tutorial;
    private BasicsBoard _basics;

    void Awake()
    {
        var speechAdapter = new SpeechSourceAdapter(speechRecognitionSystem);

        // Hands: this component's overrides, else BimanualScaler's
        BimanualScaler scaler = FindFirstObjectByType<BimanualScaler>();
        _handDistance = gameObject.AddComponent<HandDistanceSource>();
        _handDistance.Init(speechAdapter,
                           leftHand != null ? leftHand : scaler != null ? scaler.LeftHand : null,
                           rightHand != null ? rightHand : scaler != null ? scaler.RightHand : null);

        _engine = new FusionEngine(speechAdapter, pointingSource, _handDistance);

        InteractionSetup.DefineInteractions(_engine);
        parserIntent.RegisterHandlers(_engine);

        // Found in the scene, so no extra Inspector reference is needed
        TutorialManager tutorial = FindFirstObjectByType<TutorialManager>();
        if (tutorial != null) tutorial.RegisterHandlers(_engine);
        _tutorial = tutorial;

        // A board placed in the scene is used as-is; otherwise a copy of the experiment board is created
        _basics = FindFirstObjectByType<BasicsBoard>(FindObjectsInactive.Include);
        if (_basics == null && tutorial != null) _basics = BasicsBoard.CreateNextTo(tutorial, basicsSideOffset, basicsYawTowardUser);
        if (_basics != null) _basics.RegisterHandlers(_engine, tutorial, parserIntent);
        parserIntent.SetCreateInputs(speechAdapter, pointingSource); // lets create use pointing from while the sentence was spoken

        // The flush delay is set just before each word reaches the engine, because that's when its timer
        // starts. A create that still lacks its object type ("create two ...") waits longer.
        speechAdapter.SetPointingWords(InteractionSetup.PointingTriggerWords);

        float defaultQuickFlush = _engine.QuickFlushDelaySeconds;
        speechAdapter.BeforeWordForwarded += () =>
        {
            IReadOnlyList<string> words = speechAdapter.UtteranceWords;
            if (InteractionSetup.IsResizeCommand(words))
                _engine.QuickFlushDelaySeconds = Mathf.Min(burnerFlushDelay, defaultQuickFlush); // "make it this big" is complete, no extra wait
            else if (CreateCommandParser.IsCreateMissingType(words))
                _engine.QuickFlushDelaySeconds = Mathf.Max(createTypeWaitTime, defaultQuickFlush);
            else if (InteractionSetup.IsBurnerCommand(words))
                _engine.QuickFlushDelaySeconds = Mathf.Min(burnerFlushDelay, defaultQuickFlush); // "turn on the burner": no extra wait
            else
                _engine.QuickFlushDelaySeconds = defaultQuickFlush;
        };

        ConfigureSpeechVocabulary();
        _engine.IntentRecognized += OnIntentRecognized;
    }

    // The recognizer only gets the app's words plus "[unk]". With open dictation the acoustic model turns
    // words like "testtube" into common English ("best", "the stoop").
    [ContextMenu("Self-check: create counts")]
    void RunCreateCountSelfCheck() => CreateCountSelfCheck.Run();

    [ContextMenu("Self-check: phrasings")]
    void RunPhrasingSelfCheck() => PhrasingSelfCheck.Run();

    [ContextMenu("Self-check: basics board")]
    void RunBasicsSelfCheck()
    {
        if (!Application.isPlaying || parserIntent == null)
        {
            Debug.LogError("[BasicsSelfCheck] Run it in Play mode (it creates and deletes objects in the scene).");
            return;
        }
        parserIntent.StartCoroutine(BasicsBoardSelfCheck.Run(parserIntent, _tutorial, _basics));
    }

    [ContextMenu("Self-check: context")]
    void RunContextSelfCheck()
    {
        if (!Application.isPlaying || parserIntent == null)
        {
            Debug.LogError("[ContextSelfCheck] Run it in Play mode (it creates and deletes objects in the scene).");
            return;
        }
        parserIntent.StartCoroutine(parserIntent.RunContextSelfCheck());
    }

    void ConfigureSpeechVocabulary()
    {
        if (speechRecognitionSystem == null) return;
        Recognissimo.Components.SpeechRecognizer recognizer =
            speechRecognitionSystem.GetComponent<Recognissimo.Components.SpeechRecognizer>();
        if (recognizer == null) return;

        List<string> vocabulary = _engine.GetAllRecognizedWords().ToList();
        vocabulary.Add("[unk]");
        recognizer.Vocabulary = vocabulary.Distinct().ToList();
    }

    void Update()
    {
        _engine.Tick(Time.deltaTime);
        _handDistance.Tick(); // after the engine, so a resizeTo flushed this frame gets its distance this frame
    }

    void OnEnable()
    {
        if (speechRecognitionSystem == null) return;
        speechRecognitionSystem.OnRecognized   += OnSpeechRecognizedForUI;
        speechRecognitionSystem.OnHypothesized += OnSpeechHypothesized;
    }

    void OnDisable()
    {
        if (speechRecognitionSystem == null) return;
        speechRecognitionSystem.OnRecognized   -= OnSpeechRecognizedForUI;
        speechRecognitionSystem.OnHypothesized -= OnSpeechHypothesized;
    }

    // Transcript display only; SpeechSourceAdapter feeds the engine from the same events
    void OnSpeechHypothesized(object sender, Word word)
    {
        if (speechText != null)
            speechText.text = "<color=grey>" + word.text + "</color>";
    }

    void OnSpeechRecognizedForUI(object sender, Word word)
    {
        if (speechText != null)
            speechText.text = word.text;
    }

    void OnIntentRecognized(RecognizedIntent intent)
    {
        _handDistance.OnCommandRecognized();
        Debug.Log("[InputTest] " + intent);
    }
}
