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
    //  Modes: ENDLESS (infinite procedural run) + 10 hand-built chapters.
    // ==================================================================
    public class DashlineGame : MonoBehaviour
    {
        const int W = Data.W, H = Data.H, T = Data.T, ROWS = Data.ROWS, CW = Data.CW, CWP = Data.CWP;
        const float STEP = 1f / 60f;

        enum State { Title, Play, Paused, Clear, Win, Shop }
        enum Act { Left, Right, Up, Down, Jump, Dash, Restart, Pause, Parry, Shop, Quit, Toss, W1, W2, W3, W4, W5, WNext, Unlock, Count }

        class PState
        {
            public float x, y, vx, vy, dx = 1, dy, sx = 1, sy = 1, anim, impact;
            public int w = 6, h = 14, face = 1, sdir = 1, dashes = 1, dashT, coy, wcoy, wd, wdir, jb, db, lock_, dead, parryT, parryCd, parryVis, impactT, airT, nearCd, swing, bounceT, inv, regen;
            public bool ground, dashing, slam;
            public Plat riding;
            public Vector2[] hair = new Vector2[5];
        }
        class TrailPt { public float x, y; public int l; public Color c; }
        struct Part { public float x, y, vx, vy, l; public Color c; }
        class BG
        {
            public GameObject root;
            public SpriteRenderer sky, fog;
            public SpriteRenderer[] mt = new SpriteRenderer[3], stal = new SpriteRenderer[2], cloud = new SpriteRenderer[6];
            public Theme th; public int ti;
            public readonly List<Sprite> spr = new List<Sprite>();
            public readonly List<SpriteRenderer> all = new List<SpriteRenderer>();
            public readonly List<Color> a0 = new List<Color>();
        }

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
        World lv = new World();
        Theme th = Data.Themes[1];
        int curTi = 1;
        PState p = new PState();
        State state = State.Title, shopPrev = State.Title;
        bool endless;
        int lvl, tick, deaths, time, sDeaths, sTime, flash, freeze, fade, clearT, introT, menuSel, unlocked;
        float shake, camX, acc;
        Vector2 spawn;
        readonly Part[] parts = new Part[300];
        int partI;
        readonly List<TrailPt> trail = new List<TrailPt>();
        readonly bool[] latch = new bool[(int)Act.Count];

        // ---- world entities
        readonly List<Crystal> crystals = new List<Crystal>();
        readonly List<Checkpoint> cps = new List<Checkpoint>();
        readonly List<Pickup> pickups = new List<Pickup>();
        readonly List<Shrine> shrines = new List<Shrine>();
        readonly List<Enemy> enemies = new List<Enemy>();
        readonly List<Saw> saws = new List<Saw>();
        readonly List<Plat> plats = new List<Plat>();
        readonly List<Turret> turrets = new List<Turret>();
        readonly List<Bullet> bullets = new List<Bullet>();
        bool hasGoal; int goalX, goalY;
        SpriteRenderer goalSR;
        readonly List<SpriteRenderer> goalSpark = new List<SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> chunkSR = new Dictionary<int, SpriteRenderer>();
        readonly List<Sprite> stageSprites = new List<Sprite>();

        // ---- endless
        int chunkN, runSeed, dist, zoneShown, runCoins;
        float startX;
        string lastTpl = "";
        string bannerT = "", bannerS = "";

        // ---- save / shop
        int coins, best, shield, shopSel, shopMsgT;
        readonly int[] up = new int[6];
        float hp = 100f; int weapon, charge; float railPow;
        int MaxHp() { return 100 + 25 * up[5]; }
        void Heal(float n) { hp = Mathf.Min(MaxHp(), hp + n); }
        static readonly string[] WName = { "PISTOL", "REVOLVER", "SHOTGUN", "RAILCANNON", "SMG" };
        static readonly int[] WCd = { 14, 30, 42, 86, 5 };
        static readonly Color[] WCol = { new Color(1f, .93f, .6f), new Color(1f, .7f, .25f), new Color(1f, .55f, .3f), new Color(.5f, .95f, 1f), new Color(.8f, 1f, .5f) };
        readonly Sprite[] gunSpr = new Sprite[5];
        string shopMsg = "";
        int MaxDash() { return 3 + up[0]; }
        int chain, chainT, coinCd, gunKick;
        class Tracer { public float x0, y0, x1, y1; public int life; public Color c = new Color(1f, .93f, .6f); public float wd = 1.6f; }
        readonly List<TossCoin> tossCoins = new List<TossCoin>();
        readonly List<Tracer> tracers = new List<Tracer>();
        readonly SpriteRenderer[] tracerSR = new SpriteRenderer[24];
        SpriteRenderer gunSR;
        Vector2 aimV = Vector2.right;

        Vector2 MouseWorld()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Vector2 sp = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width / 2f, Screen.height / 2f);
#else
            Vector2 sp = Input.mousePosition;
