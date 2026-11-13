using System.Collections.Generic;
using System.IO;
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
            if (gun != null) gun.transform.localScale = Vector3.zero;     // 枪已经画在身上，不再显示绕身转的那把
            if (visor != null) DestroyImmediate(visor.gameObject);
            hp.CaptureColors();
            Debug.Log("零的动作已接入（PixelLab 版，四方向）：站立、走路、跑步、闪移、开枪、边走边打。");
            return visual;
        }

        /// <summary>告诉外观现在瞄的方向（正面开枪时决定往左下还是右下）。</summary>
        public void SetAim(Vector2 a) { if (a.sqrMagnitude > 0.0001f) aim = a; }

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

        Sprite Frame(string anim, string dir, int i)
        {
            if (!anims.TryGetValue(anim + "_" + dir, out var a) || a.Length == 0)
                if (!anims.TryGetValue("idle_" + dir, out a) || a.Length == 0) return body.sprite;
            return a[((i % a.Length) + a.Length) % a.Length];
        }

        public void Tick(PlayerController.Dir facing, bool moving, bool sprint, bool dashing, Vector2 dashDir, float dashT, float dashTime, bool fired)
        {
            if (!Ready) return;
            string dir = Key(facing);
            bool flip = false;
            Sprite sprite;

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
            else if (gunPhase != GunPhase.Off)
            {
                bool kick = gunPhase == GunPhase.Recoil && gunTime < 0.11f;
                if (moving)
                {
                    int step = Mathf.FloorToInt(locoTime * 10f) % 6;
                    sprite = Frame("walkshoot", dir, step + (kick ? 6 : 0));
                }
                else sprite = Frame("shoot", dir, ShootFrame());
                if (dir == "s") flip = aim.x < 0f;                          // 正面的开枪图朝右下，往左下打时镜像
                AdvanceGun();
            }
            else if (hitting)
            {
                hitTime += Time.deltaTime;
                sprite = Frame("idle", dir, 0);
                if (hitTime >= 0.25f) hitting = false;
            }
            else if (mode == 2) sprite = Frame("run", dir, Mathf.FloorToInt(locoTime * 13f));
            else if (mode == 1) sprite = Frame("walk", dir, Mathf.FloorToInt(locoTime * 10f));
            else sprite = Frame("idle", dir, Mathf.FloorToInt(locoTime * 4f));

            body.sprite = sprite;
            body.flipX = flip;
            if (gun != null) gun.transform.localScale = Vector3.zero;
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
                if (tex.height != CellH || tex.width % CellW != 0)
                {
                    Debug.LogWarning($"零的动作图尺寸不对：{name} 是 {tex.width}×{tex.height}，每帧应为 {CellW}×{CellH}");
                    continue;
                }
                keep.Add(tex);
                int n = tex.width / CellW;
                var frames = new Sprite[n];
                for (int i = 0; i < n; i++)
                {
                    frames[i] = Sprite.Create(tex, new Rect(i * CellW, 0, CellW, CellH), new Vector2(0.5f, Foot / (float)CellH), PPU, 0, SpriteMeshType.FullRect);
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
            Ready = true;
            return true;
        }
    }
}
