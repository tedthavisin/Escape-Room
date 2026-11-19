using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Builds the escape-room puzzle props (prefabs under Prefabs/Puzzles) and keeps them in the tower prefab's
    /// "Puzzles" group. Build Everything regenerates the tower prefab, so it snapshots that group first and puts every
    /// prefab instance in it back where a designer left it.
    /// </summary>
    public static class PuzzleProps
    {
        public const string PuzzleDir = LookoutTowerBuilder.Root + "/Prefabs/Puzzles";
        public const string FogHintNotePath = PuzzleDir + "/Note_FogHint.prefab";
        public const string CompassPath = PuzzleDir + "/Prop_Compass.prefab";
        const string MatDir = LookoutTowerBuilder.Root + "/Materials";
        const string GroupName = "Puzzles";

        const string FogHintText =
            "<b>Oct 3.</b> Night watch.\n\n" +
            "The north window fogs every time I lean in to look for smoke. " +
            "Wrote the third number there so I'd stop forgetting it.";

        [MenuItem("Tools/Lookout Tower/Build Puzzle Props", priority = 30)]
        public static void BuildMenu()
        {
            BuildFogHintNote();
            BuildCompass();
            var root = PrefabUtility.LoadPrefabContents(LookoutTowerBuilder.TowerPrefabPath);
            try
            {
                var group = root.transform.Find(GroupName);
                if (group != null) Object.DestroyImmediate(group.gameObject);
                Restore(root.transform, new List<Placed>());
                PrefabUtility.SaveAsPrefabAsset(root, LookoutTowerBuilder.TowerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[LookoutTower] Built puzzle props and placed them in the tower prefab.");
        }

        // ------------------------------------------------------------------ the fog-window hint note
        /// <summary>
        /// A ranger's note on a narrow sheet of paper with a red pushpin, sized for the 11 cm posts between the cab's
        /// windows. The pivot is the middle of the paper's back; the reader looks along +Z with the text's top along +Y,
        /// so pin it with +Z pointing into the wall.
        /// </summary>
        public static GameObject BuildFogHintNote()
        {
            if (!AssetDatabase.IsValidFolder(PuzzleDir)) AssetDatabase.CreateFolder(LookoutTowerBuilder.Root + "/Prefabs", "Puzzles");

            var root = new GameObject("Note_FogHint");
            const float w = 0.12f, h = 0.21f;

            var paper = new GameObject("Paper");
            paper.transform.SetParent(root.transform, false);
            paper.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            paper.transform.localScale = new Vector3(w, h, 1f);
            paper.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var pr = paper.AddComponent<MeshRenderer>();
            pr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Paper.mat");
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;   // flush against the wall, it picks up shadow-map streaks at the post's edges

            var text = new GameObject("Text");
            text.transform.SetParent(root.transform, false);
            text.transform.localPosition = new Vector3(0f, -0.013f, -0.0016f);   // below the pin
            var tmp = text.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(w - 0.02f, h - 0.052f);
            tmp.text = FogHintText;
            tmp.fontStyle = FontStyles.Italic;
            // Near-black: TMP text is unlit, so anything lighter glows against paper that's only dimly lit at night.
            tmp.color = new Color(0.02f, 0.02f, 0.035f);
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.enableAutoSizing = false;
            tmp.fontSize = 0.12f;                              // ~5 mm lowercase
            tmp.lineSpacing = -6f;

            var pin = new GameObject("Pin");
            pin.transform.SetParent(root.transform, false);
            pin.transform.localPosition = new Vector3(0.003f, h * 0.5f - 0.014f, -0.006f);
            pin.transform.localScale = Vector3.one * 0.013f;
            pin.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            pin.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_RedPaint.mat");

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, FogHintNotePath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ compass
        /// <summary>
        /// A brass pocket compass lying face up: N E S W on the dial and a red-tipped needle that swings round to north
        /// (<see cref="CompassNeedle"/>), so players can tell which window is the north one. Pivot at the bottom centre.
        /// </summary>
        public static GameObject BuildCompass()
        {
            if (!AssetDatabase.IsValidFolder(PuzzleDir)) AssetDatabase.CreateFolder(LookoutTowerBuilder.Root + "/Prefabs", "Puzzles");
            var brass = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Brass.mat");
            var paper = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Paper.mat");
            var red = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_RedPaint.mat");
            var iron = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Iron.mat");

            var root = new GameObject("Prop_Compass");
            const float r = 0.045f;           // case radius: a 9 cm compass
            const float caseH = 0.014f;
            const float faceY = caseH + 0.0004f; // dial face, just above the top of the (solid) case

            // Built-in cylinder: 2 m tall, 0.5 m radius, centred on its pivot.
            Part(root, "Case", "Cylinder.fbx", brass, new Vector3(0f, caseH * 0.5f, 0f), new Vector3(2f * r, caseH * 0.5f, 2f * r));
            Part(root, "Dial", "Cylinder.fbx", paper, new Vector3(0f, faceY, 0f), new Vector3(2f * r - 0.006f, 0.0004f, 2f * r - 0.006f));
            Part(root, "Bow", "Cylinder.fbx", brass, new Vector3(0f, caseH * 0.5f, r + 0.004f), new Vector3(0.012f, caseH * 0.35f, 0.008f));

            // cardinal letters, upright when read with the dial's N at the top
            var letters = new[] { ("N", new Vector3(0f, 0f, 1f), new Color(0.55f, 0.05f, 0.04f)),
                                  ("E", new Vector3(1f, 0f, 0f), new Color(0.05f, 0.05f, 0.06f)),
                                  ("S", new Vector3(0f, 0f, -1f), new Color(0.05f, 0.05f, 0.06f)),
                                  ("W", new Vector3(-1f, 0f, 0f), new Color(0.05f, 0.05f, 0.06f)) };
            foreach (var (txt, dir, col) in letters)
            {
                var go = new GameObject("Letter_" + txt);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = dir * (r - 0.011f) + Vector3.up * (faceY + 0.0006f);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lying flat, top of the letter toward N
                var t = go.AddComponent<TextMeshPro>();
                t.rectTransform.sizeDelta = new Vector2(0.02f, 0.02f);
                t.text = txt;
                t.fontSize = 0.16f;
                t.fontStyle = FontStyles.Bold;
                t.color = col;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            }
            // tick marks between the letters
            for (int i = 0; i < 8; i++)
            {
                if (i % 2 == 0) continue;
                var tick = Part(root, "Tick", "Cube.fbx", iron, Vector3.zero, new Vector3(0.0012f, 0.0004f, 0.006f));
                tick.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
                tick.transform.localPosition = tick.transform.localRotation * new Vector3(0f, faceY + 0.0004f, r - 0.01f);
            }

            // needle: red half points north (+Z), dark half south, on a brass pivot
            var needle = new GameObject("Needle");
            needle.transform.SetParent(root.transform, false);
            needle.transform.localPosition = new Vector3(0f, faceY + 0.0025f, 0f);
            Part(needle, "North", "Cube.fbx", red, new Vector3(0f, 0f, 0.0105f), new Vector3(0.0045f, 0.0012f, 0.021f));
            Part(needle, "South", "Cube.fbx", iron, new Vector3(0f, 0f, -0.0105f), new Vector3(0.0045f, 0.0012f, 0.021f));
            Part(needle, "Pivot", "Cylinder.fbx", brass, new Vector3(0f, 0.001f, 0f), new Vector3(0.006f, 0.0015f, 0.006f));
            needle.AddComponent<CompassNeedle>();

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CompassPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject Part(GameObject parent, string name, string builtinMesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(builtinMesh);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------ keeping props across rebuilds
        public struct Placed
        {
            public string assetPath, name;
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        /// <summary>Prefab instances in the current tower prefab's Puzzles group, with their poses.</summary>
        public static List<Placed> Capture()
        {
            var list = new List<Placed>();
            var tower = AssetDatabase.LoadAssetAtPath<GameObject>(LookoutTowerBuilder.TowerPrefabPath);
            var group = tower != null ? tower.transform.Find(GroupName) : null;
            if (group == null) return list;
            foreach (Transform t in group)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                string path = src != null ? AssetDatabase.GetAssetPath(src) : null;
                if (string.IsNullOrEmpty(path)) continue;
                list.Add(new Placed { assetPath = path, name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            }
            return list;
        }

        /// <summary>
        /// Recreates the Puzzles group under <paramref name="towerRoot"/>, adding any default prop that isn't in
        /// <paramref name="placed"/> yet (first build, or a prop added since the last one).
        /// </summary>
        public static void Restore(Transform towerRoot, List<Placed> placed)
        {
            var group = new GameObject(GroupName).transform;
            group.SetParent(towerRoot, false);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(FogHintNotePath) == null) BuildFogHintNote();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CompassPath) == null) BuildCompass();
            foreach (var d in new[] { DefaultFogHintNotePose(), DefaultCompassPose() })
                if (!placed.Exists(p => p.assetPath == d.assetPath)) placed.Add(d);
            foreach (var p in placed)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.assetPath);
                if (asset == null) { Debug.LogWarning($"[LookoutTower] Puzzle prop {p.assetPath} is gone; skipped."); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, group);
                inst.name = p.name;
                inst.transform.localPosition = p.position;
                inst.transform.localRotation = p.rotation;
                inst.transform.localScale = p.scale;
            }
        }

        /// <summary>
        /// Pinned at standing eye height to the wall post just north of the desk (between windows E2 and E3, beside the
        /// radio), facing into the cab. The note sends the player across the cab to the north window.
        /// </summary>
        static Placed DefaultFogHintNotePose()
        {
            // East wall's inner face is x 3.53; the post between E2 and E3 runs z 0.667..0.774 and is solid floor to
            // ceiling. Note centre 1.45 m above the cab floor (30.5 m). Tower origin is the ground; +X east, +Z north.
            return new Placed
            {
                assetPath = FogHintNotePath,
                name = "Note_FogHint",
                position = new Vector3(3.5285f, 30.5f + 1.45f, 0.72f),
                // reader faces east, into the wall; pinned a touch crooked
                rotation = Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(0f, 0f, -3f),
                scale = Vector3.one,
            };
        }

        /// <summary>
        /// On the desk in front of the radio, by the seated player's left hand. Set square to the cab, so the dial's N
        /// and the needle both point at the north wall, in the Editor as well as in game.
        /// </summary>
        static Placed DefaultCompassPose()
        {
            // Radio spans x 3.10..3.40, z -0.06..0.32; the chair is pushed in at z < 0.02. Desk top 0.76 m up.
            return new Placed
            {
                assetPath = CompassPath,
                name = "Prop_Compass",
                position = new Vector3(2.99f, 30.5f + 0.76f, 0.17f),
                rotation = Quaternion.identity,
                scale = Vector3.one,
            };
        }
    }
}
