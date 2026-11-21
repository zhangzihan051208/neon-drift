using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 锅炉工（钝器型近战）：又慢又壮，6 点生命，挨打几乎不退。走到 2 格内举起铁锤，脚下的红圈慢慢扩大（预警 0.7 秒），
    /// 然后砸地，红圈里的人受伤并被击飞。打完要缓 1 秒，是绕后输出的时机。
    /// </summary>
    public class BruteEnemy : Enemy
    {
        public float speed = 2.2f, windup = 0.7f, recover = 1.0f, slamRadius = 1.9f, triggerRange = 2.0f, damage = 1f;

        enum State { Chase, Windup, Recover }
        State state; float t;
        SpriteRenderer warn;

        protected override float MaxHp => 6f;
        protected override int CreditDrop => 6;
        protected override float EnergyChance => 1f;
        protected override float AmmoChance => 0.2f;
        protected override Color BodyColor => Sprites.Hex("5a3a28");
        protected override Vector2Int BodyPixels => new Vector2Int(32, 48);
        protected override float KnockbackTaken => 1.5f;
        protected override float HitStunTaken => 0.04f;

        protected override void OnBuilt()
        {
            warn = Sprites.Make(transform, "SlamWarn", PixelFx.GroundRing(), new Color(1f, 0.25f, 0.2f, 0f), -3, new Vector2(0f, 0.1f));
            warn.sharedMaterial = Glow.Mat();
        }

        protected override void OnAllegianceChanged() { state = State.Chase; Telegraph(false); SetWarn(0f, 0f); }

        void SetWarn(float a, float k)
        {
            var c = warn.color; c.a = a; warn.color = c;
            float nat = PixelFx.GroundRing().bounds.size.x;
            float d = slamRadius * 2f * Mathf.Lerp(0.3f, 1f, k) / nat;
            warn.transform.localScale = new Vector3(d, d, 1f);
        }

        protected override void Think(Vector2 target, Health targetHp)
        {
            Vector2 to = target - Pos;
            switch (state)
            {
                case State.Chase:
                    rb.linearVelocity = to.normalized * speed;
                    if (to.magnitude < triggerRange) { state = State.Windup; t = windup; Telegraph(true); }
                    break;
                case State.Windup:
                    rb.linearVelocity = Vector2.zero;
                    t -= Time.fixedDeltaTime;
                    SetWarn(0.35f + 0.4f * (1f - t / windup), 1f - t / windup);
                    if (t <= 0f)
                    {
                        Telegraph(false); SetWarn(0f, 0f);
                        Vector2 at = Pos + Vector2.up * 0.1f;
                        Color col = Allied ? AllyRing : Sprites.Hex("ff7a4a");
                        PixelFx.Play(PixelFx.Smash(Mathf.RoundToInt(slamRadius * Sprites.PPU)), at, 0f, col, 22f, -29950);
                        Effects.Flash(at, col, 4f, 1.6f, 0.1f);
                        Effects.Splash(at, 12, 4.5f);
                        CameraRig.Shake(0.15f);
                        Sfx.Play("brute_slam", 1f, at);
                        if (to.magnitude < slamRadius)
                        {
                            bool hit = targetHp.Damage(damage, to.normalized, DamageKind.Melee);
                            var pl = Player;
                            if (!hit && pl != null && targetHp == pl.Hp && pl.IsDashing) pl.PerfectDodge();
                        }
                        state = State.Recover; t = recover;
                    }
                    break;
                case State.Recover:
                    rb.linearVelocity = Vector2.zero;
                    t -= Time.fixedDeltaTime;
                    if (t <= 0f) state = State.Chase;
                    break;
            }
        }
    }
}
