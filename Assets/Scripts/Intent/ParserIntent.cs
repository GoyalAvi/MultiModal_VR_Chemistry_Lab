// ParserIntent: Inspector settings, shared state, lifecycle and handler registration.
// The rest of the class is split by topic into the ParserIntent.*.cs files.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using MMI.Fusion;

/// <summary>Result of a voice command, as reported by ParserIntent.CommandExecuted.</summary>
public enum CommandResult { Ok, Refused }

public partial class ParserIntent : MonoBehaviour
{
    /// <summary>Raised after every command: action id, Ok/Refused, and the objects it acted on (created ones, else the selection).</summary>
    public static event System.Action<string, CommandResult, IReadOnlyList<GameObject>> CommandExecuted;

    /// <summary>Why the last refused command was refused, or null.</summary>
    public static string LastRefusalMessage { get; private set; }

    private readonly List<GameObject> _commandTargets = new List<GameObject>();
    private string _refusalMessage;

    [Header("Highlight Settings")]
    [SerializeField] private Material highlightMaterial;

    [SerializeField] private float highlightDuration = 3.0f;

    [Header("Spawn Settings")]
    [Tooltip("Prefabs must be named exactly as their object tag, e.g. 'burner'.")]
    [SerializeField] private GameObject[] spawnablePrefabs;

    public enum AirSpawnMode { Float, Drop }

    [Header("Create Placement")]
    [Tooltip("Hits within this distance are used as-is; farther hits are clamped to it along the ray.")]
    [UnityEngine.Serialization.FormerlySerializedAs("maxPointSpawnDistance")]
    [SerializeField] private float maxPointDistance = 3.0f;

    [Tooltip("Ray hits nothing: spawn this far along it (clamped to 0.4..maxPointDistance).")]
    [SerializeField] private float airSpawnDistance = 1.0f;

    [Tooltip("How far below the chosen point a surface may be for the object to be placed on it.")]
    [SerializeField] private float maxDropDistance = 1.5f;

    [Tooltip("No surface below the point: Float = stay there, Drop = fall with physics.")]
    [SerializeField] private AirSpawnMode airSpawnMode = AirSpawnMode.Float;

    [Tooltip("Minimum distance kept between the spawned object and the head.")]
    [SerializeField] private float minHeadDistance = 0.3f;

    [Tooltip("Max age (s) of the buffered pointing sample taken at sentence end.")]
    [SerializeField] private float pointingTimeTolerance = 0.5f;

    [Tooltip("Float mode: an air point only snaps onto a surface this close (m) below it.")]
    [SerializeField] private float snapTolerance = 0.05f;

    [Tooltip("The surface probe starts this far above a hit, so pointing at the table's side still finds its top.")]
    [SerializeField] private float surfaceProbeHeight = 0.3f;

    [Tooltip("How far below a directly hit spot the surface probe still searches.")]
    [SerializeField] private float surfaceProbeDepth = 0.6f;

    [Tooltip("Fallback spawn (no pointing data at all): horizontal distance in front of the viewer.")]
    [SerializeField] private float fallbackForwardDistance = 0.5f;

    [Tooltip("World Y of the table top: fallback spawn height and the plane downward air rays are intersected with.")]
    [SerializeField] private float fallbackTableHeight = 0.8f;

    [Tooltip("Fallback spawn only snaps to a surface within this height of fallbackTableHeight (not the floor).")]
    [SerializeField] private float fallbackSnapTolerance = 0.35f;

    [Tooltip("Horizontal gap kept between a spawned object and its neighbours.")]
    [SerializeField] private float spawnClearance = 0.02f;

    [Header("Create Multiple")]
    [Tooltip("Upper limit for \"create three beakers\"; larger requests are capped (and logged).")]
    [SerializeField] private int maxCreateCount = 5;

    [Tooltip("Gap between neighbouring objects in a created row, added to the object's width.")]
    [SerializeField] private float rowGap = 0.05f;

    [Header("Create Preview")]
    [Tooltip("Shows a marker where a create command will spawn, while it is being spoken.")]
    [SerializeField] private bool showSpawnPreview = true;

    [Tooltip("Optional; falls back to Highlight Material, then the default material.")]
    [SerializeField] private Material previewMaterial;

    [SerializeField] private float previewSize = 0.08f;

