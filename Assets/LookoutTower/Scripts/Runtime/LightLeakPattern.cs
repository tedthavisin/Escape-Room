using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Draws the fuse box reward's ceiling pattern (<see cref="LightLeakReveal"/>): spots of lamplight from pinholes in
    /// the lamp shade, laid out as a dot-matrix code ("___9" → _ _ _ 9), inside the dark pool of the shade's own shadow.
    /// RGB is the light, alpha is how much the pool darkens the ceiling (and 1 on the spots), so with alpha blending the
    /// dots stand out even where the lamp lights the ceiling (the Quest has no lamp shadows). Each texel is placed on the
    /// quad it will be drawn on, so the code reads the right way round for someone looking up at it whichever way the
    /// quad is turned. Holes get a little hand-punched wobble in size, brightness and position.
    /// Used at runtime by LightLeakReveal, and by the editor's LightLeakDecal for the baked fallback texture.
    /// </summary>
    public static class LightLeakPattern
    {
        // 5 x 7 dot glyphs, top row first
        static string[] Glyph(char c)
        {
            switch (c)
            {
                case '0': return new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." };
                case '1': return new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." };
                case '2': return new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" };
                case '3': return new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." };
                case '4': return new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." };
                case '5': return new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." };
                case '6': return new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." };
                case '7': return new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." };
                case '8': return new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." };
                case '9': return new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." };
                case '_': return new[] { ".....", ".....", ".....", ".....", ".....", ".....", "#####" };
                default:  return new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." };
            }
        }

        /// <param name="quad">The decal quad (built-in Quad mesh, scaled to size), already placed and turned.</param>
        /// <param name="readRight">World direction that reads left to right for someone looking up at it.</param>
        /// <param name="readUp">World direction that is the top of the characters.</param>
        /// <param name="code">Characters to punch, e.g. "___9".</param>
        /// <param name="pitch">Hole spacing, metres.</param>
        /// <param name="shadow">How dark the shade's shadow pool is, 0 (none) .. 1 (black).</param>
        public static Color32[] Render(Transform quad, Vector3 readRight, Vector3 readUp, string code, float pitch,
                                       int width, int height, float shadow)
        {
            var holes = new System.Collections.Generic.List<Vector4>();   // x, y (metres from centre), size, brightness
            var rng = new System.Random(1789);
            System.Func<float> Rand = () => (float)rng.NextDouble();
            const int GlyphW = 5, Gap = 3;
            float textW = (code.Length * GlyphW + (code.Length - 1) * Gap - 1) * pitch;
            for (int g = 0; g < code.Length; g++)
            {
                var rows = Glyph(code[g]);
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < GlyphW; c++)
                    {
                        if (rows[r][c] != '#') continue;
                        float x = -textW * 0.5f + (g * (GlyphW + Gap) + c) * pitch + (Rand() - 0.5f) * 0.12f * pitch;
                        float y = (3 - r) * pitch + (Rand() - 0.5f) * 0.12f * pitch;
                        holes.Add(new Vector4(x, y, 0.9f + 0.2f * Rand(), 0.9f + 0.1f * Rand()));
                    }
            }

            Vector3 right = readRight.normalized, up = readUp.normalized, centre = quad.position;
            float holeR = 0.42f * pitch, edge = 0.1f * pitch, halo = 0.5f * pitch;
            float reach2 = 4f * pitch * pitch;
            var px = new Color32[width * height];
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                {
                    float u = (i + 0.5f) / width, v = (j + 0.5f) / height;
                    var w = quad.TransformPoint(new Vector3(u - 0.5f, v - 0.5f, 0f)) - centre;
                    float sx = Vector3.Dot(w, right), sy = Vector3.Dot(w, up);
                    // the shade's shadow: a soft rounded pool, clear at the quad's edges
                    float ex = 2f * Mathf.Abs(u - 0.5f), ey = 2f * Mathf.Abs(v - 0.5f);
                    float m = Mathf.Sqrt(Mathf.Sqrt(ex * ex * ex * ex + ey * ey * ey * ey));
                    float pool = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - m) / 0.35f));
                    // a faint warm wash of light through the shade's top, inside the pool
                    float val = 0.06f * pool;
                    foreach (var h in holes)
                    {
                        float dx = sx - h.x, dy = sy - h.y;
                        float dd = dx * dx + dy * dy;
                        if (dd > reach2) continue;
                        float dist = Mathf.Sqrt(dd);
                        float r = holeR * h.z;
                        float disc = 1f - Mathf.SmoothStep(0f, 1f, (dist - r + edge) / (2f * edge));
                        float glow = 0.45f * Mathf.Exp(-dd / (2f * halo * halo));
                        val = Mathf.Max(val, h.w * Mathf.Max(disc, glow));
                    }
                    val = Mathf.Clamp01(val);
                    float a = Mathf.Max(shadow * pool, val);
                    byte b = (byte)Mathf.RoundToInt(val * 255f);
                    px[j * width + i] = new Color32(b, b, b, (byte)Mathf.RoundToInt(a * 255f));
                }
            return px;
        }
    }
}
