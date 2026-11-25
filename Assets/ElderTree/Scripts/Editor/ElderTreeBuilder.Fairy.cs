using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElderTree.Combat;
using ElderTree.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// Fairy: textures, materials, model import, the mote and pillar prefabs, the boss prefab (rigid chain, wings,
    /// hurtboxes, body light, wing dust, gather sparkles in the palms) and scene wiring.
    /// The model comes from Tools/Blender/build_fairy.py (exported into Art/Models/Fairy).
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string FairyModelDir = Root + "/Art/Models/Fairy";
        public const string FairyPrefabPath = Root + "/Prefabs/Fairy.prefab";
        public const string FairyMotePrefabPath = Root + "/Prefabs/FairyMote.prefab";
        public const string FairyPillarPrefabPath = Root + "/Prefabs/FairyPillar.prefab";
        const string FairyMoteMeshPath = FairyModelDir + "/FairyMoteCore.asset";

        static readonly string[] FairyMats = { "M_FairyDress", "M_FairySkin", "M_FairyHair", "M_FairyWing", "M_FairyEye", "M_FairyHollow", "M_FairyTrim", "M_FairyGem" };

        /// <summary>Hip height in the model (HIP_Z in build_fairy.py): the pivot sits here.</summary>
        const float FairyHipHeight = 1.4f;

        [MenuItem("Elder Tree/7d. Build Boss (Fairy)")]
        public static string BuildFairyBoss()
        {
            BuildFairyArt();
            ConfigureFairyImport();
            BuildFairyProjectiles();
            BuildFairyPrefab();
            AssetDatabase.SaveAssets();
            return "Fairy built";
        }

        // ------------------------------------------------------------------ textures + materials
        static void BuildFairyArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // the dress: a dusky cornflower blue gone grey with age, long folds falling from the waist, a coarse weave,
            // darker in the creases and faded where it catches the light (user: a blue dress; ART_DIRECTION: muted)
            var dress = TextureForge.SavePsx("T_FairyDress", 128, (u, v, x, y) =>
            {
                float folds = TextureForge.Fbm(u, v, 9, 1, 3, 901);
                float weave = ((x + y) % 2 == 0 ? 1f : 0.93f) * (0.94f + 0.12f * H2(x, y, 902));
                var c = Mx(Hex("#1C2A48"), Hex("#4A6698"), 0.15f + 0.85f * Sstep(0.25f, 0.8f, folds));
                c = Mx(c, Hex("#7186A6"), Sstep(0.72f, 0.9f, TextureForge.Fbm(u, v, 3, 3, 3, 903)) * 0.35f);   // faded patches
                c = Mx(c, Hex("#141A2A"), Sstep(0.6f, 0.8f, TextureForge.Fbm(u, v, 5, 5, 4, 904)) * 0.4f);    // grime
                return c * weave;
            }, mips: false);
            // skin: pale, cold, faintly mottled; a little warmth in the shadows so it doesn't read as stone
            var skin = TextureForge.SavePsx("T_FairySkin", 128, (u, v, x, y) =>
            {
                float m = TextureForge.Fbm(u, v, 6, 6, 4, 911);
                var c = Mx(Hex("#A89A92"), Hex("#DCD2CA"), 0.35f + 0.6f * m);
                c = Mx(c, Hex("#B49A98"), Sstep(0.6f, 0.75f, TextureForge.Fbm(u, v, 3, 3, 3, 912)) * 0.25f);
                return c * (0.96f + 0.08f * H2(x, y, 913));
            }, mips: false);
            // hair: long bone-white strands with grey shadow between them (strands run along v)
            var hairT = TextureForge.SavePsx("T_FairyHair", 128, (u, v, x, y) =>
            {
                float strands = TextureForge.Fbm(u, v, 40, 3, 3, 921);
                float fine = H2(x, y / 6, 922);
                var c = Mx(Hex("#9C9A94"), Hex("#EEEBE2"), 0.2f + 0.8f * Sstep(0.3f, 0.75f, strands));
                c = Mx(c, Hex("#7E7C78"), (fine < 0.12f ? 0.5f : 0f));
                c = Mx(c, Hex("#D6DCE6"), Sstep(0.7f, 0.85f, TextureForge.Fbm(u, v, 4, 2, 3, 923)) * 0.3f);    // a cold sheen
                return c;
            }, mips: false);
            // wings: u runs across the fan, v from the root to the rim. Veins radiate from the root, cross-veins
            // arc between them, a solid rim at the edge; the membrane between is faint and see-through with a cold
            // pale iridescence (blue to lilac).
            var wingT = TextureForge.Save("T_FairyWing", 128, 128, (x, y) =>
            {
                float u = x / 127f, v = y / 127f;
                float wob = (TextureForge.Fbm(u, v, 3, 6, 2, 931) - 0.5f) * 0.05f;
                float uu = (u + wob) * 7f;
                float vein = Mathf.Abs(uu - Mathf.Round(uu)) / 7f;
                float cross = 1f;
                foreach (float cv in new[] { 0.34f, 0.58f, 0.8f })
                {
                    float off = (H2(Mathf.FloorToInt(uu), 0, 932 + (int)(cv * 10)) - 0.5f) * 0.08f;
                    cross = Mathf.Min(cross, Mathf.Abs(v - cv - off - Mathf.Sin(u * 30f) * 0.012f));
                }
                bool isVein = vein < 0.009f * (1.3f - v * 0.6f) || cross < 0.008f || v > 0.95f || v < 0.06f;
                var irid = Color.Lerp(Hex("#C8DAF4"), Hex("#E2D6F2"), TextureForge.Fbm(u, v, 2, 2, 2, 933));
                Color c;
                if (isVein) c = Hex("#F2F0EA");
                else
                {
                    // the membrane: brighter near its veins and toward the root, faint toward the rim
                    float near = 1f - Mathf.Clamp01(Mathf.Min(vein * 7f, cross) / 0.06f);
                    c = Color.Lerp(irid, Hex("#F4F2EE"), near * 0.4f);
                    c.a = Mathf.Lerp(0.36f, 0.16f, v) + near * 0.12f + (H2(x, y, 934) - 0.5f) * 0.04f;
                }
                var q = TextureForge.Psx(c, x, y);
                q.a = isVein ? 0.95f : Mathf.Round(c.a * 16f) / 16f;
                return q;
            }, aniso: 0);
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath($"{TextureForge.TexDir}/T_FairyWing.png");
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = true;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
                wingT = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureForge.TexDir}/T_FairyWing.png");
            }
            // the pillar of light: bright in the middle of its height, soft streaks running down it
            var beamT = TextureForge.Save("T_FX_FairyBeam", 32, 64, (x, y) =>
            {
                float v = y / 63f;
                float streak = 0.65f + 0.35f * TextureForge.Fbm(x / 32f, 0f, 6, 1, 2, 941);
                float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v - 0.35f) * 1.6f), 0.6f) * streak;
                return new Color(1, 1, 1, a);
            }, aniso: 0);

            var dm = Lit("M_FairyDress", Color.white, dress, null, 0f, 1f, null, false);
            Matte(dm);
            dm.SetFloat("_Cull", 0f);                                     // the skirt is open underneath
            EditorUtility.SetDirty(dm);
            Matte(Lit("M_FairySkin", Color.white, skin, null, 0f, 1f, null, false));
            var hm = Lit("M_FairyHair", Color.white, hairT, null, 0f, 1f, null, false);
            Matte(hm);
            hm.SetFloat("_Cull", 0f);                                     // the fall is a single sheet
            EditorUtility.SetDirty(hm);
            Matte(Lit("M_FairyHollow", Hex("#120E12"), null, null, 0f));
            Matte(Lit("M_FairyTrim", Hex("#8C9098"), null, null, 0f));
            // cold pale-blue fae eyes and the brooch: the accent glow of this boss
            Glow("M_FairyEye", Hex("#5A88B8"), 0f, new Color(0.6f, 0.85f, 1.2f) * 1.8f);
            Glow("M_FairyGem", Hex("#6A9ACC"), 0f, new Color(0.5f, 0.8f, 1.2f) * 1.5f);
            // see-through wings (unlit, alpha blended, double sided): they catch no light, they give it
            var wm = Unlit("M_FairyWing", new Color(0.86f, 0.89f, 0.96f, 1f), wingT);
            wm.renderQueue = (int)RenderQueue.Transparent + 5;
            EditorUtility.SetDirty(wm);

            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.TexDir + "/T_FX_SoftDot.png") ?? TextureForge.BuildSoftDot();
            var ring = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.TexDir + "/T_FX_Ring.png") ?? TextureForge.BuildRing();
            Unlit("M_FX_FairyGlow", new Color(0.62f, 0.8f, 1f, 1f), dot, additive: true);
            Unlit("M_FX_FairyCore", new Color(0.86f, 0.95f, 1f, 1f), null);
            Unlit("M_FX_FairyRing", new Color(0.55f, 0.75f, 1f, 1f), ring, additive: true);
            Unlit("M_FX_FairyBeam", new Color(0.62f, 0.82f, 1f, 0.42f), beamT, additive: true);
            BuildFairyIntroArt();
        }

        /// <summary>
        /// The moon that fades in on her back at the end of her intro ballet: a round full moon (cratered face, darker limb)
        /// in a soft cold glow, on black so it can be drawn additive. Point filtered and dithered like the rest of the game.
        /// </summary>
        static void BuildFairyIntroArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            var tex = TextureForge.SavePsx("T_FX_MoonDisc", 128, (u, v, x, y) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                const float R = 0.36f;
                if (r < R)
                {
                    var c = Mx(Hex("#9AA4BC"), Hex("#E8ECF4"), 0.35f + 0.6f * TextureForge.Fbm(u, v, 5, 3, 4, 2801));
                    c = Mx(c, Hex("#5E6880"), Sstep(0.55f, 0.7f, TextureForge.Fbm(u, v, 3, 2, 3, 2802)) * 0.65f);    // seas
                    TextureForge.Worley(u, v, 10, 2803, out float f1, out _, out int id);
                    float cr = 0.05f + 0.06f * ((id * 37 % 11) / 11f);
                    if (f1 < cr) c = Mx(c, Hex("#525C72"), 0.5f * (1f - f1 / cr));
                    else if (f1 < cr * 1.35f) c = Mx(c, Hex("#F4F6FA"), 0.45f);
                    float limb = Mathf.Sqrt(Mathf.Max(0f, 1f - (r / R) * (r / R)));
                    c *= 0.62f + 0.38f * limb;                                                             // darker towards the edge
                    float rim = Sstep(R - 0.012f, R, r);                                                   // soft edge into the glow
                    return Color.Lerp(c, new Color(0.55f, 0.66f, 0.9f) * 0.55f, rim);
                }
                float g = Mathf.Exp(-(r - R) * 22f) * 0.55f * (1f - Sstep(0.44f, 0.5f, r));          // cold glow round it
                return new Color(0.55f, 0.66f, 0.9f) * g;
            }, mips: false);
            var ti = (TextureImporter)AssetImporter.GetAtPath($"{TextureForge.TexDir}/T_FX_MoonDisc.png");
            if (ti != null && ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureForge.TexDir}/T_FX_MoonDisc.png");
            var m = Unlit("M_FX_FairyIntroMoon", Color.white, tex, additive: true);
            m.renderQueue = (int)RenderQueue.Transparent - 10;      // behind her wings
            EditorUtility.SetDirty(m);
        }

        /// <summary>Wires the intro cinematic's moon, glow and sounds onto the boss.</summary>
        static void ApplyFairyIntro(FairyBoss boss)
        {
            boss.introMoonMaterial = M("M_FX_FairyIntroMoon");
            boss.introHaloMaterial = M("M_FX_FairyGlow");
            boss.introMusic = SoundPicks.FairyIntroMusic();
            boss.introRevealClip = SoundPicks.FairyIntroReveal();
            // the pack's female dance steps, retargeted onto her rig through a hidden copy of the pack's female model
            const string kevin = "Assets/Kevin Iglesias/Human Animations";
            boss.danceRig = AssetDatabase.LoadAssetAtPath<GameObject>($"{kevin}/Models/HumanF_Model.fbx");
            // order matters: FairyBoss.DanceFlow / DanceSway / DanceDrive / DanceStep
            boss.danceClips = new[] { "05", "09", "12", "17" }.Select(n =>
                AssetDatabase.LoadAllAssetsAtPath($"{kevin}/Animations/Female/Social/Dance/Steps/HumanF@Dance{n}.fbx")
                    .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"))).ToArray();
        }

        /// <summary>Adds the intro cinematic's assets to the existing Fairy prefab without rebuilding her (BuildFairyBoss does it too).</summary>
        [MenuItem("Elder Tree/7d2. Fairy intro cinematic (moon + music)")]
        public static string BuildFairyIntro()
        {
            BuildFairyIntroArt();
            var root = PrefabUtility.LoadPrefabContents(FairyPrefabPath);
            var boss = root.GetComponent<FairyBoss>();
            ApplyFairyIntro(boss);
            PrefabUtility.SaveAsPrefabAsset(root, FairyPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            return $"Fairy intro: dances {string.Join(", ", boss.danceClips.Select(c => c ? c.name : "MISSING"))}, rig {(boss.danceRig ? "ok" : "MISSING")}, moon {(boss.introMoonMaterial ? "ok" : "MISSING")}, music {(boss.introMusic ? boss.introMusic.name : "MISSING")}, reveal {(boss.introRevealClip ? boss.introRevealClip.name : "MISSING")}";
        }

        static void ConfigureFairyImport()
        {
            string path = $"{FairyModelDir}/Fairy.fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError($"[ElderTree] Missing {path} (run Tools/Blender/build_fairy.py)"); return; }
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importNormals = ModelImporterNormals.Calculate;
            imp.normalSmoothingAngle = 40f;
            imp.importTangents = ModelImporterTangents.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.addCollider = false;
            foreach (var n in FairyMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }
            imp.SaveAndReimport();
        }

        // ------------------------------------------------------------------ projectiles
        /// <summary>An 8-triangle octahedron: the faceted PS1 core of a mote.</summary>
        static Mesh FairyMoteMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FairyMoteMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "FairyMoteCore" };
                Directory.CreateDirectory(FairyModelDir);
                AssetDatabase.CreateAsset(mesh, FairyMoteMeshPath);
            }
            var p = new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };
            Vector3 top = Vector3.up * 1.5f, bot = Vector3.down * 1.5f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int k = 0; k < 4; k++)
            {
                Vector3 a = p[k], b = p[(k + 1) % 4];
                foreach (var tri in new[] { new[] { a, top, b }, new[] { b, bot, a } })
                {
                    foreach (var v in tri) { tris.Add(verts.Count); verts.Add(v * 0.5f); }
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static Light FairyLight(Transform parent, Vector3 local, float intensity, float range, bool pixel = true)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.62f, 0.78f, 1f);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.renderMode = pixel ? LightRenderMode.ForcePixel : LightRenderMode.Auto;
            return l;
        }

        static void BuildFairyProjectiles()
        {
            var glowM = M("M_FX_FairyGlow");
            // ---- the mote: a spinning faceted core in a soft halo, a short trail, a burst of sparks when it pops
            {
                var root = new GameObject("FairyMote");
                var mote = root.AddComponent<FairyMote>();
                var core = Child("Core", root.transform, Vector3.zero);
                core.transform.localScale = Vector3.one * 0.24f;
                core.AddComponent<MeshFilter>().sharedMesh = FairyMoteMesh();
                var cr = core.AddComponent<MeshRenderer>();
                cr.sharedMaterial = M("M_FX_FairyCore");
                cr.shadowCastingMode = ShadowCastingMode.Off;
                mote.core = core.transform;
                mote.coreRenderer = cr;

                var halo = NewPS("Halo", glowM, out _);
                halo.transform.SetParent(core.transform, false);
                var hm = halo.main; hm.loop = true; hm.playOnAwake = true; hm.duration = 1f; hm.startLifetime = 0.14f; hm.startSpeed = 0f;
                hm.startSize = new ParticleSystem.MinMaxCurve(0.75f, 0.95f);
                hm.startColor = new Color(0.6f, 0.82f, 1f, 0.7f);
                hm.simulationSpace = ParticleSystemSimulationSpace.Local;
                hm.scalingMode = ParticleSystemScalingMode.Shape;
                var hem = halo.emission; hem.rateOverTime = 26f;
                var hsh = halo.shape; hsh.enabled = false;
                var hc = halo.colorOverLifetime; hc.enabled = true; hc.color = Grad(Color.white, Color.white, 1f, 0f);
                halo.Play();

                var trailGo = Child("Trail", root.transform, Vector3.zero);
                var trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = 0.22f; trail.minVertexDistance = 0.06f;
                trail.widthCurve = AnimationCurve.EaseInOut(0, 0.28f, 1, 0f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.93f, 1f), 0), new GradientColorKey(new Color(0.45f, 0.6f, 1f), 1) },
                          new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0f, 1) });
                trail.colorGradient = g;
                trail.sharedMaterial = glowM;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                mote.trail = trail;

                var pop = NewPS("Pop", glowM, out _);
                pop.transform.SetParent(root.transform, false);
                var pm = pop.main; pm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
                pm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
                pm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                pm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.8f, 1f), new Color(0.95f, 0.97f, 1f));
                pm.gravityModifier = 0.25f;
                Burst(pop, 20);
                var psh = pop.shape; psh.shapeType = ParticleSystemShapeType.Sphere; psh.radius = 0.1f;
                var pc = pop.colorOverLifetime; pc.enabled = true; pc.color = Grad(Color.white, Color.white, 1f, 0f);
                SizeOverLife(pop, 1f, 0.2f);
                mote.pop = pop;

                mote.glow = FairyLight(root.transform, Vector3.zero, 1.1f, 2.6f, false);
                mote.worldMask = 1;
                PrefabUtility.SaveAsPrefabAsset(root, FairyMotePrefabPath);
                Object.DestroyImmediate(root);
            }
            // ---- the pillar: a ring that fills on the ground, motes rising out of it, then a column slams down
            {
                var root = new GameObject("FairyPillar");
                var pil = root.AddComponent<FairyPillar>();
                var marker = Child("Marker", root.transform, Vector3.up * 0.02f);
                marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                marker.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var mr = marker.AddComponent<MeshRenderer>();
                mr.sharedMaterial = M("M_FX_FairyRing");
                mr.shadowCastingMode = ShadowCastingMode.Off;
                pil.marker = marker.transform;
                pil.markerRenderer = mr;

                var beam = Child("Beam", root.transform, Vector3.up * 7f);
                beam.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                var br = beam.AddComponent<MeshRenderer>();
                br.sharedMaterial = M("M_FX_FairyBeam");
                br.shadowCastingMode = ShadowCastingMode.Off;
                pil.beam = beam.transform;
                pil.beamRenderer = br;
                beam.SetActive(false);

                var rising = NewPS("Rising", glowM, out _);
                rising.transform.SetParent(root.transform, false);
                rising.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var rm = rising.main; rm.loop = true; rm.duration = 1f;
                rm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                rm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
                rm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                rm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.75f, 1f), new Color(0.9f, 0.95f, 1f));
                rm.scalingMode = ParticleSystemScalingMode.Shape;
                var rem = rising.emission; rem.rateOverTime = 34f;
                var rsh = rising.shape; rsh.shapeType = ParticleSystemShapeType.Cone; rsh.angle = 0f; rsh.radius = 1f; rsh.radiusThickness = 0.2f;
                var rc = rising.colorOverLifetime; rc.enabled = true; rc.color = Grad(Color.white, Color.white, 1f, 0f);
                pil.rising = rising;

                var impact = NewPS("Impact", glowM, out _);
                impact.transform.SetParent(root.transform, false);
                var im = impact.main; im.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
                im.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
                im.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
                im.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.75f, 1f), new Color(0.95f, 0.97f, 1f));
                im.gravityModifier = 0.6f;
                Burst(impact, 46);
                var ish = impact.shape; ish.shapeType = ParticleSystemShapeType.Hemisphere; ish.radius = 0.8f;
                ish.rotation = new Vector3(-90f, 0f, 0f);
                var ic = impact.colorOverLifetime; ic.enabled = true; ic.color = Grad(Color.white, Color.white, 1f, 0f);
                pil.impact = impact;

                pil.glow = FairyLight(root.transform, Vector3.up * 0.6f, 2.8f, 7f, false);
                PrefabUtility.SaveAsPrefabAsset(root, FairyPillarPrefabPath);
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------ prefab
        static GameObject FairyHurt(Transform bone, string name, Vector3 a, Vector3 b, float radius)
        {
            var go = new GameObject(name) { layer = LayerHurtbox };
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(b - a, Vector3.up));
            go.transform.SetParent(bone, true);
            var c = go.AddComponent<CapsuleCollider>();
            c.isTrigger = true; c.direction = 2; c.radius = radius; c.height = Vector3.Distance(a, b) + radius * 2f;
            go.AddComponent<Hurtbox>();
            return go;
        }

        static void BuildFairyPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{FairyModelDir}/Fairy.fbx");
            if (fbx == null) { Debug.LogError("[ElderTree] Fairy.fbx missing"); return; }
            var root = new GameObject("Fairy");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var pivot = Child("Pivot", root.transform, Vector3.up * FairyHipHeight);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = "Model";
            model.transform.SetParent(pivot.transform, false);
            model.transform.localPosition = Vector3.down * FairyHipHeight;
            var all = model.GetComponentsInChildren<Transform>(true);
            Transform Part(string n) => all.First(t => t.name == n);
            // the face must look down +Z
            if (Part("Fairy_Head").GetComponent<Renderer>().bounds.center.z < Part("Fairy_Hair").GetComponent<Renderer>().bounds.center.z - 0.02f)
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Transform[] Pair(string n)
            {
                var a = Part(n + "_L"); var b = Part(n + "_R");
                return a.position.x < b.position.x ? new[] { a, b } : new[] { b, a };
            }
            var pelvis = Part("Fairy_Pelvis");
            var skirt = Part("Fairy_Skirt");
            var torso = Part("Fairy_Torso");
            var headT = Part("Fairy_Head");
            var hairT = Part("Fairy_Hair");
            var up = Pair("Fairy_UpperArm"); var fo = Pair("Fairy_Forearm"); var hd = Pair("Fairy_Hand");
            var th = Pair("Fairy_Thigh"); var sh = Pair("Fairy_Shin"); var ft = Pair("Fairy_Foot");
            var wu = Pair("Fairy_Wing_U"); var wd = Pair("Fairy_Wing_D");

            // rigid chain
            skirt.SetParent(pelvis, true);
            torso.SetParent(pelvis, true);
            headT.SetParent(torso, true);
            hairT.SetParent(headT, true);
            for (int i = 0; i < 2; i++)
            {
                up[i].SetParent(torso, true); fo[i].SetParent(up[i], true); hd[i].SetParent(fo[i], true);
                th[i].SetParent(pelvis, true); sh[i].SetParent(th[i], true); ft[i].SetParent(sh[i], true);
                wu[i].SetParent(torso, true); wd[i].SetParent(torso, true);
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = r.name.StartsWith("Fairy_Wing") ? ShadowCastingMode.Off : ShadowCastingMode.On;

            var boss = root.AddComponent<FairyBoss>();
            Vector3 upv = Vector3.up;

            // palms: where motes gather (a little in front of the palm, toward the fingers)
            var palms = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var b = hd[i].GetComponent<Renderer>().bounds;
                palms[i] = Child("Palm", hd[i], Vector3.zero).transform;
                palms[i].position = b.center + Vector3.forward * 0.04f + Vector3.down * 0.02f;
            }

            // hurtboxes: generous, she is hard enough to reach
            var tb = torso.GetComponent<Renderer>().bounds;
            FairyHurt(torso, "Hurt_Torso", new Vector3(0, 1.62f, tb.center.z), new Vector3(0, 2.18f, tb.center.z), 0.24f);
            FairyHurt(headT, "Hurt_Head", new Vector3(0, 2.3f, 0.01f), new Vector3(0, 2.58f, 0.0f), 0.16f);
            FairyHurt(skirt, "Hurt_Skirt", new Vector3(0, 1.55f, 0), new Vector3(0, 0.85f, 0.02f), 0.38f);
            // the hem and both legs as one fat capsule: low swings at a hovering target should still connect
            FairyHurt(pelvis, "Hurt_Legs", new Vector3(0, 0.95f, 0.02f), new Vector3(0, 0.1f, 0.04f), 0.34f);
            for (int i = 0; i < 2; i++)
            {
                FairyHurt(up[i], "Hurt_UpperArm", up[i].position, fo[i].position, 0.09f);
                FairyHurt(fo[i], "Hurt_Forearm", fo[i].position, hd[i].position + upv * -0.1f, 0.08f);
                FairyHurt(sh[i], "Hurt_Shin", sh[i].position, ft[i].position + upv * -0.15f, 0.12f);
                // the big upper wings take blows too (root to two thirds of the way to the tip)
                var wb = wu[i].GetComponent<Renderer>().bounds;
                Vector3 tip = wu[i].position + (wb.center - wu[i].position) * 1.3f;
                FairyHurt(wu[i], "Hurt_Wing", wu[i].position + (tip - wu[i].position) * 0.2f, tip, 0.28f);
            }

            var lockPoint = Child("LockPoint", torso, Vector3.zero);
            lockPoint.transform.position = new Vector3(0, 1.95f, tb.center.z);

            // her own cold light: lights her face and the ground under her at night
            var light = FairyLight(torso, Vector3.zero, 1.1f, 6.5f);
            light.transform.position = new Vector3(0, 1.65f, tb.center.z + 1.0f);     // out in front: lights her evenly, never blows out the face

            // fairy dust drifting off the wings
            var dust = NewPS("WingDust", M("M_FX_FairyGlow"), out _);
            dust.transform.SetParent(torso, false);
            dust.transform.position = new Vector3(0, 2.0f, tb.center.z - 0.45f);
            var dm = dust.main; dm.loop = true; dm.playOnAwake = true; dm.duration = 2f;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            dm.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.8f, 1f), new Color(0.95f, 0.97f, 1f));
            dm.gravityModifier = 0.03f;
            dm.maxParticles = 400;
            var dem = dust.emission; dem.rateOverTime = 12f;
            var dsh = dust.shape; dsh.shapeType = ParticleSystemShapeType.Box; dsh.scale = new Vector3(2.2f, 1.6f, 0.4f);
            var dn = dust.noise; dn.enabled = true; dn.strength = 0.4f; dn.frequency = 0.6f;
            var dc = dust.colorOverLifetime; dc.enabled = true; dc.color = Grad(Color.white, Color.white, 1f, 0f);
            dust.Play();

            // sparkles drawn into the palms while she gathers a spell
            var gather = new ParticleSystem[2];
            for (int i = 0; i < 2; i++)
            {
                var gp = NewPS("Gather", M("M_FX_FairyGlow"), out _);
                gp.transform.SetParent(palms[i], false);
                var gm = gp.main; gm.loop = true; gm.duration = 1f;
                gm.startLifetime = 0.35f;
                gm.startSpeed = -1.1f;
                gm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                gm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.82f, 1f), new Color(0.95f, 0.97f, 1f));
                gm.simulationSpace = ParticleSystemSimulationSpace.Local;
                var gem = gp.emission; gem.rateOverTime = 45f;
                var gsh = gp.shape; gsh.shapeType = ParticleSystemShapeType.Sphere; gsh.radius = 0.4f;
                var gc = gp.colorOverLifetime; gc.enabled = true; gc.color = Grad(Color.white, Color.white, 0.2f, 1f);
                gather[i] = gp;
            }

            boss.pivot = pivot.transform;
            boss.model = model.transform;
            boss.pelvis = pelvis; boss.skirt = skirt; boss.torso = torso; boss.head = headT; boss.hair = hairT;
            boss.upperArm = up; boss.forearm = fo; boss.hand = hd; boss.palm = palms;
            boss.thigh = th; boss.shin = sh; boss.foot = ft;
            boss.wingUp = wu; boss.wingDown = wd;
            boss.lockPoint = lockPoint.transform;
            boss.flashRenderers = model.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer && !r.name.StartsWith("Fairy_Wing")).ToArray();
            boss.bodyLight = light;
            boss.wingDust = dust;
            boss.gatherFx = gather;
            boss.motePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FairyMotePrefabPath)?.GetComponent<FairyMote>();
            boss.pillarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FairyPillarPrefabPath)?.GetComponent<FairyPillar>();
            var tg = root.AddComponent<Targetable>();
            tg.lockPoint = lockPoint.transform; tg.lockRadius = 0.8f; tg.displayName = boss.displayName; tg.rangeScale = 1.5f;
            ApplyFairyIntro(boss);

            SetLayerRecursive(root, LayerEnemy);
            foreach (var h in root.GetComponentsInChildren<Hurtbox>(true)) h.gameObject.layer = LayerHurtbox;
            PrefabUtility.SaveAsPrefabAsset(root, FairyPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ scene
        static void PlaceFairyBoss(Transform actorRoot, BossEncounter enc)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FairyPrefabPath);
            if (prefab == null || enc == null) { Debug.LogWarning("[ElderTree] Fairy prefab missing: run BuildFairyBoss()"); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(actorRoot);
            go.transform.position = new Vector3(0f, 0.6f, 40f);
            var boss = go.GetComponent<FairyBoss>();
            boss.arenaCenter = Vector3.zero;
            enc.bosses.Add(boss);
        }
    }
}
