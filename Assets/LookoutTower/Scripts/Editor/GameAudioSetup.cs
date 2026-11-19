using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Wires the CC0 sounds in Audio/ (see Audio/CREDITS.md), the escape timer and the win screen into the tower prefab
    /// and the scene.
    /// Build Everything calls it, so rebuilds keep everything; Tools ▸ Lookout Tower ▸ Apply Timer And Sounds re-applies
    /// it to the current prefab and open scene. The cue times below pick the useful part of each recording.
    /// </summary>
    public static class GameAudioSetup
    {
        const string AudioDir = LookoutTowerBuilder.Root + "/Audio";
        const string FadeMatPath = LookoutTowerBuilder.Root + "/Materials/M_ScreenFade.mat";
        const string GameRootName = "--- Game ---";

        static AudioClip Clip(string name)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{name}.ogg");
            if (c == null) Debug.LogWarning($"[LookoutTower] Missing sound {AudioDir}/{name}.ogg");
            return c;
        }

        [MenuItem("Tools/Lookout Tower/Apply Timer And Sounds", priority = 31)]
        public static void ApplyMenu()
        {
            var root = PrefabUtility.LoadPrefabContents(LookoutTowerBuilder.TowerPrefabPath);
            try
            {
                ApplyToTower(root);
                PrefabUtility.SaveAsPrefabAsset(root, LookoutTowerBuilder.TowerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var scene = SceneManager.GetActiveScene();
            if (scene.path == LookoutTowerBuilder.ScenePath)
            {
                ApplyToScene(scene);
                EditorSceneManager.SaveScene(scene);
            }
            else Debug.LogWarning($"[LookoutTower] Open {LookoutTowerBuilder.ScenePath} to add the timer and ambience to it.");
            Debug.Log("[LookoutTower] Applied the timer and sounds.");
        }

        // ------------------------------------------------------------------ tower prefab: object and puzzle sounds
        public static void ApplyToTower(GameObject root)
        {
            var creak = Clip("door_creak_open");
            var close = Clip("door_close");
            var window = Clip("window_open_close");

            foreach (var h in root.GetComponentsInChildren<HingedOpenable>(true))
            {
                string n = h.gameObject.name;
                if (n == "Door" || n == "Trapdoor")
                {
                    float heavy = n == "Trapdoor" ? 0.85f : 1f;   // the hatch lid is thick and heavy
                    h.openSound = SoundCue.Of(creak, 0.1f, 1.6f, 0.9f, heavy, 0.05f);
                    h.closeSound = SoundCue.Of(creak, 1.7f, 1.3f, 0.7f, heavy * 0.95f, 0.05f);
                    h.closedSound = SoundCue.Of(close, 2.45f, 1.1f, 1f, heavy, 0.04f);      // latch / lid landing
                    h.lockedSound = SoundCue.Of(close, 2.5f, 0.45f, 0.55f, 1.25f, 0.08f);   // a thud: it won't budge
                }
                else if (n.StartsWith("Window_"))
                {
                    h.openSound = SoundCue.Of(window, 0f, 1.6f, 0.8f, 1f, 0.06f);
                    h.closeSound = SoundCue.Of(window, 3.0f, 1.9f, 0.8f, 1f, 0.06f);
                    h.closedSound = default;
                    h.lockedSound = default;
                }
            }

            // fire finder: a ratchet click per 15° step, and radio static when it's on the smoke's bearing
            var ratchet = Clip("ratchet_slow");
            foreach (var t in root.GetComponentsInChildren<TurnOnInteract>(true))
            {
                t.turnSound = SoundCue.Of(ratchet, 0.5f, 0.35f, 0.7f, 1f, 0.08f);
                t.turnSoundSpread = 12f;
            }
            var radio = FindChild(root.transform, "Prop_Radio");
            foreach (var s in root.GetComponentsInChildren<FireFinderSighting>(true))
            {
                s.alignedSound = SoundCue.Of(Clip("radio_interference"), 0.4f, 4f, 0.55f);
                s.alignedSoundAt = radio;
            }

            // the fog window reveals its writing silently (no reveal sound)
            foreach (var w in root.GetComponentsInChildren<BreathFogWindow>(true))
                w.revealSound = default;

            // paper rustle as you step up to a note
            var paper = SoundCue.Of(Clip("paper_handling"), 12.8f, 1.5f, 0.45f, 1f, 0.06f);
            foreach (var name in new[] { "Note_FogHint", "DispatchNote", "Note_MorseGuide" })
            {
                var t = FindChild(root.transform, name);
                if (t == null) continue;
                var p = t.GetComponent<ProximitySound>();
                if (p == null) p = t.gameObject.AddComponent<ProximitySound>();
                p.sound = paper;
            }
        }

        // ------------------------------------------------------------------ scene: timer, wrist display, ambience
        public static void ApplyToScene(Scene scene)
        {
            GameObject gameRoot = null;
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == GameRootName) gameRoot = r;
            if (gameRoot == null)
            {
                gameRoot = new GameObject(GameRootName);
                SceneManager.MoveGameObjectToScene(gameRoot, scene);
            }

            var timerGo = Child(gameRoot.transform, "Escape Timer");
            var timer = timerGo.GetComponent<EscapeTimer>();
            if (timer == null) timer = timerGo.AddComponent<EscapeTimer>();
            timer.tick = SoundCue.Of(Clip("clock_tick"), 0f, 0f, 0.22f);
            timer.heartbeat = SoundCue.Of(Clip("heartbeat"), 0f, 0f, 0.9f);
            timer.alarm = SoundCue.Of(Clip("alarm_clock_ring"), 0f, 9.6f, 0.9f);
            timer.fadeMaterial = FadeMaterial();
            if (timerGo.GetComponent<WristTimer>() == null) timerGo.AddComponent<WristTimer>().timer = timer;

            var winGo = Child(gameRoot.transform, "Win Screen");
            var win = winGo.GetComponent<WinScreen>();
            if (win == null) win = winGo.AddComponent<WinScreen>();
            win.timer = timer;
            win.fadeMaterial = timer.fadeMaterial;

            var ambGo = Child(gameRoot.transform, "Ambience");
            var amb = ambGo.GetComponent<AmbienceDirector>();
            if (amb == null) amb = ambGo.AddComponent<AmbienceDirector>();
            // Levels measured in the mix: wind ~-45 dBFS, creaks and owl ~-37 to -40, a door creaking nearby ~-30.
            // Kept low: a bed under the room, not a feature.
            amb.wind = SoundCue.Of(Clip("wind_strong"));
            amb.windVolume = 0.15f;
            var creak = Clip("creak_floor");
            // single squeaks out of the floor recording, slowed down so they sound like heavy timber
            float[,] takes = { { 0.3f, 0.6f }, { 1.6f, 0.9f }, { 3.4f, 0.8f }, { 5.4f, 0.7f }, { 7.4f, 0.7f }, { 11.0f, 1.3f }, { 12.4f, 0.9f } };
            amb.creaks = new SoundCue[takes.GetLength(0)];
            for (int i = 0; i < amb.creaks.Length; i++)
                amb.creaks[i] = SoundCue.Of(creak, takes[i, 0], takes[i, 1], 0.6f, 0.6f, 0.06f);
            amb.owl = SoundCue.Of(Clip("owl_tawny"), 0f, 7f, 0.2f);   // measured: peaks ~10 dB under a nearby door creak

            EditorSceneManager.MarkSceneDirty(scene);
        }

        static Material FadeMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(FadeMatPath);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_ScreenFade" };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            m.renderQueue = 3999;
            AssetDatabase.CreateAsset(m, FadeMatPath);
            return m;
        }

        static GameObject Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
