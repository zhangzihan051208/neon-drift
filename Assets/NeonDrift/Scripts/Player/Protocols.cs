using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>能被骇入的东西：敌人、炮台、变电箱。</summary>
    public interface IHackable
    {
        Vector2 HackPoint { get; }
        int HackCost { get; }
        bool CanHack { get; }
        string HackName { get; }
        void OnHacked(PlayerController p);
    }

    /// <summary>
    /// 幽灵频道的 AI 协议。每次入侵只来一个 AI，拿出它自己的三个协议；协议挂在零的动作上（攻击、近战、闪移、换武器、护甲被打掉、骇入、被动）。
    /// 协议不叠层，拿过的不会再出现。先做霓姬（电）、熔心（火）、门客（骇入 / 倒戈），其余三个 AI 等对应系统做好再加。
    /// </summary>
    public static class Protocols
    {
        public static readonly string[] AiName = { "NEON HIME", "FORGEHEART", "DOORMAN" };
        public static readonly Color[] AiColor = { Sprites.Hex("ff4fd8"), Sprites.Hex("ff7a2e"), Sprites.Hex("ffcf3a") };
        public static readonly string[] AiLine =
        {
            "NEON HIME: \"The audience left, Zero. Will you be my only stage?\"",
            "FORGEHEART: \"This city owes me a furnace. Lend me your hands.\"",
            "DOORMAN: \"Old friend. The Flux backdoor is still open for you.\"",
        };
        static readonly Color Spark = Sprites.Hex("fff0b0");

        static BuffDef P(int ai, string id, string slot, string name, string desc) =>
            new BuffDef { id = id, group = BuffGroup.Element, ai = ai, slot = slot, name = $"{AiName[ai]}: {name}", desc = desc };

        public static readonly BuffDef[] All =
        {
            // ---------------- 霓姬：电击，麻痹，电弧在敌人之间弹射
            P(0, "arc", "Attack", "Arc Rounds", "Gun hits arc to 1 more enemy"),
            P(0, "n_blade", "Melee", "Thunder Edge", "Melee hits stun for 0.6s and throw an arc"),
            P(0, "n_trail", "Dash", "Static Trail", "Dashing leaves a shocking trail for 1.5s"),
            P(0, "n_swap", "Swap", "Swap Discharge", "Swapping guns discharges around you (3s cooldown)"),
            P(0, "n_armor", "Armor", "Static Shell", "Losing armor stuns enemies within 3 tiles for 1.2s"),
            P(0, "n_hack", "Hack", "Broadcast", "Hacks also arc to 3 nearby enemies"),
            P(0, "n_ground", "Passive", "Conduction", "Shocking an enemy in a puddle electrifies the whole puddle"),
            P(0, "n_crit", "Crit", "Overload Crit", "Crits throw arcs to 2 nearby enemies"),
            // ---------------- 熔心：热能，燃烧
            P(1, "ember", "Attack", "Hot Rounds", "All attacks set enemies on fire"),
            P(1, "e_blade", "Melee", "Molten Edge", "Melee damage +30% and sets enemies on fire"),
            P(1, "e_wall", "Dash", "Fire Line", "Dashing leaves a wall of fire for 1.5s"),
            P(1, "e_swap", "Swap", "Hot Swap", "For 2s after swapping, bullets set enemies on fire"),
            P(1, "e_armor", "Armor", "Molten Shell", "Losing armor sprays fire around you"),
            P(1, "e_hack", "Hack", "Overheat", "Hacked enemies catch fire"),
            P(1, "e_burst", "Passive", "Ember Burst", "Burning enemies explode when they die"),
            P(1, "e_crit", "Crit", "Melting Point", "Crits ignite; +50% crit damage vs burning enemies"),
            // ---------------- 门客：骇入，倒戈
            P(2, "seeker", "Attack", "Seeker", "Gun bullets gently home in"),
            P(2, "m_turn", "Hack", "Turncoat", "Hacking turns an enemy to your side for 4s"),
            P(2, "m_ram", "Hack", "Expansion", "Max RAM +1, regenerates 30% faster"),
            P(2, "m_dash", "Dash", "Quick Jack", "Hacks need no charge-up for 1.5s after a dash"),
            P(2, "m_swap", "Swap", "Sleight of Hand", "+30% crit for 1.5s after swapping guns"),
            P(2, "m_armor", "Armor", "Scapegoat", "Losing armor turns the nearest enemy for 3s"),
            P(2, "m_contacts", "Passive", "Contacts", "One enemy in each room's first wave switches sides for 6s"),
            P(2, "m_crit", "Crit", "Exploit", "Always crit stunned enemies"),
        };

        /// <summary>入侵：随机一个还有协议没拿过的 AI，出它的三个协议（不够三个就从别的 AI 补）。</summary>
        public static List<BuffDef> Offer(System.Random rng, PlayerController p, out int ai)
        {
            var ais = new List<int>();
            for (int i = 0; i < AiName.Length; i++)
                foreach (var b in All) if (b.ai == i && p.N(b.id) == 0) { ais.Add(i); break; }
            ai = ais.Count > 0 ? ais[rng.Next(ais.Count)] : rng.Next(AiName.Length);
            var mine = new List<BuffDef>(); var rest = new List<BuffDef>();
            foreach (var b in All) if (p.N(b.id) == 0) (b.ai == ai ? mine : rest).Add(b);
            var result = new List<BuffDef>();
            while (result.Count < 3 && mine.Count > 0) { var b = mine[rng.Next(mine.Count)]; mine.Remove(b); result.Add(b); }
            while (result.Count < 3 && rest.Count > 0) { var b = rest[rng.Next(rest.Count)]; rest.Remove(b); result.Add(b); }
            return result;
        }

        static bool Has(string id) { var p = GameRoot.I?.Player; return p != null && p.N(id) > 0; }

        // ---------------------------------------------------------------- 各动作上的钩子
        public static void OnHackEnemy(PlayerController p, Enemy e)
        {
            if (p.N("n_hack") > 0) Projectile.Chain(e.HackPoint, e.Hp, 3, 2f, Spark, Team.Player);
            if (p.N("e_hack") > 0) Burning.Apply(e.Hp, 2f, AiColor[1]);
            ShockPuddle.Conduct(e.transform.position);
        }

        public static void OnMeleeHit(PlayerController p, Enemy e)
        {
            if (p.N("n_blade") > 0)
            {
                e.Paralyze(0.6f);
                Projectile.Chain(e.HackPoint, e.Hp, 1, 1f, Spark, Team.Player);
                ShockPuddle.Conduct(e.transform.position);
            }
        }

        static float swapZapCd;
        public static void OnSwap(PlayerController p)
        {
            Vector2 c = (Vector2)p.transform.position + Vector2.up * 1f;
            if (p.N("n_swap") > 0 && Time.time > swapZapCd)
            {
                swapZapCd = Time.time + 3f;
                PixelFx.Play(PixelFx.Zap(16), c, 0f, Spark, 22f);
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null || e.Allied) continue;
                    Vector2 ec = e.HackPoint;
                    if (Vector2.Distance(ec, c) > 3f) continue;
                    Projectile.ArcFx(c, ec, Spark);
                    e.Hp.Damage(2f, (ec - c).normalized, DamageKind.Shock);
                    ShockPuddle.Conduct(e.transform.position);
                }
            }
            if (p.N("e_swap") > 0) p.swapBurnUntil = Time.time + 2f;
            if (p.N("m_swap") > 0) p.swapCritUntil = Time.time + 1.5f;
        }

        public static void OnArmorBroken(PlayerController p)
        {
            Vector2 c = (Vector2)p.transform.position + Vector2.up * 0.8f;
            if (p.N("n_armor") > 0)
            {
                PixelFx.Play(PixelFx.Zap(18), c, 0f, Spark, 22f);
                foreach (var e in Enemy.All.ToArray())
                    if (e != null && !e.Allied && Vector2.Distance(e.HackPoint, c) < 3f) e.Paralyze(1.2f);
            }
            if (p.N("e_armor") > 0)
            {
                PixelFx.Play(PixelFx.Explosion(Mathf.RoundToInt(2.5f * Sprites.PPU * 0.75f)), c, 0f, AiColor[1], 18f);
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null || e.Allied || Vector2.Distance(e.HackPoint, c) > 2.5f) continue;
                    e.Hp.Damage(2f, (e.HackPoint - c).normalized, DamageKind.Fire);
                    Burning.Apply(e.Hp, 1.5f, AiColor[1]);
                }
            }
            if (p.N("m_armor") > 0)
            {
                var e = Enemy.NearestHostile(p.transform.position, 8f, null);
                if (e != null && !(e is TurretEnemy)) e.Convert(3f);
            }
        }

        /// <summary>暴击时的协议效果。</summary>
        public static void OnCrit(PlayerController p, Enemy e, Vector2 at)
        {
            if (e == null) return;
            if (p.N("n_crit") > 0) { Projectile.Chain(at, e.Hp, 2, 1.5f, Spark, Team.Player); ShockPuddle.Conduct(e.transform.position); }
            if (p.N("e_crit") > 0) Burning.Apply(e.Hp, 2f, AiColor[1]);
        }

        /// <summary>门客「人脉」：第一波刷出来后，随机一个敌人倒戈。</summary>
        public static void OnFirstWave(RoomDirector room)
        {
            if (!Has("m_contacts")) return;
            var list = new List<Enemy>();
            foreach (var e in Enemy.All) if (e != null && !e.Allied && e.transform.parent == room.transform) list.Add(e);
            if (list.Count > 0) list[Random.Range(0, list.Count)].Convert(6f);
        }

        /// <summary>熔心「余烬爆」：着火的敌人死时爆开。</summary>
        public static void OnEnemyDied(Enemy dead)
        {
            if (!Has("e_burst") || dead.GetComponent<Burning>() == null) return;
            Vector2 c = dead.HackPoint;
            PixelFx.Play(PixelFx.Explosion(Mathf.RoundToInt(1.6f * Sprites.PPU * 0.75f)), c, 0f, AiColor[1], 18f);
            Effects.Flash(c, AiColor[1], 3f, 1.6f, 0.1f);
            foreach (var e in Enemy.All.ToArray())
            {
                if (e == null || e == dead || e.Allied || Vector2.Distance(e.HackPoint, c) > 1.6f) continue;
                e.Hp.Damage(2f, (e.HackPoint - c).normalized, DamageKind.Fire);
                Burning.Apply(e.Hp, 1.2f, AiColor[1]);
            }
        }

        /// <summary>闪移时沿路留下电痕 / 火墙。</summary>
        public static void DashTrail(PlayerController p, Vector2 pos)
        {
            if (p.N("n_trail") > 0) TrailHazard.Spawn(pos, false);
            if (p.N("e_wall") > 0) TrailHazard.Spawn(pos, true);
        }
    }

    /// <summary>闪移留下的一段电痕或火墙：1.5 秒，碰到的敌人每 0.4 秒吃一次。</summary>
    public class TrailHazard : MonoBehaviour
    {
        bool fire; float life = 1.5f;
        readonly Dictionary<Enemy, float> next = new Dictionary<Enemy, float>();

        public static void Spawn(Vector2 pos, bool fire)
        {
            var go = new GameObject(fire ? "FireTrail" : "ShockTrail");
            go.transform.position = pos;
            var t = go.AddComponent<TrailHazard>();
            t.fire = fire;
            var sr = fire
                ? PixelFx.Play(PixelFx.BurnFlame(), pos + Random.insideUnitCircle * 0.1f, 0f, Protocols.AiColor[1], 12f + Random.value * 4f, Iso.Order(pos.y))
                : PixelFx.Play(PixelFx.PuddleArc(Random.Range(0, 6)), pos, Random.Range(-30f, 30f), Sprites.Hex("fff0b0"), 16f, -29950);
            var fa = sr.GetComponent<FxAnim>(); fa.loop = true; fa.life = t.life;
        }

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f) { Destroy(gameObject); return; }
            Vector2 pos = transform.position;
            foreach (var e in Enemy.All.ToArray())
            {
                if (e == null || e.Allied || Vector2.Distance(e.transform.position, pos) > 0.65f) continue;
                if (next.TryGetValue(e, out var tn) && Time.time < tn) continue;
                next[e] = Time.time + 0.4f;
                if (fire) Burning.Apply(e.Hp, 1.2f, Protocols.AiColor[1]);
                else { e.Hp.Damage(1.5f, Vector2.zero, DamageKind.Shock); e.Paralyze(0.5f); ShockPuddle.Conduct(e.transform.position); }
            }
        }
    }
}
