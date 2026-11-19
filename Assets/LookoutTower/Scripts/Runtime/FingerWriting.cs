using System.Collections.Generic;
using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Rasterises digits as if someone had written them on glass with a fingertip: wobbly strokes of uneven width,
    /// smoothed through Catmull-Rom curves. Used to make the "hidden" writing that shows up when glass fogs.
    /// </summary>
    public static class FingerWriting
    {
        // Each digit is a list of strokes; each stroke is points in a 0.6 x 1 cell (x right, y up).
        // Corners are separate strokes so the curve smoothing doesn't round them off.
        static readonly Vector2[][][] Glyphs =
        {
            /* 0 */ new[] { Ellipse(0.3f, 0.5f, 0.27f, 0.5f, 18, 1.08f) },
            /* 1 */ new[] { P(0.18f, 0.78f, 0.42f, 1f), P(0.42f, 1f, 0.42f, 0.5f, 0.43f, 0f) },
            /* 2 */ new[] { P(0.04f, 0.76f, 0.14f, 0.94f, 0.32f, 1f, 0.5f, 0.93f, 0.56f, 0.75f, 0.46f, 0.53f, 0.24f, 0.27f, 0.04f, 0f),
                            P(0.04f, 0f, 0.6f, 0.01f) },
            /* 3 */ new[] { P(0.05f, 0.88f, 0.24f, 1f, 0.46f, 0.97f, 0.54f, 0.8f, 0.44f, 0.6f, 0.25f, 0.54f, 0.44f, 0.49f, 0.57f, 0.3f,
                              0.5f, 0.08f, 0.28f, 0f, 0.05f, 0.1f) },
            /* 4 */ new[] { P(0.38f, 1f, 0.02f, 0.32f), P(0.02f, 0.32f, 0.6f, 0.32f), P(0.44f, 0.72f, 0.44f, 0.3f, 0.45f, 0f) },
            /* 5 */ new[] { P(0.56f, 1f, 0.12f, 1f), P(0.12f, 1f, 0.08f, 0.56f),
                            P(0.08f, 0.56f, 0.3f, 0.63f, 0.5f, 0.55f, 0.58f, 0.32f, 0.5f, 0.09f, 0.29f, 0f, 0.05f, 0.09f) },
            /* 6 */ new[] { P(0.52f, 0.94f, 0.32f, 1f, 0.13f, 0.85f, 0.05f, 0.5f, 0.08f, 0.18f, 0.28f, 0f, 0.5f, 0.07f, 0.57f, 0.29f,
                              0.48f, 0.5f, 0.27f, 0.55f, 0.09f, 0.4f) },
            /* 7 */ new[] { P(0.02f, 1f, 0.58f, 1f), P(0.58f, 1f, 0.38f, 0.5f, 0.2f, 0f) },
            /* 8 */ new[] { P(0.3f, 0.55f, 0.1f, 0.68f, 0.09f, 0.88f, 0.3f, 1f, 0.5f, 0.88f, 0.48f, 0.68f, 0.3f, 0.55f, 0.08f, 0.38f,
                              0.05f, 0.15f, 0.3f, 0f, 0.55f, 0.15f, 0.52f, 0.38f, 0.3f, 0.55f) },
            /* 9 */ new[] { P(0.53f, 0.66f, 0.35f, 0.5f, 0.13f, 0.53f, 0.05f, 0.76f, 0.16f, 0.95f, 0.36f, 1f, 0.53f, 0.86f, 0.55f, 0.5f,
                              0.46f, 0f) },
        };

        /// <summary>'_': a short finger dash along the bottom of the cell, marking a slot without a digit.</summary>
        static readonly Vector2[][] Dash = { P(0.04f, 0.02f, 0.3f, 0.0f, 0.57f, 0.03f) };

        public const float CellAspect = 0.6f;
        /// <summary>Space between slots, as a fraction of the digit height.</summary>
        public const float SlotGap = 0.55f;

        /// <summary>
        /// Draws <paramref name="text"/> into <paramref name="mask"/> (row-major, values 0..1, max-blended).
        /// The mask covers <paramref name="areaSize"/> metres; text is centred on <paramref name="centre"/> (metres from
        /// the mask's lower-left corner). '_' draws a dash; any other non-digit leaves a gap. Returns the text's bounds in metres.
        /// </summary>
        public static Rect Draw(float[] mask, int w, int h, Vector2 areaSize, string text, Vector2 centre, float digitHeight,
                                float strokeWidth, float splitGap, int seed)
        {
            if (string.IsNullOrEmpty(text)) return default;
            var rng = new System.Random(seed);
            float cellW = digitHeight * CellAspect;
            float pitch = cellW + digitHeight * SlotGap;
            int n = text.Length;
            bool split = splitGap > 0f && n % 2 == 0;
            float totalW = n * cellW + (n - 1) * (pitch - cellW) + (split ? splitGap : 0f);
            float x0 = centre.x - totalW * 0.5f;
            float y0 = centre.y - digitHeight * 0.5f;
            float pxPerM = w / areaSize.x;

            for (int c = 0; c < n; c++)
            {
                float cx = x0 + c * pitch + (split && c >= n / 2 ? splitGap : 0f);
                int d = text[c] - '0';
                Vector2[][] glyph = d >= 0 && d <= 9 ? Glyphs[d] : text[c] == '_' ? Dash : null;
                if (glyph == null) continue;
                // A hand isn't a printer: each digit leans, sits and sizes a little differently.
                float slant = 0.12f + Rand(rng, -0.06f, 0.06f);
                float scale = 1f + Rand(rng, -0.05f, 0.05f);
                // Dashes all sit on the same line, so they read as blanks in a row rather than stray marks.
                float dy = glyph == Dash ? Rand(rng, -0.015f, 0.015f) * digitHeight : Rand(rng, -0.08f, 0.08f) * digitHeight;
                foreach (var stroke in glyph)
                {
                    var pts = new List<Vector2>(stroke.Length);
                    foreach (var p in stroke)
                    {
                        float jx = Rand(rng, -0.02f, 0.02f), jy = Rand(rng, -0.02f, 0.02f);
                        float u = (p.x + jx + slant * (p.y - 0.5f)) * scale, v = (p.y + jy - 0.5f) * scale + 0.5f;
                        pts.Add(new Vector2(cx + u * digitHeight, y0 + dy + v * digitHeight));
                    }
                    var curve = Smooth(pts, 6);
                    for (int i = 0; i + 1 < curve.Count; i++)
                    {
                        // Finger pressure: heavier mid-stroke, lighter where the finger lifts off.
                        float t = (float)i / Mathf.Max(1, curve.Count - 2);
                        float press = Mathf.Lerp(0.75f, 1f, Mathf.Sin(t * Mathf.PI)) * (1f + Rand(rng, -0.04f, 0.04f));
                        Capsule(mask, w, h, curve[i] * pxPerM, curve[i + 1] * pxPerM, strokeWidth * 0.5f * press * pxPerM);
                    }
                }
            }
            return new Rect(x0, y0 - digitHeight * 0.1f, totalW, digitHeight * 1.2f);
        }

        static void Capsule(float[] mask, int w, int h, Vector2 a, Vector2 b, float r)
        {
            float soft = Mathf.Max(1.2f, r * 0.45f);     // ragged, soft edge where the finger smeared the film
            float R = r + soft;
            int xMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - R));
            int xMax = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + R));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - R));
            int yMax = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + R));
            Vector2 ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                float dist = Vector2.Distance(p, a + ab * t);
                float v = 1f - Mathf.Clamp01((dist - (r - soft)) / (2f * soft));
                int i = y * w + x;
                if (v > mask[i]) mask[i] = v;
            }
        }

        static List<Vector2> Smooth(List<Vector2> p, int sub)
        {
            var o = new List<Vector2>();
            if (p.Count < 3) { o.AddRange(p); return o; }
            for (int i = 0; i + 1 < p.Count; i++)
            {
                Vector2 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Count - 1)];
                for (int s = 0; s < sub; s++)
                {
                    float t = s / (float)sub, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(p[p.Count - 1]);
            return o;
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        static Vector2[] P(params float[] xy)
        {
            var v = new Vector2[xy.Length / 2];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
            return v;
        }

        static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int segs, float turns)
        {
            var v = new Vector2[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                // start at the top and go anticlockwise, overshooting a little like a hand-drawn zero
                float a = Mathf.PI * 0.5f + 0.15f + i / (float)segs * turns * Mathf.PI * 2f;
                v[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            }
            return v;
        }
    }
}
