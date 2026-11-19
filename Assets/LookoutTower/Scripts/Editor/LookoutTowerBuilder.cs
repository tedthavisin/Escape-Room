using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Builds everything for the fire lookout tower from the imported FBX + textures:
    /// URP materials, prefabs (tower, openables, cab props, trees, rocks) and a night forest-clearing scene.
    /// Menu: Tools ▸ Lookout Tower. Safe to re-run: assets are updated in place.
    /// </summary>
    public static class LookoutTowerBuilder
    {
        public const string Root = "Assets/LookoutTower";
        const string TexDir = Root + "/Textures";
        const string ModelDir = Root + "/Models";
        const string MatDir = Root + "/Materials";
        const string PrefabDir = Root + "/Prefabs";
        const string PropDir = PrefabDir + "/Props";
        const string NatureDir = PrefabDir + "/Nature";
        const string SceneDir = Root + "/Scenes";
        const string SettingsDir = Root + "/Settings";
        const string TowerFbx = ModelDir + "/LookoutTower.fbx";
        const string PropsFbx = ModelDir + "/CabProps.fbx";
        const string ForestFbx = ModelDir + "/Forest.fbx";
        public const string TowerPrefabPath = PrefabDir + "/LookoutTower.prefab";
        public const string ScenePath = SceneDir + "/LookoutTower_ForestClearing.unity";

        internal const float FloorY = 30.5f;     // top of the cab floor / catwalk (100 ft)
        internal const float LowTopY = 28.1f;    // top of the switchback stairs; the steep ladder climbs the last 2.4 m
        internal const int LowFlights = 20;      // switchback flights, 8 risers each

        /// <summary>Bump when the builder's output changes, so older builds get offered a rebuild.</summary>
        public const int BuilderVersion = 9;
        /// <summary>One line for the rebuild prompt: what changed in this builder version.</summary>
        public const string WhatChanged = "fire finder puzzle: sight the smoke circled on the wall map and the ranger's lamp answers (digit 7)";
        static string StampPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LookoutTowerSource", "builder_version.txt"));

        public static int BuiltVersion()
        {
            try { return File.Exists(StampPath) && int.TryParse(File.ReadAllText(StampPath).Trim(), out int v) ? v : 0; }
            catch (Exception) { return 0; }
        }
        static readonly Color FogColor = new Color(28f / 255f, 34f / 255f, 46f / 255f);   // moonlit haze; sky exposure below lifts the horizon to match

        // ------------------------------------------------------------------ material table
        class MatSpec
        {
            public string name, tex;
            public float tile = 1f, smoothness = 0.5f, metallic;
            public Color tint = Color.white, emission = Color.black;
            public bool transparent, doubleSided;
            public MatSpec(string name, string tex, float tile) { this.name = name; this.tex = tex; this.tile = tile; }
        }

        static readonly MatSpec[] Specs =
        {
            new MatSpec("M_Timber", "Timber", 1f),
            new MatSpec("M_PlanksWeathered", "PlanksWeathered", 2f),
            new MatSpec("M_PlanksInterior", "PlanksInterior", 2f),
            new MatSpec("M_Siding", "Siding", 2f),
            new MatSpec("M_TrimGreen", "TrimGreen", 1f),
            new MatSpec("M_TrimCream", "TrimCream", 1f),
            new MatSpec("M_Paneling", "Paneling", 2f),
            new MatSpec("M_Roof", "RoofShakes", 2f),
            new MatSpec("M_Iron", "Iron", 1f),
            new MatSpec("M_Concrete", "Concrete", 1f),
            new MatSpec("M_Glass", "GlassGrime", 1f) { transparent = true },
            new MatSpec("M_Brass", "Brass", 0.5f),
            new MatSpec("M_Wool", "Wool", 0.5f),
            new MatSpec("M_Ticking", "Ticking", 1f),
            new MatSpec("M_Map", "Map", 1f),
            new MatSpec("M_PaintedMetal", "PaintedMetal", 0.5f),
            new MatSpec("M_WoodFurniture", "WoodFurniture", 1f),
            new MatSpec("M_Bark", "Bark", 1f),
            new MatSpec("M_Needles", "Needles", 1f) { doubleSided = true },
            new MatSpec("M_Rock", "Rock", 2f),
            new MatSpec("M_PlasticBlack", null, 1f) { tint = new Color(0.035f, 0.035f, 0.035f), smoothness = 0.55f },
            new MatSpec("M_DialGlow", null, 1f) { tint = new Color(1f, 0.62f, 0.3f), smoothness = 0.8f, emission = new Color(1.4f, 0.75f, 0.28f) },
            new MatSpec("M_LanternGlass", null, 1f) { tint = new Color(1f, 0.85f, 0.62f), smoothness = 0.9f, emission = new Color(2.6f, 1.4f, 0.55f) },
            new MatSpec("M_GlassInsulator", null, 1f) { tint = new Color(0.22f, 0.45f, 0.36f), smoothness = 0.9f },
            new MatSpec("M_BookCover", null, 1f) { tint = new Color(0.33f, 0.09f, 0.07f), smoothness = 0.25f },
            new MatSpec("M_Paper", null, 1f) { tint = new Color(0.82f, 0.79f, 0.7f), smoothness = 0.1f },
            new MatSpec("M_RedPaint", null, 1f) { tint = new Color(0.48f, 0.07f, 0.05f), smoothness = 0.45f },
        };

        // ================================================================== menu
        [MenuItem("Tools/Lookout Tower/Build Everything", priority = 0)]
        public static void BuildEverythingMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildEverything(true);
        }

        [MenuItem("Tools/Lookout Tower/Rebuild Materials", priority = 20)]
        public static void RebuildMaterialsMenu()
        {
            EnsureFolders();
            BuildMaterials();
            RemapModelMaterials();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Lookout Tower/Open Forest Clearing Scene", priority = 21)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("Lookout Tower", "Build the scene first (Tools ▸ Lookout Tower ▸ Build Everything).", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Lookout Tower/Capture Preview Screenshots", priority = 40)]
        public static void CaptureScreenshotsMenu()
        {
            string dir = CaptureScreenshots();
            if (dir != null) EditorUtility.RevealInFinder(dir);
        }

        /// <summary>Entry point for -executeMethod / CI: builds without dialogs and captures previews.</summary>
        public static void BatchBuildAndCapture()
        {
            BuildEverything(false);
            CaptureScreenshots();
        }

        static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LookoutTowerSource", "build_log.txt"));

        internal static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch (Exception) { /* logging must never break the build */ }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;
            Log($"{type}: {condition}" + (type == LogType.Exception || type == LogType.Error ? "\n" + stackTrace : ""));
        }

        public static void BuildEverything(bool interactive)
        {
            try { File.WriteAllText(LogPath, ""); } catch (Exception) { }
            Log($"Build started (Unity {Application.unityVersion}, pipeline {GraphicsSettings.currentRenderPipeline?.name})");
            Application.logMessageReceived += OnLog;
            try
            {
                Progress("Preparing", 0.02f);
                EnsureFolders();
                if (AssetDatabase.LoadAssetAtPath<GameObject>(TowerFbx) == null)
                    throw new Exception($"Could not load {TowerFbx}. Is the import finished?");
                // Work inside a fresh scene so the user's open scene is never touched.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                Progress("Materials", 0.08f);
                BuildMaterials();
                RemapModelMaterials();

                foreach (var mp in new[] { TowerFbx, PropsFbx, ForestFbx })
                {
                    var a = AssetDatabase.LoadAssetAtPath<GameObject>(mp);
                    if (a == null) { Log($"MISSING model {mp}"); continue; }
                    var first = a.transform.childCount > 0 ? a.transform.GetChild(0) : null;
                    Log($"{Path.GetFileName(mp)}: root rot {a.transform.localEulerAngles} scale {a.transform.localScale}, {a.transform.childCount} children" +
                        (first != null ? $", first '{first.name}' pos {first.localPosition} rot {first.localEulerAngles} scale {first.localScale}" : ""));
                }
                Progress("Cab props", 0.25f);
                var props = BuildPropPrefabs();
                Progress("Trees and rocks", 0.4f);
                var nature = BuildNaturePrefabs();
                Progress("Lookout tower prefab", 0.55f);
                var tower = BuildTowerPrefab(props);

                Progress("Forest clearing scene", 0.7f);
                BuildScene(scene, tower, nature);
                AssetDatabase.SaveAssets();
                Debug.Log($"[LookoutTower] Built {TowerPrefabPath} and {ScenePath}. Press Play to walk around (WASD, E, F, C, T).");
                Log("Build succeeded");
                try { File.WriteAllText(StampPath, BuilderVersion.ToString()); } catch (Exception) { }
                Progress("Walkability test", 0.95f);
                try { LookoutWalkabilityTest.Run(out _); }
                catch (Exception e) { Log("Walkability test could not run: " + e.Message); }
                if (interactive)
                {
                    var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                    if (sceneAsset != null) EditorGUIUtility.PingObject(sceneAsset);
                }
            }
            catch (Exception e)
            {
                Log("FAILED: " + e);
                Debug.LogException(e);
                if (interactive) EditorUtility.DisplayDialog("Lookout Tower", "Build failed: " + e.Message + "\n\nSee the Console for details.", "OK");
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                EditorUtility.ClearProgressBar();
                Log("Build finished");
            }
        }

        static void Progress(string what, float t)
        {
            Log(what);
            EditorUtility.DisplayProgressBar("Building lookout tower", what, t);
        }

        static void EnsureFolders()
        {
            foreach (var p in new[] { MatDir, PrefabDir, PropDir, NatureDir, SceneDir, SettingsDir })
            {
                if (AssetDatabase.IsValidFolder(p)) continue;
                string parent = Path.GetDirectoryName(p).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(p));
            }
        }

        // ================================================================== materials
        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");
        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        static void BuildMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new Exception("URP Lit shader not found. Is the Universal Render Pipeline package installed?");
            foreach (var s in Specs)
            {
                string path = $"{MatDir}/{s.name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(lit) { name = s.name };
                    AssetDatabase.CreateAsset(m, path);
                }
                else if (m.shader != lit) m.shader = lit;
                SetupLit(m, s);
                EditorUtility.SetDirty(m);
            }
            // night sky
            var skyShader = Shader.Find("Skybox/Panoramic");
            var skyTex = Tex("T_SkyNight");
            if (skyShader != null && skyTex != null)
            {
                string path = $"{MatDir}/M_SkyNight.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (sky == null) { sky = new Material(skyShader); AssetDatabase.CreateAsset(sky, path); }
                sky.shader = skyShader;
                sky.SetTexture("_MainTex", skyTex);
                sky.SetFloat("_Mapping", 1f);
                sky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
                sky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
                sky.SetFloat("_ImageType", 0f);
                sky.SetFloat("_Exposure", 1.2f);   // measured: below-horizon sky then renders exactly FogColor (28,34,46), so the terrain edge vanishes into it
                sky.SetFloat("_Rotation", 0f);
                EditorUtility.SetDirty(sky);
            }
            AssetDatabase.SaveAssets();
        }

        static void SetupLit(Material m, MatSpec s)
        {
            m.SetColor("_BaseColor", s.tint);
            m.SetColor("_Color", s.tint);
            m.SetFloat("_WorkflowMode", 1f);
            if (!string.IsNullOrEmpty(s.tex))
            {
                var alb = Tex($"T_{s.tex}_Albedo");
                var nrm = Tex($"T_{s.tex}_Normal");
                var msk = Tex($"T_{s.tex}_Mask");
                var scale = Vector2.one / Mathf.Max(0.01f, s.tile);
                m.SetTexture("_BaseMap", alb);
                m.SetTexture("_MainTex", alb);
                m.SetTextureScale("_BaseMap", scale);
                m.SetTextureScale("_MainTex", scale);
                if (nrm != null)
                {
                    m.SetTexture("_BumpMap", nrm);
                    m.SetFloat("_BumpScale", 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
                else m.DisableKeyword("_NORMALMAP");
                if (msk != null)
                {
                    // R metallic, G occlusion, A smoothness (URP packing)
                    m.SetTexture("_MetallicGlossMap", msk);
                    m.SetFloat("_Smoothness", 1f);
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetTexture("_OcclusionMap", msk);
                    m.SetFloat("_OcclusionStrength", 1f);
                    m.EnableKeyword("_OCCLUSIONMAP");
                }
                else
                {
                    m.DisableKeyword("_METALLICSPECGLOSSMAP");
                    m.DisableKeyword("_OCCLUSIONMAP");
                    m.SetFloat("_Smoothness", s.smoothness);
                    m.SetFloat("_Metallic", s.metallic);
                }
            }
            else
            {
                m.SetTexture("_BaseMap", null);
                m.SetTexture("_MainTex", null);
                m.SetFloat("_Smoothness", s.smoothness);
                m.SetFloat("_Metallic", s.metallic);
            }

            if (s.emission.maxColorComponent > 0f)
            {
                m.SetColor("_EmissionColor", s.emission);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            m.SetFloat("_Cull", s.doubleSided ? (float)UnityEngine.Rendering.CullMode.Off : (float)UnityEngine.Rendering.CullMode.Back);
            m.doubleSidedGI = s.doubleSided;

            if (s.transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.SetFloat("_CastShadows", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetShaderPassEnabled("ShadowCaster", false);
                m.SetShaderPassEnabled("DepthOnly", false);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.SetFloat("_CastShadows", 1f);
                m.SetOverrideTag("RenderType", "Opaque");
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetShaderPassEnabled("ShadowCaster", true);
                m.SetShaderPassEnabled("DepthOnly", true);
                m.renderQueue = -1;
            }
        }

        static void RemapModelMaterials()
        {
            foreach (var path in new[] { TowerFbx, PropsFbx, ForestFbx })
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x => x.name).ToList();
                foreach (var kv in mi.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material) && !names.Contains(kv.Key.name)) names.Add(kv.Key.name);
                bool changed = false;
                foreach (var n in names.Distinct())
                {
                    var m = Mat(n);
                    if (m == null) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                    mi.GetExternalObjectMap().TryGetValue(id, out var current);
                    if (current == m) continue;
                    mi.AddRemap(id, m);
                    changed = true;
                }
                if (changed) mi.SaveAndReimport();
            }
        }

        static void AssignMaterials(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = Mat(mats[i].name.Replace(" (Instance)", "").Trim());
                    if (m != null) mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        // ================================================================== helpers
        static GameObject InstantiateModel(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new Exception("Missing model " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.position = Vector3.zero;   // keep the importer's root rotation/scale (axis conversion)
            NormalizeAxes(go);
            AssignMaterials(go);
            return go;
        }

        /// <summary>
        /// Blender's FBX export arrives with every object turned 180° about Y. Undo that on the direct children so the
        /// layout matches the design frame (Blender x,y,z -> Unity x,z,y): door on -Z (south), identity child rotations.
        /// </summary>
        static void NormalizeAxes(GameObject model)
        {
            var kids = model.transform.Cast<Transform>().ToList();
            if (kids.Count == 0) return;
            var q = Quaternion.Inverse(kids[0].localRotation);
            if (Quaternion.Angle(q, Quaternion.identity) < 0.01f) return;
            foreach (var c in kids)
            {
                c.localPosition = q * c.localPosition;
                c.localRotation = q * c.localRotation;
            }
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
            if (!ok) throw new Exception("Could not save prefab " + path);
            return prefab;
        }

        static void SetStatic(GameObject go, StaticEditorFlags flags)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        const StaticEditorFlags FullStatic = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                                             StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic |
                                             StaticEditorFlags.ReflectionProbeStatic;

        static bool TryWorldBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        static BoxCollider AddBoundsCollider(GameObject root, float minThickness = 0.03f)
        {
            if (!TryWorldBounds(root, out var b)) return null;
            var bc = root.AddComponent<BoxCollider>();
            var size = root.transform.InverseTransformVector(b.size);
            size = new Vector3(Mathf.Max(Mathf.Abs(size.x), minThickness), Mathf.Max(Mathf.Abs(size.y), minThickness),
                               Mathf.Max(Mathf.Abs(size.z), minThickness));
            bc.center = root.transform.InverseTransformPoint(b.center);
            bc.size = size;
            return bc;
        }

        /// <summary>
        /// Collider for a cab prop. A box round the parts that stand clear of the floor (so the stove's thin hearth
        /// plate doesn't block the walkway), at least 0.5 m tall for floor-standing props (above the 0.35 m step
        /// offset, so you can't climb onto the woodpile), and a round capsule for the fire finder so you slide round it.
        /// </summary>
        static void AddPropCollider(GameObject root, string propName)
        {
            if (propName == "FireFinder")
            {
                var cap = root.AddComponent<CapsuleCollider>();
                cap.radius = 0.39f;
                cap.height = 1.2f;
                cap.center = new Vector3(0f, 0.6f, 0f);
                return;
            }
            float baseY = root.transform.position.y;
            var pts = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(m.MultiplyPoint3x4(v));
            }
            if (pts.Count == 0) { AddBoundsCollider(root); return; }
            float yMin = pts.Min(p => p.y), yMax = pts.Max(p => p.y);
            var raised = pts.Where(p => p.y > baseY + 0.03f).ToList();
            if (raised.Count == 0) raised = pts;
            float x0 = raised.Min(p => p.x), x1 = raised.Max(p => p.x), z0 = raised.Min(p => p.z), z1 = raised.Max(p => p.z);
            bool onFloor = Mathf.Abs(baseY - FloorY) < 0.02f && yMin < FloorY + 0.05f;
            if (onFloor) yMax = Mathf.Max(yMax, FloorY + 0.5f);
            var bc = root.AddComponent<BoxCollider>();
            var centre = new Vector3((x0 + x1) * 0.5f, (yMin + yMax) * 0.5f, (z0 + z1) * 0.5f);
            bc.center = root.transform.InverseTransformPoint(centre);        // prop roots are unrotated and unscaled
            bc.size = new Vector3(Mathf.Max(x1 - x0, 0.03f), Mathf.Max(yMax - yMin, 0.03f), Mathf.Max(z1 - z0, 0.03f));
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // ================================================================== fire finder puzzle (digit 7)
        /// <summary>Default bearing of the reported smoke: the big lake's north arm on the map. A multiple of 15°, and through the middle of an east window bay (120° sits behind a window post).</summary>
        const float SmokeBearing = 105f;
        /// <summary>Distance of the smoke from the tower triangle on the map texture, in texture pixels (of 1024).</summary>
        const float SmokeMapRadiusPx = 383f;
        const string MapTexSizeNote = "T_Map_Albedo is 1024², tower triangle at its centre";
        const float BeaconDistance = 90f;
        /// <summary>Which digit of the hatch code (1789) this puzzle gives, and the digit: the lamp flashes the position
        /// slowly, then the digit quickly.</summary>
        const int SmokeCodeSlot = 2, SmokeCodeDigit = 7;
        const float BeaconDipDegrees = 3f;   // how far below the horizon the lamp sits, seen from the cab (the window sill cuts off ~7° from the fire finder)

        /// <summary>The smoke bearing set on the current fire finder prefab, so a rebuild keeps a designer's change.</summary>
        static float CaptureSmokeBearing()
        {
            var old = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/Prop_FireFinder.prefab");
            var s = old != null ? old.GetComponent<FireFinderSighting>() : null;
            float b = s != null ? s.targetBearing : SmokeBearing;
            if (!Mathf.Approximately(Mathf.Repeat(b, 15f), 0f)) Log($"Smoke bearing {b}° isn't a multiple of 15°: the sight can't stop on it.");
            return b;
        }

        static Vector3 BearingDir(float bearing)
        {
            float a = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        /// <summary>
        /// The fire finder's table map is modelled with image-up pointing east (unit disc UVs). Copy the mesh with the
        /// map's UVs turned so image-up is north and image-right east, matching the wall map, and save it as an asset.
        /// </summary>
        static Mesh NorthUpFinderMesh(MeshFilter mf, MeshRenderer mr)
        {
            if (mf == null || mr == null || mf.sharedMesh == null) return null;
            int map = Array.FindIndex(mr.sharedMaterials, m => m != null && m.name == "M_Map");
            if (map < 0 || map >= mf.sharedMesh.subMeshCount) { Log("Fire finder: no M_Map part; table map left as modelled."); return null; }
            var mesh = Object.Instantiate(mf.sharedMesh);
            mesh.name = "FireFinder_NorthUp";
            var mapVerts = new HashSet<int>(mesh.GetTriangles(map));
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (s != map && mesh.GetTriangles(s).Any(mapVerts.Contains))
                {
                    Log("Fire finder: map vertices are shared with another part; table map left as modelled.");
                    Object.DestroyImmediate(mesh);
                    return null;
                }
            var uv = mesh.uv;
            foreach (int i in mapVerts) uv[i] = new Vector2(uv[i].y, 1f - uv[i].x);   // (east-up) -> (north-up)
            mesh.uv = uv;
            return SaveMeshAsset(mesh, $"{PropDir}/FireFinder_NorthUp.asset");
        }

        /// <summary>Saves a generated mesh, updating the existing asset in place (keeps its GUID and references).</summary>
        static Mesh SaveMeshAsset(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>Flat red ring (both faces), like a grease-pencil circle round the smoke on the wall map.</summary>
        static Mesh SmokeCircleMesh()
        {
            const int segs = 40;
            const float rIn = 0.019f, rOut = 0.025f;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                int b = v.Count;
                float nz = side == 0 ? 1f : -1f;   // side 0 winds front-facing toward +Z, side 1 toward -Z
                for (int i = 0; i < segs; i++)
                {
                    // a little wobble so it reads hand-drawn
                    float a = 2f * Mathf.PI * i / segs, w = 1f + 0.06f * Mathf.Sin(a * 3f + 0.7f);
                    v.Add(new Vector3(Mathf.Cos(a) * rIn * w, Mathf.Sin(a) * rIn * w, 0f));
                    v.Add(new Vector3(Mathf.Cos(a) * rOut * w, Mathf.Sin(a) * rOut * w, 0f));
                    n.Add(new Vector3(0f, 0f, nz));
                    n.Add(new Vector3(0f, 0f, nz));
                }
                for (int i = 0; i < segs; i++)
                {
                    int i0 = b + i * 2, i1 = b + ((i + 1) % segs) * 2;
                    if (side == 0) tris.AddRange(new[] { i0, i0 + 1, i1 + 1, i0, i1 + 1, i1 });
                    else tris.AddRange(new[] { i0, i1 + 1, i0 + 1, i0, i1, i1 + 1 });
                }
            }
            var mesh = new Mesh { name = "SmokeCircle" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return SaveMeshAsset(mesh, $"{PropDir}/SmokeCircle.asset");
        }

        /// <summary>
        /// World position (and outward normal) of a texture UV on a prop's flat M_Map sheet, from the sheet's own mesh:
        /// an affine fit through one of its triangles, so it follows the model wherever it was placed.
        /// </summary>
        static bool MapSheetPoint(Transform sheet, Vector2 uv, out Vector3 pos, out Vector3 normal, out Rect uvRect)
        {
            pos = normal = default;
            uvRect = default;
            var mf = sheet != null ? sheet.GetComponent<MeshFilter>() : null;
            var mr = sheet != null ? sheet.GetComponent<MeshRenderer>() : null;
            if (mf == null || mr == null) return false;
            int map = Array.FindIndex(mr.sharedMaterials, m => m != null && m.name == "M_Map");
            if (map < 0) return false;
            var mesh = mf.sharedMesh;
            var tri = mesh.GetTriangles(map);
            var verts = mesh.vertices;
            var uvs = mesh.uv;
            var norms = mesh.normals;
            Vector3 p0 = sheet.TransformPoint(verts[tri[0]]), p1 = sheet.TransformPoint(verts[tri[1]]), p2 = sheet.TransformPoint(verts[tri[2]]);
            Vector2 t0 = uvs[tri[0]], t1 = uvs[tri[1]], t2 = uvs[tri[2]];
            float det = (t1.x - t0.x) * (t2.y - t0.y) - (t2.x - t0.x) * (t1.y - t0.y);
            if (Mathf.Abs(det) < 1e-8f) return false;
            Vector3 du = ((p1 - p0) * (t2.y - t0.y) - (p2 - p0) * (t1.y - t0.y)) / det;
            Vector3 dv = ((p2 - p0) * (t1.x - t0.x) - (p1 - p0) * (t2.x - t0.x)) / det;
            pos = p0 + du * (uv.x - t0.x) + dv * (uv.y - t0.y);
            normal = norms.Length > tri[0] ? sheet.TransformDirection(norms[tri[0]]).normalized : Vector3.Cross(du, dv).normalized;
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (int i in tri) { u0 = Mathf.Min(u0, uvs[i].x); u1 = Mathf.Max(u1, uvs[i].x); v0 = Mathf.Min(v0, uvs[i].y); v1 = Mathf.Max(v1, uvs[i].y); }
            uvRect = Rect.MinMaxRect(u0, v0, u1, v1);
            return true;
        }

        /// <summary>UV on the map texture of a point at a bearing and pixel distance from the tower triangle (north up).</summary>
        static Vector2 MapUv(float bearing, float radiusPx)
        {
            Vector3 d = BearingDir(bearing);
            float x = 512f + d.x * radiusPx;      // east is image-right
            float yDown = 512f - d.z * radiusPx;  // north is image-up
            return new Vector2(x / 1024f, 1f - yDown / 1024f);
        }

        static TMPro.TextMeshPro AddLabel(Transform parent, string name, string text, Vector2 size, Color color, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.rectTransform.sizeDelta = size;
            tmp.text = text;
            tmp.color = color;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.01f;
            tmp.fontSizeMax = 2f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
            return tmp;
        }

        /// <summary>Paper quad (no collider) facing local -Z, with optional text on it.</summary>
        static GameObject AddPaper(Transform parent, string name, Vector2 size, string text, Vector3 pos, Quaternion rot)
        {
            var paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
            paper.name = name;
            Object.DestroyImmediate(paper.GetComponent<Collider>());
            paper.transform.SetParent(parent, false);
            paper.transform.SetPositionAndRotation(pos, rot);
            paper.transform.localScale = new Vector3(size.x, size.y, 1f);
            var m = Mat("M_Paper");
            if (m != null) paper.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (!string.IsNullOrEmpty(text))
            {
                // text sits just in front of the paper; undo the quad's scale so TMP sizes are in metres
                var holder = new GameObject("Text").transform;
                holder.SetParent(paper.transform, false);
                holder.localPosition = new Vector3(0f, 0f, -0.002f);
                holder.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);
                var tmp = AddLabel(holder, "Label", text, size * 0.86f, new Color(0.12f, 0.1f, 0.09f), Vector3.zero);
                tmp.alignment = TMPro.TextAlignmentOptions.TopLeft;
                FitText(tmp);
            }
            return paper;
        }

        /// <summary>
        /// Largest font size whose wrapped text fits the label's rect. TMP's own auto-size gives up early on wrapped
        /// multi-paragraph text and leaves it far too small, so measure it here and fix the size.
        /// </summary>
        static void FitText(TMPro.TextMeshPro tmp)
        {
            Vector2 box = tmp.rectTransform.sizeDelta;
            tmp.enableAutoSizing = false;
            float lo = 0.002f, hi = box.y;   // binary search on the font size (metres)
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                tmp.fontSize = mid;
                Vector2 need = tmp.GetPreferredValues(tmp.text, box.x, 0f);
                if (need.y <= box.y) lo = mid;
                else hi = mid;
            }
            tmp.fontSize = lo;
        }

        /// <summary>Wall-map smoke circle, its tag, a north marker, and the dispatch note on the desk.</summary>
        static void AddFireFinderPuzzle(Transform root, Transform puzzles)
        {
            var finder = FindChild(root, "Prop_FireFinder");
            var sighting = finder != null ? finder.GetComponent<FireFinderSighting>() : null;
            float bearing = sighting != null ? sighting.targetBearing : SmokeBearing;
            var group = new GameObject("FireFinderPuzzle").transform;
            group.SetParent(puzzles, false);

            var sheet = FindChild(root, "MapBoard");
            Vector2 uv = MapUv(bearing, SmokeMapRadiusPx);
            if (MapSheetPoint(sheet, uv, out var pos, out var normal, out var uvRect))
            {
                if (!uvRect.Contains(uv)) Log($"Smoke at {bearing}° (map uv {uv}) is off the wall map's visible part {uvRect}; move it closer ({MapTexSizeNote}).");
                var rot = Quaternion.LookRotation(-normal, Vector3.up);   // local -Z faces out of the map, toward the room
                var circle = new GameObject("SmokeCircle");
                circle.transform.SetParent(group, false);
                circle.transform.SetPositionAndRotation(pos + normal * 0.002f, rot);
                circle.AddComponent<MeshFilter>().sharedMesh = SmokeCircleMesh();
                var cr = circle.AddComponent<MeshRenderer>();
                cr.sharedMaterial = Mat("M_RedPaint");
                // tag pinned above the circle, toward the middle of the map so it stays on the sheet
                Vector3 right = rot * Vector3.right;  // the viewer faces local +Z too, so local +X is their right
                Vector3 toCentre = Vector3.Dot(sheet.position - pos, right) >= 0f ? right : -right;   // sideways toward the board's middle
                AddPaper(group, "SmokeTag", new Vector2(0.12f, 0.05f), "SMOKE 9/14\nconfirm w/ fire finder",
                         pos + normal * 0.003f + toCentre * 0.08f + Vector3.up * 0.045f, rot * Quaternion.Euler(0f, 0f, 3f));
                // north marker in the map's top-left corner
                if (MapSheetPoint(sheet, new Vector2(uvRect.xMin + 0.05f, uvRect.yMax - 0.06f), out var npos, out _, out _))
                {
                    var north = new GameObject("NorthMarker");
                    north.transform.SetParent(group, false);
                    north.transform.SetPositionAndRotation(npos + normal * 0.002f, rot);
                    AddLabel(north.transform, "N","N\n|", new Vector2(0.05f, 0.06f), new Color(0.12f, 0.1f, 0.09f), Vector3.zero).fontStyle = TMPro.FontStyles.Bold;
                }
                Log($"Fire finder puzzle: smoke at bearing {bearing}°, map uv {uv}, circle at {pos}");
            }
            else Log("Fire finder puzzle: wall map sheet not found; no smoke circle.");

            // dispatch note tucked on the logbook (text reads from the room side, top toward the wall)
            var logbook = FindChild(root, "Prop_Logbook");
            Vector3 notePos = logbook != null && TryWorldBounds(logbook.gameObject, out var lb)
                ? new Vector3(lb.center.x - 0.03f, lb.max.y + 0.002f, lb.center.z + 0.02f)
                : new Vector3(3.07f, FloorY + 0.79f, -0.23f);
            AddPaper(group, "DispatchNote", new Vector2(0.15f, 0.19f),
                     "DISPATCH 9/14\n\nSmoke reported. I circled it on the wall map.\n\nShoot a bearing with the fire finder: " +
                     "line the sight up over the spot on the table map.\n\nRanger Hale will answer with his lamp.\n\n- M.",
                     notePos, Quaternion.Euler(90f, 96f, 0f));
        }

        // ================================================================== props
        class PropInfo
        {
            public GameObject prefab;
            public Vector3 position;
        }

        static Dictionary<string, PropInfo> BuildPropPrefabs()
        {
            var result = new Dictionary<string, PropInfo>();
            float smokeBearing = CaptureSmokeBearing();
            var model = InstantiateModel(PropsFbx);
            var kids = model.transform.Cast<Transform>().ToList();
            Transform Kid(string n) => kids.FirstOrDefault(k => k.name == n);

            // sub-parts that belong to another prop
            var sight = Kid("FireFinder_Sight");
            var finder = Kid("FireFinder");
            if (sight != null && finder != null) sight.SetParent(finder, true);
            var items = Kid("CupboardItems");
            var cupboard = Kid("Cupboard");
            if (items != null && cupboard != null) items.SetParent(cupboard, true);

            foreach (var k in kids)
            {
                // sub-parts were re-parented (and destroyed with their prop's temp root): skip them
                if (k == null || k.parent != model.transform) continue;
                var root = new GameObject("Prop_" + k.name);
                root.transform.position = k.position;
                k.SetParent(root.transform, true);
                AddPropCollider(root, k.name);

                switch (k.name)
                {
                    case "FireFinder":
                    {
                        var s = FindChild(root.transform, "FireFinder_Sight");
                        if (s != null)
                        {
                            var turn = root.AddComponent<TurnOnInteract>();
                            turn.target = s;
                            turn.localAxis = s.InverseTransformDirection(Vector3.up).normalized;
                            turn.stepDegrees = 15f;
                            turn.prompt = "Turn the fire finder sight";

                            // puzzle: the sight on the reported smoke's bearing makes the ranger's lamp answer
                            var sighting = root.AddComponent<FireFinderSighting>();
                            sighting.turn = turn;
                            sighting.sight = s;
                            sighting.sightFrontLocal = s.InverseTransformDirection(Vector3.forward).normalized;   // front vane faces north as modelled
                            sighting.targetBearing = smokeBearing;
                        }
                        var tableMap = k.GetComponent<MeshFilter>();
                        var northUp = NorthUpFinderMesh(tableMap, k.GetComponent<MeshRenderer>());
                        if (northUp != null) tableMap.sharedMesh = northUp;
                        break;
                    }
                    case "Lantern":
                    {
                        var lightGo = new GameObject("Flame Light");
                        lightGo.transform.SetParent(root.transform, false);
                        lightGo.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                        var l = lightGo.AddComponent<Light>();
                        l.type = LightType.Point;
                        l.color = new Color(1f, 0.66f, 0.36f);
                        l.range = 7f;
                        l.intensity = 1.6f;
                        l.shadows = LightShadows.Soft;
                        l.shadowStrength = 0.85f;
                        l.lightmapBakeType = LightmapBakeType.Realtime;
                        // Point-light shadows need 6 atlas slots; use the Low tier so URP doesn't have to downscale (and warn).
                        var lightData = l.GetUniversalAdditionalLightData();
                        var so = new SerializedObject(lightData);
                        var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
                        if (tier != null)
                        {
                            tier.intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                            so.ApplyModifiedPropertiesWithoutUndo();
                        }
                        var f = root.AddComponent<LanternFlicker>();
                        f.lamp = l;
                        f.baseIntensity = 1.6f;
                        break;
                    }
                }

                string path = $"{PropDir}/Prop_{k.name}.prefab";
                result[k.name] = new PropInfo { prefab = SavePrefab(root, path), position = root.transform.position };
                Object.DestroyImmediate(root);
            }
            Object.DestroyImmediate(model);
            return result;
        }

        // ================================================================== nature
        class NatureSet
        {
            public List<(GameObject prefab, float weight, bool young)> trees = new List<(GameObject, float, bool)>();
            public GameObject snag, stump, log;
            public List<GameObject> rocks = new List<GameObject>();
        }

        static readonly Dictionary<string, float> TreeHeights = new Dictionary<string, float>
        {
            { "Pine_A", 21f }, { "Pine_B", 16f }, { "Pine_C", 26f }, { "Pine_D", 10f }, { "Pine_E", 5.5f },
        };

        const string BotdPrefabDir = "Assets/Forst/Conifers [BOTD]/Render Pipeline Support/URP/Prefabs";

        /// <summary>Prefab variants of the Conifers [BOTD] URP trees (each with a trunk collider), weighted for the
        /// forest. Empty when the pack isn't imported.</summary>
        static List<(GameObject prefab, float weight, bool young)> BuildBotdConifers()
        {
            var list = new List<(GameObject, float, bool)>();
            var specs = new (string name, float weight, bool young, float radius)[]
            {
                ("Tall", 0.40f, false, 0.35f), ("Medium", 0.35f, false, 0.28f), ("Small", 0.15f, true, 0.15f), ("Bare", 0.10f, false, 0.32f),
            };
            foreach (var sp in specs)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{BotdPrefabDir}/PF Conifer {sp.name} BOTD URP.prefab");
                if (src == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                inst.name = "Tree_Conifer_" + sp.name;
                if (inst.GetComponent<Collider>() == null)
                {
                    var cap = inst.AddComponent<CapsuleCollider>();
                    cap.radius = sp.radius;
                    cap.height = 5f;
                    cap.center = new Vector3(0f, 2.5f, 0f);
                }
                // Not batching-static: the CTI wind shaders sway each tree about its own pivot.
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.OccludeeStatic);
                var variant = PrefabUtility.SaveAsPrefabAsset(inst, $"{NatureDir}/Tree_Conifer_{sp.name}.prefab");
                Object.DestroyImmediate(inst);
                if (variant != null) list.Add((variant, sp.weight, sp.young));
            }
            return list;
        }

        static GameObject CopyRenderer(Transform src, Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(parent.TransformPoint(Vector3.zero) + src.position, src.rotation);
            go.transform.localScale = src.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
            return go;
        }

        static NatureSet BuildNaturePrefabs()
        {
            var set = new NatureSet();
            var model = InstantiateModel(ForestFbx);
            float[] lodHeights = { 0.5f, 0.17f, 0.07f };
            foreach (var kv in TreeHeights)
            {
                var root = new GameObject("Tree_" + kv.Key);
                var lods = new List<LOD>();
                for (int i = 0; i < 3; i++)
                {
                    var src = model.transform.Find($"{kv.Key}_LOD{i}");
                    if (src == null) continue;
                    var go = CopyRenderer(src, root.transform, "LOD" + i);
                    var mr = go.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = i == 2 ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                    mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                    mr.receiveGI = ReceiveGI.LightProbes;
                    lods.Add(new LOD(lodHeights[i], new Renderer[] { mr }));
                }
                var lg = root.AddComponent<LODGroup>();
                lg.SetLODs(lods.ToArray());
                lg.RecalculateBounds();
                var cap = root.AddComponent<CapsuleCollider>();
                cap.radius = 0.05f + 0.013f * kv.Value + 0.05f;
                cap.height = 5f;
                cap.center = new Vector3(0f, 2.5f, 0f);
                SetStatic(root, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
                var prefab = SavePrefab(root, $"{NatureDir}/Tree_{kv.Key}.prefab");
                Object.DestroyImmediate(root);
                float w = kv.Key == "Pine_A" ? 0.3f : kv.Key == "Pine_B" ? 0.27f : kv.Key == "Pine_C" ? 0.18f : kv.Key == "Pine_D" ? 0.15f : 0.1f;
                set.trees.Add((prefab, w, kv.Key == "Pine_D" || kv.Key == "Pine_E"));
            }

            // Prefer the Conifers [BOTD] pack (Asset Store, forst) when it's in the project: much more natural trees,
            // with their own LODs, billboards and wind shaders. Falls back to the generated pines above without it.
            var botd = BuildBotdConifers();
            if (botd.Count > 0) set.trees = botd;

            GameObject Simple(string srcName, string prefabName, Action<GameObject> colliders)
            {
                var src = model.transform.Find(srcName);
                if (src == null) return null;
                var root = new GameObject(prefabName);
                var go = CopyRenderer(src, root.transform, "Mesh");
                var mr = go.GetComponent<MeshRenderer>();
                mr.receiveGI = ReceiveGI.LightProbes;
                colliders(root);
                SetStatic(root, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.BatchingStatic);
                var prefab = SavePrefab(root, $"{NatureDir}/{prefabName}.prefab");
                Object.DestroyImmediate(root);
                return prefab;
            }

            set.snag = Simple("Snag_A", "Tree_Snag_A", r =>
            {
                var c = r.AddComponent<CapsuleCollider>(); c.radius = 0.3f; c.height = 5f; c.center = new Vector3(0, 2.5f, 0);
            });
            set.stump = Simple("Stump_A", "Stump_A", r =>
            {
                var c = r.AddComponent<CapsuleCollider>(); c.radius = 0.4f; c.height = 1.2f; c.center = new Vector3(0, 0.3f, 0);
            });
            set.log = Simple("Log_A", "Log_A", r =>
            {
                var c = r.AddComponent<CapsuleCollider>(); c.direction = 0; c.radius = 0.25f; c.height = 4.8f; c.center = new Vector3(0, 0.18f, 0.07f);
            });
            foreach (var n in new[] { "Rock_A", "Rock_B", "Rock_C", "Rock_D" })
            {
                var p = Simple(n, n, r =>
                {
                    var mesh = r.GetComponentInChildren<MeshFilter>().sharedMesh;
                    var mc = r.transform.GetChild(0).gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                });
                if (p != null) set.rocks.Add(p);
            }
            Object.DestroyImmediate(model);
            return set;
        }

        // ================================================================== tower
        /// <summary>Window that hides the breath-fog code when the prefab has none yet.</summary>
        const string DefaultBreathFogWindow = "Window_N2";

        /// <summary>
        /// BreathFogWindow settings on the current tower prefab, by window name, so a rebuild keeps the code and
        /// whichever window a designer moved the puzzle to.
        /// </summary>
        static Dictionary<string, string> CaptureBreathFogWindows()
        {
            var keep = new Dictionary<string, string>();
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(TowerPrefabPath);
            if (old != null)
                foreach (var w in old.GetComponentsInChildren<BreathFogWindow>(true))
                    keep[w.gameObject.name] = EditorJsonUtility.ToJson(w);
            return keep;
        }

        static void AddBreathFogWindows(Transform openRoot, Dictionary<string, string> keep)
        {
            if (keep.Count == 0) keep[DefaultBreathFogWindow] = null;
            foreach (var kv in keep)
            {
                var t = openRoot.Find(kv.Key);
                if (t == null) { Log($"Breath fog window {kv.Key} not found; skipped."); continue; }
                var w = t.gameObject.AddComponent<BreathFogWindow>();
                if (kv.Value != null) EditorJsonUtility.FromJsonOverwrite(kv.Value, w);
                w.FitToGlass();                      // the glass may have moved since the settings were saved
            }
        }

        // ------------------------------------------------------------------ hatch keypad
        const string KeypadPrefabPath = "Assets/Keypad/Prefabs/Keypad.prefab";   // the URP variant of the asset-store keypad
        internal const string HatchKeypadName = "HatchKeypad";
        /// <summary>Default code for the stair hatch. Change it on the tower prefab's keypad; rebuilds keep that.</summary>
        const int HatchCode = 1789;
        /// <summary>Bump when the default post/keypad placement below changes, so a rebuild drops an older layout.</summary>
        const int HatchKeypadLayout = 2;
        // A post west of the hatch (hatch x -1.65..-0.83, z -1.32..0.28; hinged on its west edge), turned so the keypad
        // faces south-east into the cab. It stands clear of the open lid (which leans ~0.2 m west of the hinge, below
        // 0.8 m) and of the walkway routes the walkability test checks (x = -0.73 and the cot/map-board diagonal).
        static readonly Vector3 KeypadPostPos = new Vector3(-2.0f, FloorY, -0.1f);
        static readonly Quaternion KeypadPostRot = Quaternion.Euler(0f, -45f, 0f);
        const float KeypadPostHeight = 1.25f, KeypadPostWidth = 0.1f;
        // The keypad's front faces its local -Z (its buttons press in along +Z), and it is ~6 cm deep about its pivot.
        static readonly Vector3 KeypadLocalPos = new Vector3(0f, 1.2f, -(KeypadPostWidth * 0.5f + 0.035f));
        static readonly Quaternion KeypadLocalRot = Quaternion.identity;

        class HatchKeypadKeep
        {
            public int code = HatchCode;
            public bool keepPose;
            public Vector3 postPos, kpPos;
            public Quaternion postRot, kpRot;
        }

        /// <summary>The keypad code (and, if the default layout hasn't changed, its placement) on the current prefab.</summary>
        static HatchKeypadKeep CaptureHatchKeypad()
        {
            var keep = new HatchKeypadKeep();
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(TowerPrefabPath);
            var t = old != null ? FindChild(old.transform, HatchKeypadName) : null;
            if (t == null) return keep;
            var kp = t.GetComponentInChildren<NavKeypad.Keypad>(true);
            var lockC = t.GetComponent<HatchKeypadLock>();
            if (kp != null)
            {
                var p = new SerializedObject(kp).FindProperty("keypadCombo");
                if (p != null) keep.code = p.intValue;
                if (lockC != null && lockC.builderLayout == HatchKeypadLayout)
                {
                    keep.keepPose = true;
                    keep.postPos = t.localPosition;
                    keep.postRot = t.localRotation;
                    keep.kpPos = kp.transform.localPosition;
                    keep.kpRot = kp.transform.localRotation;
                }
            }
            return keep;
        }

        static void AddHatchKeypad(Transform root, Transform openRoot, HatchKeypadKeep keep)
        {
            var trap = openRoot.Find("Trapdoor");
            var kpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeypadPrefabPath);
            if (trap == null || kpPrefab == null)
            {
                Log($"Hatch keypad skipped (trapdoor {(trap != null ? "found" : "missing")}, {KeypadPrefabPath} {(kpPrefab != null ? "found" : "missing")}).");
                return;
            }
            // share the Puzzles group with the other puzzle props (PuzzleProps) if it's already there
            var puzzles = root.Find("Puzzles");
            if (puzzles == null)
            {
                puzzles = new GameObject("Puzzles").transform;
                puzzles.SetParent(root, false);
            }

            // pivot on the floor; the (centred) cube mesh sits on a child
            var post = new GameObject(HatchKeypadName);
            post.transform.SetParent(puzzles, false);
            post.transform.localPosition = keep.keepPose ? keep.postPos : KeypadPostPos;
            post.transform.localRotation = keep.keepPose ? keep.postRot : KeypadPostRot;
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Post";
            shaft.transform.SetParent(post.transform, false);
            shaft.transform.localPosition = new Vector3(0f, KeypadPostHeight * 0.5f, 0f);
            shaft.transform.localScale = new Vector3(KeypadPostWidth, KeypadPostHeight, KeypadPostWidth);
            var timber = Mat("M_Timber");
            if (timber != null) shaft.GetComponent<MeshRenderer>().sharedMaterial = timber;
            SetStatic(shaft, FullStatic);

            var kpGo = (GameObject)PrefabUtility.InstantiatePrefab(kpPrefab, post.transform);
            kpGo.name = "Keypad";
            kpGo.transform.localPosition = keep.keepPose ? keep.kpPos : KeypadLocalPos;
            kpGo.transform.localRotation = keep.keepPose ? keep.kpRot : KeypadLocalRot;
            var keypad = kpGo.GetComponentInChildren<NavKeypad.Keypad>(true);
            if (keypad == null) { Log("Hatch keypad: the keypad prefab has no Keypad component; skipped."); Object.DestroyImmediate(post); return; }
            var so = new SerializedObject(keypad);
            so.FindProperty("keypadCombo").intValue = keep.code;
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var b in kpGo.GetComponentsInChildren<NavKeypad.KeypadButton>(true))
            {
                if (b.GetComponent<KeypadButtonInteract>() == null) b.gameObject.AddComponent<KeypadButtonInteract>();
                // The asset ships its button colliders as triggers, but the desktop explorer and XRInteractBridge
                // raycast with QueryTriggerInteraction.Ignore, so make them solid or the rays pass straight through.
                foreach (var c in b.GetComponents<Collider>()) c.isTrigger = false;
            }

            var lockC = post.AddComponent<HatchKeypadLock>();
            lockC.keypad = keypad;
            lockC.hatch = trap.GetComponent<HingedOpenable>();
            lockC.builderLayout = HatchKeypadLayout;
            Log($"Hatch keypad at {post.transform.position}, code {keep.code}{(keep.keepPose ? " (kept the prefab's placement)" : "")}");
        }

        static GameObject BuildTowerPrefab(Dictionary<string, PropInfo> props)
        {
            var breathFog = CaptureBreathFogWindows();
            var puzzleProps = PuzzleProps.Capture();
            var hatchKeypad = CaptureHatchKeypad();
            var root = InstantiateModel(TowerFbx);
            root.name = "LookoutTower";
            var children = root.transform.Cast<Transform>().ToList();

            var cab = children.FirstOrDefault(c => c.name == "Cab_Shell");
            Vector3 cabCentre = new Vector3(0f, FloorY + 1f, 0f);
            if (cab != null && TryWorldBounds(cab.gameObject, out var cb)) cabCentre = cb.center;

            var openRoot = new GameObject("Openables").transform;
            openRoot.SetParent(root.transform, false);
            foreach (var c in children)
            {
                if (c.name.StartsWith("COL_"))
                {
                    var mesh = c.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(c.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(c.GetComponent<MeshFilter>());
                    var mc = c.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    c.name = "Colliders";
                    GameObjectUtility.SetStaticEditorFlags(c.gameObject, StaticEditorFlags.BatchingStatic);
                }
                else if (c.name == "Door" || c.name == "Trapdoor" || c.name.StartsWith("Window_"))
                {
                    c.SetParent(openRoot, true);
                    SetupOpenable(c, cabCentre);
                }
                else
                {
                    SetStatic(c.gameObject, FullStatic);
                    foreach (var r in c.GetComponentsInChildren<MeshRenderer>(true)) r.receiveGI = ReceiveGI.Lightmaps;
                }
            }

            AddBreathFogWindows(openRoot, breathFog);
            PuzzleProps.Restore(root.transform, puzzleProps);
            AddHatchKeypad(root.transform, openRoot, hatchKeypad);

            // props (nested prefab instances)
            var propRoot = new GameObject("Cab_Props").transform;
            propRoot.SetParent(root.transform, false);
            foreach (var kv in props)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(kv.Value.prefab, propRoot);
                inst.transform.position = kv.Value.position;
            }
            var puzzleRoot = root.transform.Find("Puzzles");
            if (puzzleRoot == null)
            {
                puzzleRoot = new GameObject("Puzzles").transform;
                puzzleRoot.SetParent(root.transform, false);
            }
            AddFireFinderPuzzle(root.transform, puzzleRoot);

            // markers + reflection probe
            var markers = new GameObject("Markers").transform;
            markers.SetParent(root.transform, false);
            var spawn = new GameObject("CabSpawn").transform;
            spawn.SetParent(markers, false);
            spawn.position = new Vector3(0.75f, FloorY + 0.02f, -1.35f);
            spawn.rotation = Quaternion.Euler(0f, -25f, 0f);
            var centre = new GameObject("CabCentre").transform;
            centre.SetParent(markers, false);
            centre.position = new Vector3(0f, FloorY, 0f);

            var probeGo = new GameObject("Cab Reflection Probe");
            probeGo.transform.SetParent(root.transform, false);
            probeGo.transform.position = new Vector3(0f, FloorY + 1.2f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.size = new Vector3(7.1f, 2.8f, 7.1f);
            probe.boxProjection = true;
            probe.resolution = 128;

            var prefab = SavePrefab(root, TowerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void SetupOpenable(Transform t, Vector3 cabCentre)
        {
            var go = t.gameObject;
            AddBoundsCollider(go, 0.04f);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var h = go.AddComponent<HingedOpenable>();
            Vector3 worldAxis;
            Vector3 lidDir = Vector3.right;   // hinge -> middle of the leaf, horizontal
            if (TryWorldBounds(go, out var lb))
            {
                lidDir = lb.center - t.position;
                lidDir.y = 0f;
                lidDir = lidDir.sqrMagnitude > 1e-6f ? lidDir.normalized : Vector3.right;
            }
            if (t.name == "Door")
            {
                Vector3 outward = t.position - cabCentre;
                outward.y = 0f;
                outward = Mathf.Abs(outward.x) > Mathf.Abs(outward.z) ? new Vector3(Mathf.Sign(outward.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(outward.z));
                worldAxis = Vector3.Cross(lidDir, outward);       // vertical; swings the leaf out onto the catwalk
                if (worldAxis.sqrMagnitude < 1e-6f) worldAxis = Vector3.up;
                h.openAngle = 100f;
                h.displayName = "door";
                h.speed = 100f;
            }
            else if (t.name == "Trapdoor")
            {
                worldAxis = Vector3.Cross(lidDir, Vector3.up);   // lid lifts up and leans toward the wall
                h.openAngle = 105f;
                h.displayName = "trapdoor";
                h.speed = 90f;
                h.onlyCharactersInOpening = true;   // closable from above while standing at the hatch's edge
                h.locked = true;   // the way out: HatchKeypadLock unlocks it when the keypad code is entered
                // Collide with the lid and its battens only (top 6 cm): the bounds box also wrapped the pull handle
                // underneath, which made the open lid stick out over the ladder.
                var bc = go.GetComponent<BoxCollider>();
                if (bc != null)
                {
                    bc.center = new Vector3(bc.center.x, -0.03f, bc.center.z);
                    bc.size = new Vector3(bc.size.x, 0.06f, bc.size.z);
                }
            }
            else
            {
                Vector3 d = t.position - cabCentre;
                d.y = 0f;
                Vector3 outward = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
                worldAxis = Vector3.Cross(outward, Vector3.up);   // top-hinged awning sash, bottom swings out
                h.openAngle = 55f;
                h.displayName = "window";
                h.speed = 70f;
            }
            h.localAxis = t.InverseTransformDirection(worldAxis).normalized;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.receiveGI = ReceiveGI.LightProbes;
        }

        // ================================================================== scene
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        static float PathX(float z)
        {
            float wander = 5.5f * Mathf.Sin(z / 21f) + 2.5f * Mathf.Sin(z / 7.3f + 1.3f);
            float t = Smooth(-5f, -22f, z);
            return Mathf.Lerp(-1.2f, wander, t);
        }

        static float PathDist(float x, float z) => z > -3f ? 999f : Mathf.Abs(x - PathX(z));

        static float Hills(float x, float z)
        {
            return (Mathf.PerlinNoise(x * 0.012f + 31.7f, z * 0.012f + 11.3f) - 0.5f) * 9f
                 + (Mathf.PerlinNoise(x * 0.035f + 5.1f, z * 0.035f + 77.7f) - 0.5f) * 3f
                 + (Mathf.PerlinNoise(x * 0.11f + 2.2f, z * 0.11f + 9.9f) - 0.5f) * 0.6f
                 + Mathf.Clamp(z, -100f, 100f) * 0.035f;
        }

        static float GroundHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float t = Smooth(13f, 38f, r);
            float flat = (Mathf.PerlinNoise(x * 0.25f + 3f, z * 0.25f + 7f) - 0.5f) * 0.1f;
            float h = Mathf.Lerp(flat, Hills(x, z) - Hills(0f, 0f), t);
            float pd = PathDist(x, z);
            if (pd < 3f) h -= 0.12f * (1f - pd / 3f) * t;
            // ridge round the map edge, so from the high cab the border reads as distant hills lost in the fog
            h += 12f * Smooth(45f, 100f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            return h;
        }

        static TerrainLayer Layer(string name, string tex, float tile)
        {
            string path = $"{SettingsDir}/TL_{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
            l.diffuseTexture = Tex($"T_{tex}_Albedo");
            l.normalMapTexture = Tex($"T_{tex}_Normal");
            l.maskMapTexture = Tex($"T_{tex}_Mask");
            l.normalScale = 1f;
            l.tileSize = new Vector2(tile, tile);
            l.smoothness = 0.1f;
            EditorUtility.SetDirty(l);
            return l;
        }

        static Terrain BuildTerrain()
        {
            const float size = 200f, height = 40f, baseY = -12f;
            string tdPath = $"{SettingsDir}/ForestClearing_TerrainData.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath) != null) AssetDatabase.DeleteAsset(tdPath);
            var td = new TerrainData();
            td.heightmapResolution = 257;
            td.size = new Vector3(size, height, size);
            int res = td.heightmapResolution;
            var h = new float[res, res];
            for (int zi = 0; zi < res; zi++)
            for (int xi = 0; xi < res; xi++)
            {
                float x = -size / 2 + size * xi / (res - 1);
                float z = -size / 2 + size * zi / (res - 1);
                h[zi, xi] = Mathf.Clamp01((GroundHeight(x, z) - baseY) / height);
            }
            td.SetHeights(0, 0, h);

            td.terrainLayers = new[]
            {
                Layer("ForestFloor", "ForestFloor", 4f), Layer("Moss", "Moss", 4f), Layer("Gravel", "Gravel", 3f), Layer("Rock", "Rock", 2f),
            };
            td.alphamapResolution = 256;
            int ar = td.alphamapResolution;
            var a = new float[ar, ar, 4];
            for (int zi = 0; zi < ar; zi++)
            for (int xi = 0; xi < ar; xi++)
            {
                float nx = (xi + 0.5f) / ar, nz = (zi + 0.5f) / ar;
                float x = -size / 2 + size * nx, z = -size / 2 + size * nz;
                float r = Mathf.Sqrt(x * x + z * z);
                float steep = td.GetSteepness(nx, nz);
                float rock = Smooth(20f, 34f, steep);
                float path = 1f - Smooth(1.1f, 2.6f, PathDist(x, z));
                float trodden = (1f - Smooth(4.5f, 9f, r)) * (0.55f + 0.45f * Mathf.PerlinNoise(x * 0.3f, z * 0.3f));
                float gravel = Mathf.Clamp01(Mathf.Max(path, trodden) * (1f - rock));
                float moss = Smooth(0.45f, 0.7f, Mathf.PerlinNoise(x * 0.06f + 40f, z * 0.06f + 12f));
                float clearing = (1f - Smooth(11f, 20f, r)) * Smooth(0.3f, 0.6f, Mathf.PerlinNoise(x * 0.15f + 5f, z * 0.15f + 5f));
                moss = Mathf.Max(moss, clearing * 0.85f) * (1f - gravel) * (1f - rock);
                float floor = Mathf.Max(0f, 1f - rock - gravel - moss);
                float sum = floor + moss + gravel + rock + 1e-5f;
                a[zi, xi, 0] = floor / sum;
                a[zi, xi, 1] = moss / sum;
                a[zi, xi, 2] = gravel / sum;
                a[zi, xi, 3] = rock / sum;
            }
            td.SetAlphamaps(0, 0, a);
            AssetDatabase.CreateAsset(td, tdPath);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Forest Terrain";
            go.transform.position = new Vector3(-size / 2, baseY, -size / 2);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 90f;
            terrain.drawInstanced = true;
            terrain.drawTreesAndFoliage = false;
            var tShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (tShader != null)
            {
                string mp = $"{MatDir}/M_Terrain.mat";
                var tm = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (tm == null) { tm = new Material(tShader); AssetDatabase.CreateAsset(tm, mp); }
                terrain.materialTemplate = tm;
            }
            GameObjectUtility.SetStaticEditorFlags(go, FullStatic);
            return terrain;
        }

        static VolumeProfile BuildVolumeProfile()
        {
            string path = $"{SettingsDir}/LookoutTower_PostFX.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var tm = p.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(1.0f);
            ca.contrast.Override(10f);
            ca.saturation.Override(-18f);
            ca.colorFilter.Override(new Color(0.93f, 0.97f, 1f));
            ca.hueShift.Override(0f);
            var vg = p.Add<Vignette>(true);
            vg.intensity.Override(0.24f);
            vg.smoothness.Override(0.5f);
            vg.color.Override(Color.black);
            var bl = p.Add<Bloom>(true);
            bl.threshold.Override(0.95f);
            bl.intensity.Override(0.45f);
            bl.scatter.Override(0.6f);
            foreach (var c in p.components)
            {
                c.name = c.GetType().Name;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            EditorUtility.SetDirty(p);
            return p;
        }

        static void BuildScene(Scene scene, GameObject towerPrefab, NatureSet nature)
        {
            // --- environment
            var sky = Mat("M_SkyNight");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.4f, 0.54f);
            RenderSettings.ambientEquatorColor = new Color(0.21f, 0.24f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.09f, 0.11f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = 0.032f;
            RenderSettings.reflectionIntensity = 0.6f;

            var lighting = new LightingSettings { name = "LookoutTower_Lighting" };
            lighting.bakedGI = true;
            lighting.realtimeGI = false;
            lighting.mixedBakeMode = MixedLightingMode.Subtractive;
            lighting.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            lighting.lightmapResolution = 8f;
            lighting.lightmapMaxSize = 1024;
            lighting.directionalityMode = LightmapsMode.NonDirectional;
            string lsPath = $"{SettingsDir}/LookoutTower_Lighting.lighting";
            if (AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath) != null) AssetDatabase.DeleteAsset(lsPath);
            AssetDatabase.CreateAsset(lighting, lsPath);
            Lightmapping.lightingSettings = lighting;

            var env = new GameObject("--- Environment ---").transform;

            var moonGo = new GameObject("Moonlight");
            moonGo.transform.SetParent(env, false);
            moonGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.66f, 0.74f, 1f);
            moon.intensity = 1.15f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.8f;
            moon.lightmapBakeType = LightmapBakeType.Mixed;
            RenderSettings.sun = moon;

            var volGo = new GameObject("Global Volume (night grade)");
            volGo.transform.SetParent(env, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildVolumeProfile();

            var terrain = BuildTerrain();
            terrain.transform.SetParent(env, true);

            // --- tower
            var tower = (GameObject)PrefabUtility.InstantiatePrefab(towerPrefab);
            tower.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // --- the ranger's answering lamp for the fire finder puzzle, on a radio mast along the smoke bearing
            var sighting = tower.GetComponentInChildren<FireFinderSighting>(true);
            float smokeBearing = sighting != null ? sighting.targetBearing : SmokeBearing;
            Vector3 beaconDir = BearingDir(smokeBearing);
            var beacon = BuildSightingBeacon(env, sighting, beaconDir);
            Vector3 beaconXZ = new Vector3(beacon.position.x, 0f, beacon.position.z);
            // keep trees and rocks out of the sightline from the cab to the lamp
            bool InSightline(float x, float z)
            {
                Vector3 a = beaconDir * 12f, p = new Vector3(x, 0f, z);
                Vector3 ab = beaconXZ - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                return (p - (a + ab * t)).sqrMagnitude < 5f * 5f;
            }

            // --- forest
            var rnd = new System.Random(20261112);
            float R() => (float)rnd.NextDouble();
            // wind for the Conifers [BOTD] shaders (they read a global set by this WindZone; harmless without the pack)
            var windPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Forst/CTI Runtime Components/CTI Runtime Components URP 14plus/Prefabs/CTI Windzone URP.prefab");
            if (windPrefab != null)
            {
                var wind = (GameObject)PrefabUtility.InstantiatePrefab(windPrefab);
                wind.transform.SetParent(env, false);
                wind.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            }
            var forest = new GameObject("Forest").transform;
            forest.SetParent(env, false);
            var placed = new List<Vector3>();
            int target = 380;
            for (int i = 0; i < 60000 && placed.Count < target; i++)
            {
                float r = Mathf.Sqrt(Mathf.Lerp(15f * 15f, 92f * 92f, R()));
                float ang = R() * Mathf.PI * 2f;
                float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
                if (Mathf.Abs(x) > 97f || Mathf.Abs(z) > 97f) continue;
                if (PathDist(x, z) < 3.4f || InSightline(x, z)) continue;
                float minD = r < 26f ? 3.3f : 4.3f;
                bool ok = true;
                foreach (var p in placed)
                    if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < minD * minD) { ok = false; break; }
                if (!ok) continue;
                GameObject prefab;
                if (R() < 0.05f && nature.snag != null) prefab = nature.snag;
                else if (r < 24f && R() < 0.55f) prefab = PickTree(nature, R(), youngOnly: true);
                else prefab = PickTree(nature, R(), youngOnly: false);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, forest);
                float s = Mathf.Lerp(0.82f, 1.2f, R());
                inst.transform.SetPositionAndRotation(new Vector3(x, GroundHeight(x, z) - 0.05f, z), Quaternion.Euler(0f, R() * 360f, 0f));
                inst.transform.localScale = Vector3.one * s;
                placed.Add(new Vector3(x, 0f, z));
            }

            // --- rocks, stumps, logs
            var debris = new GameObject("Rocks & Deadfall").transform;
            debris.SetParent(env, false);
            void Scatter(GameObject prefab, int count, float rMin, float rMax, float sMin, float sMax, float sink, bool tiltLog = false)
            {
                if (prefab == null) return;
                int n = 0;
                for (int i = 0; i < 4000 && n < count; i++)
                {
                    float r = Mathf.Sqrt(Mathf.Lerp(rMin * rMin, rMax * rMax, R()));
                    float ang = R() * Mathf.PI * 2f;
                    float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
                    if (Mathf.Abs(x) > 96f || Mathf.Abs(z) > 96f || PathDist(x, z) < 2.8f || InSightline(x, z)) continue;
                    bool ok = true;
                    foreach (var p in placed)
                        if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < 2.2f * 2.2f) { ok = false; break; }
                    if (!ok) continue;
                    float s = Mathf.Lerp(sMin, sMax, R());
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, debris);
                    var rot = Quaternion.Euler(tiltLog ? R() * 6f - 3f : R() * 10f - 5f, R() * 360f, R() * 10f - 5f);
                    inst.transform.SetPositionAndRotation(new Vector3(x, GroundHeight(x, z) - sink * s, z), rot);
                    inst.transform.localScale = Vector3.one * s;
                    placed.Add(new Vector3(x, 0f, z));
                    n++;
                }
            }
            for (int k = 0; k < nature.rocks.Count; k++) Scatter(nature.rocks[k], k == 2 ? 10 : 16, 11f, 90f, 0.6f, 1.5f, 0.15f);
            Scatter(nature.stump, 4, 8f, 14f, 0.8f, 1.15f, 0.05f);      // the clearing was cut, so: stumps
            Scatter(nature.stump, 12, 16f, 70f, 0.8f, 1.3f, 0.05f);
            Scatter(nature.log, 2, 9f, 15f, 0.8f, 1.0f, 0.06f, true);
            Scatter(nature.log, 8, 18f, 80f, 0.8f, 1.3f, 0.06f, true);

            // --- player: starts locked in the cab; the way out is the hatch, behind the keypad
            var cabSpawn = FindChild(tower.transform, "CabSpawn");
            var keypadPost = FindChild(tower.transform, HatchKeypadName);
            var player = new GameObject("Player (Desktop Explorer)");
            Vector3 spawnPos = cabSpawn != null ? cabSpawn.position + Vector3.up * 0.05f : new Vector3(0.75f, FloorY + 0.07f, -1.35f);
            Quaternion spawnRot = cabSpawn != null ? cabSpawn.rotation : Quaternion.identity;
            if (keypadPost != null)
            {
                Vector3 toKeypad = Vector3.ProjectOnPlane(keypadPost.position - spawnPos, Vector3.up);
                if (toKeypad.sqrMagnitude > 1e-4f) spawnRot = Quaternion.LookRotation(toKeypad.normalized, Vector3.up);
            }
            player.transform.SetPositionAndRotation(spawnPos, spawnRot);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 70f;       // the last flight up through the hatch is a steep ship's ladder
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.03f;
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 260f;
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var flashGo = new GameObject("Flashlight");
            flashGo.transform.SetParent(camGo.transform, false);
            flashGo.transform.localPosition = new Vector3(0.18f, -0.2f, 0.1f);
            var fl = flashGo.AddComponent<Light>();
            fl.type = LightType.Spot;
            fl.spotAngle = 55f;
            fl.innerSpotAngle = 22f;
            fl.range = 28f;
            fl.intensity = 2.2f;
            fl.color = new Color(1f, 0.95f, 0.86f);
            fl.shadows = LightShadows.Soft;
            fl.lightmapBakeType = LightmapBakeType.Realtime;
            fl.enabled = false;
            var ex = player.AddComponent<DesktopExplorer>();
            ex.cam = cam;
            ex.flashlight = fl;
            ex.cabSpawn = cabSpawn;
            ex.allowCabTeleport = false;   // T would skip the hatch lock; tick it on the player to test

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        /// <summary>
        /// Radio mast with the ranger's lamp, BeaconDistance out along the smoke bearing, tall enough that the lamp sits
        /// BeaconDipDegrees below the horizon from a standing eye in the cab (above the window sills).
        /// Returns the lamp transform.
        /// </summary>
        static Transform BuildSightingBeacon(Transform env, FireFinderSighting sighting, Vector3 dir)
        {
            Vector3 xz = dir * BeaconDistance;
            float ground = GroundHeight(xz.x, xz.z);
            float eyeY = FloorY + 1.6f;
            float lampY = Mathf.Max(ground + 6f, eyeY - BeaconDistance * Mathf.Tan(BeaconDipDegrees * Mathf.Deg2Rad));
            float h = lampY - ground;

            var root = new GameObject("Sighting Beacon (ranger's lamp)");
            root.transform.SetParent(env, false);
            root.transform.position = new Vector3(xz.x, ground, xz.z);

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "Mast";
            mast.transform.SetParent(root.transform, false);
            mast.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            mast.transform.localScale = new Vector3(0.22f, h * 0.5f, 0.22f);
            var iron = Mat("M_Iron");
            if (iron != null) mast.GetComponent<MeshRenderer>().sharedMaterial = iron;
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "Crossarm";
            Object.DestroyImmediate(arm.GetComponent<Collider>());
            arm.transform.SetParent(root.transform, false);
            arm.transform.localPosition = new Vector3(0f, h - 0.6f, 0f);
            arm.transform.localScale = new Vector3(1.6f, 0.1f, 0.1f);
            arm.transform.localRotation = Quaternion.LookRotation(-dir);   // square-on to the tower
            if (iron != null) arm.GetComponent<MeshRenderer>().sharedMaterial = iron;

            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = "Lamp";
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(0f, h + 0.2f, 0f);
            lamp.transform.localScale = Vector3.one * 0.4f;
            var glass = Mat("M_LanternGlass");
            if (glass != null) lamp.GetComponent<MeshRenderer>().sharedMaterial = glass;

            var glowGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glowGo.name = "Glow";
            Object.DestroyImmediate(glowGo.GetComponent<Collider>());
            glowGo.transform.SetParent(lamp.transform, false);
            glowGo.transform.localScale = Vector3.one * (4f / 0.4f);     // ~4 m across in the world: a clear point of light at 90 m
            glowGo.transform.rotation = Quaternion.LookRotation(dir);    // faces the tower until the script turns it to the camera
            var glowR = glowGo.GetComponent<MeshRenderer>();
            glowR.sharedMaterial = BeaconGlowMaterial();
            glowR.shadowCastingMode = ShadowCastingMode.Off;
            glowR.receiveShadows = false;

            var lightGo = new GameObject("Lamp Light");
            lightGo.transform.SetParent(lamp.transform, false);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.78f, 0.45f);
            l.range = 14f;
            l.intensity = 3f;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;

            var b = root.AddComponent<SightingBeacon>();
            b.sighting = sighting;
            b.glow = glowR;
            b.lampLight = l;
            b.slotFlashes = SmokeCodeSlot;
            b.flashes = SmokeCodeDigit;
            Log($"Sighting beacon at {lamp.transform.position} (bearing {Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg:0}°, mast {h:0.0} m)" +
                (sighting == null ? "; no FireFinderSighting found, the lamp will never answer" : ""));
            return lamp.transform;
        }

        static Material BeaconGlowMaterial()
        {
            string path = $"{MatDir}/M_BeaconGlow.mat";
            var shader = Shader.Find("LookoutTower/BeaconGlow");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = "M_BeaconGlow" };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (shader != null) m.shader = shader;
            m.SetColor("_Color", new Color(9f, 5.6f, 2.4f, 1f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject PickTree(NatureSet n, float u, bool youngOnly)
        {
            var pool = youngOnly ? n.trees.Where(t => t.young).ToList() : n.trees;
            if (pool.Count == 0) pool = n.trees;
            float total = pool.Sum(t => t.weight);
            float acc = 0f;
            foreach (var t in pool)
            {
                acc += t.weight / total;
                if (u <= acc) return t.prefab;
            }
            return pool[pool.Count - 1].prefab;
        }

        // ================================================================== screenshots
        struct View
        {
            public string name;
            public Vector3 pos, target;
            public float fov;
            public bool trapdoorOpen;
            public View(string n, Vector3 p, Vector3 t, float f, bool trapdoorOpen = false)
            {
                name = n; pos = p; target = t; fov = f; this.trapdoorOpen = trapdoorOpen;
            }
        }

        static readonly View[] Views =
        {
            new View("01_path_approach", new Vector3(-2.5f, 1.7f, -26f), new Vector3(0f, 22f, 0f), 62f),
            new View("02_exterior_high", new Vector3(20f, 24f, -34f), new Vector3(0f, 26f, 0f), 55f),
            new View("03_cab_interior", new Vector3(2.4f, FloorY + 1.62f, -2.8f), new Vector3(-1.1f, FloorY + 0.5f, 1.5f), 75f),
            new View("04_cab_interior_desk", new Vector3(-0.5f, FloorY + 1.6f, -1.5f), new Vector3(3.1f, FloorY + 0.7f, 0.0f), 75f),
            new View("05_view_from_cab", new Vector3(0.2f, FloorY + 1.65f, 2.6f), new Vector3(0.6f, FloorY - 12f, 60f), 75f),
            new View("06_catwalk", new Vector3(4.2f, FloorY + 1.6f, -4.2f), new Vector3(-4.0f, FloorY + 1.0f, 3.0f), 75f),
            new View("07_stairs_inside", new Vector3(-1.2f, 1.65f, -1.6f), new Vector3(-0.9f, 4.2f, 1.0f), 75f),
            new View("08_aerial", new Vector3(60f, 45f, -75f), new Vector3(0f, 15f, 0f), 50f),
            new View("09_hatch_from_cab", new Vector3(-0.35f, FloorY + 1.6f, 0.95f), new Vector3(-1.24f, FloorY - 0.9f, -0.75f), 75f, true),
            new View("10_ladder_up_through_hatch", new Vector3(-1.24f, LowTopY + 1.55f, -1.45f), new Vector3(-1.24f, FloorY + 1.2f, 0.35f), 75f, true),
            new View("11_hatch_keypad", new Vector3(-0.7f, FloorY + 1.6f, -1.6f), new Vector3(-1.9f, FloorY + 0.9f, -0.1f), 70f),
        };

        public static string CaptureScreenshots()
        {
            if (!File.Exists(ScenePath)) { Debug.LogWarning("[LookoutTower] Build the scene first."); return null; }
            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
                EditorSceneManager.OpenScene(ScenePath);
            }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LookoutTowerSource", "Screenshots"));
            Directory.CreateDirectory(dir);
            var scene = SceneManager.GetActiveScene();
            bool sceneWasDirty = scene.isDirty;
            var trap = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HingedOpenable>(true))
                            .FirstOrDefault(o => o.name == "Trapdoor");
            bool trapWasOpen = trap != null && trap.IsOpen;
            // fire finder puzzle: a shot down the sight at the ranger's lamp, with the sight turned onto the smoke
            var views = Views.ToList();
            var sighting = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FireFinderSighting>(true)).FirstOrDefault();
            var beacon = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SightingBeacon>(true)).FirstOrDefault();
            Quaternion sightWas = sighting != null && sighting.sight != null ? sighting.sight.localRotation : Quaternion.identity;
            if (sighting != null && beacon != null && beacon.glow != null)
            {
                Vector3 d = BearingDir(sighting.targetBearing);
                Vector3 eye = new Vector3(sighting.transform.position.x, FloorY + 1.55f, sighting.transform.position.z) - d * 0.6f;
                views.Add(new View("12_sight_to_beacon", eye, beacon.glow.transform.position, 45f));
            }
            const int w = 1600, h = 900;
            var go = new GameObject("~LookoutTowerScreenshotCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 260f;
                cam.clearFlags = CameraClearFlags.Skybox;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                cam.targetTexture = rt;
                foreach (var v in views)
                {
                    if (trap != null) trap.SetOpenImmediate(v.trapdoorOpen);
                    if (sighting != null && sighting.sight != null && sighting.turn != null)
                        sighting.sight.localRotation = v.name == "12_sight_to_beacon"
                            ? sightWas * Quaternion.AngleAxis(sighting.targetBearing, sighting.turn.localAxis.normalized)
                            : sightWas;
                    cam.fieldOfView = v.fov;
                    go.transform.position = v.pos;
                    go.transform.LookAt(v.target);
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, v.name + ".png"), tex.EncodeToPNG());
                }
                Debug.Log($"[LookoutTower] Saved {views.Count} preview screenshots to {dir}");
                Log($"Saved {views.Count} screenshots to {dir}");
                return dir;
            }
            finally
            {
                if (sighting != null && sighting.sight != null) sighting.sight.localRotation = sightWas;
                // put the trapdoor back the way it was saved (the hatch shots open it)
                if (trap != null)
                {
                    trap.SetOpenImmediate(trapWasOpen);
                    if (!sceneWasDirty && scene.isDirty) EditorSceneManager.SaveScene(scene);
                }
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }

    /// <summary>
    /// After the files are first imported (or after the builder is updated), offers to run Build Everything.
    /// </summary>
    [InitializeOnLoad]
    static class LookoutTowerFirstRun
    {
        static double _start;

        static LookoutTowerFirstRun()
        {
            if (Application.isBatchMode) return;
            string key = "LookoutTower.FirstRunChecked.v" + LookoutTowerBuilder.BuilderVersion;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            _start = EditorApplication.timeSinceStartup;
            EditorApplication.update += Poll;
        }

        static string DeclinedKey => "LookoutTower.Declined.v" + LookoutTowerBuilder.BuilderVersion + "." + Application.dataPath.GetHashCode();

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup - _start < 2.0) return;
            // isPlaying stays true while play mode is shutting down, after isPlayingOrWillChangePlaymode has gone false
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= Poll;
            if (EditorPrefs.GetBool(DeclinedKey, false)) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LookoutTower/Models/LookoutTower.fbx") == null) return;
            bool built = File.Exists(LookoutTowerBuilder.TowerPrefabPath);
            if (built && LookoutTowerBuilder.BuiltVersion() >= LookoutTowerBuilder.BuilderVersion) return;
            string msg = built
                ? $"The Lookout Tower builder was updated ({LookoutTowerBuilder.WhatChanged}).\n\nRebuild the prefabs and the forest-clearing scene now? This overwrites LookoutTower_ForestClearing.unity."
                : "The lookout tower assets are imported.\n\nBuild the materials, prefabs and the night forest-clearing scene now?\n(You can always run Tools ▸ Lookout Tower ▸ Build Everything later.)";
            bool go = EditorUtility.DisplayDialog("Lookout Tower", msg, built ? "Rebuild now" : "Build now", "Later");
            if (!go) { EditorPrefs.SetBool(DeclinedKey, true); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) LookoutTowerBuilder.BuildEverything(true);
        }
    }
}
