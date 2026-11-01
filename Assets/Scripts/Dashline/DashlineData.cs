using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dashline
{
    // ------------------------------------------------------------------
    //  DASHLINE (Unity port) - data: themes, chunks, stages, endless
    //  templates, entity classes and the world container.
    // ------------------------------------------------------------------
    public class Theme
    {
        public string name, spike, glow, fog, ambC, amb, props, clouds, orbC, orbGlow;
        public string[] sky, mount, tile;
        public int stars;
        public bool stal, hasOrb, ceil;
        public float orbX, orbY, orbR;
    }

    public class Stage
    {
        public string name, hint;
        public int theme;
        public string[] chunks;
        public Stage(string n, int t, string h, params string[] c) { name = n; theme = t; hint = h; chunks = c; }
    }

    public class Upgrade
    {
        public string id, name, desc;
        public int max;
        public int[] cost;
        public Upgrade(string i, string n, string d, int m, params int[] c) { id = i; name = n; desc = d; max = m; cost = c; }
    }

    // ---------------- entities ----------------
    public class Crystal { public float x, y; public int cd; public SpriteRenderer sr; }
    public class Checkpoint { public float x, y, sx; public bool lit; public SpriteRenderer sr; }
    public class Pickup { public float x, y, ph; public SpriteRenderer sr; }
    public class Shrine { public float x, y; public SpriteRenderer sr; }
    public class Crumble { public int x, y, st, t; public SpriteRenderer sr; }
    public class TossCoin { public float x, y, vx, vy; public int life; public SpriteRenderer sr; }
    public class Bullet { public float x, y, vx, vy; public int life; public bool refl; public SpriteRenderer sr; }
    public class Turret { public float x, y; public int dir, per, cd; public SpriteRenderer sr; }
    public class Saw { public float cx, cy, x, y, r, ax, ay, per, ph; public SpriteRenderer sr; }
    public class Plat { public float cx, cy, w, h, ax, ay, per, ph, x, y, dx, dy; public SpriteRenderer sr; }
    public class Enemy
    {
        public int kind;                       // 0 walker, 1 bat
        public float x, y, w, h, l, r, cx, cy, rx, amp, per, t;
        public int dir, anim; public float sx, sy, spd = .5f;
        public bool alive = true;
        public SpriteRenderer sr;
    }

    public class Boss
    {
        public int kind, t, st, st2, inv, flash, dir = 1; public float hp, maxhp;
        public float x, y, w, h, vx, vy, sx, sy;
        public bool dead, active;
        public SpriteRenderer sr;
    }
    public class Arena
    {
        public float x0, x1; public int gl, gr; public bool started, done, big;
        public readonly List<Enemy> foes = new List<Enemy>();
        public Boss boss; public SpriteRenderer gateL, gateR;
    }

    // template output: an entity to spawn, in chunk-local tile coordinates
    public class EntDef { public string t; public float x, y, w, ax, ay, per, ph, rx, amp, l, rr; public int dir, bk, hp; public bool arena; }

    // deterministic RNG (mulberry32)
    public class Rng
    {
        uint a;
        public Rng(int seed) { a = unchecked((uint)seed); }
        public float Next()
        {
            unchecked
            {
                a += 0x6D2B79F5u;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (float)((t ^ (t >> 14)) / 4294967296.0);
            }
        }
        public int Ri(int lo, int hi) { return lo + (int)Mathf.Floor(Next() * (hi - lo + 1)); }
    }

    public class Tpl
    {
        public string name;
        public int min, max, w;
        public bool noCeil;
        public Action<char[][], Rng, List<EntDef>, int> fn;
    }

    // ------------------------------------------------------------------
    //  The world: a growing list of columns (each column = ROWS chars)
    // ------------------------------------------------------------------
    public class World
    {
        public List<char[]> map = new List<char[]>();
        public List<int> chunkTheme = new List<int>();
        public Dictionary<int, Crumble> crumbles = new Dictionary<int, Crumble>();
        public bool endless;
        public HashSet<int> closedGates = new HashSet<int>();
        public int Cols { get { return map.Count; } }
        public int Chunks { get { return chunkTheme.Count; } }
        public float WorldW { get { return map.Count * Data.T; } }

        public char At(int tx, int ty) { return map[tx][ty]; }
        public bool Solid(int tx, int ty)
        {
            if (tx < 0 || tx >= map.Count) return true;
            if (ty < 0 || ty >= Data.ROWS) return false;
            char c = map[tx][ty];
            if (c == '#') return true;
            if (c == 'X') return closedGates.Contains(tx);
            if (c == 'c')
            {
                Crumble b;
                if (!crumbles.TryGetValue(ty * 100000 + tx, out b)) return true;
                return b.st < 2;
            }
            return false;
        }
        // static wall for tile rendering
        public bool WallT(int tx, int ty)
        {
            if (tx < 0 || tx >= map.Count) return true;
            if (ty < 0 || ty >= Data.ROWS) return false;
            return map[tx][ty] == '#';
        }
    }

    public static class Data
    {
        public const int W = 320, H = 184, T = 8, ROWS = 23, CW = 20, CWP = CW * T;

        // ---------------------------------------------------------- themes
        public static readonly Theme[] Themes =
        {
            new Theme { name = "Dawn Meadow", sky = new[] { "#4f7fd0", "#e59aa8", "#ffd9a0" }, hasOrb = true, orbX = 236, orbY = 64, orbR = 15, orbC = "#fff3c4", orbGlow = "#ffd9a0",
                stars = 0, clouds = "#ffeee6", mount = new[] { "#a08cc6", "#7f74b6", "#5b659f" }, tile = new[] { "#6e4b3a", "#78d66a", "#43291f", "#3da35a" },
                spike = "#f4f0ff", glow = "#ffe9a0", fog = "#ffd9a0", amb = "fire", ambC = "#fff2a0", props = "meadow" },
            new Theme { name = "Crystal Caves", sky = new[] { "#0e0a24", "#1a1647", "#2a2a70" }, hasOrb = false, stars = 45, clouds = null,
                mount = new[] { "#1c1848", "#241f5c", "#2c2672" }, tile = new[] { "#3a3868", "#8f85f0", "#222148", "#63e6ff" },
                spike = "#c8f8ff", glow = "#7de8ff", fog = "#2a2a70", amb = "spore", ambC = "#7df0ff", props = "cave", stal = true, ceil = true },
            new Theme { name = "Sunset Ruins", sky = new[] { "#2a0f35", "#c4432e", "#ffb04a" }, hasOrb = true, orbX = 96, orbY = 116, orbR = 26, orbC = "#ffe6a8", orbGlow = "#ff9a3a",
                stars = 0, clouds = "#e0704c", mount = new[] { "#8a3140", "#66263f", "#431a3a" }, tile = new[] { "#6b4a52", "#e7a06e", "#3b2830", "#ff7a3a" },
                spike = "#ffe4c8", glow = "#ff9a50", fog = "#ff9a50", amb = "ember", ambC = "#ffa850", props = "ruins" },
            new Theme { name = "Frozen Peaks", sky = new[] { "#8fc2ee", "#cfe8fb", "#f2faff" }, hasOrb = true, orbX = 252, orbY = 50, orbR = 12, orbC = "#ffffff", orbGlow = "#ffffff",
                stars = 0, clouds = "#ffffff", mount = new[] { "#b6d2ec", "#8db3d8", "#6a8fba" }, tile = new[] { "#8fa9cc", "#eefaff", "#5f7aa0", "#c4f0ff" },
                spike = "#ffffff", glow = "#d0f4ff", fog = "#f2faff", amb = "snow", ambC = "#ffffff", props = "ice" },
            new Theme { name = "Midnight Summit", sky = new[] { "#04020e", "#140836", "#3c1a6c" }, hasOrb = true, orbX = 70, orbY = 48, orbR = 14, orbC = "#f2eaff", orbGlow = "#b080ff",
                stars = 80, clouds = null, mount = new[] { "#1b1048", "#291762", "#36217c" }, tile = new[] { "#2c2354", "#c47bff", "#15102f", "#ff6bd6" },
                spike = "#ffd6f6", glow = "#d08aff", fog = "#3c1a6c", amb = "spark", ambC = "#ffb8f2", props = "rune" },
            new Theme { name = "Limbo", sky = new[] { "#050308", "#12081c", "#2a0f2a" }, hasOrb = false, stars = 0, clouds = null,
                mount = new[] { "#1a0c22", "#240f2c", "#2e1236" }, tile = new[] { "#3a2a3a", "#8a5a7a", "#1e1220", "#c25a7a" },
                spike = "#e8d0d8", glow = "#c25a7a", fog = "#2a0f2a", amb = "ember", ambC = "#ff5a3a", props = "cave", stal = true, ceil = true },
            new Theme { name = "Lust", sky = new[] { "#1a0510", "#5a0f2a", "#c4264a" }, hasOrb = false, orbX = 200, orbY = 60, orbR = 22, orbC = "#ffd0d8", orbGlow = "#ff3a6a",
                stars = 0, clouds = "#e0506a", mount = new[] { "#4a0f28", "#360a1e", "#25061a" }, tile = new[] { "#5a2a3a", "#ff6a8a", "#2f1420", "#ff3a6a" },
                spike = "#ffe0e8", glow = "#ff5a8a", fog = "#c4264a", amb = "ember", ambC = "#ff8aa8", props = "ruins", ceil = true, stal = true },
            new Theme { name = "Gluttony", sky = new[] { "#08120a", "#1a2a10", "#3a4a14" }, hasOrb = false, stars = 0, clouds = null,
                mount = new[] { "#18300f", "#122408", "#0c1a06" }, tile = new[] { "#3a4a2a", "#9acd4a", "#1e2a14", "#c6ff5a" },
                spike = "#e8ffc8", glow = "#9aff4a", fog = "#3a4a14", amb = "spore", ambC = "#b8ff6a", props = "cave", stal = true, ceil = true },
            new Theme { name = "Greed", sky = new[] { "#1a1204", "#5a3c08", "#c4901a" }, hasOrb = false, orbX = 80, orbY = 70, orbR = 18, orbC = "#fff3c4", orbGlow = "#ffd34d",
                stars = 0, clouds = null, mount = new[] { "#6a4a10", "#4a3208", "#342206" }, tile = new[] { "#6a5028", "#ffd34d", "#3a2a10", "#fff0a0" },
                spike = "#fff6d0", glow = "#ffd34d", fog = "#c4901a", amb = "spark", ambC = "#ffe27a", props = "rune", ceil = true, stal = true },
            new Theme { name = "Wrath", sky = new[] { "#100000", "#4a0606", "#e8300a" }, hasOrb = false, orbX = 160, orbY = 90, orbR = 30, orbC = "#ffb080", orbGlow = "#ff3a0a",
                stars = 0, clouds = "#b02a10", mount = new[] { "#3a0808", "#2a0505", "#1c0303" }, tile = new[] { "#4a2020", "#ff4a1a", "#241010", "#ff8a3a" },
                spike = "#ffd8c8", glow = "#ff5a1a", fog = "#e8300a", amb = "ember", ambC = "#ff7a2a", props = "ruins", ceil = true, stal = true },
            new Theme { name = "Heresy", sky = new[] { "#0a0614", "#2a0a4a", "#8a1ac0" }, hasOrb = false, orbX = 200, orbY = 60, orbR = 22, orbC = "#f0d0ff", orbGlow = "#c050ff",
                stars = 0, clouds = null, mount = new[] { "#3a1858", "#2a1040", "#1a0a2a" }, tile = new[] { "#3a2858", "#c86aff", "#1c1030", "#e8b0ff" },
                spike = "#f0d8ff", glow = "#c86aff", fog = "#7a1ab0", amb = "spark", ambC = "#d89aff", props = "rune", ceil = true, stal = true },
            new Theme { name = "Violence", sky = new[] { "#140606", "#50160c", "#b83a14" }, hasOrb = false, orbX = 0, orbY = 0, orbR = 0, orbC = "#ffffff", orbGlow = "#ff6a3a",
                stars = 0, clouds = "#8a2410", mount = new[] { "#4a1a12", "#341008", "#220a05" }, tile = new[] { "#4a3a38", "#ff6a3a", "#241c1a", "#ffa070" },
                spike = "#ffe0d0", glow = "#ff6a3a", fog = "#b83a14", amb = "ember", ambC = "#ff8a4a", props = "ruins", ceil = true, stal = true },
            new Theme { name = "Fraud", sky = new[] { "#02121a", "#08384a", "#1a8aa0" }, hasOrb = false, orbX = 120, orbY = 50, orbR = 16, orbC = "#e8ffff", orbGlow = "#6aeaff",
                stars = 0, clouds = null, mount = new[] { "#1a4a5a", "#103442", "#0a2230" }, tile = new[] { "#2a4a58", "#6aeaff", "#10242c", "#c0f8ff" },
                spike = "#d8ffff", glow = "#6aeaff", fog = "#1a8aa0", amb = "snow", ambC = "#d8ffff", props = "ice", ceil = true, stal = true },
            new Theme { name = "Treachery", sky = new[] { "#04060c", "#142038", "#4a6a9a" }, hasOrb = false, orbX = 170, orbY = 80, orbR = 26, orbC = "#f4f8ff", orbGlow = "#a0c0ff",
                stars = 0, clouds = null, mount = new[] { "#24344e", "#182438", "#0e1624" }, tile = new[] { "#34445e", "#cfe0ff", "#161e30", "#ffffff" },
                spike = "#ffffff", glow = "#a0c0ff", fog = "#4a6a9a", amb = "spore", ambC = "#d8e8ff", props = "cave", stal = true, ceil = true }
        };
        public static readonly int[] Zones = { 5, 6, 7, 8, 9, 10, 11, 12, 13 };

        // ---------------------------------------------------------- shop
        public static readonly Upgrade[] Upgrades =
        {
            new Upgrade("dash", "Double Dash", "One extra air dash", 1, 200),
            new Upgrade("reach", "Quick Draw", "Faster gun and coin cooldown", 3, 80, 160, 260),
            new Upgrade("magnet", "Coin Magnet", "Pulls nearby coins to you", 3, 60, 120, 200),
            new Upgrade("shield", "Shield", "Absorb one hit at the start of a run", 3, 100, 200, 300),
            new Upgrade("style", "Showoff", "+25% style points", 3, 90, 180, 270),
            new Upgrade("vit", "Vitality", "+25 max HP", 4, 80, 160, 260, 380)
        };

        // ---------------------------------------------------------- chapters
        static readonly string[] StageNames =
        {
            "INTO THE PIT", "THE MEATGRINDER", "DOUBLE DOWN", "ONE-MACHINE ARMY", "GATES OF LUST", "APPETITE", "SWALLOWED WHOLE", "FEASTING GROUNDS",
            "GOLD RUSH", "VAULT OF SINS", "MIDAS TOUCH", "BLOOD SKY", "THE LAST RAGE", "BURNING PYRE", "FALSE PROPHETS", "HERETIC'S LAMENT",
            "BONE CATHEDRAL", "RIVER OF BLOOD", "SLAUGHTERHOUSE", "IRON MAIDEN", "WALL OF FLESH", "FROZEN LIES", "MIRROR MAZE", "CITY OF TRICKS",
            "JUDAS PIT", "THE FINAL BETRAYAL"
        };
        public static readonly Stage[] Stages = BuildStages();
        // 26 stages: 0-1, 0-2, then three per world up to 8-3
        static Stage[] BuildStages()
        {
            var L = new List<Stage>();
            L.Add(new Stage("0-1  INTO THE PIT", 5, "Arrows / WASD move  -  Z or Space jump (hold = higher)",
                "flat", "gap5", "spikes", "cp", "steps", "gap2", "spikes", "cp", "gap5", "float"));
            L.Add(new Stage("0-2  THE MEATGRINDER", 5, "Click shoot / R-click coin / 1-5 or Tab: weapons. Shoot the coin under you to bounce",
                "spikes", "gap2", "float", "cp", "steps", "gauntlet", "gap5", "cp", "sky", "spikes", "low"));
            string[] easy = { "gap5", "gap2", "spikes", "steps", "float" };
            string[] mid = { "sky", "spikeField", "dashgap", "chain", "gauntlet", "low" };
            string[] hard = { "shaft", "pillars", "chain", "low", "spikeField", "dashgap", "sky", "gauntlet" };
            for (int i = 2; i < StageNames.Length; i++)
            {
                var r = new Rng(i * 131 + 7);
                int w = 1 + (i - 2) / 3, n = 1 + (i - 2) % 3, count = Math.Min(22, 8 + i / 2);
                var c = new List<string>(); string last = "";
                for (int k = 0; k < count; k++)
                {
                    if (k % 4 == 3) { c.Add("cp"); last = "cp"; continue; }
                    string[] pool = i < 5 ? (r.Next() < .5f ? easy : mid) : i < 12 ? (r.Next() < .25f ? easy : mid) : (r.Next() < .3f ? mid : hard);
                    string pick = pool[r.Ri(0, pool.Length - 1)];
                    int guard = 0; while (pick == last && guard++ < 6) pick = pool[r.Ri(0, pool.Length - 1)];
                    c.Add(pick); last = pick;
                }
                string hint = i == 2 ? "X / Shift = dash (3, regenerates).  Down + Jump in the air = SLAM" : i == 3 ? "Hold toward a wall to slide, press jump to wall-jump. Kill all to open gates" : i == 4 ? "Bosses fight in sealed arenas. Shotgun blast in the air pushes you back" : "";
                L.Add(new Stage(w + "-" + n + "  " + StageNames[i], 5 + Math.Min(8, i / 3), hint, c.ToArray()));
            }
            return L.ToArray();
        }

        // ---------------------------------------------------------- grid helpers (g[y][x], 20 cols x 23 rows, ground top = row 18)
        public static char[][] Mk()
        {
            var g = new char[ROWS][];
            for (int y = 0; y < ROWS; y++) g[y] = new string('.', CW).ToCharArray();
            return g;
        }
        public static void Fill(char[][] g, int x0, int x1, int y0, int y1, char c)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) g[y][x] = c;
        }
        public static void Ground(char[][] g) { Fill(g, 0, CW - 1, 18, 22, '#'); }
        public static void Pit(char[][] g, int x0, int x1) { Fill(g, x0, x1, 18, 22, '.'); Fill(g, x0, x1, 22, 22, '^'); }

        public static readonly Dictionary<string, Action<char[][]>> Chunks = new Dictionary<string, Action<char[][]>>
        {
            { "start", g => { Ground(g); g[17][3] = 'P'; } },
            { "end", g => { Ground(g); g[17][11] = 'G'; Fill(g, 15, 19, 16, 17, '#'); } },
            { "flat", g => Ground(g) },
            { "cp", g => { Ground(g); g[17][10] = 'C'; } },
            { "gap5", g => { Ground(g); Pit(g, 7, 11); } },
            { "gap2", g => { Ground(g); Pit(g, 4, 7); Pit(g, 12, 15); } },
            { "spikes", g => { Ground(g); Fill(g, 8, 10, 17, 17, '^'); } },
            { "steps", g => { Ground(g); Fill(g, 5, 6, 17, 17, '#'); Fill(g, 7, 8, 16, 17, '#'); Fill(g, 9, 10, 15, 17, '#'); Fill(g, 11, 12, 16, 17, '#'); Fill(g, 13, 14, 17, 17, '#'); } },
            { "float", g => { Ground(g); Pit(g, 3, 16); Fill(g, 5, 6, 16, 16, '#'); Fill(g, 9, 10, 15, 15, '#'); Fill(g, 13, 14, 16, 16, '#'); } },
            { "sky", g => { Ground(g); Pit(g, 3, 16); Fill(g, 4, 5, 16, 16, '#'); Fill(g, 8, 9, 13, 13, '#'); Fill(g, 12, 13, 10, 10, '#'); Fill(g, 15, 16, 14, 14, '#'); } },
            { "spikeField", g => { Ground(g); Fill(g, 6, 12, 17, 17, '^'); g[14][9] = 'o'; } },
            { "dashgap", g => { Ground(g); Pit(g, 5, 13); } },
            { "chain", g => { Ground(g); Pit(g, 2, 16); g[15][6] = 'o'; g[15][10] = 'o'; g[15][14] = 'o'; } },
            { "low", g => { Ground(g); Fill(g, 2, 17, 0, 11, '#'); Fill(g, 5, 7, 17, 17, '^'); Fill(g, 11, 13, 17, 17, '^'); Fill(g, 8, 10, 12, 12, 'v'); } },
            { "shaft", g => { Ground(g); Fill(g, 5, 8, 4, 15, '#'); Fill(g, 13, 16, 4, 17, '#'); } },
            { "gauntlet", g => { Ground(g); Fill(g, 4, 4, 17, 17, '^'); Pit(g, 8, 9); Fill(g, 13, 13, 17, 17, '^'); } },
            { "arena", g => { Ground(g); Fill(g, 3, 3, 4, 17, 'X'); Fill(g, 16, 16, 4, 17, 'X'); } },
            { "pack", g => Ground(g) },
            { "snipe", g => { Ground(g); Fill(g, 16, 16, 17, 17, '#'); Fill(g, 8, 10, 17, 17, '^'); } },
            { "bossA", g => { Ground(g); Fill(g, 2, 2, 0, 17, 'X'); Fill(g, 3, 19, 0, 2, '#'); Fill(g, 6, 9, 14, 14, '#'); Fill(g, 14, 17, 11, 11, '#'); } },
            { "bossB", g => { Ground(g); Fill(g, 17, 17, 0, 17, 'X'); Fill(g, 0, 16, 0, 2, '#'); Fill(g, 2, 5, 11, 11, '#'); Fill(g, 10, 13, 14, 14, '#'); } },
            { "pillars", g => { Ground(g); Pit(g, 2, 17); Fill(g, 4, 5, 16, 16, '#'); Fill(g, 8, 9, 15, 15, '#'); Fill(g, 12, 13, 16, 16, '#'); Fill(g, 16, 17, 16, 16, '#'); } }
        };

        // entity lists for hand-built chunk names (st = stage index)
        public static List<EntDef> Ents(string n, int st)
        {
            var E = new List<EntDef>();
            switch (n)
            {
                case "arena":
                    E.Add(new EntDef { t = "walker", x = 7, y = 18, l = 5, rr = 10, arena = true });
                    E.Add(new EntDef { t = "walker", x = 12, y = 18, l = 10, rr = 15, arena = true });
                    E.Add(new EntDef { t = "bat", x = 10, y = 13.5f, rx = 3, amp = 1, per = 170, arena = true });
                    if (st >= 3) E.Add(new EntDef { t = "bat", x = 8, y = 12.5f, rx = 3, amp = 1, per = 140, arena = true });
                    if (st >= 8) { E.Add(new EntDef { t = "walker", x = 9, y = 18, l = 6, rr = 13, arena = true }); E.Add(new EntDef { t = "bat", x = 12, y = 12f, rx = 3, amp = 1, per = 120, arena = true }); }
                    if (st >= 16) E.Add(new EntDef { t = "bat", x = 6, y = 11.5f, rx = 4, amp = 1.5f, per = 110, arena = true });
                    break;
                case "pack":
                    E.Add(new EntDef { t = "walker", x = 6, y = 18, l = 3, rr = 9 });
                    E.Add(new EntDef { t = "walker", x = 14, y = 18, l = 11, rr = 17 });
                    if (st >= 2) E.Add(new EntDef { t = "bat", x = 10, y = 13.5f, rx = 4, amp = 1, per = 180 });
                    break;
                case "snipe":
                    E.Add(new EntDef { t = "turret", x = 16, y = 17, dir = -1, per = 100 });
                    break;
                case "bossB":
                    E.Add(new EntDef { t = "boss", x = 7, y = 18, bk = st % 4, hp = 30 + st * 4 });
                    break;
            }
            return E;
        }

        // ---------------------------------------------------------- endless templates
        // every template keeps columns 0-1 and 18-19 as clear ground so chunks always connect
        public static readonly List<Tpl> Templates = BuildTemplates();

        static void Add(List<Tpl> L, string name, int min, int w, Action<char[][], Rng, List<EntDef>, int> fn, bool noCeil = false, int max = 9999)
        {
            L.Add(new Tpl { name = name, min = min, w = w, fn = fn, noCeil = noCeil, max = max });
        }

        static List<Tpl> BuildTemplates()
        {
            var L = new List<Tpl>();
            Add(L, "flat", 0, 2, (g, r, E, d) => Ground(g), false, 8);
            Add(L, "gap", 0, 4, (g, r, E, d) =>
            {
                Ground(g);
                int w = r.Ri(3, d < 6 ? 5 : d < 14 ? 6 : 7), x0 = r.Ri(4, 17 - w);
                Pit(g, x0, x0 + w - 1);
            });
            Add(L, "spikes", 0, 3, (g, r, E, d) =>
            {
                Ground(g);
                int n = d < 8 ? 1 : 2, x = r.Ri(4, 6);
                for (int i = 0; i < n; i++)
                {
                    int w = r.Ri(2, 3);
                    Fill(g, x, x + w - 1, 17, 17, '^');
                    x += w + r.Ri(4, 5);
                    if (x > 15) break;
                }
            });
            Add(L, "stairs", 1, 2, (g, r, E, d) =>
            {
                Ground(g);
                int h = r.Ri(1, 3);
                var hs = new List<int>();
                for (int s = 1; s <= h; s++) hs.Add(s);
                hs.Add(h);
                for (int s = h - 1; s >= 1; s--) hs.Add(s);
                int x = 4;
                foreach (int st in hs) { Fill(g, x, x + 1, 18 - st, 17, '#'); x += 2; }
                if (r.Next() < .5f) Fill(g, 15, 17, 17, 17, '^');
            });
            Add(L, "float", 2, 3, (g, r, E, d) =>
            {
                Ground(g); Pit(g, 3, 16);
                int y = 16, x = 4, wd = d < 12 ? 2 : 1, gap = d < 8 ? 2 : 3;
                while (x < 15)
                {
                    Fill(g, x, x + wd - 1, y, y, '#');
                    x += wd + gap;
                    y = Mathf.Clamp(y + r.Ri(-2, 1), 13, 17);
                }
            });
            Add(L, "pillars", 4, 2, (g, r, E, d) =>
            {
                Ground(g); Pit(g, 2, 17);
                int[] xs = { 4, 8, 12, 16 };
                foreach (int x in xs) { int y = r.Ri(14, 16); Fill(g, x, x + 1, y, y, '#'); }
            });
            Add(L, "low", 4, 2, (g, r, E, d) =>
            {
                Ground(g); Fill(g, 2, 17, 0, 11, '#'); Fill(g, 5, 7, 17, 17, '^'); Fill(g, 11, 13, 17, 17, '^'); Fill(g, 8, 10, 12, 12, 'v');
            });
            Add(L, "shaft", 5, 2, (g, r, E, d) => { Ground(g); Fill(g, 5, 8, 4, 15, '#'); Fill(g, 13, 16, 4, 17, '#'); }, true);
            Add(L, "chain", 5, 2, (g, r, E, d) =>
            {
                Ground(g); Pit(g, 2, 16);
                int[] xs = r.Next() < .5f ? new[] { 6, 11 } : new[] { 5, 9, 13 };
                foreach (int x in xs) g[15][x] = 'o';
            });
            Add(L, "spikeField", 6, 1, (g, r, E, d) => { Ground(g); Fill(g, 6, 12, 17, 17, '^'); g[14][9] = 'o'; });
            Add(L, "crumble", 3, 3, (g, r, E, d) =>
            {
                Ground(g); Pit(g, 2, 17);
                bool skipped = true;
                for (int x = 2; x <= 17; x++)
                {
                    if (!skipped && d > 8 && x > 4 && x < 15 && r.Next() < .18f) { skipped = true; continue; }
                    skipped = false; g[18][x] = 'c';
                }
            });
            Add(L, "movingPlat", 4, 3, (g, r, E, d) =>
            {
                Ground(g); Pit(g, 2, 17);
                if (d > 12 && r.Next() < .45f)
                {
                    E.Add(new EntDef { t = "plat", x = 6, y = 16, w = 2, ax = 0, ay = 1, per = 200, ph = 0 });
                    E.Add(new EntDef { t = "plat", x = 12, y = 16, w = 2, ax = 0, ay = 1, per = 200, ph = 3.14f });
                }
                else E.Add(new EntDef { t = "plat", x = 9.5f, y = 17, w = 2, ax = 5.5f, ay = 0, per = r.Ri(200, 280), ph = r.Next() * 6.28f });
            });
            Add(L, "saws", 3, 3, (g, r, E, d) =>
            {
                Ground(g);
                int n = d < 10 ? 1 : 2;
                for (int i = 0; i < n; i++)
                {
                    int cx = 6 + i * 7 + r.Ri(0, 1);
                    if (r.Next() < .5f) E.Add(new EntDef { t = "saw", x = cx, y = 17.5f, ax = 2.5f, ay = 0, per = r.Ri(110, 170), ph = r.Next() * 6.28f });
                    else E.Add(new EntDef { t = "saw", x = cx, y = 15.5f, ax = 0, ay = 2, per = r.Ri(100, 150), ph = r.Next() * 6.28f });
                }
            });
            Add(L, "walkers", 2, 4, (g, r, E, d) =>
            {
                Ground(g);
                int n = d < 12 ? r.Ri(1, 2) : r.Ri(2, 3);
                for (int i = 0; i < n; i++) { int x = 5 + i * 5 + r.Ri(0, 1); E.Add(new EntDef { t = "walker", x = x, y = 18, l = x - 3, rr = x + 3 }); }
            });
            Add(L, "bats", 3, 3, (g, r, E, d) =>
            {
                Ground(g);
                int n = d < 14 ? 1 : 2;
                for (int i = 0; i < n; i++) E.Add(new EntDef { t = "bat", x = 8 + i * 5, y = 14.5f, rx = 4, amp = 1.2f, per = r.Ri(160, 220) });
            });
            Add(L, "turret", 5, 2, (g, r, E, d) =>
            {
                Ground(g); Fill(g, 16, 16, 17, 17, '#');
                E.Add(new EntDef { t = "turret", x = 16, y = 17, dir = -1, per = d < 20 ? 110 : 80 });
                if (d > 10) Fill(g, 8, 9, 17, 17, '^');
            });
            Add(L, "arenaFight", 6, 2, (g, r, E, d) =>
            {
                Ground(g); Fill(g, 3, 3, 4, 17, 'X'); Fill(g, 16, 16, 4, 17, 'X');
                E.Add(new EntDef { t = "walker", x = 7, y = 18, l = 5, rr = 10, arena = true });
                E.Add(new EntDef { t = "walker", x = 12, y = 18, l = 10, rr = 15, arena = true });
                E.Add(new EntDef { t = "bat", x = 10, y = 13.5f, rx = 3, amp = 1, per = 170, arena = true });
                if (d > 14) E.Add(new EntDef { t = "bat", x = 8, y = 12.5f, rx = 3, amp = 1, per = 140, arena = true });
            }, true);
            Add(L, "mix", 6, 3, (g, r, E, d) =>
            {
                Ground(g); Pit(g, 8, 11); Fill(g, 14, 15, 17, 17, '^');
                E.Add(new EntDef { t = "walker", x = 4, y = 18, l = 3, rr = 6 });
                E.Add(new EntDef { t = "bat", x = 12, y = 13.5f, rx = 3, amp = 1, per = 180 });
            });
            return L;
        }

        public static void AddCoins(char[][] g, Rng r)
        {
            var runs = new List<int[]>();          // { kind (0 ground, 1 pit), x0, x1 }
            int[] cur = null;
            for (int x = 3; x <= 16; x++)
            {
                int kind = -1;
                if (g[18][x] == '.') kind = 1;
                else if (g[18][x] == '#' && g[17][x] == '.' && g[16][x] == '.' && g[15][x] == '.') kind = 0;
                if (kind >= 0 && cur != null && cur[0] == kind) cur[2] = x;
                else
                {
                    cur = kind >= 0 ? new[] { kind, x, x } : null;
                    if (cur != null) runs.Add(cur);
                }
            }
            var good = runs.FindAll(q => q[2] - q[1] >= 2);
            int n = Mathf.Min(good.Count, 2);
            for (int i = 0; i < n; i++)
            {
                int idx = Mathf.Min(good.Count - 1, (int)(r.Next() * good.Count));
                var q = good[idx]; good.RemoveAt(idx);
                int len = Mathf.Min(5, q[2] - q[1]);
                float mid = (q[1] + q[2]) / 2f;
                for (int j = 0; j < len; j++)
                {
                    int x = Mathf.FloorToInt(mid - (len - 1) / 2f + j + .5f);
                    int arc = Mathf.Abs(j - (len - 1) / 2f) < .6f ? 1 : 0;
                    int y = (q[0] == 0 ? 16 : 15) - arc;
                    if (x >= 0 && x < CW && g[y][x] == '.') g[y][x] = '$';
                }
            }
        }
    }
}
