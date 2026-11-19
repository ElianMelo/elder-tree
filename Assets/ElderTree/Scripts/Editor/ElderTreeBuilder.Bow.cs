using System.IO;
using System.Linq;
using ElderTree.Combat;
using ElderTree.Player;
using UnityEditor;
using UnityEngine;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// Bow, arrow and quiver (modelled by Tools/Blender/build_bow.py): materials, import settings,
    /// the arrow projectile prefab, back slots on the player, the BowRig wiring and the HUD icon.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string BowPath = Root + "/Art/Models/ElderTree_Bow.fbx";
        public const string ArrowPath = Root + "/Art/Models/ElderTree_Arrow.fbx";
        public const string QuiverPath = Root + "/Art/Models/ElderTree_Quiver.fbx";
        public const string ArrowPrefabPath = Root + "/Prefabs/Arrow.prefab";

        static readonly string[] BowMats = { "M_BowWood", "M_BowHorn", "M_ArrowShaft", "M_FletchRed", "M_FletchCream", "M_Quiver", "M_Rope", "M_Iron" };

        // Back-slot poses in the character's bind space (feet at origin, facing +Z). Tuned on captures.
        public static Vector3 BowBackPos = new Vector3(-0.02f, 1.02f, -0.43f);
        public static float BowBackTilt = 30f;
        public static Vector3 QuiverBackPos = new Vector3(0.15f, 1.06f, -0.36f);
        public static Vector3 QuiverBackEuler = new Vector3(-10f, 0f, -24f);
        public static Vector3 ClubBackPos = new Vector3(-0.2f, 1.38f, -0.33f);
        public static Vector3 ClubBackDir = new Vector3(0.42f, -1f, 0f);

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Art/Textures/{name}.png");

        [MenuItem("Elder Tree/4a. Build Bow Assets")]
        public static string BuildBowAssets()
        {
            // PSX dark-fantasy weapons (Docs/ART_DIRECTION.md): dark oiled wood, black horn, dried-blood and
            // bone fletching, scuffed leather quiver. Point-filtered dithered textures from TextureForge.
            var woodA = Tex("T_DarkWood_Albedo") ?? TextureForge.BuildDarkWood();
            var leatherA = Tex("T_Leather_Albedo") ?? TextureForge.BuildLeather();
            Lit("M_BowWood", Color.white, woodA, null, 0f, 1f, new Vector2(1f, 3f));
            Lit("M_BowHorn", TextureForge.Hex("#141012"), null, null, 0f);
            Lit("M_ArrowShaft", TextureForge.Hex("#B8A890"), woodA, null, 0f, 1f, new Vector2(1f, 8f));
            Lit("M_FletchRed", TextureForge.Hex("#4A1712"), null, null, 0f);
            Lit("M_FletchCream", TextureForge.Hex("#7D7466"), null, null, 0f);
            Lit("M_Quiver", Color.white, leatherA, null, 0f, 1f, new Vector2(2f, 2f));
            Lit("M_BowString", TextureForge.Hex("#6E6656"), null, null, 0f);

            foreach (var path in new[] { BowPath, ArrowPath, QuiverPath })
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                if (imp == null) { Debug.LogError($"[ElderTree] Missing {path} (run Tools/Blender/build_bow.py)"); continue; }
                imp.globalScale = 1f;
                imp.useFileScale = true;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importBlendShapes = path == BowPath;
                imp.importNormals = ModelImporterNormals.Calculate;
                imp.normalSmoothingAngle = 50f;
                imp.importBlendShapeNormals = ModelImporterNormals.Calculate;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                foreach (var n in BowMats)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
                }
                imp.SaveAndReimport();
            }
            BuildArrowPrefab();
            AssetDatabase.SaveAssets();
            return "Bow assets built";
        }

        /// <summary>Rotates an arrow model child so its shaft runs from the parent's origin along +Z.</summary>
        static void AlignArrow(Transform parent, GameObject model)
        {
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var mf = model.GetComponentInChildren<MeshFilter>();
            var b = mf.sharedMesh.bounds;
            // mesh bounds in the parent's space
            var m = parent.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3 c = m.MultiplyPoint3x4(b.center);
            Vector3 e = new Vector3(Mathf.Abs(m.MultiplyVector(Vector3.right * b.extents.x).magnitude),
                                    Mathf.Abs(m.MultiplyVector(Vector3.up * b.extents.y).magnitude),
                                    Mathf.Abs(m.MultiplyVector(Vector3.forward * b.extents.z).magnitude));
            Vector3 along = m.MultiplyVector(e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward).normalized;
            if (Vector3.Dot(along, c) < 0f) along = -along;
            model.transform.localRotation = Quaternion.FromToRotation(along, Vector3.forward);
        }

        static void BuildArrowPrefab()
        {
            var root = new GameObject("Arrow");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPath));
            AlignArrow(root.transform, model);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(root.transform, false);
            trailGo.transform.localPosition = new Vector3(0, 0, 0.12f);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = 0.14f;
            trail.minVertexDistance = 0.05f;
            trail.widthCurve = AnimationCurve.EaseInOut(0, 0.03f, 1, 0f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0), new GradientColorKey(new Color(1f, 0.85f, 0.6f), 1) },
                      new[] { new GradientAlphaKey(0.55f, 0), new GradientAlphaKey(0f, 1) });
            trail.colorGradient = g;
            trail.sharedMaterial = M("M_FX_Trail");
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;

            var proj = root.AddComponent<ArrowProjectile>();
            proj.trail = trail;
            proj.hitMask = (1 << 0) | (1 << LayerHurtbox) | (1 << LayerEnemy);
            Directory.CreateDirectory(PrefabDir);
            PrefabUtility.SaveAsPrefabAsset(root, ArrowPrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Unity imports a blend-shape-only mesh with a MeshRenderer; swap it for a SkinnedMeshRenderer
        /// so the "Draw" shape can be driven at runtime.
        /// </summary>
        static SkinnedMeshRenderer EnsureSkinned(GameObject bow)
        {
            var smr = bow.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr) return smr;
            var mf = bow.GetComponentInChildren<MeshFilter>();
            var mr = mf.GetComponent<MeshRenderer>();
            var go = mf.gameObject;
            var mesh = mf.sharedMesh;
            var mats = mr.sharedMaterials;
            Object.DestroyImmediate(mr);
            Object.DestroyImmediate(mf);
            smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterials = mats;
            smr.updateWhenOffscreen = true;
            return smr;
        }

        static Quaternion BowAxes(Transform bow)
        {
            Transform F(string n) => bow.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            Vector3 top = bow.InverseTransformPoint(F("StringTop").position);
            Vector3 bot = bow.InverseTransformPoint(F("StringBottom").position);
            Vector3 rest = bow.InverseTransformPoint(F("ArrowRest").position);
            Vector3 up = (top - bot).normalized;
            Vector3 fwd = rest - (top + bot) * 0.5f;
            fwd.x = 0f;   // ignore the shelf's sideways offset: the limbs bend in the bow's YZ plane
            fwd -= up * Vector3.Dot(fwd, up);
            return Quaternion.LookRotation(fwd.normalized, up);
        }

        static Transform Socket(string name, Transform bone, Vector3 worldPos, Quaternion worldRot)
        {
            var s = new GameObject(name).transform;
            s.SetPositionAndRotation(worldPos, worldRot);
            s.SetParent(bone, true);
            return s;
        }

        /// <summary>Adds the bow, quiver, back slots and BowRig to the player being built (model in bind pose at the origin).</summary>
        static void AttachBow(GameObject root, Animator animator, PlayerController pc, Transform club)
        {
            var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                        ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            var model = animator.gameObject;

            // bow slung across the back, string side toward the body so the limbs hug the log
            var bowGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BowPath));
            PrefabUtility.UnpackPrefabInstance(bowGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            bowGo.name = "Bow";
            EnsureSkinned(bowGo);
            var axes = BowAxes(bowGo.transform);
            Vector3 up = Quaternion.Euler(0, 0, BowBackTilt) * Vector3.up;
            var bowSocket = Socket("BowBackSocket", chest, BowBackPos, Quaternion.LookRotation(Vector3.back, up) * Quaternion.Inverse(axes));
            bowGo.transform.SetParent(bowSocket, false);
            bowGo.transform.localPosition = Vector3.zero;
            bowGo.transform.localRotation = Quaternion.identity;
            var smr = EnsureSkinned(bowGo);
            if (smr) { smr.updateWhenOffscreen = true; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }

            var quiverSocket = Socket("QuiverSocket", chest, QuiverBackPos, Quaternion.Euler(QuiverBackEuler));
            var quiver = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(QuiverPath));
            quiver.name = "Quiver";
            quiver.transform.SetParent(quiverSocket, false);
            var quiverTop = new GameObject("QuiverTop").transform;
            quiverTop.SetParent(quiverSocket, false);
            quiverTop.localPosition = new Vector3(0, 0.34f, 0);

            var clubSocket = Socket("ClubBackSocket", chest, ClubBackPos,
                Quaternion.LookRotation(Vector3.back, ClubBackDir.normalized) * Quaternion.Euler(0, 0, 0));
            // the club runs along its local +Y: point it along ClubBackDir
            clubSocket.rotation = Quaternion.FromToRotation(Vector3.up, ClubBackDir.normalized);

            var stringGo = new GameObject("BowString");
            stringGo.transform.SetParent(model.transform, false);
            var lr = stringGo.AddComponent<LineRenderer>();
            lr.positionCount = 3;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.0045f;
            lr.numCapVertices = 0;
            lr.sharedMaterial = M("M_BowString");
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.alignment = LineAlignment.View;

            var nocked = new GameObject("NockedArrow").transform;
            nocked.SetParent(model.transform, false);
            var arrowModel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPath));
            AlignArrow(nocked, arrowModel);

            var rig = model.AddComponent<BowRig>();
            Transform F(string n) => bowGo.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            rig.player = pc;
            rig.animator = animator;
            rig.bow = bowGo.transform;
            rig.bowRenderer = smr;
            rig.stringTop = F("StringTop"); rig.stringBottom = F("StringBottom");
            rig.stringTopDrawn = F("StringTopDrawn"); rig.stringBottomDrawn = F("StringBottomDrawn");
            rig.arrowRest = F("ArrowRest");
            rig.bowString = lr;
            rig.nockedArrow = nocked;
            rig.bowBackSocket = bowSocket;
            rig.club = club;
            rig.clubBackSocket = clubSocket;
            rig.quiverTop = quiverTop;

            pc.bowRig = rig;
            pc.arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefabPath).GetComponent<ArrowProjectile>();
            pc.aimMask = (1 << 0) | (1 << LayerHurtbox) | (1 << LayerEnemy);
        }

        // ------------------------------------------------------------------ HUD icon
        /// <summary>
        /// The bow icon is rendered in Blender (Tools/Blender/build_bow.py --icon, transparent PNG);
        /// this only applies the UI import settings.
        /// </summary>
        public static string ImportBowIcon()
        {
            string path = Root + "/UI/Icon_Bow.png";
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return "Bow icon missing (run build_bow.py --icon)";
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return "Bow icon imported";
        }
    }
}
