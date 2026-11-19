using UnityEditor;
using UnityEngine;

namespace LookoutTower.EditorTools
{
    /// <summary>Play-mode shortcuts for testing the fuse box without solving it every time.</summary>
    public static class FuseBoxDebug
    {
        const string Menu = "Tools/Lookout Tower/Fuse Box: Skip To Reveal (Play mode)";

        /// <summary>Powers every circuit, plays the ceiling reveal, and points the Scene view up at it.</summary>
        [MenuItem(Menu, priority = 41)]
        static void SkipToReveal()
        {
            var panel = Object.FindAnyObjectByType<FuseBoxPanel>();
            if (panel != null)
                foreach (var c in panel.circuits)
                    if (c.outputs != null)
                        foreach (var o in c.outputs) if (o != null) o.SetPowered(true);
            var reveal = Object.FindAnyObjectByType<LightLeakReveal>();
            if (reveal == null) { Debug.LogWarning("[LookoutTower] No LightLeakReveal in the scene."); return; }
            reveal.Reveal();

            var view = SceneView.lastActiveSceneView;
            if (view != null && reveal.leak != null)
            {
                // stand a couple of metres toward the door and look up at the ceiling in front of the lamp
                var target = reveal.leak.transform.position;
                view.LookAt(target, Quaternion.LookRotation(new Vector3(0f, 0.55f, 1f).normalized, Vector3.up), 1.2f);
                view.Repaint();
            }
        }

        [MenuItem(Menu, true)]
        static bool SkipToRevealValid() => Application.isPlaying;

        const string LookMenu = "Tools/Lookout Tower/Fuse Box: Toggle Reveal Camera (Play mode)";
        const string LookCamName = "Fuse Box Reveal Camera (debug)";

        /// <summary>
        /// Puts a temporary camera over the Game view, standing where a player turned round from the fuse box would
        /// and looking up at where the code lands. Run again to remove it.
        /// </summary>
        [MenuItem(LookMenu, priority = 42)]
        static void ToggleRevealCamera()
        {
            var old = GameObject.Find(LookCamName);
            if (old != null) { Object.Destroy(old); return; }
            var reveal = Object.FindAnyObjectByType<LightLeakReveal>();
            if (reveal == null || reveal.leak == null) { Debug.LogWarning("[LookoutTower] No LightLeakReveal in the scene."); return; }
            var target = reveal.leak.transform.position;
            var go = new GameObject(LookCamName);
            var cam = go.AddComponent<Camera>();
            cam.depth = 99f;
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 60f;
            go.transform.position = new Vector3(target.x, target.y - 1.15f, target.z - 1.3f);
            go.transform.rotation = Quaternion.LookRotation(target - go.transform.position, Vector3.up);
        }

        [MenuItem(LookMenu, true)]
        static bool ToggleRevealCameraValid() => Application.isPlaying;
    }
}
