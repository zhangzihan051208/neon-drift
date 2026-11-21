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
        public override bool IsMelee => false;
        protected override int CreditDrop => 5;          // 枪手难打，掉得多
        protected override float EnergyChance => 1f;
        protected override float AmmoChance => 0.3f;           // 枪手身上有子弹
        protected override Color BodyColor => Sprites.Hex("6a2238");

        protected override void OnAllegianceChanged() { tele = 0f; cd = 0.6f; }

        Enemy NearestFriendlyMelee(Vector2 target)
        {
            Enemy best = null; float bd = float.MaxValue;
            foreach (var e in All)
            {
                if (e == null || e == this || e.Allied || !e.IsMelee) continue;
                float dd = ((Vector2)e.transform.position - target).sqrMagnitude;
                if (dd < bd) { bd = dd; best = e; }
            }
            return best;
        }

        protected override void OnBuilt()
        {
            cd = Random.Range(0.8f, fireInterval);
            strafe = Random.value < 0.5f ? Vector2.left : Vector2.right;
        }

        protected override void Think(Vector2 target, Health targetHp)
        {
            Vector2 to = target - Pos;
            float d = to.magnitude;
            Vector2 n = to / Mathf.Max(d, 0.001f);

            Vector2 v = Vector2.zero;
            // 配合近战：有自己人的近战在前面时，站到“目标 → 最近那个近战”这条线的后方 7 格左右，让近战顶在前面
            Enemy front = Allied ? null : NearestFriendlyMelee(target);
            if (front != null)
            {
                Vector2 fdir = (Vector2)front.transform.position - target;
                if (fdir.sqrMagnitude < 0.01f) fdir = -n;
                Vector2 want = target + fdir.normalized * Mathf.Max(7f, fdir.magnitude + 2.5f);
                Vector2 go = want - Pos;
                if (go.magnitude > 0.8f) v = go.normalized;
            }
            else
            {
                if (d < preferredMin + 1f) v = -n;                 // 身边没有近战了：拉得更远
                else if (d > preferredMax) v = n;
            }
            v += new Vector2(-n.y, n.x) * strafe.x * 0.5f;
            rb.linearVelocity = tele > 0f ? Vector2.zero : v.normalized * speed;

            if (tele > 0f)
            {
                tele -= Time.fixedDeltaTime;
                if (tele <= 0f)
                {
                    Telegraph(false);
                    Vector2 aim = (target + Vector2.up * 1.2f - Center).normalized;
                    Sfx.Play("enemy_shoot", 1f, Center);
                    for (int i = 0; i < burst; i++)
                    {
                        float a = (i - (burst - 1) / 2f) * spread;
                        Projectile.Spawn(Center, Quaternion.Euler(0, 0, a) * aim, MyTeam, bulletSpeed, damage, 0, Allied ? Sprites.Hex("ffcf3a") : Sprites.Hex("ff3b6b"), 0.13f, 4f);
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
