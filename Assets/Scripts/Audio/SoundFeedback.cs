using System;
using UnityEngine;

/// <summary>Feedback sounds: one slot per sound, with a generated fallback clip for empty slots.</summary>
// Object sounds are 3D from a fixed pool, UI sounds 2D; nothing is allocated per play. Chemistry and
// burner sounds come from their events; everything else calls Play/PlayAt, which are no-ops without an instance.
public class SoundFeedback : MonoBehaviour
{
    public enum Sound
    {
        CommandOk, CommandRefused, Create, Delete, Select, Move, Rotate,
        AddChemical, Mix, Precipitate, Fumes, BurnerIgnite, BurnerLoop,
        TutorialStep, ExperimentComplete,
    }

    [Serializable]
    public class Slot
    {
        [Tooltip("Leave empty to use a generated fallback sound.")]
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Minimum seconds between two plays of this sound, so a row of objects doesn't stack into one blast.")]
        public float minInterval = 0.05f;

        public Slot(float volume = 1f) { this.volume = volume; }
    }

    [Header("General")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.6f;
    [SerializeField] private bool mute = false;
    [Tooltip("Number of pooled 3D AudioSources for object sounds.")]
    [SerializeField] private int poolSize = 8;

    [Header("UI (2D)")]
    [SerializeField] private Slot commandOk          = new Slot(0.35f);
    [SerializeField] private Slot commandRefused     = new Slot(0.45f);
    [SerializeField] private Slot tutorialStep       = new Slot(0.5f);
    [SerializeField] private Slot experimentComplete = new Slot(0.6f);

    [Header("Objects (3D)")]
    [SerializeField] private Slot create      = new Slot(0.6f);
    [SerializeField] private Slot delete      = new Slot(0.6f);
    [SerializeField] private Slot select      = new Slot(0.5f);
    [SerializeField] private Slot move        = new Slot(0.5f);
    [SerializeField] private Slot rotate      = new Slot(0.5f);

    [Header("Chemistry (3D)")]
    [SerializeField] private Slot addChemical = new Slot(0.6f);
    [SerializeField] private Slot mix         = new Slot(0.6f);
    [SerializeField] private Slot precipitate = new Slot(0.6f);
    [SerializeField] private Slot fumes       = new Slot(0.5f);
    [SerializeField] private Slot burnerIgnite = new Slot(0.6f);
    [SerializeField] private Slot burnerLoop   = new Slot(0.3f);

    public static SoundFeedback Instance { get; private set; }

    private Slot[] _slots;                 // indexed by Sound
    private AudioClip[] _clips;            // assigned or generated
    private float[] _lastPlayTime;
    private AudioSource _ui;
    private AudioSource[] _pool;
    private float[] _poolBusyUntil;
    private int _poolNext;
    private AudioSource _loop;
    private Transform _loopTarget;

    // Static helpers are safe when no instance exists

    /// <summary>2D sound (ok / refused / tutorial).</summary>
    public static void Play(Sound sound, float delay = 0f)
    {
        if (Instance != null) Instance.PlayInternal(sound, null, delay);
    }

    /// <summary>3D sound at a world position.</summary>
    public static void PlayAt(Sound sound, Vector3 position, float delay = 0f)
    {
        if (Instance != null) Instance.PlayInternal(sound, position, delay);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _slots = new[]
        {
            commandOk, commandRefused, create, delete, select, move, rotate,
            addChemical, mix, precipitate, fumes, burnerIgnite, burnerLoop,
            tutorialStep, experimentComplete,
        };
        _lastPlayTime = new float[_slots.Length];
        for (int i = 0; i < _lastPlayTime.Length; i++) _lastPlayTime[i] = float.NegativeInfinity;

        _clips = new AudioClip[_slots.Length];
        for (int i = 0; i < _slots.Length; i++)
            _clips[i] = _slots[i].clip != null ? _slots[i].clip : ProceduralSounds.Create((Sound)i);

        _ui = CreateSource("SoundFeedback_UI", spatial: false);

        _pool = new AudioSource[Mathf.Max(1, poolSize)];
        _poolBusyUntil = new float[_pool.Length];
        for (int i = 0; i < _pool.Length; i++) _pool[i] = CreateSource($"SoundFeedback_3D_{i}", spatial: true);

        _loop = CreateSource("SoundFeedback_BurnerLoop", spatial: true);
        _loop.loop = true;
        _loop.clip = _clips[(int)Sound.BurnerLoop];
    }

    void OnEnable()
    {
        LabContainer.ChemicalAdded     += OnChemicalAdded;
        LabContainer.Mixed             += OnMixed;
        LabContainer.PrecipitateFormed += OnPrecipitateFormed;
        LabContainer.FumesStarted      += OnFumesStarted;
        BunsenBurner.StateChanged      += OnBurnerStateChanged;
    }

    void OnDisable()
    {
        LabContainer.ChemicalAdded     -= OnChemicalAdded;
        LabContainer.Mixed             -= OnMixed;
        LabContainer.PrecipitateFormed -= OnPrecipitateFormed;
        LabContainer.FumesStarted      -= OnFumesStarted;
        BunsenBurner.StateChanged      -= OnBurnerStateChanged;
        StopBurnerLoop();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_loop == null || !_loop.isPlaying) return;
        // Burner deleted: stop the loop
        if (_loopTarget == null) { StopBurnerLoop(); return; }
        _loop.transform.position = _loopTarget.position;
        _loop.mute = mute;
        _loop.volume = masterVolume * burnerLoop.volume;
    }

