using System;
using System.Collections.Generic;
using UnityEngine;

namespace TungSahur.Audio
{
    public enum Sfx
    {
        Knock, ImpactHeavy, Slam, WhooshLight, WhooshHeavy, Footstep, Dodge, Jump, Land, DummyHit,
        LockOn, LockOff, ChargeUp, ChargeRelease, UIMove, UISelect, UIBack, Felled, Guard, Stagger, Title
    }

    /// <summary>Pooled one-shot player with per-cue variation (random clip, pitch, volume).</summary>
    public class AudioManager : MonoBehaviour
    {
        [Serializable]
        public class Cue
        {
            public Sfx id;
            public AudioClip[] clips = Array.Empty<AudioClip>();
            [Range(0, 1)] public float volume = 0.8f;
            public float pitchMin = 0.95f, pitchMax = 1.05f;
            [Tooltip("0 = 2D, 1 = fully positional")] [Range(0, 1)] public float spatial = 0.7f;
            public float minInterval = 0.03f;
        }

        public Cue[] cues = Array.Empty<Cue>();
        public AudioClip ambienceLoop;
        [Range(0, 1)] public float ambienceVolume = 0.35f;
        [Range(0, 1)] public float masterVolume = 1f;
        [Range(0, 1)] public float sfxVolume = 1f;
        public int voices = 24;

        public static AudioManager Instance { get; private set; }

        readonly Dictionary<Sfx, Cue> map = new();
        readonly Dictionary<Sfx, float> lastPlayed = new();
        readonly Dictionary<Sfx, int> lastIndex = new();
        AudioSource[] pool;
        int next;
        AudioSource ambience;

        void Awake()
        {
            Instance = this;
            foreach (var c in cues) map[c.id] = c;
            pool = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject($"Voice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 3f;
                src.maxDistance = 45f;
                src.dopplerLevel = 0f;
                pool[i] = src;
            }
            if (ambienceLoop != null)
            {
                ambience = gameObject.AddComponent<AudioSource>();
                ambience.clip = ambienceLoop;
                ambience.loop = true;
                ambience.spatialBlend = 0f;
                ambience.volume = ambienceVolume * masterVolume;
                ambience.Play();
            }
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

            var src = pool[next];
            next = (next + 1) % pool.Length;
            src.transform.position = pos;
            src.clip = cue.clips[idx];
            src.spatialBlend = force2D ? 0f : cue.spatial;
            src.volume = cue.volume * volumeScale * sfxVolume;
            src.pitch = UnityEngine.Random.Range(cue.pitchMin, cue.pitchMax) * pitchScale;
            src.Play();
        }

        void Update()
        {
            if (ambience != null) ambience.volume = ambienceVolume;
        }
    }
}
