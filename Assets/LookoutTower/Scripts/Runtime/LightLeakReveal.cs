using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// The fuse box's reward: the ranger punched the last digit of the hatch code ("_ _ _ 9") as pinholes in the top of
    /// the pendant lamp's shade. When the panel is solved the power surges in: the lamp dips, swells, the pendant gets
    /// a jolt and swings, and the pinhole pattern of light appears on the ceiling. The pattern fades in with the surge,
    /// slides about as the pendant swings (it is re-projected from the bulb every frame) and always follows the lamp's
    /// own brightness, so it flickers and goes dark with it.
    ///
    /// The light pattern is an additive decal on the ceiling rather than a cookie light, so it costs nothing and shows
    /// the same on Quest (Forward, 4 lights per object) as on PC. Wired to <c>FuseBoxPanel.onSolved</c> by
    /// FuseBoxPuzzleBuilder; the texture is generated there too (LightLeakDecal), so the digit is a constant in the builder.
    /// </summary>
    public class LightLeakReveal : MonoBehaviour
    {
        [Tooltip("The lamp whose light leaks. The projection follows its brightness (flicker, power cut).")]
        public Light lamp;
        [Tooltip("The light pattern on the ceiling (an additive quad). Hidden until the reveal.")]
        public Renderer leak;
        [Tooltip("Colour of the leaked light at full strength (values above 1 feed the bloom).")]
        public Color leakColor = new Color(1.5f, 1.15f, 0.72f);
        [Tooltip("What swings when the power surges in (this pendant, by default). Leave empty for no swing.")]
        public Transform pendant;
        [Tooltip("Where the pendant hangs from, in the pendant's parent space (the bottom of the ceiling box).")]
        public Vector3 swingPivot;
        [Tooltip("Horizontal swing axis, in the pendant's parent space.")]
        public Vector3 swingAxis = new Vector3(1f, 0f, 0.35f);

        [Header("Pattern (drawn at start; untick to use the baked texture)")]
        public bool drawPattern = true;
        [Tooltip("What the pinholes spell, one dot-matrix character each ('_' is a dash).")]
        public string code = "___9";
        [Tooltip("Hole spacing on the ceiling, metres. 0.04 makes each character ~28 cm tall.")]
        public float pitch = 0.04f;
        [Tooltip("Size of the patch of ceiling the pattern covers (the shade's shadow pool), metres.")]
        public Vector2 size = new Vector2(1.5f, 0.62f);
        [Tooltip("Where the patch is centred from the lamp (x, z metres): west of the lights conduit so it can't cross the digits.")]
        public Vector2 offsetFromLamp = new Vector2(-0.25f, -0.45f);
        [Tooltip("World directions the code reads left-to-right and bottom-to-top for someone under it facing north (turned round from the fuse box).")]
        public Vector3 readRight = Vector3.right, readUp = Vector3.back;
        [Tooltip("How dark the shade's shadow around the dots is. Gives them contrast where the lamp lights the ceiling (no lamp shadows on Quest).")]
        [Range(0f, 1f)] public float shadeShadow = 0.8f;

        [Header("Timing")]
        [Tooltip("Seconds after the solve before the surge, so the lamp's own power-up flicker plays first.")]
        public float delay = 0.75f;
        [Tooltip("Seconds for the projection to come up to full.")]
        public float fadeIn = 1.8f;
        [Tooltip("Initial swing, degrees. Settles on its own.")]
        public float swingDegrees = 5f;
        [Tooltip("Seconds for the swing to die down to about a third.")]
        public float swingDecay = 2.6f;
        [Tooltip("Pendulum length in metres (sets the swing's period).")]
        public float pendulumLength = 0.48f;

        [Tooltip("Shown a few seconds after the reveal in case nobody looks up. Empty = no hint.")]
        public string hint = "Light is leaking through pinholes in the lamp shade onto the ceiling.";
        public float hintAfter = 4f;

        public UnityEvent onRevealed = new UnityEvent();

        public bool IsRevealed { get; private set; }

        float _t0 = -1f, _lampBase;
        Vector3 _restPos, _leakDirLocal;
        float _leakPlaneY;
        MaterialPropertyBlock _mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        Quaternion _restRot;
        bool _surged, _lampRestored, _swingDone, _hinted;

        void Awake()
        {
            if (pendant == null) pendant = transform;
            if (lamp != null) _lampBase = lamp.intensity;
            if (leak != null)
            {
                leak.enabled = false;
                PrepareForQuest(leak);
                if (drawPattern) DrawPattern();
                _mpb = new MaterialPropertyBlock();
                _leakPlaneY = leak.transform.position.y;
                if (lamp != null) _leakDirLocal = lamp.transform.InverseTransformDirection(leak.transform.position - lamp.transform.position);
            }
            _restPos = pendant.localPosition;
            _restRot = pendant.localRotation;
        }

        /// <summary>Start the reveal (wired to FuseBoxPanel.onSolved). Only the first call does anything.</summary>
        public void Reveal()
        {
            if (IsRevealed) return;
            IsRevealed = true;
            _t0 = Time.time;
        }

        [ContextMenu("Reveal now (play mode)")]
        void RevealNow()
        {
            if (Application.isPlaying) { Reveal(); _t0 = Time.time - delay; }
        }

        void Update()
        {
            if (!IsRevealed) return;
            float t = Time.time - _t0 - delay;
            if (t < 0f) return;
            bool lampOn = lamp == null || (lamp.enabled && lamp.isActiveAndEnabled);

            if (!_surged)
            {
                _surged = true;
                PuzzleSfx.Play(PuzzleSfx.Surge, transform.position, 0.8f);
                onRevealed.Invoke();
            }

            // surge on the lamp itself: a brown-out dip as the other circuits take load, a swell, then settle
            if (lamp != null && !_lampRestored)
            {
                if (t < 1.6f) { if (lampOn) lamp.intensity = _lampBase * Surge(t); }
                else { _lampRestored = true; if (lampOn) lamp.intensity = _lampBase; }
            }

            // the projection: fades in behind the surge, rides the lamp's brightness, filament shimmer while warming
            if (leak != null)
            {
                float fade = Mathf.Clamp01((t - 0.15f) / Mathf.Max(0.01f, fadeIn));
                fade = fade * fade * (3f - 2f * fade);
                float lampK = lamp != null && _lampBase > 0f ? lamp.intensity / _lampBase : 1f;
                float warm = 1f - (1f - fade) * 0.45f * Mathf.PerlinNoise(Time.time * 23f, 3.1f);
                float k = fade * lampK * warm;
                leak.enabled = lampOn && k > 0.001f;
                if (leak.enabled)
                {
                    // alpha-blended pattern: fade the whole thing (shadow and dots); additive baked one: scale the light
                    _mpb.SetColor(BaseColorId, _blended ? new Color(leakColor.r, leakColor.g, leakColor.b, Mathf.Clamp01(k)) : leakColor * k);
                    leak.SetPropertyBlock(_mpb);
                }
            }

            // the jolt: a damped pendulum about the ceiling box, so the digits drift and settle
            if (pendant != null && !_swingDone && swingDegrees > 0f)
            {
                float w = Mathf.Sqrt(9.81f / Mathf.Max(0.05f, pendulumLength));
                float amp = swingDegrees * Mathf.Exp(-t / Mathf.Max(0.1f, swingDecay));
                if (amp < 0.03f)
                {
                    _swingDone = true;
                    pendant.localPosition = _restPos;
                    pendant.localRotation = _restRot;
                }
                else
                {
                    var q = Quaternion.AngleAxis(amp * Mathf.Sin(w * t), swingAxis.normalized);
                    pendant.localRotation = q * _restRot;
                    pendant.localPosition = swingPivot + q * (_restPos - swingPivot);
                }
                FollowLamp();
            }

            if (!_hinted && !string.IsNullOrEmpty(hint) && t >= hintAfter)
            {
                _hinted = true;
                PuzzleHints.Show(hint);
            }
        }

        bool _blended;
        Texture2D _patternTex;
        Material _patternMat;

        /// <summary>
        /// Resizes the decal and draws the pinhole code on it (<see cref="LightLeakPattern"/>): bigger dots than the baked
        /// texture, inside a dark pool for the shade's shadow, alpha-blended so the pool darkens the lamp-lit ceiling.
        /// </summary>
        void DrawPattern()
        {
            var t = leak.transform;
            if (lamp != null)
            {
                var p = lamp.transform.position;
                t.position = new Vector3(p.x + offsetFromLamp.x, t.position.y, p.z + offsetFromLamp.y);
            }
            Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(size.x / parentScale.x, size.y / parentScale.y, 1f);

            const int W = 512, H = 256;
            _patternTex = new Texture2D(W, H, TextureFormat.RGBA32, true, true)
            {
                name = "Light Leak Pattern",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,   // read at a slant from below
            };
            _patternTex.SetPixels32(LightLeakPattern.Render(t, readRight, readUp, code, pitch, W, H, shadeShadow));
            _patternTex.Apply(true, true);

            _patternMat = new Material(leak.sharedMaterial) { name = "M_LightLeak (pattern)" };
            _patternMat.SetTexture("_BaseMap", _patternTex);
            _patternMat.SetFloat("_Blend", 0f);   // alpha
            _patternMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _patternMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _patternMat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            _patternMat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            leak.sharedMaterial = _patternMat;
            _blended = true;
        }

        void OnDestroy()
        {
            if (_patternMat != null) Destroy(_patternMat);
            if (_patternTex != null) Destroy(_patternTex);
        }

        /// <summary>
        /// Makes the decal draw on the Quest as it does on PC. Its material (URP Particles/Unlit) multiplies the texture
        /// by the mesh's vertex colours, and the built-in quad has none: PC fills the missing colour in as white, but the
        /// Quest can read it as black, and black added to the ceiling is nothing. So swap in the same quad with white
        /// vertex colours. Also drop it to 8 mm under the ceiling (from 3 mm) so the headset's depth buffer can't
        /// hide it behind the ceiling boards.
        /// </summary>
        static void PrepareForQuest(Renderer leak)
        {
            var mf = leak.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.colors32.Length == 0)
            {
                var src = mf.sharedMesh;
                var mesh = new Mesh { name = src.name + " (white vertex colours)" };
                if (src.isReadable)
                {
                    mesh.vertices = src.vertices;
                    mesh.normals = src.normals;
                    mesh.uv = src.uv;
                    mesh.triangles = src.triangles;
                }
                else
                {
                    // Unity's built-in quad: faces -Z, UV (0,0) at the bottom left
                    mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                    mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                    mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                    mesh.triangles = new[] { 0, 3, 1, 3, 0, 2 };
                }
                var white = new Color32[mesh.vertexCount];
                for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
                mesh.colors32 = white;
                mesh.RecalculateBounds();
                mf.sharedMesh = mesh;
            }
            leak.transform.position += Vector3.down * 0.005f;
        }

        // re-project the pattern from the (swinging) bulb onto the ceiling plane
        void FollowLamp()
        {
            if (leak == null || lamp == null) return;
            var from = lamp.transform.position;
            var dir = lamp.transform.TransformDirection(_leakDirLocal);
            if (Mathf.Abs(dir.y) < 1e-4f) return;
            var p = from + dir * ((_leakPlaneY - from.y) / dir.y);
            leak.transform.position = p;
        }

        // 1 = normal. Dip to ~35% for a beat, swell past full, ease back.
        static float Surge(float t)
        {
            if (t < 0.12f) return Mathf.Lerp(1f, 0.35f, t / 0.12f);
            if (t < 0.25f) return 0.35f;
            if (t < 0.7f) return Mathf.Lerp(0.35f, 1.45f, Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.45f));
            return Mathf.Lerp(1.45f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.9f));
        }
    }
}
