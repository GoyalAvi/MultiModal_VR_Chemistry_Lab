using System;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;
using hci.mmi.speech.SpeechRecognitionSystem;

/// <summary>Adapts the course speech wrapper to ISpeechInputSource, so the package never references Recognissimo.</summary>
public class SpeechSourceAdapter : ISpeechInputSource
{
    public event Action<SpeechWord> WordRecognized;

    // Utterance timing (Time.time), so a command can be matched with what the user did while saying it.
    // A new utterance starts after a silence gap; hypotheses arrive while talking, final words at the end.
    private const float NewUtteranceGapSeconds = 1.0f;
    private float _lastSpeechEventTime = float.NegativeInfinity;

    public bool  HasUtterance       { get; private set; }
    public float UtteranceStartTime { get; private set; }
    public float UtteranceEndTime   { get; private set; }
    public float LastSpeechEventTime => _lastSpeechEventTime;
    /// <summary>Changes when a new utterance starts.</summary>
    public int   UtteranceId        { get; private set; }

    // Rebuilt on speech events only, never per frame
    private string _recognizedText = "";
    private string _hypothesisText = "";

    public SpeechSourceAdapter(SpeechRecognitionSystem system)
    {
        system.OnRecognized   += OnRecognized;
        system.OnHypothesized += OnHypothesized;
    }

    /// <summary>True if any of the (lower-case) words appears in the utterance so far. No allocations.</summary>
    public bool CurrentUtteranceContains(string[] words)
    {
        foreach (string w in words)
        {
            if (_recognizedText.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (_hypothesisText.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private readonly List<string> _utteranceWords = new List<string>();              // final words
    private readonly List<string> _utteranceWordsWithHypothesis = new List<string>(); // + latest partial

    /// <summary>Final words of the current utterance.</summary>
    public IReadOnlyList<string> UtteranceWords => _utteranceWords;
    /// <summary>Final words plus the latest partial hypothesis, for live feedback.</summary>
    public IReadOnlyList<string> UtteranceWordsWithHypothesis => _utteranceWordsWithHypothesis;

    // When each pointing word was first seen in a partial hypothesis. Much closer to when it was said
    // than the final words, which come in one burst at the end; "put that there" relies on this.
    private readonly List<float> _pointingWordTimes = new List<float>();
    private string[] _pointingWords = { "this", "that", "there", "here" };

    /// <summary>Time.time at which the 1st, 2nd, ... pointing word of the utterance was first heard.</summary>
    public IReadOnlyList<float> PointingWordTimes => _pointingWordTimes;

    /// <summary>Should match the engine's pointing-trigger words.</summary>
    public void SetPointingWords(string[] words) => _pointingWords = words;

    /// <summary>Raised for each final word just before the engine gets it, so the host can adjust the flush delay.</summary>
    public event Action BeforeWordForwarded;

    private void OnHypothesized(object sender, Word word)
    {
        MarkSpeechActivity();
        _hypothesisText = word.text ?? "";
        RebuildWordsWithHypothesis();
    }

    private void OnRecognized(object sender, Word word)
    {
        MarkSpeechActivity();
        HasUtterance = true;
        _recognizedText += " " + word.text;
        _hypothesisText = "";
        AddWords(_utteranceWords, word.text);
        RebuildWordsWithHypothesis();

        BeforeWordForwarded?.Invoke();
        WordRecognized?.Invoke(new SpeechWord(word.text, word.confidence));
    }

    private void MarkSpeechActivity()
    {
        float now = Time.time;
        if (now - _lastSpeechEventTime > NewUtteranceGapSeconds)
        {
            UtteranceStartTime = now;
            UtteranceId++;
            _recognizedText = "";
            _hypothesisText = "";
            _utteranceWords.Clear();
            _utteranceWordsWithHypothesis.Clear();
            _pointingWordTimes.Clear();
        }
        UtteranceEndTime = now;
        _lastSpeechEventTime = now;
    }

    private void RebuildWordsWithHypothesis()
    {
        _utteranceWordsWithHypothesis.Clear();
        _utteranceWordsWithHypothesis.AddRange(_utteranceWords);
        AddWords(_utteranceWordsWithHypothesis, _hypothesisText);

        // A hypothesis revised to fewer pointing words never removes a time
        int pointingWords = 0;
        foreach (string w in _utteranceWordsWithHypothesis)
            if (Array.IndexOf(_pointingWords, w) >= 0) pointingWords++;
        while (_pointingWordTimes.Count < pointingWords) _pointingWordTimes.Add(Time.time);
    }

    private static void AddWords(List<string> into, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (string w in text.Split(' '))
            if (w.Length > 0) into.Add(w.Trim().ToLowerInvariant());
    }
}