    [Tooltip("The preview hides this many seconds after the last speech event if no create fired.")]
    [SerializeField] private float previewTimeout = 6f;

    [Header("Transform Settings")]
    [SerializeField] private float moveSpeed   = 2.0f;

    [Header("Rotation (yaw only, around the object's bounds centre)")]
    [Tooltip("Degrees per \"rotate left/right\" without a spoken angle.")]
    [SerializeField] private float voiceRotateStep = 45f;

    [Tooltip("Duration of a voice rotation animation.")]
    [SerializeField] private float rotateDuration = 0.5f;

    [Tooltip("Twist mode: object yaw degrees per degree of controller roll.")]
    [SerializeField] private float twistGain = 1.5f;

    [Tooltip("Twist mode: controller roll below this is ignored (hand jitter).")]
    [SerializeField] private float twistDeadzone = 3f;

    [Tooltip("Twist mode ends if the trigger isn't pressed within this many seconds.")]
    [SerializeField] private float twistTimeout = 8f;

    [Tooltip("Flip if twisting the controller clockwise turns the object the wrong way.")]
    [SerializeField] private bool invertTwist = false;

    [Tooltip("Optional twist trigger action. If unset, the right controller's trigger is bound in code.")]
    [SerializeField] private InputActionReference twistTriggerAction;

    [SerializeField] private float scaleSpeed  = 0.5f;

    [Tooltip("Scale factor per \"bigger\"/\"smaller\", e.g. 1.25 = 25% per step.")]
    [SerializeField] private float scaleStepFactor = 1.25f;

    [Tooltip("Absolute scale limits for repeated \"bigger\"/\"smaller\".")]
    [SerializeField] private float minVoiceScale = 0.1f;

    [SerializeField] private float maxVoiceScale = 5.0f;

    [Header("Pointing")]
    [Tooltip("Optional. Lets the selection be ignored by the pointing ray so it doesn't block what's behind it.")]
    [SerializeField] private RobustPointingSource pointingSource;

    [Tooltip("Near-miss help: a lab object within this angle of the ray is taken when the ray just misses it.")]
    [SerializeField] private float selectAngleTolerance = 5f;

    [Header("Selection Feedback")]
    [Tooltip("How far the object hops toward the viewer when selected by pointing.")]
    [SerializeField] private float selectNudgeDistance = 0.15f;

    [SerializeField] private float selectNudgeSpeed = 2.0f;

    [Header("Create Orientation")]
    [Tooltip("Turn new objects so their front faces the user. Off by default (containers are round).")]
    [SerializeField] private bool faceUserOnCreate = false;

    // Set by InputTest; without it create only has the engine slot or the fallback
    private SpeechSourceAdapter _speechTiming;

    /// <summary>Gives create the utterance timing and ray history, so pointing during the sentence counts.</summary>
    public void SetCreateInputs(SpeechSourceAdapter speechTiming, RobustPointingSource pointing)
    {
        _speechTiming = speechTiming;
        if (pointingSource == null) pointingSource = pointing;
    }

    /// <summary>The current selection, if any. BimanualScaler reads it.</summary>
    public GameObject SelectedObject => _selectedObject;

    private GameObject _selectedObject = null;

    private Material   _originalSelectedMaterial = null;

    private Material   _currentSelectionMaterialInstance = null; // runtime copy; must be destroyed, not just dropped

    /// <summary>Binds every action to its handler. Call once after InteractionSetup.</summary>
    public void RegisterHandlers(FusionEngine engine)
    {
        RegisterWithFeedback(engine, "select",        HandleSelect);
        RegisterWithFeedback(engine, "create",        HandleCreate);
        RegisterWithFeedback(engine, "highlight",     HandleHighlight);
        RegisterWithFeedback(engine, "move",          HandleMove);
        RegisterWithFeedback(engine, "moveToBurner",  HandleMoveToBurner);
        RegisterWithFeedback(engine, "rotate",        HandleRotate);
        RegisterWithFeedback(engine, "scaleUp",       HandleScaleUp);
        RegisterWithFeedback(engine, "scaleDown",     HandleScaleDown);
        RegisterWithFeedback(engine, "resizeTo",      HandleResizeTo);
        RegisterWithFeedback(engine, "delete",        HandleDelete);
        RegisterWithFeedback(engine, "deleteAll",     HandleDeleteAll);
        RegisterWithFeedback(engine, "changeColor",   HandleChangeColor);
        RegisterWithFeedback(engine, "addChemical",   HandleAddChemical);
        RegisterWithFeedback(engine, "mix",           HandleMix);
        RegisterWithFeedback(engine, "deleteSolution",HandleDeleteSolution);
        RegisterWithFeedback(engine, "burnerOn",      intent => SetBurnerState(intent, true));
        RegisterWithFeedback(engine, "burnerOff",     intent => SetBurnerState(intent, false));
        RegisterWithFeedback(engine, "putThere",      HandlePutThere);
        RegisterWithFeedback(engine, "filterSet",     HandleFilterSet);
        RegisterWithFeedback(engine, "filterClear",   HandleFilterClear);
        RegisterWithFeedback(engine, "repeat",        HandleRepeat);

        // Any recognized command ends the create preview for its utterance ("make it bigger" briefly looks like a create)
        engine.IntentRecognized += _ =>
        {
            HidePreviewForCurrentUtterance();
            EndTwist("new command"); // "rotate this" starts a new twist in its handler
        };
    }