#endif
            Vector3 wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -cam.transform.position.z));
            return new Vector2(wp.x, -wp.y);
        }
        Vector2 AimDir(int iy)
        {
            Vector2 o = new Vector2(p.x + 3, p.y + p.h / 2f - 1);
            Vector2 d = MouseWorld() - o;
            if (!p.ground && iy > 0) d = Vector2.up;     // pixel space: +y is down
            if (d.sqrMagnitude < 4f) d = new Vector2(p.face, 0);
            return d.normalized;
        }
        void AddTracer(float x0, float y0, float x1, float y1) { tracers.Add(new Tracer { x0 = x0, y0 = y0, x1 = x1, y1 = y1, life = 6 }); while (tracers.Count > tracerSR.Length) tracers.RemoveAt(0); }

        void ThrowCoin(int iy)
        {
            var c = new TossCoin { life = 100, sr = MakeSR(stageRoot, coinSpr, -30, Vector3.zero) };
            if (!p.ground || iy > 0) { c.x = p.x + 3; c.y = p.y + p.h + 10; c.vx = p.vx; c.vy = p.vy; }
            else { c.x = p.x + 3 + p.face * 4; c.y = p.y + 2; c.vx = p.face * .9f; c.vy = -2.8f; }
            tossCoins.Add(c); Sfx(1300, 1700, .06f, 2, .04f);
        }
        void UpdateCoins()
        {
            for (int i = tossCoins.Count - 1; i >= 0; i--)
            {
                var c = tossCoins[i]; c.vy = Mathf.Min(c.vy + .13f, 4f); c.x += c.vx; c.y += c.vy; c.life--;
                if (c.life <= 0 || lv.Solid(Mathf.FloorToInt(c.x / T), Mathf.FloorToInt(c.y / T)))
                { Burst(c.x, c.y, 4, new Color(1f, .83f, .3f), .7f); Kill(c.sr); tossCoins.RemoveAt(i); }
            }
            for (int i = tracers.Count - 1; i >= 0; i--) { tracers[i].life--; if (tracers[i].life <= 0) tracers.RemoveAt(i); }
        }
        // a coin was shot: bounce the player if it is under them, then ricochet to the next coin / enemy
        void HitCoin(TossCoin c, int depth)
        {
            tossCoins.Remove(c); Kill(c.sr);
            Burst(c.x, c.y, 10, new Color(1f, .9f, .4f), 1.6f); Sfx(1000 + depth * 200, 1800, .12f, 2, .06f);
            freeze = Mathf.Max(freeze, 3); shake = Mathf.Max(shake, 3);
            bool below = depth == 1 && c.y > p.y + p.h - 6 && c.y < p.y + p.h + 60 && Mathf.Abs(c.x - (p.x + 3)) < 18;
            if (below)
            {
                p.vy = -3.9f; p.vx *= .5f; p.slam = false; p.dashT = 0; p.dashes = MaxDash(); p.bounceT = 24; p.lock_ = 0;
                p.sx = .7f; p.sy = 1.3f; p.ground = false; p.coy = 0; p.riding = null; p.parryVis = 8; shake = 5; freeze = 5;
                Burst(p.x + 3, p.y + p.h, 16, Color.white, 2f);
                AddStyle(120, "COIN BOUNCE");
            }
            else AddStyle(30 + 30 * depth, depth > 1 ? "COIN CHAIN x" + depth : "COIN SHOT");
            TossCoin nc = null; float bd = 130f * 130f;
            foreach (var o in tossCoins) { float dd = (o.x - c.x) * (o.x - c.x) + (o.y - c.y) * (o.y - c.y); if (dd < bd) { bd = dd; nc = o; } }
            if (nc != null && depth < 5) { AddTracer(c.x, c.y, nc.x, nc.y); HitCoin(nc, depth + 1); return; }
            Enemy ne = null; bd = 180f * 180f;
            foreach (var e in enemies)
            {
                if (!e.alive) continue;
                float ex = e.x + e.w / 2f, ey = e.y + e.h / 2f, dd = (ex - c.x) * (ex - c.x) + (ey - c.y) * (ey - c.y);
                if (dd < bd) { bd = dd; ne = e; }
            }
            Boss tb = null;
            foreach (var ar in arenas)
            {
                var bb = ar.boss; if (bb == null || !bb.active || bb.dead) continue;
                float bx2 = bb.x + bb.w / 2f, by2 = bb.y + bb.h / 2f, dd = (bx2 - c.x) * (bx2 - c.x) + (by2 - c.y) * (by2 - c.y);
                if (dd < 240f * 240f && dd < bd) { bd = dd; tb = bb; }
            }
            if (tb != null) { AddTracer(c.x, c.y, tb.x + tb.w / 2f, tb.y + tb.h / 2f); DamageBoss(tb, 3); AddStyle(100, "RICOCHET BOSS"); }
            else if (ne != null) { AddTracer(c.x, c.y, ne.x + ne.w / 2f, ne.y + ne.h / 2f); KillEnemy(ne, depth > 1 ? "RICOCHET x" + depth : "RICOCHET", 120 + 40 * depth); }
        }
        static Vector2 Rot(Vector2 v, float a) { float c = Mathf.Cos(a), sn = Mathf.Sin(a); return new Vector2(v.x * c - v.y * sn, v.x * sn + v.y * c); }
        void Shoot(Vector2 dir)
        {
            Vector2 o = new Vector2(p.x + 3, p.y + p.h / 2f - 1) + dir * 7f;
            if (Mathf.Abs(dir.x) > .3f) p.face = dir.x > 0 ? 1 : -1;
            switch (weapon)
            {
                case 0: FireRay(o, dir, 240f, 1f, false, 0); Sfx(700, 140, .1f, 1, .05f); shake = Mathf.Max(shake, 1); break;
                case 1: FireRay(o, dir, 330f, 3f, false, 1); Sfx(520, 70, .15f, 1, .07f); shake = Mathf.Max(shake, 3); break;
                case 2:
                    for (int i = 0; i < 7; i++) FireRay(o, Rot(dir, (i - 3) * .085f + Random.Range(-.02f, .02f)), 100f, .8f, false, 2);
                    Sfx(300, 50, .2f, 1, .09f); Sfx(120, 40, .16f, 0, .05f); shake = Mathf.Max(shake, 5);
                    p.vx -= dir.x * 1.1f; if (!p.ground) p.vy = Mathf.Clamp(p.vy - dir.y * 1.0f, -4.2f, 6f);
                    break;
                case 3:
                    FireRay(o, dir, 560f, 3f + 9f * railPow, true, 3);
                    Sfx(220, 1100, .28f, 2, .08f); Sfx(90, 30, .25f, 1, .08f); shake = Mathf.Max(shake, 4 + Mathf.RoundToInt(8 * railPow)); freeze = Mathf.Max(freeze, 2);
                    {
                        // charged shot launches you backwards
                        float lp = railPow >= 1f ? 6.2f : railPow >= .6f ? 3.6f : 0f;
                        if (lp > 0f)
                        {
                            p.vx = -dir.x * lp; p.vy = Mathf.Clamp(-dir.y * lp, -6f, 5f); p.ground = false; p.coy = 0; p.dashT = 0; p.slam = false; p.riding = null;
                            p.lock_ = railPow >= 1f ? 12 : 7; p.sx = .8f; p.sy = 1.2f; shake = 10; freeze = 3;
                            Burst(p.x + 3, p.y + p.h / 2f, 22, WCol[3], 2.4f); Burst(p.x + 3, p.y + p.h / 2f, 8, Color.white, 1.6f);
                            AddStyle(railPow >= 1f ? 160 : 70, railPow >= 1f ? "RAIL LAUNCH" : "RAIL BOOST");
                        }
                    }
                    break;
                default: FireRay(o, Rot(dir, Random.Range(-.08f, .08f)), 200f, .5f, false, 4); Sfx(900, 320, .04f, 1, .025f); break;
            }
            Burst(o.x + dir.x * 3, o.y + dir.y * 3, weapon == 2 ? 6 : 3, WCol[weapon], .9f);
        }
        void FireRay(Vector2 o, Vector2 dir, float range, float bdmg, bool pierce, int wk)
        {
            Vector2 end = o; bool done = false;
            var hitB = new List<Boss>();
            string lab = wk == 3 ? "RAILED" : wk == 2 ? "SHOTGUN" : p.ground ? "SHOT" : "AIRSHOT";
            for (float sd = 0; sd < range && !done; sd += 2f)
            {
                Vector2 pos = o + dir * sd; end = pos;
                if (lv.Solid(Mathf.FloorToInt(pos.x / T), Mathf.FloorToInt(pos.y / T))) { Burst(pos.x - dir.x * 2, pos.y - dir.y * 2, 3, WCol[wk], .8f); break; }
                for (int i = tossCoins.Count - 1; i >= 0; i--)
                {
                    if (i >= tossCoins.Count) continue;
                    var c = tossCoins[i];
                    if ((c.x - pos.x) * (c.x - pos.x) + (c.y - pos.y) * (c.y - pos.y) < 36f)
                    { AddTracer(o.x, o.y, c.x, c.y); HitCoin(c, 1); if (!pierce) done = true; break; }
                }
                if (done) break;
                foreach (var e in enemies)
                    if (e.alive && pos.x > e.x - 1 && pos.x < e.x + e.w + 1 && pos.y > e.y - 1 && pos.y < e.y + e.h + 1)
                    { KillEnemy(e, lab, p.ground ? 50 : 80); if (!pierce) { done = true; break; } }
                if (done) break;
                foreach (var ar in arenas)
                {
                    var bb = ar.boss;
                    if (bb != null && bb.active && !bb.dead && !hitB.Contains(bb) && pos.x > bb.x && pos.x < bb.x + bb.w && pos.y > bb.y && pos.y < bb.y + bb.h)
                    { hitB.Add(bb); DamageBoss(bb, bdmg); if (!pierce) { done = true; break; } }
                }
                if (done) break;
                for (int i = turrets.Count - 1; i >= 0; i--)
                {
                    var t = turrets[i];
                    if (pos.x > t.x && pos.x < t.x + 8 && pos.y > t.y && pos.y < t.y + 8)
                    { Burst(t.x + 4, t.y + 4, 16, new Color(1f, .6f, .2f), 1.8f); Kill(t.sr); turrets.RemoveAt(i); GainCoins(3); AddStyle(60, "TURRET DOWN"); Sfx(300, 80, .15f, 1, .06f); if (!pierce) done = true; }
                }
                for (int i = bullets.Count - 1; i >= 0; i--)
                {
                    var b = bullets[i];
                    if ((b.x - pos.x) * (b.x - pos.x) + (b.y - pos.y) * (b.y - pos.y) < 12f) { Kill(b.sr); bullets.RemoveAt(i); AddStyle(40, "SNIPED"); if (!pierce) done = true; }
                }
                if (done) break;
                foreach (var sw in saws) if ((sw.x - pos.x) * (sw.x - pos.x) + (sw.y - pos.y) * (sw.y - pos.y) < sw.r * sw.r) { done = true; break; }
            }
            tracers.Add(new Tracer { x0 = o.x, y0 = o.y, x1 = end.x, y1 = end.y, life = wk == 3 ? 10 : 5, c = WCol[wk], wd = wk == 3 ? 3.2f : wk == 1 ? 2.2f : wk == 2 ? 1f : 1.6f });
            while (tracers.Count > tracerSR.Length) tracers.RemoveAt(0);
        }
        float SwordReach() { return 9 + 3 * up[1]; }

        void LoadSave()
        {
            coins = PlayerPrefs.GetInt("dashline_coins", 0);
            best = PlayerPrefs.GetInt("dashline_best", 0);
            for (int i = 0; i < up.Length; i++) up[i] = PlayerPrefs.GetInt("dashline_up_" + Data.Upgrades[i].id, 0);
            skin = PlayerPrefs.GetInt("dashline_skin", 0); skinOwned = 63;
        }
        void Persist()
        {
            PlayerPrefs.SetInt("dashline_coins", coins); PlayerPrefs.SetInt("dashline_best", best);
            for (int i = 0; i < up.Length; i++) PlayerPrefs.SetInt("dashline_up_" + Data.Upgrades[i].id, up[i]);
            PlayerPrefs.SetInt("dashline_skin", skin); PlayerPrefs.SetInt("dashline_skins", skinOwned);
            PlayerPrefs.Save();
        }
        void GainCoins(int n) { coins += n; runCoins += n; }

        // ---- style meter
        static readonly string[] Ranks = { "D", "C", "B", "A", "S", "SS", "SSS", "U" };
        static readonly string[] RankName = { "DESTRUCTIVE", "CHAOTIC", "BRUTAL", "ANARCHIC", "SUPREME", "SSADISTIC", "SSSHITSTORM", "ULTRAKILL" };
        static readonly int[] RankAt = { 0, 80, 170, 280, 400, 540, 700, 900 };
        static readonly Color[] RankCol =
        {
            new Color(.60f, .64f, .78f), new Color(.48f, .88f, 1f), new Color(.49f, 1f, .69f), new Color(1f, .88f, .4f),
            new Color(1f, .64f, .28f), new Color(1f, .42f, .42f), new Color(1f, .30f, .82f), new Color(1f, .12f, .12f)
        };
        class FeedItem { public string s; public int t; public Color c; }
        readonly List<FeedItem> feed = new List<FeedItem>();
        float styl;
        int styleTotal, sStyle, bestRank;
        SpriteRenderer parryRing;
        readonly SpriteRenderer[] swordSR = new SpriteRenderer[4];
        static int RankOf(float v) { int r = 0; for (int i = 0; i < RankAt.Length; i++) if (v >= RankAt[i]) r = i; return r; }
        void AddStyle(int n, string label = null)
        {
            n = Mathf.RoundToInt(n * (1f + .25f * up[4]));
            int before = RankOf(styl);
            styl = Mathf.Min(1099f, styl + n); styleTotal += n; sStyle += n;
            int r = RankOf(styl);
            if (r > before) { Sfx(500 + r * 120, 900 + r * 150, .15f, 2, .05f); feed.Add(new FeedItem { s = RankName[r] + "!", t = 70, c = RankCol[r] }); if (r >= 5) { flash = 3; shake = Mathf.Max(shake, 3); } }
            if (r > bestRank) bestRank = r;
            if (label != null) feed.Add(new FeedItem { s = label + " +" + n, t = 70, c = RankCol[r] });
            while (feed.Count > 5) feed.RemoveAt(0);
        }

        // ambient particle seeds
        readonly float[] aX = new float[40], aY = new float[40], aPh = new float[40], aPar = new float[40], aSp = new float[40];
        readonly int[] aSz = new int[40];

        // ------------------------------------------------ renderers
        Transform poolRoot, stageRoot, playerRoot;
        SpriteRenderer vigSR;
        readonly SpriteRenderer[] partSR = new SpriteRenderer[300], trailSR = new SpriteRenderer[24], glowSR = new SpriteRenderer[40],
            ambSR = new SpriteRenderer[40], scarfSR = new SpriteRenderer[5], pl = new SpriteRenderer[10];
        readonly BG[] bgs = new BG[2];
        int bgCur = -1, bgFrom = -1, bgFadeT;
        Sprite crystalSpr, lanternOn, lanternOff, portalSpr, coinSpr, walkerSpr, batSprA, batSprB, turretSpr, sawSpr, platSpr, bulletSpr, shrineSpr;
        readonly Sprite[] crumbleSpr = new Sprite[Data.Themes.Length];
        readonly Sprite[] bossSpr = new Sprite[4];
        Sprite gateSpr;
        readonly List<Arena> arenas = new List<Arena>();
        readonly List<int> gateCols = new List<int>();
        static readonly string[] BossName = { "MINOS CORPSE", "GABRIEL", "V2", "MALICIOUS FACE" };
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
            LoadSave();

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
            coinSpr = Art.CoinSprite(); walkerSpr = Art.WalkerSprite(); batSprA = Art.BatSprite(true); batSprB = Art.BatSprite(false);
            turretSpr = Art.TurretSprite(); sawSpr = Art.SawSprite(); platSpr = Art.PlatSprite(2); bulletSpr = Art.BulletSprite(); shrineSpr = Art.ShrineSprite();
            for (int i = 0; i < crumbleSpr.Length; i++) crumbleSpr[i] = Art.CrumbleSprite(Data.Themes[i]);
            for (int i = 0; i < bossSpr.Length; i++) bossSpr[i] = Art.BossSprite(i);
            gateSpr = Art.GateSprite();

            vigSR = MakeSR(cam.transform, Art.Vignette(), 40, new Vector3(-W / 2f, -H / 2f, 10f));

            for (int i = 0; i < partSR.Length; i++) partSR[i] = Quad(poolRoot, 10);
            for (int i = 0; i < trailSR.Length; i++) trailSR[i] = Quad(poolRoot, -10);
            for (int i = 0; i < glowSR.Length; i++) { glowSR[i] = MakeSR(poolRoot, Art.Glow(), 20, Vector3.zero); }
            for (int i = 0; i < ambSR.Length; i++) ambSR[i] = Quad(poolRoot, 30);
            for (int i = 0; i < scarfSR.Length; i++) scarfSR[i] = Quad(poolRoot, 0);

            playerRoot = new GameObject("Player").transform; playerRoot.SetParent(transform, false);
            int[] ord = { 1, 2, 3, 4, 4, 1, 2, 1, 1, 1 };
            for (int i = 0; i < pl.Length; i++) pl[i] = Quad(playerRoot, ord[i]);
            for (int i = 0; i < accSR.Length; i++) accSR[i] = Quad(playerRoot, 6);
            for (int i = 0; i < skinFrames.Length; i++) skinFrames[i] = Art.SkinFrames(i);
            bodySR = MakeSR(playerRoot, skinFrames[0][0], 3, Vector3.zero);
            parryRing = MakeSR(poolRoot, Art.RingSprite(), 5, Vector3.zero); parryRing.enabled = false;
            for (int gi = 0; gi < gunSpr.Length; gi++) gunSpr[gi] = Art.GunSprite(gi);
            gunSR = MakeSR(poolRoot, gunSpr[0], 6, Vector3.zero); gunSR.enabled = false;
            for (int i = 0; i < tracerSR.Length; i++) tracerSR[i] = Quad(poolRoot, 7);

            for (int i = 0; i < 40; i++)
            {
                aX[i] = Art.Hash(i, 1) * W; aY[i] = Art.Hash(i, 2) * H; aPh[i] = Art.Hash(i, 3) * 6.28f;
                aSz[i] = 1 + (int)(Art.Hash(i, 4) * 2); aPar[i] = .5f + Art.Hash(i, 5) * .9f; aSp[i] = .25f + Art.Hash(i, 6) * .7f;
            }

            TitleScene();
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
        static void Kill(SpriteRenderer sr) { if (sr != null) Destroy(sr.gameObject); }
        // place a quad using top-down pixel coords (x,y = top-left)
        static void Rc(SpriteRenderer sr, float x, float y, float w, float h)
        {
            var t = sr.transform;
            t.localPosition = new Vector3(x + w / 2f, -(y + h / 2f), 0f);
            t.localScale = new Vector3(w, h, 1f);
        }
        Sprite Track(Sprite s) { stageSprites.Add(s); return s; }

        // ================================================== skins
        static readonly string[] SkinN = { "Shadow", "Crimson", "Forest", "Gold", "Frost", "Void" };
        static readonly int[] SkinCost = { 0, 0, 0, 0, 0, 0 };
        static readonly Color[] SkinHood = { new Color(.15f, .17f, .27f), new Color(.45f, .08f, .12f), new Color(.10f, .30f, .16f), new Color(.55f, .42f, .08f), new Color(.55f, .75f, .90f), new Color(.08f, .02f, .16f) };
        static readonly Color[] SkinTorso = { new Color(.18f, .21f, .34f), new Color(.30f, .06f, .10f), new Color(.08f, .22f, .12f), new Color(.40f, .30f, .06f), new Color(.38f, .55f, .72f), new Color(.14f, .05f, .26f) };
        static readonly Color[] SkinLegs = { new Color(.09f, .10f, .17f), new Color(.16f, .04f, .06f), new Color(.05f, .12f, .07f), new Color(.24f, .18f, .04f), new Color(.22f, .36f, .52f), new Color(.06f, .02f, .12f) };
        static readonly Color[] SkinScarf = { new Color(1f, .30f, .30f), new Color(1f, .70f, .20f), new Color(.70f, 1f, .40f), new Color(1f, 1f, .60f), new Color(1f, 1f, 1f), new Color(1f, .30f, .90f) };
        int skin, skinOwned = 63, shopTab;
        readonly SpriteRenderer[] accSR = new SpriteRenderer[24];
        SpriteRenderer bodySR;
        readonly Sprite[][] skinFrames = new Sprite[6][];
        int ai;
        // accessory quad in player-local space, drawn for a right-facing player and mirrored when facing left
        void Acc(float x, float y, float w, float h, Color c)
        {
            if (ai >= accSR.Length) return;
            var sr = accSR[ai++]; sr.enabled = true;
            Rc(sr, p.face > 0 ? x : -x - w, y, w, h); sr.color = c;
        }
        static Color Cc(float r, float g, float b) { return new Color(r, g, b); }
        void DrawSkin(bool fl, int bob)
        {
            ai = 0;
            Color Wh = Color.white;
            switch (skin)
            {
                case 0: // Shadow ninja: headband tails + mask
                    {
                        Color sc = fl ? Wh : SkinScarf[0];
                        Acc(-5, -7 + bob, 3, 1, sc); Acc(-6, -6 + bob + (tick / 6 % 2), 2, 1, sc); Acc(-7, -5 + bob - (tick / 6 % 2), 2, 1, sc);
                        Acc(-1, -5 + bob, 3, 1, fl ? Wh : Cc(.07f, .08f, .14f));
                        Acc(-3, -2, 6, 1, fl ? Wh : Cc(.4f, .1f, .12f));
                        break;
                    }
                case 1: // Crimson samurai: horned kabuto, shoulder plates, katana
                    {
                        Color red = fl ? Wh : Cc(.6f, .08f, .12f), gold = fl ? Wh : Cc(1f, .8f, .25f), dk = fl ? Wh : Cc(.2f, .03f, .05f);
                        Acc(-4, -9 + bob, 8, 2, red); Acc(-3, -11 + bob, 6, 2, dk);
                        Acc(-5, -13 + bob, 1, 4, gold); Acc(4, -13 + bob, 1, 4, gold); Acc(-1, -13 + bob, 2, 2, gold);
                        Acc(-5, -4, 2, 3, red); Acc(3, -4, 2, 3, red); Acc(-5, -4, 2, 1, gold); Acc(3, -4, 2, 1, gold);
                        Acc(-3, -2, 6, 1, gold);
                        Acc(-6, -9, 1, 8, fl ? Wh : Cc(.85f, .88f, .95f)); Acc(-7, -2, 3, 1, dk);
                        break;
                    }
                case 2: // Forest ranger: feathered hat, quiver, cloak
                    {
                        Color gr = fl ? Wh : Cc(.12f, .45f, .2f), dg = fl ? Wh : Cc(.06f, .25f, .12f), br = fl ? Wh : Cc(.45f, .28f, .12f);
                        Acc(-5, -9 + bob, 10, 1, dg); Acc(-3, -11 + bob, 6, 2, gr); Acc(-2, -13 + bob, 3, 2, gr);
                        Acc(2, -15 + bob, 1, 4, fl ? Wh : Cc(1f, .35f, .3f)); Acc(3, -14 + bob, 1, 2, Wh);
                        Acc(-5, -5, 2, 4, dg); Acc(-6, -4, 1, 3, dg);
                        Acc(-6, -9, 2, 6, br); Acc(-6, -10, 1, 1, Wh); Acc(-5, -11, 1, 2, Wh); Acc(-4, -10, 1, 1, Wh);
                        Acc(-3, -2, 6, 1, br); Acc(0, -2, 1, 1, fl ? Wh : Cc(1f, .83f, .3f));
                        break;
                    }
                case 3: // Gold knight: plumed helm, pauldrons, cape
                    {
                        Color gd = fl ? Wh : Cc(.95f, .75f, .2f), lt = fl ? Wh : Cc(1f, .93f, .55f), dk = fl ? Wh : Cc(.45f, .32f, .06f), rd = fl ? Wh : Cc(.8f, .1f, .15f);
                        Acc(-4, -9 + bob, 8, 6, gd); Acc(-4, -9 + bob, 8, 1, lt);
                        Acc(0, -6 + bob, 4, 1, dk); Acc(-1, -7 + bob, 1, 3, dk);
                        Acc(-1, -12 + bob, 2, 3, rd); Acc(-4, -11 + bob + (tick / 8 % 2), 3, 2, rd);
                        Acc(-5, -4, 3, 3, gd); Acc(2, -4, 3, 3, gd); Acc(-5, -4, 3, 1, lt); Acc(2, -4, 3, 1, lt);
                        Acc(-6, -4, 1, 5, rd); Acc(-7, -2, 1, 3, rd);
                        Acc(-3, -2, 6, 1, dk);
                        break;
                    }
                case 4: // Frost mage: tall hat with star, beard, robe, staff
                    {
                        Color bl = fl ? Wh : Cc(.25f, .45f, .8f), dk = fl ? Wh : Cc(.13f, .25f, .55f), ice = fl ? Wh : Cc(.75f, .95f, 1f);
                        Acc(-5, -9 + bob, 10, 1, dk); Acc(-3, -11 + bob, 6, 2, bl); Acc(-2, -13 + bob, 4, 2, bl); Acc(-1, -15 + bob, 2, 2, bl);
                        Acc(0, -17 + bob + (tick / 10 % 2), 1, 1, ice); Acc(-1, -16 + bob, 3, 1, Wh);
                        Acc(0, -5 + bob, 3, 3, Wh);
                        Acc(-4, -4, 8, 4, bl); Acc(-4, -1, 8, 1, dk); Acc(-4, -4, 8, 1, ice);
                        Acc(-6, -11, 1, 11, fl ? Wh : Cc(.5f, .35f, .2f)); Acc(-7, -13, 3, 2, ice);
                        pl[7].enabled = false; pl[8].enabled = false;
                        break;
                    }
                case 5: // Void wraith: horns, glowing eyes, no legs, orbiting orbs
                    {
                        Color pu = fl ? Wh : Cc(.55f, .15f, .9f), mg = fl ? Wh : Cc(1f, .3f, .9f);
                        Acc(-4, -11 + bob, 1, 3, pu); Acc(3, -11 + bob, 1, 3, pu); Acc(-5, -12 + bob, 1, 2, pu); Acc(4, -12 + bob, 1, 2, pu);
                        float wob = Mathf.Sin(tick * .2f);
                        Acc(-3, -1, 6, 1, pu); Acc(-2, 0, 4, 1, Cc(.4f, .1f, .7f)); Acc(-1 + wob, 1, 2, 1, Cc(.3f, .05f, .55f));
                        float a = tick * .09f;
                        Acc(Mathf.Sin(a) * 7 - 1, -4 + Mathf.Cos(a) * 4, 2, 2, mg);
                        Acc(Mathf.Sin(a + 3.14f) * 7 - 1, -4 + Mathf.Cos(a + 3.14f) * 4, 2, 2, pu);
                        pl[2].color = fl ? Wh : Cc(.04f, 0f, .09f);
                        pl[3].color = mg; pl[4].color = mg;
                        pl[7].enabled = false; pl[8].enabled = false;
                        break;
                    }
            }
            for (int i = ai; i < accSR.Length; i++) accSR[i].enabled = false;
        }

        // ================================================== backdrop (two slots so themes can crossfade)
        Sprite cloudSpr;
        Sprite Sp(BG b, Sprite s) { b.spr.Add(s); return s; }
        BG MakeBG(int ti, int slot)
        {
            if (cloudSpr == null) cloudSpr = Art.CloudSprite();
            var b = new BG { th = Data.Themes[ti], ti = ti };
            int bo = slot == 0 ? -100 : -79;
            b.root = new GameObject("BG" + slot); b.root.transform.SetParent(transform, false);
            b.sky = MakeSR(cam.transform, Sp(b, Art.Sky(b.th)), bo, new Vector3(-W / 2f, -H / 2f, 10f));
            b.fog = MakeSR(cam.transform, Sp(b, Art.Fog(b.th)), bo + 20, new Vector3(-W / 2f, -H / 2f, 10f));
            b.fog.transform.localScale = new Vector3(W, 1, 1);
            for (int k = 0; k < 3; k++) b.mt[k] = MakeSR(b.root.transform, Sp(b, Art.MountainStrip(b.th, k)), bo + 10 + k, Vector3.zero);
            if (b.th.stal) for (int k = 0; k < 2; k++) b.stal[k] = MakeSR(b.root.transform, Sp(b, Art.StalStrip(b.th, k)), bo + 15 + k, Vector3.zero);
            Color cc = b.th.clouds != null ? Art.Hex(b.th.clouds, .55f) : Color.clear;
            for (int k = 0; k < b.cloud.Length; k++) { b.cloud[k] = MakeSR(b.root.transform, cloudSpr, bo + 5, Vector3.zero); b.cloud[k].color = cc; b.cloud[k].enabled = b.th.clouds != null; }
            b.all.Add(b.sky); b.all.Add(b.fog);
            foreach (var s in b.mt) b.all.Add(s);
            foreach (var s in b.stal) if (s != null) b.all.Add(s);
            foreach (var s in b.cloud) b.all.Add(s);
            foreach (var s in b.all) b.a0.Add(s.color);
            return b;
        }
        void DestroyBG(BG b)
        {
            if (b == null) return;
            foreach (var s in b.spr) if (s != null) { Destroy(s.texture); Destroy(s); }
            Kill(b.sky); Kill(b.fog); Destroy(b.root);
        }
        void SetAlpha(BG b, float a)
        {
            if (b == null) return;
            for (int i = 0; i < b.all.Count; i++) { var c = b.a0[i]; b.all[i].color = new Color(c.r, c.g, c.b, c.a * a); }
        }
        void SetBackdrop(int ti, bool instant)
        {
            curTi = ti; th = Data.Themes[ti];
            if (instant || bgCur < 0)
            {
                for (int i = 0; i < 2; i++) { DestroyBG(bgs[i]); bgs[i] = null; }
                bgs[0] = MakeBG(ti, 0); bgCur = 0; bgFrom = -1; bgFadeT = 0; return;
            }
            int ns = 1 - bgCur;
            DestroyBG(bgs[ns]); bgs[ns] = null;
            bgs[ns] = MakeBG(ti, ns); bgFrom = bgCur; bgCur = ns; bgFadeT = 80;
        }

        // ================================================== world building
        void ResetWorld()
        {
            if (stageRoot != null) Destroy(stageRoot.gameObject);
            foreach (var s in stageSprites) { if (s != null) { Destroy(s.texture); Destroy(s); } }
            stageSprites.Clear(); chunkSR.Clear();
            stageRoot = new GameObject("Stage").transform; stageRoot.SetParent(transform, false);
            lv = new World();
            crystals.Clear(); cps.Clear(); pickups.Clear(); shrines.Clear(); enemies.Clear(); saws.Clear(); plats.Clear(); turrets.Clear(); bullets.Clear(); tossCoins.Clear(); tracers.Clear(); arenas.Clear(); gateCols.Clear();
            hasGoal = false; goalSR = null; goalSpark.Clear();
            for (int k = 0; k < parts.Length; k++) parts[k].l = 0;
            trail.Clear(); feed.Clear();
        }

        void AppendChunk(char[][] g, List<EntDef> E, int ti, bool noCeil, int mobDiff = -1)
        {
            int k = lv.chunkTheme.Count, ox = k * CW;
            for (int x = 0; x < CW; x++)
            {
                var col = new char[ROWS];
                for (int y = 0; y < ROWS; y++) col[y] = g[y][x];
                lv.map.Add(col);
            }
            lv.chunkTheme.Add(ti);
            if (Data.Themes[ti].ceil && !noCeil)
                for (int x = ox; x < ox + CW; x++)
                {
                    int hh = 2 + (int)(Art.Hash(x >> 1, 0, 77) * 3);
                    for (int y = 0; y < hh; y++) if (lv.map[x][y] == '.') lv.map[x][y] = '#';
                }
            for (int x = ox; x < ox + CW; x++)
                for (int y = 0; y < ROWS; y++)
                {
                    char c = lv.map[x][y];
                    if (c == 'o') { crystals.Add(new Crystal { x = x * T + 4, y = y * T + 4, sr = MakeSR(stageRoot, crystalSpr, -40, Vector3.zero) }); lv.map[x][y] = '.'; }
                    else if (c == '$') { pickups.Add(new Pickup { x = x * T + 4, y = y * T + 4, ph = Random.value * 6f, sr = MakeSR(stageRoot, coinSpr, -37, Vector3.zero) }); lv.map[x][y] = '.'; }
                    else if (c == 'P') { spawn = new Vector2(x * T + 1, y * T); lv.map[x][y] = '.'; }
                    else if (c == 'C')
                    {
                        cps.Add(new Checkpoint { x = x * T + 4, y = y * T, sx = x * T + 1, sr = MakeSR(stageRoot, lanternOff, -40, new Vector3(x * T + 4, -(y * T + 8), 0)) });
                        lv.map[x][y] = '.';
                    }
                    else if (c == 'G')
                    {
                        hasGoal = true; goalX = x; goalY = y; lv.map[x][y] = '.';
                        goalSR = MakeSR(stageRoot, portalSpr, -40, new Vector3(x * T + 4, -(y * T - 6), 0));
                        for (int q = 0; q < 5; q++) { var sp = MakeSR(stageRoot, Art.Pixel(), -39, Vector3.zero); sp.color = new Color(1f, .96f, .78f, .8f); sp.transform.localScale = new Vector3(2, 2, 1); goalSpark.Add(sp); }
                    }
                    else if (c == 'X') { if (!gateCols.Contains(x)) gateCols.Add(x); }
                    else if (c == 'c')
                    {
                        var b = new Crumble { x = x, y = y, sr = MakeSR(stageRoot, crumbleSpr[ti], -44, new Vector3(x * T, -(y * T), 0)) };
                        lv.crumbles[y * 100000 + x] = b;
                    }
                }
            if (gateCols.Count >= 2)
            {
                int gl = gateCols[0], gr = gateCols[1];
                var ar = new Arena { gl = gl, gr = gr, x0 = (gl + 1) * T, x1 = gr * T };
                ar.gateL = MakeSR(stageRoot, gateSpr, -32, new Vector3(gl * T, -(4 * T), 0));
                ar.gateR = MakeSR(stageRoot, gateSpr, -32, new Vector3(gr * T, -(4 * T), 0));
                arenas.Add(ar); gateCols.Clear();
            }
            foreach (var e in E) SpawnEnt(e, ox);
            if (mobDiff >= 0) AddMobs(ox, mobDiff);
        }
        // extra monsters sprinkled on normal chunks; density, speed and turrets scale with difficulty
        void AddMobs(int ox, int diff)
        {
            var r = new Rng(ox * 31 + diff * 7 + 5);
            int n = Mathf.Min(6, 2 + diff / 5 + (r.Next() < (diff % 5) / 5f ? 1 : 0));
            for (int q = 0; q < n; q++)
            {
                float roll = r.Next(); int cx = r.Ri(3, 16);
                bool floorOk = lv.map[ox + cx][18] == '#' && lv.map[ox + cx][17] == '.';
                if (roll < .5f && floorOk)
                {
                    int l = cx, rr = cx;
                    while (l > 2 && cx - l < 5 && lv.map[ox + l - 1][18] == '#' && lv.map[ox + l - 1][17] == '.') l--;
                    while (rr < 17 && rr - cx < 5 && lv.map[ox + rr + 1][18] == '#' && lv.map[ox + rr + 1][17] == '.') rr++;
                    if (rr - l < 2) continue;
                    SpawnEnt(new EntDef { t = "walker", x = cx, y = 18, l = l, rr = rr + 1 }, ox);
                    enemies[enemies.Count - 1].spd = .5f + Mathf.Min(.65f, diff * .03f);
                }
                else if (roll < .88f)
                {
                    int yy = 7 + r.Ri(0, 5);
                    if (lv.map[ox + cx][yy] != '.') continue;
                    SpawnEnt(new EntDef { t = "bat", x = cx, y = yy, rx = r.Ri(2, 4), amp = r.Ri(1, 2), per = Mathf.Max(70, r.Ri(110, 190) - diff * 3) }, ox);
                }
                else if (diff >= 5 && floorOk)
                    SpawnEnt(new EntDef { t = "turret", x = cx, y = 17, dir = r.Next() < .5f ? -1 : 1, per = Mathf.Max(45, 110 - diff * 3) }, ox);
            }
        }

        void PlatPos(Plat q, out float px, out float py)
        {
            float a = tick * 6.2832f / q.per + q.ph;
            px = q.cx + Mathf.Sin(a) * q.ax - q.w / 2f; py = q.cy + Mathf.Sin(a) * q.ay;
        }
        void SpawnEnt(EntDef e, int ox)
        {
            float X = (ox + e.x) * T, Y = e.y * T;
            Enemy en = null;
            if (e.t == "walker")
                enemies.Add(en = new Enemy { kind = 0, x = X - 4, y = Y - 7, w = 8, h = 7, dir = Random.value < .5f ? -1 : 1, l = (ox + e.l) * T, r = (ox + e.rr) * T, sr = MakeSR(stageRoot, walkerSpr, -35, Vector3.zero) });
            else if (e.t == "bat")
                enemies.Add(en = new Enemy { kind = 1, cx = X, cy = Y, x = X, y = Y, w = 8, h = 6, rx = e.rx * T, amp = e.amp * T, per = e.per, t = Random.value * 100f, sr = MakeSR(stageRoot, batSprA, -35, Vector3.zero) });
            else if (e.t == "turret")
                turrets.Add(new Turret { x = X, y = Y, dir = e.dir, per = (int)e.per, cd = 60, sr = MakeSR(stageRoot, turretSpr, -36, Vector3.zero) });
            else if (e.t == "saw")
                saws.Add(new Saw { cx = X, cy = Y, x = X, y = Y, r = 4, ax = e.ax * T, ay = e.ay * T, per = e.per, ph = e.ph, sr = MakeSR(stageRoot, sawSpr, -36, Vector3.zero) });
            else if (e.t == "plat")
            {
                var q = new Plat { cx = X, cy = Y, w = e.w * T, h = 4, ax = e.ax * T, ay = e.ay * T, per = e.per, ph = e.ph, sr = MakeSR(stageRoot, platSpr, -42, Vector3.zero) };
                float qx, qy; PlatPos(q, out qx, out qy); q.x = qx; q.y = qy; plats.Add(q);
            }
            else if (e.t == "shrine")
                shrines.Add(new Shrine { x = X, y = Y, sr = MakeSR(stageRoot, shrineSpr, -41, Vector3.zero) });
            else if (e.t == "boss" && arenas.Count > 0)
            {
                int k = e.bk;
                float bw = k == 0 ? 26 : k == 1 ? 18 : k == 2 ? 12 : 24, bh = k == 0 ? 30 : k == 1 ? 22 : k == 2 ? 20 : 24;
                var bo = new Boss { kind = k, hp = e.hp, maxhp = e.hp, w = bw, h = bh, x = X, y = (k == 1 || k == 3) ? 56 : 18 * T - bh };
                bo.sx = bo.x; bo.sy = bo.y; bo.sr = MakeSR(stageRoot, bossSpr[k], -33, Vector3.zero);
                arenas[arenas.Count - 1].boss = bo; arenas[arenas.Count - 1].big = true;
            }
            if (en != null) { en.sx = en.x; en.sy = en.y; if (e.arena && arenas.Count > 0) arenas[arenas.Count - 1].foes.Add(en); }
        }

        Tpl FindTpl(string n) { foreach (var t in Data.Templates) if (t.name == n) return t; return Data.Templates[0]; }
        Tpl PickTpl(int d, Rng r)
        {
            var pool = new List<Tpl>();
            foreach (var t in Data.Templates) if (d >= t.min && d <= t.max && t.name != lastTpl) pool.Add(t);
            float tot = 0; foreach (var t in pool) tot += t.w;
            float v = r.Next() * tot;
            foreach (var t in pool) { v -= t.w; if (v <= 0) return t; }
            return pool[pool.Count - 1];
        }
        void GenChunk()
        {
            int k = lv.chunkTheme.Count, d = Mathf.Max(0, k - 2);
            var r = new Rng(runSeed * 7919 + k * 104729 + 17);
            int ti = Data.Zones[(k / 10) % Data.Zones.Length];
            var g = Data.Mk(); var E = new List<EntDef>(); bool noCeil = false; int mobD = -1;
            if (k == 0) Data.Chunks["start"](g);
            else if (k % 20 == 17) { Data.Chunks["cp"](g); Data.AddCoins(g, r); }
            else if (k % 20 == 18) { Data.Chunks["bossA"](g); noCeil = true; }
            else if (k % 20 == 19) { Data.Chunks["bossB"](g); noCeil = true; E.Add(new EntDef { t = "boss", x = 7, y = 18, bk = (k / 20) % 4, hp = 40 + (k / 20) * 12 }); }
            else if (k % 12 == 0) { Data.Ground(g); g[17][5] = 'C'; E.Add(new EntDef { t = "shrine", x = 12, y = 18 }); }
            else if (k % 4 == 0) { Data.Chunks["cp"](g); Data.AddCoins(g, r); }
            else
            {
                Tpl t = k == 1 ? FindTpl("flat") : k == 2 ? FindTpl("gap") : PickTpl(d, r);
                t.fn(g, r, E, d); noCeil = t.noCeil; lastTpl = t.name; mobD = d; Data.AddCoins(g, r);
            }
            AppendChunk(g, E, ti, noCeil, mobD);
        }
        void EnsureAhead() { while (lv.WorldW < camX + W + CWP * 3) GenChunk(); }

        void Prune()
        {
            float lo = camX - 1000f; int kc = Mathf.FloorToInt(camX / CWP);
            var dead = new List<int>();
            foreach (var kv in chunkSR) if (kv.Key < kc - 7) dead.Add(kv.Key);
            foreach (int k in dead) { var sr = chunkSR[k]; if (sr != null) { Destroy(sr.sprite.texture); Destroy(sr.sprite); Kill(sr); } chunkSR.Remove(k); }
            for (int i = enemies.Count - 1; i >= 0; i--) { var e = enemies[i]; if ((e.kind == 1 ? e.cx : e.x) < lo) { Kill(e.sr); enemies.RemoveAt(i); } }
            for (int i = saws.Count - 1; i >= 0; i--) if (saws[i].cx < lo) { Kill(saws[i].sr); saws.RemoveAt(i); }
            for (int i = plats.Count - 1; i >= 0; i--) if (plats[i].cx < lo) { Kill(plats[i].sr); plats.RemoveAt(i); }
            for (int i = turrets.Count - 1; i >= 0; i--) if (turrets[i].x < lo) { Kill(turrets[i].sr); turrets.RemoveAt(i); }
            for (int i = bullets.Count - 1; i >= 0; i--) if (bullets[i].x < lo) { Kill(bullets[i].sr); bullets.RemoveAt(i); }
            for (int i = pickups.Count - 1; i >= 0; i--) if (pickups[i].x < lo) { Kill(pickups[i].sr); pickups.RemoveAt(i); }
            for (int i = shrines.Count - 1; i >= 0; i--) if (shrines[i].x < lo) { Kill(shrines[i].sr); shrines.RemoveAt(i); }
            for (int i = crystals.Count - 1; i >= 0; i--) if (crystals[i].x < lo) { Kill(crystals[i].sr); crystals.RemoveAt(i); }
            for (int i = cps.Count - 1; i >= 0; i--) if (cps[i].x < lo) { Kill(cps[i].sr); cps.RemoveAt(i); }
            for (int i = arenas.Count - 1; i >= 0; i--) if (arenas[i].x1 < lo) { var ar = arenas[i]; Kill(ar.gateL); Kill(ar.gateR); if (ar.boss != null) Kill(ar.boss.sr); lv.closedGates.Remove(ar.gl); lv.closedGates.Remove(ar.gr); arenas.RemoveAt(i); }
            var cd = new List<int>();
            foreach (var kv in lv.crumbles) if (kv.Value.x * T < lo) cd.Add(kv.Key);
            foreach (int key in cd) { Kill(lv.crumbles[key].sr); lv.crumbles.Remove(key); }
        }

        void LoadChapter(int i)
        {
            endless = false; lvl = i; ResetWorld();
            var st = Data.Stages[i];
            var list = new List<string> { "start" };
            list.AddRange(st.chunks);
            if (i >= 3) list.Add("gauntlet");
            if (i >= 6) list.Add("pillars");
            if (i >= 1) list.Add("pack");
            if (i >= 2) list.Add("snipe");
            if (i >= 2) { list.Add("cp"); list.Add("arena"); }
            list.Add("cp"); list.Add("bossA"); list.Add("bossB"); list.Add("end");
            foreach (var n in list) { var g = Data.Mk(); Data.Chunks[n](g); AppendChunk(g, Data.Ents(n, i), st.theme, n.StartsWith("boss"), (n == "cp" || n == "start" || n == "end" || n == "arena" || n.StartsWith("boss")) ? -1 : i * 2 + 2); }
            SetBackdrop(st.theme, true);
            sDeaths = 0; sTime = 0; introT = 240; fade = 20; styl = 0; sStyle = 0; bestRank = 0; shield = up[3];
            bannerT = st.name.ToUpper(); bannerS = "STAGE " + (i + 1) + "  -  " + th.name;
            Respawn(); camX = Mathf.Clamp(spawn.x - W / 2f, 0, Mathf.Max(0, lv.WorldW - W));
        }
        void StartEndless()
        {
            endless = true; lvl = 0; runSeed = Random.Range(0, 1000000); lastTpl = ""; ResetWorld();
            for (int i = 0; i < 8; i++) GenChunk();
            SetBackdrop(lv.chunkTheme[0], true); zoneShown = 0;
            sDeaths = 0; sTime = 0; introT = 150; fade = 20; styl = 0; sStyle = 0; bestRank = 0; shield = up[3]; dist = 0;
            bannerT = "ENDLESS"; bannerS = "ZONE 1  -  " + th.name;
            Respawn(); startX = spawn.x; camX = 0;
        }
        void TitleScene()
        {
            LoadChapter(Mathf.Max(0, menuSel - 1)); introT = 0; fade = 0; state = State.Title;
        }
        void StartGame()
        {
            deaths = 0; time = 0; styleTotal = 0; runCoins = 0;
            if (menuSel == 0) StartEndless(); else LoadChapter(menuSel - 1);
            state = State.Play; Sfx(400, 800, .15f, 0, .05f);
        }
        void ToTitle()
        {
            if (endless && dist > best) best = dist;
            Persist(); TitleScene();
        }

        void Respawn()
        {
            p = new PState { x = spawn.x, y = spawn.y, dashes = MaxDash() }; hp = MaxHp();
            p.y = spawn.y + 8 - p.h;
            for (int i = 0; i < p.hair.Length; i++) p.hair[i] = new Vector2(spawn.x + 3, spawn.y + 2);
            foreach (var c in crystals) c.cd = 0;
            trail.Clear(); ResetArenas();
            camX = Mathf.Clamp(spawn.x - W / 2f, 0, Mathf.Max(0, lv.WorldW - W));
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
                case Act.Shop: return k.qKey.isPressed || k.bKey.isPressed;
                case Act.Quit: return k.tKey.isPressed;
                case Act.Toss: return k.fKey.isPressed || (Mouse.current != null && Mouse.current.rightButton.isPressed);
                case Act.Pause: return k.pKey.isPressed || k.escapeKey.isPressed;
                case Act.Parry: return k.vKey.isPressed || k.eKey.isPressed || k.leftCtrlKey.isPressed || (Mouse.current != null && Mouse.current.leftButton.isPressed);
                case Act.W1: return k.digit1Key.isPressed;
                case Act.W2: return k.digit2Key.isPressed;
                case Act.W3: return k.digit3Key.isPressed;
                case Act.W4: return k.digit4Key.isPressed;
                case Act.W5: return k.digit5Key.isPressed;
                case Act.WNext: return k.tabKey.isPressed;
                case Act.Unlock: return k.uKey.isPressed;
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
                case Act.Shop: return k.qKey.wasPressedThisFrame || k.bKey.wasPressedThisFrame;
                case Act.Quit: return k.tKey.wasPressedThisFrame;
                case Act.Toss: return k.fKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
                case Act.Pause: return k.pKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame;
                case Act.Parry: return k.vKey.wasPressedThisFrame || k.eKey.wasPressedThisFrame || k.leftCtrlKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                case Act.W1: return k.digit1Key.wasPressedThisFrame;
                case Act.W2: return k.digit2Key.wasPressedThisFrame;
                case Act.W3: return k.digit3Key.wasPressedThisFrame;
                case Act.W4: return k.digit4Key.wasPressedThisFrame;
                case Act.W5: return k.digit5Key.wasPressedThisFrame;
                case Act.WNext: return k.tabKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.scroll.ReadValue().y != 0f);
                case Act.Unlock: return k.uKey.wasPressedThisFrame;
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
                case Act.Shop: return Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.B);
                case Act.Quit: return Input.GetKey(KeyCode.T);
                case Act.Toss: return Input.GetKey(KeyCode.F) || Input.GetMouseButton(1);
                case Act.Pause: return Input.GetKey(KeyCode.P) || Input.GetKey(KeyCode.Escape);
                case Act.Parry: return Input.GetKey(KeyCode.V) || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.LeftControl) || Input.GetMouseButton(0);
                case Act.W1: return Input.GetKey(KeyCode.Alpha1);
                case Act.W2: return Input.GetKey(KeyCode.Alpha2);
                case Act.W3: return Input.GetKey(KeyCode.Alpha3);
                case Act.W4: return Input.GetKey(KeyCode.Alpha4);
                case Act.W5: return Input.GetKey(KeyCode.Alpha5);
                case Act.WNext: return Input.GetKey(KeyCode.Tab);
                case Act.Unlock: return Input.GetKey(KeyCode.U);
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
                case Act.Shop: return Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.B);
                case Act.Quit: return Input.GetKeyDown(KeyCode.T);
                case Act.Toss: return Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(1);
                case Act.Pause: return Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape);
                case Act.Parry: return Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.LeftControl) || Input.GetMouseButtonDown(0);
                case Act.W1: return Input.GetKeyDown(KeyCode.Alpha1);
                case Act.W2: return Input.GetKeyDown(KeyCode.Alpha2);
                case Act.W3: return Input.GetKeyDown(KeyCode.Alpha3);
                case Act.W4: return Input.GetKeyDown(KeyCode.Alpha4);
                case Act.W5: return Input.GetKeyDown(KeyCode.Alpha5);
                case Act.WNext: return Input.GetKeyDown(KeyCode.Tab) || Input.mouseScrollDelta.y != 0f;
                case Act.Unlock: return Input.GetKeyDown(KeyCode.U);
            }
            return false;
        }
