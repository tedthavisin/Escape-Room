using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// A window that fogs up where the player breathes on it, revealing part of the lock code someone once wrote on the
    /// glass with a fingertip (the finger left the glass clean, so the writing stays clear while the fog forms around it).
    ///
    /// Put it on a window sash. Lean in close to the glass from inside the cab, face it and breathe out: the mic
    /// (<see cref="BreathSensor"/>) says how hard, the head position says where. Fog builds up under the breath, lingers
    /// for a few seconds, then evaporates from the thin edges inwards.
    ///
    /// The glass only gives away some digits of the lock code: by default "__8_", the 3rd digit of 1789, with finger
    /// dashes for the others. For the lock: <see cref="CheckCode"/> says whether an attempt agrees with the digits
    /// written here; listen to <see cref="onRevealed"/> if something should happen the first time the player sees it.
    /// </summary>
    [DisallowMultipleComponent]
    public class BreathFogWindow : MonoBehaviour
    {
        [Header("Hidden code")]
        [Tooltip("What's written on the glass, one slot per character: a digit, '_' for a finger dash (a digit the " +
                 "player has to find elsewhere), or a space for an empty slot.")]
        public string code = "__8_";
        [Tooltip("Centre of the writing, in metres from the centre of the glass (x right, y up, seen from inside).")]
        public Vector2 writingCentre = new Vector2(0f, -0.24f);
        public float digitHeight = 0.11f;
        public float strokeWidth = 0.017f;
        [Tooltip("Extra space between the two halves of an even-length code so it doesn't run across the middle muntin.")]
        public float centreGap = 0.045f;

        [Header("Breath")]
        [Tooltip("The player's head. Empty = the main camera (desktop explorer or XR rig).")]
        public Transform head;
        [Tooltip("Mouth position relative to the head (eyes).")]
        public Vector3 mouthOffset = new Vector3(0f, -0.09f, 0.07f);
        [Tooltip("How far from the glass (metres) breath still reaches it.")]
        public float reach = 0.5f;
        [Tooltip("Fog build-up per second at full breath, right at the glass and at the edge of reach.")]
        public float buildRateNear = 1.6f, buildRateFar = 0.5f;
        [Tooltip("Radius of the breath patch (metres), right at the glass and at the edge of reach.")]
        public float spreadNear = 0.08f, spreadFar = 0.2f;

        [Header("Fog")]
        public Color fogColour = new Color(0.9f, 0.92f, 0.95f, 1f);
        [Range(0f, 1f)] public float maxOpacity = 0.75f;
        [Tooltip("How clear the writing stays when the glass around it is fogged.")]
        [Range(0f, 1f)] public float writingClarity = 0.92f;
        [Tooltip("Seconds the fog holds after the last breath before it starts to evaporate.")]
        public float lingerSeconds = 4f;
        [Tooltip("Seconds for fully fogged glass to clear.")]
        public float evaporateSeconds = 10f;
        [Tooltip("Fraction of the writing area that must be fogged for the code to count as visible.")]
        [Range(0.1f, 1f)] public float revealCoverage = 0.6f;
        public float texelsPerMetre = 220f;
        [Tooltip("Material the fog is made from. Empty = a copy of this window's glass material (a URP Lit transparent).")]
        public Material fogMaterialTemplate;

        [Header("Glass (Fit To Glass fills these in)")]
        public Vector3 glassCentre;
        public Vector2 glassSize;
        public Vector3 glassRight = Vector3.right;
        public Vector3 glassUp = Vector3.up;
        [Tooltip("Points from the glass into the cab, in this object's local space.")]
        public Vector3 interiorNormal = Vector3.back;
        public float surfaceOffset = 0.004f;

        [Header("Events")]
        [Tooltip("The first time the code becomes readable.")]
        public UnityEvent onRevealed = new UnityEvent();
        [Tooltip("Every time the code becomes readable (true) or fades away again (false).")]
        public UnityEvent<bool> onCodeVisibleChanged = new UnityEvent<bool>();

        public string Code => code;
        public bool HasBeenRevealed { get; private set; }
        public bool IsCodeVisible { get; private set; }
        /// <summary>How fogged the writing area is, 0..1.</summary>
        public float WritingCoverage { get; private set; }
        /// <summary>True while breath is landing on this glass.</summary>
        public bool IsBeingBreathedOn { get; private set; }

        /// <summary>True if <paramref name="attempt"/> has the same digits in the slots this window shows (blanks match anything).</summary>
        public bool CheckCode(string attempt)
        {
            if (attempt == null) return false;
            attempt = attempt.Trim();
            if (attempt.Length != code.Length) return false;
            for (int i = 0; i < code.Length; i++)
                if (char.IsDigit(code[i]) && attempt[i] != code[i]) return false;
            return true;
        }

        // simulation
        const float CellSize = 0.025f;
        int _gw, _gh, _tw, _th;
        float[] _density;
        float[] _mask, _grain;
        Rect _textRect;
        Texture2D _tex;
        Material _mat;
        MeshRenderer _overlay;
        Transform _overlayT;
        BreathSensor _sensor;
        float _lastBreath = -999f, _nextUpload;
        bool _dirty, _anyFog;

        void Start()
        {
            if (glassSize.x <= 0f || glassSize.y <= 0f) FitToGlass();
            if (glassSize.x <= 0f || glassSize.y <= 0f)
            {
                Debug.LogError($"[LookoutTower] {name}: BreathFogWindow couldn't find the glass. Run 'Fit To Glass' from the component menu.", this);
                enabled = false;
                return;
            }
            _sensor = BreathSensor.GetOrCreate();
            BuildOverlay();
            BuildMasks();
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
            if (_mat != null) Destroy(_mat);
            if (_overlay != null) Destroy(_overlay.GetComponent<MeshFilter>().sharedMesh);
        }

        // ------------------------------------------------------------------ setup
        void BuildOverlay()
        {
            var go = new GameObject("Breath Fog");
            go.layer = gameObject.layer;
            _overlayT = go.transform;
            _overlayT.SetParent(transform, false);
            Vector3 n = interiorNormal.normalized;
            _overlayT.localPosition = glassCentre + n * surfaceOffset;
            // local X = glass right, Y = glass up, Z = out through the glass (so -Z faces the cab)
            _overlayT.localRotation = Quaternion.LookRotation(-n, glassUp);

            float hw = glassSize.x * 0.5f, hh = glassSize.y * 0.5f;
            var mesh = new Mesh { name = "BreathFogQuad" };
            mesh.vertices = new[] { new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f), new Vector3(hw, hh, 0f), new Vector3(-hw, hh, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            _tw = Mathf.Max(8, Mathf.CeilToInt(glassSize.x * texelsPerMetre));
            _th = Mathf.Max(8, Mathf.CeilToInt(glassSize.y * texelsPerMetre));
            _tex = new Texture2D(_tw, _th, TextureFormat.RGBA32, false, false)
            {
                name = "BreathFog", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var px = _tex.GetPixelData<Color32>(0);
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
            _tex.Apply(false, false);

            _mat = new Material(FindTemplate()) { name = "BreathFog (runtime)" };
            _mat.SetTexture("_BaseMap", _tex);
            _mat.SetTextureScale("_BaseMap", Vector2.one);
            _mat.SetTextureOffset("_BaseMap", Vector2.zero);
            if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", _tex);
            _mat.SetColor("_BaseColor", fogColour);
            if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0.3f);
            if (_mat.HasProperty("_Cull")) _mat.SetFloat("_Cull", 0f);   // visible through the glass from outside too
            _mat.renderQueue = 3001;                                      // after the glass itself

            _overlay = go.AddComponent<MeshRenderer>();
            _overlay.sharedMaterial = _mat;
            _overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _overlay.receiveShadows = false;
            _overlay.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            _overlay.enabled = false;
        }

        Material FindTemplate()
        {
            if (fogMaterialTemplate != null) return fogMaterialTemplate;
            var r = GetComponent<Renderer>();
            if (r != null)
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.ToLowerInvariant().Contains("glass")) return m;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(lit);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }

        void BuildMasks()
        {
            _gw = Mathf.Max(2, Mathf.CeilToInt(glassSize.x / CellSize) + 1);
            _gh = Mathf.Max(2, Mathf.CeilToInt(glassSize.y / CellSize) + 1);
            _density = new float[_gw * _gh];

            _mask = new float[_tw * _th];
            Vector2 centreFromCorner = writingCentre + glassSize * 0.5f;
            _textRect = FingerWriting.Draw(_mask, _tw, _th, glassSize, code, centreFromCorner, digitHeight, strokeWidth,
                                           centreGap, StableHash(code));

            // Condensation isn't even: fine droplet grain plus broader blotches.
            _grain = new float[_tw * _th];
            var rng = new System.Random(StableHash(name + code));
            float ox = (float)rng.NextDouble() * 500f, oy = (float)rng.NextDouble() * 500f;
            for (int y = 0; y < _th; y++)
            for (int x = 0; x < _tw; x++)
            {
                float mx = x / texelsPerMetre, my = y / texelsPerMetre;
                float fine = Mathf.PerlinNoise(ox + mx * 90f, oy + my * 90f);
                float blot = Mathf.PerlinNoise(oy + mx * 14f, ox + my * 14f);
                _grain[y * _tw + x] = 0.35f * fine + 0.65f * blot;
            }
        }

        // ------------------------------------------------------------------ simulation
        void Update()
        {
            float dt = Time.deltaTime;
            IsBeingBreathedOn = false;

            if (_sensor != null && _sensor.Intensity > 0.01f && TryGetBreathPoint(out Vector2 hit, out float dist))
            {
                float k = Mathf.Clamp01(dist / reach);
                float rate = Mathf.Lerp(buildRateNear, buildRateFar, k) * _sensor.Intensity;
                float sigma = Mathf.Lerp(spreadNear, spreadFar, k);
                float inv2s2 = 1f / (2f * sigma * sigma);
                float cutoff2 = 9f * sigma * sigma;
                for (int j = 0; j < _gh; j++)
                {
                    float cy = CellY(j) - hit.y;
                    for (int i = 0; i < _gw; i++)
                    {
                        float cx = CellX(i) - hit.x;
                        float r2 = cx * cx + cy * cy;
                        if (r2 > cutoff2) continue;
                        int c = j * _gw + i;
                        _density[c] += rate * Mathf.Exp(-r2 * inv2s2) * dt * (1.05f - _density[c]);
                        if (_density[c] > 1f) _density[c] = 1f;
                    }
                }
                _lastBreath = Time.time;
                IsBeingBreathedOn = true;
                _dirty = _anyFog = true;
            }

            if (_anyFog && Time.time - _lastBreath > lingerSeconds)
            {
                // Thin fog goes first, so the patch shrinks from its edges like real condensation.
                bool any = false;
                float step = dt / Mathf.Max(0.1f, evaporateSeconds);
                for (int c = 0; c < _density.Length; c++)
                {
                    float d = _density[c];
                    if (d <= 0f) continue;
                    d -= step * Mathf.Lerp(1.8f, 0.7f, d);
                    _density[c] = d > 0f ? d : 0f;
                    any |= d > 0f;
                }
                _anyFog = any;
                _dirty = true;
            }

            if (_dirty) UpdateCoverage();
            if (_dirty && Time.unscaledTime >= _nextUpload)
            {
                _nextUpload = Time.unscaledTime + 1f / 30f;
                _dirty = false;
                UploadTexture();
            }
        }

        float CellX(int i) => -glassSize.x * 0.5f + i * glassSize.x / (_gw - 1);
        float CellY(int j) => -glassSize.y * 0.5f + j * glassSize.y / (_gh - 1);

        /// <summary>Where the breath lands on the glass (metres from its centre) and how far it travelled.</summary>
        bool TryGetBreathPoint(out Vector2 hit, out float dist)
        {
            hit = default;
            dist = 0f;
            var h = head;
            if (h == null)
            {
                var cam = Camera.main;
                if (cam == null) return false;
                h = cam.transform;
            }
            Vector3 mouth = _overlayT.InverseTransformPoint(h.TransformPoint(mouthOffset));
            float front = -mouth.z;                                     // distance in front of the glass, cab side
            if (front < -0.02f || front > reach) return false;
            Vector3 dir = _overlayT.InverseTransformDirection(h.forward);
            if (dir.z < 0.3f) return false;                             // not facing the glass
            // Breath goes where the face points, but spreads, so don't let a glancing angle push it far sideways.
            Vector2 foot = new Vector2(mouth.x, mouth.y);
            Vector2 aim = foot + new Vector2(dir.x, dir.y) * (Mathf.Max(front, 0f) / dir.z);
            hit = foot + Vector2.ClampMagnitude(aim - foot, 0.2f);
            dist = Mathf.Sqrt(Mathf.Max(front, 0f) * Mathf.Max(front, 0f) + (hit - foot).sqrMagnitude);
            float margin = 0.1f;
            return dist <= reach && Mathf.Abs(hit.x) <= glassSize.x * 0.5f + margin && Mathf.Abs(hit.y) <= glassSize.y * 0.5f + margin;
        }

        void UpdateCoverage()
        {
            // average fog over the cells under the writing
            float sum = 0f;
            int n = 0;
            for (int j = 0; j < _gh; j++)
            {
                float y = CellY(j) + glassSize.y * 0.5f;
                if (y < _textRect.yMin || y > _textRect.yMax) continue;
                for (int i = 0; i < _gw; i++)
                {
                    float x = CellX(i) + glassSize.x * 0.5f;
                    if (x < _textRect.xMin || x > _textRect.xMax) continue;
                    sum += Mathf.Clamp01((_density[j * _gw + i] - 0.15f) / 0.5f);
                    n++;
                }
            }
            WritingCoverage = n > 0 ? sum / n : 0f;

            bool visible = IsCodeVisible ? WritingCoverage > revealCoverage * 0.6f : WritingCoverage >= revealCoverage;
            if (visible == IsCodeVisible) return;
            IsCodeVisible = visible;
            onCodeVisibleChanged.Invoke(visible);
            if (visible && !HasBeenRevealed)
            {
                HasBeenRevealed = true;
                Debug.Log($"[LookoutTower] {name}: breath revealed the code on the glass.");
                onRevealed.Invoke();
            }
        }

        void UploadTexture()
        {
            var px = _tex.GetPixelData<Color32>(0);
            float gx = (_gw - 1f) / Mathf.Max(1, _tw - 1), gy = (_gh - 1f) / Mathf.Max(1, _th - 1);
            float maxA = maxOpacity * 255f;
            bool any = false;
            for (int y = 0; y < _th; y++)
            {
                float fy = y * gy;
                int j0 = Mathf.Min((int)fy, _gh - 2);
                float ty = fy - j0;
                int row0 = j0 * _gw, row1 = row0 + _gw;
                for (int x = 0; x < _tw; x++)
                {
                    float fx = x * gx;
                    int i0 = Mathf.Min((int)fx, _gw - 2);
                    float tx = fx - i0;
                    float d0 = _density[row0 + i0] + (_density[row0 + i0 + 1] - _density[row0 + i0]) * tx;
                    float d1 = _density[row1 + i0] + (_density[row1 + i0 + 1] - _density[row1 + i0]) * tx;
                    float d = d0 + (d1 - d0) * ty;
                    int p = y * _tw + x;
                    byte a = 0;
                    if (d > 0.01f)
                    {
                        float g = _grain[p];
                        float t = d + (g - 0.5f) * 0.45f;                      // ragged, blotchy edge
                        float f = Mathf.Clamp01((t - 0.08f) / 0.45f);
                        f = f * f * (3f - 2f * f);
                        f *= 0.8f + 0.2f * g;
                        f *= 1f - _mask[p] * writingClarity;
                        a = (byte)(f * maxA);
                        any |= a > 0;
                    }
                    var c = px[p];
                    c.a = a;
                    px[p] = c;
                }
            }
            _tex.Apply(false, false);
            _overlay.enabled = any;
        }

        // ------------------------------------------------------------------ helpers
        static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char ch in s) h = h * 31 + ch;
                return h;
            }
        }

        /// <summary>Fogs the whole pane at once (testing, hints, or a scripted "someone breathed here" moment).</summary>
        [ContextMenu("Fog The Glass Now")]
        public void FogNow()
        {
            if (_density == null) return;
            for (int c = 0; c < _density.Length; c++) _density[c] = 1f;
            _lastBreath = Time.time;
            _dirty = _anyFog = true;
        }

        /// <summary>Finds this window's glass (the submesh whose material name contains "Glass") and stores its rectangle.</summary>
        [ContextMenu("Fit To Glass")]
        public void FitToGlass()
        {
            var mf = GetComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null) return;
            var mesh = mf.sharedMesh;
            if (!Application.isEditor && !mesh.isReadable) return;
            int sub = -1;
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("glass")) { sub = i; break; }
            if (sub < 0) return;

            var verts = mesh.vertices;
            var idx = mesh.GetIndices(sub);
            if (idx.Length == 0) return;
            var b = new Bounds(verts[idx[0]], Vector3.zero);
            foreach (int i in idx) b.Encapsulate(verts[i]);

            // thinnest axis is the glass normal; of the other two, the one closest to world up is "up"
            Vector3 s = b.size;
            int nAxis = s.x < s.y ? (s.x < s.z ? 0 : 2) : (s.y < s.z ? 1 : 2);
            Vector3 localWorldUp = transform.InverseTransformDirection(Vector3.up);
            int uAxis = -1;
            float best = -1f;
            for (int a = 0; a < 3; a++)
            {
                if (a == nAxis) continue;
                float dot = Mathf.Abs(localWorldUp[a]);
                if (dot > best) { best = dot; uAxis = a; }
            }
            int rAxis = 3 - nAxis - uAxis;
            Vector3 up = Vector3.zero, normal = Vector3.zero;
            up[uAxis] = Mathf.Sign(localWorldUp[uAxis]);
            normal[nAxis] = 1f;

            // into the cab = toward the tower's centre marker, or its root
            Vector3 inside = transform.root.position;
            for (var t = transform.parent; t != null; t = t.parent)
            {
                var m = t.Find("Markers/CabCentre");
                if (m != null) { inside = m.position; break; }
            }
            Vector3 toInside = transform.InverseTransformPoint(inside) - b.center;
            if (Vector3.Dot(toInside, normal) < 0f) normal = -normal;

            glassCentre = b.center;
            glassUp = up;
            interiorNormal = normal;
            glassRight = Vector3.Cross(up, -normal);         // right as seen from inside, looking out
            glassSize = new Vector2(s[rAxis], s[uAxis]);
        }

        void Reset() => FitToGlass();

        void OnDrawGizmosSelected()
        {
            if (glassSize.x <= 0f) return;
            Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(glassCentre, Quaternion.LookRotation(-interiorNormal, glassUp), Vector3.one);
            Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(glassSize.x, glassSize.y, 0f));
            float cw = digitHeight * FingerWriting.CellAspect;
            int n = string.IsNullOrEmpty(code) ? 0 : code.Length;
            float w = n * cw + Mathf.Max(0, n - 1) * digitHeight * FingerWriting.SlotGap + (n % 2 == 0 ? centreGap : 0f);
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(writingCentre.x, writingCentre.y, 0f), new Vector3(w, digitHeight, 0f));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, -reach));
        }
    }
}
