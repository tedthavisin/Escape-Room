using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Builds the escape-room puzzle props (prefabs under Prefabs/Puzzles) and keeps them in the tower prefab's
    /// "Puzzles" group, which it shares with other puzzle pieces such as the hatch keypad. Build Everything regenerates
    /// the tower prefab, so it snapshots this code's props first and puts each back where a designer left it. It only
    /// ever touches instances of prefabs in Prefabs/Puzzles; anything else in the group is left alone.
    /// </summary>
    public static class PuzzleProps
    {
        public const string PuzzleDir = LookoutTowerBuilder.Root + "/Prefabs/Puzzles";
        public const string FogHintNotePath = PuzzleDir + "/Note_FogHint.prefab";
        public const string CompassPath = PuzzleDir + "/Prop_Compass.prefab";
        public const string MorseLampPath = PuzzleDir + "/Prop_MorseLamp.prefab";
        public const string MorseGuidePath = PuzzleDir + "/Note_MorseGuide.prefab";
        const string MorseMarksMeshPath = PuzzleDir + "/MorseGuideMarks.asset";
        const string MorseBulbMatPath = LookoutTowerBuilder.Root + "/Materials/M_MorseBulb.mat";
        const string MatDir = LookoutTowerBuilder.Root + "/Materials";
        const string GroupName = "Puzzles";

        const string FogHintText =
            "<b>Oct 3.</b> Night watch.\n\n" +
            "The north window fogs every time I lean in to look for smoke. " +
            "Wrote the third number there so I'd stop forgetting it.";

        /// <summary>What the hanging lamp blinks, and which place of the hatch code (1789) it is, shown on its tag.</summary>
        const string MorseLampMessage = "1";
        const string MorseLampTag = "X _ _ _";
        /// <summary>Digits on the wall chart. Includes the lamp's digit, plus a few others so it isn't a giveaway.</summary>
        const string MorseGuideDigits = "123456";

        [MenuItem("Tools/Lookout Tower/Build Puzzle Props", priority = 30)]
        public static void BuildMenu()
        {
            BuildFogHintNote();
            BuildCompass();
            BuildMorseLamp();
            BuildMorseGuide();
            var root = PrefabUtility.LoadPrefabContents(LookoutTowerBuilder.TowerPrefabPath);
            try
            {
                // reset placement: drop only our own props, keep the rest of the group (e.g. the hatch keypad)
                var group = Group(root.transform);
                for (int i = group.childCount - 1; i >= 0; i--)
                    if (IsOurProp(group.GetChild(i), out _)) Object.DestroyImmediate(group.GetChild(i).gameObject);
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

        // ------------------------------------------------------------------ Morse lamp
        /// <summary>
        /// A signal lamp hanging on a cord from a ceiling beam: an iron ceiling cup, cord, a green enamel can shade and an
        /// opal bulb that blinks <see cref="MorseLampMessage"/> (<see cref="MorseLamp"/>). A paper tag hangs from the shade
        /// rim with "X _ _ _" on both sides: the digit is the first of the code. Pivot at the top (the underside of the
        /// beam); the lamp hangs 0.53 m and the tag 0.65 m below it. No colliders: it's all above head height.
        /// </summary>
        public static GameObject BuildMorseLamp()
        {
            if (!AssetDatabase.IsValidFolder(PuzzleDir)) AssetDatabase.CreateFolder(LookoutTowerBuilder.Root + "/Prefabs", "Puzzles");
            var iron = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Iron.mat");
            var shadeMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_TrimGreen.mat");
            var cordMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_PlasticBlack.mat");
            var paper = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Paper.mat");
            var bulbMat = MorseBulbMaterial();

            var root = new GameObject("Prop_MorseLamp");
            const float cordLen = 0.33f;
            const float shadeTop = -0.012f - cordLen, shadeH = 0.12f, shadeR = 0.07f;
            const float shadeBottom = shadeTop - shadeH;
            const float bulbR = 0.05f, bulbY = shadeBottom - 0.015f;

            // Built-in cylinder: 2 m tall, 0.5 m radius, centred on its pivot.
            Part(root, "CeilingCup", "Cylinder.fbx", iron, new Vector3(0f, -0.006f, 0f), new Vector3(0.06f, 0.006f, 0.06f));
            Part(root, "Cord", "Cylinder.fbx", cordMat, new Vector3(0f, -0.012f - cordLen * 0.5f, 0f), new Vector3(0.008f, cordLen * 0.5f, 0.008f));
            Part(root, "ShadeCap", "Sphere.fbx", shadeMat, new Vector3(0f, shadeTop, 0f), new Vector3(2f * shadeR, 0.05f, 2f * shadeR));
            Part(root, "Shade", "Cylinder.fbx", shadeMat, new Vector3(0f, shadeTop - shadeH * 0.5f, 0f), new Vector3(2f * shadeR, shadeH * 0.5f, 2f * shadeR));
            Part(root, "Rim", "Cylinder.fbx", iron, new Vector3(0f, shadeBottom + 0.004f, 0f), new Vector3(2f * shadeR + 0.03f, 0.004f, 2f * shadeR + 0.03f));
            var bulb = Part(root, "Bulb", "Sphere.fbx", bulbMat, new Vector3(0f, bulbY, 0f), new Vector3(2f * bulbR, 2.2f * bulbR, 2f * bulbR));

            var lightGo = new GameObject("Signal Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, bulbY - bulbR - 0.02f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.9f, 0.72f);
            l.range = 4.5f;
            l.intensity = 2.4f;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;

            // the tag: a string from the rim down to a paper card, "X _ _ _" on both faces
            const float tagW = 0.13f, tagH = 0.065f, stringLen = 0.11f;
            float rimX = shadeR + 0.01f;
            Part(root, "TagString", "Cylinder.fbx", cordMat, new Vector3(rimX, shadeBottom - stringLen * 0.5f, 0f), new Vector3(0.003f, stringLen * 0.5f, 0.003f));
            var tag = new GameObject("Tag");
            tag.transform.SetParent(root.transform, false);
            tag.transform.localPosition = new Vector3(rimX, shadeBottom - stringLen - tagH * 0.5f, 0f);
            tag.transform.localRotation = Quaternion.Euler(0f, 0f, 4f);   // hangs a touch crooked
            Part(tag, "Card", "Cube.fbx", paper, Vector3.zero, new Vector3(tagW, tagH, 0.002f));
            foreach (var back in new[] { false, true })
            {
                var go = new GameObject(back ? "Text_Back" : "Text_Front");
                go.transform.SetParent(tag.transform, false);
                // TMP reads from -Z; the back face's copy is turned round to read from +Z
                go.transform.localPosition = new Vector3(0f, 0f, back ? 0.0016f : -0.0016f);
                go.transform.localRotation = back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                var t = go.AddComponent<TextMeshPro>();
                t.rectTransform.sizeDelta = new Vector2(tagW - 0.014f, tagH - 0.01f);
                t.text = MorseLampTag;
                t.fontStyle = FontStyles.Bold;
                t.color = new Color(0.02f, 0.02f, 0.035f);   // near-black: TMP is unlit (see the fog-hint note)
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.enableAutoSizing = true;
                t.fontSizeMin = 0.1f;
                t.fontSizeMax = 0.45f;
            }

            var lamp = root.AddComponent<MorseLamp>();
            lamp.message = MorseLampMessage;
            lamp.bulb = bulb.GetComponent<MeshRenderer>();
            lamp.lampLight = l;

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, MorseLampPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Opal glass for the signal bulb: emission on but black, so <see cref="MorseLamp"/> can light it per
        /// renderer. Its own material so nothing else blinks with it.</summary>
        static Material MorseBulbMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MorseBulbMatPath);
            bool isNew = m == null;
            if (isNew) m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_MorseBulb" };
            m.SetColor("_BaseColor", new Color(0.86f, 0.84f, 0.78f));
            m.SetFloat("_Smoothness", 0.85f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (isNew) AssetDatabase.CreateAsset(m, MorseBulbMatPath);
            else EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ Morse guide chart
        /// <summary>
        /// A printed card of Morse numerals (<see cref="MorseGuideDigits"/>), pinned to the wall: each digit, then its
        /// dots and dashes. Same conventions as the fog-hint note: the pivot is the middle of the card's back, the reader
        /// looks along +Z with the top along +Y, so pin it with +Z pointing into the wall.
        /// </summary>
        public static GameObject BuildMorseGuide()
        {
            if (!AssetDatabase.IsValidFolder(PuzzleDir)) AssetDatabase.CreateFolder(LookoutTowerBuilder.Root + "/Prefabs", "Puzzles");
            var paperMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Paper.mat");
            var ink = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_PlasticBlack.mat");
            var red = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_RedPaint.mat");
            var dark = new Color(0.02f, 0.02f, 0.035f);

            var root = new GameObject("Note_MorseGuide");
            const float w = 0.22f, h = 0.30f;
            const float titleTop = h * 0.5f - 0.03f;   // below the pin
            const float rowTop = titleTop - 0.065f, rowPitch = 0.034f;
            const float labelX = -w * 0.5f + 0.03f, marksX = labelX + 0.03f;

            var paper = Part(root, "Paper", "Quad.fbx", paperMat, new Vector3(0f, 0f, -0.001f), new Vector3(w, h, 1f));
            paper.GetComponent<MeshRenderer>().receiveShadows = false;   // flush to the wall (see the fog-hint note)

            var title = Text(root, "Title", "<b>MORSE CODE</b>\n<size=70%><i>numbers</i></size>", new Vector3(0f, titleTop - 0.025f, -0.0016f),
                             new Vector2(w - 0.02f, 0.05f), 0.2f, TextAlignmentOptions.Center, dark);
            title.lineSpacing = -10f;

            var rows = new List<string>();
            int r = 0;
            foreach (char c in MorseGuideDigits)
            {
                var code = MorseLamp.MorseFor(c);
                if (code == null) continue;
                float y = rowTop - r * rowPitch;
                Text(root, "Digit_" + c, $"<b>{c}</b>", new Vector3(labelX, y, -0.0016f), new Vector2(0.03f, rowPitch), 0.2f,
                     TextAlignmentOptions.Center, dark);
                rows.Add(code);
                r++;
            }

            var marks = new GameObject("Marks");
            marks.transform.SetParent(root.transform, false);
            marks.transform.localPosition = new Vector3(0f, 0f, -0.0014f);
            marks.AddComponent<MeshFilter>().sharedMesh = MorseMarksMesh(rows, marksX, rowTop, rowPitch);
            var mmr = marks.AddComponent<MeshRenderer>();
            mmr.sharedMaterial = ink;
            mmr.receiveShadows = false;

            Part(root, "Pin", "Sphere.fbx", red, new Vector3(0f, h * 0.5f - 0.014f, -0.006f), Vector3.one * 0.013f);

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, MorseGuidePath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The chart's dots and dashes, one row per digit, as flat shapes in the XY plane facing -Z (the reader).
        /// Each row starts at <paramref name="x0"/> and is centred on its row's line.</summary>
        static Mesh MorseMarksMesh(List<string> rows, float x0, float rowTop, float rowPitch)
        {
            const float dotR = 0.0052f, dashW = 0.026f, dashH = 0.0075f, gap = 0.0085f;
            const int segs = 14;
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int r = 0; r < rows.Count; r++)
            {
                float y = rowTop - r * rowPitch, x = x0;
                foreach (char m in rows[r])
                {
                    if (m == '-')
                    {
                        int b = v.Count;
                        v.Add(new Vector3(x, y - dashH * 0.5f)); v.Add(new Vector3(x, y + dashH * 0.5f));
                        v.Add(new Vector3(x + dashW, y + dashH * 0.5f)); v.Add(new Vector3(x + dashW, y - dashH * 0.5f));
                        // clockwise as seen from -Z
                        tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                        x += dashW + gap;
                    }
                    else
                    {
                        int c = v.Count;
                        var centre = new Vector3(x + dotR, y);
                        v.Add(centre);
                        for (int i = 0; i < segs; i++)
                        {
                            float a = -i * Mathf.PI * 2f / segs;   // angle decreasing = clockwise from -Z
                            v.Add(centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * dotR);
                        }
                        for (int i = 0; i < segs; i++) tri.AddRange(new[] { c, c + 1 + i, c + 1 + (i + 1) % segs });
                        x += 2f * dotR + gap;
                    }
                }
            }
            var mesh = new Mesh { name = "MorseGuideMarks" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            var n = new Vector3[v.Count];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.back;
            mesh.normals = n;
            mesh.RecalculateBounds();
            return LookoutTowerBuilder.SaveMeshAsset(mesh, MorseMarksMeshPath);
        }

        static TextMeshPro Text(GameObject parent, string name, string text, Vector3 pos, Vector2 size, float fontSize,
                                TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = size;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.enableAutoSizing = false;
            return t;
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

        /// <summary>Instances of our props (prefabs in Prefabs/Puzzles) in the current tower prefab, with their poses.</summary>
        public static List<Placed> Capture()
        {
            var list = new List<Placed>();
            var tower = AssetDatabase.LoadAssetAtPath<GameObject>(LookoutTowerBuilder.TowerPrefabPath);
            if (tower == null) return list;
            foreach (Transform g in tower.transform)
            {
                if (g.name != GroupName) continue;              // older prefabs can have more than one group
                foreach (Transform t in g)
                    if (IsOurProp(t, out string path))
                        list.Add(new Placed { assetPath = path, name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            }
            return list;
        }

        /// <summary>
        /// Puts our props into the Puzzles group under <paramref name="towerRoot"/> (sharing it if it already exists),
        /// adding any default prop that isn't in <paramref name="placed"/> yet (first build, or a prop added since).
        /// </summary>
        public static void Restore(Transform towerRoot, List<Placed> placed)
        {
            var group = Group(towerRoot);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(FogHintNotePath) == null) BuildFogHintNote();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CompassPath) == null) BuildCompass();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MorseLampPath) == null) BuildMorseLamp();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MorseGuidePath) == null) BuildMorseGuide();
            foreach (var d in new[] { DefaultFogHintNotePose(), DefaultCompassPose(), DefaultMorseLampPose(), DefaultMorseGuidePose() })
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
        /// The tower's one Puzzles group: created if missing; if a merge left several, their children are moved into
        /// the first and the extras removed.
        /// </summary>
        static Transform Group(Transform towerRoot)
        {
            Transform group = null;
            var extras = new List<Transform>();
            foreach (Transform c in towerRoot)
                if (c.name == GroupName) { if (group == null) group = c; else extras.Add(c); }
            if (group == null)
            {
                group = new GameObject(GroupName).transform;
                group.SetParent(towerRoot, false);
            }
            foreach (var e in extras)
            {
                while (e.childCount > 0) e.GetChild(0).SetParent(group, false);   // all groups sit at the tower origin
                Object.DestroyImmediate(e.gameObject);
            }
            return group;
        }

        static bool IsOurProp(Transform t, out string assetPath)
        {
            assetPath = null;
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) return false;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            assetPath = src != null ? AssetDatabase.GetAssetPath(src) : null;
            return !string.IsNullOrEmpty(assetPath) && assetPath.StartsWith(PuzzleDir + "/");
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

        /// <summary>
        /// Hung from the south ceiling beam (z -1.95..-1.85, underside 0.12 m below the 2.75 m ceiling) in line with the
        /// door, so from the middle of the cab it blinks in front of the Morse chart pinned beside the door. Bulb bottom
        /// 2.1 m and tag bottom ~1.98 m above the floor, clear of a 1.8 m player.
        /// </summary>
        static Placed DefaultMorseLampPose()
        {
            return new Placed
            {
                assetPath = MorseLampPath,
                name = "Prop_MorseLamp",
                position = new Vector3(0f, 30.5f + 2.75f - 0.12f, -1.9f),
                rotation = Quaternion.identity,
                scale = Vector3.one,
            };
        }

        /// <summary>
        /// On the strip of paneling east of the door (x 0.43..0.67 between the door jamb and the window post; the
        /// south wall's inner face is z -3.53), centred 1.5 m above the cab floor, facing into the cab. Seen from the
        /// middle of the cab, it sits just below and beside the hanging lamp.
        /// </summary>
        static Placed DefaultMorseGuidePose()
        {
            return new Placed
            {
                assetPath = MorseGuidePath,
                name = "Note_MorseGuide",
                // solid strip of wall just west of the cab door (the strip east of it holds the fuse box panel)
                position = new Vector3(-0.55f, 30.5f + 1.5f, -3.5285f),
                // reader faces south, into the wall; pinned a touch crooked
                rotation = Quaternion.LookRotation(Vector3.back, Vector3.up) * Quaternion.Euler(0f, 0f, 2f),
                scale = Vector3.one,
            };
        }
    }
}