#endif
        bool Pressed(Act a) { return latch[(int)a]; }

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
        Plat OnPlat()
        {
            if (p.vy < 0f) return null;
            foreach (var q in plats) if (p.x + p.w > q.x && p.x < q.x + q.w && Mathf.Abs(p.y + p.h - q.y) <= 1.2f) return q;
            return null;
        }
        // what the sword is touching: 0 nothing, 1 wall, 2 spike/saw, 3 enemy, 4 bullet
        int hitKind; Enemy hitEnemy; Bullet hitBullet;
        static bool RectOv(float ax0, float ax1, float ay0, float ay1, float bx, float by, float bw, float bh)
        { return ax0 < bx + bw && ax1 > bx && ay0 < by + bh && ay1 > by; }
        bool SwordHit()
        {
            hitKind = 0; hitEnemy = null; hitBullet = null;
            float R = SwordReach(), x0, x1, y0, y1;
            if (p.sdir == 0) { x0 = p.x + .5f; x1 = p.x + p.w - .5f; y0 = p.y + p.h; y1 = y0 + R; }
            else if (p.sdir > 0) { x0 = p.x + p.w; x1 = x0 + R; y0 = p.y + .5f; y1 = p.y + p.h - .5f; }
            else { x1 = p.x; x0 = x1 - R; y0 = p.y + .5f; y1 = p.y + p.h - .5f; }
            foreach (var e in enemies) if (e.alive && RectOv(x0, x1, y0, y1, e.x, e.y, e.w, e.h)) { hitKind = 3; hitEnemy = e; return true; }
            foreach (var b in bullets) if (!b.refl && RectOv(x0, x1, y0, y1, b.x - 1.5f, b.y - 1.5f, 3, 3)) { hitKind = 4; hitBullet = b; return true; }
            foreach (var s in saws) if (RectOv(x0, x1, y0, y1, s.x - s.r, s.y - s.r, s.r * 2, s.r * 2)) { hitKind = 2; return true; }
            int res = 0;
            for (int ty = Mathf.FloorToInt(y0 / T); ty <= Mathf.FloorToInt(y1 / T); ty++)
                for (int tx = Mathf.FloorToInt(x0 / T); tx <= Mathf.FloorToInt(x1 / T); tx++)
                {
                    if (lv.Solid(tx, ty)) { if (res == 0) res = 1; }
                    else if (ty >= 0 && ty < ROWS && tx >= 0 && tx < lv.Cols) { char c = lv.map[tx][ty]; if (c == '^' || c == 'v') res = 2; }
                }
            hitKind = res;
            return res != 0;
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
                if (s > 0f)
                    foreach (var q in plats)
                        if (p.x + p.w > q.x && p.x < q.x + q.w && p.y + p.h <= q.y + 1f && p.y + p.h + s > q.y) { p.y = q.y - p.h; p.vy = 0; p.riding = q; return true; }
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

        void Shockwave()
        {
            p.slam = false; shake = 8; freeze = 4; flash = 2;
            float fx = p.x + 3, fy = p.y + p.h;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * 6.2832f; partI = (partI + 1) % parts.Length;
                parts[partI] = new Part { x = fx, y = fy, vx = Mathf.Cos(a) * 2.2f, vy = -Mathf.Abs(Mathf.Sin(a)) * 1.2f, l = 22, c = Art.Hex(Data.Themes[curTi].tile[1]) };
            }
            Sfx(120, 40, .3f, 1, .09f);
            foreach (var e in enemies) if (e.alive && Mathf.Abs(e.x + e.w / 2f - fx) < 44 && Mathf.Abs(e.y + e.h - fy) < 20) KillEnemy(e, "GROUND SLAM", 90);
            for (int i = bullets.Count - 1; i >= 0; i--) { var b = bullets[i]; if (Mathf.Abs(b.x - fx) < 44 && Mathf.Abs(b.y - fy) < 24) { Kill(b.sr); bullets.RemoveAt(i); } }
            foreach (var c in lv.crumbles.Values) if (c.st == 0 && Mathf.Abs(c.x * T + 4 - fx) < 24 && Mathf.Abs(c.y * T - fy) < 12) { c.st = 1; c.t = 6; }
            foreach (var ar in arenas)
            {
                var bb = ar.boss;
                if (bb != null && bb.active && !bb.dead && Mathf.Abs(bb.x + bb.w / 2f - fx) < 44 && Mathf.Abs(bb.y + bb.h - fy) < 24) DamageBoss(bb, 2);
            }
            AddStyle(30, "SLAM");
        }

        // ================================================== arenas + bosses
        void BossShot(float x, float y, float vx, float vy)
        { bullets.Add(new Bullet { x = x, y = y, vx = vx, vy = vy, life = 260, sr = MakeSR(stageRoot, bulletSpr, -34, Vector3.zero) }); }
        void BossAimed(Boss b, float spd, float spread)
        {
            float cx = b.x + b.w / 2f, cy = b.y + b.h / 2f, ang = Mathf.Atan2(p.y + p.h / 2f - cy, p.x + 3 - cx) + spread;
            BossShot(cx, cy, Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd);
        }
        void DamageBoss(Boss b, float n)
        {
            if (b.dead) return;
            b.hp -= n; b.flash = 4;
            Burst(b.x + b.w / 2f, b.y + b.h / 2f, 8, new Color(.85f, .05f, .1f), 1.8f); Sfx(250, 100, .1f, 1, .06f);
            AddStyle(Mathf.Max(1, Mathf.RoundToInt(20 * n)), null);
            if (b.hp <= 0) BossDie(b);
        }
        void BossDie(Boss b)
        {
            b.dead = true; b.active = false; Heal(40);
            foreach (var bu in bullets) Kill(bu.sr);
            bullets.Clear();
            for (int i = 0; i < 4; i++) Burst(b.x + b.w / 2f, b.y + b.h / 2f, 14, i % 2 == 0 ? new Color(1f, .3f, .1f) : new Color(1f, .85f, .3f), 3f);
            freeze = 10; shake = 14; flash = 6; Sfx(200, 30, .6f, 1, .1f); Sfx(900, 1800, .4f, 2, .06f, .2f);
            AddStyle(500, "BOSS DOWN");
        }
        void UpdateArenas()
        {
            foreach (var a in arenas)
            {
                if (a.done) continue;
                float px = p.x + 3;
                if (!a.started)
                {
                    if (p.dead <= 0 && px > a.x0 + 14 && px < a.x1 - 14)
                    {
                        a.started = true; lv.closedGates.Add(a.gl); lv.closedGates.Add(a.gr); shake = 6; freeze = 4; Sfx(150, 60, .3f, 1, .08f);
                        if (a.boss != null) { a.boss.active = true; bannerT = BossName[a.boss.kind]; bannerS = "BOSS FIGHT"; introT = 130; }
                        else { bannerT = "ARENA"; bannerS = "KILL THEM ALL"; introT = 100; }
                    }
                    continue;
                }
                bool clear = a.boss == null || a.boss.dead;
                foreach (var f in a.foes) if (f.alive) { clear = false; break; }
                if (clear)
                {
                    a.done = true; lv.closedGates.Remove(a.gl); lv.closedGates.Remove(a.gr);
                    GainCoins(a.boss != null ? 25 : 10); AddStyle(a.boss != null ? 300 : 150, a.boss != null ? "VICTORY" : "ARENA CLEAR");
                    Sfx(500, 1200, .3f, 2, .07f); shake = 5;
                }
            }
        }
        void ResetArenas()
        {
            foreach (var a in arenas)
            {
                if (a.done || !a.started) continue;
                a.started = false; lv.closedGates.Remove(a.gl); lv.closedGates.Remove(a.gr);
                foreach (var f in a.foes) { f.alive = true; f.x = f.sx; f.y = f.sy; f.t = 0; }
                var b = a.boss;
                if (b != null) { b.hp = b.maxhp; b.active = false; b.x = b.sx; b.y = b.sy; b.st = 0; b.st2 = 0; b.t = 0; b.vx = 0; b.vy = 0; b.inv = 0; b.flash = 0; }
                foreach (var bu in bullets) Kill(bu.sr);
                bullets.Clear();
            }
        }
        void UpdateBoss()
        {
            foreach (var a in arenas)
            {
                var b = a.boss; if (b == null || !b.active || b.dead) continue;
                b.t++; if (b.inv > 0) b.inv--; if (b.flash > 0) b.flash--;
                float pcx = p.x + 3, pcy = p.y + p.h / 2f, cx = b.x + b.w / 2f, cy = b.y + b.h / 2f, gTop = 18 * T, mid = (a.x0 + a.x1) / 2f;
                b.dir = pcx < cx ? -1 : 1;
                switch (b.kind)
                {
                    case 0: // Minos corpse: walks, leaps and slams (shockwaves), spits shots
                        if (b.st == 0)
                        {
                            b.y = gTop - b.h; b.x += b.dir * .45f;
                            if (b.t % 150 == 0) { b.st = 1; b.vy = -4.4f; b.vx = b.dir * 1.3f; Sfx(200, 400, .15f, 1, .06f); }
                            if (b.t % 210 == 100) { BossAimed(b, 1.7f, -.25f); BossAimed(b, 1.7f, 0); BossAimed(b, 1.7f, .25f); Sfx(300, 120, .12f, 1, .05f); }
                        }
                        else
                        {
                            b.vy += .18f; b.x += b.vx; b.y += b.vy;
                            if (b.y + b.h >= gTop)
                            {
                                b.y = gTop - b.h; b.st = 0; shake = 8; Sfx(100, 40, .3f, 1, .09f);
                                BossShot(cx - 6, gTop - 3, -1.7f, 0); BossShot(cx + 6, gTop - 3, 1.7f, 0);
                                Burst(cx, gTop, 14, Art.Hex(Data.Themes[curTi].tile[1]), 2f);
                            }
                        }
                        break;
                    case 1: // Gabriel: hovers, telegraphs, dashes, rains spears
                        if (b.st == 0)
                        {
                            b.x += (pcx - cx) * .02f; b.y = 56 + Mathf.Sin(b.t * .05f) * 8; b.st2++;
                            if (b.st2 > 110) { b.st = 1; b.st2 = 0; }
                            if (b.t % 80 == 40) { BossShot(cx, cy, -.9f, 1.7f); BossShot(cx, cy, 0, 1.8f); BossShot(cx, cy, .9f, 1.7f); Sfx(500, 300, .1f, 1, .05f); }
                        }
                        else if (b.st == 1)
                        {
                            b.st2++; b.y += (pcy - b.h / 2f - b.y) * .12f; if ((b.t / 3) % 2 == 0) b.flash = 2;
                            if (b.st2 > 30) { b.st = 2; b.st2 = 0; b.vx = b.dir * 4.8f; Sfx(300, 900, .2f, 1, .07f); }
                        }
                        else { b.st2++; b.x += b.vx; if (b.st2 > 36) { b.st = 0; b.st2 = 0; } }
                        break;
                    case 2: // V2: keeps distance, shoots, jumps, dashes
                        {
                            b.vy += .18f; b.y += b.vy; if (b.y + b.h >= gTop) { b.y = gTop - b.h; b.vy = 0; }
                            float dd = pcx - cx, want = Mathf.Abs(dd) < 70 ? -b.dir : (Mathf.Abs(dd) > 110 ? b.dir : 0);
                            b.x += want * .9f;
                            if (b.t % 100 == 60 && b.vy == 0) b.vy = -3.4f;
                            if (b.t % 45 == 0) BossAimed(b, 1.9f, 0);
                            if (b.t % 170 == 120) { b.vx = -b.dir * 4f; b.st2 = 10; }
                            if (b.st2 > 0) { b.x += b.vx; b.st2--; }
                            break;
                        }
                    default: // Malicious face: floats, ring volleys
                        {
                            b.x = mid + Mathf.Sin(b.t * .02f) * 70 - b.w / 2f; b.y = 50 + Mathf.Sin(b.t * .04f) * 8;
                            int per = b.hp * 2 < b.maxhp ? 70 : 110;
                            if (b.t % per == 0)
                            {
                                float off = b.t * .05f;
                                for (int i = 0; i < 10; i++) { float ang = off + i * .6283f; BossShot(cx, cy, Mathf.Cos(ang) * 1.25f, Mathf.Sin(ang) * 1.25f); }
                                Sfx(250, 500, .15f, 1, .05f);
                            }
                            if (b.t % 260 == 130) { BossAimed(b, 1.8f, -.2f); BossAimed(b, 1.8f, 0); BossAimed(b, 1.8f, .2f); }
                            break;
                        }
                }
                b.x = Mathf.Clamp(b.x, a.x0 + 4, a.x1 - 4 - b.w);
            }
        }

        // ================================================== damage / death
        void Die()
        {
            if (p.dead > 0) return;
            deaths++; sDeaths++; styl = 0; p.dead = 32; freeze = 0; shake = 8; flash = 5;
            if (endless && dist > best) best = dist;
            Persist();
            Burst(p.x + 3, p.y + p.h / 2f, 26, new Color(1f, .35f, .48f), 1.9f);
            Burst(p.x + 3, p.y + p.h / 2f, 10, Color.white, 1.2f);
            Sfx(220, 40, .3f, 1, .08f);
        }
        void Hurt(bool pit, int dmg = 25)
        {
            if (p.dead > 0 || (p.inv > 0 && !pit)) return;
            if (pit) { hp = 0; Die(); return; }
            if (shield > 0)
            {
                shield--; p.inv = 80; p.vy = -3f; p.vx = -p.face * 1.2f; p.dashT = 0; p.lock_ = 6; p.dashes = MaxDash(); freeze = 4; shake = 6; flash = 3;
                Burst(p.x + 3, p.y + p.h / 2f, 16, new Color(.49f, .82f, 1f), 1.6f); Sfx(180, 700, .2f, 2, .08f);
                feed.Add(new FeedItem { s = "SHIELD BROKE", t = 70, c = new Color(.49f, .82f, 1f) });
                return;
            }
            hp -= dmg; styl = Mathf.Max(0f, styl - 40f);
            if (hp <= 0) { hp = 0; Die(); return; }
            p.inv = 55; p.vy = -2.6f; p.vx = -p.face * 1.4f; p.dashT = 0; p.lock_ = 6; freeze = 3; shake = 6; flash = 3;
            Burst(p.x + 3, p.y + p.h / 2f, 14, new Color(1f, .2f, .3f), 1.6f); Sfx(200, 60, .18f, 1, .08f);
            feed.Add(new FeedItem { s = "-" + dmg + " HP", t = 40, c = new Color(1f, .4f, .45f) });
        }
        void KillEnemy(Enemy e, string label, int pts)
        {
            e.alive = false; GainCoins(3); Heal(6);
            float ex = e.x + e.w / 2f, ey = e.y + e.h / 2f;
            Burst(ex, ey, 14, e.kind == 1 ? new Color(.54f, .39f, .82f) : new Color(.88f, .32f, .48f), 1.6f); Burst(ex, ey, 6, new Color(1f, .83f, .3f), 1.2f);
            Sfx(400, 120, .15f, 1, .06f);
            freeze = Mathf.Max(freeze, 4); shake = Mathf.Max(shake, 4);
            Burst(ex, ey, 20, new Color(.8f, .05f, .1f), 2.4f);
            chain = chainT > 0 ? chain + 1 : 1; chainT = 100;
            if (!string.IsNullOrEmpty(label)) AddStyle(pts, label);
            if (chain >= 2) AddStyle(35 * chain, "MULTIKILL x" + chain);
        }
        void FinishStage()
        {
            state = State.Clear; clearT = 130; freeze = 0;
            Sfx(523, 523, .12f, 2, .07f); Sfx(659, 659, .12f, 2, .07f, .1f); Sfx(784, 784, .12f, 2, .07f, .2f); Sfx(1046, 1046, .35f, 2, .07f, .3f);
            Burst(goalX * T + 4, goalY * T - 4, 40, new Color(1f, .83f, .3f), 2.2f);
            if (lvl + 1 > unlocked && lvl + 1 < Data.Stages.Length) { unlocked = lvl + 1; PlayerPrefs.SetInt("dashline_unlocked", unlocked); }
            GainCoins(sDeaths == 0 ? 30 : 15);
            Persist();
        }

        void CheckWorld()
        {
            int x0 = Mathf.FloorToInt(p.x / T), x1 = Mathf.FloorToInt((p.x + p.w - .001f) / T);
            int y0 = Mathf.FloorToInt(p.y / T), y1 = Mathf.FloorToInt((p.y + p.h - .001f) / T);
            for (int ty = y0; ty <= y1; ty++)
                for (int tx = x0; tx <= x1; tx++)
                {
                    if (tx < 0 || tx >= lv.Cols || ty < 0 || ty >= ROWS) continue;
                    char c = lv.map[tx][ty];
                    if (c == '^' || c == 'v')
                    {
                        float sy = c == '^' ? ty * T + 4 : ty * T;
                        if (p.x + 5.5f > tx * T && p.x + .5f < tx * T + T && p.y + p.h - .5f > sy && p.y + .5f < sy + 4) { Hurt(false, 30); if (p.dead > 0) return; }
                    }
                }
            if (p.nearCd > 0) p.nearCd--;
            else
                for (int ty = y0 - 1; ty <= y1 + 1; ty++)
                    for (int tx = x0 - 1; tx <= x1 + 1; tx++)
                    {
                        if (tx < 0 || tx >= lv.Cols || ty < 0 || ty >= ROWS) continue;
                        char c2 = lv.map[tx][ty];
                        if (c2 != '^' && c2 != 'v') continue;
                        float sy2 = c2 == '^' ? ty * T + 4 : ty * T;
                        float ox = Mathf.Max(tx * T - (p.x + 5.5f), (p.x + .5f) - (tx * T + T)), oy = Mathf.Max(sy2 - (p.y + p.h - .5f), (p.y + .5f) - (sy2 + 4));
                        float gap = Mathf.Max(ox, oy);
                        if (gap > 0 && gap <= 2) { AddStyle(15, "CLOSE CALL"); p.nearCd = 40; }
                    }
            if (hasGoal && Mathf.Abs(p.x + 3 - (goalX * T + 4)) < 9 && Mathf.Abs(p.y + p.h / 2f - goalY * T) < 14) { FinishStage(); return; }
            if (p.y > H + 8) { Hurt(true); return; }
            foreach (var c in crystals)
            {
                if (c.cd > 0) { c.cd--; continue; }
                if (p.dashes < MaxDash() && Vector2.Distance(new Vector2(p.x + 3, p.y + p.h / 2f), new Vector2(c.x, c.y)) < 11)
                {
                    p.dashes = MaxDash(); c.cd = 150; Burst(c.x, c.y, 12, new Color(.49f, 1f, .69f), 1.3f);
                    Sfx(700, 1400, .12f, 2, .06f); AddStyle(40, "CRYSTAL");
                }
            }
            foreach (var k in cps)
            {
                if (!k.lit && Mathf.Abs(p.x + 3 - k.x) < 10 && Mathf.Abs(p.y + p.h - k.y) < 14)
                {
                    k.lit = true; hp = MaxHp(); spawn = new Vector2(k.sx, k.y);
                    Burst(k.x, k.y - 14, 14, new Color(1f, .83f, .3f), 1.2f, .5f);
                    Sfx(660, 990, .18f, 2, .06f); AddStyle(40, "CHECKPOINT");
                }
            }
            CheckEntities();
        }

        bool NearX(float x) { return x > camX - 60 && x < camX + W + 60; }
        void CheckEntities()
        {
            float bx = p.x + 1, by = p.y + 1, bw = p.w - 2, bh = p.h - 2;
            foreach (var e in enemies)
            {
                if (!e.alive || !NearX(e.x)) continue;
                if (RectOv(bx, bx + bw, by, by + bh, e.x, e.y, e.w, e.h))
                {
                    if (p.dashing || p.slam) KillEnemy(e, p.slam ? "STOMP" : "DASH KILL", 70);
                    else { Hurt(false, 25); if (p.dead > 0) return; }
                }
            }
            foreach (var a in arenas)
            {
                var bb = a.boss; if (bb == null || !bb.active || bb.dead) continue;
                if (!RectOv(bx, bx + bw, by, by + bh, bb.x, bb.y, bb.w, bb.h)) continue;
                if ((p.dashing || p.slam) && bb.inv <= 0)
                {
                    bool sl = p.slam; DamageBoss(bb, sl ? 3 : 2); bb.inv = 25; p.dashT = 0; p.slam = false; p.vy = -2.6f; p.vx = -p.face * 1.6f; p.lock_ = 6;
                    AddStyle(60, sl ? "STOMP" : "DASH HIT");
                }
                else if (bb.inv <= 0) { Hurt(false, 35); if (p.dead > 0) return; }
            }
            foreach (var s in saws)
            {
                if (!NearX(s.x)) continue;
                float qx = Mathf.Clamp(s.x, bx, bx + bw), qy = Mathf.Clamp(s.y, by, by + bh);
                if ((qx - s.x) * (qx - s.x) + (qy - s.y) * (qy - s.y) < (s.r - .5f) * (s.r - .5f)) { Hurt(false, 30); if (p.dead > 0) return; }
            }
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                if (!b.refl && RectOv(bx, bx + bw, by, by + bh, b.x - 1.5f, b.y - 1.5f, 3, 3)) { Kill(b.sr); bullets.RemoveAt(i); Hurt(false, 15); if (p.dead > 0) return; }
            }
            float mag = up[2] * 20f;
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var c = pickups[i]; if (!NearX(c.x)) continue;
                float dx = p.x + 3 - c.x, dy = p.y + p.h / 2f - c.y, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (mag > 0 && d < mag + 8 && d > 1) { c.x += dx / d * 2.2f; c.y += dy / d * 2.2f; }
                if (d < 7) { Kill(c.sr); pickups.RemoveAt(i); GainCoins(1); Sfx(1200, 1700, .06f, 0, .03f); Burst(c.x, c.y, 3, new Color(1f, .83f, .3f), .6f); }
            }
        }

        // ================================================== entity updates
        void UpdateEnts()
        {
            foreach (var q in plats)
            {
                if (Mathf.Abs(q.cx - camX - W / 2f) > W / 2f + 120 + q.ax) continue;
                float nx, ny; PlatPos(q, out nx, out ny);
                q.dx = nx - q.x; q.dy = ny - q.y; q.x = nx; q.y = ny;
                if (p.riding == q && p.dead <= 0) { if (!Hits(p.x + q.dx, p.y)) p.x += q.dx; p.y = q.y - p.h; }
            }
            foreach (var s in saws)
            {
                if (!NearX(s.cx)) continue;
                float a = tick * 6.2832f / s.per + s.ph; s.x = s.cx + Mathf.Sin(a) * s.ax; s.y = s.cy + Mathf.Sin(a) * s.ay;
            }
            foreach (var e in enemies)
            {
                if (!e.alive) continue;
                if (e.kind == 0)
                {
                    if (!NearX(e.x)) continue;
                    e.anim++; float nx = e.x + e.dir * e.spd;
                    int tx = Mathf.FloorToInt((e.dir > 0 ? nx + e.w : nx) / T), ty = Mathf.FloorToInt((e.y + e.h - 1) / T);
                    if (nx < e.l) e.dir = 1; else if (nx + e.w > e.r) e.dir = -1; else if (lv.Solid(tx, ty)) e.dir = -e.dir; else e.x = nx;
                }
                else
                {
                    if (!NearX(e.cx)) continue;
                    e.t++; float a = e.t * 6.2832f / e.per;
                    e.x = e.cx + Mathf.Sin(a) * e.rx - e.w / 2f; e.y = e.cy + Mathf.Sin(a * 2f) * e.amp - e.h / 2f;
                }
            }
            foreach (var t in turrets)
            {
                if (!NearX(t.x)) continue;
                t.cd--;
                float dxp = (p.x - t.x) * t.dir;
                if (t.cd <= 0)
                {
                    t.cd = t.per;
                    if (dxp > 0 && dxp < 170 && p.dead <= 0)
                    {
                        bullets.Add(new Bullet { x = t.x + (t.dir < 0 ? -2 : 10), y = t.y + 4, vx = t.dir * 1.25f, life = 260, sr = MakeSR(stageRoot, bulletSpr, -34, Vector3.zero) });
                        Sfx(260, 120, .12f, 0, .03f);
                    }
                }
            }
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i]; b.x += b.vx; b.y += b.vy; b.life--;
                if (b.refl)
                {
                    bool hitE = false;
                    foreach (var e in enemies) if (e.alive && RectOv(b.x - 2, b.x + 2, b.y - 2, b.y + 2, e.x, e.y, e.w, e.h)) { KillEnemy(e, "FRIENDLY FIRE", 150); hitE = true; break; }
                    if (hitE) { Kill(b.sr); bullets.RemoveAt(i); continue; }
                }
                if (b.life <= 0 || lv.Solid(Mathf.FloorToInt(b.x / T), Mathf.FloorToInt(b.y / T)))
                { Burst(b.x, b.y, 3, new Color(1f, .6f, .31f), .6f); Kill(b.sr); bullets.RemoveAt(i); }
            }
            foreach (var b in lv.crumbles.Values)
            {
                if (b.st == 1) { if (--b.t <= 0) { b.st = 2; b.t = 150; Burst(b.x * T + 4, b.y * T + 4, 8, Art.Hex(Data.Themes[curTi].tile[1]), .9f); Sfx(120, 60, .12f, 2, .04f); } }
                else if (b.st == 2 && --b.t <= 0)
                {
                    if (RectOv(p.x, p.x + p.w, p.y, p.y + p.h, b.x * T, b.y * T, T, T)) b.t = 10; else b.st = 0;
                }
            }
        }

        // ================================================== shop
        void Buy(int i)
        {
            if (shopTab == 1)
            {
                if ((skinOwned & (1 << i)) != 0) { skin = i; Persist(); shopMsg = "Equipped " + SkinN[i]; shopMsgT = 70; Sfx(600, 900, .08f, 2, .05f); return; }
                if (coins < SkinCost[i]) { shopMsg = "Not enough coins"; shopMsgT = 70; Sfx(200, 120, .15f, 0, .05f); return; }
                coins -= SkinCost[i]; skinOwned |= 1 << i; skin = i; Persist();
                shopMsg = "Bought " + SkinN[i] + "!"; shopMsgT = 70; Sfx(600, 1200, .12f, 2, .06f); Sfx(900, 1500, .12f, 2, .05f, .08f);
                return;
            }
            var u = Data.Upgrades[i]; int l = up[i];
            if (l >= u.max) { shopMsg = "Already maxed"; shopMsgT = 70; Sfx(200, 150, .1f, 0, .04f); return; }
            int c = u.cost[l];
            if (coins < c) { shopMsg = "Not enough coins"; shopMsgT = 70; Sfx(200, 120, .15f, 0, .05f); return; }
            coins -= c; up[i]++; Persist();
            if (u.id == "shield") shield++;
            shopMsg = "Bought " + u.name + "!"; shopMsgT = 70; Sfx(600, 1200, .12f, 2, .06f); Sfx(900, 1500, .12f, 2, .05f, .08f);
        }
        void OpenShop(State prev) { shopPrev = prev; state = State.Shop; shopSel = 0; shopTab = 0; shopMsg = ""; Sfx(500, 800, .08f, 2, .04f); }
        void UpdateShop()
        {
            if (shopMsgT > 0) shopMsgT--;
            if (Pressed(Act.Left) || Pressed(Act.Right)) { shopTab = 1 - shopTab; shopSel = 0; Sfx(500, 700, .05f, 2, .04f); }
            int n = shopTab == 0 ? Data.Upgrades.Length : SkinN.Length;
            if (Pressed(Act.Up)) shopSel = (shopSel + n - 1) % n;
            if (Pressed(Act.Down)) shopSel = (shopSel + 1) % n;
            if (Pressed(Act.Jump)) Buy(shopSel);
            if (Pressed(Act.Shop) || Pressed(Act.Pause) || Pressed(Act.Dash)) { Persist(); state = shopPrev; Sfx(500, 300, .08f, 2, .04f); }
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
            if (bgFadeT > 0)
            {
                bgFadeT--;
                if (bgFadeT == 0 && bgFrom >= 0) { DestroyBG(bgs[bgFrom]); bgs[bgFrom] = null; bgFrom = -1; }
            }

            if (state == State.Title)
            {
                camX = (tick * .35f) % Mathf.Max(1f, lv.WorldW - W);
                if (Pressed(Act.Unlock)) { unlocked = Data.Stages.Length - 1; PlayerPrefs.SetInt("dashline_unlocked", unlocked); Sfx(600, 1400, .2f, 2, .06f); }
                int maxSel = Mathf.Min(unlocked, Data.Stages.Length - 1) + 1;
                if (Pressed(Act.Up) && menuSel > 0) { menuSel = Mathf.Max(0, menuSel - 3); TitleScene(); }
                if (Pressed(Act.Down) && menuSel < maxSel) { menuSel = Mathf.Min(maxSel, menuSel + 3); TitleScene(); }
                if (Pressed(Act.Left) && menuSel > 0) { menuSel--; TitleScene(); }
                if (Pressed(Act.Right) && menuSel < maxSel) { menuSel++; TitleScene(); }
                if (Pressed(Act.Shop)) OpenShop(State.Title);
                if (Pressed(Act.Jump)) StartGame();
                return;
            }
            if (state == State.Shop) { UpdateShop(); return; }
            if (state == State.Win)
            {
                camX = (tick * .35f) % Mathf.Max(1f, lv.WorldW - W);
                if (Pressed(Act.Jump) || Pressed(Act.Restart)) { menuSel = 0; TitleScene(); }
                return;
            }
            if (state == State.Paused)
            {
                if (Pressed(Act.Pause) || Pressed(Act.Jump)) state = State.Play;
                if (Pressed(Act.Shop)) OpenShop(State.Paused);
                if (Pressed(Act.Quit)) ToTitle();
                return;
            }
            if (state == State.Clear)
            {
                if (--clearT <= 0)
                {
                    if (lvl + 1 >= Data.Stages.Length) state = State.Win;
                    else { LoadChapter(lvl + 1); state = State.Play; }
                }
                return;
            }
            if (Pressed(Act.Pause)) { state = State.Paused; return; }
            if (Pressed(Act.Shop)) { foreach (var s in shrines) if (Mathf.Abs(p.x + 3 - s.x) < 22 && p.ground) { OpenShop(State.Play); return; } }
            if (introT > 0) introT--;
            if (p.dead > 0) { if (--p.dead == 0) { Respawn(); fade = 10; } return; }
            if (freeze > 0) { freeze--; return; }
            if (Pressed(Act.Restart)) { Respawn(); return; }
            time++; sTime++;
            if (p.parryCd > 0) p.parryCd--;
            if (p.impactT > 0) p.impactT--;
            if (p.parryVis > 0) p.parryVis--;
            if (p.swing > 0) p.swing--;
            if (p.bounceT > 0) p.bounceT--;
            if (p.inv > 0) p.inv--;
            styl = Mathf.Max(0f, styl - (.22f + RankOf(styl) * .07f) * ((p.ground && Mathf.Abs(p.vx) < .2f) ? 2f : 1f));
            if (!p.ground) { p.airT++; if (p.airT % 24 == 0) AddStyle(8); } else p.airT = 0;

            // ---- zones, endless generation, distance
            int ci = Mathf.Clamp(Mathf.FloorToInt((p.x + 3) / CWP), 0, lv.Chunks - 1), ti = lv.chunkTheme[ci];
            if (ti != curTi) SetBackdrop(ti, false);
            if (endless)
            {
                EnsureAhead(); if (tick % 90 == 0) Prune();
                dist = Mathf.Max(dist, Mathf.FloorToInt((p.x - startX) / T));
                int zone = ci / 10;
                if (zone != zoneShown)
                {
                    zoneShown = zone;
                    if (zone > 0) { introT = 150; bannerT = th.name.ToUpper(); bannerS = "ZONE " + (zone + 1) + "  -  DISTANCE " + dist + "m"; Sfx(400, 800, .2f, 2, .05f); Sfx(600, 1200, .2f, 2, .05f, .15f); }
                }
            }

            int ix = (Held(Act.Right) ? 1 : 0) - (Held(Act.Left) ? 1 : 0);
            int iy = (Held(Act.Down) ? 1 : 0) - (Held(Act.Up) ? 1 : 0);
            bool jumpHeld = Held(Act.Jump);
            if (ix != 0) p.face = ix;
            p.sx = Approach(p.sx, 1, .06f); p.sy = Approach(p.sy, 1, .06f);

            UpdateEnts();
            p.riding = OnPlat();
            p.ground = Hits(p.x, p.y + 1) || p.riding != null;
            bool r = Hits(p.x + 1, p.y), l = Hits(p.x - 1, p.y);
            p.wdir = (r && l) ? ix : (r ? 1 : l ? -1 : 0);
            if (p.ground) p.coy = 6; else p.coy--;
            if (!p.ground && p.wdir != 0) { p.wcoy = 5; p.wd = p.wdir; } else p.wcoy--;
            p.jb = Pressed(Act.Jump) ? 6 : p.jb - 1;
            p.db = Pressed(Act.Dash) ? 5 : p.db - 1;
            if (p.dashes < MaxDash() && p.dashT <= 0) { if (++p.regen >= (p.ground ? 22 : 50)) { p.regen = 0; p.dashes++; Sfx(900, 1300, .05f, 2, .03f); } } else p.regen = 0;
            if (chainT > 0) chainT--;

            // crumble blocks start to fall when stood on
            if (p.ground && p.riding == null)
            {
                int cty = Mathf.FloorToInt((p.y + p.h + .5f) / T);
                for (int tx = Mathf.FloorToInt(p.x / T); tx <= Mathf.FloorToInt((p.x + p.w - .001f) / T); tx++)
                {
                    Crumble b;
                    if (lv.crumbles.TryGetValue(cty * 100000 + tx, out b) && b.st == 0) { b.st = 1; b.t = 20; Sfx(150, 100, .1f, 2, .04f); }
                }
            }

            // ---- gun: click = shoot at the mouse (Down in the air = straight down). Right click / F = toss a coin.
            aimV = AimDir(iy);
            if (gunKick > 0) gunKick--;
            if (coinCd > 0) coinCd--;
            for (int wi = 0; wi < 5; wi++) if (Pressed((Act)((int)Act.W1 + wi))) { weapon = wi; Sfx(600, 900, .05f, 2, .03f); }
            if (Pressed(Act.WNext)) { weapon = (weapon + 1) % 5; Sfx(600, 900, .05f, 2, .03f); }
            for (int wi2 = 0; wi2 < 5; wi2++) if (wi2 != weapon && Pressed((Act)((int)Act.W1 + wi2)) || Pressed(Act.WNext)) charge = 0;
            if (weapon == 3)
            {
                if (Held(Act.Parry))
                {
                    if (charge < 70) { charge++; if (charge % 7 == 0) Sfx(300 + charge * 12, 340 + charge * 14, .06f, 2, .03f); if (charge == 70) { Sfx(900, 1800, .2f, 2, .07f); shake = Mathf.Max(shake, 3); } }
                    if (charge % 3 == 0) { Vector2 mo = new Vector2(p.x + 3, p.y + p.h / 2f - 1) + aimV * 9f; Burst(mo.x + Random.Range(-6f, 6f), mo.y + Random.Range(-6f, 6f), 1, WCol[3], .5f); }
                    gunKick = Mathf.Max(gunKick, charge >= 70 ? 2 : 0);
                }
                else if (charge > 0)
                {
                    if (charge >= 12 && p.parryCd <= 0) { railPow = Mathf.Clamp01(charge / 70f); p.parryCd = 30; gunKick = 10; Shoot(aimV); }
                    charge = 0;
                }
            }
            else charge = 0;
            bool fireIn = weapon == 3 ? false : weapon == 4 ? Held(Act.Parry) : Pressed(Act.Parry);
            if (fireIn && p.parryCd <= 0) { p.parryCd = Mathf.Max(weapon == 4 ? 4 : 8, Mathf.RoundToInt(WCd[weapon] * (1f - .12f * up[1]))); gunKick = weapon == 3 ? 10 : weapon == 2 ? 8 : 5; Shoot(aimV); }
            if (Pressed(Act.Toss) && coinCd <= 0 && tossCoins.Count < 4) { coinCd = 12 - 2 * up[1]; ThrowCoin(iy); }
            UpdateCoins();
            UpdateArenas(); UpdateBoss();

            // ---- ground slam: Down + Jump in the air
            if (!p.ground && !p.slam && iy > 0 && Pressed(Act.Jump) && p.wcoy <= 0 && p.coy <= 0 && p.dashT <= 0)
            { p.slam = true; p.vy = 5.2f; p.vx *= .3f; p.jb = 0; shake = Mathf.Max(shake, 2); Sfx(200, 80, .25f, 1, .06f); AddStyle(10, "SLAM"); }

            // ---- dash start
            if (p.db > 0 && p.dashes > 0 && p.dashT <= 0)
            {
                p.dashes--; p.db = 0;
                float dx = ix, dy = iy;
                if (dx == 0 && dy == 0) dx = p.face;
                float m = Mathf.Sqrt(dx * dx + dy * dy); dx /= m; dy /= m;
                p.slam = false; p.dx = dx; p.dy = dy; p.dashT = 8; p.vx = dx * 3.6f; p.vy = dy * 3.6f; freeze = 3; shake = 3; p.lock_ = 0; p.riding = null;
                bool horiz = Mathf.Abs(dx) > .1f;
                p.sx = horiz ? 1.4f : .8f; p.sy = horiz ? .75f : 1.4f;
                Sfx(420, 90, .18f, 1, .06f); Burst(p.x + 3, p.y + p.h / 2f, 10, Color.white, 1.1f); AddStyle(p.ground ? 10 : 20, "DASH");
            }

            // ---- dashing
            p.dashing = false;
            if (p.dashT > 0 && !(p.jb > 0 && p.coy > 0))
            {
                p.dashT--; p.dashing = true;
                trail.Add(new TrailPt { x = p.x, y = p.y, l = 12, c = p.dashes > 0 ? SkinScarf[skin] : ScarfBlue });
                if (tick % 2 == 0) Burst(p.x + 3, p.y + p.h / 2f, 1, new Color(.75f, .9f, 1f), .3f);
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
                float maxR = 1.75f, accel = p.ground ? .36f : .2f;
                if (Mathf.Abs(p.vx) > maxR && Mathf.Sign(p.vx) == ix) p.vx = Approach(p.vx, ix * maxR, .04f);
                else p.vx = Approach(p.vx, ix * maxR, ix != 0 ? accel : (p.ground ? .35f : .08f));
            }

            // ---- gravity / wall slide
            float g = .13f;
            if (Mathf.Abs(p.vy) < .5f && jumpHeld) g *= .5f;
            if (p.vy < 0 && !jumpHeld && p.bounceT <= 0) g *= 2f;
            p.vy = p.slam ? 5.2f : Mathf.Min(p.vy + g, 3.4f);
            bool sliding = !p.ground && p.wdir != 0 && ix == p.wdir && p.vy > 0;
            if (sliding) { p.vy = Mathf.Min(p.vy, .7f); if (tick % 4 == 0) Dust(p.x + (p.wdir > 0 ? 6 : 0), p.y + p.h - 2, 1); }

            // ---- jump / wall jump
            if (p.jb > 0)
            {
                if (p.coy > 0)
                {
                    p.vy = -2.7f; p.vx += ix * .25f; p.jb = 0; p.coy = 0; p.sx = .7f; p.sy = 1.3f; p.riding = null;
                    Sfx(300, 600, .12f, 0, .04f); Dust(p.x + 3, p.y + p.h, 6);
                }
                else if (p.wcoy > 0)
                {
                    int d = -p.wd;
                    p.vx = d * 1.9f; p.vy = -2.5f; p.jb = 0; p.wcoy = 0; p.face = d; p.lock_ = 8; p.sx = .75f; p.sy = 1.25f;
                    Sfx(350, 700, .12f, 0, .04f); Dust(p.x + (d > 0 ? 0 : 6), p.y + p.h / 2f, 6); AddStyle(30, "WALL JUMP");
                }
            }

            MoveX(p.vx);
            float vyB = p.vy;
            bool landed = MoveY(p.vy);
            if (landed && p.slam) Shockwave();
            else if (landed && vyB > 1.8f) { Dust(p.x + 3, p.y + p.h, 6); Sfx(120, 60, .06f, 2, .05f); p.sx = 1.35f; p.sy = .7f; }
            if (p.ground && Mathf.Abs(p.vx) > .8f) { p.anim += Mathf.Abs(p.vx) * .16f; if (tick % 9 == 0) Dust(p.x + 3, p.y + p.h, 1); }
            else if (!p.ground) p.anim = 0;

            UpdateHair(); CheckWorld(); UpdateCam();
        }


        void UpdateHair()
        {
            float px = p.x + 3 - p.face * 2, py = p.y + p.h - 10;
            for (int i = 0; i < p.hair.Length; i++)
            {
                var h = p.hair[i];
                h.x = Mathf.Lerp(h.x, px - p.face * 1.2f, .45f); h.y = Mathf.Lerp(h.y, py + .8f, .45f);
                p.hair[i] = h; px = h.x; py = h.y;
            }
        }
        void UpdateCam()
        {
            float tx = Mathf.Clamp(p.x + 3 - W / 2f + p.face * 22, 0, Mathf.Max(0, lv.WorldW - W));
            float cs = .1f;
            foreach (var ar in arenas) if (ar.big && ar.started && !ar.done) { tx = Mathf.Clamp((ar.x0 + ar.x1) / 2f - W / 2f, 0, Mathf.Max(0, lv.WorldW - W)); cs = .07f; break; }
            camX += (tx - camX) * cs;
        }

        // ================================================== rendering
        void PlaceSword(SpriteRenderer sr, float x, float y, float phi, float a)
        {
            sr.enabled = true;
            sr.transform.position = new Vector3(Mathf.Round(x), -Mathf.Round(y), 0);
            sr.transform.rotation = Quaternion.Euler(0, 0, -phi * Mathf.Rad2Deg);
            sr.color = new Color(1, 1, 1, a);
        }
        bool Vis(float x) { return x > camX - 40 && x < camX + W + 40; }
        int glowN;
        void GlowAt(float x, float y, float r, Color col, float a)
        {
            if (glowN >= glowSR.Length) return;
            var sr = glowSR[glowN++];
            sr.enabled = true; sr.color = new Color(col.r, col.g, col.b, a);
            sr.transform.position = new Vector3(x, -y, 0); sr.transform.localScale = Vector3.one * (r * 2f / 64f);
        }

        // ================================================== rendering
        void RenderChunks()
        {
            int kc = Mathf.FloorToInt(camX / CWP);
            for (int k = Mathf.Max(0, kc - 1); k <= kc + 2 && k < lv.Chunks; k++)
            {
                if (chunkSR.ContainsKey(k)) continue;
                var spr = Art.TileChunk(lv, Data.Themes[lv.chunkTheme[k]], k);
                var sr = MakeSR(stageRoot, spr, -50, new Vector3(k * CWP, -H, 0));
                sr.name = "Chunk" + k;
                chunkSR[k] = sr;
            }
        }
        void RenderBackdrop(float cx)
        {
            if (bgCur < 0) return;
            if (bgFrom >= 0)
            {
                float t = 1f - bgFadeT / 80f;
                if (bgCur == 1) { SetAlpha(bgs[1], t); SetAlpha(bgs[0], 1f); }
                else { SetAlpha(bgs[0], 1f); SetAlpha(bgs[1], 1f - t); }
            }
            else SetAlpha(bgs[bgCur], 1f);
            for (int s = 0; s < 2; s++)
            {
                var b = bgs[s]; if (b == null) continue;
                for (int i = 0; i < b.mt.Length; i++)
                    b.mt[i].transform.position = new Vector3(cx - Mathf.Repeat(Art.MountainParallax(i) * cx, Art.Period), -H, 0);
                for (int i = 0; i < b.stal.Length; i++)
                    if (b.stal[i] != null) b.stal[i].transform.position = new Vector3(cx - Mathf.Repeat(Art.StalParallax(i) * cx, Art.Period), -H, 0);
                if (b.th.clouds != null)
                    for (int i = 0; i < b.cloud.Length; i++)
                    {
                        float sp = .5f + (i % 3) * .3f;
                        float x = Mathf.Repeat(i * 83 - camX * .06f - tick * .06f * sp, W + 90) - 45, y = 14 + i * 13;
                        b.cloud[i].transform.position = new Vector3(cx + x - 10, -(y + 10), 0);
                    }
            }
        }
        void RenderEnts()
        {
            // crystals
            foreach (var c in crystals)
            {
                bool v = Vis(c.x); c.sr.enabled = v; if (!v) continue;
                float bob = Mathf.Sin(tick * .08f + c.x) * 1.4f;
                c.sr.transform.position = new Vector3(c.x, -(c.y + bob), 0);
                c.sr.color = c.cd > 0 ? new Color(1, 1, 1, .2f) : Color.white;
                if (c.cd <= 0) GlowAt(c.x, c.y, 26, new Color(.25f, 1f, .63f), .4f);
            }
            foreach (var k in cps)
            {
                bool v = Vis(k.x); k.sr.enabled = v; if (!v) continue;
                k.sr.sprite = k.lit ? lanternOn : lanternOff;
                if (k.lit) GlowAt(k.x, k.y - 11, 28, new Color(1f, .83f, .3f), .45f);
            }
            foreach (var c in pickups)
            {
                bool v = Vis(c.x); c.sr.enabled = v; if (!v) continue;
                float bob = Mathf.Sin(tick * .1f + c.ph) * 1.2f, sx = Mathf.Abs(Mathf.Cos(tick * .08f + c.ph));
                c.sr.transform.position = new Vector3(Mathf.Round(c.x), -Mathf.Round(c.y + bob), 0);
                c.sr.transform.localScale = new Vector3(Mathf.Max(.25f, sx), 1f, 1f);
            }
            foreach (var s in shrines)
            {
                bool v = Vis(s.x); s.sr.enabled = v; if (!v) continue;
                s.sr.transform.position = new Vector3(s.x, -s.y, 0);
                GlowAt(s.x, s.y - 12, 34, new Color(1f, .83f, .3f), .35f + .1f * Mathf.Sin(tick * .06f));
            }
            foreach (var q in plats)
            {
                bool v = Vis(q.x); q.sr.enabled = v; if (!v) continue;
                q.sr.transform.position = new Vector3(Mathf.Round(q.x), -Mathf.Round(q.y), 0);
            }
            foreach (var s in saws)
            {
                bool v = Vis(s.x); s.sr.enabled = v; if (!v) continue;
                s.sr.transform.position = new Vector3(Mathf.Round(s.x), -Mathf.Round(s.y), 0);
                s.sr.transform.rotation = Quaternion.Euler(0, 0, -tick * 14f);
            }
            foreach (var e in enemies)
            {
                bool v = e.alive && Vis(e.x); e.sr.enabled = v; if (!v) continue;
                e.sr.transform.position = new Vector3(Mathf.Round(e.x + e.w / 2f), -Mathf.Round(e.y + e.h / 2f), 0);
                if (e.kind == 0) e.sr.transform.localScale = new Vector3(e.dir < 0 ? -1 : 1, 1 + (e.anim / 8 % 2) * .06f, 1);
                else e.sr.sprite = (tick / 8) % 2 == 0 ? batSprA : batSprB;
            }
            foreach (var t in turrets)
            {
                bool v = Vis(t.x); t.sr.enabled = v; if (!v) continue;
                t.sr.transform.position = new Vector3(t.x + 4, -(t.y + 4), 0);
                t.sr.transform.localScale = new Vector3(t.dir < 0 ? 1 : -1, 1, 1);
            }
            foreach (var b in bullets)
            {
                b.sr.transform.position = new Vector3(Mathf.Round(b.x), -Mathf.Round(b.y), 0);
                b.sr.transform.localScale = new Vector3(b.vx < 0 ? 1 : -1, 1, 1); b.sr.color = b.refl ? new Color(.4f, 1f, 1f) : Color.white;
                GlowAt(b.x, b.y, 10, new Color(1f, .6f, .2f), .5f);
            }
            foreach (var b in lv.crumbles.Values)
            {
                bool v = b.st != 2 && Vis(b.x * T); b.sr.enabled = v; if (!v) continue;
                float jx = b.st == 1 ? (tick % 2 == 0 ? .6f : -.6f) : 0f;
                b.sr.transform.position = new Vector3(b.x * T + jx, -(b.y * T), 0);
            }
            foreach (var a in arenas)
            {
                bool closed = lv.closedGates.Contains(a.gl), v = Vis(a.x0);
                a.gateL.enabled = a.gateR.enabled = v;
                float ga = closed ? .85f + .15f * Mathf.Sin(tick * .2f) : .18f;
                a.gateL.color = a.gateR.color = new Color(1, 1, 1, ga);
                var b = a.boss;
                if (b != null)
                {
                    bool vb = !b.dead && Vis(b.x); b.sr.enabled = vb;
                    if (vb)
                    {
                        float jx = (b.kind == 1 && b.st == 1) ? (tick % 2 == 0 ? 1f : -1f) : 0f;
                        b.sr.transform.position = new Vector3(Mathf.Round(b.x + b.w / 2f + jx), -Mathf.Round(b.y + b.h), 0);
                        b.sr.transform.localScale = new Vector3(b.dir, 1, 1);
                        b.sr.color = b.flash > 0 ? new Color(1f, .45f, .45f) : (b.active ? Color.white : new Color(.7f, .7f, .7f));
                        GlowAt(b.x + b.w / 2f, b.y + b.h / 2f, 40, new Color(1f, .2f, .1f), .22f);
                    }
                }
            }
            // goal
            if (hasGoal && goalSR != null)
            {
                float gx = goalX * T + 4, gy = goalY * T - 6;
                for (int i = 0; i < goalSpark.Count; i++)
                {
                    float a = tick * .05f + i * 1.26f;
                    goalSpark[i].transform.position = new Vector3(gx + Mathf.Cos(a) * 6, -(gy + Mathf.Sin(a) * 11), 0);
                }
                if (Vis(gx)) GlowAt(gx, gy, 40, new Color(1f, .83f, .3f), .5f);
            }
        }

        void Render()
        {
            float ox = 0, oy = 0;
            if (shake > 0) { ox = Mathf.Round((Random.value - .5f) * shake); oy = Mathf.Round((Random.value - .5f) * shake); }
            float cx = Mathf.Round(camX);
            cam.transform.position = new Vector3(cx + W / 2f + ox, -H / 2f + oy, -10f);

            glowN = 0;
            bool inGame = state == State.Play || state == State.Paused || state == State.Clear || state == State.Shop;
            RenderBackdrop(cx);
            RenderChunks();
            RenderEnts();

            // player
            bool show = inGame && p.dead <= 0;
            if (p.inv > 0 && (tick / 3) % 2 == 0) show = false;
            playerRoot.gameObject.SetActive(show);
            foreach (var s in scarfSR) s.enabled = show;
            if (show)
            {
                bool fl = p.dashT > 0, run = p.ground && Mathf.Abs(p.vx) > .5f;
                int f = p.face, step = Mathf.FloorToInt(p.anim) % 2;
                int bob = (p.ground && !run) ? (Mathf.Sin(tick * .08f) > .4f ? 1 : 0) : 0;
                Color scarf = p.dashes > 0 ? SkinScarf[skin] : ScarfBlue;
                playerRoot.position = new Vector3(Mathf.Round(p.x + 3), -Mathf.Round(p.y + p.h), 0);
                playerRoot.localScale = new Vector3(p.sx, p.sy, 1);
                Color W_ = Color.white;
                foreach (var q in pl) q.enabled = false;
                int fr;
                if (fl) fr = 7;
                else if (!p.ground) fr = (p.wdir != 0 && p.vy > 0) ? 6 : (p.vy < 0 ? 3 : 4);
                else fr = run ? 1 + step : 0;
                bodySR.enabled = true; bodySR.sprite = skinFrames[skin][fr];
                bodySR.transform.localPosition = new Vector3(0, skin == 5 ? 1.5f + Mathf.Sin(tick * .1f) : 0, 0);
                bodySR.transform.localScale = new Vector3(p.face, 1, 1);
                for (int i = 0; i < accSR.Length; i++) accSR[i].enabled = false;

                float[] sizes = { 3f, 2.6f, 2.2f, 1.6f, 1.1f };
                for (int i = 0; i < scarfSR.Length; i++)
                {
                    var h = p.hair[i];
                    Rc(scarfSR[i], Mathf.Round(h.x - sizes[i] / 2f), Mathf.Round(h.y - sizes[i] / 2f), Mathf.Ceil(sizes[i]), Mathf.Ceil(sizes[i]));
                    scarfSR[i].color = fl ? W_ : scarf;
                }
                GlowAt(p.x + 3, p.y + p.h / 2f, 46, Art.Hex(th.glow), .22f);
                if (fl) GlowAt(p.x + 3, p.y + p.h / 2f, 30, Color.white, .25f);
                if (shield > 0) GlowAt(p.x + 3, p.y + p.h / 2f, 22, new Color(.49f, .82f, 1f), .3f + .1f * Mathf.Sin(tick * .1f));
            }

            gunSR.enabled = show;
            if (show)
            {
                float ang = Mathf.Atan2(-aimV.y, aimV.x) * Mathf.Rad2Deg;
                float kick = gunKick > 0 ? gunKick * .6f : 0f;
                Vector2 gp = new Vector2(p.x + 3, p.y + p.h / 2f - 1) - aimV * kick;
                gunSR.transform.position = new Vector3(Mathf.Round(gp.x), -Mathf.Round(gp.y), 0);
                gunSR.transform.rotation = Quaternion.Euler(0, 0, ang);
                gunSR.transform.localScale = new Vector3(1, aimV.x < 0 ? -1 : 1, 1); gunSR.sprite = gunSpr[weapon];
            }
            for (int i = 0; i < tracerSR.Length; i++)
            {
                if (i < tracers.Count && inGame)
                {
                    var tr = tracers[i]; float tdx = tr.x1 - tr.x0, tdy = tr.y1 - tr.y0, len = Mathf.Max(1f, Mathf.Sqrt(tdx * tdx + tdy * tdy));
                    tracerSR[i].enabled = true;
                    tracerSR[i].transform.position = new Vector3((tr.x0 + tr.x1) / 2f, -(tr.y0 + tr.y1) / 2f, 0);
                    tracerSR[i].transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(-tdy, tdx) * Mathf.Rad2Deg);
                    tracerSR[i].transform.localScale = new Vector3(len, tr.life > 3 ? tr.wd : tr.wd * .6f, 1);
                    tracerSR[i].color = new Color(tr.c.r, tr.c.g, tr.c.b, Mathf.Clamp01(tr.life / 6f));
                }
                else tracerSR[i].enabled = false;
            }
            foreach (var tc in tossCoins)
            {
                tc.sr.transform.position = new Vector3(Mathf.Round(tc.x), -Mathf.Round(tc.y), 0);
                tc.sr.transform.localScale = new Vector3(Mathf.Max(.3f, Mathf.Abs(Mathf.Cos(tick * .25f))), 1f, 1f);
                GlowAt(tc.x, tc.y, 12, new Color(1f, .85f, .3f), .5f);
            }
            if (show && p.parryVis > 0)
            {
                parryRing.enabled = true;
                parryRing.transform.position = new Vector3(p.x + 3, -(p.y + p.h / 2f), 0);
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
                    Rc(trailSR[i], Mathf.Round(t.x), Mathf.Round(t.y), 6, p.h);
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
            float so = Mathf.Max(1f, Mathf.Floor(sc * .25f));
            GUI.Label(new Rect(r.x + so, r.y + so, r.width, r.height), s, style);
            style.normal.textColor = col;
            GUI.Label(r, s, style);
        }
        static string Fmt(int t) { float s = t / 60f; return ((int)(s / 60f)).ToString("00") + ":" + (s % 60f).ToString("00.0"); }

        void Coin(float x, float y) { Box(x, y, 5, 5, new Color(.79f, .54f, .06f)); Box(x + 1, y + 1, 3, 3, new Color(1f, .83f, .3f)); }

        void DrawShop(Color white, Color cyan, Color gold)
        {
            var C = TextAnchor.UpperCenter; var Lf = TextAnchor.UpperLeft; var Rt = TextAnchor.UpperRight;
            Box(0, 0, W, H, new Color(.03f, .03f, .09f, .93f));
            Txt("SHOP", W / 2f, 4, gold, 16, C);
            Coin(W - 54, 8); Txt(coins.ToString(), W - 46, 7, gold, 9, Lf);
            Txt("< UPGRADES >", 70, 8, shopTab == 0 ? white : new Color(.5f, .55f, .75f), 8, C);
            Txt("< SKINS >", 250 - 70, 8, shopTab == 1 ? white : new Color(.5f, .55f, .75f), 8, C);
            int n = shopTab == 0 ? Data.Upgrades.Length : SkinN.Length;
            for (int i = 0; i < n; i++)
            {
                float y = 26 + i * (shopTab == 0 ? 18 : 17); bool sel = i == shopSel;
                Box(14, y, W - 28, shopTab == 0 ? 18 : 15, sel ? new Color(1f, .83f, .3f, .18f) : new Color(1, 1, 1, .06f));
                if (sel) { Box(14, y, W - 28, 1, gold); Box(14, y + (shopTab == 0 ? 17 : 14), W - 28, 1, gold); }
                if (shopTab == 0)
                {
                    var u = Data.Upgrades[i]; int lvN = up[i];
                    Txt(u.name, 20, y + 5, sel ? white : new Color(.78f, .81f, .94f), 8, Lf);
                    for (int q = 0; q < u.max; q++) Box(130 + q * 9, y + 6, 7, 6, q < lvN ? new Color(.49f, 1f, .69f) : new Color(1, 1, 1, .2f));
                    if (lvN >= u.max) Txt("MAXED", W - 20, y + 5, new Color(.49f, 1f, .69f), 8, Rt);
                    else { int c = u.cost[lvN]; Coin(W - 52, y + 6); Txt(c.ToString(), W - 20, y + 5, coins >= c ? gold : new Color(1f, .48f, .48f), 8, Rt); }
                }
                else
                {
                    Box(20, y + 3, 5, 9, SkinHood[i]); Box(26, y + 3, 5, 9, SkinTorso[i]); Box(32, y + 3, 5, 9, SkinScarf[i]);
                    Txt(SkinN[i], 44, y + 4, sel ? white : new Color(.78f, .81f, .94f), 8, Lf);
                    if (skin == i) Txt("EQUIPPED", W - 20, y + 4, new Color(.49f, 1f, .69f), 8, Rt);
                    else if ((skinOwned & (1 << i)) != 0) Txt("OWNED", W - 20, y + 4, cyan, 8, Rt);
                    else { Coin(W - 52, y + 5); Txt(SkinCost[i].ToString(), W - 20, y + 4, coins >= SkinCost[i] ? gold : new Color(1f, .48f, .48f), 8, Rt); }
                }
            }
            if (shopTab == 0) Txt(Data.Upgrades[shopSel].desc, W / 2f, 140, cyan, 8, C);
            if (shopMsgT > 0) Txt(shopMsg, W / 2f, 153, shopMsg.StartsWith("Bought") || shopMsg.StartsWith("Equipped") ? new Color(.49f, 1f, .69f) : new Color(1f, .48f, .48f), 8, C);
            Txt("Left/Right tab   Up/Down select   Z buy/equip   B back", W / 2f, H - 12, new Color(.56f, .6f, .78f), 8, C);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || cam == null) return;
            if (style == null) { style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow }; }
            Rect pr = cam.pixelRect;
            sc = pr.height / H; vpx = pr.x; vpy = Screen.height - pr.yMax;
            Color white = Color.white, cyan = new Color(.8f, .84f, 1f), gold = new Color(1f, .83f, .3f);
            var C = TextAnchor.UpperCenter; var Lf = TextAnchor.UpperLeft; var Rt = TextAnchor.UpperRight;

            if (flash > 0) Box(0, 0, W, H, new Color(1, 1, 1, flash * .06f));

            if (state == State.Shop) { DrawShop(white, cyan, gold); return; }
            if (state == State.Title)
            {
                Box(0, 0, W, H, new Color(0, 0, 0, .4f));
                float bob = Mathf.Sin(tick * .04f) * 2f;
                Txt("DASHLINE", W / 2f, 24 + bob, white, 28, C);
                Box(W / 2f - 60, 56 + bob, 120, 2, new Color(1f, .35f, .35f));
                Txt("a precision platformer", W / 2f, 64, new Color(.9f, .93f, 1f), 8, C);
                int maxSel = Mathf.Min(unlocked, Data.Stages.Length - 1) + 1;
                string nm = menuSel == 0 ? "ENDLESS" : "STAGE " + menuSel + ": " + Data.Stages[menuSel - 1].name;
                Txt((menuSel > 0 ? "< " : "  ") + nm + (menuSel < maxSel ? " >" : "  "), W / 2f, 88, gold, 10, C);
                Txt(menuSel == 0 ? "Infinite procedural run  -  best " + best + "m" : Data.Themes[Data.Stages[menuSel - 1].theme].name, W / 2f, 102, new Color(.75f, .8f, 1f), 8, C);
                Txt("Left/Right: stage   Up/Down: +-3   U: unlock all", W / 2f, H - 10, new Color(.6f, .65f, .85f), 6, C);
                Coin(W / 2f - 36, 117); Txt(coins + "   B = SHOP / SKINS", W / 2f - 28, 116, gold, 8, Lf);
                if ((tick / 30) % 2 == 0) Txt("Press Z / Space / Enter to play", W / 2f, 136, white, 8, C);
                Txt("Arrows move  Z jump  X dash  Click shoot  R-click coin  R retry", W / 2f, H - 14, new Color(.67f, .71f, .91f), 8, C);
            }
            else
            {
                Box(0, 0, W, 12, new Color(0, 0, 0, .42f));
                Txt(endless ? "ENDLESS  " + dist + "m" : (lvl + 1) + "/" + Data.Stages.Length + " " + Data.Stages[lvl].name, 3, 2, cyan, 8, Lf);
                Txt("x" + deaths + "  " + Fmt(time), W - 3, 2, cyan, 8, Rt);
                Coin(100, 3); Txt(coins.ToString(), 107, 2, gold, 8, Lf);
                float bx = 150, bw = 70;
                if (endless) Txt("best " + Mathf.Max(best, dist) + "m", bx, 2, new Color(.67f, .71f, .91f), 8, Lf);
                else
                {
                    float prog = Mathf.Clamp01(p.x / Mathf.Max(1f, lv.WorldW));
                    Box(bx, 5, bw, 3, new Color(1, 1, 1, .2f)); Box(bx, 5, bw * prog, 3, gold);
                    foreach (var k in cps) Box(bx + (k.x / lv.WorldW) * bw, 4, 1, 5, k.lit ? new Color(1f, .96f, .75f) : new Color(1, 1, 1, .5f));
                }
                for (int d = 0; d < Mathf.Max(MaxDash(), p.dashes); d++) Box(bx - 6 - Mathf.Max(MaxDash(), p.dashes) * 6 + d * 6, 4, 5, 5, p.dashes > d ? SkinScarf[skin] : ScarfBlue);
                for (int s = 0; s < shield; s++) Box(3 + s * 7, 24, 5, 5, new Color(.49f, .82f, 1f));
                {
                    float mh = MaxHp(), fr = Mathf.Clamp01(hp / mh), bwid = 70f;
                    Color hc = fr > .5f ? Color.Lerp(new Color(1f, .8f, .2f), new Color(.3f, .9f, .4f), (fr - .5f) * 2f) : Color.Lerp(new Color(.9f, .12f, .15f), new Color(1f, .8f, .2f), fr * 2f);
                    if (fr < .3f && (tick / 6) % 2 == 0) hc = Color.Lerp(hc, Color.white, .5f);
                    bwid = 90f; Box(2, 11, bwid + 2, 11, new Color(0, 0, 0, .7f)); Box(3, 12, bwid, 9, new Color(.25f, .05f, .08f));
                    Box(3, 12, bwid * fr, 9, hc); Box(3, 12, bwid * fr, 2, new Color(1, 1, 1, .35f));
                    Txt(Mathf.CeilToInt(hp) + "/" + Mathf.RoundToInt(mh), 3 + bwid / 2f, 13, white, 7, C);
                    Txt(WName[weapon], 3, H - 21, WCol[weapon], 6, Lf);
                    if (weapon == 3 && charge > 0)
                    {
                        float cf = charge / 70f; bool full = charge >= 70;
                        Box(W / 2f - 31, H - 16, 62, 7, new Color(0, 0, 0, .7f));
                        Box(W / 2f - 30, H - 15, 60f * cf, 5, full ? (((tick / 3) % 2 == 0) ? Color.white : WCol[3]) : Color.Lerp(new Color(.2f, .5f, .8f), WCol[3], cf));
                        Box(W / 2f - 30 + 60f * .6f, H - 15, 1, 5, new Color(0, 0, 0, .6f));
                        Txt(full ? "LAUNCH!" : "CHARGING", W / 2f, H - 25, full ? Color.white : WCol[3], 6, C);
                    }
                    for (int wi = 0; wi < 5; wi++)
                    {
                        float wx = 3 + wi * 15; bool ws = wi == weapon;
                        Box(wx, H - 13, 14, 10, ws ? new Color(WCol[wi].r, WCol[wi].g, WCol[wi].b, .85f) : new Color(0, 0, 0, .55f));
                        Txt((wi + 1).ToString(), wx + 7, H - 12, ws ? new Color(.05f, .05f, .1f) : new Color(.7f, .72f, .85f), 6, C);
                    }
                }
                {
                    int rk = RankOf(styl);
                    float lo = RankAt[rk], hi = rk + 1 < RankAt.Length ? RankAt[rk + 1] : 1100f, fr = Mathf.Clamp01((styl - lo) / (hi - lo));
                    Color rc = RankCol[rk];
                    if (styl > 1f || feed.Count > 0)
                    {
                        Txt(Ranks[rk], W - 5 + (rk >= 5 ? Random.Range(-1f, 1f) : 0f), 14 + (rk >= 5 ? Random.Range(-1f, 1f) : 0f), rc, rk >= 4 ? 22 : 18, Rt);
                        Box(W - 46, 39, 42, 4, new Color(0, 0, 0, .5f)); Box(W - 45, 40, 40 * fr, 2, rc);
                        Txt(RankName[rk], W - 5, 45, rc, 6, Rt);
                    }
                    for (int i = 0; i < feed.Count; i++)
                    {
                        var fc = feed[i].c; fc.a = Mathf.Clamp01(feed[i].t / 20f);
                        Txt(feed[i].s, W - 5, 55 + i * 9, fc, 8, Rt);
                    }
                }
                foreach (var a in arenas)
                {
                    var bo = a.boss;
                    if (bo != null && bo.active && !bo.dead)
                    {
                        Box(W / 2f - 71, 14, 142, 7, new Color(0, 0, 0, .65f));
                        Box(W / 2f - 70, 15, 140f * Mathf.Clamp01(bo.hp / (float)bo.maxhp), 5, new Color(.9f, .12f, .12f));
                        Txt(BossName[bo.kind], W / 2f, 22, white, 6, C);
                    }
                }
                if (state == State.Play && p.ground)
                    foreach (var s in shrines) if (Mathf.Abs(p.x + 3 - s.x) < 22) Txt("Q = SHOP", W / 2f, H - 26, gold, 8, C);

                if (introT > 0 && state == State.Play)
                {
                    float tot = endless ? 150f : 240f;
                    float a = Mathf.Clamp01(Mathf.Min(introT / 40f, (tot - introT) / 20f));
                    Txt(bannerS, W / 2f, 38, new Color(gold.r, gold.g, gold.b, a), 8, C);
                    Txt(bannerT, W / 2f, 48, new Color(1, 1, 1, a), 16, C);
                    string hint = endless ? (zoneShown == 0 ? "Click: shoot   R-click: coin   Shoot the coin under you to bounce" : "") : Data.Stages[lvl].hint;
                    if (hint.Length > 0) { Box(0, H - 13, W, 13, new Color(0, 0, 0, .5f)); Txt(hint, W / 2f, H - 11, white, 8, C); }
                }
                if (state == State.Paused)
                {
                    Box(0, 0, W, H, new Color(0, 0, 0, .6f));
                    Txt("PAUSED", W / 2f, 60, white, 16, C); Txt("P / Z resume    B shop    T quit to title", W / 2f, 86, cyan, 8, C);
                }
                if (state == State.Clear)
                {
                    float a = Mathf.Clamp01((130 - clearT) / 15f);
                    Box(0, 0, W, H, new Color(0, 0, 0, .55f * a));
                    Txt("STAGE CLEAR", W / 2f, 56, new Color(gold.r, gold.g, gold.b, a), 16, C);
                    Txt("Time " + Fmt(sTime) + "    Deaths " + sDeaths, W / 2f, 84, new Color(1, 1, 1, a), 8, C);
                    var bc = RankCol[bestRank]; Txt("Best style " + Ranks[bestRank] + "   (" + sStyle + " pts)", W / 2f, 98, new Color(bc.r, bc.g, bc.b, a), 10, C);
                    Txt("Coins +" + (sDeaths == 0 ? 30 : 15), W / 2f, 114, new Color(gold.r, gold.g, gold.b, a), 8, C);
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
                if (p.dead > 0 && endless && state == State.Play) Txt(dist + "m", W / 2f, 70, white, 12, C);
            }
            if (fade > 0) Box(0, 0, W, H, new Color(0, 0, 0, fade / 20f));
        }
    }
}
