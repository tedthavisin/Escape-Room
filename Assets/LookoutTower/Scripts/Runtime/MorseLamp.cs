using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// A signal lamp hanging from the cab ceiling that blinks <see cref="message"/> in Morse code, over and over:
    /// dot = 1 unit lit, dash = 3 units, 1 unit dark between the marks of a character, 3 between characters, then
    /// <see cref="pause"/> seconds dark before it repeats. The bulb's emission and a point light switch together.
    /// </summary>
    public class MorseLamp : MonoBehaviour
    {
        [Tooltip("What the lamp spells. Digits and letters; anything else is skipped.")]
        public string message = "1";
        [Tooltip("Length of a dot, in seconds. A dash is three of these.")]
        public float unit = 0.35f;
        [Tooltip("Dark pause after the message before it repeats.")]
        public float pause = 3f;

        [Header("Parts")]
        public Renderer bulb;
        [ColorUsage(false, true)] public Color litEmission = new Color(3.2f, 2.6f, 1.6f);
        public Light lampLight;

        [Tooltip("Debug: keep the lamp lit.")]
        public bool alwaysLit;

        [Tooltip("Fires as each mark starts; true for a dash. Hook a click or buzz sound here.")]
        public UnityEvent<bool> onMark = new UnityEvent<bool>();

        static readonly Dictionary<char, string> Code = new Dictionary<char, string>
        {
            ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-",
            ['5'] = ".....", ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.",
            ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.", ['G'] = "--.",
            ['H'] = "....", ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..", ['M'] = "--", ['N'] = "-.",
            ['O'] = "---", ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-", ['U'] = "..-",
            ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-", ['Y'] = "-.--", ['Z'] = "--..",
        };

        /// <summary>Morse for one character (dots and dashes), or null if it has none.</summary>
        public static string MorseFor(char c) => Code.TryGetValue(char.ToUpperInvariant(c), out var s) ? s : null;

        // The signal as on/off steps in units: (lit, units, isDash).
        readonly List<(bool lit, int units, bool dash)> _steps = new List<(bool, int, bool)>();
        string _builtFor;
        int _cycleUnits;
        float _t;
        MaterialPropertyBlock _mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public bool IsLit { get; private set; }
        public bool IsDash { get; private set; }

        /// <summary>The message as dots and dashes, characters separated by spaces (e.g. ".----").</summary>
        public string Pattern
        {
            get
            {
                var sb = new StringBuilder();
                foreach (char c in message ?? "")
                {
                    var m = MorseFor(c);
                    if (m == null) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(m);
                }
                return sb.ToString();
            }
        }

        /// <summary>Length of one full signal, message plus pause, in seconds.</summary>
        public float CycleLength { get { Build(); return _cycleUnits * unit + pause; } }

        void Start()
        {
            SetLit(false, false, true);
        }

        void Update()
        {
            _t += Time.deltaTime;
            bool lit = LitAt(_t, out bool dash) || alwaysLit;
            if (lit && !IsLit) onMark.Invoke(dash);
            SetLit(lit, dash);
        }

        /// <summary>Whether the lamp is lit <paramref name="time"/> seconds after it starts, and if so whether that
        /// mark is a dash. The signal is a function of time, so tests can check it without playing.</summary>
        public bool LitAt(float time, out bool dash)
        {
            Build();
            dash = false;
            if (_cycleUnits == 0 || unit <= 0f) return false;
            float t = Mathf.Repeat(time, CycleLength) / unit;
            foreach (var s in _steps)
            {
                if (t < s.units) { dash = s.lit && s.dash; return s.lit; }
                t -= s.units;
            }
            return false;   // in the pause
        }

        void Build()
        {
            if (_builtFor == message) return;
            _builtFor = message;
            _steps.Clear();
            _cycleUnits = 0;
            bool first = true;
            foreach (char c in message ?? "")
            {
                var m = MorseFor(c);
                if (m == null) continue;
                if (!first) Add(false, 3, false);   // between characters
                first = false;
                for (int i = 0; i < m.Length; i++)
                {
                    if (i > 0) Add(false, 1, false);   // between marks
                    bool d = m[i] == '-';
                    Add(true, d ? 3 : 1, d);
                }
            }
        }

        void Add(bool lit, int units, bool dash)
        {
            _steps.Add((lit, units, dash));
            _cycleUnits += units;
        }

        void OnValidate() => _builtFor = null;

        void SetLit(bool lit, bool dash, bool force = false)
        {
            if (!force && lit == IsLit) { IsDash = lit && dash; return; }
            IsLit = lit;
            IsDash = lit && dash;
            if (bulb != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                bulb.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionId, lit ? litEmission : Color.black);
                bulb.SetPropertyBlock(_mpb);
            }
            if (lampLight != null) lampLight.enabled = lit;
        }
    }
}
