using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Builds the fuse box puzzle from Models/FuseBox.fbx (made by LookoutTowerSource/blender_scripts/build_fusebox.py):
    /// materials, the FuseBoxPuzzle prefab (panel, door, main switch, sockets, fuses, tin, crate, lamps, labels) and
    /// puts it in the tower prefab under Puzzles/, wired to the radio.
    ///
    /// Menu: Tools ▸ Lookout Tower ▸ Build Fuse Box Puzzle. Build Everything re-attaches the existing prefab (it only
    /// rebuilds it if it's missing), so tweaks made to Prefabs/FuseBoxPuzzle.prefab survive a tower rebuild.
    /// </summary>
    public static class FuseBoxPuzzleBuilder
    {
        const string Root = LookoutTowerBuilder.Root;
        const string ModelPath = Root + "/Models/FuseBox.fbx";
        const string MatDir = Root + "/Materials";
        public const string PrefabPath = Root + "/Prefabs/FuseBoxPuzzle.prefab";
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        const string PuzzlesGroup = "Puzzles";
        const string InstanceName = "FuseBoxPuzzle";

        // ------------------------------------------------------------------ puzzle setup (edit here)
        // Slot ratings and what starts where. Colours must match the M_FuseBand_* materials below.
        static readonly (string circuit, int amps, string colour)[] Circuits =
        {
            ("Lights", 15, "blue"), ("Radio", 10, "red"), ("Catwalk", 20, "yellow"),
        };

        class FuseSpec
        {
            public string spot;      // Slot_1..3, Tin, Crate_1, Crate_2
            public int amps;
            public string colour;
            public bool blown;
        }

        static readonly FuseSpec[] StartFuses =
        {
            new FuseSpec { spot = "Slot_1", amps = 15, colour = "blue", blown = true },     // the reason the lights are out
            new FuseSpec { spot = "Slot_2", amps = 20, colour = "yellow" },                // someone jammed a 20 in the radio slot
            new FuseSpec { spot = "Tin", amps = 15, colour = "blue" },                     // red tin on the desk
            new FuseSpec { spot = "Crate_1", amps = 10, colour = "red" },                  // crate on the north wall shelf
            new FuseSpec { spot = "Crate_2", amps = 30, colour = "green" },               // decoy: too big for anything
        };

        // The reveal once solved: the ranger's pinholes in the pendant shade throw this onto the ceiling ("_" = blank),
        // matching the fog window's "_ _ 8 _". It lands LeakOffset (x, z metres) from the lamp and reads the right way
        // up for someone facing +Z (turned round from the panel) and looking up: left to right along +X, and the tops
        // of the characters toward them (-Z), because the near side of a ceiling is the top of your view.
        const string LeakCode = "___9";
        static readonly Vector2 LeakOffset = new Vector2(-0.06f, -0.38f);   // clear of the ceiling box and the lights conduit
        static readonly Vector3 LeakReadRight = Vector3.right, LeakReadUp = Vector3.back;
        const float LeakPitch = 0.026f;                 // hole spacing on the ceiling
        const float CeilingY = F + 2.75f;              // flat cab ceiling (build_tower.py CEIL_Z)
        const string TexDir = Root + "/Textures";
        const string LeakTexPath = TexDir + "/T_FuseBox_LightLeak.png";
        static readonly Vector2 LeakSize = new Vector2(1.04f, 0.52f);   // decal quad on the ceiling, metres

        static readonly Dictionary<string, Color> BandColours = new Dictionary<string, Color>
        {
            { "blue", new Color(0.09f, 0.22f, 0.62f) }, { "red", new Color(0.6f, 0.06f, 0.05f) },
            { "yellow", new Color(0.85f, 0.65f, 0.08f) }, { "green", new Color(0.1f, 0.42f, 0.16f) },
        };

        static string Hex(string colour)
        {
            var c = BandColours.TryGetValue(colour, out var v) ? v : Color.black;
            return "#" + ColorUtility.ToHtmlStringRGB(c * 0.85f);
        }

        // Panel geometry (Unity metres; mirrors build_fusebox.py, Blender (x, y, z) -> Unity (x, z, y)).
        const float F = LookoutTowerBuilder.FloorY;
        const float WallZ = -3.53f;                     // inside face of the south wall
        const float BoardFront = WallZ + 0.02f;
        static readonly Vector3 Out = Vector3.forward;   // out of the south wall, into the cab
        const float SeatDepth = 0.0165f;                 // how far a fuse's thread goes into the slot

        // ------------------------------------------------------------------ menu
        [MenuItem("Tools/Lookout Tower/Build Fuse Box Puzzle", priority = 10)]
        public static void BuildMenu()
        {
            try
            {
                var prefab = BuildPrefab();
                if (AttachToTowerPrefab(prefab))
                {
                    Debug.Log($"[LookoutTower] Fuse box puzzle built ({PrefabPath}) and added to the tower prefab under {PuzzlesGroup}/.");
                    FuseBoxReachCheck.Run(false);
                }
                EditorGUIUtility.PingObject(prefab);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Fuse box puzzle", "Build failed: " + e.Message + "\n\nSee the Console for details.", "OK");
            }
        }

        /// <summary>Called by LookoutTowerBuilder while it builds the tower prefab. Uses the existing puzzle prefab if there is one.</summary>
        public static void AttachToTower(GameObject towerRoot)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                LookoutTowerBuilder.Log("Fuse box: no FuseBox.fbx, skipped.");
                return;
            }
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null) prefab = BuildPrefab();
                Attach(towerRoot, prefab);
                LookoutTowerBuilder.Log("Fuse box puzzle attached.");
            }
            catch (Exception e)
            {
                LookoutTowerBuilder.Log("Fuse box puzzle FAILED: " + e.Message);
                Debug.LogException(e);
            }
        }

        static bool AttachToTowerPrefab(GameObject prefab)
        {
            var path = LookoutTowerBuilder.TowerPrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning("[LookoutTower] Built the fuse box prefab, but there's no tower prefab yet. Run Build Everything (it adds the puzzle).");
                return false;
            }
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Attach(contents, prefab);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return true;
        }

        static void Attach(GameObject towerRoot, GameObject prefab)
        {
            var group = towerRoot.transform.Find(PuzzlesGroup);
            if (group == null)
            {
                group = new GameObject(PuzzlesGroup).transform;
                group.SetParent(towerRoot.transform, false);
            }
            var old = group.Find(InstanceName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            inst.name = InstanceName;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;

            // the radio dial goes dark until its circuit is live
            var radioOut = inst.GetComponentsInChildren<PoweredOutput>(true).FirstOrDefault(p => p.name == "Power_Radio");
            var radio = FindDeep(towerRoot.transform, "Prop_Radio");
            if (radioOut != null && radio != null)
            {
                radioOut.emissiveRenderers = radio.GetComponentsInChildren<Renderer>(true);
                radioOut.emissiveMaterialPrefix = "M_DialGlow";
                radioOut.transform.position = radio.position + Vector3.up * 0.12f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(radioOut);
                PrefabUtility.RecordPrefabInstancePropertyModifications(radioOut.transform);
            }
            else LookoutTowerBuilder.Log("Fuse box: radio not found; the radio circuit drives nothing.");
        }

        // ------------------------------------------------------------------ prefab
        public static GameObject BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new Exception($"Missing {ModelPath}. Copy FuseBox.fbx from LookoutTowerSource (see README).");
            var mats = EnsureMaterials();
            RemapModelMaterials(mats);

            var src = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(src, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            src.transform.position = Vector3.zero;
            NormalizeAxes(src);
            AssignMaterials(src, mats);
            var kids = src.transform.Cast<Transform>().ToDictionary(t => t.name, t => t);
            Func<string, Transform> K = n =>
            {
                Transform t;
                if (!kids.TryGetValue(n, out t)) throw new Exception($"FuseBox.fbx has no '{n}'. Re-export it with build_fusebox.py.");
                return t;
            };
            Func<string, Vector3> M = n => K("MK_" + n).position;

            var root = new GameObject(InstanceName);
            try
            {
                // sanity check: slot 1 should be on the south wall east of the door at eye height
                var s1 = M("Socket_1");
                if (Mathf.Abs(s1.z - (WallZ + 0.064f)) > 0.05f || Mathf.Abs(s1.y - (F + 1.555f)) > 0.05f)
                    Debug.LogWarning($"[LookoutTower] Fuse box markers look misplaced (slot 1 at {s1}). Check the FBX axis settings.");

                // ---------------- panel
                var panelGo = Group(root, "FuseBoxPanel", K("FuseBox_Panel").position);
                Adopt(K("FuseBox_Panel"), panelGo, true);
                var panel = panelGo.AddComponent<FuseBoxPanel>();
                PanelColliders(panelGo.transform);

                // door
                var door = K("FuseBox_Door");
                Adopt(door, panelGo, false);
                var hinge = door.gameObject.AddComponent<HingedOpenable>();
                hinge.localAxis = door.InverseTransformDirection(Vector3.up).normalized;
                hinge.openAngle = SwingSign(door, Vector3.up, d => d.z) * 105f;     // leaf swings out into the cab
                hinge.speed = 240f;
                hinge.displayName = "fuse box door";
                hinge.stopAtCharacters = false;
                WorldBox(door, "Collider", RendererBounds(door), 0.004f);
                panel.door = hinge;
                Label(door, "Wiring Card", M("DiagramCard"), -Out, DiagramText(), 0.152f, 0.335f, Ink, TextAlignmentOptions.Top);

                // main switch handle
                var handle = K("FuseBox_SwitchHandle");
                Adopt(handle, panelGo, false);
                var sw = handle.gameObject.AddComponent<FuseBoxSwitch>();
                sw.panel = panel;
                WorldBox(handle, "Collider", RendererBounds(handle), 0.012f);
                panel.switchHandle = handle;
                panel.handleAxis = handle.InverseTransformDirection(Vector3.right).normalized;
                float up = SwingSign(handle, Vector3.right, d => d.y);
                panel.onAngle = up * 55f;
                panel.offAngle = -up * 55f;
                panel.sparkMaterial = mats["M_Spark"];
                var spark = new GameObject("SparkPoint").transform;
                spark.SetParent(panelGo.transform, false);
                spark.SetPositionAndRotation(M("SparkPoint"), Quaternion.FromToRotation(Vector3.up, Out));
                panel.sparkPoint = spark;

                // labels on the panel
                Label(panelGo.transform, "Label FUSES", M("Label_Fuses"), Out, "<b>FUSES</b>", 0.072f, 0.017f, Stamp);
                Label(panelGo.transform, "Label ON", M("Label_On"), Out, "<b>ON</b>", 0.046f, 0.011f, Stamp);
                Label(panelGo.transform, "Label OFF", M("Label_Off"), Out, "<b>OFF</b>", 0.046f, 0.011f, Stamp);
                Label(panelGo.transform, "Label Caution", M("Label_Warning"), Out,
                      "<b>CAUTION</b>  LIVE PARTS\nMAIN SWITCH OFF BEFORE OPENING", 0.162f, 0.04f, new Color(0.45f, 0.07f, 0.04f));

                // slots
                var sockets = new Dictionary<string, FuseSocket>();
                var circuits = new List<FuseCircuit>();
                for (int i = 0; i < 3; i++)
                {
                    var mouth = M($"Socket_{i + 1}");
                    var s = Socket(panelGo.transform, $"Slot_{i + 1}", mouth - Out * SeatDepth, Quaternion.FromToRotation(Vector3.up, Out),
                                   $"slot {i + 1}", false, new Vector3(0.05f, 0.032f, 0.05f), new Vector3(0f, SeatDepth + 0.004f, 0f));
                    s.panel = panel;
                    sockets[$"Slot_{i + 1}"] = s;
                    Label(panelGo.transform, $"Slot Number {i + 1}", M($"SlotNumber_{i + 1}"), Out, $"<b>{i + 1}</b>", 0.026f, 0.026f, new Color(0.05f, 0.04f, 0.03f));
                    circuits.Add(new FuseCircuit { name = Circuits[i].circuit, amps = Circuits[i].amps, socket = s });
                }

                // ---------------- wiring + lamps
                var wiring = K("FuseBox_Conduits");
                Adopt(wiring, root, true);

                var ceiling = Group(root, "CeilingLamp", K("CeilingLamp").position);
                Adopt(K("CeilingLamp"), ceiling, true);
                var bulb = K("CeilingLamp_Bulb");
                Adopt(bulb, ceiling, false);
                var lampLight = PointLight(ceiling.transform, "Lamp Light", M("Light_Ceiling"), new Color(1f, 0.78f, 0.52f), 2.4f, 7.5f, true);
                var lightsOut = Output(ceiling.transform, "Power_Lights", M("Light_Ceiling"), new[] { lampLight }, new[] { bulb.GetComponent<Renderer>() }, "M_Bulb");
                var humSrc = lightsOut.gameObject.AddComponent<AudioSource>();
                humSrc.spatialBlend = 1f;
                humSrc.rolloffMode = AudioRolloffMode.Linear;
                humSrc.minDistance = 0.4f;
                humSrc.maxDistance = 5f;
                humSrc.volume = 0.07f;
                humSrc.dopplerLevel = 0f;
                humSrc.playOnAwake = false;
                humSrc.loop = true;
                lightsOut.hum = humSrc;

                // the reward: pinholes in the top of the shade throw the last hatch digit onto the ceiling once solved
                var leak = LeakDecal(root.transform, M("Light_Ceiling"), mats);
                var reveal = ceiling.AddComponent<LightLeakReveal>();
                reveal.lamp = lampLight;
                reveal.leak = leak;
                reveal.pendant = ceiling.transform;
                var lampAt = M("Light_Ceiling");
                reveal.swingPivot = root.transform.InverseTransformPoint(new Vector3(lampAt.x, CeilingY - 0.06f, lampAt.z));
                reveal.pendulumLength = CeilingY - 0.06f - lampAt.y;
                UnityEventTools.AddPersistentListener(panel.onSolved, reveal.Reveal);

                var catwalk = Group(root, "CatwalkLamp", K("CatwalkLamp").position);
                Adopt(K("CatwalkLamp"), catwalk, true);
                var globe = K("CatwalkLamp_Globe");
                Adopt(globe, catwalk, false);
                var spot = PointLight(catwalk.transform, "Lamp Light", M("Light_Catwalk"), new Color(1f, 0.8f, 0.55f), 3.2f, 8f, false);
                spot.type = LightType.Spot;
                spot.spotAngle = 115f;
                spot.innerSpotAngle = 60f;
                spot.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -1f, -0.75f).normalized, Vector3.back);
                var catOut = Output(catwalk.transform, "Power_Catwalk", M("Light_Catwalk"), new[] { spot }, new[] { globe.GetComponent<Renderer>() }, "M_Bulb");

                var radioOut = Output(root.transform, "Power_Radio", new Vector3(3.26f, F + 0.9f, 0.13f), new Light[0], new Renderer[0], "M_DialGlow");
                circuits[0].outputs = new[] { lightsOut };
                circuits[1].outputs = new[] { radioOut };
                circuits[2].outputs = new[] { catOut };
                panel.circuits = circuits.ToArray();

                // ---------------- spare-fuse tin
                var tin = Group(root, "FuseTin", K("FuseTin").position);
                Adopt(K("FuseTin"), tin, false);
                var tb = RendererBounds(K("FuseTin"));
                WorldBox(tin.transform, "Floor", new Bounds(new Vector3(tb.center.x, tb.min.y + 0.004f, tb.center.z), new Vector3(tb.size.x, 0.008f, tb.size.z)), 0f);
                var lid = K("FuseTin_Lid");
                Adopt(lid, tin, false);
                var lidHinge = lid.gameObject.AddComponent<HingedOpenable>();
                lidHinge.localAxis = lid.InverseTransformDirection(Vector3.right).normalized;
                lidHinge.openAngle = SwingSign(lid, Vector3.right, d => d.y) * 115f;
                lidHinge.speed = 260f;
                lidHinge.displayName = "tin";
                lidHinge.stopAtCharacters = false;
                WorldBox(lid, "Collider", RendererBounds(lid), 0.01f);
                // the whole tin is a target: aiming at its side opens/closes the lid (fuses inside still win)
                WorldBox(tin.transform, "Body", tb, 0.008f);
                tin.AddComponent<InteractProxy>().target = lidHinge;
                // read from the chair side of the desk (west), so the top of the text points east
                Label(lid, "Label", M("Label_Tin"), Vector3.up, "<b>SPARE\nFUSES</b>", 0.044f, 0.03f, Ink, TextAlignmentOptions.Center, Vector3.right);
                sockets["Tin"] = Socket(tin.transform, "Tin_Spot", M("TinSpot_1"), Quaternion.identity, "the tin", true,
                                        new Vector3(0.07f, 0.034f, 0.07f), new Vector3(0f, 0.017f, 0f));

                // ---------------- wall shelf + parts crate (waist height, so nobody has to crouch)
                var shelf = K("Wall_Shelf");
                Adopt(shelf, root, true);
                var sb = RendererBounds(shelf);
                // stop just under the crate's floor so the shelf never catches the aim meant for the crate
                float shelfTop = sb.max.y - 0.017f;
                Box(shelf, "Shelf", new Vector3(sb.center.x, (sb.min.y + shelfTop) / 2f, sb.center.z),
                    new Vector3(sb.size.x, shelfTop - sb.min.y, sb.size.z));
                var crate = Group(root, "PartsCrate", K("PartsCrate").position);
                Adopt(K("PartsCrate"), crate, false);
                CrateColliders(crate.transform, RendererBounds(K("PartsCrate")));
                var clid = K("PartsCrate_Lid");
                Adopt(clid, crate, false);
                var clidHinge = clid.gameObject.AddComponent<HingedOpenable>();
                clidHinge.localAxis = clid.InverseTransformDirection(Vector3.right).normalized;
                clidHinge.openAngle = SwingSign(clid, Vector3.right, d => d.y) * 95f;
                clidHinge.speed = 160f;
                clidHinge.displayName = "crate";
                clidHinge.stopAtCharacters = false;
                WorldBox(clid, "Collider", RendererBounds(clid), 0.01f);
                crate.AddComponent<InteractProxy>().target = clidHinge;     // aiming at the crate's sides works too
                Label(crate.transform, "Label", M("Label_Crate"), Vector3.back, "<b>ELECTRICAL</b>", 0.17f, 0.026f, Ink);
                var lying = new Vector3(0f, 0.0165f, 0f);
                sockets["Crate_1"] = Socket(crate.transform, "Crate_Spot_1", M("CrateSpot_1") + lying,
                                            Quaternion.FromToRotation(Vector3.up, new Vector3(1f, 0f, 0.35f).normalized), "the crate", true,
                                            new Vector3(0.07f, 0.06f, 0.07f), new Vector3(0f, 0.017f, 0f));
                sockets["Crate_2"] = Socket(crate.transform, "Crate_Spot_2", M("CrateSpot_2") + lying,
                                            Quaternion.FromToRotation(Vector3.up, new Vector3(-0.8f, 0f, 0.6f).normalized), "the crate", true,
                                            new Vector3(0.07f, 0.06f, 0.07f), new Vector3(0f, 0.017f, 0f));

                // ---------------- fuses
                var fuseModel = K("Fuse");
                foreach (var spec in StartFuses)
                {
                    if (!sockets.TryGetValue(spec.spot, out var sock)) { Debug.LogWarning("[LookoutTower] Unknown fuse spot " + spec.spot); continue; }
                    var f = MakeFuse(fuseModel, spec, mats);
                    f.transform.SetParent(sock.transform, false);
                    f.transform.localPosition = Vector3.zero;
                    f.transform.localRotation = Quaternion.identity;
                    sock.startFuse = f;
                }

                // static where nothing moves, for baked lighting (not the pendant lamp: it swings when the power surges in)
                foreach (var t in new[] { K("FuseBox_Panel"), wiring, K("CatwalkLamp"), K("PartsCrate"), K("FuseTin"), shelf })
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                                                                         StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok) throw new Exception("Could not save " + PrefabPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(src);
            }
        }

        static readonly Color Ink = new Color(0.12f, 0.09f, 0.06f);
        static readonly Color Stamp = new Color(0.16f, 0.11f, 0.04f);

        static string DiagramText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b><size=125%>CAB CIRCUITS</size></b>\n<size=70%>U.S. FOREST SERVICE</size>\n\n");
            for (int i = 0; i < Circuits.Length; i++)
            {
                var c = Circuits[i];
                sb.Append($"<b>{i + 1}</b>  {c.circuit.ToUpperInvariant()}\n<b>{c.amps} A</b>  <color={Hex(c.colour)}><b>{c.colour.ToUpperInvariant()}</b></color>\n\n");
            }
            sb.Append("<size=72%>USE CORRECT RATING ONLY.\nMAIN SWITCH OFF BEFORE\nCHANGING FUSES.</size>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ pieces
        static GameObject Group(GameObject parent, string name, Vector3 pos)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.transform.position = pos;
            return g;
        }

        static void Adopt(Transform t, GameObject parent, bool keepName)
        {
            t.SetParent(parent.transform, true);
            if (!keepName) t.name = t.name.Replace("FuseBox_", "").Replace("CeilingLamp_", "").Replace("CatwalkLamp_", "")
                                         .Replace("FuseTin_", "").Replace("PartsCrate_", "");
        }

        static FuseSocket Socket(Transform parent, string name, Vector3 pos, Quaternion rot, string spot, bool storage,
                                 Vector3 colliderSize, Vector3 colliderCentre)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var s = go.AddComponent<FuseSocket>();
            s.spotName = spot;
            s.storage = storage;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = colliderSize;
            bc.center = colliderCentre;
            return s;
        }

        static Fuse MakeFuse(Transform model, FuseSpec spec, Dictionary<string, Material> mats)
        {
            var go = new GameObject(spec.blown ? $"Fuse {spec.amps}A {spec.colour} (blown)" : $"Fuse {spec.amps}A {spec.colour}");
            var vis = new GameObject("Model");
            vis.transform.SetParent(go.transform, false);
            vis.transform.localRotation = model.rotation;           // the FBX's axis conversion: model up -> fuse up
            vis.AddComponent<MeshFilter>().sharedMesh = model.GetComponent<MeshFilter>().sharedMesh;
            var mr = vis.AddComponent<MeshRenderer>();
            var src = model.GetComponent<MeshRenderer>().sharedMaterials;
            var band = mats.TryGetValue("M_FuseBand_" + Cap(spec.colour), out var bm) ? bm : mats["M_FuseBand"];
            mr.sharedMaterials = src.Select(m => m != null && m.name.StartsWith("M_FuseBand") ? band : m).ToArray();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.026f;
            col.center = new Vector3(0f, 0.018f, 0f);
            var f = go.AddComponent<Fuse>();
            f.amps = spec.amps;
            f.colourName = spec.colour;
            f.blown = spec.blown;
            f.renderers = new Renderer[] { mr };
            f.windowMaterial = mats["M_FuseWindow"];
            f.linkMaterial = mats["M_FuseLink"];
            f.sootMaterial = mats["M_FuseSoot"];          // Fuse swaps the window to soot at runtime if blown
            return f;
        }

        static PoweredOutput Output(Transform parent, string name, Vector3 pos, Light[] lights, Renderer[] glow, string prefix)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var o = go.AddComponent<PoweredOutput>();
            o.lights = lights;
            o.emissiveRenderers = glow.Where(r => r != null).ToArray();
            o.emissiveMaterialPrefix = prefix;
            o.startPowered = false;
            return o;
        }

        /// <summary>
        /// The light pattern for <see cref="LightLeakReveal"/>: an additive quad just under the ceiling, a little toward
        /// the cab door from the lamp so it lands clear of the ceiling box, reading the right way up for someone who has
        /// just turned round from the fuse box. Its texture is regenerated whenever the prefab is rebuilt.
        /// </summary>
        static Renderer LeakDecal(Transform parent, Vector3 lampAt, Dictionary<string, Material> mats)
        {
            var go = new GameObject("Light Leak (code)");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(lampAt.x + LeakOffset.x, CeilingY - 0.003f, lampAt.z + LeakOffset.y);
            // the built-in quad faces -Z: turn that face down (the texture is generated to match however it's turned)
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            go.transform.localScale = new Vector3(LeakSize.x, LeakSize.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (!AssetDatabase.IsValidFolder(TexDir)) AssetDatabase.CreateFolder(LookoutTowerBuilder.Root, "Textures");
            var tex = LightLeakDecal.Build(LeakTexPath, go.transform, LeakReadRight, LeakReadUp, LeakCode, LeakPitch);
            var m = mats["M_LightLeak"];
            if (m != null)
            {
                m.SetTexture("_BaseMap", tex);
                EditorUtility.SetDirty(m);
                r.sharedMaterial = m;
            }
            r.enabled = false;                      // LightLeakReveal shows it
            return r;
        }

        static Light PointLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.shadowStrength = 0.8f;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.enabled = false;                      // PoweredOutput turns it on with the circuit
            if (shadows)
            {
                // point-light shadows take 6 atlas slots; the low tier keeps it and the lantern within the atlas (same as the lantern)
                var data = l.GetComponent<UniversalAdditionalLightData>();
                if (data == null) data = l.gameObject.AddComponent<UniversalAdditionalLightData>();
                var so = new SerializedObject(data);
                var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
                if (tier != null)
                {
                    tier.intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            return l;
        }

        /// <summary>A text label on a surface. <paramref name="normal"/> points out of the surface towards the reader.</summary>
        static TextMeshPro Label(Transform parent, string name, Vector3 pos, Vector3 normal, string text, float w, float h, Color colour,
                                 TextAlignmentOptions align = TextAlignmentOptions.Center, Vector3? textUp = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            var rt = tmp.rectTransform;
            rt.position = pos + normal.normalized * 0.0006f;
            rt.rotation = Quaternion.LookRotation(-normal.normalized, textUp ?? Vector3.up);
            rt.sizeDelta = new Vector2(w, h);
            var font = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.color = colour;
            tmp.alignment = align;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.001f;
            tmp.fontSizeMax = 72f;
            tmp.richText = true;
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tmp;
        }

        // ------------------------------------------------------------------ colliders
        static void PanelColliders(Transform panel)
        {
            // board, switch box, and the cabinet's back / sides / top / bottom (its front stays open for the slots)
            const float x0 = 0.455f, x1 = 0.645f, y0 = F + 1.24f, y1 = F + 1.64f, d = 0.085f, t = 0.01f;
            float zb = BoardFront, zf = BoardFront + d;
            Box(panel, "Board", new Vector3(0.55f, F + 1.325f, WallZ + 0.01f), new Vector3(0.21f, 0.79f, 0.02f));
            Box(panel, "Switch Box", new Vector3(0.55f, F + 1.09f, zb + 0.035f), new Vector3(0.14f, 0.18f, 0.07f));
            Box(panel, "Cabinet Back", new Vector3(0.55f, (y0 + y1) / 2, zb + 0.006f), new Vector3(x1 - x0, y1 - y0, 0.012f));
            Box(panel, "Cabinet Left", new Vector3(x0 + t / 2, (y0 + y1) / 2, (zb + zf) / 2), new Vector3(t, y1 - y0, d));
            Box(panel, "Cabinet Right", new Vector3(x1 - t / 2, (y0 + y1) / 2, (zb + zf) / 2), new Vector3(t, y1 - y0, d));
            Box(panel, "Cabinet Top", new Vector3(0.55f, y1 - t / 2, (zb + zf) / 2), new Vector3(x1 - x0, t, d));
            Box(panel, "Cabinet Bottom", new Vector3(0.55f, y0 + t / 2, (zb + zf) / 2), new Vector3(x1 - x0, t, d));
        }

        static void CrateColliders(Transform crate, Bounds b)
        {
            // full-height walls: they're the crate's "open me" target (InteractProxy), and InteractionPicker lets the
            // fuses inside win over them once the lid is up
            const float hx = 0.21f, hz = 0.15f, h = 0.12f, t = 0.016f;
            var c = b.center;
            float y0 = b.min.y;
            Box(crate, "Floor", new Vector3(c.x, y0 + 0.006f, c.z), new Vector3(2 * hx, 0.012f, 2 * hz));
            Box(crate, "Wall S", new Vector3(c.x, y0 + h / 2, c.z - hz + t / 2), new Vector3(2 * hx, h, t));
            Box(crate, "Wall N", new Vector3(c.x, y0 + h / 2, c.z + hz - t / 2), new Vector3(2 * hx, h, t));
            Box(crate, "Wall W", new Vector3(c.x - hx + t / 2, y0 + h / 2, c.z), new Vector3(t, h, 2 * hz));
            Box(crate, "Wall E", new Vector3(c.x + hx - t / 2, y0 + h / 2, c.z), new Vector3(t, h, 2 * hz));
        }

        static void Box(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject("COL " + name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>World-aligned box collider on a child (so it follows the parent when it swings).</summary>
        static void WorldBox(Transform parent, string name, Bounds b, float pad)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(b.center, Quaternion.identity);
            go.AddComponent<BoxCollider>().size = b.size + Vector3.one * pad;
        }

        static Bounds RendererBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.02f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>+1 or -1: which way to turn about <paramref name="worldAxis"/> so the part's centre moves the way <paramref name="score"/> likes.</summary>
        static float SwingSign(Transform pivot, Vector3 worldAxis, Func<Vector3, float> score)
        {
            Vector3 rel = RendererBounds(pivot).center - pivot.position;
            float plus = score(Quaternion.AngleAxis(60f, worldAxis) * rel);
            float minus = score(Quaternion.AngleAxis(-60f, worldAxis) * rel);
            return plus >= minus ? 1f : -1f;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ------------------------------------------------------------------ model import
        /// <summary>Same fix-up as the tower's builder: Blender's FBX arrives with every object turned 180° about Y.</summary>
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

        static void RemapModelMaterials(Dictionary<string, Material> mats)
        {
            var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (mi == null) return;
            var names = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>().Select(x => x.name).ToList();
            foreach (var kv in mi.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material) && !names.Contains(kv.Key.name)) names.Add(kv.Key.name);
            bool changed = false;
            foreach (var n in names.Distinct())
            {
                var m = FindMat(n, mats);
                if (m == null) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                mi.GetExternalObjectMap().TryGetValue(id, out var current);
                if (current == m) continue;
                mi.AddRemap(id, m);
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }

        static void AssignMaterials(GameObject go, Dictionary<string, Material> mats)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i] == null) continue;
                    var m = FindMat(ms[i].name.Replace(" (Instance)", "").Trim(), mats);
                    if (m != null) ms[i] = m;
                }
                r.sharedMaterials = ms;
            }
        }

        static Material FindMat(string name, Dictionary<string, Material> mats) =>
            mats.TryGetValue(name, out var m) ? m : AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        // ------------------------------------------------------------------ materials
        static Dictionary<string, Material> EnsureMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new Exception("URP Lit shader not found.");
            var d = new Dictionary<string, Material>();
            Plain(d, lit, "M_Porcelain", new Color(0.86f, 0.84f, 0.78f), 0.82f);
            Plain(d, lit, "M_FuseBand", new Color(0.4f, 0.4f, 0.4f), 0.55f);
            // faintly luminous bands (radium-paint style) so fuses can be spotted in the dark cab
            foreach (var kv in BandColours) Plain(d, lit, "M_FuseBand_" + Cap(kv.Key), kv.Value, 0.55f, 0f, kv.Value * 0.45f);
            Plain(d, lit, "M_FuseWindow", new Color(0.62f, 0.66f, 0.68f), 0.95f);
            Plain(d, lit, "M_FuseLink", new Color(0.75f, 0.75f, 0.77f), 0.7f, 1f);
            Plain(d, lit, "M_FuseSoot", new Color(0.24f, 0.17f, 0.11f), 0.2f);   // scorched brown: reads as "burnt", not as an empty hole
            Plain(d, lit, "M_Enamel", new Color(0.12f, 0.2f, 0.14f), 0.8f);
            Plain(d, lit, "M_EnamelWhite", new Color(0.85f, 0.85f, 0.8f), 0.75f);
            // the "on" glow; PoweredOutput blacks it out while the circuit is dead
            Plain(d, lit, "M_Bulb", new Color(0.95f, 0.9f, 0.76f), 0.8f, 0f, new Color(2.4f, 1.75f, 0.95f));

            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particles != null)
            {
                var m = LoadOrCreate("M_Spark", particles);
                m.SetColor("_BaseColor", Color.white);
                var tex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 2f);                       // additive
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(m);
                d["M_Spark"] = m;
            }
            else d["M_Spark"] = null;
            if (particles != null)
            {
                // the ceiling light pattern: the texture carries the light, added on top of whatever is there
                var m = LoadOrCreate("M_LightLeak", particles);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 2f);                       // additive
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(m);
                d["M_LightLeak"] = m;
            }
            else d["M_LightLeak"] = null;
            AssetDatabase.SaveAssets();
            return d;
        }

        static void Plain(Dictionary<string, Material> d, Shader lit, string n, Color c, float smooth, float metal = 0f, Color? emission = null)
        {
            var m = LoadOrCreate(n, lit);
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetTexture("_BaseMap", null);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            var e = emission ?? Color.black;
            m.SetColor("_EmissionColor", e);
            if (e.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(m);
            d[n] = m;
        }

        static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }
    }
}
