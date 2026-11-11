using System;
using UnityEngine;

namespace NeonDrift
{
    public enum Team { Player, Enemy }

    /// <summary>生命值、受击闪白、无敌帧。玩家和敌人共用。</summary>
    public class Health : MonoBehaviour
    {
        public Team team;
        public float max = 3f;
        public float hp = 3f;

        /// <summary>闪移等状态下的无敌。</summary>
        [NonSerialized] public bool invulnerable;
        /// <summary>受击后的无敌时间（玩家用）。</summary>
        public float hurtIFrames = 0f;

        public event Action<float, Vector2> Damaged;
        public event Action Died;

        public bool IsDead => hp <= 0f;
        public bool IsInvulnerable => invulnerable || iframes > 0f;

        SpriteRenderer[] rends;
        Color[] baseColors;
        float flash, iframes;

        public void Init(Team t, float maxHp, float hurtIFrames = 0f)
        {
            team = t;
            max = hp = maxHp;
            this.hurtIFrames = hurtIFrames;
            CaptureColors();
        }

        /// <summary>子物体颜色改变后调用，重新记录受击闪白要恢复的颜色。</summary>
        public void CaptureColors()
        {
            rends = GetComponentsInChildren<SpriteRenderer>();
            baseColors = new Color[rends.Length];
            for (int i = 0; i < rends.Length; i++) baseColors[i] = rends[i].color;
        }

        /// <summary>改变某个部件的基础颜色（例如出招预警），受击闪白结束后会恢复到这个颜色。</summary>
        public void SetBase(SpriteRenderer r, Color c)
        {
            if (rends == null) CaptureColors();
            for (int i = 0; i < rends.Length; i++)
                if (rends[i] == r) baseColors[i] = c;
            if (flash <= 0f) r.color = c;
        }

        public bool Damage(float amount, Vector2 dir)
        {
            if (IsDead || IsInvulnerable) return false;
            hp = Mathf.Max(0f, hp - amount);
            flash = 0.07f;
            iframes = hurtIFrames;
            SetWhite(true);
            Damaged?.Invoke(amount, dir);
            if (hp <= 0f) Died?.Invoke();
            return true;
        }

        public void Heal(float amount) => hp = Mathf.Min(max, hp + amount);

        void Update()
        {
            if (flash > 0f)
            {
                flash -= Time.deltaTime;
                if (flash <= 0f) SetWhite(false);
            }
            if (iframes > 0f)
            {
                iframes -= Time.deltaTime;
                bool blink = iframes > 0f && Mathf.Repeat(iframes, 0.12f) < 0.06f;
                foreach (var r in rends) if (r) r.enabled = !blink;
                if (iframes <= 0f) foreach (var r in rends) if (r) r.enabled = true;
            }
        }

        void SetWhite(bool on)
        {
            if (rends == null) return;
            for (int i = 0; i < rends.Length; i++)
                if (rends[i]) rends[i].color = on ? Color.white : baseColors[i];
        }
    }
}
