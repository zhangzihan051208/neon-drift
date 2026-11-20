using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>伤害类型：抗性按类型减免。积水导电、火焰、爆炸的敌人和陷阱以后都按这个打。</summary>
    public enum DamageKind { Bullet, Melee, Explosion, Fire, Shock }

    /// <summary>增益分五系：攻击、生存（含抗性）、武器专精、元素（幽灵频道 AI 协议）、资源。</summary>
    public enum BuffGroup { Attack, Defense, Weapon, Element, Resource }

    public class BuffDef
    {
        public string id, name, desc;
        public BuffGroup group;
        public string category;         // 武器专精：只对这一类武器生效
        public int ai = -1;             // AI 协议：属于哪个 AI（Protocols.AiName 的下标），普通增益是 -1
        public string slot;             // AI 协议：挂在哪个动作上
    }

    /// <summary>
    /// 增益池（义体）：义体诊所事件三选一、黑市花信用点买。AI 协议见 Protocols。同一个增益可以重复拿，效果叠加。
    /// 数值写在 PlayerController 里用到它的地方，这里只放名字和说明。
    /// </summary>
    public static class Buffs
    {
        static BuffDef B(string id, BuffGroup g, string name, string desc, string cat = null) => new BuffDef { id = id, group = g, name = name, desc = desc, category = cat };

        public static readonly BuffDef[] All =
        {
            // ---------------- 攻击（原来的三个 + 新的）
            B("overclock", BuffGroup.Attack, "Overclock", "Fire rate +20%"),
            B("breach", BuffGroup.Attack, "Breach", "Damage +25%"),
            B("refract", BuffGroup.Attack, "Refract", "Bullets bounce off walls 1 more time"),
            B("crit", BuffGroup.Attack, "Hunter Chip", "Crit chance +15%"),
            B("critdmg", BuffGroup.Attack, "Lethal", "Crit damage +50% (x2 -> x2.5)"),
            B("weak", BuffGroup.Attack, "Weak Point Scan", "+25% crit chance vs stunned, burning or turned enemies"),
            B("afterglow", BuffGroup.Attack, "Afterglow", "After a crit, shoot and swing 30% faster for 1s"),
            B("eco", BuffGroup.Attack, "Power Saver", "All weapons use 30% less power"),
            B("fastload", BuffGroup.Attack, "Speed Loader", "Reload 35% faster"),
            B("bigmag", BuffGroup.Attack, "Extended Mags", "All gun magazines +30%"),
            B("closeq", BuffGroup.Attack, "Close Quarters", "Melee damage +25%; melee kills restore 1 armor"),
            // ---------------- 生存与抗性（原来的一个 + 新的）
            B("redundant", BuffGroup.Defense, "Redundancy", "Max HP +1 and heal 1"),
            B("plating", BuffGroup.Defense, "Subdermal Plating", "All damage taken -20%"),
            B("deflect", BuffGroup.Defense, "Ballistic Deflector", "Bullet damage taken -35%"),
            B("parry", BuffGroup.Defense, "Parry Reflex", "Melee damage taken -40%"),
            B("fireproof", BuffGroup.Defense, "Fireproof Layer", "Fire damage taken -60%"),
            B("insulate", BuffGroup.Defense, "Insulation", "Shock damage taken -60% (live water)"),
            B("blastpad", BuffGroup.Defense, "Blast Padding", "Explosion damage taken -50%"),
            B("shield", BuffGroup.Defense, "Emergency Shield", "The first hit in each room does nothing"),
            B("reactive", BuffGroup.Defense, "Reactive Armor", "Getting hurt sets off a shockwave: knockback + 2 damage"),
            B("aftershock", BuffGroup.Defense, "Aftershock Buffer", "+0.5s invulnerability after getting hurt"),
            B("nano", BuffGroup.Defense, "Clotting Nanites", "Heal 1 HP after clearing each room"),
            B("plates", BuffGroup.Defense, "Composite Plates", "Max armor +1"),
            B("repair", BuffGroup.Defense, "Nano Repair", "Armor regenerates 50% faster"),
            B("overcharge", BuffGroup.Defense, "Overcharged Armor", "Losing armor shocks enemies within 2.5 tiles (3 damage)"),
            B("bulwark", BuffGroup.Defense, "Full Plate Assault", "Damage +20% while armor is full"),
            B("doubledash", BuffGroup.Defense, "Double Dash", "Dash cooldown -35%"),
            B("exo", BuffGroup.Defense, "Exoskeleton", "Carry capacity +3"),
            B("springleg", BuffGroup.Defense, "Spring Legs", "Move speed +12%"),
            // ---------------- 武器专精：每类武器一个
            B("quickdraw", BuffGroup.Weapon, "Quickdraw", "Pistols: fire rate +40%, damage +20%", "手枪"),
            B("stabilizer", BuffGroup.Weapon, "Stabilizer", "Rifles: spread -60%, damage +20%", "自动步枪"),
            B("slugs", BuffGroup.Weapon, "Heavy Shot", "Shotguns: +3 pellets, range +25%", "霰弹枪"),
            B("falcon", BuffGroup.Weapon, "Falcon Eye", "Snipers: damage +50%, pierce 2 more", "狙击步枪"),
            B("beltfeed", BuffGroup.Weapon, "Belt Feed", "MGs: power use -40%, fire rate +15%", "机枪"),
            B("hidden", BuffGroup.Weapon, "Hidden Blades", "Thrown: damage +30%, longer pickup range, 20% chance not to use one", "投掷"),
            B("iai", BuffGroup.Weapon, "Iaido", "Blades: first swing after 1.5s of rest always crits", "刀"),
            B("tiblade", BuffGroup.Weapon, "Titanium Edge", "Blades: reach +30%, damage +30%", "刀"),
            B("servo", BuffGroup.Weapon, "Servo Arms", "Blunt: damage +40%, speed +20%, knockback +50%", "钝器"),
            B("highex", BuffGroup.Weapon, "High Explosive", "Explosives: radius +40%, blast damage +40%", "爆炸武器"),
            B("overheat", BuffGroup.Weapon, "Overheat Core", "Specials: burn damage x2, arcs jump 2 more times", "特殊武器"),
            B("pierce", BuffGroup.Weapon, "Skewer", "Polearms: reach +25%, damage +30%", "长柄"),
            // AI 协议（元素）单独放在 Protocols 里，只在幽灵频道入侵时出现
            // ---------------- 资源
            B("scavenger", BuffGroup.Resource, "Scavenger", "Enemies drop 50% more credits"),
            B("recycle", BuffGroup.Resource, "Charge Recycler", "Enemies always drop charge, +2 power each"),
        };

        public static readonly string[] GroupName = { "Offense", "Survival", "Weapon Mastery", "AI Protocol", "Resource" };
        public static readonly Color[] GroupColor = { Sprites.Hex("ff5a2e"), Sprites.Hex("e43b44"), Sprites.Hex("ffb347"), Sprites.Hex("ff4fd8"), Sprites.Hex("ffcf3a") };

        public static BuffDef Get(string id)
        {
            foreach (var b in All) if (b.id == id) return b;
            foreach (var b in Protocols.All) if (b.id == id) return b;
            return null;
        }

        /// <summary>
        /// 抽 count 个不重复的增益。preferWeapon 时第一个尽量是“手上武器那一类”的专精，
        /// 其余从 groups 里随机（groups 为空表示全部）。
        /// </summary>
        public static List<BuffDef> Roll(System.Random rng, int count, PlayerController p, bool preferWeapon, params BuffGroup[] groups)
        {
            var result = new List<BuffDef>();
            if (preferWeapon && p != null)
            {
                var fit = new List<BuffDef>();
                foreach (var b in All)
                    if (b.group == BuffGroup.Weapon && ((p.Slots[0] != null && p.Slots[0].category == b.category) || (p.Slots[1] != null && p.Slots[1].category == b.category)
                                                        || (p.MeleeWeapon != null && p.MeleeWeapon.category == b.category)))
                        fit.Add(b);
                if (fit.Count > 0) result.Add(fit[rng.Next(fit.Count)]);
            }
            var pool = new List<BuffDef>();
            foreach (var b in All)
                if (!result.Contains(b) && (groups.Length == 0 || System.Array.IndexOf(groups, b.group) >= 0)) pool.Add(b);
            while (result.Count < count && pool.Count > 0)
            {
                var b = pool[rng.Next(pool.Count)];
                pool.Remove(b);
                result.Add(b);
            }
            return result;
        }
    }
}
