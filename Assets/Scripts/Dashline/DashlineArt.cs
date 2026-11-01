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
        public void Outline(Color c)
        {
            var src = (Color[])b.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (src[y * w + x].a > .01f) continue;
                    bool n = (x > 0 && src[y * w + x - 1].a > .5f) || (x < w - 1 && src[y * w + x + 1].a > .5f) || (y > 0 && src[(y - 1) * w + x].a > .5f) || (y < h - 1 && src[(y + 1) * w + x].a > .5f);
                    if (n) b[y * w + x] = c;
                }
        }
        public void WhiteFill() { for (int i = 0; i < b.Length; i++) if (b[i].a > 0f) b[i] = new Color(1f, 1f, 1f, b[i].a); }
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
        public static Sprite SwordSprite()
        {
            var p = new Pix(12, 4);
            Color gold = Hex("#d9a43a"), blade = Hex("#dfe6ff");
            p.Rect(0, 1, 2, 2, gold); p.Rect(3, 0, 1, 4, gold); p.Rect(4, 1, 8, 2, blade); p.Rect(4, 1, 8, 1, Color.white);
            return p.ToSprite(new Vector2(2f / 12f, .5f));
        }
        public static Sprite GunSprite() { return GunSprite(0); }
        public static Sprite GunSprite(int kind)
        {
            Color st = Hex("#c8ccd8"), dk = Hex("#6a6e82"), br = Hex("#7a4a28"), gd = Hex("#ffd34d"), cy = Hex("#6aeaff"), bk = Hex("#2a2c3a");
            Pix p;
            switch (kind)
            {
                case 1: // revolver: long barrel, gold cylinder
                    p = new Pix(14, 7);
                    p.Rect(5, 1, 9, 2, st); p.Rect(5, 1, 9, 1, Color.white); p.Rect(5, 3, 7, 1, dk); p.Rect(13, 0, 1, 1, dk);
                    p.Rect(3, 0, 3, 4, gd); p.Rect(4, 1, 1, 2, bk); p.Rect(3, 0, 3, 1, Color.white);
                    p.Rect(0, 2, 3, 3, br); p.Rect(0, 4, 3, 3, br); p.Rect(1, 3, 1, 3, Hex("#a06a38"));
                    return p.ToSprite(new Vector2(.15f, .45f));
                case 2: // shotgun: twin tube, pump, stock
                    p = new Pix(18, 6);
                    p.Rect(6, 1, 12, 1, st); p.Rect(6, 1, 12, 1, Color.white); p.Rect(6, 2, 12, 1, dk); p.Rect(17, 1, 1, 2, bk);
                    p.Rect(9, 3, 5, 2, br); p.Rect(9, 3, 5, 1, Hex("#a06a38"));
                    p.Rect(4, 0, 4, 4, bk); p.Rect(5, 1, 2, 1, gd);
                    p.Rect(0, 2, 5, 3, br); p.Rect(0, 3, 2, 3, br); p.Rect(1, 2, 3, 1, Hex("#a06a38"));
                    return p.ToSprite(new Vector2(.15f, .4f));
                case 3: // railcannon: coils and a glowing barrel
                    p = new Pix(20, 8);
                    p.Rect(3, 2, 10, 4, dk); p.Rect(3, 2, 10, 1, st);
                    p.Rect(4, 1, 1, 6, cy); p.Rect(7, 1, 1, 6, cy); p.Rect(10, 1, 1, 6, cy);
                    p.Rect(13, 3, 7, 2, st); p.Rect(13, 3, 7, 1, Color.white); p.Rect(13, 4, 7, 1, cy);
                    p.Rect(0, 3, 3, 5, br); p.Rect(2, 5, 3, 3, bk);
                    return p.ToSprite(new Vector2(.12f, .45f));
                case 4: // SMG: boxy body, long mag
                    p = new Pix(13, 9);
                    p.Rect(2, 1, 9, 3, st); p.Rect(2, 1, 9, 1, Color.white); p.Rect(10, 2, 3, 1, dk); p.Rect(4, 0, 4, 1, dk);
                    p.Rect(6, 4, 2, 5, bk); p.Rect(6, 4, 2, 1, gd);
                    p.Rect(2, 4, 2, 4, br); p.Rect(0, 2, 3, 2, br);
                    return p.ToSprite(new Vector2(.15f, .3f));
            }
            p = new Pix(11, 6);
            p.Rect(3, 1, 8, 2, st); p.Rect(3, 1, 8, 1, Color.white); p.Rect(10, 1, 1, 2, dk);
            p.Rect(2, 0, 3, 4, gd); p.Rect(3, 1, 1, 2, dk);
            p.Rect(0, 2, 3, 3, br); p.Rect(0, 4, 2, 2, br);
            p.Rect(4, 3, 2, 1, dk);
            return p.ToSprite(new Vector2(.2f, .5f));
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
        public static Sprite TileChunk(World lv, Theme th, int chunk)
        {
            var p = new Pix(Data.CW * T, H);
            Color t0 = Hex(th.tile[0]), t1 = Hex(th.tile[1]), t2 = Hex(th.tile[2]), t3 = Hex(th.tile[3]);
            Color spike = Hex(th.spike);
            for (int y = 0; y < ROWS; y++)
                for (int cx = 0; cx < Data.CW; cx++)
                {
                    int x = chunk * Data.CW + cx;
                    char ch = lv.map[x][y];
                    int X = cx * T, Y = y * T;
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
                        bool up = lv.WallT(x, y - 1), dn = lv.WallT(x, y + 1), lf = lv.WallT(x - 1, y), rt = lv.WallT(x + 1, y);
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

        // ---------------- periodic parallax strips (endless) ----------------
        public const int Period = 720;
        public static Sprite MountainStrip(Theme th, int i)
        {
            float bas = MtL[i][1], amp = MtL[i][2], k = 6.2832f / Period;
            var p = new Pix(Period + W, H);
            Color col = Hex(th.mount[i]);
            for (int x = 0; x < Period + W; x++)
            {
                float y = bas - (Mathf.Sin(x * k * (3 + i) + i * 2.1f) * amp * .6f + Mathf.Sin(x * k * (7 + i * 2) + i * 5.3f) * amp * .3f + Mathf.Abs(Mathf.Sin(x * k * (2 + i) + i)) * amp * .7f);
                int yi = Mathf.Clamp(Mathf.RoundToInt(y), 0, H);
                p.Rect(x, yi, 1, H - yi, col);
            }
            return p.ToSprite(new Vector2(0, 0));
        }
        public static Sprite StalStrip(Theme th, int k)
        {
            int step = 30 + k * 15;
            var p = new Pix(Period + W, H);
            Color col = Hex(th.mount[2 - k]);
            for (int id = -1; id * step < Period + W + step; id++)
            {
                float x = id * step, hh = 14 + Hash(((id % (Period / step)) + Period / step) % (Period / step), k, 3) * 26;
                p.Tri(x - 8, 0, x + 2, hh, x + 12, 0, col);
            }
            return p.ToSprite(new Vector2(0, 0));
        }


        // ---------------- humanoid skins: 16x22 frames, feet on the bottom row, facing right ----------------
        public static Sprite[] SkinFrames(int sk)
        {
            var res = new Sprite[8];
            for (int f = 0; f < 7; f++) res[f] = SkinFrame(sk, f, false);
            res[7] = SkinFrame(sk, 5, true);
            return res;
        }
        static void Limb(Pix p, float x0, float y0, float x1, float y1, int th, Color c)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2f));
            for (int i = 0; i <= n; i++) { float t = i / (float)n; p.Rect(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), th, th, c); }
        }
        static Sprite SkinFrame(int sk, int pose, bool white)
        {
            var p = new Pix(16, 22);
            Color skinT = C(244, 204, 170), skinD = C(206, 156, 124), eye = C(24, 20, 34), line = C(14, 10, 22);
            Color top, topL, topD, pants, pantsD, boot, belt;
            switch (sk)
            {
                case 1: top = C(176, 32, 44); topL = C(220, 70, 80); topD = C(112, 16, 28); pants = C(52, 40, 52); pantsD = C(34, 26, 36); boot = C(30, 22, 28); belt = C(244, 196, 74); break;
                case 2: top = C(64, 142, 74); topL = C(100, 184, 98); topD = C(38, 92, 50); pants = C(116, 82, 48); pantsD = C(84, 58, 34); boot = C(72, 46, 28); belt = C(156, 104, 52); break;
                case 3: top = C(232, 182, 62); topL = C(255, 238, 146); topD = C(162, 112, 32); pants = C(170, 122, 40); pantsD = C(124, 84, 26); boot = C(120, 84, 28); belt = C(122, 78, 20); break;
                case 4: top = C(72, 112, 204); topL = C(124, 164, 244); topD = C(42, 72, 142); pants = C(60, 96, 180); pantsD = C(42, 72, 142); boot = C(60, 44, 40); belt = C(236, 236, 255); break;
                case 5: top = C(64, 32, 114); topL = C(116, 64, 186); topD = C(32, 14, 62); pants = C(44, 22, 84); pantsD = C(28, 12, 56); boot = C(28, 12, 56); belt = C(255, 80, 220); break;
                default: top = C(42, 50, 88); topL = C(66, 76, 124); topD = C(26, 30, 54); pants = C(30, 34, 62); pantsD = C(20, 22, 44); boot = C(18, 20, 34); belt = C(196, 52, 62); break;
            }
            int hy = 6, L = 0, bob = 0;
            float bdx = 0, bl = 0, fdx = 0, fl = 0, badx = -1, bady = 5, fadx = 1, fady = 5;
            switch (pose)
            {
                case 1: bdx = -3; bl = 0; fdx = 2; fl = 2; badx = 2; bady = 4; fadx = -2; fady = 4; break;
                case 2: bdx = 2; bl = 3; fdx = -2; fl = 0; badx = -2; bady = 4; fadx = 2; fady = 4; bob = 1; break;
                case 3: bdx = -1; bl = 2; fdx = 2; fl = 3; badx = -1; bady = -2; fadx = 2; fady = -3; break;
                case 4: bdx = -2; bl = 1; fdx = 2; fl = 1; badx = -3; bady = -1; fadx = 3; fady = -2; break;
                case 5: L = 2; bob = 1; bdx = -5; bl = 1; fdx = -3; fl = 2; badx = -5; bady = 1; fadx = -4; fady = 2; break;
                case 6: bdx = 0; bl = 0; fdx = 1; fl = 1; badx = -1; bady = -4; fadx = 1; fady = -4; break;
            }
            bool ghost = sk == 5, robe = sk == 4;
            int ty = hy + 5 + bob;     // torso top
            int hipY = hy + 10;
            // ---- back arm
            Limb(p, 5 + L, ty + 1, 5 + L + badx, ty + 1 + bady, 2, topD);
            p.Rect(5 + L + (int)badx, ty + 1 + (int)bady, 2, 2, skinD);
            // ---- legs (back then front)
            if (!ghost)
            {
                Limb(p, 5, hipY, 5 + bdx, 20 - bl, 2, pantsD); p.Rect((int)(5 + bdx), (int)(20 - bl), 3, 2, boot);
            }
            // ---- back accessories
            switch (sk)
            {
                case 0: break;
                case 1: Limb(p, 2 + L, ty + 7, 6 + L, ty - 3, 1, C(70, 70, 84)); p.Rect(6 + L, ty - 4, 2, 2, belt); break;
                case 2:
                    p.Rect(2 + L, ty - 1, 3, 8, C(120, 78, 40)); p.Rect(2 + L, ty - 3, 1, 2, Color.white); p.Rect(3 + L, ty - 4, 1, 3, Color.white); p.Rect(4 + L, ty - 3, 1, 2, Color.white);
                    p.Rect(3 + L, ty, 4, 9, C(34, 86, 46)); break;
                case 3: p.Rect(2 + L, ty, 4, 11, C(190, 36, 48)); p.Rect(2 + L, ty, 1, 11, C(130, 20, 32)); break;
                case 4: p.Rect(3 + L, hy + 1, 3, 8, C(226, 232, 246)); break;
                case 5: p.Disc(2 + L, ty + 2, 1.5f, C(255, 80, 220)); p.Disc(14 + L, ty + 6, 1.5f, C(150, 90, 255)); break;
            }
            // ---- torso
            p.Rect(5 + L, ty, 6, 5, top);
            p.Rect(5 + L, ty, 6, 1, topL); p.Rect(5 + L, ty + 1, 1, 4, topD); p.Rect(10 + L, ty + 1, 1, 4, topD);
            p.Rect(5 + L, ty + 4, 6, 1, belt);
            if (robe)
            {
                for (int r = 0; r < 6; r++) { int wd = 6 + r / 2; p.Rect(5 + L - r / 3 - (pose == 1 ? 0 : 0), hipY + r - 1 + bob / 2, wd + r / 3, 1, r == 0 ? topL : (r % 2 == 0 ? top : topD)); }
            }
            if (ghost)
            {
                for (int r = 0; r < 7; r++) { int wd = Mathf.Max(1, 6 - r); int off = Mathf.RoundToInt(Mathf.Sin(r * .9f + pose) * 1.2f); p.Rect(8 + L - wd / 2 + off, hipY - 1 + r + bob / 2, wd, 1, r % 2 == 0 ? top : topD); }
            }
            // ---- front leg
            if (!ghost)
            {
                Limb(p, 8, hipY, 8 + fdx, 20 - fl, 2, pants); p.Rect((int)(8 + fdx), (int)(20 - fl), 3, 2, boot);
                p.Rect((int)(8 + fdx) + 2, (int)(20 - fl), 1, 1, C(255, 255, 255, .25f));
            }
            // ---- head
            int hx = 5 + L, hyb = hy + bob;
            p.Rect(hx, hyb, 6, 5, skinT); p.Rect(hx, hyb + 4, 6, 1, skinD);
            p.Rect(hx + 4, hyb + 2, 1, 2, eye); p.Rect(hx + 2, hyb + 2, 1, 2, eye);
            p.Px(hx + 4, hyb + 2, C(255, 255, 255, .0f));
            // ---- hair / headgear / face details per skin
            switch (sk)
            {
                case 0: // ninja: hood + mask + headband with tails
                    p.Rect(hx - 1, hyb - 1, 8, 6, top); p.Rect(hx - 1, hyb - 1, 8, 1, topL);
                    p.Rect(hx + 1, hyb + 1, 5, 2, skinT); p.Rect(hx + 2, hyb + 1, 1, 2, eye); p.Rect(hx + 4, hyb + 1, 1, 2, eye);
                    p.Rect(hx - 1, hyb + 3, 8, 2, topD);
                    p.Rect(hx - 1, hyb, 8, 1, belt);
                    { int w2 = (pose == 1 || pose == 2 || pose == 5) ? 1 : 0; p.Rect(hx - 5, hyb, 4, 1, belt); p.Rect(hx - 6, hyb + 1 + w2, 3, 1, belt); p.Rect(hx - 7, hyb + 2 + w2 * 2, 2, 1, C(150, 36, 48)); }
                    break;
                case 1: // samurai: kabuto with horns, face guard
                    p.Rect(hx - 1, hyb - 1, 8, 2, C(30, 24, 34)); p.Rect(hx - 1, hyb + 1, 2, 3, C(30, 24, 34));
                    p.Rect(hx, hyb - 3, 6, 3, top); p.Rect(hx, hyb - 3, 6, 1, topL); p.Rect(hx - 1, hyb, 8, 1, topD);
                    p.Rect(hx - 1, hyb - 6, 1, 4, belt); p.Rect(hx + 6, hyb - 6, 1, 4, belt); p.Rect(hx + 2, hyb - 6, 2, 3, belt);
                    p.Rect(hx + 1, hyb + 3, 5, 2, topD); p.Rect(hx + 1, hyb + 3, 5, 1, top);
                    break;
                case 2: // ranger: brown hair, green feathered hat
                    p.Rect(hx - 1, hyb + 1, 2, 4, C(112, 70, 40)); p.Rect(hx, hyb, 6, 1, C(112, 70, 40));
                    p.Rect(hx - 2, hyb - 1, 10, 1, topD); p.Rect(hx, hyb - 3, 6, 2, top); p.Rect(hx + 1, hyb - 5, 3, 2, top); p.Rect(hx, hyb - 3, 6, 1, topL);
                    p.Rect(hx, hyb - 1, 6, 1, belt);
                    Limb(p, hx + 4, hyb - 4, hx + 7, hyb - 8, 1, C(238, 70, 70)); p.Rect(hx + 7, hyb - 8, 1, 2, Color.white);
                    break;
                case 3: // knight: gold helm, visor, plume
                    p.Rect(hx - 1, hyb - 1, 8, 6, top); p.Rect(hx - 1, hyb - 1, 8, 1, topL); p.Rect(hx + 5, hyb, 1, 5, topD); p.Rect(hx - 1, hyb, 1, 5, topD);
                    p.Rect(hx + 2, hyb + 2, 5, 1, C(30, 22, 12)); p.Rect(hx + 2, hyb + 3, 5, 1, topD); p.Rect(hx + 4, hyb + 1, 1, 4, topD);
                    p.Rect(hx + 1, hyb - 4, 3, 3, C(206, 44, 58)); p.Rect(hx - 3, hyb - 3 + (pose == 5 ? 1 : 0), 4, 2, C(150, 24, 36));
                    break;
                case 4: // mage: grey hair, hat, beard
                    p.Rect(hx, hyb, 6, 1, C(226, 232, 246));
                    p.Rect(hx - 2, hyb - 1, 10, 1, topD); p.Rect(hx, hyb - 3, 6, 2, top); p.Rect(hx + 1, hyb - 5, 4, 2, top); p.Rect(hx + 2, hyb - 7, 2, 2, top);
                    p.Rect(hx + 4, hyb - 8, 2, 1, top); p.Px(hx + 3, hyb - 4, Color.white); p.Px(hx + 1, hyb - 2, C(200, 240, 255));
                    p.Rect(hx + 3, hyb + 3, 3, 4, Color.white); p.Rect(hx + 3, hyb + 3, 1, 4, C(210, 218, 236));
                    break;
                case 5: // wraith: dark face, glowing eyes, horns
                    p.Rect(hx - 1, hyb - 1, 8, 6, C(26, 10, 52)); p.Rect(hx - 1, hyb - 1, 8, 1, topL);
                    p.Rect(hx + 1, hyb + 1, 5, 2, C(10, 4, 22)); p.Rect(hx + 2, hyb + 1, 1, 2, C(255, 80, 230)); p.Rect(hx + 4, hyb + 1, 1, 2, C(255, 80, 230));
                    p.Rect(hx - 2, hyb - 4, 1, 4, C(150, 90, 255)); p.Rect(hx + 6, hyb - 4, 1, 4, C(150, 90, 255)); p.Rect(hx - 3, hyb - 5, 1, 2, C(150, 90, 255)); p.Rect(hx + 7, hyb - 5, 1, 2, C(150, 90, 255));
                    break;
            }
            // ---- front arm (over torso)
            Limb(p, 9 + L, ty + 1, 9 + L + fadx, ty + 1 + fady, 2, top);
            p.Rect(9 + L + (int)fadx, ty + 1 + (int)fady, 2, 2, skinT);
            // ---- front accessories
            switch (sk)
            {
                case 1: p.Rect(9 + L, ty, 3, 3, top); p.Rect(9 + L, ty, 3, 1, belt); p.Rect(5 + L, ty + 4, 6, 1, belt); break;
                case 2: p.Rect(5 + L, ty + 4, 6, 1, belt); p.Rect(7 + L, ty + 4, 2, 1, C(255, 214, 90)); break;
                case 3: p.Rect(9 + L, ty - 1, 3, 3, top); p.Rect(9 + L, ty - 1, 3, 1, topL); p.Rect(10 + L, ty + 1, 1, 1, topD); break;
                case 4: p.Rect(12 + L + (int)fadx - 1, hyb - 2, 1, 21 - hyb + 2 - (int)(fl), C(140, 100, 60)); p.Disc(12 + L + (int)fadx - .5f, hyb - 3, 2f, C(150, 236, 255)); p.Px(11 + L + (int)fadx, hyb - 4, Color.white); break;
            }
            if (white) p.WhiteFill();
            p.Outline(line);
            return p.ToSprite(new Vector2(.5f, 0f));
        }

        // ---------------- gates + bosses ----------------
        public static Sprite GateSprite()
        {
            var p = new Pix(8, 112);
            Color bar = Hex("#a01818"), hi = Hex("#ff5a4a"), dk = Hex("#400808");
            p.Rect(0, 0, 8, 112, new Color(.6f, .05f, .05f, .35f));
            for (int x = 0; x < 8; x += 3) { p.Rect(x, 0, 2, 112, bar); p.Rect(x, 0, 1, 112, hi); }
            for (int y = 0; y < 112; y += 14) p.Rect(0, y, 8, 2, dk);
            return p.ToSprite(new Vector2(0, 1));
        }
        public static Sprite BossSprite(int kind)
        {
            var p = new Pix(32, 32);
            switch (kind)
            {
                case 0: // Minos corpse
                    {
                        Color sk = C(122, 130, 106), dk = C(70, 78, 62), lt = C(166, 176, 144), red = C(255, 50, 40);
                        p.Rect(8, 22, 6, 10, dk); p.Rect(18, 22, 6, 10, dk); p.Rect(7, 30, 8, 2, sk); p.Rect(17, 30, 8, 2, sk);
                        p.Rect(6, 8, 20, 15, sk); p.Rect(6, 8, 20, 2, lt);
                        for (int i = 0; i < 4; i++) p.Rect(10, 12 + i * 3, 12, 1, dk);
                        p.Rect(14, 14, 4, 5, C(150, 20, 20));
                        p.Rect(1, 10, 5, 14, sk); p.Rect(26, 10, 5, 14, sk); p.Rect(0, 23, 6, 4, dk); p.Rect(26, 23, 6, 4, dk);
                        p.Rect(10, 0, 12, 9, dk); p.Rect(12, 3, 2, 2, red); p.Rect(18, 3, 2, 2, red); p.Rect(12, 7, 8, 2, C(200, 200, 180));
                        p.Rect(9, 0, 1, 3, lt); p.Rect(22, 0, 1, 3, lt);
                        break;
                    }
                case 1: // Gabriel
                    {
                        Color gold = C(240, 200, 80), wh = C(250, 245, 230), bl = C(120, 170, 255), lt = C(255, 236, 170);
                        p.Tri(14, 10, 0, 2, 3, 22, gold); p.Tri(18, 10, 32, 2, 29, 22, gold);
                        p.Tri(14, 11, 4, 6, 6, 19, lt); p.Tri(18, 11, 28, 6, 26, 19, lt);
                        p.Rect(12, 10, 8, 12, wh); p.Rect(12, 18, 8, 2, gold); p.Rect(12, 10, 8, 1, gold);
                        p.Rect(13, 22, 6, 6, wh); p.Tri(13, 28, 19, 28, 16, 32, wh);
                        p.Disc(16, 6, 4, C(250, 222, 192)); p.Ellipse(16, 2, 6, 2, 1, gold);
                        p.Rect(14, 6, 1, 1, bl); p.Rect(17, 6, 1, 1, bl);
                        break;
                    }
                case 2: // V2
                    {
                        Color red = C(200, 34, 44), dk = C(40, 30, 44), cy = C(80, 210, 255), st = C(156, 156, 176);
                        p.Tri(11, 11, 4, 29, 11, 27, C(120, 20, 30));
                        p.Rect(12, 4, 8, 7, dk); p.Rect(14, 6, 6, 2, cy);
                        p.Rect(11, 11, 10, 10, red); p.Rect(15, 14, 2, 3, cy); p.Rect(11, 11, 10, 1, C(255, 90, 90));
                        p.Rect(12, 21, 3, 9, dk); p.Rect(17, 21, 3, 9, dk); p.Rect(11, 29, 5, 3, red); p.Rect(16, 29, 5, 3, red);
                        p.Rect(8, 12, 3, 8, dk); p.Rect(21, 13, 3, 6, dk); p.Rect(23, 15, 8, 3, st); p.Rect(30, 15, 1, 3, cy);
                        break;
                    }
                default: // Malicious face
                    {
                        Color bone = C(228, 222, 202), sh = C(176, 170, 150), blk = C(12, 8, 14), red = C(255, 50, 40);
                        p.Disc(16, 14, 13, bone); p.Rect(5, 22, 22, 3, sh);
                        p.Tri(5, 4, 1, 0, 9, 9, bone); p.Tri(27, 4, 31, 0, 23, 9, bone);
                        p.Rect(8, 9, 6, 7, blk); p.Rect(18, 9, 6, 7, blk); p.Rect(10, 11, 2, 3, red); p.Rect(20, 11, 2, 3, red);
                        p.Tri(16, 17, 14, 22, 18, 22, blk);
                        for (int i = 0; i < 5; i++) p.Rect(8 + i * 4, 24, 3, 5, Color.white);
                        p.Rect(15, 2, 1, 5, sh);
                        break;
                    }
            }
            p.Outline(C(10, 6, 14));
            return p.ToSprite(new Vector2(.5f, 0f));
        }

        // ---------------- enemy / obstacle sprites ----------------
        public static Sprite CoinSprite()
        {
            var p = new Pix(7, 7);
            p.Disc(3.5f, 3.5f, 3.4f, Hex("#c98a10")); p.Disc(3.5f, 3.5f, 2.6f, Hex("#ffd34d")); p.Rect(3, 2, 1, 3, Hex("#fff3b0"));
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite WalkerSprite()
        {
            var p = new Pix(8, 7);
            Color b = Hex("#b0405e"), d = Hex("#6a2038");
            p.Rect(1, 1, 6, 5, b); p.Rect(2, 0, 4, 1, b); p.Rect(0, 3, 1, 3, d); p.Rect(7, 3, 1, 3, d);
            p.Rect(1, 5, 2, 2, d); p.Rect(5, 5, 2, 2, d);
            p.Rect(4, 2, 2, 2, Color.white); p.Rect(5, 3, 1, 1, Color.black);
            p.Rect(2, 0, 1, 1, Hex("#ffd0d8")); p.Rect(5, 0, 1, 1, Hex("#ffd0d8"));
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite BatSprite(bool up)
        {
            var p = new Pix(12, 7);
            Color b = Hex("#7a4fc8"), d = Hex("#40236e");
            p.Rect(4, 2, 4, 4, b); p.Rect(4, 1, 1, 1, b); p.Rect(7, 1, 1, 1, b);
            if (up) { p.Tri(4, 3, 0, 0, 1, 4, d); p.Tri(8, 3, 12, 0, 11, 4, d); }
            else { p.Tri(4, 3, 0, 6, 1, 3, d); p.Tri(8, 3, 12, 6, 11, 3, d); }
            p.Rect(5, 3, 1, 1, Hex("#ff5c7a")); p.Rect(6, 3, 1, 1, Hex("#ff5c7a"));
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite TurretSprite()
        {
            var p = new Pix(8, 8);
            Color b = Hex("#8a8fb8"), d = Hex("#4b4f72");
            p.Rect(0, 2, 8, 6, b); p.Rect(0, 6, 8, 2, d); p.Rect(0, 3, 3, 2, Color.black); p.Rect(1, 3, 1, 1, Hex("#ff5c3a"));
            p.Rect(3, 1, 4, 1, d);
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite SawSprite()
        {
            var p = new Pix(11, 11);
            Color m = Hex("#c8ccd8"), d = Hex("#6a6e82");
            for (int k = 0; k < 8; k++)
            {
                float a = k * .7854f;
                p.Tri(5.5f + Mathf.Cos(a - .3f) * 4, 5.5f + Mathf.Sin(a - .3f) * 4, 5.5f + Mathf.Cos(a) * 5.4f, 5.5f + Mathf.Sin(a) * 5.4f, 5.5f + Mathf.Cos(a + .3f) * 4, 5.5f + Mathf.Sin(a + .3f) * 4, m);
            }
            p.Disc(5.5f, 5.5f, 4f, m); p.Disc(5.5f, 5.5f, 1.6f, d);
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite PlatSprite(int wTiles)
        {
            var p = new Pix(wTiles * T, 4);
            p.Rect(0, 0, wTiles * T, 4, Hex("#7e86b8")); p.Rect(0, 0, wTiles * T, 1, Hex("#c8d0ff")); p.Rect(0, 3, wTiles * T, 1, Hex("#4a4f78"));
            for (int x = 3; x < wTiles * T; x += 8) p.Rect(x, 1, 1, 2, Hex("#4a4f78"));
            return p.ToSprite(new Vector2(0, 1));
        }
        public static Sprite BulletSprite()
        {
            var p = new Pix(5, 3);
            p.Rect(0, 0, 5, 3, Hex("#ff7a3a")); p.Rect(1, 1, 3, 1, Hex("#fff0a0"));
            return p.ToSprite(new Vector2(.5f, .5f));
        }
        public static Sprite CrumbleSprite(Theme th)
        {
            var p = new Pix(T, T);
            Color a = Hex(th.tile[0]), b = Hex(th.tile[2]);
            p.Rect(0, 0, T, T, a); p.Rect(0, 0, T, 1, Hex(th.tile[1])); p.Rect(0, T - 1, T, 1, b);
            p.Px(2, 3, b); p.Px(3, 4, b); p.Px(4, 5, b); p.Px(5, 4, b); p.Px(5, 3, b);
            p.Rect(0, 0, 1, T, new Color(1, 1, 1, .18f)); p.Rect(T - 1, 0, 1, T, new Color(0, 0, 0, .3f));
            return p.ToSprite(new Vector2(0, 1));
        }
        public static Sprite ShrineSprite()
        {
            var p = new Pix(14, 22);
            Color st = Hex("#7e86b8"), dk = Hex("#4a4f78"), gold = Hex("#ffd34d");
            p.Rect(1, 17, 12, 5, dk); p.Rect(2, 14, 10, 3, st); p.Rect(3, 6, 8, 8, st); p.Rect(3, 6, 8, 1, Hex("#c8d0ff"));
            p.Rect(4, 8, 6, 4, Hex("#1a1630")); p.Rect(5, 9, 4, 2, gold); p.Rect(6, 9, 2, 1, Hex("#fff3b0"));
            p.Tri(2, 6, 7, 0, 12, 6, dk);
            return p.ToSprite(new Vector2(.5f, 0f));
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
