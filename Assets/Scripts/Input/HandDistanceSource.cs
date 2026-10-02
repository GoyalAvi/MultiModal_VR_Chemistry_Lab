using System;
using UnityEngine;
using MMI.Fusion;

/// <summary>Gesture input for "make it this big": the distance between the hands, sent as GestureSample("handDistance").</summary>
// Uses the distance at the moment "this" was said, so the hands can drop before the sentence ends.
// Added at runtime by InputTest, which ticks it after the engine.
public class HandDistanceSource : MonoBehaviour, IGestureInputSource
{
    public const string Kind = "handDistance";

    public event Action<GestureSample> GestureChanged;

    /// <summary>True while a "this big" sentence is pending; BimanualScaler stays out meanwhile.</summary>
    public static bool SizeCommandInProgress => _instance != null && _instance._showing;

    private static HandDistanceSource _instance;

    // Final words arrive 0.5-1 s after speaking, plus the flush, so 2 s could already lose the "this"
    // moment of a longer sentence
    private const float HistorySeconds = 4f;
    private const int Capacity = 640;            // 4 s at up to 160 fps
    private const float MaxSampleAge = 0.5f;     // max age of a sample at the looked-up moment
    private const float PendingTimeout = 6f;     // stop offering a distance this long after the last speech event
    private const float FeedbackSilence = 3f;    // hide the feedback after this much silence

    private readonly float[] _times = new float[Capacity];
    private readonly float[] _distances = new float[Capacity];
    private int _head;
    private int _count;

    private SpeechSourceAdapter _speech;
    private Transform _left, _right;

    private int _firedUtterance = -1;
    private int _sampledUtterance = -1;
    private float _cachedDistance = -1f;
    private bool _showing;

    public void Init(SpeechSourceAdapter speech, Transform left, Transform right)
    {
        _speech = speech;
        _left = left;
        _right = right;
        _instance = this;
        if (_left == null || _right == null)
            Debug.LogWarning("[HandDistanceSource] No hand transforms (assign them on InputTest or BimanualScaler) — \"make it this big\" will be refused.");
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_feedbackRoot != null) Destroy(_feedbackRoot);
    }

    private bool HandsTracked => _left != null && _right != null
                                 && _left.gameObject.activeInHierarchy && _right.gameObject.activeInHierarchy;

    /// <summary>Records this frame's distance and, while a resize sentence waits, offers it to the engine.</summary>
    public void Tick()
    {
        if (HandsTracked)
        {
            _times[_head] = Time.time;
            _distances[_head] = Vector3.Distance(_left.position, _right.position);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        // The engine ignores samples it isn't waiting for, so offering one every frame is harmless
        if (_speech == null || !_speech.HasUtterance || _speech.UtteranceId == _firedUtterance) return;
        if (Time.time - _speech.LastSpeechEventTime > PendingTimeout) return;
        int marker = InteractionSetup.FindSizeMarker(_speech.UtteranceWords);
        if (marker < 0) return;

        if (_sampledUtterance != _speech.UtteranceId)
        {
            _sampledUtterance = _speech.UtteranceId;
            _cachedDistance = SampleForSentence(marker, out string source);
            Debug.Log($"[HandDistanceSource] hand distance {(_cachedDistance >= 0f ? $"{_cachedDistance * 100f:F1} cm" : "none")} ({source})");
        }
        GestureChanged?.Invoke(new GestureSample(Kind, _cachedDistance));
    }

    /// <summary>Any recognized command ends the sentence: no more samples, feedback hidden.</summary>
    public void OnCommandRecognized()
    {
        if (_speech != null) _firedUtterance = _speech.UtteranceId;
        SetFeedbackVisible(false);
    }

    // Distance at "this", else just before the sentence ended, else -1
    private float SampleForSentence(int marker, out string source)
    {
        // "this" is a pointing word, so its time sits in PointingWordTimes at its ordinal
        int ordinal = 0;
        for (int i = 0; i < marker; i++)
            if (Array.IndexOf(InteractionSetup.PointingTriggerWords, _speech.UtteranceWords[i]) >= 0) ordinal++;

        if (ordinal < _speech.PointingWordTimes.Count)
        {
            float wordTime = _speech.PointingWordTimes[ordinal];
            if (TryGetDistanceAt(wordTime, out float d, out float t))
            {
                source = $"at \"this\", sample {t - wordTime:+0.00;-0.00}s";
                return d;
            }
        }
        if (TryGetDistanceAt(_speech.UtteranceEndTime, out float end, out float endT))
        {
            source = $"latest before sentence end, sample {endT - _speech.UtteranceEndTime:+0.00;-0.00}s";
            return end;
        }
        source = "hands not tracked around the sentence";
        return -1f;
    }

    private bool TryGetDistanceAt(float time, out float distance, out float sampleTime)
    {
        for (int n = 0; n < _count; n++)
        {
            int i = (_head - 1 - n + Capacity) % Capacity;
            float t = _times[i];
            if (Time.time - t > HistorySeconds) break;
            if (t > time + 0.001f) continue;
            if (time - t > MaxSampleAge) break;
            distance = _distances[i];
            sampleTime = t;
            return true;
        }
        distance = -1f;
        sampleTime = 0f;
        return false;
    }

    // Feedback: a line between the hands with a live "xx cm" label

    private GameObject _feedbackRoot;
    private LineRenderer _line;
    private HudLabel _label;
    private int _shownCm = -1;

    void LateUpdate()
    {
        bool show = _speech != null && HandsTracked
                    && _speech.UtteranceId != _firedUtterance
                    && Time.time - _speech.LastSpeechEventTime < FeedbackSilence
                    && InteractionSetup.IsResizeCommand(_speech.UtteranceWordsWithHypothesis);
        SetFeedbackVisible(show);
        if (!show) return;

        Vector3 a = _left.position, b = _right.position;
        _line.SetPosition(0, a);
        _line.SetPosition(1, b);

        int cm = Mathf.RoundToInt(Vector3.Distance(a, b) * 100f);
        if (cm != _shownCm)
        {
            _shownCm = cm;
            _label.Text.SetText("{0} cm", cm);   // no string allocation
            _label.Resize();
        }

        _label.Root.position = (a + b) * 0.5f + Vector3.up * 0.06f;
        Camera cam = Camera.main;
        if (cam != null) _label.Face(cam.transform.position);
    }

    private void SetFeedbackVisible(bool visible)
    {
        _showing = visible;
        if (visible && _feedbackRoot == null) BuildFeedback();
        if (_feedbackRoot != null && _feedbackRoot.activeSelf != visible) _feedbackRoot.SetActive(visible);
        if (!visible) _shownCm = -1;
    }

    private void BuildFeedback()
    {
        _feedbackRoot = new GameObject("HandDistanceFeedback");

        _line = _feedbackRoot.AddComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.useWorldSpace = true;
        _line.widthMultiplier = 0.003f;
        _line.numCapVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        Material lineMat = ContainerLabel.RoundedSprite.GetMaterial(3999);
        if (lineMat != null) _line.sharedMaterial = lineMat;
        _line.startColor = _line.endColor = HudLabel.AccentColor;

        _label = new HudLabel("Label", _feedbackRoot.transform, HudLabel.AccentColor);
    }
}
