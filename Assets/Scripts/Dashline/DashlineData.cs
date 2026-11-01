using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dashline
{
    // ------------------------------------------------------------------
    //  DASHLINE (Unity port) - data: themes, chunks, stages, level builder
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

    public class Crystal { public float x, y; public int cd; }
    public class Checkpoint { public float x, y, sx; public bool lit; }

    public class Level
    {
        public int index, cols;
        public float worldW;
        public char[][] map;
        public Theme th;
        public Vector2 spawn;
        public bool hasGoal;
        public int goalX, goalY;
        public List<Crystal> crystals = new List<Crystal>();
        public List<Checkpoint> cps = new List<Checkpoint>();

        public bool Solid(int tx, int ty)
        {
            if (tx < 0 || tx >= cols) return true;
            if (ty < 0 || ty >= Data.ROWS) return false;
            return map[ty][tx] == '#';
        }

        public static Level Build(int i)
        {
            const int T = Data.T, ROWS = Data.ROWS, CW = Data.CW;
            var st = Data.Stages[i];
            var lv = new Level { index = i, th = Data.Themes[st.theme] };

            // checkpoints are thinned out: only every second 'cp' chunk is kept
            int cpN = 0;
            var list = new List<string> { "start" };
            foreach (var n in st.chunks)
            {
                if (n == "cp") list.Add((cpN++ % 2 == 0) ? "cp" : "flat");
                else list.Add(n);
            }
            if (i >= 2) list.Add("gauntlet");
            if (i >= 4) list.Add("pillars");
            if (i >= 6) { list.Add("shaft"); list.Add("gauntlet"); }
            list.Add("end");

            lv.cols = list.Count * CW;
            lv.worldW = lv.cols * T;
            lv.map = new char[ROWS][];
            for (int y = 0; y < ROWS; y++) lv.map[y] = new string('.', lv.cols).ToCharArray();

            for (int k = 0; k < list.Count; k++)
            {
                var g = Data.Mk();
                Data.Chunks[list[k]](g);
                for (int y = 0; y < ROWS; y++)
                    for (int x = 0; x < CW; x++)
                        lv.map[y][k * CW + x] = g[y][x];
            }
            // caves get a jagged rock ceiling
            if (lv.th.ceil)
                for (int x = 0; x < lv.cols; x++)
                {
                    int hh = 2 + (int)(Art.Hash(x >> 1, 0, 77) * 3);
                    for (int y = 0; y < hh; y++) if (lv.map[y][x] == '.') lv.map[y][x] = '#';
                }


            for (int y = 0; y < ROWS; y++)
                for (int x = 0; x < lv.cols; x++)
                {
                    char c = lv.map[y][x];
                    if (c == 'o') { lv.crystals.Add(new Crystal { x = x * T + 4, y = y * T + 4 }); lv.map[y][x] = '.'; }
                    else if (c == 'P') { lv.spawn = new Vector2(x * T + 1, y * T); lv.map[y][x] = '.'; }
                    else if (c == 'C') { lv.cps.Add(new Checkpoint { x = x * T + 4, y = y * T, sx = x * T + 1 }); lv.map[y][x] = '.'; }
                    else if (c == 'G') { lv.hasGoal = true; lv.goalX = x; lv.goalY = y; lv.map[y][x] = '.'; }
                }
            return lv;
        }
    }

    public static class Data
    {
        public const int W = 320, H = 184, T = 8, ROWS = 23, CW = 20;

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
                spike = "#ffd6f6", glow = "#d08aff", fog = "#3c1a6c", amb = "spark", ambC = "#ffb8f2", props = "rune" }
        };

        // ---------------------------------------------------------- stages (no springs)
        public static readonly Stage[] Stages =
        {
            new Stage("Awakening", 1, "Arrows / WASD move  -  Z or Space jump (hold = higher)",
                "flat", "gap5", "spikes", "cp", "steps", "gap2", "spikes", "cp", "gap5", "float"),
            new Stage("Echo Tunnel", 1, "V / E = PARRY: press just before hitting a wall for a perfect bounce and big style",
                "spikes", "gap2", "float", "cp", "steps", "gauntlet", "gap5", "cp", "sky", "spikes", "low"),
            new Stage("First Dash", 0, "X / Shift = dash in 8 directions. Recharges on the ground and on crystals",
                "dashgap", "cp", "spikes", "spikeField", "gap5", "cp", "steps", "float", "dashgap", "sky"),
            new Stage("Sunlit Climb", 0, "Hold toward a wall to slide, press jump to wall-jump",
                "shaft", "cp", "gap2", "gauntlet", "dashgap", "cp", "low", "spikes", "chain", "sky"),
            new Stage("Burning Gate", 2, "",
                "low", "spikeField", "cp", "shaft", "float", "gauntlet", "cp", "dashgap", "gap2", "chain"),
            new Stage("Ashen Stair", 2, "",
                "chain", "spikes", "cp", "shaft", "low", "sky", "cp", "gauntlet", "spikeField", "dashgap", "shaft"),
            new Stage("Whiteout", 3, "",
                "sky", "shaft", "cp", "chain", "low", "pillars", "cp", "spikeField", "gap2", "shaft", "chain"),
            new Stage("Glacier Run", 3, "",
                "shaft", "pillars", "cp", "chain", "sky", "low", "cp", "dashgap", "shaft", "chain", "low"),
            new Stage("Starfall", 4, "",
                "chain", "low", "shaft", "cp", "spikeField", "sky", "pillars", "cp", "chain", "shaft", "low", "spikes"),
            new Stage("The Summit", 4, "One last climb. Breathe.",
                "shaft", "chain", "low", "cp", "sky", "spikeField", "pillars", "shaft", "cp", "chain", "low", "dashgap", "chain", "shaft")
        };

        // ---------------------------------------------------------- chunks (20 cols x 23 rows, ground top = row 18)
        public static char[][] Mk()
        {
            var g = new char[ROWS][];
            for (int y = 0; y < ROWS; y++) g[y] = new string('.', CW).ToCharArray();
            return g;
        }
        static void Fill(char[][] g, int x0, int x1, int y0, int y1, char c)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) g[y][x] = c;
        }
        static void Ground(char[][] g) { Fill(g, 0, CW - 1, 18, 22, '#'); }
        static void Pit(char[][] g, int x0, int x1) { Fill(g, x0, x1, 18, 22, '.'); Fill(g, x0, x1, 22, 22, '^'); }

        public static readonly Dictionary<string, Action<char[][]>> Chunks = new Dictionary<string, Action<char[][]>>
        {
            { "start", g => { Ground(g); g[17][3] = 'P'; } },
            { "end", g => { Ground(g); g[17][11] = 'G'; Fill(g, 15, 19, 16, 17, '#'); } },
            { "flat", g => Ground(g) },
            { "cp", g => { Ground(g); g[17][10] = 'C'; } },
            { "gap5", g => { Ground(g); Pit(g, 6, 12); } },
            { "gap2", g => { Ground(g); Pit(g, 3, 7); Pit(g, 11, 15); } },
            { "spikes", g => { Ground(g); Fill(g, 6, 8, 17, 17, '^'); Fill(g, 11, 13, 17, 17, '^'); } },
            { "steps", g => { Ground(g); Fill(g, 5, 6, 17, 17, '#'); Fill(g, 7, 8, 16, 17, '#'); Fill(g, 9, 10, 15, 17, '#'); Fill(g, 11, 12, 16, 17, '#'); Fill(g, 13, 14, 17, 17, '#'); Fill(g, 15, 16, 17, 17, '^'); } },
            { "float", g => { Ground(g); Pit(g, 3, 16); Fill(g, 5, 5, 16, 16, '#'); Fill(g, 8, 8, 14, 14, '#'); Fill(g, 11, 11, 15, 15, '#'); Fill(g, 14, 14, 16, 16, '#'); } },
            { "sky", g => { Ground(g); Pit(g, 3, 16); Fill(g, 4, 5, 16, 16, '#'); Fill(g, 8, 9, 13, 13, '#'); Fill(g, 12, 13, 10, 10, '#'); Fill(g, 15, 16, 14, 14, '#'); Fill(g, 8, 9, 11, 11, 'v'); } },
            { "spikeField", g => { Ground(g); Fill(g, 5, 13, 17, 17, '^'); g[14][7] = 'o'; g[14][11] = 'o'; } },
            { "dashgap", g => { Ground(g); Pit(g, 5, 14); } },
            { "chain", g => { Ground(g); Pit(g, 2, 17); g[15][5] = 'o'; g[15][10] = 'o'; g[15][15] = 'o'; } },
            { "low", g => { Ground(g); Fill(g, 2, 17, 0, 11, '#'); Fill(g, 4, 5, 17, 17, '^'); Fill(g, 8, 9, 17, 17, '^'); Fill(g, 12, 13, 17, 17, '^'); Fill(g, 6, 7, 12, 12, 'v'); Fill(g, 10, 11, 12, 12, 'v'); } },
            { "shaft", g => { Ground(g); Fill(g, 5, 8, 2, 15, '#'); Fill(g, 13, 16, 2, 17, '#'); } },
            { "gauntlet", g => { Ground(g); Fill(g, 3, 4, 17, 17, '^'); Pit(g, 7, 9); Fill(g, 12, 13, 17, 17, '^'); Fill(g, 16, 17, 17, 17, '^'); } },
            { "pillars", g => { Ground(g); Pit(g, 2, 17); Fill(g, 4, 4, 16, 16, '#'); Fill(g, 7, 7, 14, 14, '#'); Fill(g, 10, 10, 16, 16, '#'); Fill(g, 13, 13, 14, 14, '#'); Fill(g, 16, 16, 16, 16, '#'); } }
        };
    }
}
