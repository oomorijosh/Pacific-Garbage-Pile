using System;
using UnityEngine;

/// <summary>Two continuous stereo loops, with surface waves following the exhibit camera's height.</summary>
[DisallowMultipleComponent]
public sealed class GPGPExhibitAudio : MonoBehaviour
{
    public const string MusicSourceName = "Undertow - music";
    public const string WavesSourceName = "Calm waves - surface ambience";

    [Header("Exhibit coordinates")]
    [Tooltip("The continuous water compartment whose local bottom and surface define the fade.")]
    public GPGPWaterCompartment water;
    [Tooltip("The navigating Cyber Canoe CC_HEAD, not the headset room's tracking camera.")]
    public Transform ccHeadTransform;

    [Header("Continuous loops")]
    public AudioClip musicClip;
    public AudioClip wavesClip;
    [Range(0f, 1f)] public float musicVolume = .16f;
    [Range(0f, 1f)] public float wavesMaxVolume = .4f;
    [Tooltip("Fraction of water height kept silent at the bottom. The lowest 8% is the Micro band.")]
    [Range(0f, .95f)] public float silentBottomFraction = .08f;
    [Tooltip("Smoothing time for movement and the initial music fade, in real-time seconds.")]
    [Range(.05f, 3f)] public float mixSmoothSeconds = .65f;

    [SerializeField, HideInInspector] AudioSource musicSource;
    [SerializeField, HideInInspector] AudioSource wavesSource;
    bool initialized;
    bool musicStarted, wavesStarted;
    int musicResumeSample, wavesResumeSample;

    public AudioSource MusicSource => musicSource;
    public AudioSource WavesSource => wavesSource;

    /// <summary>Actual head height in the water's transformed local bounds, clamped bottom to top.</summary>
    public float NormalizedHeight
    {
        get
        {
            if (!water || !ccHeadTransform) return 0f;
            float height = Mathf.Max(.01f, water.size.y);
            return Mathf.Clamp01(water.transform.InverseTransformPoint(ccHeadTransform.position).y / height + .5f);
        }
    }

    public float TargetWaveVolume => water && ccHeadTransform
        ? EvaluateWaveVolume(NormalizedHeight, wavesMaxVolume, silentBottomFraction) : 0f;

    public static float EvaluateWaveVolume(float normalizedHeight, float maximumVolume, float silentBottomFraction = .08f)
    {
        float fadeHeight = Mathf.InverseLerp(Mathf.Clamp(silentBottomFraction, 0f, .95f), 1f, normalizedHeight);
        return Mathf.Clamp01(maximumVolume) * Mathf.SmoothStep(0f, 1f, fadeHeight);
    }

    void Start()
    {
        PrepareSources();
        musicSource.volume = wavesSource.volume = 0f;
        initialized = true;
        musicStarted = musicClip;
        wavesStarted = wavesClip;
        if (musicStarted) musicSource.Play();
        if (wavesStarted) wavesSource.Play();
        if (!water || !ccHeadTransform)
            Debug.LogWarning("GPGP audio needs its water compartment and navigating CC_HEAD; waves remain silent until both are assigned.", this);
    }

    void OnEnable()
    {
        if (!initialized) return;
        musicSource.volume = wavesSource.volume = 0f;
        ResumeSource(musicSource, musicStarted, musicResumeSample);
        ResumeSource(wavesSource, wavesStarted, wavesResumeSample);
    }

    void OnDisable()
    {
        if (!initialized) return;
        CachePlayheads();
        if (musicSource) musicSource.Pause();
        if (wavesSource) wavesSource.Pause();
    }

    static void ResumeSource(AudioSource source, bool previouslyStarted, int resumeSample)
    {
        if (!source || !previouslyStarted || !source.clip) return;
        source.UnPause();
        if (source.isPlaying) return;
        // Deactivating the whole root stops child sources instead of merely pausing them.
        // Restore the last observed playhead before the silent fade-in becomes audible.
        source.Play();
        source.timeSamples = Mathf.Clamp(resumeSample, 0, Mathf.Max(0, source.clip.samples - 1));
    }

    void CachePlayheads()
    {
        // A child may already be stopped when its parent's OnDisable runs. In that
        // case keep the last LateUpdate sample instead of replacing it with zero.
        if (musicSource && musicSource.isPlaying) musicResumeSample = musicSource.timeSamples;
        if (wavesSource && wavesSource.isPlaying) wavesResumeSample = wavesSource.timeSamples;
    }

    // Follow after navigation updates. No UI/category events affect playback or sample position.
    void LateUpdate() => UpdateMix(Time.unscaledDeltaTime);

    public void UpdateMix(float deltaTime)
    {
        if (!musicSource || !wavesSource) return;
        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / Mathf.Max(.01f, mixSmoothSeconds));
        musicSource.volume = SmoothVolume(musicSource.volume, Mathf.Clamp01(musicVolume), blend);
        wavesSource.volume = SmoothVolume(wavesSource.volume, TargetWaveVolume, blend);
        if (water)
            wavesSource.transform.position = water.transform.TransformPoint(new Vector3(0f, Mathf.Max(.01f, water.size.y) * .5f, 0f));
        CachePlayheads();
    }

    static float SmoothVolume(float current, float target, float blend)
    {
        float result = Mathf.Lerp(current, target, blend);
        // End an inaudible smoothing tail at exact zero instead of keeping a residual forever.
        return Mathf.Abs(result - target) < .0001f ? target : result;
    }

    /// <summary>Creates or configures only this component's two owned children. Does not start playback.</summary>
    public void PrepareSources()
    {
        ValidateSourceSlot(musicSource, MusicSourceName);
        ValidateSourceSlot(wavesSource, WavesSourceName);
        musicSource = GetOrCreateSource(musicSource, MusicSourceName);
        wavesSource = GetOrCreateSource(wavesSource, WavesSourceName);
        ConfigureSource(musicSource, musicClip, 64);
        ConfigureSource(wavesSource, wavesClip, 128);
        if (water)
            wavesSource.transform.position = water.transform.TransformPoint(new Vector3(0f, Mathf.Max(.01f, water.size.y) * .5f, 0f));
    }

    void ValidateSourceSlot(AudioSource assigned, string childName)
    {
        if (assigned && (assigned.transform.parent != transform || assigned.gameObject.name != childName))
            throw new InvalidOperationException("Keep the GPGP audio sources directly under their audio root with their original names.");
        int matches = 0;
        foreach (Transform child in transform)
        {
            if (child.name != childName) continue;
            matches++;
            if (child.GetComponents<AudioSource>().Length != 1 || (assigned && child != assigned.transform))
                throw new InvalidOperationException("An unrelated object occupies the GPGP audio source name; it has been preserved.");
        }
        if (matches > 1)
            throw new InvalidOperationException("Duplicate GPGP audio source children have been preserved. Keep one source of each kind.");
    }

    AudioSource GetOrCreateSource(AudioSource assigned, string childName)
    {
        if (assigned) return assigned;
        Transform child = transform.Find(childName);
        if (child) return child.GetComponent<AudioSource>();
        var sourceObject = new GameObject(childName);
        sourceObject.transform.SetParent(transform, false);
        return sourceObject.AddComponent<AudioSource>();
    }

    static void ConfigureSource(AudioSource source, AudioClip clip, int priority)
    {
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;
        source.pitch = 1f;
        source.priority = priority;
        source.panStereo = 0f;
        source.spatialBlend = 0f;
        source.spatialize = false;
        source.dopplerLevel = 0f;
        source.reverbZoneMix = 0f;
    }
}
