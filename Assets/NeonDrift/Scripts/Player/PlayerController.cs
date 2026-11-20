using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonDrift
{
    /// <summary>
    /// 零：四方向移动、鼠标/右摇杆瞄准、射击（动能枪·反弹）、闪移。
    /// 动作图加载成功时用像素身体；失败时退回红方块。
    /// 手感参数都是公开字段，Play 模式下可以在 Inspector 里直接调。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        public enum Dir { Down, Right, Up, Left }

        [Header("移动")]
        public float moveSpeed = 7.2f;
        public float sprintSpeed = 10.2f;
        public float acceleration = 70f;
        [Tooltip("斜 45° 视角下，上下方向在屏幕上显得更短。1 = 不缩短")]
        public float verticalScale = 0.7f;

        [Header("闪移")]
        public float dashSpeed = 23f;
        public float dashTime = 0.14f;
        public float dashCooldown = 0.45f;

        // 当前武器（暂时用数字键 1–9 切换九类样板武器；之后换成两个武器槽 + 地上捡）
        public WeaponDef Weapon { get; private set; } = Weapons.Test[0];
        // 局内强化是倍率，换武器后仍然有效
        float fireMul = 1f, dmgMul = 1f; int bounceAdd;
        public float fireRate => Weapon.fireRate * fireMul;
        public float bulletDamage => Weapon.damage * dmgMul;
        public int bulletBounces => Weapon.bounces + (Weapon.melee == 0 ? bounceAdd : 0);
        public float energyCost = 0f;

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
        SpriteRenderer body, visor, gunSr;
        Transform gun;
        ZeroVisual visual;
        Vector2 move, dashDir;
        float dashT, dashCd, fireCd, lastShotTime = -9f, lastMouseMove = -9f, dashBuf, recoil, stepT, ghostT;
        Vector2 lastMousePos;
        bool sprintHeld, shotVisual;

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
            feet.radius = 0.38f;          // 角色放大加宽了，脚下的碰撞也跟着大一点
            feet.offset = new Vector2(0f, 0.25f);
            var hurt = gameObject.AddComponent<BoxCollider2D>();
            hurt.isTrigger = true;
            hurt.size = new Vector2(0.7f, 2.2f);          // 新的零约 2.6 个单位高，受击框比图略小一圈
            hurt.offset = new Vector2(0f, 1.15f);

            // 红方块是动作图加载失败时的退路。加载成功后换成 64 像素高的零，脚底仍在原点。
            body = Sprites.Make(transform, "Body", Sprites.Box(22, 60, feet: true), CoatRed, 0, Vector2.zero);
            visor = Sprites.Make(transform, "Facing", Sprites.Box(10, 4), Sprites.Hex("3ff2ff"), 1, new Vector2(0f, 1.95f));
            gunSr = Sprites.Make(transform, "Gun", Sprites.Box(16, 5), Sprites.Hex("8a8f96"), 2, new Vector2(0f, 2.15f));
            gun = gunSr.transform;
            gameObject.AddComponent<YSort>();
            GroundShadow.Attach(transform, 1.1f);

            hp = gameObject.AddComponent<Health>();
            hp.Init(Team.Player, maxHp, hurtIFrames: 0.9f);
            visual = ZeroVisual.Attach(body, visor, gunSr, hp);
            hp.Damaged += (amt, dir) =>
            {
                CameraRig.Shake(0.25f);
                GameRoot.Hitstop(0.06f);
                rb.linearVelocity += dir.normalized * 6f;
                visual?.PlayHit(Facing);
            };
            hp.Died += () =>
            {
                rb.linearVelocity = Vector2.zero;
                if (visual != null && visual.Ready)
                {
                    visual.PlayDeath(Facing);                         // 播倒地动作，停在最后一帧躺着
                    CameraRig.Shake(0.3f);
                }
                else
                {
                    Effects.Burst(transform.position + Vector3.up * 0.8f, CoatRed);
                    foreach (var r in GetComponentsInChildren<SpriteRenderer>()) r.enabled = false;
                }
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
            dashBuf -= Time.deltaTime;
            // 闪移残影：每 0.035 秒在当前位置留一个，冷灰色，0.22 秒淡出
            if (IsDashing)
            {
                ghostT -= Time.deltaTime;
                if (ghostT <= 0f)
                {
                    ghostT = 0.035f;
                    var grp = GetComponent<UnityEngine.Rendering.SortingGroup>();
                    Effects.Afterimage(body, new Color(0.46f, 0.50f, 0.62f, 0.45f), grp != null ? grp.sortingOrder - 60 : int.MinValue);      // 往上冲时人在画面里越来越靠后，残影要排得足够靠后才不会盖住人
                }
            }
            else ghostT = 0f;
            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 1.8f);
            // 湿地上走路溅起一点水
            if (move.sqrMagnitude > 0.01f && !IsDashing)
            {
                stepT -= Time.deltaTime;
                if (stepT <= 0f) { stepT = sprintHeld ? 0.2f : 0.3f; Effects.Splash((Vector2)transform.position + Vector2.up * 0.1f, sprintHeld ? 3 : 2, 1.6f); }
            }
            else stepT = 0.1f;
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
            sprintHeld = (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
                || (gp != null && gp.leftShoulder.isPressed);

            // 瞄准：鼠标 > 右摇杆 > 自动瞄准最近敌人
            Vector2 origin = (Vector2)transform.position + Vector2.up * 2.15f;      // 和枪口同高，子弹才会真的打向准星
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
                if (e != null) aim = ((Vector2)e.transform.position + Vector2.up * 1.2f - origin).normalized;
                else if (move.sqrMagnitude > 0.01f) aim = move.normalized;
                AimPoint = origin + aim * 4f;
            }
            AimDir = aim;

            // 射击
            bool fire = (ms != null && ms.leftButton.isPressed) || (gp != null && gp.rightTrigger.ReadValue() > 0.4f);
            // 开枪时不能跑：按住射击、或者刚开过枪的 0.35 秒内，按着 Shift 也只是走
            if (fire || Time.time - lastShotTime < 0.35f) sprintHeld = false;
            if (fire && fireCd <= 0f && energy >= energyCost && !IsDashing) { if (Weapon.melee > 0) Melee(); else Shoot(); }

            // 临时：数字键 1–9 切换九类样板武器，用来看各类武器的手感和特效
            if (kb != null)
                for (int k = 0; k < Weapons.Test.Length; k++)
                {
                    var key = kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + k)];
                    if (key != null && key.wasPressedThisFrame && Weapon != Weapons.Test[k])
                    {
                        Weapon = Weapons.Test[k];
                        fireCd = 0.15f;
                        GreyboxHUD.Message($"{k + 1}  {Weapon.category} · {Weapon.name}", 1.5f);
                    }
                }

            // 闪移
            bool dash = (kb != null && kb.spaceKey.wasPressedThisFrame) || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (dash) dashBuf = 0.15f;                    // 提前一点按也算：冷却一好马上闪
            if (dashBuf > 0f && dashCd <= 0f && !IsDashing) { dashBuf = 0f; StartDash(); }
        }

        void Shoot()
        {
            var w = Weapon;
            fireCd = 1f / fireRate;
            energy -= energyCost;
            lastShotTime = Time.time;
            Vector2 muzzle = (Vector2)gun.position + AimDir * 0.8f;      // 画出来的枪口离身体约 1.25 个单位
            float ang = Mathf.Atan2(AimDir.y, AimDir.x) * Mathf.Rad2Deg;
            for (int i = 0; i < w.pellets; i++)
            {
                float a = w.pellets > 1 ? Mathf.Lerp(-w.spread, w.spread, (i + Random.value * 0.6f) / w.pellets) : Random.Range(-w.spread, w.spread);
                Vector2 d = Quaternion.Euler(0, 0, a) * AimDir;
                float spd = w.speed * (w.pellets > 1 ? Random.Range(0.85f, 1.1f) : 1f);
                Projectile.Spawn(muzzle, d, Team.Player, spd, bulletDamage, bulletBounces, w.color, w.bullet, w.radius, w.life, w.trail,
                                 w.pierce, w.explode, w.explodeDamage * dmgMul);
            }
            if (w.muzzle > 0)
            {
                PixelFx.Play(PixelFx.Muzzle(w.muzzle), muzzle, ang, w.color, 30f, 6000, false, transform);   // 像素枪口火光，跟着人走
                Effects.Flash(muzzle, w.color, 2f + w.muzzle * 0.2f, 1.5f, 0.06f);                          // 枪口火光照亮周围一瞬
            }
            else if (Random.value < 0.25f) Effects.Flash(muzzle, w.color, 3f, 0.8f, 0.08f);               // 火焰喷射器：持续的暖光
            recoil = Mathf.Min(0.3f, 0.06f + w.shake);
            CameraRig.Shake(w.shake);
            shotVisual = true;
        }

        bool swingFlip;

        /// <summary>
        /// 近战。刀：身前一道扇形刀光，扇形里的敌人受伤，敌人的子弹被劈碎；来回两刀方向相反。
        /// 钝器：往前砸地，落点一圈冲击波，圈里的敌人受重击并被击退，震屏 + 顿帧。
        /// </summary>
        void Melee()
        {
            var w = Weapon;
            fireCd = 1f / fireRate;
            lastShotTime = Time.time;
            Vector2 chest = (Vector2)transform.position + Vector2.up * 1.1f;
            float ang = Mathf.Atan2(AimDir.y, AimDir.x) * Mathf.Rad2Deg;
            int hits = 0;
            if (w.melee == 1)
            {
                swingFlip = !swingFlip;
                int rpx = Mathf.RoundToInt(w.range * Sprites.PPU * 0.85f);
                PixelFx.Play(PixelFx.Slash(rpx, w.arc), chest, ang, w.color, 26f, 6000, swingFlip, transform);
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null) continue;
                    Vector2 d = (Vector2)e.transform.position + Vector2.up * 0.7f - chest;
                    if (d.magnitude > w.range + 0.3f || Vector2.Angle(AimDir, d) > w.arc * 0.5f) continue;
                    var h = e.GetComponent<Health>();
                    if (h != null && h.Damage(bulletDamage, d.normalized))
                    {
                        hits++;
                        PixelFx.Play(PixelFx.Impact(5), (Vector2)e.transform.position + Vector2.up * 0.8f, 45f, w.color, 26f);
                    }
                }
                int cut = Projectile.CutInArc(chest, AimDir, w.range + 0.2f, w.arc, Team.Player);
                if (cut > 0) Effects.Flash(chest + AimDir, Color.white, 2.5f, 1.2f, 0.06f);
                CameraRig.Shake(hits > 0 ? w.shake * 2f : w.shake);
                if (hits > 0) GameRoot.Hitstop(0.03f);
            }
            else
            {
                Vector2 at = (Vector2)transform.position + AimDir * w.range * 0.75f + Vector2.up * 0.1f;
                int rpx = Mathf.RoundToInt(w.range * Sprites.PPU * 0.9f);
                PixelFx.Play(PixelFx.Smash(rpx), at, 0f, w.color, 22f, -29950);           // 贴着地面，在人物和物件下面
                Effects.Flash(at, w.color, 3.5f, 1.6f, 0.1f);
                Effects.Splash(at, 10, 4f);
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null) continue;
                    Vector2 d = (Vector2)e.transform.position - at;
                    if (d.magnitude > w.range) continue;
                    var h = e.GetComponent<Health>();
                    if (h != null && h.Damage(bulletDamage, (d.sqrMagnitude > 0.001f ? d.normalized : AimDir) * 2f))
                    {
                        hits++;
                        PixelFx.Play(PixelFx.Impact(6), (Vector2)e.transform.position + Vector2.up * 0.8f, 0f, w.color, 22f);
                        var erb = e.GetComponent<Rigidbody2D>();
                        if (erb != null) erb.linearVelocity = (d.sqrMagnitude > 0.001f ? d.normalized : AimDir) * 11f;   // 钝器：重击退
                    }
                }
                CameraRig.Shake(w.shake);
                GameRoot.Hitstop(hits > 0 ? 0.06f : 0.025f);
            }
        }

        void StartDash()
        {
            dashDir = move.sqrMagnitude > 0.01f ? move.normalized : AimDir;
            dashT = dashTime;
            hp.invulnerable = true;
            CameraRig.Shake(0.05f);
            Effects.Splash((Vector2)transform.position + Vector2.up * 0.1f, 7, 3.2f);
        }

        void FixedUpdate()
        {
            if (IsDead) { rb.linearVelocity = Vector2.zero; return; }
            if (dashT > 0f)
            {
                // 先快后慢：起步 1.4 倍速，收尾 0.55 倍速，平均下来和原来的距离差不多
                float u = 1f - Mathf.Clamp01(dashT / dashTime);
                rb.linearVelocity = dashDir * dashSpeed * (1.4f - 0.85f * u);
                dashT -= Time.fixedDeltaTime;
                if (dashT <= 0f)
                {
                    hp.invulnerable = false;
                    dashCd = dashCooldown;
                    rb.linearVelocity = dashDir * moveSpeed;
                }
                return;
            }
            float speed = sprintHeld && move.sqrMagnitude > 0.01f ? sprintSpeed : moveSpeed;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, new Vector2(move.x, move.y * verticalScale) * speed, acceleration * Time.fixedDeltaTime);
        }

        void UpdateFacingVisuals()
        {
            // 最近 0.35 秒内开过枪：朝向跟瞄准走；否则跟移动方向走（元气骑士式）
            Vector2 f = Time.time - lastShotTime < 0.35f || move.sqrMagnitude < 0.01f ? AimDir : move;
            Facing = Mathf.Abs(f.x) > Mathf.Abs(f.y) ? (f.x > 0 ? Dir.Right : Dir.Left) : (f.y > 0 ? Dir.Up : Dir.Down);

            // 像素外观自己表示朝向。动作图没加载到时，仍用青色面罩。
            if ((visual == null || !visual.Ready) && visor != null)
            {
                switch (Facing)
                {
                    case Dir.Down: visor.transform.localPosition = new Vector2(0f, 1.95f); visor.sortingOrder = 1; break;
                    case Dir.Up: visor.transform.localPosition = new Vector2(0f, 1.95f); visor.sortingOrder = -1; break;
                    case Dir.Right: visor.transform.localPosition = new Vector2(0.22f, 1.95f); visor.sortingOrder = 1; break;
                    case Dir.Left: visor.transform.localPosition = new Vector2(-0.22f, 1.95f); visor.sortingOrder = 1; break;
                }
            }

            // 枪 360° 指向瞄准方向；朝上时画在身体后面
            float ang = Mathf.Atan2(AimDir.y, AimDir.x) * Mathf.Rad2Deg;
            gun.localRotation = Quaternion.Euler(0, 0, ang);
            gun.localPosition = new Vector2(0f, 2.15f) + AimDir * (0.45f - recoil);      // 开枪后枪往回顶一下
            var gsr = gun.GetComponent<SpriteRenderer>();
            gsr.sortingOrder = Facing == Dir.Up ? -2 : 2;
            gsr.flipY = AimDir.x < 0f;

            visual?.SetAim(AimDir);
            visual?.Tick(Facing, move.sqrMagnitude > 0.01f, sprintHeld && move.sqrMagnitude > 0.01f,
                IsDashing, dashDir, dashT, dashTime, shotVisual);
            shotVisual = false;
        }

        // ---------------------------------------------------------------- 奖励
        public void ApplyReward(RewardType r)
        {
            switch (r)
            {
                case RewardType.FireRate: fireMul *= 1.2f; break;
                case RewardType.Bounce: bounceAdd += 1; break;
                case RewardType.MaxHp: maxHp += 1; hp.max = maxHp; hp.Heal(maxHp); break;
                case RewardType.Damage: dmgMul *= 1.25f; break;
            }
        }
    }
}
