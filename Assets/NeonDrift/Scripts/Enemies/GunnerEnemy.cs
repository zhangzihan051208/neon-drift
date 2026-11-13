using UnityEngine;

namespace NeonDrift
{
    /// <summary>管道枪手：和玩家保持距离，预警后三连发。</summary>
    public class GunnerEnemy : Enemy
    {
        public float speed = 2.6f;
        public float preferredMin = 4.5f, preferredMax = 7.5f;
        public float fireInterval = 1.9f;
        public float telegraph = 0.4f;
        public int burst = 3;
        public float spread = 12f;
        public float bulletSpeed = 8.5f;
        public float damage = 1f;

        float cd, tele;
        Vector2 strafe;

        protected override float MaxHp => 2.5f;
        protected override Color BodyColor => Sprites.Hex("6a2238");

        protected override void OnBuilt()
        {
            cd = Random.Range(0.8f, fireInterval);
            strafe = Random.value < 0.5f ? Vector2.left : Vector2.right;
        }

        protected override void Think(PlayerController p)
        {
            Vector2 to = (Vector2)p.transform.position - Pos;
            float d = to.magnitude;
            Vector2 n = to / Mathf.Max(d, 0.001f);

            Vector2 v = Vector2.zero;
            if (d < preferredMin) v = -n;
            else if (d > preferredMax) v = n;
            v += new Vector2(-n.y, n.x) * strafe.x * 0.5f;
            rb.linearVelocity = tele > 0f ? Vector2.zero : v.normalized * speed;

            if (tele > 0f)
            {
                tele -= Time.fixedDeltaTime;
                if (tele <= 0f)
                {
                    Telegraph(false);
                    Vector2 aim = ((Vector2)p.transform.position + Vector2.up * 1.5f - Center).normalized;
                    for (int i = 0; i < burst; i++)
                    {
                        float a = (i - (burst - 1) / 2f) * spread;
                        Projectile.Spawn(Center, Quaternion.Euler(0, 0, a) * aim, Team.Enemy, bulletSpeed, damage, 0, Sprites.Hex("ff3b6b"), 0.13f, 4f);
                    }
                    cd = fireInterval;
                    if (Random.value < 0.3f) strafe = -strafe;
                }
                return;
            }
            cd -= Time.fixedDeltaTime;
            if (cd <= 0f && d < 13f) { tele = telegraph; Telegraph(true); }
        }
    }
}
