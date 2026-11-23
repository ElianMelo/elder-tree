using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace ElderTree.Audio
{
    public enum Sfx
    {
        Knock, ImpactHeavy, Slam, WhooshLight, WhooshHeavy, Footstep, Dodge, Jump, Land, DummyHit,
        LockOn, LockOff, ChargeUp, ChargeRelease, UIMove, UISelect, UIBack, Felled, Guard, Stagger, Title,
        BowDraw, BowRelease, BowNock, ArrowHit, FullDraw,
        Explosion, BombWhistle, BombBeep, CrocRoar, JawSnap, MetalHit, PlayerHurt, Skid, YouDied, GreatEnemyFelled, BombPop,
        Deflect, RockBreak, CannonFire, MaskOn, MaskScream, Transform, ManaEmpty, MaskReceived,
        SharkRoar, SneakerSqueak, Burrow,
        BladeWhoosh, BladeHit, VineCut, VineRustle, CrossbowFire, CrossbowReload, SpinWhirl,
        BladeRing, GroundGrind,
        WoodGroan, RootSnap, DeepRumble,
        SpellGather, VineCast, VineBurst, OrbCharge, OrbFire, OrbImpact,
        PotionDrink
    }

    /// <summary>
    /// Mix layers. Each has its own volume, an optional AudioMixerGroup output and a voice priority, so the layers
    /// stack on top of each other instead of cutting each other off: a burst of footsteps can never silence a boss
    /// roar, and a menu click never steals the music.
    /// </summary>
    public enum AudioLayer { SFX, UI, Footsteps, Ambience, Music }

    /// <summary>What the music should be doing. Bosses switch to <see cref="Boss"/> for the fight.</summary>
    public enum MusicMood { None, Explore, Boss }

    /// <summary>
    /// The game's audio: pooled one-shots with per-cue variation (random clip, pitch, volume), mixed in layers,
    /// plus the ambience bed and crossfading background music.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Serializable]
        public class Cue
        {
            public Sfx id;
            public AudioLayer layer = AudioLayer.SFX;
            public AudioClip[] clips = Array.Empty<AudioClip>();
            [Range(0, 1)] public float volume = 0.8f;
            public float pitchMin = 0.95f, pitchMax = 1.05f;
            [Tooltip("0 = 2D, 1 = fully positional")] [Range(0, 1)] public float spatial = 0.7f;
            public float minInterval = 0.03f;
            [Tooltip("How many copies of this cue may ring at once (0 = the manager's default).")]
            public int maxVoices;
        }

        /// <summary>A looping music track. Tracks fade in and out, so the loop restarts with a crossfade before the tail.</summary>
        [Serializable]
        public class MusicTrack
        {
            public AudioClip clip;
            [Range(0, 1)] public float volume = 0.4f;
            [Tooltip("Where a loop restarts (s).")] public float loopStart;
            [Tooltip("Where the music ends before its fade-out tail (s); 0 = the clip's end.")] public float loopEnd;
        }

        public Cue[] cues = Array.Empty<Cue>();

        [Header("Layers")]
        [Range(0, 1)] public float masterVolume = 1f;
        [Range(0, 1)] public float sfxVolume = 1f;
        [Range(0, 1)] public float uiVolume = 1f;
        [Range(0, 1)] public float footstepVolume = 1f;
        [Range(0, 1)] public float ambienceVolume = 0.3f;
        [Range(0, 1)] public float musicVolume = 1f;
        [Tooltip("Optional mixer routing per layer (leave empty to play straight to the listener).")]
        public AudioMixerGroup sfxOutput, uiOutput, footstepOutput, ambienceOutput, musicOutput;

        [Header("Voices")]
        [Tooltip("One-shot voices shared by SFX, UI and footsteps.")]
        public int voices = 40;
        [Tooltip("Default limit of simultaneous copies of one cue.")]
        public int maxVoicesPerCue = 6;
        public int maxFootstepVoices = 4;
        public int maxUIVoices = 6;

        [Header("Ambience")]
        public AudioClip ambienceLoop;

        [Header("Music")]
        public MusicTrack exploreMusic = new();
        public MusicTrack bossMusic = new();
        [Tooltip("Crossfade between tracks and around each loop (s).")]
        public float musicCrossfade = 4f;
        public MusicMood startMood = MusicMood.Explore;

        public static AudioManager Instance { get; private set; }

        /// <summary>Raised for every one-shot that starts (debug overlays, tests).</summary>
        public static event Action<Sfx, AudioLayer> Played;

        class Voice
        {
            public AudioSource src;
            public AudioLayer layer;
            public Sfx id;
            public float startedAt;
        }

        class MusicVoice
        {
            public AudioSource src;
            public MusicTrack track;
            public float gain, target;
        }

        readonly Dictionary<Sfx, Cue> map = new();
        readonly Dictionary<Sfx, float> lastPlayed = new();
        readonly Dictionary<Sfx, int> lastIndex = new();
        Voice[] pool;
        AudioSource ambience;
        readonly MusicVoice[] music = new MusicVoice[2];
        int musicCur;
        MusicMood mood;

        // lower = more important (Unity keeps high-priority sources when real voices run out)
        static int Priority(AudioLayer l) => l switch
        {
            AudioLayer.Music => 0,
            AudioLayer.Ambience => 16,
            AudioLayer.UI => 32,
            AudioLayer.SFX => 128,
            _ => 200,
        };

        // which voices get stolen first when the pool is full
        static int StealRank(AudioLayer l) => l switch { AudioLayer.Footsteps => 0, AudioLayer.SFX => 1, _ => 2 };

        AudioMixerGroup Output(AudioLayer l) => l switch
        {
            AudioLayer.UI => uiOutput,
            AudioLayer.Footsteps => footstepOutput,
            AudioLayer.Ambience => ambienceOutput,
            AudioLayer.Music => musicOutput,
            _ => sfxOutput,
        };

        float LayerVolume(AudioLayer l) => l switch
        {
            AudioLayer.UI => uiVolume,
            AudioLayer.Footsteps => footstepVolume,
            AudioLayer.Ambience => ambienceVolume,
            AudioLayer.Music => musicVolume,
            _ => sfxVolume,
        };

        void Awake()
        {
            Instance = this;
            foreach (var c in cues) map[c.id] = c;
            pool = new Voice[Mathf.Max(8, voices)];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject($"Voice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 3f;
                src.maxDistance = 45f;
                src.dopplerLevel = 0f;
                pool[i] = new Voice { src = src };
            }
            if (ambienceLoop != null)
            {
                ambience = gameObject.AddComponent<AudioSource>();
                ambience.clip = ambienceLoop;
                ambience.loop = true;
                ambience.spatialBlend = 0f;
                ambience.priority = Priority(AudioLayer.Ambience);
                ambience.outputAudioMixerGroup = ambienceOutput;
                ambience.volume = ambienceVolume;
                ambience.Play();
            }
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject($"Music{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                src.priority = Priority(AudioLayer.Music);
                src.outputAudioMixerGroup = musicOutput;
                src.ignoreListenerPause = true;
                music[i] = new MusicVoice { src = src };
            }
            mood = startMood;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetMaster(float v)
        {
            masterVolume = v;
            AudioListener.volume = v;
        }

        public static void Play(Sfx id, Vector3 pos, float volumeScale = 1f, float pitchScale = 1f)
        {
            if (Instance == null) return;
            Instance.PlayInternal(id, pos, volumeScale, pitchScale);
        }

        public static void Play2D(Sfx id, float volumeScale = 1f, float pitchScale = 1f)
        {
            if (Instance == null) return;
            Instance.PlayInternal(id, Vector3.zero, volumeScale, pitchScale, force2D: true);
        }

        /// <summary>Switch the background music (crossfades). Bosses call this when their fight starts and ends.</summary>
        public static void SetMusic(MusicMood m)
        {
            if (Instance != null) Instance.mood = m;
        }

        public static MusicMood CurrentMusic => Instance != null ? Instance.mood : MusicMood.None;

        void PlayInternal(Sfx id, Vector3 pos, float volumeScale, float pitchScale, bool force2D = false)
        {
            if (!map.TryGetValue(id, out var cue) || cue.clips.Length == 0) return;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(id, out var t) && now - t < cue.minInterval) return;
            lastPlayed[id] = now;

            // avoid repeating the same variation twice in a row
            int idx = UnityEngine.Random.Range(0, cue.clips.Length);
            if (cue.clips.Length > 1 && lastIndex.TryGetValue(id, out var li) && li == idx) idx = (idx + 1) % cue.clips.Length;
            lastIndex[id] = idx;

            var v = Acquire(cue);
            var src = v.src;
            src.Stop();
            src.transform.position = pos;
            src.clip = cue.clips[idx];
            src.spatialBlend = force2D ? 0f : cue.spatial;
            src.volume = cue.volume * volumeScale * LayerVolume(cue.layer);
            src.pitch = UnityEngine.Random.Range(cue.pitchMin, cue.pitchMax) * pitchScale;
            src.priority = Priority(cue.layer);
            src.outputAudioMixerGroup = Output(cue.layer);
            src.Play();
            v.layer = cue.layer;
            v.id = id;
            v.startedAt = now;
            Played?.Invoke(id, cue.layer);
        }

        /// <summary>
        /// Pick a voice: respect the per-cue and per-layer limits (re-using the voice of that group closest to
        /// finishing), otherwise take a free voice, otherwise steal from the least important layer (footsteps, then
        /// SFX, then UI) the voice with the least left to play. Long stings and tails are therefore the last to go.
        /// </summary>
        Voice Acquire(Cue cue)
        {
            int cueCap = cue.maxVoices > 0 ? cue.maxVoices : maxVoicesPerCue;
            int layerCap = cue.layer == AudioLayer.Footsteps ? maxFootstepVoices : cue.layer == AudioLayer.UI ? maxUIVoices : int.MaxValue;
            int cueCount = 0, layerCount = 0;
            Voice endingCue = null, endingLayer = null, free = null, steal = null;
            foreach (var v in pool)
            {
                if (!v.src.isPlaying) { free ??= v; continue; }
                if (v.id == cue.id && v.layer == cue.layer)
                {
                    cueCount++;
                    if (endingCue == null || Remaining(v) < Remaining(endingCue)) endingCue = v;
                }
                if (v.layer == cue.layer)
                {
                    layerCount++;
                    if (endingLayer == null || Remaining(v) < Remaining(endingLayer)) endingLayer = v;
                }
                if (steal == null || StealRank(v.layer) < StealRank(steal.layer)
                    || (StealRank(v.layer) == StealRank(steal.layer) && Remaining(v) < Remaining(steal))) steal = v;
            }
            if (cueCount >= cueCap && endingCue != null) return endingCue;
            if (layerCount >= layerCap && endingLayer != null) return endingLayer;
            return free ?? steal ?? pool[0];
        }

        static float Remaining(Voice v) =>
            v.src.clip == null ? 0f : (v.src.clip.length - v.src.time) / Mathf.Max(0.01f, Mathf.Abs(v.src.pitch));

        /// <summary>How many one-shot voices are sounding right now (debug).</summary>
        public int ActiveVoices
        {
            get { int n = 0; if (pool != null) foreach (var v in pool) if (v.src.isPlaying) n++; return n; }
        }

        void Update()
        {
            if (ambience != null)
            {
                ambience.volume = ambienceVolume;
                ambience.outputAudioMixerGroup = ambienceOutput;
            }
            UpdateMusic(Time.unscaledDeltaTime);
        }

        MusicTrack TrackFor(MusicMood m)
        {
            var t = m switch { MusicMood.Explore => exploreMusic, MusicMood.Boss => bossMusic, _ => null };
            return t != null && t.clip != null ? t : null;
        }

        void StartTrack(MusicTrack t)
        {
            music[musicCur].target = 0f;
            musicCur = 1 - musicCur;
            var v = music[musicCur];
            v.track = t;
            v.gain = 0f;
            v.target = 1f;
            v.src.clip = t.clip;
            v.src.time = Mathf.Clamp(t.loopStart, 0f, t.clip.length - 0.1f);
            v.src.Play();
        }

        void UpdateMusic(float dt)
        {
            float fade = dt / Mathf.Max(0.1f, musicCrossfade);
            foreach (var v in music)
            {
                if (v == null) return;
                v.gain = Mathf.MoveTowards(v.gain, v.target, fade);
                v.src.volume = v.track != null ? v.gain * v.track.volume * musicVolume : 0f;
                v.src.outputAudioMixerGroup = musicOutput;
                if (v.target <= 0f && v.gain <= 0f && v.src.isPlaying) v.src.Stop();
            }

            var want = TrackFor(mood);
            var cur = music[musicCur];
            if (want == null) { cur.target = 0f; return; }
            if (cur.track != want || cur.target <= 0f) { StartTrack(want); return; }
            // loop: start the next pass before this one reaches its fade-out tail, and crossfade
            float end = want.loopEnd > 0f ? Mathf.Min(want.loopEnd, want.clip.length) : want.clip.length;
            if (!cur.src.isPlaying || cur.src.time >= end - musicCrossfade) StartTrack(want);
        }
    }
}
