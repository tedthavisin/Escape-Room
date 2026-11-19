using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Puts the gorilla statue easter egg (<see cref="GorillaStatue"/>) into the forest clearing scene so it can be
    /// seen and moved in the Editor. Optional: at Play the statue builds itself if the scene doesn't have one.
    /// Build Everything calls <see cref="AddToScene"/>.
    /// </summary>
    public static class GorillaEasterEgg
    {
        const string MatDir = LookoutTowerBuilder.Root + "/Materials";

        [MenuItem("Tools/Lookout Tower/Place Gorilla Statue (Easter Egg)", priority = 60)]
        static void PlaceMenu()
        {
            var scene = SceneManager.GetActiveScene();
            var tower = FindTower(scene);
            if (tower == null)
            {
                EditorUtility.DisplayDialog("Lookout Tower", "Open the forest clearing scene first (it needs the tower in it).", "OK");
                return;
            }
            Selection.activeGameObject = AddToScene(tower);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>For -executeMethod: opens the scene, places the statue and saves.</summary>
        public static void BatchPlace()
        {
            var scene = EditorSceneManager.OpenScene(LookoutTowerBuilder.ScenePath);
            var tower = FindTower(scene);
            if (tower == null) { Debug.LogError("[GorillaEasterEgg] Tower not found in the scene."); return; }
            var go = AddToScene(tower);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[GorillaEasterEgg] Placed {go.name} at {go.transform.position}");
        }

        static Transform FindTower(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LookoutTowerBuilder.TowerPrefabPath);
            var roots = scene.GetRootGameObjects();
            var g = roots.FirstOrDefault(r => prefab != null && PrefabUtility.GetCorrespondingObjectFromSource(r) == prefab)
                    ?? roots.FirstOrDefault(r => r.name == "LookoutTower");
            return g != null ? g.transform : null;
        }

        /// <summary>Adds (or replaces) the statue in the tower's scene. Returns the statue.</summary>
        public static GameObject AddToScene(Transform tower)
        {
            Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
            return GorillaStatue.Create(tower, M("M_Concrete"), M("M_Rock"), M("M_DialGlow"), M("M_PlasticBlack"));
        }
    }
}
