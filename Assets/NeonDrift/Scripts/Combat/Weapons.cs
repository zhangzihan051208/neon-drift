using UnityEngine;

namespace NeonDrift
{
    /// <summary>一把武器的参数。枪：子弹样式和弹道；近战：范围和挥砍角度。</summary>
    public class WeaponDef
    {
        public string name, category;
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
        public int melee;                        // 0 枪 / 1 刀 / 2 钝器
        public float range = 1.8f, arc = 150f;   // 近战：攻击距离和挥砍角度
    }

    /// <summary>
    /// 九类武器各一把样板（临时测试用，数字键 1–9 切换）。数值是初版手感，之后按章节平衡再调。
    /// 颜色只用暖色和洋红，不用青蓝色（和场景的光保持一致）。
    /// </summary>
    public static class Weapons
    {
        static Color H(string s) => Sprites.Hex(s);

        public static readonly WeaponDef[] Test =
        {
            new WeaponDef { name = "「巷战」手枪", category = "手枪", fireRate = 4.5f, speed = 13f, damage = 1f, spread = 2f, bounces = 1,
                            bullet = "pistol", color = H("ffb347"), muzzle = 5, trail = 2f, shake = 0.04f },
            new WeaponDef { name = "「老兵」突击步枪", category = "自动步枪", fireRate = 9f, speed = 17f, damage = 0.7f, spread = 4f,
                            bullet = "rifle", color = H("ffd27a"), muzzle = 6, trail = 2f, shake = 0.03f },
            new WeaponDef { name = "「碎颅」霰弹枪", category = "霰弹枪", fireRate = 1.6f, speed = 14f, damage = 0.55f, spread = 16f, pellets = 7, life = 0.45f,
                            bullet = "pellet", color = H("ff9a3c"), muzzle = 9, trail = 1f, shake = 0.14f },
            new WeaponDef { name = "「幽灵独角兽」狙击步枪", category = "狙击步枪", fireRate = 1.1f, speed = 34f, damage = 4f, spread = 0f, pierce = 3, life = 1.5f,
                            bullet = "sniper", color = H("ff6ad5"), muzzle = 8, trail = 3f, shake = 0.12f },
            new WeaponDef { name = "「绞肉机」重机枪", category = "机枪", fireRate = 14f, speed = 16f, damage = 0.45f, spread = 7f,
                            bullet = "mg", color = H("ffcf5a"), muzzle = 6, trail = 2f, shake = 0.025f },
            new WeaponDef { name = "「白昼」热能武士刀", category = "刀", melee = 1, fireRate = 3.2f, damage = 1.6f, range = 1.9f, arc = 150f,
                            color = H("ff8a3d"), shake = 0.06f },
            new WeaponDef { name = "「铁破」动力大锤", category = "钝器", melee = 2, fireRate = 1.3f, damage = 2.6f, range = 1.6f,
                            color = H("ffc070"), shake = 0.2f },
            new WeaponDef { name = "「烟花」榴弹发射器", category = "爆炸武器", fireRate = 1.2f, speed = 9f, damage = 1f, spread = 1f, life = 0.9f, radius = 0.14f,
                            explode = 1.8f, explodeDamage = 3f, bullet = "grenade", color = H("ff5a2e"), muzzle = 7, trail = 0f, shake = 0.08f },
            new WeaponDef { name = "「熔炉」火焰喷射器", category = "特殊武器", fireRate = 22f, speed = 7f, damage = 0.18f, spread = 10f, pierce = 99, life = 0.38f, radius = 0.2f,
                            bullet = "flame", color = H("ff7a2e"), muzzle = 0, trail = 0f, shake = 0.01f },
        };
    }
}
