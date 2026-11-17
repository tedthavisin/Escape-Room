using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Walks a CharacterController the size of the desktop player (1.8 m tall, 0.3 m radius; and again crouched)
    /// from the ground up every flight, through the hatch, round the fire finder to each piece of furniture,
    /// out onto the catwalk and back down to the ground, in a throwaway scene. Reports where it gets stuck.
    /// Tools ▸ Lookout Tower ▸ Run Walkability Test. Also runs at the end of Build Everything.
    /// Coordinates are the tower prefab's local x/z (metres; the cab floor is at y = 30.5).
    /// </summary>
    public static class LookoutWalkabilityTest
    {
        struct Wp
        {
            public readonly string name;
            public readonly float x, z, y;   // y: expected height of the feet when reached (NaN = don't check)
            public Wp(string name, float x, float z, float y) { this.name = name; this.x = x; this.z = z; this.y = y; }
        }

        const float Floor = LookoutTowerBuilder.FloorY;
        const float LowTop = LookoutTowerBuilder.LowTopY;
        const int Flights = LookoutTowerBuilder.LowFlights;   // even: the last switchback ends on the south landing

        // Lane A (west, x -1.24) climbs north, lane B (east, x -0.28) climbs south. Flight j (0-based) uses lane A
        // when j is even; landing m (the top of flight m-1) is at z = +1.3 when m is odd and -1.3 when m is even.
        static float LaneX(int flight) => flight % 2 == 0 ? -1.24f : -0.28f;
        static float LandingZ(int m) => m % 2 == 1 ? 1.3f : -1.3f;
        static float LandingY(int m) => LowTop * m / Flights;

        static readonly Wp[] Climb = BuildClimb();
        static readonly Wp[] Descend = BuildDescend();

        static Wp[] BuildClimb()
        {
            var l = new List<Wp>
            {
                new Wp("start, south of the tower", -1.24f, -6.0f, 0f),
                new Wp("foot of the stairs", -1.24f, -1.5f, 0f),
            };
            for (int m = 1; m <= Flights; m++)
            {
                l.Add(new Wp(m == Flights ? "top landing" : $"landing {m}", LaneX(m - 1), LandingZ(m), LandingY(m)));
                if (m < Flights) l.Add(new Wp($"landing {m}, other lane", LaneX(m), LandingZ(m), LandingY(m)));
            }
            l.Add(new Wp("foot of the ladder", -1.24f, -1.3f, LowTop));
            l.Add(new Wp("up the ladder and through the hatch", -1.24f, 0.70f, Floor));
            return l.ToArray();
        }

        static Wp[] BuildDescend()
        {
            var l = new List<Wp>
            {
                new Wp("back in through the door", 0f, -3.2f, Floor),
                new Wp("cab: south-west of the fire finder", -0.73f, -1.9f, Floor),
                new Wp("cab: between fire finder and hatch", -0.73f, 0.35f, Floor),
                new Wp("cab: north of the hatch", -0.95f, 0.75f, Floor),
                new Wp("cab: top of the ladder", -1.24f, 0.75f, Floor),
                new Wp("down the ladder to the top landing", -1.24f, -1.3f, LowTop),
                new Wp("top landing, east lane", -0.28f, -1.3f, LowTop),
            };
            for (int m = Flights - 1; m >= 1; m--)
            {
                l.Add(new Wp($"down to landing {m}", LaneX(m), LandingZ(m), LandingY(m)));
                l.Add(new Wp($"landing {m}, other lane", LaneX(m - 1), LandingZ(m), LandingY(m)));
            }
            l.Add(new Wp("down to the ground", -1.24f, -1.5f, 0f));
            l.Add(new Wp("out from under the tower", -1.24f, -6.0f, 0f));
            return l.ToArray();
        }

        // The cab is 7.06 m across inside: desk on the east wall, stove NE, cot NW, cupboard SE, map board on the
        // south wall, fire finder in the middle, hatch (x -1.65..-0.83, z -1.32..0.28) west of it.
        static readonly Wp[] Cab =
        {
            new Wp("cab: north-west of the fire finder", -0.55f, 1.2f, Floor),
            new Wp("cab: north of the fire finder", 0.05f, 1.4f, Floor),
            new Wp("cab: north-east of the fire finder", 1.4f, 1.4f, Floor),
            new Wp("cab: in front of the stove", 2.9f, 1.7f, Floor),
            new Wp("cab: back from the stove", 1.4f, 1.4f, Floor),
            new Wp("cab: east of the fire finder, by the desk", 2.3f, 0.0f, Floor),
            new Wp("cab: towards the cupboard", 2.3f, -2.4f, Floor),
            new Wp("cab: in front of the cupboard", 3.06f, -2.6f, Floor),
            new Wp("cab: south of the fire finder", 0.05f, -1.4f, Floor),
            new Wp("cab: south-west of the fire finder", -0.73f, -0.75f, Floor),
            new Wp("cab: between fire finder and hatch", -0.73f, 0.35f, Floor),
            new Wp("cab: round the fire finder (loop closed)", -0.55f, 1.2f, Floor),
            new Wp("cab: in front of the cot", -2.4f, 2.4f, Floor),
            new Wp("cab: west of the hatch", -2.4f, -0.5f, Floor),
            new Wp("cab: south of the hatch", -0.73f, -1.9f, Floor),
            new Wp("cab: at the map board", -2.2f, -3.2f, Floor),
            new Wp("cab: inside the door", 0f, -3.2f, Floor),
            new Wp("doorway", 0f, -3.75f, Floor),
            new Wp("catwalk", 0f, -4.15f, Floor),
        };

        // just the steep last flight: from where the stairs arrive on the top landing, through the hatch
        static readonly Wp[] Ladder =
        {
            new Wp("top landing", -0.28f, -1.3f, LowTop),
            new Wp("foot of the ladder", -1.24f, -1.3f, LowTop),
            new Wp("up the ladder and through the hatch", -1.24f, 0.70f, Floor),
        };

        [MenuItem("Tools/Lookout Tower/Run Walkability Test", priority = 41)]
        static void Menu()
        {
            bool ok = Run(out string report);
            EditorUtility.DisplayDialog("Lookout Tower: walkability " + (ok ? "PASSED" : "FAILED"), report, "OK");
        }

        /// <summary>Runs every route at standing and crouched height. Results go to the Console and build_log.txt.</summary>
        public static bool Run(out string report)
        {
            var sb = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report = "Stop Play mode first.";
                return false;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LookoutTowerBuilder.TowerPrefabPath);
            if (prefab == null)
            {
                report = "Build the tower first (Tools ▸ Lookout Tower ▸ Build Everything).";
                LookoutTowerBuilder.Log("WALKTEST skipped: " + report);
                return false;
            }

            var route = new List<Wp>();
            route.AddRange(Climb);
            route.AddRange(Cab);
            route.AddRange(Descend);

            // A preview scene keeps the test out of whatever scene is open. If CharacterControllers don't move
            // there in this Unity version, fall back to a temporary additive scene.
            bool ok = false, simulated = false;
            foreach (bool preview in new[] { true, false })
            {
                if (!preview && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                {
                    sb.AppendLine("(Could not fall back to an additive test scene: save the open scene first.)");
                    break;
                }
                sb.Clear();
                ok = RunIn(preview, prefab, route.ToArray(), sb, out simulated);
                if (simulated) break;
            }
            if (!simulated) sb.AppendLine("CharacterController.Move did nothing outside Play mode, so nothing was tested.");

            report = sb.ToString().TrimEnd();
            foreach (var line in report.Split('\n')) LookoutTowerBuilder.Log("WALKTEST " + line.TrimEnd());
            if (ok) Debug.Log("[LookoutTower] Walkability test passed.\n" + report);
            else Debug.LogWarning("[LookoutTower] Walkability test FAILED.\n" + report);
            return ok && simulated;
        }

        static bool RunIn(bool preview, GameObject prefab, Wp[] route, StringBuilder sb, out bool simulated)
        {
            simulated = false;
            Scene active = SceneManager.GetActiveScene();
            Scene scene = preview ? EditorSceneManager.NewPreviewScene()
                                  : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            bool allOk = true;
            try
            {
                // well clear of the forest scene, in case this Unity version shares one physics world between scenes
                Vector3 origin = new Vector3(600f, 0f, 600f);
                var tower = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                tower.transform.SetPositionAndRotation(origin, Quaternion.identity);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Ground";
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = origin + new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(80f, 1f, 80f);
                int opened = 0;
                foreach (var h in tower.GetComponentsInChildren<HingedOpenable>(true))
                    if (h.name == "Door" || h.name == "Trapdoor") { h.SetOpenImmediate(true); opened++; }
                Physics.SyncTransforms();
                sb.AppendLine($"Test scene: {(preview ? "preview" : "additive")}, door + trapdoor opened ({opened}/2).");

                foreach (float height in new[] { 1.8f, 1.15f })
                {
                    var r = Walk(scene, origin, height, route);
                    if (r.notSimulated) return false;
                    simulated = true;
                    allOk &= r.ok;
                    sb.AppendLine($"{(r.ok ? "PASS" : "FAIL")}  player {height:0.00} m: {r.message}");
                }

                // How much headroom is left: the tallest capsule that still gets up the ladder and through the hatch.
                // (In VR the capsule usually follows the headset, so this is roughly the tallest eye height that fits.)
                float tallest = 0f;
                string limit = "fits at every height tried";
                for (int k = 0; k <= 12; k++)
                {
                    float h = 1.8f + 0.05f * k;
                    var r = Walk(scene, origin, h, Ladder);
                    if (r.ok) { tallest = h; continue; }
                    limit = $"at {h:0.00} m: {r.message}";
                    break;
                }
                sb.AppendLine(tallest > 0f
                    ? $"Headroom: capsules up to {tallest:0.00} m tall get from the top landing up through the hatch ({limit})"
                    : $"Headroom: even 1.80 m doesn't fit ({limit})");

                // Control: with the trapdoor shut the ladder must be blocked, otherwise this test isn't seeing collisions.
                var trap = tower.GetComponentsInChildren<HingedOpenable>(true).FirstOrDefault(o => o.name == "Trapdoor");
                if (trap != null)
                {
                    trap.SetOpenImmediate(false);
                    Physics.SyncTransforms();
                    var shut = Walk(scene, origin, 1.8f, Ladder);
                    trap.SetOpenImmediate(true);
                    Physics.SyncTransforms();
                    if (shut.ok)
                    {
                        allOk = false;
                        sb.AppendLine("FAIL  control: walked up through the CLOSED trapdoor, so collisions aren't being tested properly");
                    }
                    else sb.AppendLine("OK    control: with the trapdoor shut the ladder is blocked, as it should be");
                }
            }
            finally
            {
                if (preview) EditorSceneManager.ClosePreviewScene(scene);
                else
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (active.IsValid()) SceneManager.SetActiveScene(active);
                }
            }
            return allOk;
        }

        struct WalkResult
        {
            public bool ok, notSimulated;
            public string message;
        }

        static WalkResult Walk(Scene scene, Vector3 origin, float height, Wp[] route)
        {
            const float dt = 1f / 60f, speed = 2.0f, gravity = -15f;
            var go = new GameObject($"Walker {height:0.00}");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = origin + new Vector3(route[0].x, route[0].y + 0.05f, route[0].z);
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.3f;
            cc.height = height;
            cc.center = new Vector3(0f, height * 0.5f, 0f);
            cc.slopeLimit = 70f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;
            Physics.SyncTransforms();

            Vector3 start = go.transform.position;
            float vy = 0f, t = 0f, best = float.MaxValue, sinceProgress = 0f;
            int i = 1;
            try
            {
                while (i < route.Length)
                {
                    Vector3 p = go.transform.position;
                    Vector3 flat = new Vector3(origin.x + route[i].x - p.x, 0f, origin.z + route[i].z - p.z);
                    float dist = flat.magnitude;
                    if (dist < 0.12f)
                    {
                        float feet = p.y - origin.y;
                        if (!float.IsNaN(route[i].y) && Mathf.Abs(feet - route[i].y) > 0.3f)
                            return Fail($"reached '{route[i].name}' at the wrong height (feet at {feet:0.00} m, expected {route[i].y:0.00})");
                        i++;
                        best = float.MaxValue;
                        sinceProgress = 0f;
                        continue;
                    }
                    Vector3 v = flat / dist * Mathf.Min(speed, dist / dt);
                    if (cc.isGrounded && vy < 0f) vy = -2f;
                    vy += gravity * dt;
                    v.y = vy;
                    cc.Move(v * dt);
                    t += dt;

                    if (t > 0.5f && t < 0.5f + dt * 1.5f && (go.transform.position - start).magnitude < 0.02f)
                        return new WalkResult { notSimulated = true, message = "couldn't move at all from where it started" };
                    if (dist < best - 0.01f) { best = dist; sinceProgress = 0f; }
                    else sinceProgress += dt;
                    Vector3 q = go.transform.position - origin;
                    if (sinceProgress > 2.5f)
                        return Fail($"STUCK on the way to '{route[i].name}' (#{i}) at ({q.x:0.00}, {q.y:0.00}, {q.z:0.00})");
                    if (q.y < -1f) return Fail("fell through the ground");
                    if (t > 1200f) return Fail("ran out of time");
                }
                return new WalkResult { ok = true, message = $"all {route.Length} waypoints reached ({t:0} s of walking)" };
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static WalkResult Fail(string msg) => new WalkResult { ok = false, message = msg };
    }
}
