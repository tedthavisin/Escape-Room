using TMPro;
using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Shows the <see cref="EscapeTimer"/>. In VR it's a small watch face on the left wrist (attached to the left
    /// controller once the XR rig is up); on desktop, or until a wrist is found, it's a readout in the screen corner.
    /// White normally, amber under 3 minutes, pulsing red in the last minute, green once the player has escaped.
    /// </summary>
    public class WristTimer : MonoBehaviour
    {
        public EscapeTimer timer;
        [Tooltip("Left controller or hand. Empty = looked for by name (XRInteractBridge's left controller, \"Left " +
                 "Controller\", \"LeftHand Controller\", ...) every second until one turns up.")]
        public Transform wristAnchor;
        [Tooltip("Watch position on the anchor: a little up and back from the controller, toward the wrist.")]
        public Vector3 watchOffset = new Vector3(0f, 0.035f, -0.09f);
        [Tooltip("Watch face rotation on the anchor; the default faces up out of the back of the wrist.")]
        public Vector3 watchEuler = new Vector3(90f, 0f, 0f);
        public bool showOnScreenWithoutWrist = true;
        public float amberBelowSeconds = 180f;

        static readonly string[] AnchorNames =
            { "Left Controller", "LeftHand Controller", "Left Hand", "LeftHand", "LeftControllerAnchor", "LeftHandAnchor" };

        TextMeshPro _watchText;
        Transform _watch;
        float _nextSearch;
        GUIStyle _style, _shadow;
        WinScreen _win;

        void Start()
        {
            if (timer == null) timer = FindAnyObjectByType<EscapeTimer>();
            _win = FindAnyObjectByType<WinScreen>();
        }

        void Update()
        {
            if (timer == null) return;
            if (wristAnchor == null && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                wristAnchor = FindWrist();
            }
            if (wristAnchor != null && _watch == null) BuildWatch();
            if (_watchText != null)
            {
                _watchText.text = EscapeTimer.Format(timer.TimeLeft);
                _watchText.color = CurrentColour();
            }
        }

        Transform FindWrist()
        {
            var bridge = FindAnyObjectByType<XRInteractBridge>();
            if (bridge != null && bridge.leftController != null && bridge.leftController.gameObject.activeInHierarchy)
                return bridge.leftController;
            foreach (var n in AnchorNames)
            {
                var go = GameObject.Find(n);
                if (go != null) return go.transform;
            }
            return null;
        }

        void BuildWatch()
        {
            _watch = new GameObject("Wrist Timer").transform;
            _watch.SetParent(wristAnchor, false);
            _watch.localPosition = watchOffset;
            _watch.localRotation = Quaternion.Euler(watchEuler);

            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(face.GetComponent<Collider>());
            face.name = "Face";
            face.transform.SetParent(_watch, false);
            face.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            face.transform.localScale = new Vector3(0.05f, 0.026f, 0.006f);
            var mr = face.GetComponent<MeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f));
            mr.SetPropertyBlock(mpb);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var tgo = new GameObject("Time");
            tgo.transform.SetParent(_watch, false);
            tgo.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            _watchText = tgo.AddComponent<TextMeshPro>();
            _watchText.rectTransform.sizeDelta = new Vector2(0.048f, 0.024f);
            _watchText.fontSize = 0.16f;
            _watchText.fontStyle = FontStyles.Bold;
            _watchText.alignment = TextAlignmentOptions.Center;
            _watchText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        Color CurrentColour()
        {
            if (timer.HasEscaped) return new Color(0.45f, 0.95f, 0.5f);
            if (timer.TimeLeft <= timer.lastMinuteSeconds)
            {
                float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI));
                return new Color(1f, 0.22f, 0.18f) * pulse + Color.black * (1f - pulse);
            }
            if (timer.TimeLeft <= amberBelowSeconds) return new Color(1f, 0.72f, 0.25f);
            return new Color(0.92f, 0.92f, 0.88f);
        }

        void OnGUI()
        {
            if (timer == null || !showOnScreenWithoutWrist || _watch != null || timer.HasExpired) return;
            if (_win != null && _win.IsShowing) return;          // the win screen shows the time itself
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
                _shadow = new GUIStyle(_style);
                _shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            }
            _style.normal.textColor = CurrentColour();
            var r = new Rect(Screen.width - 220f, 16f, 200f, 40f);
            string s = EscapeTimer.Format(timer.TimeLeft);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, _shadow);
            GUI.Label(r, s, _style);
        }
    }
}
