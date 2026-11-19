using UnityEngine;
using UnityEngine.SceneManagement;

namespace LookoutTower
{
    /// <summary>
    /// Easter egg: a little stone gorilla perched on the catwalk railing outside the cab door. Take it (E on the
    /// desktop, trigger in VR) and the player gets Gorilla-Tag-style arm movement (<see cref="GorillaMode"/>).
    /// It isn't part of any puzzle and nothing else listens to it.
    ///
    /// If the scene with the tower doesn't already have one (placed by the editor tool), one is built at Play from
    /// primitives and the scene's own materials, so it's always there without editing the scene.
    /// </summary>
    public class GorillaStatue : MonoBehaviour, IInteractable
    {
        public const string GroupName = "Easter Eggs";
        public const string StatueName = "Gorilla Statue";
        const float FloorY = 30.5f;   // cab floor / catwalk (LookoutTowerBuilder.FloorY)
        // tower-local: rail post at x = 1.836 on the south rail (z = -4.59); the rail's cap tops out 1.10 m above the deck
        static readonly Vector3 LocalPos = new Vector3(1.836f, FloorY + 1.10f, -4.59f);
        static readonly Vector3 LookAtLocal = new Vector3(0f, 0f, -3.2f);   // faces the doorway

        public string prompt = "Take the gorilla statue";
        [Tooltip("Material for the desktop player's two gorilla fists.")]
        public Material handMaterial;

        [Header("Leash (keeps the gorilla on the cab and catwalk so it can't skip the escape room)")]
        [Tooltip("World centre of the tower (xz used).")]
        public Vector3 leashCentre;
        [Tooltip("Half width of the square the body's centre must stay inside while above leashMinY.")]
        public float leashHalfExtent = 4.3f;
        [Tooltip("The leash only applies above this height (world y): the cab and catwalk, not the stairs below.")]
        public float leashMinY = 29f;

        public bool Collected { get; private set; }
        public string Prompt => prompt;

        public void Interact()
        {
            if (Collected) return;
            if (GorillaMode.Grant(this) == null) return;
            Collected = true;
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ spawning
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) EnsureInScene(SceneManager.GetSceneAt(i));
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInScene(scene);

