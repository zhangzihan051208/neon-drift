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
        public float dashCooldown = 1.1f;                 // 闪移冷却：0.45 太短，闪移几乎可以连着用

        // 两个枪械槽（元气骑士式）：Q / 滚轮 / 手柄 Y 切换，地上的枪按 E 换手里这把
        // 另有一个近战位：右键 / 手柄 B 随时挥，地上的刀和钝器按 E 换掉它。主流鼓励近战：近战命中回子弹，近战打倒敌人装满弹夹并回电
        public readonly WeaponDef[] Slots = { Weapons.Get("Alleyway"), null };              // 开局：「巷战」手枪 + 一个空枪槽
        public WeaponDef MeleeWeapon { get; private set; } = Weapons.Signature;          // 开局近战：零的本命武器「无铭」黑刀
        float meleeCd;
        // 弹夹：每把枪各记各的剩余子弹；打空自动换弹，R / 手柄 LT 手动换弹，换枪会打断换弹
        readonly System.Collections.Generic.Dictionary<WeaponDef, int> ammo = new System.Collections.Generic.Dictionary<WeaponDef, int>();
        float reloadT, reloadTotal; WeaponDef reloadW;
        public bool Reloading => reloadT > 0f;
        public float ReloadProgress => reloadTotal > 0f ? 1f - reloadT / reloadTotal : 0f;
        public int MagSize(WeaponDef w) => w == null || w.melee > 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(w.mag * Pow(1.3f, "bigmag")));
        public int Ammo(WeaponDef w)
        {
            if (w == null || w.melee > 0) return 0;
            if (!ammo.TryGetValue(w, out var a)) { a = MagSize(w); ammo[w] = a; }
            return Mathf.Min(a, MagSize(w));
        }
        void SetAmmo(WeaponDef w, int a) { if (w != null) ammo[w] = Mathf.Clamp(a, 0, MagSize(w)); }
        // 备弹：子弹是有限的。弹夹打空后从备弹里换，备弹也没了就换不了。捡到的枪带一个备用弹夹；开局的手枪没有备弹（只有 7 发）
        readonly System.Collections.Generic.Dictionary<WeaponDef, int> reserve = new System.Collections.Generic.Dictionary<WeaponDef, int>();
        public int Reserve(WeaponDef w)
        {
            if (w == null || w.melee > 0 || w.thrown) return 0;                 // 投掷物没有备弹，靠捡回来
            if (!reserve.TryGetValue(w, out var r)) { r = MagSize(w); reserve[w] = r; }
            return r;
        }
        public void AddReserve(WeaponDef w, int n)
        {
            if (w == null || w.melee > 0 || n <= 0) return;
            if (w.thrown) { SetAmmo(w, Ammo(w) + n); return; }                  // 捡到子弹 / 弹药箱：投掷物直接补数量
            reserve[w] = Reserve(w) + n;
            if (Ammo(w) == 0 && !Reloading) StartReload();
        }
        float lastNoAmmo = -9f, lastMeleeTime = -9f, critBoostUntil;

        /// <summary>捡回扔出去的投掷物。</summary>
        public void ReturnThrown(WeaponDef w, int n)
        {
            if (w == null || n <= 0) return;
            SetAmmo(w, Ammo(w) + n);
            Sfx.Play("pickup_ammo", 0.8f);
            GreyboxHUD.Popup((Vector2)transform.position + Vector2.up * 2.2f, $"+{n} {w.name}", new Color(0.85f, 0.82f, 0.76f));
        }
        bool iaiReady;
        public int ActiveSlot { get; private set; }
        public WeaponDef Weapon => Slots[ActiveSlot] ?? Slots[1 - ActiveSlot];
        public WeaponDef OtherWeapon => Slots[ActiveSlot] == null ? null : Slots[1 - ActiveSlot];
        public int credits;                              // 信用点
        // 爬梯子转场时由 GameRoot 接管：不读输入，只按 ClimbMove 播走路动画
        [System.NonSerialized] public bool Frozen;
        [System.NonSerialized] public Vector2 ClimbMove;
        float lastNoEnergy = -9f;
        // 受击硬直：这段时间里不能移动、开火、闪移，只会被击退往后滑
        public const float HitStun = 0.3f;
        float stunT;
        public bool Stunned => stunT > 0f;
        // 增益：每个 id 拿了几层。倍率按层数连乘，换武器后仍然有效；武器专精只对对应那一类生效
        readonly System.Collections.Generic.Dictionary<string, int> buffs = new System.Collections.Generic.Dictionary<string, int>();
        public System.Collections.Generic.IReadOnlyDictionary<string, int> BuffStacks => buffs;
        public int N(string id) => buffs.TryGetValue(id, out var n) ? n : 0;
        float Pow(float b, string id) => Mathf.Pow(b, N(id));
        float Cat(string id, string cat, float per, WeaponDef w = null) => (w ?? Weapon).category == cat ? Mathf.Pow(per, N(id)) : 1f;
        float fireMul => Pow(1.2f, "overclock");
        float dmgMul => Pow(1.25f, "breach");
        public float RateOf(WeaponDef w) => w.fireRate * fireMul * (Time.time < pdUntil ? 1.25f : 1f) * (Time.time < critBoostUntil ? 1f + 0.3f * N("afterglow") : 1f)
                                            * Cat("quickdraw", "手枪", 1.4f, w) * Cat("beltfeed", "机枪", 1.15f, w) * Cat("servo", "钝器", 1.2f, w);
        public float DamageOf(WeaponDef w) => w.damage * dmgMul * Cat("quickdraw", "手枪", 1.2f, w) * Cat("stabilizer", "自动步枪", 1.2f, w)
                                              * Cat("falcon", "狙击步枪", 1.5f, w) * Cat("tiblade", "刀", 1.3f, w) * Cat("servo", "钝器", 1.4f, w)
                                              * (Armor >= MaxArmor ? Pow(1.2f, "bulwark") : 1f) * (w.melee > 0 ? Pow(1.25f, "closeq") : 1f) * Cat("pierce", "长柄", 1.3f, w)
                                              * Cat("hidden", "投掷", 1.3f, w);
        public float fireRate => RateOf(Weapon);
        public float bulletDamage => DamageOf(Weapon);
        public int bulletBounces => Weapon.bounces + (Weapon.melee == 0 ? N("refract") : 0);
        public float energyCost => Weapon.melee > 0 ? 0f : Weapon.energy * Pow(0.7f, "eco") * Cat("beltfeed", "机枪", 0.6f);
        float CritChance => 0.15f * N("crit") + (Time.time < pdUntil ? 0.2f : 0f) + (Time.time < swapCritUntil ? 0.3f : 0f);
        // 完美闪避、换武器后的短暂加成窗口
        [System.NonSerialized] public float pdUntil, swapBurnUntil, swapCritUntil;
        public float LastDashEnd { get; private set; } = -99f;
        bool dodgedThisDash;
        float trailT;
        public Hacker Hacker { get; private set; }
        float BurnDps(WeaponDef w) => (w.burn * Cat("overheat", "特殊武器", 2f) + 0.6f * N("ember") + (Time.time < swapBurnUntil ? 1.5f : 0f)) * dmgMul;
        bool shieldReady;
        float reactiveCd;

        [Header("能量（电量）")]
        public float maxEnergy = 100f;
        public float energy = 100f;                      // 不会自己回复：靠残电、浮箱和黑市电芯

        [Header("生命")]
        public float maxHp = 6f;

        [Header("护甲：每格挡下一次攻击，一段时间没挨打后一格一格恢复")]
        public int maxArmor = 2;
        public float armorDelay = 2.5f;      // 最后一次挨打后多久开始恢复
        public float armorRegen = 2f;        // 之后每格恢复要多久
        public int Armor { get; private set; }
        public int MaxArmor => maxArmor + N("plates");
        float ArmorDelay => armorDelay * Pow(0.67f, "repair");
        float ArmorRegen => armorRegen * Pow(0.67f, "repair");
        /// <summary>正在恢复的那一格进度（0–1），HUD 用。</summary>
        public float ArmorProgress => Armor >= MaxArmor || Time.time - lastHitTime < ArmorDelay ? 0f : Mathf.Clamp01(armorT / ArmorRegen);
        float lastHitTime = -99f, armorT;
        static readonly Color ArmorCol = new Color(0.86f, 0.80f, 0.62f);

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
            hp.Modify = ModifyDamage;
            reserve[Slots[0]] = 0;                                     // 开局手枪：只有弹夹里的 7 发
            Hacker = gameObject.AddComponent<Hacker>();
            Armor = MaxArmor;
            visual = ZeroVisual.Attach(body, visor, gunSr, hp);
            hp.Damaged += (amt, dir) =>
            {
                CameraRig.Shake(0.25f);
                GameRoot.Hitstop(0.06f);
                rb.linearVelocity = (dir.sqrMagnitude > 0.001f ? dir.normalized : -AimDir) * 7.5f;     // 击退：往受击方向滑一小段
                stunT = HitStun;
                Sfx.Play("hurt");
                lastHitTime = Time.time; armorT = 0f;
                move = Vector2.zero; sprintHeld = false; dashBuf = 0f;
                fireCd = Mathf.Max(fireCd, HitStun);
                visual?.PlayHit(Facing);
                ReactiveBurst();
            };
            hp.Died += () =>
            {
                Sfx.Play("death");
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
            if (IsDead || Menus.Blocking) return;
            if (Frozen) { move = ClimbMove; UpdateFacingVisuals(); return; }
            RegenArmor();
            if (stunT > 0f) { stunT -= Time.deltaTime; move = Vector2.zero; sprintHeld = false; }
            else ReadInput();
            UpdateFacingVisuals();

            dashCd -= Time.deltaTime;
            fireCd -= Time.deltaTime;
            meleeCd -= Time.deltaTime;
            TickReload();
            dashBuf -= Time.deltaTime;
            // 闪移残影：每 0.035 秒在当前位置留一个，冷灰色，0.22 秒淡出
            if (IsDashing)
            {
                ghostT -= Time.deltaTime;
                if (ghostT <= 0f)
                {
                    ghostT = 0.035f;
                    trailT -= 1f;
                    if (trailT <= 0f) { trailT = 2f; Protocols.DashTrail(this, (Vector2)transform.position + Vector2.up * 0.1f); }     // 每隔一个残影留一段电痕 / 火墙
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
            bool meleeIn = (ms != null && ms.rightButton.isPressed) || (gp != null && gp.buttonEast.isPressed);
            if (fire || meleeIn || Time.time - lastShotTime < 0.35f) sprintHeld = false;
            if (fire && fireCd <= 0f && !IsDashing && !Reloading)
            {
                if (Weapon.melee > 0) Melee(Weapon);
                else if (Ammo(Weapon) <= 0) { if (Reserve(Weapon) > 0) StartReload(); else NoAmmo(); }
                else if (energy >= energyCost) Shoot();
                else NoEnergy();
            }
            if (meleeIn && meleeCd <= 0f && !IsDashing && MeleeWeapon != null) Melee(MeleeWeapon);
            bool reloadIn = (kb != null && kb.rKey.wasPressedThisFrame) || (gp != null && gp.leftTrigger.wasPressedThisFrame);
            if (reloadIn) StartReload();
            if ((kb != null && kb.gKey.wasPressedThisFrame) || (gp != null && gp.dpad.down.wasPressedThisFrame)) DropGun();
            if ((kb != null && kb.xKey.wasPressedThisFrame) || (gp != null && gp.dpad.left.wasPressedThisFrame)) DropMelee();

            // 换武器
            bool swap = (kb != null && kb.qKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame)
                        || (ms != null && Mathf.Abs(ms.scroll.ReadValue().y) > 0.1f);
            if (swap) SwapWeapon();

            // 调试：数字键 1–9 对应九类武器，同一个键连按会在这一类里按稀有度轮换，换掉手里这把
            if (kb != null)
                for (int k = 0; k < Weapons.Categories.Length; k++)
                {
                    var key = k < 9 ? kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + k)] : k == 9 ? kb.digit0Key : kb.minusKey;
                    if (key == null || !key.wasPressedThisFrame) continue;
                    var list = new System.Collections.Generic.List<WeaponDef>();
                    foreach (var w in Weapons.All) if (w.category == Weapons.Categories[k]) list.Add(w);
                    bool isMelee = list[0].melee > 0;
                    int cur = list.IndexOf(isMelee ? MeleeWeapon : Weapon);
                    var pick = list[(cur + 1) % list.Count];
                    if (isMelee) MeleeWeapon = pick; else { Slots[ActiveSlot] = pick; SetAmmo(pick, MagSize(pick)); reserve[pick] = MagSize(pick) * 2; reloadT = 0f; }
                    fireCd = 0.15f;
                    GreyboxHUD.Message($"{k + 1}  {Weapons.Tag(pick)} · {pick.name}\n{pick.special}", 1.8f);
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
            energy = Mathf.Max(0f, energy - energyCost);
            if (!w.thrown) SetAmmo(w, Ammo(w) - 1);
            lastShotTime = Time.time;
            Vector2 muzzle = (Vector2)gun.position + AimDir * 0.8f;      // 画出来的枪口离身体约 1.25 个单位
            float ang = Mathf.Atan2(AimDir.y, AimDir.x) * Mathf.Rad2Deg;
            // 增益改写的弹道参数
            int pellets = w.pellets + (w.category == "霰弹枪" ? 3 * N("slugs") : 0);
            float spread = w.spread * Cat("stabilizer", "自动步枪", 0.4f);
            float life = w.life * Cat("slugs", "霰弹枪", 1.25f);
            int pierce = w.pierce + (w.category == "狙击步枪" ? 2 * N("falcon") : 0);
            float explode = w.explode * Cat("highex", "爆炸武器", 1.4f);
            float explodeDmg = w.explodeDamage * dmgMul * Cat("highex", "爆炸武器", 1.4f);
            int chain = w.chain + N("arc") + (w.category == "特殊武器" ? 2 * N("overheat") : 0);
            if (w.thrown)                                                           // 投掷：一次扔几件就少几件（「暗器」有概率不消耗）
            {
                pellets = Mathf.Min(pellets, Ammo(w));
                if (Random.value >= 0.2f * N("hidden")) SetAmmo(w, Ammo(w) - pellets);
            }
            for (int i = 0; i < pellets; i++)
            {
                float a = pellets > 1 ? Mathf.Lerp(-spread, spread, (i + Random.value * 0.6f) / pellets) : Random.Range(-spread, spread);
                Vector2 d = Quaternion.Euler(0, 0, a) * AimDir;
                float spd = w.speed * (pellets > 1 ? Random.Range(0.85f, 1.1f) : 1f);
                var pr = Projectile.Spawn(muzzle, d, Team.Player, spd, bulletDamage, bulletBounces, w.color, w.bullet, w.radius, life, w.trail,
                                 pierce, explode, explodeDmg);
                pr.homing = w.homing + 90f * N("seeker"); pr.chain = chain; pr.burn = BurnDps(w); pr.weapon = w;
                if (w.thrown) pr.retrieve = w;
                if (w.bullet == "caltrop") pr.SetLife(Mathf.Clamp(Vector2.Distance(muzzle, AimPoint) / Mathf.Max(1f, spd), 0.08f, 0.5f));    // 铁蒺藜落在准星处
            }
            if (w.muzzle > 0)
            {
                PixelFx.Play(PixelFx.Muzzle(w.muzzle), muzzle, ang, w.color, 30f, 6000, false, transform);   // 像素枪口火光，跟着人走
                Effects.Flash(muzzle, w.color, 2f + w.muzzle * 0.2f, 1.5f, 0.06f);                          // 枪口火光照亮周围一瞬
            }
            else if (Random.value < 0.25f) Effects.Flash(muzzle, w.color, 3f, 0.8f, 0.08f);               // 火焰喷射器：持续的暖光
            ShotExtras(w, muzzle, ang);
            recoil = Mathf.Min(0.3f, 0.06f + w.shake);
            CameraRig.Shake(w.shake);
            shotVisual = true;
            string snd = Sfx.ShotSound(w);
            Sfx.Play(snd, snd == "shoot_flame" ? 0.8f : 1f, null, 0.06f, snd == "shoot_flame" ? 0.12f : snd == "shoot_rifle" ? 0.05f : 0.03f);
            if (Ammo(w) <= 0 && Reserve(w) > 0) StartReload();     // 打空自动换弹（有备弹的话）
        }

        /// <summary>各武器开火时额外的东西：弹壳、冲击环、轨道炮光束、导弹发射的烟、电炮的电花。</summary>
        /// <summary>刀光：普通刀一道月牙；热刀多一道慢慢退去的余热和火星；臂刃两道快刀；单分子线一根细线。</summary>
        void SlashFx(WeaponDef w, Vector2 chest, float ang, int rpx)
        {
            switch (w.fx)
            {
                case "heat":
                    PixelFx.Play(PixelFx.Slash(rpx, w.arc), chest, ang, w.color, 26f, 6000, swingFlip, transform);
                    PixelFx.Play(PixelFx.Slash(rpx, w.arc), chest, ang, new Color(w.color.r, w.color.g * 0.5f, w.color.b * 0.3f, 0.6f), 9f, 5999, swingFlip, transform);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (ang + Random.Range(-w.arc, w.arc) * 0.5f) * Mathf.Deg2Rad;
                        Effects.Sparks(chest + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w.range * 0.85f, w.color, 1, 2.5f);
                    }
                    break;
                case "mantis":
                    PixelFx.Play(PixelFx.Slash(rpx, w.arc * 0.8f), chest, ang + (swingFlip ? 12f : -12f), w.color, 36f, 6000, swingFlip, transform);
                    PixelFx.Play(PixelFx.Slash(Mathf.RoundToInt(rpx * 0.8f), w.arc * 0.6f), chest, ang + (swingFlip ? -15f : 15f), w.color, 36f, 6000, !swingFlip, transform);
                    break;
                case "muming":
                    // 无铭：黑色的刃（不发光），外缘一道细细的红色刃口
                    PixelFx.Play(PixelFx.Slash(rpx, w.arc), chest, ang, new Color(0.07f, 0.05f, 0.07f, 0.95f), 26f, 6000, swingFlip, transform, additive: false);
                    PixelFx.Play(PixelFx.WireSlash(rpx + 1, w.arc), chest, ang, w.color, 24f, 6001, swingFlip, transform);
                    break;
                case "chain":
                    PixelFx.Play(PixelFx.WireSlash(rpx, w.arc), chest, ang, w.color, 18f, 6000, swingFlip, transform);
                    PixelFx.Play(PixelFx.Slash(Mathf.RoundToInt(rpx * 0.9f), w.arc), chest, ang, new Color(w.color.r, w.color.g, w.color.b, 0.5f), 20f, 5999, swingFlip, transform);
                    break;
                case "saw":
                    PixelFx.Play(PixelFx.Slash(rpx, w.arc), chest, ang, w.color, 18f, 6000, swingFlip, transform);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = (ang + Random.Range(-w.arc, w.arc) * 0.5f) * Mathf.Deg2Rad;
                        Effects.Sparks(chest + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w.range * 0.8f, Sprites.Hex("ffd27a"), 2, 4f);
                    }
                    break;
                case "wire":
                    PixelFx.Play(PixelFx.WireSlash(rpx, w.arc), chest, ang, w.color, 22f, 6000, swingFlip, transform);
                    Effects.Flash(chest + AimDir * w.range * 0.6f, w.color, 3f, 0.8f, 0.06f);
                    break;
                default:
                    PixelFx.Play(PixelFx.Slash(rpx, w.arc), chest, ang, w.color, 26f, 6000, swingFlip, transform);
                    break;
            }
        }

        /// <summary>砸地：撬棍一圈小冲击波；大锤冲击波 + 扬尘 + 碎石；拳套往前推出一道扇形冲击波。</summary>
        void SmashFx(WeaponDef w, Vector2 at, float ang, int rpx)
        {
            switch (w.fx)
            {
                case "punch":
                {
                    Vector2 c = (Vector2)transform.position + Vector2.up * 1.1f + AimDir * 0.5f;
                    PixelFx.Play(PixelFx.Punch(Mathf.RoundToInt(rpx * 1.2f)), c, ang, w.color, 24f);
                    PixelFx.Play(PixelFx.Impact(5), c + AimDir * 0.4f, 0f, Color.white, 30f);
                    Effects.Flash(c + AimDir, w.color, 3f, 1.4f, 0.08f);
                    break;
                }
                case "hammer":
                    PixelFx.Play(PixelFx.Smash(Mathf.RoundToInt(rpx * 1.25f)), at, 0f, w.color, 20f, -29950);
                    PixelFx.Play(PixelFx.Puff(8), at + Vector2.up * 0.3f, 0f, new Color(0.5f, 0.46f, 0.42f, 0.6f), 10f, 4990);
                    Effects.Sparks(at, new Color(0.55f, 0.5f, 0.45f), 8, 5f);           // 飞起的碎石
                    Effects.Flash(at, w.color, 4.5f, 2f, 0.12f);
                    Effects.Splash(at, 16, 5f);
                    break;
                default:
                    PixelFx.Play(PixelFx.Smash(rpx), at, 0f, w.color, 22f, -29950);       // 贴着地面，在人物和物件下面
                    Effects.Flash(at, w.color, 3.5f, 1.6f, 0.1f);
                    Effects.Splash(at, 10, 4f);
                    break;
            }
        }

        void ShotExtras(WeaponDef w, Vector2 muzzle, float ang)
        {
            switch (w.bullet)
            {
                case "rifle": case "smg": case "mg":
                    if (w.bullet != "mg" || Random.value < 0.6f) PixelFx.Casing((Vector2)gun.position, AimDir);
                    break;
                case "pellet": case "ember":
                    PixelFx.Casing((Vector2)gun.position, AimDir);                 // 霰弹壳
                    PixelFx.Play(PixelFx.Puff(4), muzzle + AimDir * 0.3f, 0f, new Color(0.5f, 0.46f, 0.44f, 0.6f), 14f, 4990);
                    break;
                case "slug":
                    PixelFx.Play(PixelFx.Ring(9), muzzle + AimDir * 0.2f, ang, w.color, 22f);
                    Effects.Sparks(muzzle, w.color, 4, 5f);
                    break;
                case "rail":
                {
                    // 光束一直打到墙上
                    Vector2 end = muzzle + AimDir * 30f;
                    foreach (var h in Physics2D.RaycastAll(muzzle, AimDir, 30f))
                        if (!h.collider.isTrigger && h.collider.GetComponentInParent<Health>() == null) { end = h.point; break; }
                    PixelFx.Beam(muzzle, end, w.color, 6f, 0.28f);
                    PixelFx.Beam(muzzle, end, Color.white, 2f, 0.12f);
                    PixelFx.Play(PixelFx.Ring(14), muzzle, ang, w.color, 20f);
                    Effects.Flash(muzzle, w.color, 5f, 2.5f, 0.12f);
                    GameRoot.Hitstop(0.03f);
                    break;
                }
                case "missile":
                    PixelFx.Play(PixelFx.Puff(5), muzzle - AimDir * 0.2f, 0f, new Color(0.55f, 0.5f, 0.48f, 0.7f), 12f, 4990);
                    break;
                case "orb": case "bolt":
                    PixelFx.Play(PixelFx.Zap(w.bullet == "orb" ? 7 : 4), muzzle, 0f, w.color, 26f);
                    break;
                case "nail":
                    Effects.Sparks(muzzle, w.color, 2, 3f);
                    break;
            }
        }

        bool swingFlip;

        /// <summary>
        /// 近战。刀：身前一道扇形刀光，扇形里的敌人受伤，敌人的子弹被劈碎；来回两刀方向相反。
        /// 钝器：往前砸地，落点一圈冲击波，圈里的敌人受重击并被击退，震屏 + 顿帧。
        /// </summary>
        void Melee(WeaponDef w)
        {
            meleeCd = 1f / RateOf(w);
            lastShotTime = Time.time;
            Vector2 chest = (Vector2)transform.position + Vector2.up * 1.1f;
            float ang = Mathf.Atan2(AimDir.y, AimDir.x) * Mathf.Rad2Deg;
            int hits = 0, kills = 0;
            float range = w.range * Cat("tiblade", "刀", 1.3f, w) * Cat("pierce", "长柄", 1.25f, w);
            float meleeDmg = DamageOf(w) * Pow(1.3f, "e_blade");
            iaiReady = Time.time - lastMeleeTime > 1.5f;              // 「拔刀术」：歇了 1.5 秒后的第一刀
            lastMeleeTime = Time.time;
            Sfx.Play(w.melee == 3 ? "thrust" : w.melee == 2 ? "heavy_swing" : "swing", 0.9f);
            float meleeBurn = BurnDps(w) + 1.2f * N("e_blade") * dmgMul;
            if (w.melee == 3)
            {
                // 突刺：朝瞄准方向刺出一条直线，线上的敌人全都中，还能戳掉正面飞来的子弹；人往前跟一小步
                swingFlip = !swingFlip;
                int lpx = Mathf.RoundToInt(range * Sprites.PPU);
                Vector2 start = chest + AimDir * 0.25f + (w.fx == "twin" ? Vector2.Perpendicular(AimDir) * (swingFlip ? 0.1f : -0.1f) : Vector2.zero);
                PixelFx.Play(PixelFx.Thrust(lpx, w.fx == "twin", w.fx == "lance"), start, ang, w.color, w.fx == "twin" ? 34f : 26f, 6000, false, transform);
                if (w.fx == "lance") PixelFx.Play(PixelFx.Zap(6), chest + AimDir * range, 0f, w.color, 24f);
                rb.linearVelocity += AimDir * 5f;
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null || e.Allied) continue;
                    Vector2 d = (Vector2)e.transform.position + Vector2.up * 0.7f - chest;
                    float along = Vector2.Dot(d, AimDir), side = Mathf.Abs(d.x * AimDir.y - d.y * AimDir.x);
                    if (along < -0.2f || along > range + 0.35f || side > w.width + 0.35f) continue;
                    var h = e.GetComponent<Health>();
                    if (h != null && h.Damage(CritDamage(w, e, meleeDmg, out bool crit), AimDir))
                    {
                        hits++;
                        if (crit) OnCrit((Vector2)e.transform.position + Vector2.up * 0.9f, e);
                        if (h.IsDead) kills++;
                        if (w.stunHit > 0f) e.Paralyze(w.stunHit);
                        if (meleeBurn > 0f) Burning.Apply(h, meleeBurn, w.color);
                        Protocols.OnMeleeHit(this, e);
                        if (w.fx == "lance") ShockPuddle.Conduct(e.transform.position);
                        PixelFx.Play(PixelFx.Impact(4), (Vector2)e.transform.position + Vector2.up * 0.8f, 0f, w.color, 28f);
                    }
                }
                int cut = Projectile.CutInArc(chest, AimDir, range + 0.4f, 40f, Team.Player);
                if (cut > 0) Effects.Flash(chest + AimDir, Color.white, 2f, 1f, 0.05f);
                CameraRig.Shake(hits > 0 ? w.shake * 2f : w.shake);
                if (hits > 0) GameRoot.Hitstop(0.025f);
            }
            else if (w.melee == 1)
            {
                swingFlip = !swingFlip;
                int rpx = Mathf.RoundToInt(range * Sprites.PPU * 0.85f);
                SlashFx(w, chest, ang, rpx);
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null || e.Allied) continue;
                    Vector2 d = (Vector2)e.transform.position + Vector2.up * 0.7f - chest;
                    if (d.magnitude > range + 0.3f || Vector2.Angle(AimDir, d) > w.arc * 0.5f) continue;
                    var h = e.GetComponent<Health>();
                    if (h != null && h.Damage(CritDamage(w, e, meleeDmg, out bool crit), d.normalized))
                    {
                        hits++;
                        if (crit) OnCrit((Vector2)e.transform.position + Vector2.up * 0.9f, e);
                        if (h.IsDead) kills++;
                        if (w.stunHit > 0f) e.Paralyze(w.stunHit);
                        if (meleeBurn > 0f) Burning.Apply(h, meleeBurn, w.color);
                        Protocols.OnMeleeHit(this, e);
                        if (w.fx == "mantis") PixelFx.Play(PixelFx.Cross(5), (Vector2)e.transform.position + Vector2.up * 0.8f, Random.Range(0f, 90f), w.color, 30f);
                        if (w.fx == "wire") PixelFx.Play(PixelFx.Cross(7), (Vector2)e.transform.position + Vector2.up * 0.8f, 0f, Color.white, 28f);
                        PixelFx.Play(PixelFx.Impact(5), (Vector2)e.transform.position + Vector2.up * 0.8f, 45f, w.color, 26f);
                    }
                }
                int cut = Projectile.CutInArc(chest, AimDir, range + 0.5f, w.arc + 20f, Team.Player, reflect: w.signature);
                if (cut > 0) Effects.Flash(chest + AimDir, Color.white, 2.5f, 1.2f, 0.06f);
                CameraRig.Shake(hits > 0 ? w.shake * 2f : w.shake);
                if (hits > 0) GameRoot.Hitstop(0.03f);
            }
            else
            {
                Vector2 at = (Vector2)transform.position + AimDir * range * 0.75f + Vector2.up * 0.1f;
                int rpx = Mathf.RoundToInt(range * Sprites.PPU * 0.9f);
                SmashFx(w, at, ang, rpx);
                int cutB = Projectile.CutInArc(at + Vector2.up * 0.8f, AimDir, range + 0.4f, 360f, Team.Player);      // 钝器：砸地的冲击把周围的子弹震碎
                if (cutB > 0) Effects.Flash(at + Vector2.up * 0.8f, Color.white, 2f, 1f, 0.05f);
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null || e.Allied) continue;
                    Vector2 d = (Vector2)e.transform.position - at;
                    if (d.magnitude > range) continue;
                    var h = e.GetComponent<Health>();
                    if (h != null && h.Damage(CritDamage(w, e, meleeDmg, out bool crit), (d.sqrMagnitude > 0.001f ? d.normalized : AimDir) * 2f))
                    {
                        hits++;
                        if (crit) OnCrit((Vector2)e.transform.position + Vector2.up * 0.9f, e);
                        if (h.IsDead) kills++;
                        if (w.stunHit > 0f) e.Paralyze(w.stunHit);
                        if (meleeBurn > 0f) Burning.Apply(h, meleeBurn, w.color);
                        Protocols.OnMeleeHit(this, e);
                        PixelFx.Play(PixelFx.Impact(6), (Vector2)e.transform.position + Vector2.up * 0.8f, 0f, w.color, 22f);
                        var erb = e.GetComponent<Rigidbody2D>();
                        if (erb != null) erb.linearVelocity = (d.sqrMagnitude > 0.001f ? d.normalized : AimDir) * w.knock * Cat("servo", "钝器", 1.5f, w);   // 钝器：重击退
                    }
                }
                CameraRig.Shake(w.shake);
                GameRoot.Hitstop(hits > 0 ? 0.06f : 0.025f);
            }
            if (hits > 0) Sfx.Play(w.melee == 2 ? "blunt_hit" : "melee_hit", 1f, null, 0.08f);
            MeleeReward(hits, kills);
        }

        void StartDash()
        {
            dashDir = move.sqrMagnitude > 0.01f ? move.normalized : AimDir;
            dashT = dashTime;
            hp.invulnerable = true;
            dodgedThisDash = false; trailT = 0f;
            Sfx.Play("dash", 0.9f);
            CameraRig.Shake(0.05f);
            Effects.Splash((Vector2)transform.position + Vector2.up * 0.1f, 7, 3.2f);
        }

        void FixedUpdate()
        {
            if (IsDead || Frozen) { rb.linearVelocity = Vector2.zero; return; }
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
                    LastDashEnd = Time.time;
                    rb.linearVelocity = dashDir * moveSpeed;
                }
                return;
            }
            float speed = sprintHeld && move.sqrMagnitude > 0.01f ? sprintSpeed : moveSpeed;
            float acc = stunT > 0f ? 25f : acceleration;          // 硬直时减速慢一点，被打得往后滑一小段
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, new Vector2(move.x, move.y * verticalScale) * speed, acc * Time.fixedDeltaTime);
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

        // ---------------------------------------------------------------- 武器槽、电量、补给
        void SwapWeapon()
        {
            if (Slots[0] == null || Slots[1] == null) return;
            ActiveSlot = 1 - ActiveSlot;
            fireCd = Mathf.Max(fireCd, 0.15f);
            GreyboxHUD.Message($"{Weapons.Tag(Weapon)} · {Weapon.name} ({Weapons.CostText(Weapon)})", 1.2f);
            Protocols.OnSwap(this);
            Sfx.Play("swap");
        }

        /// <summary>
        /// 完美闪避：闪移的无敌时间里正好躲过一发子弹或一次扑砍。一次闪移只算一次。
        /// 奖励：接下来 1 秒射速 +25%、暴击率 +20%；画面一瞬慢动作 + 白光。
        /// </summary>
        public void PerfectDodge()
        {
            if (!IsDashing || dodgedThisDash) return;
            dodgedThisDash = true;
            pdUntil = Time.time + 1f;
            Sfx.Play("perfect_dodge");
            Vector2 c = (Vector2)transform.position + Vector2.up * 1.2f;
            PixelFx.Play(PixelFx.Ring(20), c, 90f, Color.white, 20f);
            Effects.Flash(c, Color.white, 3.5f, 1.8f, 0.12f);
            Effects.Afterimage(body, new Color(1f, 1f, 1f, 0.7f));
            GameRoot.SlowMo(0.18f, 0.3f);
            GreyboxHUD.Message("PERFECT DODGE", 0.6f);
        }

        /// <summary>
        /// 鼓励近战：近战打中至少一个敌人，手上的枪回 1 发子弹；近战打倒敌人，手上的枪立刻装满（打断换弹），并回 2 点电量；
        /// 「贴身流」再回 1 格护甲。
        /// </summary>
        /// <summary>
        /// 鼓励近战：近战打倒敌人，手上的枪备弹 +1，并回 2 点电量；「贴身流」再回 1 格护甲。
        /// （子弹是稀缺资源：另外只有敌人小概率掉落、浮箱和黑市的弹药箱。）
        /// </summary>
        void MeleeReward(int hits, int kills)
        {
            var g = Weapon;
            if (kills > 0)
            {
                if (g != null && g.melee == 0) AddReserve(g, kills);
                AddEnergy(2f * kills);
                if (N("closeq") > 0 && Armor < MaxArmor) Armor++;
                Effects.Sparks((Vector2)transform.position + Vector2.up * 1.6f, Loot.AmmoCol, 4, 2f);
            }
        }

        // ---------------------------------------------------------------- 暴击
        /// <summary>暴击伤害倍率：基础 ×2，「致命」每层 +0.5。</summary>
        public float CritMul => 2f + 0.5f * N("critdmg");
        /// <summary>对这个敌人的暴击倍率：熔心「熔点」对着火的敌人再 ×1.5。</summary>
        public float CritMulFor(Enemy e) => CritMul * (N("e_crit") > 0 && e != null && e.GetComponent<Burning>() != null ? 1.5f : 1f);

        /// <summary>判定这一下打 e 是不是暴击：武器基础暴击率 + 增益 + 各种条件。</summary>
        public bool RollCrit(WeaponDef w, Enemy e)
        {
            float c = (w != null ? w.crit : 0f) + CritChance;
            if (e != null)
            {
                bool weak = e.Paralyzed || e.Allied || e.GetComponent<Burning>() != null;
                if (weak) c += 0.25f * N("weak");
                if (N("m_crit") > 0 && e.Paralyzed) c = 1f;                   // 门客「漏洞利用」
            }
            if (w != null && w.melee > 0 && w.category == "刀" && N("iai") > 0 && iaiReady) c = 1f;     // 「拔刀术」
            return Random.value < c;
        }

        float CritDamage(WeaponDef w, Enemy e, float baseDmg, out bool crit)
        {
            crit = RollCrit(w, e);
            return crit ? baseDmg * CritMulFor(e) : baseDmg;
        }

        /// <summary>暴击的手感：白红色的十字大闪光、额外顿帧和震屏、飘一个“暴击”字。</summary>
        public void OnCrit(Vector2 at, Enemy e)
        {
            Sfx.Play("crit", 1f, at, 0.04f, 0.06f);
            PixelFx.Play(PixelFx.Cross(9), at, 45f, Color.white, 26f, 6002);
            PixelFx.Play(PixelFx.Impact(8), at, 0f, Sprites.Hex("ff5040"), 24f, 6001);
            Effects.Flash(at, Color.white, 3f, 2f, 0.08f);
            Effects.Sparks(at, Color.white, 5, 5f);
            CameraRig.Shake(0.12f);
            GameRoot.Hitstop(0.05f);
            GreyboxHUD.Popup(at + Vector2.up * 0.4f, "CRIT", Sprites.Hex("ff5040"));
            if (N("afterglow") > 0) critBoostUntil = Time.time + 1f;
            Protocols.OnCrit(this, e, at);
        }

        void NoAmmo()
        {
            fireCd = 0.3f;
            Sfx.Play("dry_fire", 0.9f, null, 0.03f, 0.25f);
            Effects.Sparks((Vector2)gun.position + AimDir * 0.6f, new Color(0.5f, 0.5f, 0.5f), 2, 1f);      // 空膛咔哒一下
            if (Time.time - lastNoAmmo < 1.5f) return;
            lastNoAmmo = Time.time;
            GreyboxHUD.Message(Weapon != null && Weapon.thrown ? $"All your {Weapon.name} are out there. Pick them up, or use melee (RMB)" : "Out of ammo. Melee (RMB) — melee kills give bullets back", 1.6f);
        }

        void StartReload()
        {
            var w = Weapon;
            if (w == null || w.melee > 0 || Reloading || Ammo(w) >= MagSize(w)) return;
            if (w.thrown) { if (Ammo(w) == 0) NoAmmo(); return; }                     // 投掷物不换弹
            if (Reserve(w) <= 0) { if (Ammo(w) == 0) NoAmmo(); else GreyboxHUD.Message("No spare ammo", 1f); return; }
            reloadW = w;
            reloadTotal = reloadT = w.reload * Pow(0.65f, "fastload");
            Sfx.Play("reload_start");
            // 空弹匣掉在地上
            var mag = Sprites.Make(null, "Mag", Sprites.Box(3, 5), Sprites.Hex("3a3a40"), 4990, (Vector2)gun.position);
            var cf = mag.gameObject.AddComponent<CasingFly>();
            cf.vel = new Vector2(Random.Range(-0.8f, 0.8f), 1.5f); cf.groundY = transform.position.y + 0.05f;
        }

        void TickReload()
        {
            if (reloadT <= 0f) return;
            if (Weapon != reloadW) { reloadT = 0f; return; }               // 换枪打断换弹
            reloadT -= Time.deltaTime;
            if (reloadT <= 0f)
            {
                int take = Mathf.Min(MagSize(reloadW) - Ammo(reloadW), Reserve(reloadW));
                SetAmmo(reloadW, Ammo(reloadW) + take);
                reserve[reloadW] = Reserve(reloadW) - take;
                Sfx.Play("reload_end");
                Effects.Sparks((Vector2)gun.position, Sprites.Hex("d8a040"), 2, 1.5f);
            }
        }

        void NoEnergy()
        {
            fireCd = 0.25f;
            if (Time.time - lastNoEnergy < 1.5f) return;
            lastNoEnergy = Time.time;
            Effects.Sparks((Vector2)gun.position + AimDir * 0.8f, new Color(0.6f, 0.6f, 0.6f), 3, 1.5f);
            GreyboxHUD.Message("Low power. Melee (RMB) to recharge, or Q to swap to a gun that needs none", 1.6f);
        }

        // ---------------------------------------------------------------- 负重
        public int Capacity => Weapons.Capacity + 3 * N("exo");
        public int Load => Wt(Slots[0]) + Wt(Slots[1]) + Wt(MeleeWeapon);
        static int Wt(WeaponDef w) => w == null ? 0 : w.weight;

        /// <summary>捡这把要放哪：-1 近战位，0 / 1 枪槽，-2 超重放不下。先放空枪槽，放不下再试换掉手上那把。</summary>
        int PlanPickUp(WeaponDef w, out int newLoad)
        {
            if (w.melee > 0) { newLoad = Load - Wt(MeleeWeapon) + w.weight; return newLoad <= Capacity ? -1 : -2; }
            for (int i = 0; i < 2; i++)
                if (Slots[i] == null) { newLoad = Load + w.weight; if (newLoad <= Capacity) return i; }
            int act = Slots[ActiveSlot] != null ? ActiveSlot : 1 - ActiveSlot;
            newLoad = Load - Wt(Slots[act]) + w.weight;
            return newLoad <= Capacity ? act : -2;
        }

        public bool CanPickUp(WeaponDef w, out int newLoad) => PlanPickUp(w, out newLoad) != -2;

        /// <summary>捡起一把武器，返回被换下来的（丢在地上）。先用 CanPickUp 检查负重。</summary>
        public WeaponDef PickUp(WeaponDef w)
        {
            int plan = PlanPickUp(w, out _);
            if (plan == -2) return null;
            Sfx.Play("weapon_pickup");
            if (plan == -1)
            {
                var oldm = MeleeWeapon; MeleeWeapon = w; meleeCd = 0.2f;
                return oldm.signature ? null : oldm;                    // 无铭不会丢在地上，收回背上
            }
            fireCd = Mathf.Max(fireCd, 0.2f);
            reloadT = 0f;
            var old = Slots[plan];
            Slots[plan] = w;
            ActiveSlot = plan;
            return old;
        }

        Transform DropParent => GameRoot.I != null && GameRoot.I.World != null && GameRoot.I.World.Current != null ? GameRoot.I.World.Current.transform : null;

        /// <summary>G：丢下手上的枪（至少留一把）。</summary>
        void DropGun()
        {
            if (Slots[0] == null || Slots[1] == null) { GreyboxHUD.Message("You have to keep at least one gun", 1.2f); return; }
            var w = Slots[ActiveSlot];
            Slots[ActiveSlot] = null;
            ActiveSlot = 1 - ActiveSlot;
            reloadT = 0f;
            WeaponPickup.Spawn(DropParent, (Vector2)transform.position + AimDir * 0.6f, w);
            GreyboxHUD.Message($"Dropped {w.name} (load {Load}/{Capacity})", 1.2f);
        }

        /// <summary>X：丢下近战武器，换成徒手。</summary>
        void DropMelee()
        {
            if (MeleeWeapon == null || MeleeWeapon.signature) { GreyboxHUD.Message("Nameless is Zero's own blade. She won't drop it", 1.2f); return; }
            var w = MeleeWeapon;
            MeleeWeapon = Weapons.Signature;
            WeaponPickup.Spawn(DropParent, (Vector2)transform.position + AimDir * 0.6f, w);
            GreyboxHUD.Message($"Dropped {w.name}, back to Nameless (load {Load}/{Capacity})", 1.2f);
        }

        public bool Holds(WeaponDef w) => Slots[0] == w || Slots[1] == w || MeleeWeapon == w;
        public void AddEnergy(float v) => energy = Mathf.Min(maxEnergy, energy + v);
        public void Heal(float v) => hp.Heal(v);

        // ---------------------------------------------------------------- 增益
        public void ApplyBuff(BuffDef b)
        {
            buffs[b.id] = N(b.id) + 1;
            switch (b.id)
            {
                case "redundant": maxHp += 1; hp.max = maxHp; hp.Heal(1f); break;
                case "aftershock": hp.hurtIFrames += 0.5f; break;
                case "doubledash": dashCooldown *= 0.65f; break;
                case "springleg": moveSpeed *= 1.12f; sprintSpeed *= 1.12f; break;
                case "shield": shieldReady = true; break;
                case "plates": Armor++; break;
            }
        }

        /// <summary>护甲和抗性：先看应急护盾，再按“全部伤害”和“这一种伤害”的减免连乘。</summary>
        float ModifyDamage(float a, DamageKind k)
        {
            if (shieldReady && N("shield") > 0)
            {
                shieldReady = false;
                Vector2 c = (Vector2)transform.position + Vector2.up * 1.2f;
                PixelFx.Play(PixelFx.Ring(20), c, 90f, Buffs.GroupColor[(int)BuffGroup.Defense], 18f);
                Effects.Flash(c, Color.white, 3f, 1.5f, 0.1f);
                GreyboxHUD.Message("Emergency shield absorbed the hit", 1.2f);
                return 0f;
            }
            if (Armor > 0) { BlockWithArmor(); return 0f; }
            a *= Pow(0.8f, "plating");
            switch (k)
            {
                case DamageKind.Bullet: a *= Pow(0.65f, "deflect"); break;
                case DamageKind.Melee: a *= Pow(0.6f, "parry"); break;
                case DamageKind.Fire: a *= Pow(0.4f, "fireproof"); break;
                case DamageKind.Shock: a *= Pow(0.4f, "insulate"); break;
                case DamageKind.Explosion: a *= Pow(0.5f, "blastpad"); break;
            }
            return a;
        }

        /// <summary>反应装甲：受伤时以自己为中心炸开一圈，击退并伤害 2.2 格内的敌人。</summary>
        void ReactiveBurst()
        {
            if (N("reactive") == 0 || Time.time < reactiveCd) return;
            reactiveCd = Time.time + 1f;
            Vector2 at = (Vector2)transform.position + Vector2.up * 0.1f;
            PixelFx.Play(PixelFx.Smash(Mathf.RoundToInt(2.2f * Sprites.PPU)), at, 0f, Buffs.GroupColor[(int)BuffGroup.Defense], 22f, -29950);
            Effects.Flash(at, Buffs.GroupColor[(int)BuffGroup.Defense], 4f, 1.8f, 0.12f);
            foreach (var e in Enemy.All.ToArray())
            {
                if (e == null || e.Allied) continue;
                Vector2 d = (Vector2)e.transform.position - at;
                if (d.magnitude > 2.2f) continue;
                var eh = e.GetComponent<Health>();
                if (eh != null) eh.Damage(2f * N("reactive"), d.normalized, DamageKind.Explosion);
                var erb = e.GetComponent<Rigidbody2D>();
                if (erb != null) erb.linearVelocity = (d.sqrMagnitude > 0.001f ? d.normalized : Vector2.up) * 12f;
            }
        }

        /// <summary>进入一间房：应急护盾充好，护甲回满。</summary>
        public void OnRoomStart() { if (N("shield") > 0) shieldReady = true; Armor = MaxArmor; armorT = 0f; }

        /// <summary>护甲挡下一次攻击：不掉血、不硬直，扣一格，给 0.5 秒无敌。</summary>
        void BlockWithArmor()
        {
            Armor--;
            lastHitTime = Time.time; armorT = 0f;
            Sfx.Play("armor_block");
            hp.SetIFrames(0.5f);
            Vector2 c = (Vector2)transform.position + Vector2.up * 1.2f;
            PixelFx.Play(PixelFx.Ring(18), c, 90f, ArmorCol, 22f);
            PixelFx.Play(PixelFx.Impact(6), c, 45f, Color.white, 30f);
            Effects.Sparks(c, ArmorCol, 6, 4f);
            CameraRig.Shake(0.1f);
            GameRoot.Hitstop(0.03f);
            if (Armor == 0) GreyboxHUD.Message("Armor broken", 1f);
            Protocols.OnArmorBroken(this);
            if (N("overcharge") > 0)
            {
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null || e.Allied) continue;
                    Vector2 ec = (Vector2)e.transform.position + Vector2.up * 0.9f;
                    if (Vector2.Distance(ec, c) > 2.5f) continue;
                    var eh = e.GetComponent<Health>();
                    if (eh == null || eh.IsDead) continue;
                    Projectile.ArcFx(c, ec, Sprites.Hex("fff0b0"));
                    eh.Damage(3f * N("overcharge"), (ec - c).normalized, DamageKind.Shock);
                }
            }
        }

        /// <summary>一段时间没挨打后，护甲一格一格恢复。</summary>
        void RegenArmor()
        {
            if (Armor >= MaxArmor) { armorT = 0f; return; }
            if (Time.time - lastHitTime < ArmorDelay) return;
            armorT += Time.deltaTime;
            if (armorT < ArmorRegen) return;
            armorT = 0f;
            Armor++;
            Sfx.Play("armor_regen", 0.8f);
            Effects.Sparks((Vector2)transform.position + Vector2.up * 1.4f, ArmorCol, 3, 1.5f);
        }

        /// <summary>清完一间房：止血纳米回血。</summary>
        public void OnRoomCleared() { if (N("nano") > 0 && !IsDead) hp.Heal(N("nano")); }
    }
}
