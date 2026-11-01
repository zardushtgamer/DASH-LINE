using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Dashline
{
    // ==================================================================
    //  DASHLINE - precision platformer (Unity port)
    //  Drop the three scripts into Assets/ and press Play. No scene setup:
    //  the game bootstraps itself, builds all art in code, and creates its
    //  own camera if the scene has none.
    // ==================================================================
    public class DashlineGame : MonoBehaviour
    {
        const int W = Data.W, H = Data.H, T = Data.T, ROWS = Data.ROWS;
        const float STEP = 1f / 60f;

        enum State { Title, Play, Paused, Clear, Win }
        enum Act { Left, Right, Up, Down, Jump, Dash, Restart, Pause, Parry, Count }

        class PState
        {
            public float x, y, vx, vy, dx = 1, dy, sx = 1, sy = 1, anim, impact;
            public int w = 6, h = 8, face = 1, dashes = 1, dashT, coy, wcoy, wd, wdir, jb, db, lock_, dead, parryT, parryCd, parryVis, impactT, airT, nearCd;
            public bool ground;
            public Vector2[] hair = new Vector2[5];
        }
        class TrailPt { public float x, y; public int l; public Color c; }
        struct Part { public float x, y, vx, vy, l; public Color c; }

        // ------------------------------------------------ boot
#if UNITY_2023_1_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (FindFirstObjectByType<DashlineGame>() == null) new GameObject("Dashline").AddComponent<DashlineGame>(); }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (FindObjectOfType<DashlineGame>() == null) new GameObject("Dashline").AddComponent<DashlineGame>(); }
#endif

        // ------------------------------------------------ state
        Camera cam;
        Level lv;
        Theme th;
        PState p = new PState();
        State state = State.Title;
        int lvl, tick, deaths, time, sDeaths, sTime, flash, freeze, fade, clearT, introT, sel, unlocked;
        float shake, camX, acc;
        Vector2 spawn;
        readonly Part[] parts = new Part[300];
        int partI;
        readonly List<TrailPt> trail = new List<TrailPt>();
        readonly bool[] latch = new bool[(int)Act.Count];

        // ---- style meter
        static readonly string[] Ranks = { "D", "C", "B", "A", "S", "SS", "SSS" };
        static readonly int[] RankAt = { 0, 80, 170, 280, 400, 540, 700 };
        static readonly Color[] RankCol =
        {
            new Color(.60f, .64f, .78f), new Color(.48f, .88f, 1f), new Color(.49f, 1f, .69f), new Color(1f, .88f, .4f),
            new Color(1f, .64f, .28f), new Color(1f, .42f, .42f), new Color(1f, .30f, .82f)
        };
        class FeedItem { public string s; public int t; public Color c; }
        readonly List<FeedItem> feed = new List<FeedItem>();
        float styl;
        int styleTotal, sStyle, bestRank;
        SpriteRenderer parryRing;
        static int RankOf(float v) { int r = 0; for (int i = 0; i < RankAt.Length; i++) if (v >= RankAt[i]) r = i; return r; }
        void AddStyle(int n, string label = null)
        {
            int before = RankOf(styl);
            styl = Mathf.Min(799f, styl + n); styleTotal += n; sStyle += n;
            int r = RankOf(styl);
            if (r > before) { Sfx(500 + r * 120, 900 + r * 150, .15f, 2, .05f); feed.Add(new FeedItem { s = Ranks[r] + " RANK!", t = 70, c = RankCol[r] }); }
            if (r > bestRank) bestRank = r;
            if (label != null) feed.Add(new FeedItem { s = label + " +" + n, t = 70, c = RankCol[r] });
            while (feed.Count > 5) feed.RemoveAt(0);
        }

        // ambient particle seeds
        readonly float[] aX = new float[40], aY = new float[40], aPh = new float[40], aPar = new float[40], aSp = new float[40];
        readonly int[] aSz = new int[40];

        // ------------------------------------------------ renderers
        Transform poolRoot, stageRoot, playerRoot;
        SpriteRenderer skySR, fogSR, vigSR;
        readonly SpriteRenderer[] partSR = new SpriteRenderer[300], trailSR = new SpriteRenderer[24], glowSR = new SpriteRenderer[40],
            ambSR = new SpriteRenderer[40], scarfSR = new SpriteRenderer[5], cloudSR = new SpriteRenderer[6], pl = new SpriteRenderer[10];
        readonly List<SpriteRenderer> mtSR = new List<SpriteRenderer>(), stalSR = new List<SpriteRenderer>();
        readonly List<float> mtPar = new List<float>(), stalPar = new List<float>();
        readonly List<SpriteRenderer> crystalSR = new List<SpriteRenderer>(), cpSR = new List<SpriteRenderer>(), goalSpark = new List<SpriteRenderer>();
        SpriteRenderer goalSR;
        Sprite crystalSpr, lanternOn, lanternOff, portalSpr;
        readonly List<Sprite> stageSprites = new List<Sprite>();
        AudioSource audioSrc;
        GUIStyle style;
        float vpx, vpy, sc = 1f;

        static readonly Color ScarfRed = new Color(1f, .30f, .30f), ScarfBlue = new Color(.30f, .65f, 1f);
        static readonly Color Hood = new Color(.15f, .17f, .27f), Skin = new Color(.94f, .82f, .69f), Eye = new Color(.06f, .07f, .12f),
            Torso = new Color(.18f, .21f, .34f), Legs = new Color(.09f, .10f, .17f);

        // ================================================== setup
        void Awake()
        {
            Application.targetFrameRate = 60;
            unlocked = Mathf.Clamp(PlayerPrefs.GetInt("dashline_unlocked", 0), 0, Data.Stages.Length - 1);

            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Dashline Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.orthographic = true; cam.orthographicSize = H / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.transform.rotation = Quaternion.identity; cam.depth = 0; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;

            // black letterbox camera behind the main one
            var bg = new GameObject("Dashline Letterbox").AddComponent<Camera>();
            bg.clearFlags = CameraClearFlags.SolidColor; bg.backgroundColor = Color.black; bg.cullingMask = 0; bg.depth = -100;
            bg.transform.SetParent(transform, false);

            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;

            poolRoot = new GameObject("Pool").transform; poolRoot.SetParent(transform, false);
            crystalSpr = Art.CrystalSprite(); lanternOn = Art.LanternSprite(true); lanternOff = Art.LanternSprite(false); portalSpr = Art.PortalSprite();

            skySR = MakeSR(cam.transform, null, -100, new Vector3(-W / 2f, -H / 2f, 10f));
            fogSR = MakeSR(cam.transform, null, -80, new Vector3(-W / 2f, -H / 2f, 10f)); fogSR.transform.localScale = new Vector3(W, 1, 1);
            vigSR = MakeSR(cam.transform, Art.Vignette(), 40, new Vector3(-W / 2f, -H / 2f, 10f));

            for (int i = 0; i < partSR.Length; i++) partSR[i] = Quad(poolRoot, 10);
            for (int i = 0; i < trailSR.Length; i++) trailSR[i] = Quad(poolRoot, -10);
            for (int i = 0; i < glowSR.Length; i++) { glowSR[i] = MakeSR(poolRoot, Art.Glow(), 20, Vector3.zero); }
            for (int i = 0; i < ambSR.Length; i++) ambSR[i] = Quad(poolRoot, 30);
            for (int i = 0; i < scarfSR.Length; i++) scarfSR[i] = Quad(poolRoot, 0);
            for (int i = 0; i < cloudSR.Length; i++) cloudSR[i] = MakeSR(poolRoot, Art.CloudSprite(), -95, Vector3.zero);

            playerRoot = new GameObject("Player").transform; playerRoot.SetParent(transform, false);
            int[] ord = { 1, 2, 3, 4, 4, 1, 2, 1, 1, 1 };
            for (int i = 0; i < pl.Length; i++) pl[i] = Quad(playerRoot, ord[i]);
            parryRing = MakeSR(poolRoot, Art.RingSprite(), 5, Vector3.zero); parryRing.enabled = false;

            for (int i = 0; i < 40; i++)
            {
                aX[i] = Art.Hash(i, 1) * W; aY[i] = Art.Hash(i, 2) * H; aPh[i] = Art.Hash(i, 3) * 6.28f;
                aSz[i] = 1 + (int)(Art.Hash(i, 4) * 2); aPar[i] = .5f + Art.Hash(i, 5) * .9f; aSp[i] = .25f + Art.Hash(i, 6) * .7f;
            }

            LoadStage(0);
            state = State.Title;
        }

        SpriteRenderer Quad(Transform parent, int order)
        {
            var sr = MakeSR(parent, Art.Pixel(), order, Vector3.zero);
            sr.enabled = false; return sr;
        }
        SpriteRenderer MakeSR(Transform parent, Sprite s, int order, Vector3 localPos)
        {
            var go = new GameObject("sr");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.sortingOrder = order;
            return sr;
        }
        // place a quad using top-down pixel coords (x,y = top-left)
        static void Rc(SpriteRenderer sr, float x, float y, float w, float h)
        {
            var t = sr.transform;
            t.localPosition = new Vector3(x + w / 2f, -(y + h / 2f), 0f);
            t.localScale = new Vector3(w, h, 1f);
        }

        // ================================================== stage loading
        void LoadStage(int i)
        {
            lvl = i;
            lv = Level.Build(i);
            th = lv.th;

            if (stageRoot != null) Destroy(stageRoot.gameObject);
            foreach (var s in stageSprites) { if (s != null) { Destroy(s.texture); Destroy(s); } }
            stageSprites.Clear(); mtSR.Clear(); stalSR.Clear(); mtPar.Clear(); stalPar.Clear();
            crystalSR.Clear(); cpSR.Clear(); goalSpark.Clear(); goalSR = null;
            stageRoot = new GameObject("Stage").transform; stageRoot.SetParent(transform, false);

            skySR.sprite = Track(Art.Sky(th));
            fogSR.sprite = Track(Art.Fog(th));
            var layer = MakeSR(stageRoot, Track(Art.TileLayer(lv)), -50, new Vector3(0, -H, 0));
            layer.name = "Tiles";
            for (int k = 0; k < 3; k++) { mtSR.Add(MakeSR(stageRoot, Track(Art.Mountain(th, k, lv.worldW)), -90 + k, Vector3.zero)); mtPar.Add(Art.MountainParallax(k)); }
            if (th.stal) for (int k = 0; k < 2; k++) { stalSR.Add(MakeSR(stageRoot, Track(Art.Stalactites(th, k, lv.worldW)), -85 + k, Vector3.zero)); stalPar.Add(Art.StalParallax(k)); }

            foreach (var c in lv.crystals) { crystalSR.Add(MakeSR(stageRoot, crystalSpr, -40, Vector3.zero)); }
            foreach (var k in lv.cps) { var sr = MakeSR(stageRoot, lanternOff, -40, new Vector3(k.x, -(k.y + 8), 0)); cpSR.Add(sr); }
            if (lv.hasGoal)
            {
                goalSR = MakeSR(stageRoot, portalSpr, -40, new Vector3(lv.goalX * T + 4, -(lv.goalY * T - 6), 0));
                for (int k = 0; k < 5; k++) { var q = MakeSR(stageRoot, Art.Pixel(), -39, Vector3.zero); q.color = new Color(1f, .96f, .78f, .8f); q.transform.localScale = new Vector3(2, 2, 1); goalSpark.Add(q); }
            }
            Color cc = th.clouds != null ? Art.Hex(th.clouds, .55f) : Color.clear;
            for (int k = 0; k < cloudSR.Length; k++) { cloudSR[k].color = cc; cloudSR[k].enabled = th.clouds != null; }

            for (int k = 0; k < parts.Length; k++) parts[k].l = 0;
            sDeaths = 0; sTime = 0; introT = 240; fade = 20; styl = 0; sStyle = 0; bestRank = 0; feed.Clear();
            spawn = lv.spawn;
            Respawn();
            camX = Mathf.Clamp(spawn.x - W / 2f, 0, lv.worldW - W);
        }
        Sprite Track(Sprite s) { stageSprites.Add(s); return s; }

        void Respawn()
        {
            p = new PState { x = spawn.x, y = spawn.y };
            for (int i = 0; i < p.hair.Length; i++) p.hair[i] = new Vector2(spawn.x + 3, spawn.y + 2);
            foreach (var c in lv.crystals) c.cd = 0;
            trail.Clear();
        }

        // ================================================== input
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static bool Held(Act a)
        {
            var k = Keyboard.current; if (k == null) return false;
            switch (a)
            {
                case Act.Left: return k.leftArrowKey.isPressed || k.aKey.isPressed;
                case Act.Right: return k.rightArrowKey.isPressed || k.dKey.isPressed;
                case Act.Up: return k.upArrowKey.isPressed || k.wKey.isPressed;
                case Act.Down: return k.downArrowKey.isPressed || k.sKey.isPressed;
                case Act.Jump: return k.zKey.isPressed || k.spaceKey.isPressed || k.cKey.isPressed || k.kKey.isPressed || k.enterKey.isPressed;
                case Act.Dash: return k.xKey.isPressed || k.leftShiftKey.isPressed || k.rightShiftKey.isPressed || k.jKey.isPressed;
                case Act.Restart: return k.rKey.isPressed;
                case Act.Pause: return k.pKey.isPressed || k.escapeKey.isPressed;
                case Act.Parry: return k.vKey.isPressed || k.eKey.isPressed || k.leftCtrlKey.isPressed;
            }
            return false;
        }
        static bool PressedNow(Act a)
        {
            var k = Keyboard.current; if (k == null) return false;
            switch (a)
            {
                case Act.Left: return k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame;
                case Act.Right: return k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame;
                case Act.Up: return k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame;
                case Act.Down: return k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame;
                case Act.Jump: return k.zKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.cKey.wasPressedThisFrame || k.kKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame;
                case Act.Dash: return k.xKey.wasPressedThisFrame || k.leftShiftKey.wasPressedThisFrame || k.rightShiftKey.wasPressedThisFrame || k.jKey.wasPressedThisFrame;
                case Act.Restart: return k.rKey.wasPressedThisFrame;
                case Act.Pause: return k.pKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame;
                case Act.Parry: return k.vKey.wasPressedThisFrame || k.eKey.wasPressedThisFrame || k.leftCtrlKey.wasPressedThisFrame;
            }
            return false;
        }
#else
        static bool Held(Act a)
        {
            switch (a)
            {
                case Act.Left: return Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
                case Act.Right: return Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
                case Act.Up: return Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
                case Act.Down: return Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
                case Act.Jump: return Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.Return);
                case Act.Dash: return Input.GetKey(KeyCode.X) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.J);
                case Act.Restart: return Input.GetKey(KeyCode.R);
                case Act.Pause: return Input.GetKey(KeyCode.P) || Input.GetKey(KeyCode.Escape);
                case Act.Parry: return Input.GetKey(KeyCode.V) || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.LeftControl);
            }
            return false;
        }
        static bool PressedNow(Act a)
        {
            switch (a)
            {
                case Act.Left: return Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
                case Act.Right: return Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
                case Act.Up: return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
                case Act.Down: return Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
                case Act.Jump: return Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Return);
                case Act.Dash: return Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) || Input.GetKeyDown(KeyCode.J);
                case Act.Restart: return Input.GetKeyDown(KeyCode.R);
                case Act.Pause: return Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape);
                case Act.Parry: return Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.LeftControl);
            }
            return false;
        }
