using System.IO;
using System.Linq;
using TungSahur.Combat;
using TungSahur.Enemies;
using TungSahur.FX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TungSahur.EditorTools
{
    /// <summary>
    /// Bombardiro Crocodilo: textures, materials, model import, FX, prefabs and scene wiring.
    /// Models come from Tools/Blender/build_bombardiro.py (copied into Art/Models/Boss).
    /// </summary>
    public static partial class TungSahurBuilder
    {
        public const string BossModelDir = Root + "/Art/Models/Boss";
        public const string BossPrefabPath = Root + "/Prefabs/Bombardiro.prefab";
        public const string AerialBombPrefabPath = Root + "/Prefabs/AerialBomb.prefab";
        public const string LivingBombPrefabPath = Root + "/Prefabs/LivingBomb.prefab";

        static readonly string[] BossMats =
            { "M_BomberMetal", "M_BomberGlass", "M_Iron", "M_CrocHide", "M_CrocMouth", "M_CrocTeeth", "M_CrocEye", "M_BombIron", "M_BombBand", "M_BombEye" };

        [MenuItem("Tung Sahur/7. Build Boss (Bombardiro)")]
        public static string BuildBoss()
        {
            BuildBossArt();
            ConfigureBossImport();
            BuildBossFX();
            BuildAerialBombPrefab();
            BuildLivingBombPrefab();
            BuildBossPrefab();
            AssetDatabase.SaveAssets();
            return "Boss built";
        }

        // ------------------------------------------------------------------ textures + materials
        static float H2(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static Color Mx(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        /// <summary>GLSL-style smoothstep threshold (Mathf.SmoothStep interpolates between its first two args).</summary>
        static float Sstep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static void BuildBossArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // riveted, oil-streaked, rust-bitten aluminium: 4x4 panels per tile (one tile = 2.4 m)
            var metal = TextureForge.SavePsx("T_BomberMetal", 128, (u, v, x, y) =>
            {
                const int P = 4;
                float pu = u * P, pv = v * P * 2f;      // panels are twice as wide as tall
                int iu = Mathf.FloorToInt(pu), iv = Mathf.FloorToInt(pv + (iu % 2) * 0.5f);
                float fu = pu - iu, fv = pv + (iu % 2) * 0.5f - iv;
                float tint = H2(iu, iv, 3) * 0.12f - 0.06f;
                var c = Mx(Hex("#4C4E48"), Hex("#7E8076"), 0.45f + tint + 0.35f * TextureForge.Fbm(u, v, 6, 6, 3, 401));
                // seams + rivet rows along the panel edges
                float edge = Mathf.Min(Mathf.Min(fu, 1f - fu) * 2.2f, Mathf.Min(fv, 1f - fv));
                if (edge < 0.035f) c *= 0.55f;
                bool rivetRow = Mathf.Abs(fv - 0.08f) < 0.03f || Mathf.Abs(fv - 0.92f) < 0.03f;
                if (rivetRow && (x % 4) == 1) c = Mx(c, Hex("#8A8A80"), 0.6f);
                if (rivetRow && (x % 4) == 2) c *= 0.7f;
                // oil streaks running aft, grime, rust
                float streak = TextureForge.Fbm(u * 0.5f, v, 16, 2, 2, 402);
                c *= 1f - Sstep(0.55f, 0.75f, streak) * 0.35f;
                float rust = Sstep(0.58f, 0.72f, TextureForge.Fbm(u, v, 5, 5, 4, 403));
                c = Mx(c, Mx(Hex("#3A1F12"), Hex("#6A3A1E"), TextureForge.Fbm(u, v, 20, 20, 2, 404)), rust * 0.75f);
                c *= 0.92f + 0.16f * (H2(x, y, 405) - 0.5f) + 0.1f;
                return c;
            });
            // crocodile hide: Worley scutes with dark crevices, bumpy osteoderms, olive-black
            var hide = TextureForge.SavePsx("T_CrocHide", 128, (u, v, x, y) =>
            {
                TextureForge.Worley(u, v * 1.5f, 8, 411, out float f1, out float f2, out int id);
                float crev = Sstep(0.0f, 0.12f, f2 - f1);
                float bump = 1f - Mathf.Clamp01(f1 * 1.6f);
                float tone = (id % 7) / 7f;
                var c = Mx(Hex("#26301A"), Hex("#5E6E3A"), 0.3f + 0.45f * bump + tone * 0.25f);
                c = Mx(c, Hex("#5E6A48"), Sstep(0.62f, 0.8f, TextureForge.Fbm(u, v, 6, 6, 3, 412)) * 0.5f);   // dried mud
                c *= Mathf.Lerp(0.45f, 1f, crev);
                c *= 0.9f + 0.2f * (H2(x, y, 413) - 0.5f);
                return c;
            });
            var bone = TextureForge.SavePsx("T_CrocTeeth", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#5E5444"), Hex("#B4A68A"), 0.4f + 0.5f * TextureForge.Fbm(u, v, 4, 8, 3, 421));
                c = Mx(c, Hex("#3A2418"), Sstep(0.6f, 0.75f, TextureForge.Fbm(u, v, 6, 6, 2, 422)) * 0.7f);
                return c * (0.9f + 0.2f * (H2(x, y, 423) - 0.5f));
            });
            var flesh = TextureForge.SavePsx("T_CrocMouth", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#2A0A0C"), Hex("#6A1E1E"), TextureForge.Fbm(u, v, 6, 6, 3, 431));
                return c * (0.85f + 0.3f * (H2(x, y, 432) - 0.5f));
            });

            Matte(Lit("M_BomberMetal", Color.white, metal, null, 0f, 1f, null, false));
            Matte(Lit("M_CrocHide", Color.white, hide, null, 0f, 1f, null, false));
            Matte(Lit("M_CrocTeeth", Color.white, bone, null, 0f, 1f, null, false));
            Matte(Lit("M_CrocMouth", Color.white, flesh, null, 0f, 1f, null, false));
            Matte(Lit("M_BombIron", new Color(0.62f, 0.66f, 0.5f), metal, null, 0f, 1f, null, false));
            Matte(Lit("M_BombBand", TextureForge.Hex("#4A1210"), null, null, 0f));
            Glow("M_CrocEye", TextureForge.Hex("#B8C03A"), 2.6f);         // sickly toxic glow (ART_DIRECTION accent)
            Glow("M_BomberGlass", TextureForge.Hex("#2A2036"), 0f, TextureForge.Hex("#C8781E") * 0.22f);   // lantern-lit cockpit
            Glow("M_BombEye", TextureForge.Hex("#8A1A10"), 0f);           // emission driven at runtime (blink)
            Glow("M_NavRed", TextureForge.Hex("#FF3020"), 3f);
            Glow("M_NavGreen", TextureForge.Hex("#7CFF60"), 2.2f);

            // ground marker for incoming bombs: thin rim + filled disk (additive ember)
            var rim = TextureForge.Save("T_FX_MarkRim", 128, 128, (x, y) =>
            {
                float u = x / 127f * 2 - 1, v = y / 127f * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = r < 1f && r > 0.9f ? 1f : r <= 0.9f ? Mathf.Clamp01((r - 0.6f) / 0.3f) * 0.18f : 0f;
                return new Color(1, 1, 1, a);
            }, aniso: 0);
            var disk = TextureForge.Save("T_FX_MarkDisk", 64, 64, (x, y) =>
            {
                float u = x / 63f * 2 - 1, v = y / 63f * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v);
                return new Color(1, 1, 1, r < 1f ? 0.55f + 0.45f * r * r : 0f);
            }, aniso: 0);
            foreach (var t in new[] { rim, disk })
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
                imp.wrapMode = TextureWrapMode.Clamp; imp.alphaIsTransparency = true; imp.filterMode = FilterMode.Point; imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            var markRim = Unlit("M_FX_BombRim", Color.white, rim, additive: true);
            var markFill = Unlit("M_FX_BombFill", Color.white, disk, additive: true);
            markRim.renderQueue = markFill.renderQueue = (int)RenderQueue.Transparent - 10;

            BuildBombShadowMaterial();
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.TexDir + "/T_FX_SoftDot.png") ?? TextureForge.BuildSoftDot();
            Unlit("M_FX_Fire", new Color(1f, 0.55f, 0.18f, 1f), dot, additive: true);
            Unlit("M_FX_Smoke", new Color(0.13f, 0.11f, 0.15f, 0.75f), dot);
        }

        static void Matte(Material m)
        {
            m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
        }

        static void Glow(string name, Color baseColor, float emissionK, Color? emission = null)
        {
            var m = Lit(name, baseColor, null, null, 0f);
            Matte(m);
            m.SetColor("_EmissionColor", emission ?? baseColor * emissionK);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
        }

        static void ConfigureBossImport()
        {
            foreach (var f in new[] { "Bombardiro", "AerialBomb", "LivingBomb" })
            {
                string path = $"{BossModelDir}/{f}.fbx";
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                if (imp == null) { Debug.LogError($"[TungSahur] Missing {path} (run Tools/Blender/build_bombardiro.py)"); continue; }
                imp.globalScale = 1f;
                imp.useFileScale = true;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importBlendShapes = false;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importNormals = ModelImporterNormals.Calculate;
                imp.normalSmoothingAngle = 35f;            // faceted PSX shading
                imp.importTangents = ModelImporterTangents.None;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                foreach (var n in BossMats)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
                }
                imp.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ FX
        static void BuildBossFX()
        {
            ParticleSystem Explosion(string file, float s, int fire, int smoke, int sparks, int debris, float light)
            {
                var root = NewPS(file, M("M_FX_Glow"), out var gr);         // core flash
                var m = root.main; m.startLifetime = 0.16f; m.startSpeed = 0f; m.startSize = 4.2f * s;
                m.startColor = new Color(1f, 0.78f, 0.45f, 1f);
                Burst(root, 1);
                var sh = root.shape; sh.enabled = false;
                SizeOverLife(root, 0.4f, 1.4f);
                var col = root.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);

                var fb = NewPS("Fireball", M("M_FX_Fire"), out _);
                fb.transform.SetParent(root.transform, false);
                var fm = fb.main; fm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f); fm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * s, 5f * s);
                fm.startSize = new ParticleSystem.MinMaxCurve(0.9f * s, 1.9f * s); fm.gravityModifier = -0.3f;
                fm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                Burst(fb, fire);
                var fs = fb.shape; fs.shapeType = ParticleSystemShapeType.Hemisphere; fs.radius = 0.4f * s;
                SizeOverLife(fb, 0.6f, 1.5f);
                var fc = fb.colorOverLifetime; fc.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.35f), new GradientColorKey(new Color(0.35f, 0.08f, 0.04f), 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
                fc.color = new ParticleSystem.MinMaxGradient(g);

                var sm = NewPS("Smoke", M("M_FX_Smoke"), out _);
                sm.transform.SetParent(root.transform, false);
                var smm = sm.main; smm.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f); smm.startSpeed = new ParticleSystem.MinMaxCurve(1f * s, 3.4f * s);
                smm.startSize = new ParticleSystem.MinMaxCurve(1.2f * s, 2.4f * s); smm.gravityModifier = -0.12f;
                smm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                var em = sm.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0.06f, (short)smoke) });
                var ss = sm.shape; ss.shapeType = ParticleSystemShapeType.Hemisphere; ss.radius = 0.6f * s;
                var lv = sm.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 0.6f; lv.dampen = 0.12f;
                SizeOverLife(sm, 0.6f, 1.8f);
                var rot = sm.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
                var sc = sm.colorOverLifetime; sc.enabled = true; sc.color = Grad(Color.white, Color.white, 1f, 0f);

                var sp = NewPS("Embers", M("M_FX_Spark"), out var spr);
                sp.transform.SetParent(root.transform, false);
                var spm = sp.main; spm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f); spm.startSpeed = new ParticleSystem.MinMaxCurve(6f * s, 15f * s);
                spm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f); spm.gravityModifier = 1.4f;
                spm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.5f, 0.15f));
                Burst(sp, sparks);
                var sps = sp.shape; sps.shapeType = ParticleSystemShapeType.Hemisphere; sps.radius = 0.3f;
                spr.renderMode = ParticleSystemRenderMode.Stretch; spr.velocityScale = 0.04f; spr.lengthScale = 1.5f;

                var db = NewPS("Debris", M("M_FX_Chip"), out _);
                db.transform.SetParent(root.transform, false);
                var dbm = db.main; dbm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f); dbm.startSpeed = new ParticleSystem.MinMaxCurve(4f * s, 10f * s);
                dbm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f * s); dbm.gravityModifier = 2.2f;
                dbm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.17f, 0.15f), new Color(0.36f, 0.3f, 0.26f));
                dbm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                Burst(db, debris);
                var dbs = db.shape; dbs.shapeType = ParticleSystemShapeType.Cone; dbs.angle = 35f; dbs.radius = 0.3f; dbs.rotation = new Vector3(-90, 0, 0);
                var dc = db.collision; dc.enabled = true; dc.type = ParticleSystemCollisionType.World; dc.bounce = 0.25f; dc.dampen = 0.6f; dc.collidesWith = 1;
                dc.quality = ParticleSystemCollisionQuality.Low;

                var lgo = new GameObject("Flash");
                lgo.transform.SetParent(root.transform, false);
                lgo.transform.localPosition = Vector3.up * 1.2f;
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point; l.color = Palette.Ember; l.range = 14f * s; l.intensity = 0f; l.shadows = LightShadows.None;
                var fl = lgo.AddComponent<FlashLight>(); fl.peakIntensity = light; fl.duration = 0.5f;
                return SavePS(root, file);
            }
            Explosion("FX_Explosion", 1f, 14, 12, 30, 14, 45f);
            Explosion("FX_BombPop", 0.55f, 6, 5, 14, 6, 14f);

            // one-shot dark smoke puff (wreck, crash)
            {
                var ps = NewPS("FX_Smoke", M("M_FX_Smoke"), out _);
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); m.gravityModifier = -0.1f; m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                Burst(ps, 8);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.8f;
                SizeOverLife(ps, 0.6f, 1.8f);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(ps, "FX_Smoke");
            }
        }

        static ParticleSystem EngineSmoke(Transform parent, Vector3 local)
        {
            var ps = NewPS("EngineSmoke", M("M_FX_Smoke"), out _);
            ps.transform.SetParent(parent, false);
            ps.transform.localPosition = local;
            ps.transform.localRotation = Quaternion.LookRotation(Vector3.back);
            var m = ps.main; m.loop = true; m.duration = 2f; m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f); m.gravityModifier = -0.04f; m.maxParticles = 80;
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var em = ps.emission; em.rateOverTime = 7f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.15f;
            SizeOverLife(ps, 0.6f, 2.4f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 0.9f, 0f);
            return ps;
        }

        // ------------------------------------------------------------------ prefabs
        static GameObject Child(string name, Transform parent, Vector3 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            return go;
        }

        static void AddHurtbox(Transform parent, string name, Vector3 center, Vector3? box = null, float radius = 0f, float height = 0f, int dir = 2)
        {
            var go = Child(name, parent, Vector3.zero);
            go.layer = LayerHurtbox;
            if (box.HasValue)
            {
                var b = go.AddComponent<BoxCollider>(); b.isTrigger = true; b.center = center; b.size = box.Value;
            }
            else
            {
                var c = go.AddComponent<CapsuleCollider>(); c.isTrigger = true; c.center = center; c.radius = radius; c.height = height; c.direction = dir;
            }
            go.AddComponent<Hurtbox>();
        }

        static GameObject Quad(string name, Transform parent, Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = name;
            q.transform.SetParent(parent, false);
            q.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q;
        }

        static void BuildAerialBombPrefab()
        {
            var root = new GameObject("AerialBomb");
            var bomb = root.AddComponent<AerialBomb>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{BossModelDir}/AerialBomb.fbx"));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            // no glowing marker: the warning is the bomb's own shadow on the ground
            var shadowRoot = Child("Shadow", root.transform, Vector3.zero);
            var shadow = Quad("Blob", shadowRoot.transform, M("M_FX_BombShadow"));
            bomb.model = model.transform;
            bomb.shadow = shadowRoot.transform;
            bomb.shadowRenderer = shadow.GetComponent<Renderer>();
            PrefabUtility.SaveAsPrefabAsset(root, AerialBombPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void BuildLivingBombPrefab()
        {
            var root = new GameObject("LivingBomb");
            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.3f; cc.height = 0.75f; cc.center = new Vector3(0, 0.42f, 0); cc.stepOffset = 0.2f; cc.skinWidth = 0.03f; cc.minMoveDistance = 0f;
            var modelRoot = Child("Model", root.transform, Vector3.zero);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{BossModelDir}/LivingBomb.fbx"));
            model.transform.SetParent(modelRoot.transform, false);
            var lb = root.AddComponent<LivingBomb>();
            lb.model = modelRoot.transform;
            lb.legs = Enumerable.Range(0, 4).Select(i => model.GetComponentsInChildren<Transform>().First(t => t.name == $"LBomb_Leg{i}")).ToArray();
            var body = model.GetComponentsInChildren<Renderer>().First(r => r.name == "LBomb_Body");
            lb.bodyRenderer = body;
            lb.eyeMaterialIndex = System.Array.FindIndex(body.sharedMaterials, m => m != null && m.name == "M_BombEye");

            AddHurtbox(root.transform, "Hurtbox", new Vector3(0, 0.45f, 0.05f), null, 0.42f, 1.1f, 2);
            var lockPoint = Child("LockPoint", root.transform, new Vector3(0, 0.55f, 0));
            var tg = root.AddComponent<Targetable>();
            tg.lockPoint = lockPoint.transform; tg.lockRadius = 0.3f; tg.displayName = "Living Bomb";

            // sputtering fuse
            var ps = NewPS("FuseSpark", M("M_FX_Spark"), out var r);
            ps.transform.SetParent(modelRoot.transform, false);
            ps.transform.localPosition = new Vector3(0, body.bounds.max.y + 0.02f, -0.1f);
            var m = ps.main; m.loop = true; m.playOnAwake = false; m.duration = 1f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f); m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f); m.gravityModifier = 0.8f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 26f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.02f;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1f;
            lb.fuseSpark = ps;

            SetLayerRecursive(root, LayerEnemy);
            root.transform.Find("Hurtbox").gameObject.layer = LayerHurtbox;
            PrefabUtility.SaveAsPrefabAsset(root, LivingBombPrefabPath);
            Object.DestroyImmediate(root);
        }

        const float BossPivotHeight = 1.8f;

        static void BuildBossPrefab()
        {
            var root = new GameObject("Bombardiro");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            var pivot = Child("Pivot", root.transform, Vector3.up * BossPivotHeight);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{BossModelDir}/Bombardiro.fbx"));
            model.name = "Model";
            model.transform.SetParent(pivot.transform, false);
            model.transform.localPosition = Vector3.down * BossPivotHeight;
            // the croc head must point down +Z; flip if the exporter put it behind
            var head = model.GetComponentsInChildren<Renderer>().First(r => r.name == "Bomber_Head");
            if (head.bounds.center.z < 0f) model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            Transform Part(string n) => model.GetComponentsInChildren<Transform>().First(t => t.name == n);

            // hurtboxes (pivot space: y = 0 is the fuselage centreline, +Z the snout)
            AddHurtbox(pivot.transform, "Hurt_Fuselage", new Vector3(0, 0.1f, -1.2f), null, 0.95f, 7.6f, 2);
            AddHurtbox(pivot.transform, "Hurt_Head", new Vector3(0, -0.15f, 4.4f), null, 1.0f, 4.8f, 2);
            AddHurtbox(pivot.transform, "Hurt_Wings", new Vector3(0, 0.85f, 0.05f), new Vector3(10.4f, 0.5f, 1.9f));
            AddHurtbox(pivot.transform, "Hurt_Tail", new Vector3(0, 0.9f, -4.4f), new Vector3(4f, 2.2f, 1.2f));
            // solid body while it sits on the field
            var solid = Child("Body_Solid", pivot.transform, Vector3.zero);
            var c1 = solid.AddComponent<CapsuleCollider>(); c1.center = new Vector3(0, 0.05f, -1.0f); c1.radius = 0.9f; c1.height = 7.4f; c1.direction = 2;
            var c2 = solid.AddComponent<CapsuleCollider>(); c2.center = new Vector3(0, -0.2f, 4.3f); c2.radius = 0.95f; c2.height = 4.6f; c2.direction = 2;

            var lockPoint = Child("LockPoint", pivot.transform, new Vector3(0, 0.25f, 1.2f));
            var socket = Child("BombSocket", pivot.transform, new Vector3(0, -0.95f, -0.3f));
            var smoke = new[]
            {
                EngineSmoke(pivot.transform, new Vector3(3.9f, 0.75f, -0.9f)),
                EngineSmoke(pivot.transform, new Vector3(-3.9f, 0.75f, -0.9f)),
            };

            // navigation lights: port red, starboard green (Unity -X is the plane's left), blinking belly beacon
            GameObject NavLamp(string n, Vector3 local, string mat, float size)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.name = n;
                c.transform.SetParent(pivot.transform, false);
                c.transform.localPosition = local;
                c.transform.localScale = Vector3.one * size;
                var r = c.GetComponent<MeshRenderer>();
                r.sharedMaterial = M(mat);
                r.shadowCastingMode = ShadowCastingMode.Off;
                return c;
            }
            NavLamp("Nav_Port", new Vector3(-5.25f, 0.98f, -0.05f), "M_NavRed", 0.2f);
            NavLamp("Nav_Starboard", new Vector3(5.25f, 0.98f, -0.05f), "M_NavGreen", 0.2f);
            var beaconGlow = NavLamp("Beacon", new Vector3(0, -0.93f, -1.6f), "M_NavRed", 0.24f);
            var bl = Child("BeaconLight", pivot.transform, new Vector3(0, -1.2f, -1.6f)).AddComponent<Light>();
            bl.type = LightType.Point; bl.color = new Color(1f, 0.2f, 0.12f); bl.range = 13f; bl.intensity = 6f; bl.shadows = LightShadows.None;

            var audio = root.AddComponent<AudioSource>();
            audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxForge.Dir + "/sfx_engine_loop.wav") ?? SfxForge.EngineLoop();
            audio.loop = true; audio.playOnAwake = false; audio.spatialBlend = 0.8f; audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 10f; audio.maxDistance = 90f; audio.dopplerLevel = 0f; audio.volume = 0f;

            var boss = root.AddComponent<BombardiroBoss>();
            boss.pivot = pivot.transform;
            boss.jaw = Part("Bomber_Jaw");
            boss.props = Enumerable.Range(0, 4).Select(i => Part($"Bomber_Prop{i}")).ToArray();
            boss.bombSocket = socket.transform;
            boss.beacon = bl;
            boss.beaconGlow = beaconGlow.GetComponent<Renderer>();
            boss.flashRenderers = model.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer).ToArray();
            boss.engineAudio = audio;
            boss.engineSmoke = smoke;
            boss.bodyColliders = solid.GetComponents<Collider>();
            boss.bombPrefab = AssetDatabase.LoadAssetAtPath<AerialBomb>(AerialBombPrefabPath);
            boss.livingBombPrefab = AssetDatabase.LoadAssetAtPath<LivingBomb>(LivingBombPrefabPath);
            var tg = root.AddComponent<Targetable>();
            tg.lockPoint = lockPoint.transform; tg.lockRadius = 1.2f; tg.displayName = boss.displayName; tg.rangeScale = 1.75f;

            SetLayerRecursive(root, LayerEnemy);
            foreach (var hb in root.GetComponentsInChildren<Hurtbox>(true)) hb.gameObject.layer = LayerHurtbox;
            PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ scene
        static void PlaceBoss(Transform actorRoot, GameObject systems, TungSahur.Player.PlayerController pc, UI.HUD hud, Transform spawn)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
            if (prefab == null) { Debug.LogWarning("[TungSahur] Boss prefab missing: run BuildBoss()"); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(actorRoot);
            go.transform.position = new Vector3(0, 30f, 60f);
            var boss = go.GetComponent<BombardiroBoss>();
            boss.arenaCenter = Vector3.zero;
            var enc = systems.AddComponent<BossEncounter>();
            enc.boss = boss; enc.player = pc; enc.hud = hud; enc.spawnPoint = spawn;
            enc.bosses.Add(boss);
        }
    }
}
