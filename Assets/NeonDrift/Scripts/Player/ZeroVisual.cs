using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 零的像素外观。源动画是朝左的侧面条带（112×129），
    /// 这里播的是按灰盒 32 像素/单位对半抽样后的 64 像素高版本。
    /// 上下没有动作条带，只用正面和背面。
    /// </summary>
    public class ZeroVisual : MonoBehaviour
    {
        public bool Ready { get; private set; }

        enum GunPhase { Off, Raise, Aim, Recoil, Lower }

        SpriteRenderer body, gun;
        Sprite idle, front, back;
        Sprite[] walk, sprint, dash, hit, gunFrames;
        readonly List<Object> keep = new List<Object>();

        GunPhase gunPhase;
        float gunTime, hitTime, locoTime;
        int loco = -1;
        bool hitting, hitRight;

        static readonly int[] DashFrames = { 2, 3, 4, 5, 5, 6 };

        public static ZeroVisual Attach(SpriteRenderer body, SpriteRenderer visor, SpriteRenderer gun, Health hp)
        {
            var visual = body.transform.parent.gameObject.AddComponent<ZeroVisual>();
            if (!visual.TryLoad(body, gun))
            {
                DestroyImmediate(visual);
                return null;
            }

            body.color = Color.white;
            gun.sprite = MakePistol();
            gun.color = Color.white;
            if (visor != null) DestroyImmediate(visor.gameObject);
            hp.CaptureColors();
            Debug.Log("零的动作已接入。左右：走路，按住 Shift 疾跑；闪移和受击用侧面条带。举枪只在水平瞄准时出现，垂直瞄准仍用旋转枪。");
            return visual;
        }

        public void PlayHit(PlayerController.Dir facing)
        {
            if (!Ready) return;
            if (facing != PlayerController.Dir.Left && facing != PlayerController.Dir.Right) return;
            hitting = true;
            hitRight = facing == PlayerController.Dir.Right;
            hitTime = 0f;
            gunPhase = GunPhase.Off;
        }

        public void Tick(PlayerController.Dir facing, bool moving, bool sprint, bool dashing, Vector2 dashDir, float dashT, float dashTime, bool fired)
        {
            if (!Ready) return;

            if (dashing)
            {
                gunPhase = GunPhase.Off;
                hitting = false;
            }
            else if (fired) NoteFire(IsSide(facing));

            Sprite sprite;
            bool flip;
            bool drawnGun = false;

            if (dashing && Mathf.Abs(dashDir.x) >= Mathf.Abs(dashDir.y))
            {
                float u = 1f - Mathf.Clamp01(dashT / Mathf.Max(0.0001f, dashTime));
                int i = Mathf.Clamp(Mathf.FloorToInt(u * DashFrames.Length), 0, DashFrames.Length - 1);
                sprite = dash[DashFrames[i]];
                flip = dashDir.x > 0f;
            }
            else if (dashing)
            {
                sprite = dashDir.y > 0f ? back : front;
                flip = false;
            }
            else if (hitting)
            {
                hitTime += Time.deltaTime;
                int f = Mathf.Min(hit.Length - 1, Mathf.FloorToInt(hitTime * 14f));
                sprite = hit[f];
                flip = hitRight;
                if (hitTime >= hit.Length / 14f) hitting = false;
            }
            else if (gunPhase != GunPhase.Off && IsSide(facing))
            {
                sprite = GunSprite();
                flip = facing == PlayerController.Dir.Right;
                drawnGun = true;
            }
            else
            {
                gunPhase = GunPhase.Off;
                int mode = !moving ? 0 : sprint ? 2 : 1;
                if (!IsSide(facing))
                {
                    sprite = facing == PlayerController.Dir.Up ? back : front;
                    flip = false;
                }
                else
                {
                    if (mode != loco) { loco = mode; locoTime = 0f; }
                    else locoTime += Time.deltaTime;
                    if (mode == 2) sprite = this.sprint[Mathf.FloorToInt(locoTime * 14f) % this.sprint.Length];
                    else if (mode == 1) sprite = walk[Mathf.FloorToInt(locoTime * 10f) % walk.Length];
                    else sprite = idle;
                    flip = facing == PlayerController.Dir.Right;
                }
            }

            body.sprite = sprite;
            body.flipX = flip;
            // 身体上已经画了枪，或正在闪移时，藏起那把绕身转的枪。
            if (gun != null) gun.transform.localScale = (drawnGun || dashing) ? Vector3.zero : Vector3.one;
        }

        void NoteFire(bool side)
        {
            if (hitting) return;
            if (!side)
            {
                if (gunPhase == GunPhase.Raise || gunPhase == GunPhase.Aim || gunPhase == GunPhase.Recoil)
                {
                    gunPhase = GunPhase.Lower;
                    gunTime = 0f;
                }
                return;
            }
            if (gunPhase == GunPhase.Off || gunPhase == GunPhase.Lower)
            {
                gunPhase = GunPhase.Raise;
                gunTime = 0f;
            }
            else
            {
                gunPhase = GunPhase.Recoil;
                gunTime = 0f;
            }
        }

        Sprite GunSprite()
        {
            gunTime += Time.deltaTime;
            switch (gunPhase)
            {
                case GunPhase.Raise:
                {
                    int f = Mathf.Min(3, Mathf.FloorToInt(gunTime / 0.06f));
                    if (gunTime >= 0.24f) { gunPhase = GunPhase.Aim; gunTime = 0f; }
                    return gunFrames[f];
                }
                case GunPhase.Aim:
                    if (gunTime >= 0.16f) { gunPhase = GunPhase.Lower; gunTime = 0f; return gunFrames[6]; }
                    return gunFrames[3];
                case GunPhase.Recoil:
                    if (gunTime < 0.05f) return gunFrames[4];
                    if (gunTime < 0.11f) return gunFrames[5];
                    gunPhase = GunPhase.Aim;
                    gunTime = 0f;
                    return gunFrames[3];
                case GunPhase.Lower:
                {
                    int f = gunTime < 0.08f ? 6 : 7;
                    if (gunTime >= 0.16f) gunPhase = GunPhase.Off;
                    return gunFrames[f];
                }
                default:
                    return idle;
            }
        }

        static bool IsSide(PlayerController.Dir facing)
        {
            return facing == PlayerController.Dir.Left || facing == PlayerController.Dir.Right;
        }

        bool TryLoad(SpriteRenderer bodyRenderer, SpriteRenderer gunRenderer)
        {
            body = bodyRenderer;
            gun = gunRenderer;
            // 80 像素高的加宽版（原来 64 像素高、偏细长）：正背面每帧 56×80，侧面每帧 88×80
            var frontTex = LoadTex("zero_front.png", 56, 80);
            var backTex = LoadTex("zero_back.png", 56, 80);
            var idleTex = LoadTex("zero_idle.png", 88, 80);
            var walkTex = LoadTex("zero_walk.png", 88 * 8, 80);
            var sprintTex = LoadTex("zero_sprint.png", 88 * 6, 80);
            var dashTex = LoadTex("zero_dash.png", 88 * 8, 80);
            var hitTex = LoadTex("zero_hit.png", 88 * 6, 80);
            var gunTex = LoadTex("zero_gun.png", 88 * 8, 80);
            if (frontTex == null || backTex == null || idleTex == null || walkTex == null || sprintTex == null || dashTex == null || hitTex == null || gunTex == null)
                return false;

            front = One(frontTex);
            back = One(backTex);
            idle = One(idleTex);
            walk = Slice(walkTex, 8);
            sprint = Slice(sprintTex, 6);
            dash = Slice(dashTex, 8);
            hit = Slice(hitTex, 6);
            gunFrames = Slice(gunTex, 8);
            body.sprite = front;
            Ready = true;
            return true;
        }

        Texture2D LoadTex(string name, int w, int h)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Zero", name);
            if (!File.Exists(path))
            {
                Debug.LogWarning("找不到零的动作图：" + path);
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
            {
                Debug.LogWarning("零的动作图读不出来：" + name);
                return null;
            }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = name;
            if (tex.width != w || tex.height != h)
            {
                Debug.LogWarning($"零的动作图尺寸不对：{name} 是 {tex.width}×{tex.height}，期望 {w}×{h}");
                return null;
            }
            keep.Add(tex);
            return tex;
        }

        Sprite One(Texture2D tex)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), Sprites.PPU, 0, SpriteMeshType.FullRect);
            keep.Add(s);
            return s;
        }

        Sprite[] Slice(Texture2D tex, int count)
        {
            int cell = tex.width / count;
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(i * cell, 0, cell, tex.height), new Vector2(0.5f, 0f), Sprites.PPU, 0, SpriteMeshType.FullRect);
                frames[i].name = tex.name + i;
                keep.Add(frames[i]);
            }
            return frames;
        }

        /// <summary>朝右的小手枪，轴心在中心，好接原来那把枪的 360° 旋转。</summary>
        static Sprite MakePistol()
        {
            const int w = 16, h = 6;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "pistol"
            };
            var px = new Color32[w * h];
            var graphite = new Color32(48, 50, 60, 255);
            var hi = new Color32(108, 116, 128, 255);
            var dk = new Color32(30, 32, 40, 255);
            var cyan = new Color32(92, 224, 234, 255);
            var porcelain = new Color32(238, 240, 244, 255);
            for (int x = 3; x <= 13; x++)
            {
                px[3 * w + x] = graphite;
                px[4 * w + x] = x % 3 == 0 ? hi : graphite;
            }
            px[3 * w + 14] = cyan;
            px[4 * w + 14] = cyan;
            px[3 * w + 15] = cyan;
            px[2 * w + 2] = porcelain;
            px[2 * w + 3] = porcelain;
            px[1 * w + 2] = porcelain;
            px[1 * w + 3] = dk;
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Sprites.PPU, 0, SpriteMeshType.FullRect);
        }
    }
}