#endif
        bool Pressed(Act a) { return latch[(int)a]; }

        void OnApplicationFocus(bool focus) { if (!focus && state == State.Play) state = State.Paused; }

        // ================================================== audio
        void Sfx(float f0, float f1, float dur, int wave = 0, float vol = .05f, float delay = 0f)
        {
            const int sr = 22050;
            int total = Mathf.CeilToInt((dur + delay) * sr), start = Mathf.CeilToInt(delay * sr), n = total - start;
            if (n <= 0) return;
            var d = new float[total];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = f0 * Mathf.Pow(Mathf.Max(f1, 20f) / f0, t);
                ph += f / sr;
                float fr = (float)(ph - System.Math.Floor(ph));
                float s = wave == 0 ? (fr < .5f ? 1f : -1f) : wave == 1 ? (fr * 2f - 1f) : (Mathf.Abs(fr * 4f - 2f) - 1f);
                d[start + i] = s * vol * Mathf.Pow(.0001f / vol, t) * 4f;
            }
            var clip = AudioClip.Create("sfx", total, 1, sr, false);
            clip.SetData(d, 0);
            audioSrc.PlayOneShot(clip);
            Destroy(clip, dur + delay + .5f);
        }

        // ================================================== particles
        void Burst(float x, float y, int n, Color c, float sp = 1.5f, float up = 0f)
        {
            for (int i = 0; i < n; i++)
            {
                partI = (partI + 1) % parts.Length;
                parts[partI] = new Part { x = x, y = y, vx = (Random.value - .5f) * 2 * sp, vy = (Random.value - .5f) * 2 * sp - up, l = 20 + Random.value * 18, c = c };
            }
        }
        void Dust(float x, float y, int n = 5) { Burst(x, y, n, Art.Hex(th.tile[1]), .7f, .2f); }

        // ================================================== physics helpers
        bool Hits(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x / T), x1 = Mathf.FloorToInt((x + p.w - .001f) / T);
            int y0 = Mathf.FloorToInt(y / T), y1 = Mathf.FloorToInt((y + p.h - .001f) / T);
            for (int ty = y0; ty <= y1; ty++) for (int tx = x0; tx <= x1; tx++) if (lv.Solid(tx, ty)) return true;
            return false;
        }
        bool MoveX(float dx)
        {
            while (dx != 0f)
            {
                float s = Mathf.Abs(dx) < 1f ? dx : Mathf.Sign(dx);
                if (Hits(p.x + s, p.y)) { p.impact = Mathf.Abs(p.vx); p.impactT = 8; p.vx = 0; return true; }
                p.x += s; dx -= s;
            }
            return false;
        }
        bool MoveY(float dy)
        {
            while (dy != 0f)
            {
                float s = Mathf.Abs(dy) < 1f ? dy : Mathf.Sign(dy);
                if (Hits(p.x, p.y + s))
                {
                    if (s < 0f)
                    {
                        bool ok = false;
                        int[] nudge = { 1, 2, -1, -2 };
                        foreach (int n in nudge)
                            if (!Hits(p.x + n, p.y + s) && !Hits(p.x + n, p.y)) { p.x += n; ok = true; break; }
                        if (ok) { p.y += s; dy -= s; continue; }
                    }
                    p.vy = 0; return true;
                }
                p.y += s; dy -= s;
            }
            return false;
        }
        static float Approach(float v, float t, float a) { return v < t ? Mathf.Min(v + a, t) : Mathf.Max(v - a, t); }

        void Die()
        {
            if (p.dead > 0) return;
            deaths++; sDeaths++; styl = 0; p.dead = 32; freeze = 0; shake = 8; flash = 5;
            Burst(p.x + 3, p.y + 4, 26, new Color(1f, .35f, .48f), 1.9f);
            Burst(p.x + 3, p.y + 4, 10, Color.white, 1.2f);
            Sfx(220, 40, .3f, 1, .08f);
        }
        void FinishStage()
        {
            state = State.Clear; clearT = 130; freeze = 0;
            Sfx(523, 523, .12f, 2, .07f); Sfx(659, 659, .12f, 2, .07f, .1f); Sfx(784, 784, .12f, 2, .07f, .2f); Sfx(1046, 1046, .35f, 2, .07f, .3f);
            Burst(lv.goalX * T + 4, lv.goalY * T - 4, 40, new Color(1f, .83f, .3f), 2.2f);
            if (lvl + 1 > unlocked && lvl + 1 < Data.Stages.Length) { unlocked = lvl + 1; PlayerPrefs.SetInt("dashline_unlocked", unlocked); PlayerPrefs.Save(); }
        }

        void CheckWorld()
        {
            int x0 = Mathf.FloorToInt(p.x / T), x1 = Mathf.FloorToInt((p.x + p.w - .001f) / T);
            int y0 = Mathf.FloorToInt(p.y / T), y1 = Mathf.FloorToInt((p.y + p.h - .001f) / T);
            for (int ty = y0; ty <= y1; ty++)
                for (int tx = x0; tx <= x1; tx++)
                {
                    if (tx < 0 || tx >= lv.cols || ty < 0 || ty >= ROWS) continue;
                    char c = lv.map[ty][tx];
                    if (c == '^' || c == 'v')
                    {
                        float sy = c == '^' ? ty * T + 4 : ty * T;
                        if (p.x + 5.5f > tx * T && p.x + .5f < tx * T + T && p.y + 7.5f > sy && p.y + .5f < sy + 4) { Die(); return; }
                    }
                }
            if (p.nearCd > 0) p.nearCd--;
            else
                for (int ty = y0 - 1; ty <= y1 + 1; ty++)
                    for (int tx = x0 - 1; tx <= x1 + 1; tx++)
                    {
                        if (tx < 0 || tx >= lv.cols || ty < 0 || ty >= ROWS) continue;
                        char c2 = lv.map[ty][tx];
                        if (c2 != '^' && c2 != 'v') continue;
                        float sy2 = c2 == '^' ? ty * T + 4 : ty * T;
                        float ox = Mathf.Max(tx * T - (p.x + 5.5f), (p.x + .5f) - (tx * T + T)), oy = Mathf.Max(sy2 - (p.y + 7.5f), (p.y + .5f) - (sy2 + 4));
                        float gap = Mathf.Max(ox, oy);
                        if (gap > 0 && gap <= 2) { AddStyle(15, "CLOSE CALL"); p.nearCd = 40; }
                    }
            if (lv.hasGoal && Mathf.Abs(p.x + 3 - (lv.goalX * T + 4)) < 9 && Mathf.Abs(p.y + 4 - lv.goalY * T) < 14) { FinishStage(); return; }
            if (p.y > H + 8) { Die(); return; }
            foreach (var c in lv.crystals)
            {
                if (c.cd > 0) { c.cd--; continue; }
                if (p.dashes < 1 && Vector2.Distance(new Vector2(p.x + 3, p.y + 4), new Vector2(c.x, c.y)) < 11)
                {
                    p.dashes = 1; c.cd = 150; Burst(c.x, c.y, 12, new Color(.49f, 1f, .69f), 1.3f);
                    Sfx(700, 1400, .12f, 2, .06f); AddStyle(40, "CRYSTAL");
                }
            }
            foreach (var k in lv.cps)
            {
                if (!k.lit && Mathf.Abs(p.x + 3 - k.x) < 10 && Mathf.Abs(p.y + 8 - k.y) < 14)
                {
                    k.lit = true; spawn = new Vector2(k.sx, k.y);
                    Burst(k.x, k.y - 14, 14, new Color(1f, .83f, .3f), 1.2f, .5f);
                    Sfx(660, 990, .18f, 2, .06f); AddStyle(40, "CHECKPOINT");
                }
            }
        }

        // ================================================== main loop
        void Update()
        {
            // letterbox to 320:184
            float target = (float)W / H, win = (float)Screen.width / Mathf.Max(1, Screen.height);
            cam.rect = win > target ? new Rect((1 - target / win) / 2f, 0, target / win, 1) : new Rect(0, (1 - win / target) / 2f, 1, win / target);

            for (int a = 0; a < (int)Act.Count; a++) if (PressedNow((Act)a)) latch[a] = true;
            acc += Mathf.Min(Time.unscaledDeltaTime, .1f);
            while (acc >= STEP)
            {
                Step();
                for (int a = 0; a < latch.Length; a++) latch[a] = false;
                acc -= STEP;
            }
            Render();
        }

        void Step()
        {
            tick++;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].l <= 0) continue;
                parts[i].x += parts[i].vx; parts[i].y += parts[i].vy; parts[i].vy += .05f; parts[i].l--;
            }
            for (int i = trail.Count - 1; i >= 0; i--) { trail[i].l--; if (trail[i].l <= 0) trail.RemoveAt(i); }
            for (int i = feed.Count - 1; i >= 0; i--) { feed[i].t--; if (feed[i].t <= 0) feed.RemoveAt(i); }
            if (shake > 0) shake = Mathf.Max(0, shake - .5f);
            if (flash > 0) flash--;
            if (fade > 0) fade--;

            if (state == State.Title)
            {
                camX = (tick * .35f) % Mathf.Max(1f, lv.worldW - W);
                if (Pressed(Act.Left)) sel = Mathf.Max(0, sel - 1);
                if (Pressed(Act.Right)) sel = Mathf.Min(unlocked, sel + 1);
                if (sel != lvl && (Pressed(Act.Left) || Pressed(Act.Right))) LoadStage(sel);
                if (Pressed(Act.Jump)) { deaths = 0; time = 0; styleTotal = 0; LoadStage(sel); state = State.Play; Sfx(400, 800, .15f, 0, .05f); }
                return;
            }
            if (state == State.Win)
            {
                camX = (tick * .35f) % Mathf.Max(1f, lv.worldW - W);
                if (Pressed(Act.Jump) || Pressed(Act.Restart)) { sel = 0; LoadStage(0); state = State.Title; }
                return;
            }
            if (state == State.Paused) { if (Pressed(Act.Pause) || Pressed(Act.Jump)) state = State.Play; return; }
            if (state == State.Clear)
            {
                if (--clearT <= 0)
                {
                    if (lvl + 1 >= Data.Stages.Length) state = State.Win;
                    else { LoadStage(lvl + 1); state = State.Play; }
                }
                return;
            }
            if (Pressed(Act.Pause)) { state = State.Paused; return; }
            if (introT > 0) introT--;
            if (p.dead > 0) { if (--p.dead == 0) { Respawn(); fade = 10; } return; }
            if (freeze > 0) { freeze--; return; }
            if (Pressed(Act.Restart)) { Respawn(); return; }
            time++; sTime++;
            if (p.parryCd > 0) p.parryCd--;
            if (p.impactT > 0) p.impactT--;
            if (p.parryVis > 0) p.parryVis--;
            styl = Mathf.Max(0f, styl - (.22f + RankOf(styl) * .07f) * ((p.ground && Mathf.Abs(p.vx) < .2f) ? 2f : 1f));
            if (!p.ground) { p.airT++; if (p.airT % 24 == 0) AddStyle(8); } else p.airT = 0;

            int ix = (Held(Act.Right) ? 1 : 0) - (Held(Act.Left) ? 1 : 0);
            int iy = (Held(Act.Down) ? 1 : 0) - (Held(Act.Up) ? 1 : 0);
            bool jumpHeld = Held(Act.Jump);
            if (ix != 0) p.face = ix;
            p.sx = Approach(p.sx, 1, .06f); p.sy = Approach(p.sy, 1, .06f);

            p.ground = Hits(p.x, p.y + 1);
            bool r = Hits(p.x + 1, p.y), l = Hits(p.x - 1, p.y);
            p.wdir = (r && l) ? ix : (r ? 1 : l ? -1 : 0);
            if (p.ground) { p.coy = 5; if (p.dashT <= 0) p.dashes = 1; } else p.coy--;
            if (!p.ground && p.wdir != 0) { p.wcoy = 5; p.wd = p.wdir; } else p.wcoy--;
            p.jb = Pressed(Act.Jump) ? 5 : p.jb - 1;
            p.db = Pressed(Act.Dash) ? 5 : p.db - 1;

            // ---- parry: press just before (or just after) hitting a wall to bounce off it
            if (Pressed(Act.Parry) && p.parryCd <= 0) { p.parryT = 10; p.parryCd = 22; p.parryVis = 8; Sfx(900, 500, .06f, 0, .04f); }
            if (p.parryT > 0)
            {
                p.parryT--;
                if (p.wdir != 0)
                {
                    bool perfect = p.impactT > 0 && p.impact > 1.1f;
                    int pd = -p.wdir;
                    p.vx = pd * (perfect ? 2.8f : 2.0f); p.vy = perfect ? -3.0f : -2.3f; p.face = pd; p.lock_ = 10; p.dashT = 0; p.dashes = 1;
                    p.parryT = 0; p.parryCd = 6; p.impactT = 0; p.sx = .7f; p.sy = 1.3f; p.ground = false; p.coy = 0;
                    freeze = perfect ? 5 : 2; shake = perfect ? 5 : 2;
                    Burst(p.x + (pd > 0 ? 0 : 6), p.y + 4, perfect ? 22 : 10, perfect ? Color.white : new Color(1f, .83f, .3f), perfect ? 2f : 1.2f);
                    Sfx(perfect ? 900 : 600, perfect ? 1800 : 1000, .18f, 0, .06f);
                    AddStyle(perfect ? 120 : 45, perfect ? "PERFECT PARRY" : "PARRY");
                }
            }

            // ---- dash start
            if (p.db > 0 && p.dashes > 0 && p.dashT <= 0)
            {
                p.dashes--; p.db = 0;
                float dx = ix, dy = iy;
                if (dx == 0 && dy == 0) dx = p.face;
                float m = Mathf.Sqrt(dx * dx + dy * dy); dx /= m; dy /= m;
                p.dx = dx; p.dy = dy; p.dashT = 8; p.vx = dx * 3.6f; p.vy = dy * 3.6f; freeze = 3; shake = 3; p.lock_ = 0;
                bool horiz = Mathf.Abs(dx) > .1f;
                p.sx = horiz ? 1.4f : .8f; p.sy = horiz ? .75f : 1.4f;
                Sfx(420, 90, .18f, 1, .06f); Burst(p.x + 3, p.y + 4, 10, Color.white, 1.1f); AddStyle(p.ground ? 10 : 20, "DASH");
            }

            // ---- dashing
            if (p.dashT > 0 && !(p.jb > 0 && p.coy > 0))
            {
                p.dashT--;
                trail.Add(new TrailPt { x = p.x, y = p.y, l = 12, c = p.dashes > 0 ? ScarfRed : ScarfBlue });
                if (tick % 2 == 0) Burst(p.x + 3, p.y + 4, 1, new Color(.75f, .9f, 1f), .3f);
                MoveX(p.vx); MoveY(p.vy);
                if (p.dashT == 0) { p.vx = p.dx * 1.8f; p.vy = p.dy * (p.dy < 0 ? 1.3f : 1.8f); }
                UpdateHair(); CheckWorld(); UpdateCam();
                return;
            }
            p.dashT = 0;

            // ---- horizontal
            if (p.lock_ > 0) p.lock_--;
            else
            {
                float maxR = 1.45f, accel = p.ground ? .28f : .16f;
                if (Mathf.Abs(p.vx) > maxR && Mathf.Sign(p.vx) == ix) p.vx = Approach(p.vx, ix * maxR, .04f);
                else p.vx = Approach(p.vx, ix * maxR, ix != 0 ? accel : (p.ground ? .35f : .08f));
            }

            // ---- gravity / wall slide
            float g = .13f;
            if (Mathf.Abs(p.vy) < .5f && jumpHeld) g *= .5f;
            if (p.vy < 0 && !jumpHeld) g *= 2f;
            p.vy = Mathf.Min(p.vy + g, 3.4f);
            bool sliding = !p.ground && p.wdir != 0 && ix == p.wdir && p.vy > 0;
            if (sliding) { p.vy = Mathf.Min(p.vy, .7f); if (tick % 4 == 0) Dust(p.x + (p.wdir > 0 ? 6 : 0), p.y + 6, 1); }

            // ---- jump / wall jump
            if (p.jb > 0)
            {
                if (p.coy > 0)
                {
                    p.vy = -2.7f; p.vx += ix * .25f; p.jb = 0; p.coy = 0; p.sx = .7f; p.sy = 1.3f;
                    Sfx(300, 600, .12f, 0, .04f); Dust(p.x + 3, p.y + 8, 6);
                }
                else if (p.wcoy > 0)
                {
                    int d = -p.wd;
                    p.vx = d * 1.9f; p.vy = -2.5f; p.jb = 0; p.wcoy = 0; p.face = d; p.lock_ = 8; p.sx = .75f; p.sy = 1.25f;
                    Sfx(350, 700, .12f, 0, .04f); Dust(p.x + (d > 0 ? 0 : 6), p.y + 4, 6); AddStyle(30, "WALL JUMP");
                }
            }

            MoveX(p.vx);
            float vyB = p.vy;
            if (MoveY(p.vy) && vyB > 1.8f) { Dust(p.x + 3, p.y + 8, 6); Sfx(120, 60, .06f, 2, .05f); p.sx = 1.35f; p.sy = .7f; }
            if (p.ground && Mathf.Abs(p.vx) > .8f) { p.anim += Mathf.Abs(p.vx) * .16f; if (tick % 9 == 0) Dust(p.x + 3, p.y + 8, 1); }
            else if (!p.ground) p.anim = 0;

            UpdateHair(); CheckWorld(); UpdateCam();
        }

        void UpdateHair()
        {
            float px = p.x + 3 - p.face * 2, py = p.y + 2;
            for (int i = 0; i < p.hair.Length; i++)
            {
                var h = p.hair[i];
                h.x = Mathf.Lerp(h.x, px - p.face * 1.2f, .45f); h.y = Mathf.Lerp(h.y, py + .8f, .45f);
                p.hair[i] = h; px = h.x; py = h.y;
            }
        }
        void UpdateCam()
        {
            float tx = Mathf.Clamp(p.x + 3 - W / 2f + p.face * 22, 0, lv.worldW - W);
            camX += (tx - camX) * .1f;
        }

        // ================================================== rendering
        bool Vis(float x) { return x > camX - 40 && x < camX + W + 40; }
        int glowN;
        void GlowAt(float x, float y, float r, Color col, float a)
        {
            if (glowN >= glowSR.Length) return;
            var sr = glowSR[glowN++];
            sr.enabled = true; sr.color = new Color(col.r, col.g, col.b, a);
            sr.transform.position = new Vector3(x, -y, 0); sr.transform.localScale = Vector3.one * (r * 2f / 64f);
        }

        void Render()
        {
            float ox = 0, oy = 0;
            if (shake > 0) { ox = Mathf.Round((Random.value - .5f) * shake); oy = Mathf.Round((Random.value - .5f) * shake); }
            float cx = Mathf.Round(camX);
            cam.transform.position = new Vector3(cx + W / 2f + ox, -H / 2f + oy, -10f);

            // parallax
            for (int i = 0; i < mtSR.Count; i++) mtSR[i].transform.position = new Vector3(cx * (1f - mtPar[i]), -H, 0);
            for (int i = 0; i < stalSR.Count; i++) stalSR[i].transform.position = new Vector3(cx * (1f - stalPar[i]), -H, 0);
            if (th.clouds != null)
                for (int i = 0; i < cloudSR.Length; i++)
                {
                    float sp = .5f + (i % 3) * .3f;
                    float x = Mathf.Repeat(i * 83 - camX * .06f - tick * .06f * sp, W + 90) - 45, y = 14 + i * 13;
                    cloudSR[i].transform.position = new Vector3(cx + x - 10, -(y + 10), 0);
                }

            glowN = 0;
            bool inGame = state == State.Play || state == State.Paused || state == State.Clear;

            // crystals
            for (int i = 0; i < lv.crystals.Count; i++)
            {
                var c = lv.crystals[i]; var sr = crystalSR[i];
                float bob = Mathf.Sin(tick * .08f + c.x) * 1.4f;
                sr.transform.position = new Vector3(c.x, -(c.y + bob), 0);
                sr.color = c.cd > 0 ? new Color(1, 1, 1, .2f) : Color.white;
                if (c.cd <= 0 && Vis(c.x)) GlowAt(c.x, c.y, 26, new Color(.25f, 1f, .63f), .4f);
            }
            // checkpoints
            for (int i = 0; i < lv.cps.Count; i++)
            {
                var k = lv.cps[i];
                cpSR[i].sprite = k.lit ? lanternOn : lanternOff;
                if (k.lit && Vis(k.x)) GlowAt(k.x, k.y - 11, 28, new Color(1f, .83f, .3f), .45f);
            }
            // goal
            if (lv.hasGoal && goalSR != null)
            {
                float gx = lv.goalX * T + 4, gy = lv.goalY * T - 6;
                for (int i = 0; i < goalSpark.Count; i++)
                {
                    float a = tick * .05f + i * 1.26f;
                    goalSpark[i].transform.position = new Vector3(gx + Mathf.Cos(a) * 6, -(gy + Mathf.Sin(a) * 11), 0);
                }
                if (Vis(gx)) GlowAt(gx, gy, 40, new Color(1f, .83f, .3f), .5f);
            }

            // player
            bool show = inGame && p.dead <= 0;
            playerRoot.gameObject.SetActive(show);
            foreach (var s in scarfSR) s.enabled = show;
            if (show)
            {
                bool fl = p.dashT > 0, run = p.ground && Mathf.Abs(p.vx) > .5f;
                int f = p.face, step = Mathf.FloorToInt(p.anim) % 2;
                int bob = (p.ground && !run) ? (Mathf.Sin(tick * .08f) > .4f ? 1 : 0) : 0;
                Color scarf = p.dashes > 0 ? ScarfRed : ScarfBlue;
                playerRoot.position = new Vector3(Mathf.Round(p.x + 3), -Mathf.Round(p.y + 8), 0);
                playerRoot.localScale = new Vector3(p.sx, p.sy, 1);
                Color W_ = Color.white;
                pl[0].enabled = true; Rc(pl[0], -3, -8 + bob, 6, 4); pl[0].color = fl ? W_ : Hood;
                pl[1].enabled = true; Rc(pl[1], -3, -7 + bob, 6, 1); pl[1].color = fl ? W_ : scarf;
                pl[2].enabled = true; Rc(pl[2], f > 0 ? -1 : -3, -6 + bob, 4, 2); pl[2].color = fl ? W_ : Skin;
                int ex = f > 0 ? 0 : -2;
                pl[3].enabled = true; Rc(pl[3], ex, -6 + bob, 1, 1); pl[3].color = Eye;
                pl[4].enabled = true; Rc(pl[4], ex + 2, -6 + bob, 1, 1); pl[4].color = Eye;
                pl[5].enabled = true; Rc(pl[5], -3, -4, 6, 3); pl[5].color = fl ? W_ : Torso;
                pl[6].enabled = true; Rc(pl[6], -3, -3, 6, 1); pl[6].color = fl ? W_ : scarf;
                pl[7].enabled = true; pl[8].enabled = true; pl[9].enabled = false;
                if (run) { Rc(pl[7], -3 + step, -1, 2, 1); Rc(pl[8], 1 - step, -1, 2, 1); }
                else { Rc(pl[7], -3, -1, 2, 1); Rc(pl[8], 1, -1, 2, 1); }
                pl[7].color = fl ? W_ : Legs; pl[8].color = fl ? W_ : Legs;

                float[] sizes = { 3f, 2.6f, 2.2f, 1.6f, 1.1f };
                for (int i = 0; i < scarfSR.Length; i++)
                {
                    var h = p.hair[i];
                    Rc(scarfSR[i], Mathf.Round(h.x - sizes[i] / 2f), Mathf.Round(h.y - sizes[i] / 2f), Mathf.Ceil(sizes[i]), Mathf.Ceil(sizes[i]));
                    scarfSR[i].color = fl ? W_ : scarf;
                }
                GlowAt(p.x + 3, p.y + 4, 46, Art.Hex(th.glow), .22f);
                if (fl) GlowAt(p.x + 3, p.y + 4, 30, Color.white, .25f);
            }

            if (show && p.parryVis > 0)
            {
                parryRing.enabled = true;
                parryRing.transform.position = new Vector3(p.x + 3, -(p.y + 4), 0);
                float rr = 6 + (8 - p.parryVis) * .9f;
                parryRing.transform.localScale = Vector3.one * (rr * 2f / 32f);
                parryRing.color = new Color(1, 1, 1, p.parryVis / 8f);
            }
            else parryRing.enabled = false;

            // dash trail
            for (int i = 0; i < trailSR.Length; i++)
            {
                if (i < trail.Count && inGame)
                {
                    var t = trail[i]; trailSR[i].enabled = true;
                    Rc(trailSR[i], Mathf.Round(t.x), Mathf.Round(t.y), 6, 8);
                    trailSR[i].color = new Color(t.c.r, t.c.g, t.c.b, t.l / 16f);
                }
                else trailSR[i].enabled = false;
            }
            // particles
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].l > 0)
                {
                    partSR[i].enabled = true; Rc(partSR[i], Mathf.Round(parts[i].x), Mathf.Round(parts[i].y), 2, 2);
                    var c = parts[i].c; partSR[i].color = new Color(c.r, c.g, c.b, Mathf.Min(1f, parts[i].l / 14f));
                }
                else partSR[i].enabled = false;
            }
            for (int i = glowN; i < glowSR.Length; i++) glowSR[i].enabled = false;

            // ambient
            Color ac = Art.Hex(th.ambC);
            for (int i = 0; i < ambSR.Length; i++)
            {
                float x = aX[i] - camX * aPar[i] * .5f, y = aY[i];
                switch (th.amb)
                {
                    case "fire": x += Mathf.Sin(tick * .02f + aPh[i]) * 10; y = aY[i] * .6f + 70 + Mathf.Sin(tick * .03f + aPh[i] * 2) * 10; break;
                    case "spore": y = Mathf.Repeat(aY[i] - tick * aSp[i] * .3f, H); x += Mathf.Sin(tick * .02f + aPh[i]) * 6; break;
                    case "ember": y = Mathf.Repeat(aY[i] - tick * aSp[i] * .9f, H); x += Mathf.Sin(tick * .05f + aPh[i]) * 8; break;
                    case "snow": y = Mathf.Repeat(aY[i] + tick * aSp[i] * 1.1f, H); x += Mathf.Sin(tick * .03f + aPh[i]) * 6 + tick * .25f * aSp[i]; break;
                    case "spark": y = aY[i] + Mathf.Sin(tick * .02f + aPh[i]) * 6; break;
                }
                x = Mathf.Repeat(x, W);
                float tw = .45f + .55f * Mathf.Abs(Mathf.Sin(tick * .05f + aPh[i]));
                int sz = aSz[i] > 2 ? 2 : 1;
                ambSR[i].enabled = true;
                Rc(ambSR[i], cx + Mathf.Round(x), Mathf.Round(y), sz, sz);
                ambSR[i].color = new Color(ac.r, ac.g, ac.b, th.amb == "snow" ? .85f : tw);
            }
        }

        // ================================================== UI (IMGUI, drawn in 320x184 virtual space)
        void Box(float x, float y, float w, float h, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(vpx + x * sc, vpy + y * sc, w * sc, h * sc), Texture2D.whiteTexture);
            GUI.color = old;
        }
        void Txt(string s, float x, float y, Color col, float size, TextAnchor a)
        {
            style.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * sc * 1.15f)); style.alignment = a;
            float px = vpx + x * sc, py = vpy + y * sc, bw = 1200f;
            Rect r = a == TextAnchor.UpperLeft ? new Rect(px, py, bw, 400) : a == TextAnchor.UpperRight ? new Rect(px - bw, py, bw, 400) : new Rect(px - bw / 2f, py, bw, 400);
            style.normal.textColor = new Color(0, 0, 0, .65f * col.a);
            GUI.Label(new Rect(r.x + sc, r.y + sc, r.width, r.height), s, style);
            style.normal.textColor = col;
            GUI.Label(r, s, style);
        }
        static string Fmt(int t) { float s = t / 60f; return ((int)(s / 60f)).ToString("00") + ":" + (s % 60f).ToString("00.0"); }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || cam == null || lv == null) return;
            if (style == null) { style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow }; }
            Rect pr = cam.pixelRect;
            sc = pr.height / H; vpx = pr.x; vpy = Screen.height - pr.yMax;
            Color white = Color.white, cyan = new Color(.8f, .84f, 1f), gold = new Color(1f, .83f, .3f);
            var C = TextAnchor.UpperCenter; var Lf = TextAnchor.UpperLeft; var Rt = TextAnchor.UpperRight;

            if (flash > 0) Box(0, 0, W, H, new Color(1, 1, 1, flash * .06f));

            if (state == State.Title)
            {
                Box(0, 0, W, H, new Color(0, 0, 0, .35f));
                float bob = Mathf.Sin(tick * .04f) * 2f;
                Txt("DASHLINE", W / 2f, 38 + bob, white, 28, C);
                Box(W / 2f - 60, 70 + bob, 120, 2, new Color(1f, .35f, .35f));
                Txt("a precision platformer", W / 2f, 78, new Color(.9f, .93f, 1f), 8, C);
                Txt("< STAGE " + (sel + 1) + ": " + Data.Stages[sel].name + " >", W / 2f, 104, gold, 10, C);
                Txt(Data.Themes[Data.Stages[sel].theme].name, W / 2f, 117, new Color(.75f, .8f, 1f), 8, C);
                if ((tick / 30) % 2 == 0) Txt("Press Z / Space / Enter to play", W / 2f, 138, white, 8, C);
                Txt("Arrows move  Z jump  X dash  V parry  R retry  P pause", W / 2f, H - 14, new Color(.67f, .71f, .91f), 8, C);
            }
            else
            {
                Box(0, 0, W, 12, new Color(0, 0, 0, .42f));
                Txt((lvl + 1) + "/" + Data.Stages.Length + " " + Data.Stages[lvl].name, 3, 2, cyan, 8, Lf);
                Txt("x" + deaths + "  " + Fmt(time), W - 3, 2, cyan, 8, Rt);
                float bx = 112, bw = 96, prog = Mathf.Clamp01(p.x / lv.worldW);
                Box(bx, 5, bw, 3, new Color(1, 1, 1, .2f)); Box(bx, 5, bw * prog, 3, gold);
                foreach (var k in lv.cps) Box(bx + (k.x / lv.worldW) * bw, 4, 1, 5, k.lit ? new Color(1f, .96f, .75f) : new Color(1, 1, 1, .5f));
                Box(bx - 8, 4, 5, 5, p.dashes > 0 ? ScarfRed : ScarfBlue);
                {
                    int rk = RankOf(styl);
                    float lo = RankAt[rk], hi = rk + 1 < RankAt.Length ? RankAt[rk + 1] : 800f, fr = Mathf.Clamp01((styl - lo) / (hi - lo));
                    Color rc = RankCol[rk];
                    if (styl > 1f || feed.Count > 0)
                    {
                        Txt(Ranks[rk], W - 5, 14, rc, rk >= 4 ? 22 : 18, Rt);
                        Box(W - 46, 39, 42, 4, new Color(0, 0, 0, .5f)); Box(W - 45, 40, 40 * fr, 2, rc);
                        Txt("STYLE", W - 5, 45, cyan, 6, Rt);
                    }
                    for (int i = 0; i < feed.Count; i++)
                    {
                        var fc = feed[i].c; fc.a = Mathf.Clamp01(feed[i].t / 20f);
                        Txt(feed[i].s, W - 5, 55 + i * 9, fc, 8, Rt);
                    }
                }

                if (introT > 0 && state == State.Play)
                {
                    float a = Mathf.Clamp01(Mathf.Min(introT / 40f, (240 - introT) / 20f));
                    Txt("STAGE " + (lvl + 1), W / 2f, 38, new Color(gold.r, gold.g, gold.b, a), 8, C);
                    Txt(Data.Stages[lvl].name.ToUpper(), W / 2f, 48, new Color(1, 1, 1, a), 16, C);
                    Txt(th.name, W / 2f, 68, new Color(cyan.r, cyan.g, cyan.b, a), 8, C);
                    if (Data.Stages[lvl].hint.Length > 0) { Box(0, H - 13, W, 13, new Color(0, 0, 0, .5f)); Txt(Data.Stages[lvl].hint, W / 2f, H - 11, white, 8, C); }
                }
                if (state == State.Paused)
                {
                    Box(0, 0, W, H, new Color(0, 0, 0, .6f));
                    Txt("PAUSED", W / 2f, 70, white, 16, C); Txt("Press P or Z to resume", W / 2f, 94, cyan, 8, C);
                }
                if (state == State.Clear)
                {
                    float a = Mathf.Clamp01((130 - clearT) / 15f);
                    Box(0, 0, W, H, new Color(0, 0, 0, .55f * a));
                    Txt("STAGE CLEAR", W / 2f, 56, new Color(gold.r, gold.g, gold.b, a), 16, C);
                    Txt("Time " + Fmt(sTime) + "    Deaths " + sDeaths, W / 2f, 84, new Color(1, 1, 1, a), 8, C);
                    var bc = RankCol[bestRank]; Txt("Best style " + Ranks[bestRank] + "   (" + sStyle + " pts)", W / 2f, 98, new Color(bc.r, bc.g, bc.b, a), 10, C);
                }
                if (state == State.Win)
                {
                    Box(0, 0, W, H, new Color(0, 0, 0, .6f));
                    Txt("SUMMIT REACHED", W / 2f, 46, gold, 18, C);
                    Txt("You cleared all " + Data.Stages.Length + " stages!", W / 2f, 74, white, 8, C);
                    Txt("Total time " + Fmt(time) + "    Deaths " + deaths, W / 2f, 90, cyan, 8, C);
                    Txt("Total style points " + styleTotal, W / 2f, 102, gold, 8, C);
                    if ((tick / 30) % 2 == 0) Txt("Press Z to return to the title", W / 2f, 118, white, 8, C);
                }
            }
            if (fade > 0) Box(0, 0, W, H, new Color(0, 0, 0, fade / 20f));
        }
    }
}
