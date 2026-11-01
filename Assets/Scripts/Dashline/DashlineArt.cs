using UnityEngine;

namespace Dashline
{
    // ------------------------------------------------------------------
    //  Pixel canvas used to paint every texture in code (no asset files).
    //  Coordinates are top-down like the original game: (0,0) = top-left.
    // ------------------------------------------------------------------
    public class Pix
    {
        public readonly int w, h;
        readonly Color[] b;
        public Pix(int w, int h) { this.w = w; this.h = h; b = new Color[w * h]; }

        public void Px(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || c.a <= 0f) return;
            int i = y * w + x;
            Color d = b[i];
            float a = c.a, oa = a + d.a * (1f - a);
            if (oa <= 0f) return;
            float k = d.a * (1f - a);
            b[i] = new Color((c.r * a + d.r * k) / oa, (c.g * a + d.g * k) / oa, (c.b * a + d.b * k) / oa, oa);
        }
        public void Rect(int x, int y, int rw, int rh, Color c)
        {
            for (int yy = y; yy < y + rh; yy++) for (int xx = x; xx < x + rw; xx++) Px(xx, yy, c);
        }
        public void Tri(float x0, float y0, float x1, float y1, float x2, float y2, Color c)
        {
            int minx = Mathf.FloorToInt(Mathf.Min(x0, Mathf.Min(x1, x2))), maxx = Mathf.CeilToInt(Mathf.Max(x0, Mathf.Max(x1, x2)));
            int miny = Mathf.FloorToInt(Mathf.Min(y0, Mathf.Min(y1, y2))), maxy = Mathf.CeilToInt(Mathf.Max(y0, Mathf.Max(y1, y2)));
            float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (Mathf.Abs(den) < 1e-6f) return;
            for (int y = miny; y <= maxy; y++)
                for (int x = minx; x <= maxx; x++)
                {
                    float px = x + .5f, py = y + .5f;
                    float a = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / den;
                    float bb = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / den;
                    float cc = 1f - a - bb;
                    if (a >= 0 && bb >= 0 && cc >= 0) Px(x, y, c);
                }
        }
        public void Disc(float cx, float cy, float r, Color c)
        {
            for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                    if ((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy) <= r * r) Px(x, y, c);
        }
        public void Ellipse(float cx, float cy, float rx, float ry, float thick, Color c)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + .5f - cx) / rx, dy = (y + .5f - cy) / ry, e = dx * dx + dy * dy;
                    if (e > 1f) continue;
                    if (thick <= 0f) { Px(x, y, c); continue; }
                    float ix = (x + .5f - cx) / (rx - thick), iy = (y + .5f - cy) / (ry - thick);
                    if (ix * ix + iy * iy >= 1f) Px(x, y, c);
                }
        }
        public Sprite ToSprite(Vector2 pivot01)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) System.Array.Copy(b, y * w, px, (h - 1 - y) * w, w);
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), pivot01, 1f);
        }
    }

    // ------------------------------------------------------------------
    //  All procedural art
    // ------------------------------------------------------------------
    public static class Art
    {
        const int W = Data.W, H = Data.H, T = Data.T, ROWS = Data.ROWS;

        public static Color Hex(string h, float a = 1f) { Color c; ColorUtility.TryParseHtmlString(h, out c); c.a = a; return c; }
        public static Color C(int r, int g, int b, float a = 1f) { return new Color(r / 255f, g / 255f, b / 255f, a); }
        public static float Hash(int x, int y, int s = 0)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + s * 982451653;
                n = (n ^ (int)((uint)n >> 13)) * 1274126177;
                uint u = (uint)(n ^ (int)((uint)n >> 16));
                return u / 4294967296f;
            }
        }

        static Sprite pixel, glow, vig;
        public static Sprite Pixel()
        {
            if (pixel == null)
            {
                var t = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                t.SetPixel(0, 0, Color.white); t.Apply();
                pixel = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
            }
            return pixel;
        }
        public static Sprite Glow()
        {
            if (glow == null)
            {
                var p = new Pix(64, 64);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 32f;
                    if (d < 1f) { float a = 1f - d; p.Px(x, y, new Color(1, 1, 1, a * a)); }
                }
                glow = p.ToSprite(new Vector2(.5f, .5f));
                glow.texture.filterMode = FilterMode.Bilinear;
            }
            return glow;
        }
        public static Sprite Vignette()
        {
            if (vig == null)
            {
                var p = new Pix(W, H);
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    float d = Mathf.Sqrt((x - W / 2f) * (x - W / 2f) + (y - H / 2f) * (y - H / 2f));
                    float t = Mathf.Clamp01((d - H * .35f) / (W * .62f - H * .35f));
                    p.Px(x, y, new Color(0, 0, 0, .55f * t * t * (3 - 2 * t)));
                }
                vig = p.ToSprite(new Vector2(0, 0));
                vig.texture.filterMode = FilterMode.Bilinear;
            }
            return vig;
        }

        // ---------------- small object sprites ----------------
        public static Sprite CrystalSprite()
        {
            var p = new Pix(10, 12);
            Color body = Hex("#3fd98a"), lite = Hex("#9dffc8");
            p.Tri(5, 1, 9, 6, 5, 11, body); p.Tri(5, 1, 5, 11, 1, 6, body);
            p.Tri(5, 1, 7, 6, 5, 7, lite); p.Tri(5, 1, 5, 7, 3, 5, lite);
            p.Rect(4, 3, 1, 2, Color.white);
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite LanternSprite(bool lit)
        {
            var p = new Pix(10, 24);
            Color pole = Hex("#9aa0c4");
            p.Rect(4, 8, 2, 16, pole);
            p.Rect(2, 3, 6, 5, lit ? Hex("#ffd34d") : Hex("#5b5f82"));
            p.Rect(3, 4, 4, 3, lit ? Hex("#fff6c0") : Hex("#3b3e58"));
            p.Rect(1, 2, 8, 1, pole); p.Rect(1, 8, 8, 1, pole);
            return p.ToSprite(new Vector2(.5f, 0f));
        }
        public static Sprite PortalSprite()
        {
            var p = new Pix(22, 34);
            p.Ellipse(11, 17, 9, 15, 0, new Color(1f, .94f, .7f, .22f));
            p.Ellipse(11, 17, 9, 15, 2, Hex("#ffd34d"));
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite RingSprite()
        {
            var p = new Pix(32, 32);
            p.Ellipse(16, 16, 15, 15, 1.3f, Color.white);
            var s = p.ToSprite(new Vector2(.5f, .5f));
            s.texture.filterMode = FilterMode.Bilinear;
            return s;
        }
        public static Sprite CloudSprite()
        {
            var p = new Pix(48, 26);
            p.Disc(10, 16, 7, Color.white); p.Disc(21, 11, 9, Color.white); p.Disc(33, 16, 7, Color.white); p.Rect(10, 16, 23, 7, Color.white);
            return p.ToSprite(new Vector2(0, 0));
        }

        // ---------------- backgrounds ----------------
        public static Sprite Sky(Theme th)
        {
            var p = new Pix(W, H);
            Color s0 = Hex(th.sky[0]), s1 = Hex(th.sky[1]), s2 = Hex(th.sky[2]);
            for (int y = 0; y < H; y++)
            {
                float t = y / (float)(H - 1);
                p.Rect(0, y, W, 1, t < .6f ? Color.Lerp(s0, s1, t / .6f) : Color.Lerp(s1, s2, (t - .6f) / .4f));
            }
            for (int i = 0; i < th.stars; i++)
            {
                float s = Hash(i, 13);
                int sz = s > .85f ? 2 : 1;
                p.Rect((int)(Hash(i, 11) * W), (int)(Hash(i, 12) * H * .7f), sz, sz, new Color(1, 1, 1, .35f + s * .6f));
            }
            if (th.hasOrb)
            {
                Color gl = Hex(th.orbGlow);
                float R = th.orbR * 4.5f;
                for (int y = Mathf.Max(0, (int)(th.orbY - R)); y < Mathf.Min(H, (int)(th.orbY + R)); y++)
                    for (int x = Mathf.Max(0, (int)(th.orbX - R)); x < Mathf.Min(W, (int)(th.orbX + R)); x++)
                    {
                        float d = Mathf.Sqrt((x - th.orbX) * (x - th.orbX) + (y - th.orbY) * (y - th.orbY));
                        float t = Mathf.Clamp01((d - th.orbR * .4f) / (R - th.orbR * .4f));
                        if (t < 1f) p.Px(x, y, new Color(gl.r, gl.g, gl.b, .6f * (1f - t)));
                    }
                p.Disc(th.orbX, th.orbY, th.orbR, Hex(th.orbC));
            }
            return p.ToSprite(new Vector2(0, 0));
        }
        public static Sprite Fog(Theme th)
        {
            var p = new Pix(1, H);
            Color f = Hex(th.fog);
            for (int y = 0; y < H; y++)
            {
                float t = Mathf.Clamp01((y - H * .35f) / (H * .65f));
                p.Px(0, y, new Color(f.r, f.g, f.b, .38f * t));
            }
            var s = p.ToSprite(new Vector2(0, 0));
            s.texture.filterMode = FilterMode.Bilinear;
            return s;
        }
        static readonly float[][] MtL = { new[] { .07f, 120f, 24f }, new[] { .16f, 140f, 20f }, new[] { .3f, 156f, 15f } };
        public static float MountainParallax(int i) { return MtL[i][0]; }
        public static Sprite Mountain(Theme th, int i, float worldW)
        {
            float par = MtL[i][0], bas = MtL[i][1], amp = MtL[i][2];
            int width = Mathf.CeilToInt(worldW * par + W + 8);
            var p = new Pix(width, H);
            Color col = Hex(th.mount[i]);
            for (int x = 0; x < width; x++)
            {
                float y = bas - (Mathf.Sin(x * .021f + i * 2.1f) * amp * .6f + Mathf.Sin(x * .047f + i * 5.3f) * amp * .3f + Mathf.Abs(Mathf.Sin(x * .013f + i)) * amp * .7f);
                int yi = Mathf.Clamp(Mathf.RoundToInt(y), 0, H);
                p.Rect(x, yi, 1, H - yi, col);
            }
            return p.ToSprite(new Vector2(0, 0));
        }
        public static float StalParallax(int k) { return .22f + k * .22f; }
        public static Sprite Stalactites(Theme th, int k, float worldW)
        {
            float par = StalParallax(k);
            int step = 30 + k * 16, width = Mathf.CeilToInt(worldW * par + W + 40);
            var p = new Pix(width, H);
            Color col = Hex(th.mount[2 - k]);
            for (int id = -1; id * step < width + step; id++)
            {
                float x = id * step, hh = 14 + Hash(id, k, 3) * 26;
                p.Tri(x - 8, 0, x + 2, hh, x + 12, 0, col);
            }
            return p.ToSprite(new Vector2(0, 0));
        }

        // ---------------- the big static tile layer ----------------
        public static Sprite TileLayer(Level lv)
        {
            Theme th = lv.th;
            var p = new Pix(lv.cols * T, H);
            Color t0 = Hex(th.tile[0]), t1 = Hex(th.tile[1]), t2 = Hex(th.tile[2]), t3 = Hex(th.tile[3]);
            Color spike = Hex(th.spike);
            for (int y = 0; y < ROWS; y++)
                for (int x = 0; x < lv.cols; x++)
                {
                    char ch = lv.map[y][x];
                    int X = x * T, Y = y * T;
                    if (ch == '#')
                    {
                        float r = Hash(x, y, 1);
                        p.Rect(X, Y, T, T, t0);
                        p.Rect(X, Y, T, T, r > .5f ? C(255, 255, 255, .06f) : C(0, 0, 0, .10f));
                        for (int k = 0; k < 3; k++)
                        {
                            int sx = (int)(Hash(x, y, 10 + k) * 7), sy = (int)(Hash(x, y, 20 + k) * 7);
                            p.Px(X + sx, Y + sy, Hash(x, y, 30 + k) > .5f ? C(255, 255, 255, .12f) : C(0, 0, 0, .2f));
                        }
                        p.Rect(X, Y + T - 1, T, 1, C(0, 0, 0, .12f));
                        p.Rect(X + ((y % 2 == 1) ? 3 : 7), Y, 1, T - 1, C(0, 0, 0, .12f));
                        bool up = lv.Solid(x, y - 1), dn = lv.Solid(x, y + 1), lf = lv.Solid(x - 1, y), rt = lv.Solid(x + 1, y);
                        if (up && dn && lf && rt) p.Rect(X, Y, T, T, C(0, 0, 0, .16f));
                        if (!up) { p.Rect(X, Y, T, 2, t1); p.Rect(X, Y, T, 1, C(255, 255, 255, .35f)); }
                        if (!dn) p.Rect(X, Y + T - 2, T, 2, t2);
                        if (!lf) p.Rect(X, Y, 1, T, C(255, 255, 255, .2f));
                        if (!rt) p.Rect(X + T - 1, Y, 1, T, C(0, 0, 0, .28f));
                        Decor(p, th, x, y, up, dn, t1, t2, t3);
                    }
                    else if (ch == '^' || ch == 'v')
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            float bx = X + k * 4;
                            if (ch == '^')
                            {
                                p.Tri(bx - .5f, Y + T, bx + 2, Y + 2, bx + 4.5f, Y + T, t2);
                                p.Tri(bx + .5f, Y + T, bx + 2, Y + 3, bx + 3.5f, Y + T, spike);
                            }
                            else
                            {
                                p.Tri(bx - .5f, Y, bx + 2, Y + 6, bx + 4.5f, Y, t2);
                                p.Tri(bx + .5f, Y, bx + 2, Y + 5, bx + 3.5f, Y, spike);
                            }
                        }
                    }
                }
            return p.ToSprite(new Vector2(0, 0));
        }

        static void Decor(Pix c, Theme th, int x, int y, bool up, bool dn, Color t1, Color t2, Color t3)
        {
            int X = x * T, Y = y * T;
            float r = Hash(x, y, 5), r2 = Hash(x, y, 6);
            switch (th.props)
            {
                case "meadow":
                    if (!up)
                    {
                        for (int i = 0; i < T; i += 2) { int hh = 1 + (int)(Hash(x, y, 40 + i) * 3); c.Rect(X + i, Y - hh, 1, hh, t3); }
                        c.Rect(X, Y + 2, T, 1, C(60, 170, 80, .55f));
                        if (r > .78f)
                        {
                            c.Rect(X + 3, Y - 4, 1, 4, Hex("#2f8a4a"));
                            string[] fl = { "#ff7aa8", "#ffe066", "#ffffff" };
                            c.Rect(X + 2, Y - 6, 3, 2, Hex(fl[(int)(r2 * 3) % 3]));
                            c.Rect(X + 3, Y - 5, 1, 1, Hex("#ffd23a"));
                        }
                    }
                    break;
                case "cave":
                    if (!up && r > .8f) { c.Tri(X + 2, Y, X + 3.5f, Y - 6, X + 5, Y, t3); c.Rect(X + 3, Y - 4, 1, 2, Color.white); }
                    if (!dn && r2 > .65f) c.Tri(X + 2, Y + T, X + 3.5f, Y + T + 3 + (int)(r * 4), X + 5, Y + T, t1);
                    break;
                case "ruins":
                    if (r > .72f) { int a = (int)(r2 * 5), b = (int)(r * 5); c.Rect(X + a, Y + b + 1, 2, 1, t3); c.Rect(X + a + 1, Y + b + 2, 1, 2, t3); }
                    if (!up && r2 > .78f) { c.Rect(X + 1, Y - 2, 3, 2, t2); c.Rect(X + 4, Y - 1, 2, 1, t2); }
                    break;
                case "ice":
                    if (!up) { c.Rect(X, Y - 1, T, 1, Color.white); if (r > .5f) c.Rect(X + 2, Y - 2, 4, 1, Color.white); }
                    if (!dn && r2 > .55f) c.Tri(X + 2, Y + T, X + 3.5f, Y + T + 2 + (int)(r * 5), X + 5, Y + T, t3);
                    break;
                case "rune":
                    if (r > .84f) { c.Rect(X + 2, Y + 2, 1, 4, t3); c.Rect(X + 2, Y + 3, 3, 1, t3); c.Rect(X + 4, Y + 2, 1, 2, t3); }
                    if (!up && r2 > .78f) { c.Rect(X + 3, Y - 2, 1, 2, t1); c.Rect(X + 3, Y - 3, 1, 1, Color.white); }
                    break;
            }
        }
    }
}
