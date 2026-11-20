using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>稀有度：越高越强、越贵、越特别。</summary>
    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

    /// <summary>一把武器的参数。枪：子弹样式和弹道；近战：范围和挥砍角度。</summary>
    public class WeaponDef
    {
        public string name, category;
        public Rarity rarity;
        public string special = "";              // 特殊效果的一句话说明
        public string fx = "";                   // 近战特效样式：heat 热刀 / mantis 臂刃 / wire 单分子线 / hammer 大锤 / punch 拳套
        public float fireRate = 4f;              // 每秒几次
        public float speed = 13f;                // 子弹速度（单位/秒）
        public float damage = 1f;
        public float spread = 2f;                // 散布（度）
        public int pellets = 1;                  // 一次打出几发
        public int bounces;                      // 撞墙反弹次数
        public int pierce;                       // 能穿过几个敌人
        public float life = 2f;                  // 子弹飞多久
        public float radius = 0.1f;              // 子弹判定半径
        public string bullet = "pistol";         // PixelFx 子弹样式
        public Color color = Color.white;
        public int muzzle = 5;                   // 枪口火光长度（像素），0 = 不要
        public float trail = 2f;                 // 光尾宽度（像素），0 = 不要
        public float shake = 0.04f;
        public float explode;                    // 爆炸半径（单位），0 = 不爆
        public float explodeDamage;
        public int melee;                        // 0 枪 / 1 挥砍（刀）/ 2 钝器 / 3 突刺（长柄、匕首）
        public float range = 1.8f, arc = 150f;   // 近战：攻击距离和挥砍角度
        public float knock = 11f;                // 钝器：击退速度
        public float width = 0.45f;              // 突刺：刺击判定的半宽（单位）
        public float stunHit;                    // 近战打中时麻痹几秒（电击棍、电磁长枪）
        public int weight;                       // 重量：身上两把枪 + 近战位加起来不能超过负重上限（10）
        public string reach = "";                // 近战：短 / 中 / 长 距离
        public float crit = -1f;                 // 基础暴击率（-1 = 按类别自动定）
        public bool signature;                   // 本命武器（零的「无铭」）：不占负重、不会丢
        public bool thrown;                      // 投掷冷兵器：放在枪槽里，不耗电、不换弹，扔出去的落地后走过去捡回来
        public float energy;                     // 每发耗电；0 = 不耗电（只有普通手枪和近战）
        public int price;                        // 黑市售价，按稀有度定
        public int mag = 10;                     // 弹夹容量（近战没有）
        public float reload = 1.5f;              // 换弹时间（秒）
        // 特殊效果
        public float homing;                     // 追踪：每秒最多转多少度
        public int chain;                        // 电弧：命中后跳到几个附近的敌人（每跳 60% 伤害）
        public float burn;                       // 燃烧：每秒伤害，持续 2.5 秒
    }

    /// <summary>
    /// 九类武器 × 五种稀有度，共 27 把。数值是初版手感，之后按章节平衡再调。
    /// 射速以手枪约 1 秒 1 枪为基准：步枪 4–6 发/秒，机枪 7–10，霰弹 0.6–0.8，狙击 0.35–0.6，左轮 0.6。
    /// 射速降了以后，单发伤害和每发耗电相应提高（每秒耗电大致不变）。
    /// 稀有度的强度大致是：普通每秒伤害 ×1.0，改良 ×1.2，精良 ×1.35 + 一个特殊效果，史诗 ×1.5 + 强力特效，传说 ×1.8 + 独特玩法。
    /// 子弹颜色只用暖色和洋红，不用青蓝色（和场景的光保持一致）。
    /// </summary>
    public static class Weapons
    {
        static Color H(string s) => Sprites.Hex(s);

        public static readonly string[] RarityName = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };
        // 精良原定蓝色，因为场景里不用蓝青色的光，改成洋红
        public static readonly Color[] RarityColor = { H("d8d4cc"), H("7ee05a"), H("ff4fd8"), H("b066ff"), H("ff9a2a") };
        public static readonly int[] RarityPrice = { 50, 80, 120, 170, 250 };
        public static readonly string[] Categories = { "手枪", "自动步枪", "霰弹枪", "狙击步枪", "机枪", "刀", "钝器", "爆炸武器", "特殊武器", "长柄", "投掷" };

        /// <summary>负重上限。基本配重：手枪 3 + 突击步枪 7、手枪 3 + 长距离近战 7、突击步枪 7 + 中距离近战 3、重武器 8 + 短距离近战 2。</summary>
        public const int Capacity = 10;

        /// <summary>
        /// 零的本命武器「无铭」：一把没有刻名字的黑色武士刀，和她自己一样是一份没有署名的拷贝。
        /// 开局就在近战位上，不占负重、不会丢；捡别的近战会把它收回背上，丢掉那把就换回它。
        /// 暴击率高（20%），劈掉的子弹会被反弹回去。刀光是黑色的刃加一道红色刃口。
        /// </summary>
        public static readonly WeaponDef Signature = new WeaponDef { name = "Nameless", category = "刀", rarity = Rarity.Legendary, signature = true, melee = 1, fx = "muming",
                                                                     fireRate = 3f, damage = 1.7f, range = 2.4f, arc = 160f, crit = 0.2f, color = H("ff3a3a"), shake = 0.06f,
                                                                     reach = "中", weight = 0, special = "Signature weapon · 20% crit · bullets it cuts fly back" };
        public static readonly Color SignatureCol = H("c02828");

        public static readonly WeaponDef[] All =
        {
            // ---------------- 手枪
            new WeaponDef { name = "Alleyway Pistol", mag = 7, reload = 1.0f, category = "手枪", rarity = Rarity.Common, energy = 0f, fireRate = 1.0f, speed = 13f, damage = 1.8f, spread = 2f, bounces = 1,
                            bullet = "pistol", color = H("ffb347"), muzzle = 5, trail = 2f, shake = 0.04f, special = "Bullets bounce off walls once" },
            new WeaponDef { name = "Harmonic Smart Pistol", mag = 9, reload = 1.1f, category = "手枪", rarity = Rarity.Rare, energy = 1.3f, fireRate = 1.3f, speed = 14f, damage = 2.0f, spread = 3f,
                            homing = 260f, bullet = "smart", color = H("ff7ae0"), muzzle = 5, trail = 2f, shake = 0.04f, special = "Bullets home in on the nearest enemy" },
            new WeaponDef { name = "Verdict Revolver", mag = 6, reload = 1.6f, category = "手枪", rarity = Rarity.Epic, energy = 3.3f, fireRate = 0.6f, speed = 24f, damage = 6.0f, spread = 0.5f, pierce = 2,
                            bullet = "slug", color = H("ffd27a"), muzzle = 8, trail = 2f, shake = 0.1f, special = "Heavy rounds pierce 2 enemies" },
            // ---------------- 自动步枪
            new WeaponDef { name = "Veteran Assault Rifle", mag = 20, reload = 1.6f, category = "自动步枪", rarity = Rarity.Common, energy = 0.6f, fireRate = 4.5f, speed = 17f, damage = 0.7f, spread = 8f,
                            bullet = "rifle", color = H("ffd27a"), muzzle = 6, trail = 2f, shake = 0.03f },
            new WeaponDef { name = "Torrent SMG", mag = 24, reload = 1.5f, category = "自动步枪", rarity = Rarity.Uncommon, energy = 0.5f, fireRate = 6.5f, speed = 16f, damage = 0.55f, spread = 13f,
                            bullet = "smg", color = H("ffc060"), muzzle = 5, trail = 2f, shake = 0.025f, special = "Very fast, very wild" },
            new WeaponDef { name = "Hornet Seeker SMG", mag = 22, reload = 1.6f, category = "自动步枪", rarity = Rarity.Rare, energy = 0.6f, fireRate = 5.5f, speed = 15f, damage = 0.6f, spread = 11f,
                            homing = 200f, bullet = "stinger", color = H("ffe14a"), muzzle = 5, trail = 2f, shake = 0.025f, special = "Bullets home in on the nearest enemy" },
            new WeaponDef { name = "Neon Hime Arc Rifle", mag = 18, reload = 1.8f, category = "自动步枪", rarity = Rarity.Epic, energy = 1.0f, fireRate = 3.5f, speed = 18f, damage = 1.2f, spread = 6f,
                            chain = 2, bullet = "bolt", color = H("e0409a"), muzzle = 6, trail = 2f, shake = 0.035f, special = "Hits arc to 2 more enemies" },
            // ---------------- 霰弹枪
            new WeaponDef { name = "Skullsplitter Shotgun", mag = 5, reload = 1.8f, category = "霰弹枪", rarity = Rarity.Common, energy = 3.2f, fireRate = 0.8f, speed = 14f, damage = 0.8f, spread = 16f, pellets = 7, life = 0.45f,
                            bullet = "pellet", color = H("ff9a3c"), muzzle = 9, trail = 1f, shake = 0.14f },
            new WeaponDef { name = "Sawn-Off Double Barrel", mag = 2, reload = 1.4f, category = "霰弹枪", rarity = Rarity.Uncommon, energy = 3.7f, fireRate = 0.6f, speed = 15f, damage = 0.85f, spread = 22f, pellets = 10, life = 0.35f,
                            bullet = "pellet", color = H("ffb070"), muzzle = 11, trail = 1f, shake = 0.2f, special = "10 pellets, shorter range" },
            new WeaponDef { name = "Dragon's Breath Shotgun", mag = 4, reload = 1.9f, category = "霰弹枪", rarity = Rarity.Epic, energy = 4.0f, fireRate = 0.75f, speed = 13f, damage = 0.75f, spread = 18f, pellets = 8, life = 0.5f,
                            burn = 1.2f, bullet = "ember", color = H("ff6a1e"), muzzle = 10, trail = 0f, shake = 0.16f, special = "Pellets set enemies on fire" },
            // ---------------- 狙击步枪
            new WeaponDef { name = "Spike Bolt-Action", mag = 5, reload = 2.0f, category = "狙击步枪", rarity = Rarity.Uncommon, energy = 3.6f, fireRate = 0.5f, speed = 30f, damage = 7.0f, spread = 0f, pierce = 1, life = 1.5f,
                            bullet = "sniper", color = H("ffcf8a"), muzzle = 8, trail = 3f, shake = 0.12f, special = "Pierces 1 enemy" },
            new WeaponDef { name = "Ghost Unicorn Railsniper", mag = 4, reload = 2.2f, category = "狙击步枪", rarity = Rarity.Rare, energy = 4.6f, fireRate = 0.6f, speed = 34f, damage = 7.5f, spread = 0f, pierce = 3, life = 1.5f,
                            bullet = "sniper", color = H("ff6ad5"), muzzle = 8, trail = 3f, shake = 0.12f, special = "Pierces 3 enemies" },
            new WeaponDef { name = "Wrath Railgun", mag = 2, reload = 2.6f, category = "狙击步枪", rarity = Rarity.Legendary, energy = 8.0f, fireRate = 0.35f, speed = 45f, damage = 16.0f, spread = 0f, pierce = 99, life = 1.2f,
                            chain = 3, radius = 0.16f, bullet = "rail", color = H("ff9a2a"), muzzle = 12, trail = 4f, shake = 0.25f, special = "Punches through a whole line; every hit arcs" },
            // ---------------- 机枪
            new WeaponDef { name = "Meatgrinder HMG", mag = 22, reload = 2.4f, category = "机枪", rarity = Rarity.Uncommon, energy = 0.44f, fireRate = 7.0f, speed = 16f, damage = 0.5f, spread = 11f,
                            bullet = "mg", color = H("ffcf5a"), muzzle = 6, trail = 2f, shake = 0.025f },
            new WeaponDef { name = "Downpour Minigun", mag = 26, reload = 2.8f, category = "机枪", rarity = Rarity.Epic, energy = 0.5f, fireRate = 10.0f, speed = 17f, damage = 0.5f, spread = 12f, bounces = 1,
                            bullet = "mg", color = H("ffa040"), muzzle = 7, trail = 2f, shake = 0.03f, special = "20 rounds/sec, bullets bounce once" },
            // ---------------- 刀（不耗电）
            new WeaponDef { name = "Pipe Machete", category = "刀", rarity = Rarity.Common, melee = 1, fireRate = 2.6f, damage = 1.5f, range = 1.7f, arc = 130f,
                            color = H("d8c8b0"), shake = 0.05f, special = "Can cut bullets" },
            new WeaponDef { name = "Daybreak Thermal Katana", category = "刀", rarity = Rarity.Uncommon, melee = 1, fx = "heat", fireRate = 3.2f, damage = 1.8f, range = 1.9f, arc = 150f,
                            burn = 0.8f, color = H("ff8a3d"), shake = 0.06f, special = "Red-hot blade sets enemies on fire" },
            new WeaponDef { name = "Mantis Arm Blades", category = "刀", rarity = Rarity.Rare, melee = 1, fx = "mantis", fireRate = 5f, damage = 1.4f, range = 1.3f, arc = 120f,
                            color = H("ff4fd8"), shake = 0.04f, special = "5 slashes per second" },
            new WeaponDef { name = "Monomolecular Wire", category = "刀", rarity = Rarity.Legendary, melee = 1, fx = "wire", fireRate = 2.4f, damage = 4f, range = 3.2f, arc = 240f,
                            color = H("ffe0b0"), shake = 0.1f, special = "Invisible wire sweeps 240° in front of you" },
            // ---------------- 钝器（不耗电）
            new WeaponDef { name = "Crowbar", category = "钝器", rarity = Rarity.Common, melee = 2, fireRate = 1.6f, damage = 2.2f, range = 1.5f, knock = 9f,
                            color = H("c8a080"), shake = 0.15f },
            new WeaponDef { name = "Ironbreak Power Hammer", category = "钝器", rarity = Rarity.Rare, melee = 2, fx = "hammer", fireRate = 1.3f, damage = 3.6f, range = 1.8f, knock = 16f,
                            color = H("ffc070"), shake = 0.22f, special = "Huge slam, sends enemies flying" },
            new WeaponDef { name = "Demolisher Hydraulic Fists", category = "钝器", rarity = Rarity.Epic, melee = 2, fx = "punch", fireRate = 2.4f, damage = 3f, range = 1.4f, knock = 20f,
                            color = H("ff7a3a"), shake = 0.18f, special = "Fast punches, longest knockback" },
            // ---------------- 更多挥砍
            new WeaponDef { name = "Butterfly Knife", category = "刀", rarity = Rarity.Common, melee = 1, fireRate = 4.5f, damage = 0.9f, range = 1.1f, arc = 100f,
                            color = H("e0d0c0"), shake = 0.03f, special = "The fastest short blade" },
            new WeaponDef { name = "Whirl Chain Blade", category = "刀", rarity = Rarity.Rare, melee = 1, fx = "chain", fireRate = 2f, damage = 2f, range = 2.6f, arc = 200f,
                            color = H("ff9a5a"), shake = 0.07f, special = "Whips a chain blade through 200°" },
            new WeaponDef { name = "Headsman Chainsaw Sword", category = "刀", rarity = Rarity.Epic, melee = 1, fx = "saw", fireRate = 1.4f, damage = 4f, range = 2.8f, arc = 170f,
                            color = H("ffb030"), shake = 0.16f, special = "Heavy sweeps that throw sparks" },
            // ---------------- 更多钝器
            new WeaponDef { name = "Brass Knuckles", category = "钝器", rarity = Rarity.Common, melee = 2, fx = "punch", fireRate = 3.5f, damage = 1.2f, range = 1.1f, knock = 8f,
                            color = H("d8a040"), shake = 0.06f, special = "Rapid punches" },
            new WeaponDef { name = "Stun Baton", category = "钝器", rarity = Rarity.Rare, melee = 2, fireRate = 2.2f, damage = 1.6f, range = 1.6f, knock = 7f, stunHit = 0.5f,
                            color = H("fff0b0"), shake = 0.1f, special = "Stuns enemies for 0.5s" },
            new WeaponDef { name = "Road Sign", category = "钝器", rarity = Rarity.Uncommon, melee = 2, fx = "hammer", fireRate = 1.1f, damage = 3f, range = 2.2f, knock = 14f,
                            color = H("c8b070"), shake = 0.2f, special = "A ripped-up road sign. Swing it." },
            // ---------------- 突刺（长柄、匕首）
            new WeaponDef { name = "Ice Pick", category = "长柄", rarity = Rarity.Common, melee = 3, fireRate = 5f, damage = 0.8f, range = 1.3f, width = 0.35f,
                            color = H("e8e0d8"), shake = 0.03f, special = "Five stabs a second" },
            new WeaponDef { name = "Bayonet", category = "长柄", rarity = Rarity.Uncommon, melee = 3, fireRate = 3f, damage = 1.4f, range = 1.8f, width = 0.4f,
                            color = H("d8c8b0"), shake = 0.05f, special = "Stabs through a whole line" },
            new WeaponDef { name = "Rebar Spear", category = "长柄", rarity = Rarity.Common, melee = 3, fireRate = 1.8f, damage = 2f, range = 2.6f, width = 0.45f,
                            color = H("b07a50"), shake = 0.07f, special = "Rebar from a building site. Long reach." },
            new WeaponDef { name = "Fang Twin Daggers", category = "长柄", rarity = Rarity.Rare, melee = 3, fx = "twin", fireRate = 6f, damage = 0.9f, range = 1.3f, width = 0.5f,
                            color = H("b8f040"), shake = 0.04f, special = "Alternating stabs, fastest melee" },
            new WeaponDef { name = "Piercer Mag-Lance", category = "长柄", rarity = Rarity.Epic, melee = 3, fx = "lance", fireRate = 1.4f, damage = 3.5f, range = 3.4f, width = 0.5f, stunHit = 0.4f,
                            color = H("ff4fd8"), shake = 0.12f, special = "Charged lance, pierces and stuns" },
            // ---------------- 爆炸武器
            new WeaponDef { name = "Firework Grenade Launcher", mag = 4, reload = 2.0f, category = "爆炸武器", rarity = Rarity.Uncommon, energy = 3.75f, fireRate = 0.8f, speed = 9f, damage = 1.0f, spread = 1f, life = 0.9f, radius = 0.14f,
                            explode = 1.8f, explodeDamage = 4.5f, bullet = "grenade", color = H("ff5a2e"), muzzle = 7, trail = 0f, shake = 0.08f, special = "Area explosion" },
            new WeaponDef { name = "Swarm Micro-Missiles", mag = 3, reload = 2.2f, category = "爆炸武器", rarity = Rarity.Epic, energy = 5.8f, fireRate = 0.6f, speed = 10f, damage = 1.0f, spread = 25f, pellets = 3, life = 1.4f, radius = 0.12f,
                            homing = 320f, explode = 1.2f, explodeDamage = 3.5f, bullet = "missile", color = H("ff7a3a"), muzzle = 7, trail = 1f, shake = 0.1f, special = "3 homing missiles per shot" },
            // ---------------- 投掷冷兵器（不耗电、不换弹，扔出去的东西要捡回来）
            new WeaponDef { name = "Iron Star Shuriken", category = "投掷", thrown = true, mag = 8, reload = 0f, rarity = Rarity.Common, fireRate = 3f, speed = 20f, damage = 1.1f, spread = 1f, life = 0.9f, radius = 0.12f,
                            crit = 0.15f, bullet = "shuriken", color = H("d8d4cc"), muzzle = 0, trail = 1f, shake = 0.02f, special = "Spins out; pick it back up where it lands" },
            new WeaponDef { name = "Willow Throwing Knives", category = "投掷", thrown = true, mag = 9, reload = 0f, rarity = Rarity.Uncommon, fireRate = 1.6f, speed = 18f, damage = 1f, spread = 12f, pellets = 3, life = 0.8f, radius = 0.1f,
                            crit = 0.2f, bullet = "knife", color = H("e0dcd4"), muzzle = 0, trail = 1f, shake = 0.03f, special = "Throws a fan of 3" },
            new WeaponDef { name = "Caltrops", category = "投掷", thrown = true, mag = 3, reload = 0f, rarity = Rarity.Uncommon, fireRate = 1f, speed = 12f, damage = 0.6f, spread = 0f, pierce = 99, life = 0.5f, radius = 0.1f,
                            crit = 0.05f, bullet = "caltrop", color = H("b0a898"), muzzle = 0, trail = 0f, shake = 0.02f, special = "Scatter at the crosshair; hurts anyone who steps on them for 6s" },
            new WeaponDef { name = "Armorbreak Javelin", category = "投掷", thrown = true, mag = 3, reload = 0f, rarity = Rarity.Rare, fireRate = 0.8f, speed = 15f, damage = 4.5f, spread = 0f, pierce = 2, life = 1.2f, radius = 0.15f,
                            crit = 0.2f, bullet = "javelin", color = H("d8c8a8"), muzzle = 0, trail = 2f, shake = 0.1f, special = "Pierces 2 enemies and pins them for 1s" },
            new WeaponDef { name = "Moon Chakram", category = "投掷", thrown = true, mag = 1, reload = 0f, rarity = Rarity.Rare, fireRate = 1.5f, speed = 14f, damage = 1.6f, spread = 0f, pierce = 99, life = 4f, radius = 0.2f,
                            crit = 0.12f, bullet = "chakram", color = H("ffd27a"), muzzle = 0, trail = 2f, shake = 0.04f, special = "Flies out and back, hits both ways, no pickup needed" },
            new WeaponDef { name = "Senbon Needles", category = "投掷", thrown = true, mag = 30, reload = 0f, rarity = Rarity.Epic, fireRate = 1.2f, speed = 22f, damage = 0.5f, spread = 28f, pellets = 10, life = 0.7f, radius = 0.08f,
                            crit = 0.25f, bullet = "needle", color = H("f0e8e0"), muzzle = 0, trail = 1f, shake = 0.04f, special = "A handful of 10 needles" },
            // ---------------- 特殊武器
            new WeaponDef { name = "Mag Nailgun", mag = 12, reload = 1.6f, category = "特殊武器", rarity = Rarity.Uncommon, energy = 1.2f, fireRate = 1.5f, speed = 24f, damage = 2.2f, spread = 1.5f, pierce = 2,
                            bullet = "nail", color = H("e8c890"), muzzle = 4, trail = 1f, shake = 0.05f, special = "Nails pierce 2 enemies" },
            new WeaponDef { name = "Furnace Flamethrower", mag = 60, reload = 2.0f, category = "特殊武器", rarity = Rarity.Rare, energy = 0.13f, fireRate = 22f, speed = 7f, damage = 0.2f, spread = 10f, pierce = 99, life = 0.38f, radius = 0.2f,
                            burn = 1f, bullet = "flame", color = H("ff7a2e"), muzzle = 0, trail = 0f, shake = 0.01f, special = "Flames pass through and ignite everything" },
            new WeaponDef { name = "Tether Arc Cannon", mag = 5, reload = 2.0f, category = "特殊武器", rarity = Rarity.Legendary, energy = 4.0f, fireRate = 1.0f, speed = 16f, damage = 4.5f, spread = 1f, radius = 0.14f,
                            chain = 5, bullet = "orb", color = H("ff4fd8"), muzzle = 8, trail = 3f, shake = 0.1f, special = "Arc jumps between 5 enemies" },
        };

        static Weapons()
        {
            foreach (var w in All)
            {
                w.price = RarityPrice[(int)w.rarity];
                if (w.melee > 0)
                {
                    // 近战整体够得更远（×1.3），再按攻击距离分短 / 中 / 长，越长越重
                    w.range *= 1.3f;
                    w.reach = w.range <= 1.85f ? "短" : w.range < 2.85f ? "中" : "长";
                    w.weight = w.reach == "短" ? 2 : w.reach == "中" ? 3 : 7;
                    if (w.name.Contains("Ironbreak")) { w.reach = "长"; w.weight = 7; }          // 大锤虽然够不远，但很重
                }
                if (w.crit < 0f) w.crit = w.melee == 1 ? 0.12f : w.melee == 3 ? 0.15f : w.melee == 2 ? 0.08f : w.category switch
                {
                    "手枪" => 0.08f, "自动步枪" => 0.04f, "机枪" => 0.03f, "狙击步枪" => 0.2f, _ => 0.05f,
                };
                if (w.category == "霰弹枪") w.life *= 1.3f;                                   // 霰弹射程也稍微远一点
                if (w.melee > 0) continue;
                w.weight = w.category switch
                {
                    "手枪" => 3,
                    "自动步枪" => w.name.Contains("Torrent") ? 5 : 7,
                    "霰弹枪" => w.name.Contains("Sawn-Off") ? 6 : 7,
                    "狙击步枪" => w.name.Contains("Wrath") ? 8 : 7,
                    "机枪" => 8,
                    "爆炸武器" => 8,
                    "投掷" => w.name.Contains("Javelin") ? 5 : w.name.Contains("Chakram") || w.name.Contains("Senbon") ? 3 : 2,
                    _ => w.name.Contains("Nailgun") ? 5 : w.name.Contains("Furnace") ? 8 : 7,
                };
            }
        }

        /// <summary>重武器（重量 8）：机枪、爆炸武器、轨道炮、火焰喷射器。</summary>
        public static bool Heavy(WeaponDef w) => w != null && w.melee == 0 && w.weight >= 8;

        public static WeaponDef Get(string name)
        {
            foreach (var w in All) if (w.name.Contains(name)) return w;
            return All[0];
        }

        public static string CostText(WeaponDef w) => w.thrown ? $"Thrown · Retrievable · Carry {w.mag} · Crit {w.crit * 100f:0}% · Wt {w.weight}"
                                                    : w.melee > 0 ? $"Melee · {ReachName(w.reach)} reach · Crit {w.crit * 100f:0}% · Wt {w.weight}"
                                                    : (w.energy <= 0f ? "No power" : $"{w.energy:0.##} power/shot") + $" · Mag {w.mag} · Wt {w.weight}";
        public static string Tag(WeaponDef w) => w.signature ? "Signature Blade" : $"{RarityName[(int)w.rarity]} {CatName(w.category)}";

        /// <summary>游戏里显示的英文类别名（内部仍用中文类别做判断）。</summary>
        public static string CatName(string cat) => cat switch
        {
            "手枪" => "Pistol", "自动步枪" => "Rifle", "霰弹枪" => "Shotgun", "狙击步枪" => "Sniper", "机枪" => "MG", "刀" => "Blade",
            "钝器" => "Blunt", "爆炸武器" => "Explosive", "特殊武器" => "Special", "长柄" => "Polearm", "投掷" => "Thrown", _ => cat,
        };
        public static string ReachName(string r) => r == "短" ? "Short" : r == "中" ? "Mid" : r == "长" ? "Long" : r;

        // 掉落的稀有度权重：普通 / 改良 / 精良 / 史诗 / 传说
        public static readonly float[] ChestOdds = { 55, 30, 12, 3, 0 };       // 浮箱
        public static readonly float[] ShopOdds = { 0, 40, 40, 18, 2 };        // 老鬼的黑市
        public static readonly float[] EventOdds = { 0, 30, 40, 25, 5 };       // 投币机等随机事件

        /// <summary>按稀有度权重抽一把，尽量不和 exclude 里的重复。抽到的稀有度没有可用武器时往下降一级。</summary>
        public static WeaponDef Roll(System.Random rng, float[] odds, params WeaponDef[] exclude)
        {
            float total = 0f; foreach (var o in odds) total += o;
            float r = (float)rng.NextDouble() * total;
            int tier = 0;
            for (; tier < odds.Length - 1; tier++) { if (r < odds[tier]) break; r -= odds[tier]; }
            for (int t = tier; t >= 0; t--)
            {
                var pool = new List<WeaponDef>();
                foreach (var w in All) if ((int)w.rarity == t && System.Array.IndexOf(exclude, w) < 0) pool.Add(w);
                if (pool.Count > 0) return pool[rng.Next(pool.Count)];
            }
            return All[rng.Next(All.Length)];
        }
    }
}