    void Awake()
    {
        // Add sound feedback if the scene has none
        if (FindFirstObjectByType<SoundFeedback>() == null) gameObject.AddComponent<SoundFeedback>();
        _filterBadge = gameObject.AddComponent<FilterBadge>(); // hidden until a filter is set
    }

    private bool _commandRefused;

    // Wraps a handler so the ok chime or the refused buzz plays afterwards
    private void RegisterWithFeedback(FusionEngine engine, string action, System.Action<RecognizedIntent> handler) =>
        engine.Register(action, intent => RunWithFeedback(handler, intent));

    private void RunWithFeedback(System.Action<RecognizedIntent> handler, RecognizedIntent intent)
    {
        _commandRefused = false;
        _refusalMessage = null;
        _commandTargets.Clear();
        handler(intent);
        SoundFeedback.Play(_commandRefused ? SoundFeedback.Sound.CommandRefused : SoundFeedback.Sound.CommandOk);

        // Remember the command for "again" only if it ran, and keep the filter visuals up to date
        if (!_commandRefused)
            _context.RecordCommand(intent, handler, _selectedObject != null ? _selectedObject.name : "none selected");
        RefreshContextVisuals();

        if (_commandTargets.Count == 0 && _selectedObject != null) _commandTargets.Add(_selectedObject);
        LastRefusalMessage = _commandRefused ? _refusalMessage : null;
        if (CommandExecuted != null)
        {
            try { CommandExecuted(intent.Action, _commandRefused ? CommandResult.Refused : CommandResult.Ok, _commandTargets); }
            catch (System.Exception ex) { Debug.LogException(ex); }
        }
    }

    /// <summary>Refuses the running command: logs the message and plays the refused buzz.</summary>
    // The only way a command counts as refused. Other log output (Unity shader, XR or physics warnings)
    // never changes the result.
    private void Refuse(string message, bool warning = true)
    {
        if (warning) Debug.LogWarning(message); else Debug.Log(message);
        _commandRefused = true;
        if (_refusalMessage == null) _refusalMessage = message;
    }

    /// <summary>Refuses the running command without a log line (e.g. "delete all" found nothing).</summary>
    private void MarkRefused() => _commandRefused = true;

    void OnEnable()
    {
        if (twistTriggerAction != null && twistTriggerAction.action != null)
        {
            twistTriggerAction.action.Enable();
            return;
        }
        // No Input Action assigned: bind the right controller's trigger in code
        if (_codeTwistTrigger == null)
        {
            _codeTwistTrigger = new InputAction("TwistTrigger", InputActionType.Button);
            _codeTwistTrigger.AddBinding("<XRController>{RightHand}/{TriggerButton}");
            _codeTwistTrigger.AddBinding("<XRController>{RightHand}/triggerPressed");
        }
        _codeTwistTrigger.Enable();
    }

    void OnDisable()
    {
        _codeTwistTrigger?.Disable();
        EndTwist("disabled");
    }

    void Update()
    {
        UpdateSpawnPreview();
        UpdateTwist();
    }

    void OnDestroy()
    {
        _codeTwistTrigger?.Dispose();
        if (_twistRing != null) Destroy(_twistRing);
        if (_previewPool == null) return;
        foreach (GameObject disc in _previewPool)
            if (disc != null) Destroy(disc);
    }
}
