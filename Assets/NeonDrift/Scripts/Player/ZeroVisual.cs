using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 零的像素外观（PixelLab 版）。StreamingAssets/Zero2 下每个动作按方向一张条带（s 正面 / e 朝右 / n 背面 / w 朝左），
    /// 每帧 240×180，脚底在离下边 4 像素的那一行：
    /// idle 站立 4 帧、walk 走路 6 帧、run 跑步 6 帧、dash 闪移 4 帧（起步 / 冲刺 / 冲刺 / 收势）、
    /// shoot 站着开枪 8 帧（举枪 3 / 瞄准 / 后坐 2 / 放下 2）、walkshoot 边走边打 12 帧（前 6 帧瞄准，后 6 帧后坐）。
    /// 正面开枪的图是朝右下瞄的，往左下打时镜像。
    /// </summary>
    public class ZeroVisual : MonoBehaviour
    {
        public bool Ready { get; private set; }

        const float PPU = 56f;                  // 人物 148 像素高 ≈ 2.6 个单位，和原来的大小差不多
        const int CellW = 240, CellH = 180, Foot = 4;

        enum GunPhase { Off, Raise, Aim, Recoil, Lower }

        SpriteRenderer body, gun;
        readonly Dictionary<string, Sprite[]> anims = new Dictionary<string, Sprite[]>();
        readonly List<Object> keep = new List<Object>();

        GunPhase gunPhase;
        float gunTime, hitTime, locoTime;
        int loco = -1;
        string locoDir = "";
        bool hitting;
        Vector2 aim = Vector2.right;

        // 风衣飘动
        Rigidbody2D rb;
        MaterialPropertyBlock mpb;
        float coatPhase, coatFlow;
        static readonly int FrameId = Shader.PropertyToID("_Frame"), CoatId = Shader.PropertyToID("_Coat");

        public static ZeroVisual Attach(SpriteRenderer body, SpriteRenderer visor, SpriteRenderer gun, Health hp)
        {
            var visual = body.transform.parent.gameObject.AddComponent<ZeroVisual>();
            if (!visual.TryLoad(body, gun))
            {
                DestroyImmediate(visual);
                return null;
            }
            body.color = Color.white;
            if (visor != null) DestroyImmediate(visor.gameObject);
            hp.CaptureColors();
            Debug.Log("零的动作已接入（PixelLab 版，四方向）：站立、走路、跑步、闪移、开枪、边走边打。");
            return visual;
        }

        // ---------------------------------------------------------------- 持武器姿势 + 八方向
        /// <summary>
        /// 现在手上这类武器用哪套姿势：pistol 单手、rifle 双手长枪、heavy 重武器、thrown 投掷，近战时 slash / thrust / blunt。
        /// 动作图按「动作_姿势_方向」找，比如 idle_rifle_ne；找不到就退到「动作_方向」（现在的四方向图）。
        /// 带姿势的图里手是空的（摆好握枪的姿势），武器由 PlayerController 单独画一层拿在手上。
        /// </summary>
        public string Pose = "pistol";
        /// <summary>手上那层武器图要不要显示（用画死的手枪开枪时不显示，免得手里两把枪）。</summary>
        public bool LayerVisible { get; private set; } = true;
        /// <summary>这一下开枪用的是人物图里画死的那把手枪（枪口在伸直的手臂末端）。</summary>
        public bool BakedPistol { get; private set; }

        public static string PoseOf(WeaponDef w, bool melee)
        {
            if (w == null) return "pistol";
            if (melee || w.melee > 0) return w.melee == 3 ? "thrust" : w.melee == 2 ? "blunt" : "slash";
            if (w.thrown) return "thrown";
            switch (w.category)
            {
                case "手枪": return "pistol";
                case "机枪": case "爆炸武器": return "heavy";
                default: return "rifle";
            }
        }

        static readonly string[] Dir8 = { "e", "ne", "n", "nw", "w", "sw", "s", "se" };
        static string Key8(Vector2 v)
        {
            float a = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            return Dir8[((Mathf.RoundToInt(a / 45f) % 8) + 8) % 8];
        }
        string dir8 = "s";
        Vector2 lastLook = Vector2.down;
        /// <summary>八方向里离它最近的四方向（斜方向归到左右，和 PlayerController 判断朝向的规则一致）。</summary>
        static string Key4(Vector2 v) => Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0 ? "e" : "w") : (v.y > 0 ? "n" : "s");
        bool Has(string anim, string dir) => anims.ContainsKey(anim + "_" + dir);

        /// <summary>告诉外观现在瞄的方向（正面开枪时决定往左下还是右下）。</summary>
        public void SetAim(Vector2 a) { if (a.sqrMagnitude > 0.0001f) aim = a; }

        Vector2 moveDir;

        // ---------------------------------------------------------------- 近战挥砍（竖劈 melee_v / 横劈 melee_h，刀和刀光画在人物图上）
        // 每套 8 帧：0 站姿、1–2 蓄力、3 出刀、4 劈到底、5–6 收刀、7 站姿。游戏里伤害是按下就结算的，所以从蓄力的最后一帧开始快放。
        static readonly int[] SlashSeq = { 2, 3, 4, 5, 6, 7 };
        static readonly float[] SlashDur = { 0.04f, 0.04f, 0.06f, 0.07f, 0.07f, 0.08f };
        float slashT = -1f;
        string slashKey, slashFxKey;
        int slashFrame;
        Color slashTint = Color.white;
        SpriteRenderer slashFx;
        /// <summary>每帧手的位置和武器朝向（melee_grips.json）：格子里的像素坐标 x, y（从左上数）、角度（度，逆时针）、是否画在身后。</summary>
        readonly Dictionary<string, float[][]> grips = new Dictionary<string, float[][]>();
        /// <summary>正在播挥砍动作（这时手上那层武器图不画，刀已经画在人物图上）。</summary>
        public bool Slashing => slashT >= 0f;

        /// <summary>
        /// 播一刀挥砍：horizontal 为横劈，否则竖劈，方向取最近的八方向。人物图里手是空的，
        /// 武器由 PlayerController 按 SlashHand 给的位置画在手上；刀光是单独一层（melee_vfx / melee_hfx），按 tint 上色。没有这套图时返回 false。
        /// </summary>
        public bool PlaySlash(bool horizontal, Vector2 dir, Color tint, float weaponPx = 18f)
        {
            if (!Ready || dying || dir.sqrMagnitude < 0.0001f) return false;
            string d8 = Key8(dir);
            string key = (horizontal ? "melee_h_" : "melee_v_") + d8;
            if (!anims.ContainsKey(key)) return false;
            slashKey = key;
            // 刀光按武器长度分四档：短刀（≤25 像素）、中（≤50）、长（≤72）、特长，刀尖都落在刀光末端
            string size = weaponPx <= 25f ? "" : weaponPx <= 50f ? "m" : weaponPx <= 72f ? "l" : "xl";
            slashFxKey = (horizontal ? "melee_hfx" : "melee_vfx") + size + "_" + d8;
            if (!anims.ContainsKey(slashFxKey)) slashFxKey = (horizontal ? "melee_hfx_" : "melee_vfx_") + d8;
            slashTint = tint;
            slashFrame = SlashSeq[0];
            slashT = 0f;
            gunPhase = GunPhase.Off;
            hitting = false;
            lastLook = dir;
            return true;
        }

        /// <summary>挥砍中这一帧手的世界坐标、武器角度（度）和要不要画在身后。不在挥砍时返回 false。</summary>
        public bool SlashHand(out Vector3 pos, out float angle, out bool behind)
        {
            pos = Vector3.zero; angle = 0f; behind = false;
            if (!Slashing || !grips.TryGetValue(slashKey.Substring(6), out var g) || slashFrame >= g.Length) return false;
            var r = g[slashFrame];
            const float PivotFromTop = 216f - 28f;                            // melee_ 格子 240×216，脚底（pivot）离下边 28 行
            var local = new Vector3((r[0] + 0.5f - CellW * 0.5f) / PPU, (PivotFromTop - (r[1] + 0.5f)) / PPU, 0f);
            pos = body.transform.TransformPoint(local);
            angle = r[2];
            behind = r[3] > 0.5f;
            return true;
        }

        void LoadGrips(string dir)
        {
            string p = Path.Combine(dir, "melee_grips.json");
            if (!File.Exists(p)) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (Match m in Regex.Matches(File.ReadAllText(p), "\"([a-z]+_[a-z]+)\":\\[(.*?)\\]\\]"))
            {
                var rows = new List<float[]>();
                foreach (Match r in Regex.Matches(m.Groups[2].Value + "]", "\\[([^\\[\\]]+)\\]"))
                {
                    var parts = r.Groups[1].Value.Split(',');
                    var v = new float[parts.Length];
                    for (int i = 0; i < parts.Length; i++) v[i] = float.Parse(parts[i], inv);
                    rows.Add(v);
                }
                grips[m.Groups[1].Value] = rows.ToArray();
            }
        }
        /// <summary>告诉外观现在按的移动方向（八方向走路用）。</summary>
        public void SetMove(Vector2 m) { moveDir = m; }

        bool dying;
        float deathTime;
        string deathDir = "s";

        /// <summary>死亡：播倒地动作（某个方向还没有图时用正面的），停在最后一帧。</summary>
        public void PlayDeath(PlayerController.Dir facing)
        {
            if (!Ready) return;
            string d = Key(facing);
            deathDir = anims.ContainsKey("death_" + d) ? d : "s";
            dying = anims.ContainsKey("death_" + deathDir);
            deathTime = 0f;
            gunPhase = GunPhase.Off;
            hitting = false;
        }

        void Update()
        {
            if (!dying) return;                                     // 死了以后 PlayerController 不再调 Tick，这里自己播
            deathTime += Time.deltaTime;
            var a = anims["death_" + deathDir];
            body.sprite = a[Mathf.Min(a.Length - 1, Mathf.FloorToInt(deathTime * 9f))];
            body.flipX = false;
            body.color = Color.white;
            UpdateCoat(deathDir, false);
        }

        public void PlayHit(PlayerController.Dir facing)
        {
            if (!Ready) return;
            hitting = true;
            hitTime = 0f;
        }

        static string Key(PlayerController.Dir d)
        {
            switch (d)
            {
                case PlayerController.Dir.Right: return "e";
                case PlayerController.Dir.Up: return "n";
                case PlayerController.Dir.Left: return "w";
                default: return "s";
            }
        }

        /// <summary>按「带姿势的八方向 → 带姿势的四方向 → 八方向 → 四方向 → 站立」的顺序找图。</summary>
        Sprite Frame(string anim, string dir, int i)
        {
            Sprite[] a = null;
            foreach (var k in new[] { $"{anim}_{Pose}_{dir8}", $"{anim}_{Pose}_{dir}", $"{anim}_{dir8}", $"{anim}_{dir}", $"idle_{dir}" })
                if (anims.TryGetValue(k, out a) && a.Length > 0) break;
            if (a == null || a.Length == 0) return body.sprite;
            return a[((i % a.Length) + a.Length) % a.Length];
        }

        public void Tick(PlayerController.Dir facing, bool moving, bool sprint, bool dashing, Vector2 dashDir, float dashT, float dashTime, bool fired)
        {
            if (!Ready) return;
            string dir = Key(facing);
            bool flip = false;
            Sprite sprite;

            // 八方向：开枪/瞄准时跟着瞄准方向，走动时跟着移动方向，站着不动时跟着朝向
            // 站着不动时保持最后一次的八方向（走到斜方向停下就面朝斜方向站着）
            Vector2 vel = rb != null ? rb.linearVelocity : Vector2.zero;
            // 走动时用按键方向（WASD 斜着按就是斜方向；不用速度，因为上下速度被压扁过，斜着走会被算成左右）
            if (gunPhase != GunPhase.Off) lastLook = aim;
            else if (moving && moveDir.sqrMagnitude > 0.01f) lastLook = moveDir;
            else if (!moving) lastLook = aim;                                   // 站着不动时和四方向朝向一样跟着瞄准方向
            if (Key4(lastLook) != dir) lastLook = dir == "e" ? Vector2.right : dir == "w" ? Vector2.left : dir == "n" ? Vector2.up : Vector2.down;   // 和四方向朝向对不上时以朝向为准
            dir8 = Key8(lastLook);
            // 有这套姿势的开枪图（手是空的）就用它，武器图一直显示；没有的话手枪用画死的那把，其他武器不播开枪图
            bool poseShoot = Has("shoot_" + Pose, dir) || Has("shoot_" + Pose, dir8);
            bool bakedOk = Pose == "pistol" && !poseShoot && Has("shoot", dir);
            BakedPistol = bakedOk;
            LayerVisible = !(bakedOk && gunPhase != GunPhase.Off);
            if (dashing) slashT = -1f;                                         // 闪移打断挥砍

            if (dashing) { gunPhase = GunPhase.Off; hitting = false; }
            else if (fired) NoteFire();
            if (gunPhase != GunPhase.Off) gunTime += Time.deltaTime;

            // 走、跑、站的节拍：换了状态或方向才从头播
            int mode = !moving ? 0 : sprint ? 2 : 1;
            if (mode != loco || dir != locoDir) { loco = mode; locoDir = dir; locoTime = 0f; }
            else locoTime += Time.deltaTime;

            if (dashing)
            {
                string dd = Mathf.Abs(dashDir.x) >= Mathf.Abs(dashDir.y) ? (dashDir.x >= 0f ? "e" : "w") : (dashDir.y >= 0f ? "n" : "s");
                float u = 1f - Mathf.Clamp01(dashT / Mathf.Max(0.0001f, dashTime));
                int f = u < 0.15f ? 0 : u < 0.5f ? 1 : u < 0.85f ? 2 : 3;
                sprite = Frame("dash", dd, f);
            }
            else if (Slashing)
            {
                slashT += Time.deltaTime;
                float t = slashT; int i = 0;
                while (i < SlashSeq.Length - 1 && t >= SlashDur[i]) { t -= SlashDur[i]; i++; }
                slashFrame = SlashSeq[i];
                sprite = anims[slashKey][slashFrame];
                if (i == SlashSeq.Length - 1 && t >= SlashDur[i]) slashT = -1f;   // 播完回到平常的动作
            }
            else if (gunPhase != GunPhase.Off && !poseShoot && !bakedOk)
            {
                AdvanceGun();                                                  // 这把武器没有开枪图：身体照常走/站，手上那层武器自己瞄准
                if (mode == 2) sprite = Frame("run", dir, Mathf.FloorToInt(locoTime * 13f));
                else if (mode == 1) sprite = Frame("walk", dir, Mathf.FloorToInt(locoTime * 10f));
                else sprite = Frame("idle", dir, Mathf.FloorToInt(locoTime * 4f));
            }
            else if (gunPhase != GunPhase.Off)
            {
                bool kick = gunPhase == GunPhase.Recoil && gunTime < 0.11f;
                if (moving)
                {
                    int step = Mathf.FloorToInt(locoTime * 10f) % 6;
                    sprite = Frame("walkshoot", dir, step + (kick ? 6 : 0));
                }
                else sprite = Frame("shoot", dir, ShootFrame());
                if (dir == "s" && bakedOk) flip = aim.x < 0f;              // 正面的开枪图朝右下，往左下打时镜像
                AdvanceGun();
            }
            else if (hitting)
            {
                hitTime += Time.deltaTime;
                sprite = Frame("hit", dir, Mathf.Min(2, Mathf.FloorToInt(hitTime / PlayerController.HitStun * 3f)));   // 没有受击图时自动用站立图
                if (hitTime >= PlayerController.HitStun) hitting = false;          // 受击姿势和硬直一样长
            }
            else if (mode == 2) sprite = Frame("run", dir, Mathf.FloorToInt(locoTime * 13f));
            else if (mode == 1) sprite = Frame("walk", dir, Mathf.FloorToInt(locoTime * 10f));
            else sprite = Frame("idle", dir, Mathf.FloorToInt(locoTime * 4f));

            // 刀光层：挥砍时跟着人物同一帧显示，其余时候清空
            if (slashFx != null)
            {
                bool on = Slashing && anims.TryGetValue(slashFxKey, out var fxa) && slashFrame < fxa.Length;
                slashFx.sprite = on ? anims[slashFxKey][slashFrame] : null;
                slashFx.color = slashTint;
                slashFx.sortingLayerID = body.sortingLayerID;
                slashFx.sortingOrder = body.sortingOrder + 3;
            }

            body.sprite = sprite;
            body.flipX = flip;
            UpdateCoat(dir, dashing);
        }

        /// <summary>
        /// 风衣下摆：侧面往身后飘（速度越快飘得越开），正面背面左右移动时往反方向带一点；
        /// 一直有一层波浪，站着不动时很轻，跑起来更明显。
        /// </summary>
        void UpdateCoat(string dir, bool dashing)
        {
            if (mpb == null || body.sprite == null) return;
            Vector2 v = rb != null ? rb.linearVelocity : Vector2.zero;
            float speed = v.magnitude;
            float target;
            if (dir == "e" || dir == "w") target = -Mathf.Sign(v.x) * Mathf.Clamp01(Mathf.Abs(v.x) / 9f) * (dashing ? 8f : 5f);
            else target = -Mathf.Clamp(v.x / 9f, -1f, 1f) * 3f;
            coatFlow = Mathf.MoveTowards(coatFlow, target, Time.deltaTime * 40f);       // 停下时慢慢落回去
            float ripple = 0.6f + Mathf.Clamp01(speed / 9f) * 1.8f;
            coatPhase += Time.deltaTime * (2.5f + speed * 1.1f);
            var r = body.sprite.textureRect; var tex = body.sprite.texture;
            body.GetPropertyBlock(mpb);
            mpb.SetVector(FrameId, new Vector4(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height));
            mpb.SetVector(CoatId, new Vector4(coatFlow, ripple, coatPhase, body.flipX ? -1f : 1f));
            mpb.SetTexture("_MainTex", tex);
            body.SetPropertyBlock(mpb);
        }

        void NoteFire()
        {
            hitting = false;
            if (gunPhase == GunPhase.Off || gunPhase == GunPhase.Lower) { gunPhase = GunPhase.Raise; gunTime = 0f; }
            else { gunPhase = GunPhase.Recoil; gunTime = 0f; }
        }

        /// <summary>站着开枪：举枪 0–2、瞄准 3、后坐 4–5、放下 6–7。</summary>
        int ShootFrame()
        {
            switch (gunPhase)
            {
                case GunPhase.Raise: return Mathf.Min(2, Mathf.FloorToInt(gunTime / 0.06f));
                case GunPhase.Recoil: return gunTime < 0.05f ? 4 : gunTime < 0.11f ? 5 : 3;
                case GunPhase.Lower: return gunTime < 0.08f ? 6 : 7;
                default: return 3;
            }
        }

        void AdvanceGun()
        {
            switch (gunPhase)
            {
                case GunPhase.Raise: if (gunTime >= 0.18f) { gunPhase = GunPhase.Aim; gunTime = 0f; } break;
                case GunPhase.Recoil: if (gunTime >= 0.11f) { gunPhase = GunPhase.Aim; gunTime = 0f; } break;
                case GunPhase.Aim: if (gunTime >= 0.35f) { gunPhase = GunPhase.Lower; gunTime = 0f; } break;      // 停火 0.35 秒后放下枪
                case GunPhase.Lower: if (gunTime >= 0.16f) { gunPhase = GunPhase.Off; gunTime = 0f; } break;
            }
        }

        /// <summary>近战挥砍图（melee_）上边多留了 12 行给举过头顶的刀和刀光，其余图多出来的行都在下边。</summary>
        static int TopExtra(string name) => name.StartsWith("melee_") ? 12 : 0;
        /// <summary>刀光层（melee_vfx… / melee_hfx…）：格子 480×432，长武器的大刀光也放得下；脚底在离下边 124 行。</summary>
        static bool IsSlashFx(string name) => name.StartsWith("melee_vfx") || name.StartsWith("melee_hfx");
        const int FxCellW = 480;
        const float FxPivotFromBottom = 124f;

        bool TryLoad(SpriteRenderer bodyRenderer, SpriteRenderer gunRenderer)
        {
            body = bodyRenderer;
            gun = gunRenderer;
            string dir = Path.Combine(Application.streamingAssetsPath, "Zero2");
            if (!Directory.Exists(dir)) { Debug.LogWarning("找不到零的动作图目录：" + dir); return false; }
            foreach (var png in Directory.GetFiles(dir, "*.png"))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(png))) continue;
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                string name = Path.GetFileNameWithoutExtension(png);
                tex.name = name;
                if (tex.height < CellH || tex.width % CellW != 0)              // 比 180 高的图（死亡倒地）是在下边多留了几行，脚底仍在原来那一行
                {
                    Debug.LogWarning($"零的动作图尺寸不对：{name} 是 {tex.width}×{tex.height}，每帧应为 {CellW}×{CellH}（可以更高，多出来的放在下边）");
                    continue;
                }
                keep.Add(tex);
                bool fx = IsSlashFx(name);
                int cw = fx ? FxCellW : CellW;
                int n = tex.width / cw;
                var frames = new Sprite[n];
                for (int i = 0; i < n; i++)
                {
                    var pivot = fx ? new Vector2(0.5f, FxPivotFromBottom / (float)tex.height)
                                   : new Vector2(0.5f, (Foot + tex.height - CellH - TopExtra(name)) / (float)tex.height);
                    frames[i] = Sprite.Create(tex, new Rect(i * cw, 0, cw, tex.height), pivot, PPU, 0, SpriteMeshType.FullRect);
                    frames[i].name = name + i;
                    keep.Add(frames[i]);
                }
                anims[name] = frames;
            }
            foreach (var d in new[] { "s", "e", "n", "w" })
                if (!anims.ContainsKey("idle_" + d)) { Debug.LogWarning("零的动作图缺少 idle_" + d); return false; }
            body.sprite = Frame("idle", "s", 0);
            rb = GetComponent<Rigidbody2D>();
            var coat = Shader.Find("NeonDrift/ZeroCoat");
            if (coat != null) { body.sharedMaterial = new Material(coat) { name = "ZeroCoat" }; mpb = new MaterialPropertyBlock(); }
            else Debug.LogWarning("找不到 NeonDrift/ZeroCoat 着色器，风衣不会飘");
            LoadGrips(dir);
            var fxGo = new GameObject("SlashFx");
            fxGo.transform.SetParent(body.transform, false);
            slashFx = fxGo.AddComponent<SpriteRenderer>();
            Ready = true;
            return true;
        }
    }
}
