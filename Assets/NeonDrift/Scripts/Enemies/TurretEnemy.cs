using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 炮台：固定在地上不动，预警后打一发。骇入（2 RAM）会被策反，一直替玩家打到房间清完，脚下光圈从红变金、炮口子弹变金色。
    /// </summary>
    public class TurretEnemy : Enemy
    {
        public float fireInterval = 1.5f, telegraph = 0.35f, bulletSpeed = 9f, damage = 1f, range = 12f;
        float cd, tele;
        Transform barrel;

        protected override float MaxHp => 6f;
        protected override int CreditDrop => 4;
        protected override float EnergyChance => 1f;
        protected override float AmmoChance => 0.3f;
        protected override Color BodyColor => Sprites.Hex("4a4a50");
        protected override Vector2Int BodyPixels => new Vector2Int(22, 20);

        public override int HackCost => 2;
        public override bool IsMelee => false;
        public override string HackName => "Turn";

        protected override void OnBuilt()
        {
            rb.constraints = RigidbodyConstraints2D.FreezeAll;        // 钉死在地上：挨打也不会被击退
            cd = Random.Range(0.6f, fireInterval);
            var b = new GameObject("Barrel").transform;
            b.SetParent(transform, false);
            b.localPosition = new Vector2(0f, 0.95f);
            barrel = b;
            Sprites.Make(b, "Gun", Sprites.Box(26, 6), Sprites.Hex("2a2a30"), 2, new Vector2(13f / Sprites.PPU, 0f));
            Sprites.Make(transform, "Base", Sprites.Box(46, 8, feet: true), Sprites.Hex("2e2e34"), -1, Vector2.zero);
        }

        protected override void OnAllegianceChanged() { tele = 0f; cd = 0.4f; Telegraph(false); }

        protected override void Think(Vector2 target, Health targetHp)
        {
            rb.linearVelocity = Vector2.zero;
            Vector2 from = (Vector2)barrel.position;
            Vector2 aim = (target + Vector2.up * 1.2f - from);
            float d = aim.magnitude;
            aim /= Mathf.Max(d, 0.001f);
            barrel.right = Vector2.Lerp(barrel.right, aim, 0.25f);
            if (tele > 0f)
            {
                tele -= Time.fixedDeltaTime;
                if (tele <= 0f)
                {
                    Telegraph(false);
                    Vector2 muzzle = from + aim * 0.8f;
                    Sfx.Play("turret_shoot", 1f, muzzle);
                    Projectile.Spawn(muzzle, aim, MyTeam, bulletSpeed, damage, 0, Allied ? Sprites.Hex("ffcf3a") : Sprites.Hex("ff3b6b"), 0.13f, 4f);
                    PixelFx.Play(PixelFx.Muzzle(6), muzzle, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg, Allied ? Sprites.Hex("ffcf3a") : Sprites.Hex("ff3b6b"), 30f);
                    cd = Allied ? fireInterval * 0.7f : fireInterval;
                }
                return;
            }
            cd -= Time.fixedDeltaTime;
            if (cd <= 0f && d < range) { tele = telegraph; Telegraph(true); }
        }

        /// <summary>骇入炮台 = 策反到房间结束。</summary>
        public override void OnHacked(PlayerController p)
        {
            Convert(0f);
            GreyboxHUD.Message("Turret turned", 1.2f);
            Protocols.OnHackEnemy(p, this);
        }
    }
}