    private void OnChemicalAdded(LabContainer c, string id) => PlayAt(Sound.AddChemical, c.transform.position);
    private void OnMixed(LabContainer dest, LabContainer src) => PlayAt(Sound.Mix, dest.transform.position);
    private void OnPrecipitateFormed(LabContainer c)          => PlayAt(Sound.Precipitate, c.transform.position, 0.1f);
    private void OnFumesStarted(LabContainer c)               => PlayAt(Sound.Fumes, c.transform.position);

    private void OnBurnerStateChanged(BunsenBurner burner, bool on)
    {
        if (on)
        {
            PlayAt(Sound.BurnerIgnite, burner.transform.position);
            _loopTarget = burner.transform;
            _loop.transform.position = burner.transform.position;
            _loop.volume = masterVolume * burnerLoop.volume;
            _loop.mute = mute;
            if (!_loop.isPlaying) _loop.PlayDelayed(0.15f);
        }
        else
        {
            StopBurnerLoop();
        }
    }

    private void StopBurnerLoop()
    {
        if (_loop != null && _loop.isPlaying) _loop.Stop();
        _loopTarget = null;
    }

    private void PlayInternal(Sound sound, Vector3? position, float delay)
    {
        if (mute || _clips == null) return;
        int i = (int)sound;
        Slot slot = _slots[i];
        AudioClip clip = _clips[i];
        if (clip == null) return;

        float playTime = Time.time + Mathf.Max(0f, delay);
        if (playTime - _lastPlayTime[i] < slot.minInterval) return;
        _lastPlayTime[i] = playTime;

        float volume = masterVolume * slot.volume;
        if (!position.HasValue)
        {
            if (delay <= 0f) { _ui.PlayOneShot(clip, volume); return; }
            AudioSource src2d = NextPoolSource(clip.length + delay);
            src2d.spatialBlend = 0f;
            Begin(src2d, clip, volume, delay);
            return;
        }

        AudioSource src = NextPoolSource(clip.length + delay);
        src.spatialBlend = 1f;
        src.transform.position = position.Value;
        Begin(src, clip, volume, delay);
    }

    private static void Begin(AudioSource src, AudioClip clip, float volume, float delay)
    {
        src.clip = clip;
        src.volume = volume;
        if (delay > 0f) src.PlayDelayed(delay); else src.Play();
    }

