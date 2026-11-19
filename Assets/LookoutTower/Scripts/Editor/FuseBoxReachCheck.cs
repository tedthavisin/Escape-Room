using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Checks, in the open scene, that every interactable in the fuse box puzzle can be picked from where a player
    /// would stand: casts the same rays as the desktop crosshair (InteractionPicker) from standing-height spots all
    /// round each target and reports what got in the way. Writes LookoutTowerSource/fusebox_reach_report.txt.
    /// Menu: Tools ▸ Lookout Tower ▸ Check Fuse Box Reach (also runs after Build Fuse Box Puzzle).
    /// </summary>
    public static class FuseBoxReachCheck
    {
        const float EyeAboveFloor = 1.65f;
        const float Reach = 2.6f;
        static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LookoutTowerSource", "fusebox_reach_report.txt"));

        [MenuItem("Tools/Lookout Tower/Check Fuse Box Reach", priority = 11)]
        static void Menu() => Run(true);

        public static void Run(bool interactive)
        {
            var panel = Object.FindAnyObjectByType<FuseBoxPanel>();
            if (panel == null)
            {
                Debug.LogWarning("[LookoutTower] Reach check: no fuse box in the open scene (open LookoutTower_ForestClearing).");
                return;
            }
            Physics.SyncTransforms();
            var root = panel.transform.parent != null ? panel.transform.parent : panel.transform;
            var sb = new StringBuilder();
            sb.AppendLine($"Fuse box reach check  ({System.DateTime.Now:yyyy-MM-dd HH:mm})  reach {Reach} m, eye {EyeAboveFloor} m");
            int bad = 0, total = 0;
            var targets = root.GetComponentsInChildren<MonoBehaviour>(true)
                              .Where(m => m is IInteractable && !(m is IProxyInteractable)).ToList();
            // Things inside the panel, tin and crate are checked with the door / lids open (that's how players reach
            // them); everything is closed again afterwards.
            var hinges = root.GetComponentsInChildren<HingedOpenable>(true).ToList();
            try
            {
            foreach (var t in targets)
            {
                bool inside = t is FuseSocket || t is Fuse;
                foreach (var h in hinges) h.SetOpenImmediate(inside);
                Physics.SyncTransforms();
                var cols = t.GetComponentsInChildren<Collider>(true).Where(c => c.enabled).ToList();
                if (t is FuseSocket) cols = t.GetComponents<Collider>().Where(c => c.enabled).ToList();   // the slot itself, not its fuse
                if (cols.Count == 0) { sb.AppendLine($"  NO COLLIDER  {PathOf(t.transform)}"); bad++; continue; }
                var b = cols[0].bounds;
                foreach (var c in cols) b.Encapsulate(c.bounds);
                Vector3 aim = b.center;
                int ok = 0, tried = 0;
                var blockers = new Dictionary<string, int>();
                foreach (var eye in Eyes(aim, LookoutTowerBuilder.FloorY))
                {
                    var ray = new Ray(eye, (aim - eye).normalized);
                    if (Vector3.Distance(eye, aim) > Reach) continue;
                    tried++;
                    var picked = InteractionPicker.Pick(ray, Reach);
                    if (Same(Resolve(picked), t)) { ok++; continue; }
                    string why;
                    if (Physics.Raycast(ray, out var h, Reach, ~0, QueryTriggerInteraction.Ignore))
                        why = $"{PathOf(h.collider.transform)} @ {h.distance:0.00} m" + (picked != null ? $" (picked {PathOf(((MonoBehaviour)picked).transform)})" : "");
                    else why = "ray hit nothing";
                    blockers[why] = blockers.TryGetValue(why, out var k) ? k + 1 : 1;
                }
                total++;
                bool fine = tried > 0 && ok * 2 >= tried;
                if (!fine) bad++;
                sb.AppendLine($"  {(fine ? "ok " : "BAD")}  {ok,2}/{tried,-2}  {PathOf(t.transform)}  [{t.GetType().Name}]");
                foreach (var kv in blockers.OrderByDescending(x => x.Value).Take(3))
                    sb.AppendLine($"           {kv.Value,2}x  {kv.Key}");
            }
            }
            finally
            {
                foreach (var h in hinges) h.SetOpenImmediate(false);
                Physics.SyncTransforms();
            }
            sb.AppendLine(bad == 0 ? "All reachable." : $"{bad} of {total} targets hard to reach.");
            try { File.WriteAllText(ReportPath, sb.ToString()); } catch (IOException) { }
            if (bad == 0) Debug.Log("[LookoutTower] Fuse box reach check: everything reachable from standing height.");
            else Debug.LogWarning($"[LookoutTower] Fuse box reach check: {bad} target(s) hard to reach. See {ReportPath}\n{sb}");
            if (interactive) EditorUtility.DisplayDialog("Fuse box reach", bad == 0 ? "Everything reachable." : $"{bad} target(s) hard to reach.\nSee the Console or {ReportPath}.", "OK");
        }

        /// <summary>Standing eye positions round a target, inside the cab and not inside furniture.</summary>
        static IEnumerable<Vector3> Eyes(Vector3 aim, float floorY)
        {
            const float cabHalf = 3.3f;
            for (int a = 0; a < 16; a++)
            {
                float ang = a * Mathf.PI * 2f / 16f;
                foreach (float r in new[] { 0.65f, 0.95f, 1.3f })
                {
                    var feet = new Vector3(aim.x + Mathf.Cos(ang) * r, floorY, aim.z + Mathf.Sin(ang) * r);
                    if (Mathf.Abs(feet.x) > cabHalf || Mathf.Abs(feet.z) > cabHalf) continue;
                    // a standing body has to fit here (capsule, radius 0.25)
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.55f, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    yield return feet + Vector3.up * EyeAboveFloor;
                }
            }
        }

        /// <summary>A fuse in a slot and the slot do the same thing when used.</summary>
        static bool Same(IInteractable picked, MonoBehaviour target)
        {
            if (picked == null) return false;
            if (ReferenceEquals(picked, target)) return true;
            if (picked is Fuse f && f.Socket != null && ReferenceEquals(f.Socket, target)) return true;
            if (target is Fuse tf && picked is FuseSocket ps && ReferenceEquals(ps.Current, tf)) return true;
            if (picked is Fuse f2 && target is FuseSocket ts && ts.startFuse == f2) return true;
            if (target is Fuse tf2 && picked is FuseSocket ps2 && ps2.startFuse == tf2) return true;
            return false;
        }

        static IInteractable Resolve(IInteractable it)
        {
            if (it is InteractProxy p) return p.target as IInteractable;
            return it;
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 4; i++, t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
