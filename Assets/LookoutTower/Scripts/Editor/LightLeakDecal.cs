using System.IO;
using UnityEditor;
using UnityEngine;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Generates the texture for <see cref="LightLeakReveal"/>'s ceiling decal: spots of lamplight from pinholes, laid out
    /// as a dot-matrix code ("___9" → _ _ _ 9) in a faint warm pool. Each texel is placed on the quad it will be drawn on,
    /// so the code reads the right way round for someone looking up at it whichever way the quad is turned. The holes get
    /// a little hand-punched wobble in size, brightness and position.
    /// </summary>
    public static class LightLeakDecal
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

        /// <param name="path">Asset path of the PNG to write.</param>
        /// <param name="quad">The decal quad (built-in Quad mesh, scaled to size), already placed and turned.</param>
        /// <param name="readRight">World direction that reads left to right for someone looking up at it.</param>
        /// <param name="readUp">World direction that is the top of the characters.</param>
        /// <param name="code">Characters to punch, e.g. "___9".</param>
        /// <param name="pitch">Hole spacing, metres.</param>
        public static Texture2D Build(string path, Transform quad, Vector3 readRight, Vector3 readUp, string code,
                                      float pitch = 0.022f, int width = 512, int height = 256)
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
                        float x = -textW * 0.5f + (g * (GlyphW + Gap) + c) * pitch + (Rand() - 0.5f) * 0.16f * pitch;
                        float y = (3 - r) * pitch + (Rand() - 0.5f) * 0.16f * pitch;
                        holes.Add(new Vector4(x, y, 0.85f + 0.3f * Rand(), 0.8f + 0.2f * Rand()));
                    }
            }

            Vector3 right = readRight.normalized, up = readUp.normalized, centre = quad.position;
            float holeR = 0.36f * pitch, edge = 0.12f * pitch, halo = 0.4f * pitch;
            var px = new Color32[width * height];
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                {
                    float u = (i + 0.5f) / width, v = (j + 0.5f) / height;
                    var w = quad.TransformPoint(new Vector3(u - 0.5f, v - 0.5f, 0f)) - centre;
                    float sx = Vector3.Dot(w, right), sy = Vector3.Dot(w, up);
                    // faint pool of light around the code, zero at the quad's edges
                    float ex = 2f * Mathf.Abs(u - 0.5f), ey = 2f * Mathf.Abs(v - 0.5f);
                    float border = Mathf.Clamp01((1f - Mathf.Max(ex, ey)) / 0.15f);
                    float val = 0.1f * Mathf.Exp(-(ex * ex + ey * ey) * 2.2f) * border;
                    foreach (var h in holes)
                    {
                        float dx = sx - h.x, dy = sy - h.y;
                        float dd = dx * dx + dy * dy;
                        if (dd > 4f * pitch * pitch) continue;
                        float dist = Mathf.Sqrt(dd);
                        float r = holeR * h.z;
                        float disc = 1f - Mathf.SmoothStep(0f, 1f, (dist - r + edge) / (2f * edge));
                        float glow = 0.3f * Mathf.Exp(-dd / (2f * halo * halo));
                        val = Mathf.Max(val, h.w * Mathf.Max(disc, glow) * border);
                    }
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(val) * 255f);
                    px[j * width + i] = new Color32(b, b, b, 255);
                }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
