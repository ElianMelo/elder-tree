using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TungSahur.EditorTools
{
    /// <summary>
    /// Procedural sound design: every cue is synthesized (damped modal resonators for wood,
    /// filtered noise for whooshes / dirt / straw, FM blips for UI) and written as 16-bit WAV.
    /// Deterministic seeds keep re-runs stable.
    /// </summary>
    public static class SfxForge
    {
        public const string Dir = "Assets/TungSahur/Audio";
        const int SR = 44100;

        // ------------------------------------------------------------------ primitives
        class Rng
        {
            uint s;
            public Rng(int seed) { s = (uint)seed * 2654435761u + 1; }
            public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f; }
        }

        /// <summary>State-variable filter (Chamberlin), per-sample cutoff.</summary>
        class SVF
        {
            float low, band;
            public float Process(float x, float cutoff, float q, out float hp, out float bp)
            {
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(cutoff, 20f, SR * 0.22f) / SR);
                low += f * band;
                hp = x - low - q * band;
                band += f * hp;
                bp = band;
                return low;
            }
        }

        static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * SR)];

        /// <summary>Modal resonator bank: sum of exponentially decaying sines (wood, drums).</summary>
        static void Modal(float[] b, float start, (float freq, float amp, float decay)[] modes, float pitchDrop = 0f, float dropTime = 0.05f)
        {
            int s0 = (int)(start * SR);
            foreach (var (freq, amp, decay) in modes)
            {
                double phase = 0;
                for (int i = s0; i < b.Length; i++)
                {
                    float t = (i - s0) / (float)SR;
                    float env = Mathf.Exp(-t / decay);
                    if (env < 0.0005f) break;
                    float f = freq * (1f + pitchDrop * Mathf.Exp(-t / dropTime));
                    phase += 2 * Math.PI * f / SR;
                    b[i] += (float)Math.Sin(phase) * amp * env;
                }
            }
        }

        static void NoiseBurst(float[] b, float start, float dur, float amp, float cutoffStart, float cutoffEnd, float q, int seed,
            float attack = 0.002f, string mode = "low", float curve = 1f)
        {
            var r = new Rng(seed);
            var f = new SVF();
            int s0 = (int)(start * SR), n = (int)(dur * SR);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Min(1f, (i / (float)SR) / attack) * Mathf.Pow(1f - t, curve);
                float cut = Mathf.Lerp(cutoffStart, cutoffEnd, t);
                float lp = f.Process(r.Next(), cut, q, out float hp, out float bp);
                float v = mode == "low" ? lp : mode == "band" ? bp : hp;
                b[s0 + i] += v * amp * env;
            }
        }

        /// <summary>Whoosh: band-passed noise with a swelling envelope and sweeping centre.</summary>
        static void Whoosh(float[] b, float dur, float amp, float f0, float f1, float peakAt, int seed, float q = 0.5f)
        {
            var r = new Rng(seed);
            var f = new SVF();
            int n = Mathf.Min(b.Length, (int)(dur * SR));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = t < peakAt ? Mathf.Pow(t / peakAt, 2.2f) : Mathf.Pow(1f - (t - peakAt) / (1f - peakAt), 1.6f);
                float cut = Mathf.Lerp(f0, f1, Mathf.SmoothStep(0, 1, t));
                f.Process(r.Next(), cut, q, out _, out float bp);
                b[i] += bp * amp * env;
            }
        }

        static void Sine(float[] b, float start, float dur, float freq, float amp, float attack, float decay, float vibrato = 0f)
        {
            int s0 = (int)(start * SR), n = (int)(dur * SR);
            double ph = 0;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float t = i / (float)SR;
                float env = Mathf.Min(1f, t / attack) * Mathf.Exp(-t / decay);
                ph += 2 * Math.PI * freq * (1 + vibrato * Mathf.Sin(t * 30f)) / SR;
                b[s0 + i] += (float)Math.Sin(ph) * amp * env;
            }
        }

        static float[] Normalize(float[] b, float peak = 0.89f)
        {
            float m = 1e-6f;
            foreach (var v in b) m = Mathf.Max(m, Mathf.Abs(v));
            float k = peak / m;
            for (int i = 0; i < b.Length; i++) b[i] = (float)Math.Tanh(b[i] * k * 1.1f) / (float)Math.Tanh(1.1f) * 1f;
            // short fade-out to avoid clicks
            int fade = Mathf.Min(b.Length, 400);
            for (int i = 0; i < fade; i++) b[b.Length - 1 - i] *= i / (float)fade;
            return b;
        }

        static AudioClip Write(string name, float[] data, bool loop = false)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.wav";
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int bytes = data.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (var v in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = imp.defaultSampleSettings;
            s.loadType = loop ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // ------------------------------------------------------------------ cues
        public static AudioClip[] Knock()
        {
            var clips = new AudioClip[3];
            float[] roots = { 196f, 176f, 216f };
            for (int v = 0; v < 3; v++)
            {
                var b = Buf(0.6f);
                float f = roots[v];
                // hollow log: strong fundamental + inharmonic wood partials
                Modal(b, 0f, new[] { (f, 1f, 0.16f), (f * 2.32f, 0.55f, 0.07f), (f * 3.9f, 0.32f, 0.035f), (f * 5.6f, 0.2f, 0.02f), (f * 0.5f, 0.35f, 0.12f) },
                    pitchDrop: 0.18f, dropTime: 0.02f);
                NoiseBurst(b, 0f, 0.02f, 0.9f, 6000f, 1500f, 0.4f, 11 + v, mode: "band");   // stick contact click
                NoiseBurst(b, 0f, 0.09f, 0.3f, 900f, 300f, 0.7f, 21 + v);                    // body thunk
                clips[v] = Write($"sfx_knock_{v}", Normalize(b));
            }
            return clips;
        }

        public static AudioClip[] ImpactHeavy()
        {
            var clips = new AudioClip[2];
            for (int v = 0; v < 2; v++)
            {
                var b = Buf(0.9f);
                Modal(b, 0f, new[] { (58f + v * 6, 1f, 0.22f), (92f, 0.5f, 0.12f), (140f, 0.25f, 0.06f) }, pitchDrop: 1.1f, dropTime: 0.03f);
                NoiseBurst(b, 0f, 0.25f, 0.7f, 2400f, 200f, 0.6f, 31 + v, curve: 2.2f);
                NoiseBurst(b, 0.005f, 0.06f, 0.6f, 7000f, 2500f, 0.3f, 41 + v, mode: "band");  // crack
                clips[v] = Write($"sfx_impact_heavy_{v}", Normalize(b));
            }
            return clips;
        }

        public static AudioClip[] Slam()
        {
            var b = Buf(1.6f);
            Modal(b, 0f, new[] { (46f, 1f, 0.45f), (70f, 0.6f, 0.25f), (110f, 0.3f, 0.1f) }, pitchDrop: 1.6f, dropTime: 0.04f);
            NoiseBurst(b, 0f, 1.2f, 0.55f, 1800f, 120f, 0.8f, 51, curve: 2.6f);              // rumble + debris
            NoiseBurst(b, 0.0f, 0.08f, 0.7f, 5000f, 1800f, 0.4f, 52, mode: "band");
            for (int k = 0; k < 9; k++)                                                          // pebbles falling
                NoiseBurst(b, 0.12f + k * 0.07f + (k % 3) * 0.013f, 0.03f, 0.18f, 3500f, 2000f, 0.6f, 60 + k, mode: "band");
            return new[] { Write("sfx_slam", Normalize(b)) };
        }

        public static AudioClip[] WhooshLight()
        {
            var a = new AudioClip[3];
            for (int v = 0; v < 3; v++)
            {
                var b = Buf(0.32f);
                Whoosh(b, 0.32f, 1f, 500f + v * 80, 2400f + v * 300, 0.45f, 70 + v, 0.6f);
                a[v] = Write($"sfx_whoosh_light_{v}", Normalize(b, 0.8f));
            }
            return a;
        }

        public static AudioClip[] WhooshHeavy()
        {
            var a = new AudioClip[2];
            for (int v = 0; v < 2; v++)
            {
                var b = Buf(0.55f);
                Whoosh(b, 0.55f, 1f, 220f + v * 40, 1300f, 0.55f, 80 + v, 0.45f);
                Whoosh(b, 0.55f, 0.4f, 90f, 260f, 0.6f, 90 + v, 0.8f);                           // air pressure
                a[v] = Write($"sfx_whoosh_heavy_{v}", Normalize(b, 0.85f));
            }
            return a;
        }

        public static AudioClip[] Footsteps()
        {
            var a = new AudioClip[4];
            for (int v = 0; v < 4; v++)
            {
                var b = Buf(0.22f);
                NoiseBurst(b, 0f, 0.11f, 1f, 900f + v * 120, 250f, 0.7f, 100 + v, attack: 0.004f, curve: 1.8f); // thud on dirt
                NoiseBurst(b, 0.01f, 0.12f, 0.35f, 4500f, 2500f, 0.5f, 110 + v, mode: "band", curve: 2f);         // grit
                Modal(b, 0f, new[] { (120f + v * 7, 0.25f, 0.03f) });                                            // wooden foot
                a[v] = Write($"sfx_step_{v}", Normalize(b, 0.7f));
            }
            return a;
        }

        public static AudioClip[] Dodge()
        {
            var b = Buf(0.5f);
            Whoosh(b, 0.35f, 0.8f, 300f, 1500f, 0.35f, 120, 0.5f);
            NoiseBurst(b, 0.28f, 0.18f, 0.6f, 1600f, 300f, 0.6f, 121, curve: 1.6f);                // scuff/land
            Modal(b, 0.3f, new[] { (150f, 0.35f, 0.05f), (330f, 0.2f, 0.03f) });                      // log tap
            return new[] { Write("sfx_dodge", Normalize(b, 0.8f)) };
        }

        public static AudioClip[] Jump()
        {
            var b = Buf(0.3f);
            NoiseBurst(b, 0f, 0.12f, 0.7f, 1300f, 400f, 0.6f, 130, curve: 1.6f);
            Whoosh(b, 0.25f, 0.4f, 400f, 1200f, 0.3f, 131);
            return new[] { Write("sfx_jump", Normalize(b, 0.7f)) };
        }

        public static AudioClip[] Land()
        {
            var b = Buf(0.45f);
            NoiseBurst(b, 0f, 0.2f, 1f, 1100f, 150f, 0.7f, 140, curve: 2f);
            Modal(b, 0f, new[] { (82f, 0.7f, 0.1f), (180f, 0.35f, 0.05f) }, pitchDrop: 0.4f);
            return new[] { Write("sfx_land", Normalize(b, 0.8f)) };
        }

        public static AudioClip[] DummyHit()
        {
            var a = new AudioClip[3];
            for (int v = 0; v < 3; v++)
            {
                var b = Buf(0.4f);
                NoiseBurst(b, 0f, 0.16f, 1f, 2200f + v * 200, 500f, 0.5f, 150 + v, curve: 1.4f);   // straw crunch
                for (int k = 0; k < 6; k++)
                    NoiseBurst(b, 0.01f + k * 0.012f, 0.02f, 0.35f, 5500f, 3500f, 0.4f, 160 + v * 10 + k, mode: "band");
                Modal(b, 0f, new[] { (95f + v * 8, 0.6f, 0.07f) }, pitchDrop: 0.5f);              // sack thump
                a[v] = Write($"sfx_dummy_hit_{v}", Normalize(b, 0.8f));
            }
            return a;
        }

        public static AudioClip[] LockOn()
        {
            var b = Buf(0.25f);
            Sine(b, 0f, 0.25f, 1320f, 0.5f, 0.002f, 0.05f);
            Sine(b, 0.035f, 0.2f, 1980f, 0.35f, 0.002f, 0.05f);
            return new[] { Write("sfx_lockon", Normalize(b, 0.5f)) };
        }

        public static AudioClip[] LockOff()
        {
            var b = Buf(0.2f);
            Sine(b, 0f, 0.2f, 990f, 0.5f, 0.002f, 0.04f);
            return new[] { Write("sfx_lockoff", Normalize(b, 0.4f)) };
        }

        public static AudioClip[] ChargeUp()
        {
            var b = Buf(0.9f);
            int n = b.Length; double ph = 0; var r = new Rng(170); var f = new SVF();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float freq = Mathf.Lerp(110f, 330f, t * t);
                ph += 2 * Math.PI * freq / SR;
                float env = Mathf.SmoothStep(0, 1, t * 3f) * (1 - Mathf.Pow(t, 6));
                f.Process(r.Next(), 600f + 3000f * t, 0.4f, out _, out float bp);
                b[i] = ((float)Math.Sin(ph) * 0.5f + (float)Math.Sin(ph * 2.01) * 0.2f + bp * 0.5f) * env;
            }
            return new[] { Write("sfx_charge", Normalize(b, 0.6f)) };
        }

        public static AudioClip[] ChargeRelease()
        {
            var b = Buf(0.5f);
            Whoosh(b, 0.4f, 1f, 800f, 4000f, 0.15f, 180, 0.4f);
            Sine(b, 0f, 0.4f, 660f, 0.3f, 0.002f, 0.08f);
            return new[] { Write("sfx_charge_release", Normalize(b, 0.7f)) };
        }

        public static AudioClip[] UIMove()
        {
            var b = Buf(0.08f);
            Sine(b, 0f, 0.08f, 1500f, 0.5f, 0.001f, 0.015f);
            NoiseBurst(b, 0f, 0.01f, 0.2f, 8000f, 6000f, 0.4f, 190, mode: "band");
            return new[] { Write("sfx_ui_move", Normalize(b, 0.35f)) };
        }

        public static AudioClip[] UISelect()
        {
            var b = Buf(0.35f);
            Sine(b, 0f, 0.35f, 880f, 0.5f, 0.002f, 0.06f);
            Sine(b, 0.05f, 0.3f, 1320f, 0.4f, 0.002f, 0.08f);
            Modal(b, 0f, new[] { (330f, 0.3f, 0.06f) });
            return new[] { Write("sfx_ui_select", Normalize(b, 0.45f)) };
        }

        public static AudioClip[] UIBack()
        {
            var b = Buf(0.25f);
            Sine(b, 0f, 0.25f, 660f, 0.5f, 0.002f, 0.05f);
            Sine(b, 0.04f, 0.2f, 440f, 0.4f, 0.002f, 0.06f);
            return new[] { Write("sfx_ui_back", Normalize(b, 0.4f)) };
        }

        public static AudioClip[] Felled()
        {
            var b = Buf(3.2f);
            Modal(b, 0f, new[] { (55f, 1f, 0.9f), (82.5f, 0.5f, 0.7f), (110f, 0.4f, 0.5f) }, pitchDrop: 0.7f, dropTime: 0.05f);
            NoiseBurst(b, 0f, 1.2f, 0.4f, 900f, 100f, 0.8f, 200, curve: 2.4f);
            // shimmering golden chord (A minor add9) swelling in
            float[] chord = { 220f, 261.6f, 329.6f, 493.9f, 659.3f };
            foreach (var f in chord) Sine(b, 0.25f, 2.9f, f, 0.16f, 0.6f, 1.4f, 0.002f);
            return new[] { Write("sfx_felled", Normalize(b, 0.8f)) };
        }

        public static AudioClip[] GuardUp()
        {
            var b = Buf(0.2f);
            NoiseBurst(b, 0f, 0.1f, 0.6f, 1800f, 600f, 0.6f, 210);
            Modal(b, 0f, new[] { (240f, 0.3f, 0.04f) });
            return new[] { Write("sfx_guard", Normalize(b, 0.5f)) };
        }

        public static AudioClip[] Stagger()
        {
            var b = Buf(0.7f);
            Modal(b, 0f, new[] { (140f, 0.8f, 0.25f), (210f, 0.5f, 0.2f), (315f, 0.3f, 0.15f) }, pitchDrop: -0.25f, dropTime: 0.3f);
            NoiseBurst(b, 0f, 0.3f, 0.4f, 3000f, 600f, 0.5f, 220);
            return new[] { Write("sfx_stagger", Normalize(b, 0.7f)) };
        }

        /// <summary>The title sting: three kentongan knocks — "tung, tung, tung" — and a low swell.</summary>
        public static AudioClip[] Title()
        {
            var b = Buf(2.6f);
            float[] times = { 0f, 0.32f, 0.64f };
            foreach (var t in times)
                Modal(b, t, new[] { (180f, 1f, 0.22f), (418f, 0.45f, 0.08f), (702f, 0.25f, 0.04f) }, pitchDrop: 0.15f, dropTime: 0.02f);
            Modal(b, 0.96f, new[] { (45f, 0.9f, 0.9f), (90f, 0.4f, 0.6f) }, pitchDrop: 0.5f);
            Sine(b, 0.96f, 1.6f, 220f, 0.12f, 0.3f, 0.8f);
            Sine(b, 0.96f, 1.6f, 329.6f, 0.1f, 0.3f, 0.8f);
            return new[] { Write("sfx_title", Normalize(b, 0.8f)) };
        }

        /// <summary>Night ambience loop: crickets (AM chirps), distant wind, faint frogs. Seamless 16s.</summary>
        // ------------------------------------------------------------------ bow
        /// <summary>Wood creak + string stretch while the bow is drawn.</summary>
        public static AudioClip[] BowDraw()
        {
            var a = new AudioClip[2];
            for (int v = 0; v < 2; v++)
            {
                var b = Buf(0.75f);
                var r = new Rng(300 + v);
                // creak: clicks getting denser and tighter as the limbs load
                float t = 0.02f;
                int k = 0;
                while (t < 0.62f)
                {
                    float prog = t / 0.62f;
                    NoiseBurst(b, t, 0.006f, 0.35f + 0.25f * prog, 1600f + 1400f * prog, 1200f, 0.3f, 310 + v * 50 + k, mode: "band");
                    t += Mathf.Lerp(0.055f, 0.014f, prog) * (0.8f + 0.4f * Mathf.Abs(r.Next()));
                    k++;
                }
                Modal(b, 0.02f, new[] { (128f + v * 9f, 0.25f, 0.35f), (301f, 0.08f, 0.2f) });
                Whoosh(b, 0.7f, 0.35f, 350f, 900f, 0.8f, 320 + v, 0.9f);   // string stretch
                a[v] = Write($"sfx_bow_draw_{v}", Normalize(b, 0.55f));
            }
            return a;
        }

        /// <summary>String twang + limb thump + the arrow leaving.</summary>
        public static AudioClip[] BowRelease()
        {
            var a = new AudioClip[2];
            for (int v = 0; v < 2; v++)
            {
                var b = Buf(0.6f);
                float f = v == 0 ? 104f : 96f;
                Modal(b, 0f, new[] { (f, 1f, 0.11f), (f * 2.01f, 0.55f, 0.07f), (f * 3.02f, 0.3f, 0.045f), (f * 5.1f, 0.18f, 0.025f) },
                    pitchDrop: 0.35f, dropTime: 0.012f);
                NoiseBurst(b, 0f, 0.05f, 0.7f, 700f, 160f, 0.6f, 340 + v);                         // limb thump
                NoiseBurst(b, 0f, 0.008f, 0.6f, 5200f, 3000f, 0.4f, 350 + v, mode: "band");        // finger slip
                var w = Buf(0.6f);
                Whoosh(w, 0.32f, 0.6f, 1600f, 5200f, 0.12f, 360 + v, 0.45f);                     // arrow away
                for (int i = 0; i < b.Length; i++) b[i] += w[i];
                a[v] = Write($"sfx_bow_release_{v}", Normalize(b, 0.8f));
            }
            return a;
        }

        public static AudioClip[] BowNock()
        {
            var b = Buf(0.16f);
            NoiseBurst(b, 0f, 0.007f, 0.8f, 4200f, 2600f, 0.35f, 370, mode: "band");
            NoiseBurst(b, 0.045f, 0.006f, 0.6f, 3600f, 2400f, 0.35f, 371, mode: "band");
            Modal(b, 0.045f, new[] { (1180f, 0.25f, 0.02f), (2400f, 0.12f, 0.012f) });
            return new[] { Write("sfx_bow_nock", Normalize(b, 0.45f)) };
        }

        /// <summary>Arrowhead biting into wood / earth: a short, dry thunk.</summary>
        public static AudioClip[] ArrowHit()
        {
            var a = new AudioClip[3];
            for (int v = 0; v < 3; v++)
            {
                var b = Buf(0.4f);
                Modal(b, 0f, new[] { (230f + v * 18f, 1f, 0.05f), (540f + v * 30f, 0.4f, 0.025f), (92f, 0.5f, 0.07f) }, pitchDrop: 0.3f, dropTime: 0.01f);
                NoiseBurst(b, 0f, 0.04f, 0.6f, 2200f, 400f, 0.5f, 380 + v);
                NoiseBurst(b, 0f, 0.006f, 0.7f, 6000f, 4000f, 0.4f, 390 + v, mode: "band");
                // the shaft buzzing after it sticks
                Sine(b, 0.01f, 0.3f, 62f + v * 5f, 0.18f, 0.004f, 0.09f, vibrato: 0.02f);
                a[v] = Write($"sfx_arrow_hit_{v}", Normalize(b, 0.75f));
            }
            return a;
        }

        /// <summary>Subtle chime when the draw reaches full power.</summary>
        public static AudioClip[] FullDraw()
        {
            var b = Buf(0.35f);
            Sine(b, 0f, 0.35f, 1568f, 0.35f, 0.003f, 0.09f);
            Sine(b, 0.03f, 0.32f, 2349f, 0.22f, 0.003f, 0.08f);
            NoiseBurst(b, 0f, 0.01f, 0.2f, 5000f, 3000f, 0.4f, 395, mode: "band");
            return new[] { Write("sfx_full_draw", Normalize(b, 0.4f)) };
        }

        public static AudioClip Ambience()
        {
            const float L = 16f;
            var b = Buf(L);
            var r = new Rng(300);
            var f = new SVF();
            int n = b.Length;
            // wind: slow-modulated low noise
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float mod = 0.55f + 0.45f * Mathf.Sin(2 * Mathf.PI * t / L) * Mathf.Sin(2 * Mathf.PI * 3 * t / L + 1.2f);
                float lp = f.Process(r.Next(), 280f + 180f * mod, 0.9f, out _, out _);
                b[i] += lp * 0.5f * mod;
            }
            // crickets: several insects at different pitches/rates, periodic in L
            var cr = new Rng(310);
            for (int c = 0; c < 5; c++)
            {
                float freq = 4300f + c * 260f + cr.Next() * 100f;
                float rate = 2f * Mathf.Round((3.2f + c * 0.45f) * L / 2f) / L; // integer cycles per loop
                float amp = 0.05f + 0.03f * (c % 2);
                float phaseOff = cr.Next() * 3f;
                double ph = 0;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SR;
                    float pulseT = Mathf.Repeat(t * rate + phaseOff, 1f);
                    // each chirp = 3 quick pulses
                    float burst = pulseT < 0.18f ? Mathf.Max(0, Mathf.Sin(pulseT / 0.18f * Mathf.PI * 3f)) : 0f;
                    float swell = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * (t / L) * (c + 1) + c);
                    ph += 2 * Math.PI * freq / SR;
                    b[i] += (float)Math.Sin(ph) * burst * amp * swell;
                }
            }
            // distant frogs: low croaks a few times per loop
            for (int k = 0; k < 6; k++)
            {
                float t0 = 1.3f + k * 2.4f + (k % 2) * 0.5f;
                for (int j = 0; j < 3; j++)
                    Modal(b, t0 + j * 0.11f, new[] { (260f + k * 12, 0.04f, 0.03f), (520f + k * 20, 0.02f, 0.02f) });
            }
            // seamless loop: crossfade tail into head
            int x = SR / 2;
            for (int i = 0; i < x; i++)
            {
                float k = i / (float)x;
                b[i] = b[i] * k + b[n - x + i] * (1 - k);
            }
            var trimmed = new float[n - x];
            Array.Copy(b, trimmed, trimmed.Length);
            float m = 1e-6f;
            foreach (var v in trimmed) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < trimmed.Length; i++) trimmed[i] *= 0.6f / m;
            return Write("amb_night_loop", trimmed, loop: true);
        }
    }
}