    // A free pooled source, or the one that frees up soonest
    private AudioSource NextPoolSource(float busyFor)
    {
        int best = -1;
        for (int n = 0; n < _pool.Length; n++)
        {
            int i = (_poolNext + n) % _pool.Length;
            if (_poolBusyUntil[i] <= Time.time) { best = i; break; }
        }
        if (best < 0)
        {
            best = 0;
            for (int i = 1; i < _pool.Length; i++)
                if (_poolBusyUntil[i] < _poolBusyUntil[best]) best = i;
        }
        _poolNext = (best + 1) % _pool.Length;
        _poolBusyUntil[best] = Time.time + busyFor;
        return _pool[best];
    }

    private AudioSource CreateSource(string sourceName, bool spatial)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = spatial ? 1f : 0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 1f;
        src.maxDistance = 15f;
        src.dopplerLevel = 0f;
        return src;
    }
}

// Generated fallback clips (mono, 44.1 kHz). Soft envelopes and low amplitudes keep them child-friendly.
internal static class ProceduralSounds
{
    private const int Rate = 44100;

    public static AudioClip Create(SoundFeedback.Sound sound)
    {
        switch (sound)
        {
            case SoundFeedback.Sound.CommandOk:          return Chime("fb_ok", new[] { 880f, 1320f }, 0.09f, 0.35f);
            case SoundFeedback.Sound.CommandRefused:     return Buzz("fb_refused", 140f, 0.25f, 0.25f);
            case SoundFeedback.Sound.Create:             return Pop("fb_create", 650f, 220f, 0.09f, 0.5f);
            case SoundFeedback.Sound.Delete:             return Sweep("fb_delete", 520f, 140f, 0.22f, 0.45f, noise: 0.15f);
            case SoundFeedback.Sound.Select:             return Chime("fb_select", new[] { 1200f }, 0.06f, 0.35f);
            case SoundFeedback.Sound.Move:               return Noise("fb_move", 0.28f, 0.35f, attack: 0.4f, lowpass: 0.08f, seed: 1);
            case SoundFeedback.Sound.Rotate:             return Noise("fb_rotate", 0.14f, 0.3f, attack: 0.2f, lowpass: 0.2f, seed: 2);
            case SoundFeedback.Sound.AddChemical:        return Sweep("fb_add", 300f, 700f, 0.12f, 0.45f, noise: 0f);
            case SoundFeedback.Sound.Mix:                return Bubbles("fb_mix", 5, 0.45f, 0.4f);
            case SoundFeedback.Sound.Precipitate:        return Chime("fb_precipitate", new[] { 1568f, 2093f, 2637f }, 0.08f, 0.25f);
            case SoundFeedback.Sound.Fumes:              return Noise("fb_fumes", 1.4f, 0.3f, attack: 0.15f, lowpass: 0.5f, seed: 3);
            case SoundFeedback.Sound.BurnerIgnite:       return Sweep("fb_ignite", 90f, 60f, 0.35f, 0.5f, noise: 0.6f);
            case SoundFeedback.Sound.BurnerLoop:         return Loop("fb_burner_loop", 1.0f, 0.3f);
            case SoundFeedback.Sound.TutorialStep:       return Chime("fb_step", new[] { 660f, 990f }, 0.1f, 0.35f);
            case SoundFeedback.Sound.ExperimentComplete: return Chime("fb_complete", new[] { 523f, 659f, 784f, 1047f }, 0.13f, 0.4f);
            default:                                     return null;
        }
    }

