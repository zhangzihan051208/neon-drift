using UnityEngine;

namespace NeonDrift
{
    /// <summary>拾荒刀手：冲向玩家，近身后停顿预警，再向前扑砍。</summary>
    public class ScavengerEnemy : Enemy
    {
        public float speed = 3.4f;
        public float windup = 0.4f;
        public float lungeSpeed = 13f;
        public float lungeTime = 0.16f;
        public float recover = 0.7f;
        public float attackRange = 1.4f;
        public float damage = 1f;

        enum State { Chase, Windup, Lunge, Recover }
        State state;
        float t;
        Vector2 lungeDir;
        bool hitThisLunge;

        protected override float MaxHp => 3f;
        protected override Color BodyColor => Sprites.Hex("4a5a3a");

        protected override void Think(PlayerController p)
        {
            Vector2 to = (Vector2)p.transform.position - Pos;
            switch (state)
            {
                case State.Chase:
                    rb.linearVelocity = to.normalized * speed;
                    if (to.magnitude < attackRange) { state = State.Windup; t = windup; Telegraph(true); }
                    break;
                case State.Windup:
                    rb.linearVelocity = Vector2.zero;
                    t -= Time.fixedDeltaTime;
                    if (t <= 0f) { state = State.Lunge; t = lungeTime; lungeDir = to.normalized; hitThisLunge = false; Telegraph(false); }
                    break;
                case State.Lunge:
                    rb.linearVelocity = lungeDir * lungeSpeed;
                    t -= Time.fixedDeltaTime;
                    if (!hitThisLunge && to.magnitude < 0.9f)
                    {
                        hitThisLunge = true;
                        p.Hp.Damage(damage, lungeDir);
                    }
                    if (t <= 0f) { state = State.Recover; t = recover; }
                    break;
                case State.Recover:
                    rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 30f * Time.fixedDeltaTime);
                    t -= Time.fixedDeltaTime;
                    if (t <= 0f) state = State.Chase;
                    break;
            }
        }
    }
}
