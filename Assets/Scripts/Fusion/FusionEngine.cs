using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MMI.Fusion
{
    /// <summary>
    /// App-agnostic fusion engine: buffers words, matches them against the defined interactions, waits for any
    /// pointing/gesture samples they need, then raises IntentRecognized and calls the registered handler.
    /// </summary>
    // Not a MonoBehaviour: the host owns the update loop and calls Tick() every frame.
    public class FusionEngine
    {
        public float BufferTimeoutSeconds = 3.0f;
        public float QuickFlushDelaySeconds = 0.6f;
        public float PointingResolutionTimeoutSeconds = 5.0f;
        public int MaxBufferSize = 5;

        private readonly List<InteractionDefinition> _definitions = new List<InteractionDefinition>();
        private readonly Dictionary<string, Dictionary<string, string>> _vocabulary =
            new Dictionary<string, Dictionary<string, string>>();
        private HashSet<string> _pointingTriggerWords =
            new HashSet<string>(new[] { "this", "that", "there", "here" }, StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Action<RecognizedIntent>> _handlers =
            new Dictionary<string, Action<RecognizedIntent>>();

        /// <summary>Raised for every recognized intent, whether or not a handler is registered.</summary>
        public event Action<RecognizedIntent> IntentRecognized;

        private readonly ISpeechInputSource _speech;
        private readonly IPointingInputSource _pointing;
        private readonly IGestureInputSource _gesture;

        public FusionEngine(ISpeechInputSource speech, IPointingInputSource pointing = null, IGestureInputSource gesture = null)
        {
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            _pointing = pointing;
            _gesture = gesture;

            _speech.WordRecognized += OnWordRecognized;
            if (_pointing != null) _pointing.PointingChanged += OnPointingChanged;
            if (_gesture != null) _gesture.GestureChanged += OnGestureChanged;
        }

        public void DefineInteraction(InteractionDefinition definition) => _definitions.Add(definition);

        /// <summary>Adds a slot whose words map to themselves (colours, object types).</summary>
        public void RegisterVocabulary(string slot, IEnumerable<string> words)
        {
            var map = words.ToDictionary(w => w, w => w, StringComparer.OrdinalIgnoreCase);
            RegisterVocabulary(slot, map);
        }

        /// <summary>Adds a slot whose (possibly multi-word) aliases map to a value, e.g. "hydrochloric acid" -&gt; "hcl".</summary>
        public void RegisterVocabulary(string slot, IDictionary<string, string> aliasToValue)
        {
            if (!_vocabulary.TryGetValue(slot, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _vocabulary[slot] = existing;
            }
            foreach (var kv in aliasToValue) existing[kv.Key] = kv.Value;
        }

        public void SetPointingTriggerWords(IEnumerable<string> words) =>
            _pointingTriggerWords = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _fillerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Words the recognizer should hear but the engine ignores ("to", "the").</summary>
        // Without them a constrained recognizer maps unknown words to the nearest command word, e.g. "to" became "tube".
        public void RegisterFillerWords(IEnumerable<string> words)
        {
            foreach (string w in words) _fillerWords.Add(w);
        }

        /// <summary>All words the engine knows, for restricting the recognizer's vocabulary.</summary>
        public IEnumerable<string> GetAllRecognizedWords()
        {
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in _definitions)
                foreach (var phrase in def.TriggerPhrases)
                    foreach (var w in phrase) words.Add(w);
            foreach (var slot in _vocabulary.Values)
                foreach (var alias in slot.Keys)
                    foreach (var w in alias.Split(' ')) words.Add(w);
            foreach (var w in _pointingTriggerWords) words.Add(w);
            foreach (var w in _fillerWords) words.Add(w);
            return words;
        }

        /// <summary>Calls handler whenever the action is recognized. Several handlers per action are allowed.</summary>
        public void Register(string action, Action<RecognizedIntent> handler)
        {
            if (_handlers.TryGetValue(action, out var existing)) _handlers[action] = existing + handler;
            else _handlers[action] = handler;
        }

        public void Unregister(string action, Action<RecognizedIntent> handler)
        {
            if (!_handlers.TryGetValue(action, out var existing)) return;
            existing -= handler;
            if (existing == null) _handlers.Remove(action);
            else _handlers[action] = existing;
        }

        private readonly List<string> _buffer = new List<string>();
        private float _bufferTimer;

        private bool _awaitingPointing;
        private bool _awaitingGesture;
        private InteractionDefinition _pendingDefinition;
        private Dictionary<string, string> _pendingParameters;
        private readonly List<PointingSample> _pendingPointing = new List<PointingSample>();
        private int _pendingPointingNeeded;
        private GestureSample? _pendingGesture;
        private float _pendingTimer;

        /// <summary>Advances the flush and pointing/gesture timeouts. Call once per frame.</summary>
        public void Tick(float deltaTime)
        {
            if (_buffer.Count > 0)
            {
                _bufferTimer -= deltaTime;
                if (_bufferTimer <= 0f) Flush();
            }

            if (_awaitingPointing || _awaitingGesture)
            {
                _pendingTimer -= deltaTime;
                if (_pendingTimer <= 0f) DropPending();
            }
        }

        private void OnWordRecognized(SpeechWord word)
        {
            string w = word.Text.Trim().ToLowerInvariant();
            if (w.Length == 0) return;

            _buffer.Add(w);
            _bufferTimer = IsBufferComplete(_buffer) ? QuickFlushDelaySeconds : BufferTimeoutSeconds;

            if (_buffer.Count >= MaxBufferSize) Flush();
        }

        // A sentence that already matches and has a parameter or pointing word gets the short flush delay
        private bool IsBufferComplete(List<string> buffer)
        {
            InteractionDefinition def = MatchDefinition(buffer);
            if (def == null) return false;
            return ExtractParameters(buffer, def).Count > 0 || CountPointingTriggers(buffer) > 0;
        }

        private void Flush()
        {
            if (_buffer.Count == 0) return;

            List<string> words = new List<string>(_buffer);
            _buffer.Clear();
            Debug.Log("[FusionEngine] Buffer: " + string.Join(" ", words));

            InteractionDefinition def = MatchDefinition(words);
            if (def == null) return;

            Dictionary<string, string> parameters = ExtractParameters(words, def);
            int pronounCount = CountPointingTriggers(words);
            int waitCount = Math.Min(pronounCount, def.MaxPointingSlots);

            if (waitCount > 0 || def.RequiresGesture)
            {
                _pendingDefinition = def;
                _pendingParameters = parameters;
                _pendingPointing.Clear();
                _pendingPointingNeeded = waitCount;
                _pendingGesture = null;
                _awaitingPointing = waitCount > 0;
                _awaitingGesture = def.RequiresGesture;
                _pendingTimer = PointingResolutionTimeoutSeconds;
            }
            else
            {
                Fire(def, parameters, Array.Empty<PointingSample>(), null);
            }
        }

        private void DropPending()
        {
            _awaitingPointing = false;
            _awaitingGesture = false;
            _pendingDefinition = null;
            _pendingPointing.Clear();
            _pendingGesture = null;
        }

        // Free-space samples have no HitObject, so compare their points instead
        private const float FreeSpaceSameSpotThreshold = 0.05f;

        private void OnPointingChanged(PointingSample sample)
        {
            if (!_awaitingPointing) return;

            // The pointing source fires every frame. Skip repeats of the previous sample so each slot of a
            // multi-slot command needs a new target, not two frames of the same hit.
            if (_pendingPointing.Count > 0)
            {
                PointingSample last = _pendingPointing[_pendingPointing.Count - 1];
                bool sameRealObject = last.HitObject != null && last.HitObject == sample.HitObject;
                bool sameFreeSpaceSpot = last.HitObject == null && sample.HitObject == null
                    && Vector3.Distance(last.HitPoint, sample.HitPoint) < FreeSpaceSameSpotThreshold;
                if (sameRealObject || sameFreeSpaceSpot) return;
            }

            _pendingPointing.Add(sample);
            if (_pendingPointing.Count < _pendingPointingNeeded) return;

            _awaitingPointing = false;
            TryFirePending();
        }

        private void OnGestureChanged(GestureSample sample)
        {
            if (!_awaitingGesture) return;

            _pendingGesture = sample;
            _awaitingGesture = false;
            TryFirePending();
        }

        private void TryFirePending()
        {
            if (_awaitingPointing || _awaitingGesture) return;  // still waiting for the other modality
            if (_pendingDefinition == null) return;

            Fire(_pendingDefinition, _pendingParameters, new List<PointingSample>(_pendingPointing), _pendingGesture);
            _pendingDefinition = null;
            _pendingPointing.Clear();
            _pendingGesture = null;
        }

        private void Fire(InteractionDefinition def, Dictionary<string, string> parameters, IReadOnlyList<PointingSample> pointing, GestureSample? gesture)
        {
            var intent = new RecognizedIntent(def.Action, parameters, pointing, gesture);
            IntentRecognized?.Invoke(intent);
            if (_handlers.TryGetValue(def.Action, out var handler)) handler(intent);
        }

        // Longest fully matched trigger phrase wins; on a tie the interaction defined first wins.
        // So a compound phrase like "burner" + "on" beats a shorter one sharing a word.
        private InteractionDefinition MatchDefinition(List<string> buffer)
        {
            var bufferSet = new HashSet<string>(buffer, StringComparer.OrdinalIgnoreCase);
            InteractionDefinition best = null;
            int bestLength = 0;

            foreach (InteractionDefinition def in _definitions)
            {
                foreach (string[] phrase in def.TriggerPhrases)
                {
                    if (phrase.Length <= bestLength) continue;
                    if (phrase.All(w => bufferSet.Contains(w)))
                    {
                        best = def;
                        bestLength = phrase.Length;
                    }
                }
            }
            return best;
        }

        private Dictionary<string, string> ExtractParameters(List<string> buffer, InteractionDefinition def)
        {
            var result = new Dictionary<string, string>();
            string joined = string.Join(" ", buffer);

            foreach (string slot in def.ParameterSlots)
            {
                if (!_vocabulary.TryGetValue(slot, out var aliases)) continue;

                string found = null;

                // Multi-word aliases first, longest first
                foreach (string alias in aliases.Keys.Where(a => a.Contains(' ')).OrderByDescending(a => a.Length))
                {
                    if (joined.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0) { found = aliases[alias]; break; }
                }

                if (found == null)
                {
                    foreach (string word in buffer)
                        if (aliases.TryGetValue(word, out string value)) { found = value; break; }
                }

                if (found != null) result[slot] = found;
            }
            return result;
        }

        private int CountPointingTriggers(List<string> buffer)
        {
            int count = 0;
            foreach (string w in buffer) if (_pointingTriggerWords.Contains(w)) count++;
            return count;
        }
    }
}