    private static AudioClip Make(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Env(float t, float length, float attack = 0.01f)
    {
        float a = Mathf.Clamp01(t / Mathf.Max(attack, 1e-4f));
        float r = Mathf.Clamp01((length - t) / (length * 0.6f));
        return a * r * r;
    }

    private static AudioClip Chime(string name, float[] notes, float noteLength, float amp)
    {
        float tail = noteLength * 2.5f;
        int n = (int)((noteLength * (notes.Length - 1) + tail) * Rate);
        var d = new float[n];
        for (int k = 0; k < notes.Length; k++)
        {
            int start = (int)(k * noteLength * Rate);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)Rate;
                if (t > tail) break;
                d[i] += amp * Mathf.Sin(2f * Mathf.PI * notes[k] * t) * Mathf.Exp(-t * 9f) * Mathf.Clamp01(t / 0.005f);
            }
        }
        return Make(name, d);
    }

    private static AudioClip Buzz(string name, float freq, float length, float amp)
    {
        int n = (int)(length * Rate);
        var d = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float sq = Mathf.Sin(2f * Mathf.PI * freq * t) >= 0f ? 1f : -1f;
            lp += (sq - lp) * 0.15f; // soften the edges
            d[i] = amp * lp * Env(t, length, 0.01f);
        }
        return Make(name, d);
    }

    private static AudioClip Pop(string name, float from, float to, float length, float amp)
    {
        int n = (int)(length * Rate);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = Mathf.Lerp(from, to, t / length);
            phase += 2f * Mathf.PI * f / Rate;
            d[i] = amp * Mathf.Sin(phase) * Env(t, length, 0.003f);
        }
        return Make(name, d);
    }

    private static AudioClip Sweep(string name, float from, float to, float length, float amp, float noise)
    {
        var rng = new System.Random(7);
        int n = (int)(length * Rate);
        var d = new float[n];
        float phase = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = Mathf.Lerp(from, to, t / length);
            phase += 2f * Mathf.PI * f / Rate;
            lp += ((float)(rng.NextDouble() * 2.0 - 1.0) - lp) * 0.1f;
            d[i] = amp * ((1f - noise) * Mathf.Sin(phase) + noise * lp * 3f) * Env(t, length, 0.01f);
        }
        return Make(name, d);
    }

    private static AudioClip Noise(string name, float length, float amp, float attack, float lowpass, int seed)
    {
        var rng = new System.Random(seed);
        int n = (int)(length * Rate);
        var d = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            lp += ((float)(rng.NextDouble() * 2.0 - 1.0) - lp) * lowpass;
            d[i] = amp * lp * 2f * Env(t, length, attack * length);
        }
        return Make(name, d);
    }

    private static AudioClip Bubbles(string name, int count, float length, float amp)
    {
        var rng = new System.Random(11);
        int n = (int)(length * Rate);
        var d = new float[n];
        for (int b = 0; b < count; b++)
        {
            int start = (int)(rng.NextDouble() * (length - 0.08f) * Rate);
            float f0 = 250f + (float)rng.NextDouble() * 300f;
            float phase = 0f;
            for (int i = 0; i < (int)(0.08f * Rate) && start + i < n; i++)
            {
                float t = i / (float)Rate;
                phase += 2f * Mathf.PI * (f0 + t * 5000f) / Rate;
                d[start + i] += amp * 0.6f * Mathf.Sin(phase) * Env(t, 0.08f, 0.004f);
            }
        }
        return Make(name, d);
    }

    // Steady amplitude so the loop has no click
    private static AudioClip Loop(string name, float length, float amp)
    {
        var rng = new System.Random(5);
        int n = (int)(length * Rate);
        int fade = Rate / 20;
        var raw = new float[n + fade];
        float lp = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            lp += ((float)(rng.NextDouble() * 2.0 - 1.0) - lp) * 0.04f;
            raw[i] = amp * lp * 4f;
        }
        // Blend the extra tail into the start so the loop point is continuous
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = raw[i];
        for (int i = 0; i < fade; i++)
        {
            float w = i / (float)fade;
            d[i] = raw[i] * w + raw[n + i] * (1f - w);
        }
        return Make(name, d);
    }
}
