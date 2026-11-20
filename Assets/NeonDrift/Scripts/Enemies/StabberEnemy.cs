using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 穿刺手（突刺型近战）：比拾荒刀手快、血薄。靠近到 3.6 格内，地上先亮出一条红色刺击线（预警 0.45 秒），
    /// 然后沿这条线猛地刺出约 3.6 格，线上的人都会被刺中。看到红线往侧面走或者闪移穿过去就能躲。
    /// </summary>
    public class StabberEnemy : Enemy
    {
        public float speed = 3.8f, windup = 0.45f, lungeSpeed = 18f, lungeTime = 0.2f, recover = 0.8f, attackRange = 3.6f, damage = 1f;

        enum State { Chase, Windup, Lunge, Recover }
        State state; float t; Vector2 dir; bool hitThis;
        SpriteRenderer line;

        protected override float MaxHp => 2.2f;
        protected override Color BodyColor => Sprites.Hex("6a4a3a");
        protected override Vector2Int BodyPixels => new Vector2Int(20, 46);

        protected override void OnBuilt()
        {
            // 预警线：贴地的一条细长红条，平时藏起来
            line = Sprites.Make(transform, "StabLine", Sprites.Box(1, 1), new Color(1f, 0.2f, 0.2f, 0f), -3, new Vector2(0f, 0.2f));
            line.sharedMaterial = Glow.Mat();
            var pivot = new GameObject("p").transform;            // 支点在一端：缩放时只往前长
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector2(0f, 0.2f);
            line.transform.SetParent(pivot, false);
            line.transform.localPosition = new Vector2(0.5f, 0f);
        }

        protected override void OnAllegianceChanged() { state = State.Chase; Telegraph(false); ShowLine(0f); }

        void ShowLine(float a)
        {
            var c = line.color; c.a = a; line.color = c;
            var p = line.transform.parent;
            p.right = dir.sqrMagnitude > 0.01f ? (Vector3)dir : Vector3.right;
            p.localScale = new Vector3(attackRange, 0.12f, 1f);
        }

        protected override void Think(Vector2 target, Health targetHp)
        {
            Vector2 to = target - Pos;
            switch (state)
            {
                case State.Chase:
                    rb.linearVelocity = to.normalized * speed;
                    if (to.magnitude < attackRange) { state = State.Windup; t = windup; dir = to.normalized; Telegraph(true); }
                    break;
                case State.Windup:
                    rb.linearVelocity = Vector2.zero;
                    if (t > 0.15f) dir = Vector2.Lerp(dir, to.normalized, 0.08f).normalized;     // 预警时还会稍微跟一下，最后 0.15 秒锁定
                    t -= Time.fixedDeltaTime;
                    ShowLine(0.25f + 0.35f * (1f - t / windup));
                    if (t <= 0f)
                    {
                        state = State.Lunge; t = lungeTime; hitThis = false; Telegraph(false); ShowLine(0f);
                        PixelFx.Play(PixelFx.Thrust(Mathf.RoundToInt(attackRange * Sprites.PPU * 0.6f), false, false), Center, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg,
                                     Allied ? AllyRing : Sprites.Hex("ff5a4a"), 22f, 6000, false, transform);
                    }
                    break;
                case State.Lunge:
                    rb.linearVelocity = dir * lungeSpeed;
                    t -= Time.fixedDeltaTime;
                    if (!hitThis && to.magnitude < 1.0f)
                    {
                        hitThis = true;
                        bool hit = targetHp.Damage(damage, dir, DamageKind.Melee);
                        var pl = Player;
                        if (!hit && pl != null && targetHp == pl.Hp && pl.IsDashing) pl.PerfectDodge();
                    }
                    if (t <= 0f) { state = State.Recover; t = recover; }
                    break;
                case State.Recover:
                    rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 40f * Time.fixedDeltaTime);
                    t -= Time.fixedDeltaTime;
                    if (t <= 0f) state = State.Chase;
                    break;
            }
        }
    }
}
