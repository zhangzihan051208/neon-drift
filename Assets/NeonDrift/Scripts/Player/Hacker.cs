using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonDrift
{
    /// <summary>
    /// 骇入（F / 手柄 LB）：自动选中前方最近能骇入的东西（敌人、炮台、变电箱），蓄力 0.3 秒后生效。
    /// 用 RAM：上限 3 格，每 6 秒回 1 格，打倒敌人回 0.25 格。RAM 和电量分开，电量只当弹药。
    /// 敌人：「短路」麻痹 1.5 秒 + 2 点电击（1 RAM）；炮台：「策反」到房间结束（2 RAM）；变电箱：水坑立刻带电 2 秒（1 RAM）。
    /// </summary>
    public class Hacker : MonoBehaviour
    {
        public static readonly Color HackCol = Sprites.Hex("e0409a");
        public const float ChargeTime = 0.3f, Range = 8f;

        PlayerController p;
        public float Ram { get; private set; } = 3f;
        public int MaxRam => 3 + (p != null ? p.N("m_ram") : 0);
        float RegenTime => 6f * Mathf.Pow(0.7f, p.N("m_ram"));

        public IHackable Target { get; private set; }
        public bool Charging { get; private set; }
        public float Charge => Charging ? Mathf.Clamp01(chargeT / ChargeTime) : 0f;
        float chargeT;
        IHackable chargeTarget;
        float lastNoRam = -9f;

        void Awake() { p = GetComponent<PlayerController>(); }

        static bool Valid(IHackable h) => h is Object o && o != null && h.CanHack;

        public void OnKill() => Ram = Mathf.Min(MaxRam, Ram + 0.25f);

        void Update()
        {
            if (p == null || p.IsDead) { Target = null; Charging = false; return; }
            Ram = Mathf.Min(MaxRam, Ram + Time.deltaTime / RegenTime);
            if (p.Frozen) { Target = null; Charging = false; return; }

            if (Charging)
            {
                if (!Valid(chargeTarget)) { Charging = false; return; }
                Target = chargeTarget;
                chargeT += Time.deltaTime;
                if (chargeT >= ChargeTime) Fire(chargeTarget);
                return;
            }

            Target = FindTarget();
            var kb = Keyboard.current; var gp = Gamepad.current;
            bool press = (kb != null && kb.fKey.wasPressedThisFrame) || (gp != null && gp.leftShoulder.wasPressedThisFrame);
            if (!press || p.Stunned) return;
            if (Target == null) { GreyboxHUD.Message("Nothing to hack nearby", 0.8f); return; }
            if (Ram < Target.HackCost)
            {
                if (Time.time - lastNoRam > 1f) { lastNoRam = Time.time; GreyboxHUD.Message($"Not enough RAM ({Target.HackName} needs {Target.HackCost})", 1f); }
                return;
            }
            chargeTarget = Target;
            chargeT = 0f;
            Charging = true;
            if (p.N("m_dash") > 0 && Time.time - p.LastDashEnd < 1.5f) Fire(chargeTarget);    // 门客「快速接入」：闪移后不用蓄力
        }

        void Fire(IHackable h)
        {
            Charging = false;
            Ram -= h.HackCost;
            PixelFx.Play(PixelFx.Glitch(12), h.HackPoint, 0f, HackCol, 20f);
            Effects.Flash(h.HackPoint, HackCol, 2.5f, 1.2f, 0.1f);
            CameraRig.Shake(0.06f);
            h.OnHacked(p);
        }

        /// <summary>8 格内、越靠近瞄准方向越优先。</summary>
        IHackable FindTarget()
        {
            Vector2 from = (Vector2)p.transform.position + Vector2.up * 1f;
            IHackable best = null; float bs = float.MaxValue;
            void Consider(IHackable h)
            {
                if (!Valid(h)) return;
                Vector2 d = h.HackPoint - from;
                float dist = d.magnitude;
                if (dist > Range) return;
                float score = dist + Vector2.Angle(p.AimDir, d) / 25f;
                if (score < bs) { bs = score; best = h; }
            }
            foreach (var e in Enemy.All) Consider(e);
            foreach (var s in ShockPuddle.All) Consider(s);
            return best;
        }
    }
}
