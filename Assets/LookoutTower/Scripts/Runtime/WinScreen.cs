using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LookoutTower
{
    /// <summary>
    /// When the player climbs down through the unlocked hatch, the view fades to black and a panel appears ~1.3 m in
    /// front of them: "You escaped!", the time taken, and a Play Again button that restarts the game.
    ///
    /// Once black, the camera draws only the panel's layer, so walls on the ladder can't hide it. The button is an
    /// <see cref="IInteractable"/> (controller pointer + trigger, or look + E), and any trigger, E, Enter, Space or mouse
    /// click also plays again, so nobody has to aim from a cramped ladder.
    /// </summary>
    public class WinScreen : MonoBehaviour
    {
        public EscapeTimer timer;
        [Tooltip("Height of the cab floor. Empty/0 = the tower's CabCentre marker.")]
        public float floorHeight;
        [Tooltip("How far below the cab floor the player's head must go (down the hatch) to count as escaped.")]
        public float dropBelowFloor = 0.4f;
        [Tooltip("Unlit transparent material (M_ScreenFade) for the fade and the button.")]
        public Material fadeMaterial;
        public float fadeSeconds = 1.5f;
        public float panelDistance = 1.3f;
        [Tooltip("Ignore input for this long after the panel appears, so a held trigger doesn't skip it.")]
        public float inputGrace = 1f;

        public string title = "You escaped!";
        public string buttonLabel = "Play again";
        public string hint = "Pull a trigger, press E or click";

        public bool IsShowing { get; private set; }
        /// <summary>The win screen on show right now, if any (pointers aim only at its panel while it's up).</summary>
        public static WinScreen Showing { get; private set; }
        /// <summary>Layer the panel is on; the camera draws only this layer while it shows.</summary>
        public int PanelLayer { get; private set; } = 5;

        const string PanelLayerName = "UI";
        InputAction _confirm;
        float _acceptInputAt = float.MaxValue;
        Material _buttonMat;
        bool _restarting;

        void Start()
        {
            if (timer == null) timer = FindAnyObjectByType<EscapeTimer>();
            if (floorHeight <= 0f)
            {
                var m = GameObject.Find("CabCentre");
                floorHeight = m != null ? m.transform.position.y : 30.5f;
            }
        }

        void OnDestroy()
        {
            _confirm?.Dispose();
            if (Showing == this) Showing = null;
        }

        void Update()
        {
            if (!IsShowing)
            {
                var cam = Camera.main;
                if (timer != null && timer.HasEscaped && cam != null && cam.transform.position.y < floorHeight - dropBelowFloor)
                    StartCoroutine(Show(cam));
                return;
            }
            if (_buttonMat != null)
            {
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 3f);
                _buttonMat.SetColor("_BaseColor", new Color(0.16f * pulse, 0.5f * pulse, 0.26f * pulse, 1f));
            }
            if (Time.unscaledTime >= _acceptInputAt && _confirm != null && _confirm.WasPressedThisFrame()) PlayAgain();
        }

        IEnumerator Show(Camera cam)
        {
            IsShowing = true;
            var head = cam.transform;

            // the player stops moving; the controller pointer stays on so it can press the button
            foreach (var d in FindObjectsByType<DesktopExplorer>())
            {
                if (d.flashlight != null) d.flashlight.enabled = false;
                d.enabled = false;
            }

            var fadeMat = ViewFader.Create(head, fadeMaterial, out var fadeRoot);
            for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
            {
                ViewFader.SetAlpha(fadeMat, t / fadeSeconds);
                yield return null;
            }
            ViewFader.SetAlpha(fadeMat, 1f);

            // from here on the camera draws only the panel
            int layer = LayerMask.NameToLayer(PanelLayerName);
            if (layer < 0) layer = 5;
            PanelLayer = layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << layer;
            Destroy(fadeRoot);
            BuildPanel(head, layer);
            Showing = this;

            _confirm = new InputAction("WinConfirm", InputActionType.Button);
            _confirm.AddBinding("<XRController>{LeftHand}/triggerPressed");
            _confirm.AddBinding("<XRController>{RightHand}/triggerPressed");
            _confirm.AddBinding("<Keyboard>/e");
            _confirm.AddBinding("<Keyboard>/enter");
            _confirm.AddBinding("<Keyboard>/space");
            _confirm.AddBinding("<Mouse>/leftButton");
            _confirm.Enable();
            _acceptInputAt = Time.unscaledTime + inputGrace;
        }

        void BuildPanel(Transform head, int layer)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);   // looking straight down
            fwd.Normalize();
            var panel = new GameObject("Win Panel").transform;
            panel.SetPositionAndRotation(head.position + fwd * panelDistance, Quaternion.LookRotation(fwd, Vector3.up));

            float used = timer != null ? timer.durationSeconds - timer.TimeLeft : 0f;
            Text(panel, "Title", title, new Vector3(0f, 0.2f, 0f), 1.1f, new Color(0.55f, 0.95f, 0.6f), FontStyles.Bold, layer);
            Text(panel, "Time", $"Time: {EscapeTimer.Format(used)}", new Vector3(0f, 0.06f, 0f), 0.6f, new Color(0.9f, 0.9f, 0.86f), FontStyles.Normal, layer);

            var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = "Play Again Button";
            button.layer = layer;
            button.transform.SetParent(panel, false);
            button.transform.localPosition = new Vector3(0f, -0.13f, 0f);
            button.transform.localScale = new Vector3(0.5f, 0.14f, 0.03f);
            _buttonMat = ViewFader.MakeMaterial(fadeMaterial, new Color(0.16f, 0.5f, 0.26f, 1f));
            _buttonMat.renderQueue = 2999;                                     // before the label text, not over it
            var mr = button.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _buttonMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            button.AddComponent<WinButton>().screen = this;
            Text(panel, "Label", buttonLabel, new Vector3(0f, -0.13f, -0.02f), 0.55f, Color.white, FontStyles.Bold, layer);

            Text(panel, "Hint", hint, new Vector3(0f, -0.31f, 0f), 0.42f, new Color(0.65f, 0.65f, 0.62f), FontStyles.Italic, layer);
        }

        static void Text(Transform parent, string name, string s, Vector3 pos, float size, Color colour, FontStyles style, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = new Vector2(1.2f, 0.2f);
            t.text = s;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = colour;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
        }

        /// <summary>Restarts the game with a full clock.</summary>
        public void PlayAgain()
        {
            if (_restarting) return;
            _restarting = true;
            EscapeTimer.RestartGame();
        }

        [ContextMenu("Debug: Show Win Screen")]
        void DebugShow()
        {
            if (!IsShowing && Camera.main != null) StartCoroutine(Show(Camera.main));
        }
    }
}