        static void EnsureInScene(Scene scene)
        {
            if (!scene.isLoaded) return;
            Transform tower = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<GorillaStatue>(true) != null) return;   // already placed
                if (tower == null && FindDeep(root.transform, "CabCentre") != null) tower = root.transform;
            }
            if (tower == null) return;   // not the lookout scene
            var go = Create(tower, FindMat("M_Concrete"), FindMat("M_Rock"), FindMat("M_DialGlow"), FindMat("M_PlasticBlack"));
            SceneManager.MoveGameObjectToScene(go.transform.root.gameObject, scene);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static Material FindMat(string name)
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name == name) return m;
            return null;
        }

        /// <summary>Builds the statue (under an "Easter Eggs" group) on the rail outside the door of <paramref name="tower"/>.</summary>
        public static GameObject Create(Transform tower, Material stone, Material plinth, Material eyes, Material fist)
        {
            var groupGo = GameObject.Find("/" + GroupName);
            if (groupGo == null) groupGo = new GameObject(GroupName);
            if (groupGo.scene != tower.gameObject.scene) SceneManager.MoveGameObjectToScene(groupGo, tower.gameObject.scene);
            var old = groupGo.transform.Find(StatueName);
            if (old != null) DestroyNow(old.gameObject);

            var root = new GameObject(StatueName);
            root.transform.SetParent(groupGo.transform, false);
            Vector3 pos = tower.TransformPoint(LocalPos);
            Vector3 look = Vector3.ProjectOnPlane(tower.TransformPoint(LookAtLocal) - pos, Vector3.up);
            root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));

            BuildGorilla(root.transform, stone, plinth != null ? plinth : stone, eyes);

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.26f, 0.03f);
            col.size = new Vector3(0.36f, 0.52f, 0.32f);

            var statue = root.AddComponent<GorillaStatue>();
            statue.handMaterial = fist;
            statue.leashCentre = tower.position;
            statue.leashHalfExtent = 4.3f;   // body centre stays inside the catwalk rail (4.55 m out)
            statue.leashMinY = tower.position.y + FloorY - 1.5f;
            return root;
        }

        /// <summary>Knuckle-standing gorilla, ~0.5 m tall on a small plinth, facing +Z. Pivot on the plinth's underside.</summary>
        static void BuildGorilla(Transform root, Material stone, Material plinthMat, Material eyes)
        {
            Part(root, PrimitiveType.Cube, "Plinth", new Vector3(0f, 0.03f, 0f), Vector3.zero, new Vector3(0.26f, 0.06f, 0.26f), plinthMat);
            const float B = 0.06f;   // top of the plinth
            var body = new GameObject("Gorilla").transform;
            body.SetParent(root, false);

            // haunches, legs, feet
            Part(body, PrimitiveType.Sphere, "Hips", new Vector3(0f, B + 0.09f, -0.05f), Vector3.zero, new Vector3(0.2f, 0.15f, 0.17f), stone);
            for (int s = -1; s <= 1; s += 2)
            {
                Part(body, PrimitiveType.Sphere, "Thigh", new Vector3(s * 0.08f, B + 0.065f, 0f), new Vector3(0f, s * 10f, 0f), new Vector3(0.09f, 0.11f, 0.15f), stone);
                Part(body, PrimitiveType.Sphere, "Foot", new Vector3(s * 0.09f, B + 0.02f, 0.05f), Vector3.zero, new Vector3(0.07f, 0.04f, 0.11f), stone);
            }

            // torso leaning forward, shoulder hump, chest
            Part(body, PrimitiveType.Sphere, "Torso", new Vector3(0f, B + 0.22f, 0f), new Vector3(18f, 0f, 0f), new Vector3(0.24f, 0.28f, 0.2f), stone);
            Part(body, PrimitiveType.Sphere, "Shoulders", new Vector3(0f, B + 0.32f, 0.02f), Vector3.zero, new Vector3(0.29f, 0.12f, 0.17f), stone);
            Part(body, PrimitiveType.Sphere, "Chest", new Vector3(0f, B + 0.26f, 0.085f), Vector3.zero, new Vector3(0.17f, 0.13f, 0.08f), stone);

            // head: skull, crest, brow ridge, muzzle, nostrils, glinting eyes, ears
            Part(body, PrimitiveType.Sphere, "Head", new Vector3(0f, B + 0.40f, 0.07f), Vector3.zero, new Vector3(0.13f, 0.14f, 0.13f), stone);
            Part(body, PrimitiveType.Sphere, "Crest", new Vector3(0f, B + 0.465f, 0.05f), Vector3.zero, new Vector3(0.07f, 0.08f, 0.09f), stone);
            Part(body, PrimitiveType.Capsule, "Brow", new Vector3(0f, B + 0.425f, 0.125f), new Vector3(0f, 0f, 90f), new Vector3(0.035f, 0.065f, 0.04f), stone);
            Part(body, PrimitiveType.Sphere, "Muzzle", new Vector3(0f, B + 0.373f, 0.125f), Vector3.zero, new Vector3(0.095f, 0.065f, 0.07f), stone);
            for (int s = -1; s <= 1; s += 2)
            {
                Part(body, PrimitiveType.Sphere, "Nostril", new Vector3(s * 0.014f, B + 0.385f, 0.16f), Vector3.zero, Vector3.one * 0.014f, stone);
                Part(body, PrimitiveType.Sphere, "Eye", new Vector3(s * 0.028f, B + 0.408f, 0.132f), Vector3.zero, Vector3.one * 0.017f, eyes != null ? eyes : stone);
                Part(body, PrimitiveType.Sphere, "Ear", new Vector3(s * 0.066f, B + 0.405f, 0.06f), Vector3.zero, new Vector3(0.02f, 0.035f, 0.03f), stone);
            }

            // long arms down to the knuckles
            for (int s = -1; s <= 1; s += 2)
            {
                var shoulder = new Vector3(s * 0.13f, B + 0.32f, 0.03f);
                var elbow = new Vector3(s * 0.16f, B + 0.18f, 0.09f);
                var wrist = new Vector3(s * 0.15f, B + 0.055f, 0.12f);
                Limb(body, "Upper Arm", shoulder, elbow, 0.08f, stone);
                Limb(body, "Forearm", elbow, wrist, 0.072f, stone);
                Part(body, PrimitiveType.Sphere, "Fist", new Vector3(s * 0.15f, B + 0.03f, 0.13f), Vector3.zero, new Vector3(0.075f, 0.06f, 0.08f), stone);
            }
        }

        static void Limb(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material m)
        {
            Vector3 d = b - a;
            var t = Part(parent, PrimitiveType.Capsule, name, (a + b) * 0.5f, Vector3.zero,
                new Vector3(thickness, d.magnitude * 0.5f + thickness * 0.25f, thickness), m);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        }

        static Transform Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            DestroyNow(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            if (m != null) go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        static void DestroyNow(Object o)
        {
            if (o == null) return;
            if (!Application.isPlaying) { DestroyImmediate(o); return; }
            // Destroy() waits for the end of the frame: switch colliders off now so nothing hits them meanwhile
            if (o is Collider col) col.enabled = false;
            Destroy(o);
        }
    }
}
