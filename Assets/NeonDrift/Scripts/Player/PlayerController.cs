using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonDrift
{
    /// <summary>
    /// 零（灰盒版）：四方向移动、鼠标/右摇杆瞄准、射击（动能枪·反弹）、闪移。
    /// 所有手感参数都是公开字段，Play 模式下在 Inspector 里直接调，找到满意的数值后记下来。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        public enum Dir { Down, Right, Up, Left }

        [Header("移动")]
        public float moveSpeed = 6.5f;
        public float acceleration = 70f;

        [Header("闪移")]
        public float dashSpeed = 20f;
        public float dashTime = 0.14f;
        public float dashCooldown = 0.45f;

        [Header("武器 · 「巷战」动能手枪")]
        public float fireRate = 7f;
        public float bulletSpeed = 22f;
        public float bulletDamage = 1f;
        public int bulletBounces = 1;
        public float spreadDegrees = 3f;
        public float energyCost = 0f;
        public float recoilShake = 0.04f;

        [Header("能量（电量）")]
        public float maxEnergy = 100f;
        public float energy = 100f;
        public float energyRegen = 8f;

        [Header("生命")]
        public float maxHp = 6f;

        public Dir Facing { get; private set; } = Dir.Down;
        public Vector2 AimDir { get; private set; } = Vector2.right;
        public Vector2 AimPoint { get; private set; }
        public bool IsDead => hp != null && hp.IsDead;
        public bool IsDashing => dashT > 0f;
        public float DashReady => dashCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(dashCd / dashCooldown);
        public Health Hp => hp;

        Rigidbody2D rb;
        Health hp;
        SpriteRenderer body, visor;
        Transform gun;
        Vector2 move, dashDir;
        float dashT, dashCd, fireCd, lastShotTime = -9f, lastMouseMove = -9f;
        Vector2 lastMousePos;

        static readonly Color CoatRed = Sprites.Hex("b0202c");

        public static PlayerController Spawn(Vector2 pos)
        {
            var go = new GameObject("Player · 零");
            go.transform.position = pos;
            go.AddComponent<Rigidbody2D>();
            var pc = go.AddComponent<PlayerController>();
            pc.Build();
            return pc;
        }

        void Build()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // 脚下的移动碰撞体 + 身体的受击判定框
            var feet = gameObject.AddComponent<CircleCollider2D>();
            feet.radius = 0.3f;
            feet.offset = new Vector2(0f, 0.25f);
            var hurt = gameObject.AddComponent<BoxCollider2D>();
            hurt.isTrigger = true;
            hurt.size = new Vector2(0.6f, 1.6f);
            hurt.offset = new Vector2(0f, 0.85f);

            // 灰盒外观：红色身体（64 像素高 = 2 格）、青色面罩表示朝向、灰色枪
            body = Sprites.Make(transform, "Body", Sprites.Box(22, 60, feet: true), CoatRed, 0, Vector2.zero);
            visor = Sprites.Make(transform, "Facing", Sprites.Box(10, 4), Sprites.Hex("3ff2ff"), 1, new Vector2(0f, 1.55f));
            var g = Sprites.Make(transform, "Gun", Sprites.Box(16, 5), Sprites.Hex("8a8f96"), 2, new Vector2(0f, 1.0f));
            g.sprite = Sprites.Box(16, 5);
            gun = g.transform;
            gameObject.AddComponent<YSort>();

            hp = gameObject.AddComponent<Health>();
            hp.Init(Team.Player, maxHp, hurtIFrames: 0.9f);
            hp.Damaged += (amt, dir) =>
            {
                CameraRig.Shake(0.25f);
                GameRoot.Hitstop(0.06f);
                rb.linearVelocity += dir.normalized * 6f;
            };
            hp.Died += () =>
            {
                Effects.Burst(transform.position + Vector3.up * 0.8f, CoatRed);
                foreach (var r in GetComponentsInChildren<SpriteRenderer>()) r.enabled = false;
                rb.linearVelocity = Vector2.zero;
                GameRoot.I?.OnPlayerDied();
            };
        }

        void Update()
        {
            if (IsDead) return;
            ReadInput();
            UpdateFacingVisuals();

            dashCd -= Time.deltaTime;
            fireCd -= Time.deltaTime;
            energy = Mathf.Min(maxEnergy, energy + energyRegen * Time.deltaTime);
        }

        void ReadInput()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            var ms = Mouse.current;

            // 移动
            Vector2 m = Vector2.zero;
            if (kb != null)
            {
                if (kb.aKey.isPressed) m.x -= 1; if (kb.dKey.isPressed) m.x += 1;
                if (kb.sKey.isPressed) m.y -= 1; if (kb.wKey.isPressed) m.y += 1;
            }
            if (gp != null && gp.leftStick.ReadValue().sqrMagnitude > 0.04f) m = gp.leftStick.ReadValue();
            move = m.sqrMagnitude > 1f ? m.normalized : m;

            // 瞄准：鼠标 > 右摇杆 > 自动瞄准最近敌人
            Vector2 origin = (Vector2)transform.position + Vector2.up * 1.0f;
            Vector2 aim = AimDir;
            if (ms != null)
            {
                Vector2 mp = ms.position.ReadValue();
                if ((mp - lastMousePos).sqrMagnitude > 1f) { lastMouseMove = Time.time; lastMousePos = mp; }
            }
            Vector2 rs = gp != null ? gp.rightStick.ReadValue() : Vector2.zero;
            if (rs.sqrMagnitude > 0.1f)
            {
                aim = rs.normalized;
                AimPoint = origin + aim * 4f;
            }
            else if (ms != null && Time.time - lastMouseMove < 3f)
            {
                AimPoint = CameraRig.I.MouseWorld();
                var d = AimPoint - origin;
                if (d.sqrMagnitude > 0.01f) aim = d.normalized;
            }
            else
            {
                var e = Enemy.Nearest(origin, 12f);
                if (e != null) aim = ((Vector2)e.transform.position + Vector2.up * 0.8f - origin).normalized;
                else if (move.sqrMagnitude > 0.01f) aim = move.normalized;
                AimPoint = origin + aim * 4f;
            }
            AimDir = aim;

            // 射击
            bool fire = (ms != null && ms.leftButton.isPressed) || (gp != null && gp.rightTrigger.ReadValue() > 0.4f);
            if (fire && fireCd <= 0f && energy >= energyCost && !IsDashing) Shoot();

            // 闪移
            bool dash = (kb != null && kb.spaceKey.wasPressedThisFrame) || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (dash && dashCd <= 0f && !IsDashing) StartDash();
        }

        void Shoot()
        {
            fireCd = 1f / fireRate;
            energy -= energyCost;
            lastShotTime = Time.time;
            Vector2 muzzle = (Vector2)gun.position + AimDir * 0.45f;
            float a = Random.Range(-spreadDegrees, spreadDegrees);
            Vector2 d = Quaternion.Euler(0, 0, a) * AimDir;
            Projectile.Spawn(muzzle, d, Team.Player, bulletSpeed, bulletDamage, bulletBounces, Sprites.Hex("ffb347"));
            Effects.Sparks(muzzle, Sprites.Hex("ffd27a"), 2, 2f);
            CameraRig.Shake(recoilShake);
        }

        void StartDash()
        {
            dashDir = move.sqrMagnitude > 0.01f ? move.normalized : AimDir;
            dashT = dashTime;
            hp.invulnerable = true;
        }

        void FixedUpdate()
        {
            if (IsDead) { rb.linearVelocity = Vector2.zero; return; }
            if (dashT > 0f)
            {
                rb.linearVelocity = dashDir * dashSpeed;
                dashT -= Time.fixedDeltaTime;
                Effects.Afterimage(body, new Color(1f, 0.16f, 0.43f, 0.45f));
                if (dashT <= 0f)
                {
                    hp.invulnerable = false;
                    dashCd = dashCooldown;
                    rb.linearVelocity = dashDir * moveSpeed;
                }
                return;
            }
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, move * moveSpeed, acceleration * Time.fixedDeltaTime);
        }

        void UpdateFacingVisuals()
        {
            // 最近 0.35 秒内开过枪：朝向跟瞄准走；否则跟移动方向走（元气骑士式）
            Vector2 f = Time.time - lastShotTime < 0.35f || move.sqrMagnitude < 0.01f ? AimDir : move;
            Facing = Mathf.Abs(f.x) > Mathf.Abs(f.y) ? (f.x > 0 ? Dir.Right : Dir.Left) : (f.y > 0 ? Dir.Up : Dir.Down);

            // 面罩位置表示朝向；朝上时面罩藏到身后
            switch (Facing)
            {
                case Dir.Down: visor.transform.localPosition = new Vector2(0f, 1.55f); visor.sortingOrder = 1; break;
                case Dir.Up: visor.transform.localPosition = new Vector2(0f, 1.55f); visor.sortingOrder = -1; break;
                case Dir.Right: visor.transform.localPosition = new Vector2(0.22f, 1.55f); visor.sortingOrder = 1; break;
                case Dir.Left: visor.transform.localPosition = new Vector2(-0.22f, 1.55f); visor.sortingOrder = 1; break;
            }

            // 枪 360° 指向瞄准方向；朝上时画在身体后面
            float ang = Mathf.Atan2(AimDir.y, AimDir.x) * Mathf.Rad2Deg;
            gun.localRotation = Quaternion.Euler(0, 0, ang);
            gun.localPosition = new Vector2(0f, 1.0f) + AimDir * 0.35f;
            var gsr = gun.GetComponent<SpriteRenderer>();
            gsr.sortingOrder = Facing == Dir.Up ? -2 : 2;
            gsr.flipY = AimDir.x < 0f;
        }

        // ---------------------------------------------------------------- 奖励
        public void ApplyReward(RewardType r)
        {
            switch (r)
            {
                case RewardType.FireRate: fireRate *= 1.2f; break;
                case RewardType.Bounce: bulletBounces += 1; break;
                case RewardType.MaxHp: maxHp += 1; hp.max = maxHp; hp.Heal(maxHp); break;
                case RewardType.Damage: bulletDamage *= 1.25f; break;
            }
        }
    }
}
