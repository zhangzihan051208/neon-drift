using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonDrift
{
    public enum RoomKind { Combat, Shop, Event, Boss, Signal }      // Signal：幽灵频道房，拿一次 AI 协议

    /// <summary>补给道具。名字按积水区的街头黑市来起。</summary>
    public enum Item { RedLine, Gel, Cell, Overload, Ammo }

    public static class Items
    {
        public static string Name(Item i) => i switch
        {
            Item.RedLine => "Redline Shot",       // 街头诊所的止血纳米针，针管上缠着红线
            Item.Gel => "Suture Gel",         // 拾荒者往伤口上一抹就封口的医用凝胶
            Item.Cell => "Black Market Cell",        // 从报废义体上拆下来、重新充过的电芯
            Item.Ammo => "Ammo Crate",           // 巡逻队的制式弹药，撬开箱子按口径分
            _ => "Overload Battery",                 // 老鬼私藏的军用电池，标签被刮掉了
        };
        public static string Effect(Item i) => i switch
        {
            Item.RedLine => "HP +1", Item.Gel => "HP +2", Item.Cell => "Power +30", Item.Ammo => "+1 magazine for the gun in hand", _ => "Power +60",
        };
        public static int Price(Item i) => i switch { Item.RedLine => 35, Item.Gel => 65, Item.Cell => 45, Item.Ammo => 40, _ => 80 };
        public static bool IsHeal(Item i) => i == Item.RedLine || i == Item.Gel;
        public static void Apply(Item i, PlayerController p)
        {
            switch (i)
            {
                case Item.RedLine: p.Heal(1f); break;
                case Item.Gel: p.Heal(2f); break;
                case Item.Cell: p.AddEnergy(30f); break;
                case Item.Overload: p.AddEnergy(60f); break;
                case Item.Ammo: p.AddReserve(p.Weapon, p.MagSize(p.Weapon)); break;
            }
        }
    }

    public static class Loot
    {
        public static readonly Color EnergyCol = Sprites.Hex("b8f040");     // 残电、电芯：黄绿色（场景里不用青蓝色）
        public static readonly Color CreditCol = Sprites.Hex("ffcf3a");     // 信用芯片：金色
        public static readonly Color HealCol = Sprites.Hex("e43b44");
        public static readonly Color AmmoCol = Sprites.Hex("d8a040");     // 子弹：黄铜色
        static readonly System.Random rng = new System.Random();

        /// <summary>打倒敌人：几枚信用芯片 + 可能一格残电，往四周弹开，靠近后被吸过来。</summary>
        public static void KillDrop(Transform parent, Vector2 pos, int credits, float energyChance, float ammoChance = 0f)
        {
            var p = GameRoot.I?.Player;
            if (p != null && p.N("scavenger") > 0) credits = Mathf.RoundToInt(credits * Mathf.Pow(1.5f, p.N("scavenger")));
            float cell = 1f;
            if (p != null && p.N("recycle") > 0) { energyChance = 1f; cell = 1f + 2f * p.N("recycle"); }
            for (int i = 0; i < credits; i++) Drop.Spawn(parent, pos, DropKind.Credits, 1f);
            if (rng.NextDouble() < energyChance) Drop.Spawn(parent, pos, DropKind.Energy, cell);
            if (rng.NextDouble() < ammoChance) Drop.Spawn(parent, pos, DropKind.Ammo, 1f);           // 小概率掉一发子弹
        }

        /// <summary>地上的东西：带 YSort 的空物体，子物体放像素块。</summary>
        public static GameObject Holder(Transform parent, string name, Vector2 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.AddComponent<YSort>();
            return go;
        }

        public static SpriteRenderer Px(Transform parent, int w, int h, Color c, Vector2 offsetPx, int order = 0)
        {
            var sr = Sprites.Make(parent, "px", Sprites.Box(w, h), c, order, offsetPx / Sprites.PPU);
            return sr;
        }

        /// <summary>道具的像素小图标：针剂红色、电芯黄绿色。</summary>
        public static void ItemIcon(Transform t, Item i)
        {
            if (i == Item.Ammo)
            {
                Px(t, 10, 7, Sprites.Hex("4a4a30"), new Vector2(0, 3.5f));
                Px(t, 8, 2, AmmoCol, new Vector2(0, 8), 1);
                for (int k = -3; k <= 3; k += 2) Px(t, 1, 3, AmmoCol, new Vector2(k, 10), 1);
                return;
            }
            if (Items.IsHeal(i))
            {
                int h = i == Item.Gel ? 10 : 8;
                Px(t, 5, h, HealCol, new Vector2(0, h / 2f));
                Px(t, 5, 2, Sprites.Hex("f2e6d8"), new Vector2(0, h + 1), 1);
                if (i == Item.Gel) Px(t, 3, 2, Sprites.Hex("ffd0d0"), new Vector2(0, 5), 1);
            }
            else
            {
                int h = i == Item.Overload ? 11 : 8;
                Px(t, i == Item.Overload ? 7 : 5, h, EnergyCol, new Vector2(0, h / 2f));
                Px(t, 3, 2, Sprites.Hex("6a6a5a"), new Vector2(0, h + 1), 1);
                Px(t, 1, h - 3, Sprites.Hex("40581a"), new Vector2(0, h / 2f), 1);
            }
        }

        /// <summary>地上武器的像素小图标：一根带颜色的枪身。</summary>
        public static void WeaponIcon(Transform t, WeaponDef w)
        {
            int len = w.melee == 1 ? 18 : w.melee == 2 ? 14 : w.category == "手枪" ? 9 : w.category == "狙击步枪" ? 20 : 15;
            // 脚下一道稀有度颜色的光条，远远就能看出好坏；精良以上再带一点光
            var rc = Weapons.RarityColor[(int)w.rarity];
            Px(t, len + 6, 2, rc, new Vector2(0, -1));
            if (w.rarity >= Rarity.Rare) Atmosphere.Lamp(t, (Vector2)t.position + Vector2.up * 0.2f, rc, 1.4f + (int)w.rarity * 0.3f, 0.6f);
            var root = new GameObject("icon").transform;
            root.SetParent(t, false);
            root.localPosition = new Vector2(0, 6f / Sprites.PPU);
            root.localRotation = Quaternion.Euler(0, 0, 18f);
            Px(root, len + 2, 6, Sprites.Hex("15161a"), Vector2.zero);
            Px(root, len, 4, w.color, Vector2.zero, 1);
            if (w.melee == 0) Px(root, 3, 5, Sprites.Hex("2a2b30"), new Vector2(-len / 4f, -4), 1);       // 握把
            else Px(root, 3, 7, Sprites.Hex("2a2b30"), new Vector2(-len / 2f - 1, 0), 1);                   // 刀柄
        }
    }

    // ================================================================ 自动拾取：信用芯片、残电
    public enum DropKind { Energy, Credits, Ammo }

    public class Drop : MonoBehaviour
    {
        DropKind kind; float value, t; Vector2 vel;

        public static void Spawn(Transform parent, Vector2 pos, DropKind k, float v)
        {
            var go = Loot.Holder(parent, k == DropKind.Energy ? "残电" : "信用芯片", pos);
            if (k == DropKind.Energy) { Loot.Px(go.transform, 4, 6, Loot.EnergyCol, new Vector2(0, 3)); Loot.Px(go.transform, 2, 1, Sprites.Hex("e8ffb0"), new Vector2(0, 6.5f), 1); }
            else if (k == DropKind.Ammo) { Loot.Px(go.transform, 2, 5, Loot.AmmoCol, new Vector2(0, 2.5f)); Loot.Px(go.transform, 2, 2, Sprites.Hex("8a5a20"), new Vector2(0, 6), 1); }
            else { Loot.Px(go.transform, 4, 3, Loot.CreditCol, new Vector2(0, 2)); Loot.Px(go.transform, 2, 1, Sprites.Hex("8a6a10"), new Vector2(0, 2), 1); }
            var d = go.AddComponent<Drop>();
            d.kind = k; d.value = v;
            float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f), s = UnityEngine.Random.Range(2.5f, 5f);
            d.vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f) * s;
        }

        void Update()
        {
            t += Time.deltaTime;
            transform.position += (Vector3)(vel * Time.deltaTime);
            vel *= Mathf.Exp(-7f * Time.deltaTime);
            var pl = GameRoot.I?.Player;
            if (pl == null || pl.IsDead || t < 0.35f) return;
            Vector2 to = ((Vector2)pl.transform.position + Vector2.up * 0.6f) - (Vector2)transform.position;
            float dist = to.magnitude;
            if (dist < 0.45f)
            {
                Sfx.Play(kind == DropKind.Energy ? "pickup_energy" : kind == DropKind.Ammo ? "pickup_ammo" : "pickup_credit", 0.9f, null, 0.08f, 0.04f);
                if (kind == DropKind.Energy) pl.AddEnergy(value);
                else if (kind == DropKind.Ammo) { pl.AddReserve(pl.Weapon, Mathf.RoundToInt(value)); GreyboxHUD.Popup(transform.position, "+1 AMMO", Loot.AmmoCol); }
                else pl.credits += Mathf.RoundToInt(value);
                Effects.Sparks(transform.position, kind == DropKind.Energy ? Loot.EnergyCol : kind == DropKind.Ammo ? Loot.AmmoCol : Loot.CreditCol, 2, 1.5f);
                Destroy(gameObject);
                return;
            }
            if (dist < 3.5f || t > 6f)          // 靠近就吸过来；放久了也自己飞过来，不用满地捡
                transform.position += (Vector3)(to.normalized * Mathf.Min(dist, (6f + t * 4f) * Time.deltaTime));
        }
    }

    // ================================================================ 按 E 互动
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> All = new List<Interactable>();
        public static Interactable Current { get; private set; }

        public float radius = 1.3f;
        public abstract string Prompt { get; }
        public virtual string Label => null;                                  // 头顶常驻的名字
        public virtual Vector2 Point => transform.position;
        public virtual Vector2 LabelPoint => (Vector2)transform.position + Vector2.up * 0.9f;
        public abstract void Use(PlayerController p);

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() { All.Remove(this); if (Current == this) Current = null; }

        /// <summary>每帧由 GameRoot 调用：找最近的一个，按 E / 手柄 X 使用。</summary>
        public static void Tick(PlayerController p)
        {
            Current = null;
            if (p == null || p.IsDead || p.Frozen) return;
            Vector2 pp = (Vector2)p.transform.position + Vector2.up * 0.3f;
            float best = float.MaxValue;
            foreach (var i in All)
            {
                float d = Vector2.Distance(i.Point, pp);
                if (d < i.radius && d < best) { best = d; Current = i; }
            }
            var kb = Keyboard.current; var gp = Gamepad.current;
            if (Current != null && ((kb != null && kb.eKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame)))
                Current.Use(p);
        }
    }

    /// <summary>地上的武器。price &gt; 0 是黑市货架上的，要花信用点买。</summary>
    public class WeaponPickup : Interactable
    {
        public WeaponDef def; public int price;

        public static WeaponPickup Spawn(Transform parent, Vector2 pos, WeaponDef w, int price = 0)
        {
            var go = Loot.Holder(parent, "Weapon", pos);
            Loot.WeaponIcon(go.transform, w);
            var wp = go.AddComponent<WeaponPickup>();
            wp.def = w; wp.price = price;
            return wp;
        }

        public override string Label => price > 0 ? $"{def.name}  ₡{price}" : def.name;
        public override string Prompt
        {
            get
            {
                var p = GameRoot.I?.Player;
                string load = "";
                if (p != null) load = p.CanPickUp(def, out int nl) ? $"\nLoad after pickup {nl}/{p.Capacity}" : $"\nTOO HEAVY! {nl}/{p.Capacity} (G drop gun · X drop melee)";
                return (price > 0 ? $"E  Buy {def.name} (₡{price})" : $"E  Pick up {def.name}")
                       + $"\n{Weapons.Tag(def)} · {Weapons.CostText(def)}" + (string.IsNullOrEmpty(def.special) ? "" : $"\n{def.special}") + load;
            }
        }
        public Color LabelColor => Weapons.RarityColor[(int)def.rarity];

        public override void Use(PlayerController p)
        {
            if (!p.CanPickUp(def, out int nl))
            {
                GreyboxHUD.Message($"Too heavy: {def.name} weighs {def.weight}, load would be {nl}/{p.Capacity}\nG drops your gun, X drops your melee weapon", 2.5f);
                return;
            }
            if (price > 0)
            {
                if (p.credits < price) { GreyboxHUD.Message($"Not enough credits (need ₡{price - p.credits} more)", 1.5f); return; }
                p.credits -= price;
            }
            if (price > 0) Sfx.Play("buy");
            var old = p.PickUp(def);
            if (old != null) Spawn(transform.parent, transform.position, old);
            Effects.Sparks(transform.position, def.color, 6, 2.5f);
            GreyboxHUD.Message($"{Weapons.Tag(def)} · {def.name} ({Weapons.CostText(def)})", 1.6f);
            Destroy(gameObject);
        }
    }

    /// <summary>补给道具：浮箱里开出来的免费，黑市货架上的要钱。</summary>
    public class ItemPickup : Interactable
    {
        public Item item; public int price;

        public static ItemPickup Spawn(Transform parent, Vector2 pos, Item i, int price = 0)
        {
            var go = Loot.Holder(parent, "Item", pos);
            Loot.ItemIcon(go.transform, i);
            var ip = go.AddComponent<ItemPickup>();
            ip.item = i; ip.price = price;
            return ip;
        }

        public override string Label => price > 0 ? $"{Items.Name(item)}  ₡{price}" : Items.Name(item);
        public override string Prompt => (price > 0 ? $"E  Buy {Items.Name(item)} (₡{price})" : $"E  Use {Items.Name(item)}") + $"\n{Items.Effect(item)}";

        public override void Use(PlayerController p)
        {
            bool heal = Items.IsHeal(item);
            if (heal && p.Hp.hp >= p.Hp.max) { GreyboxHUD.Message("HP is full. Save it for later", 1.2f); return; }
            if (!heal && item != Item.Ammo && p.energy >= p.maxEnergy) { GreyboxHUD.Message("Power is full. Save it for later", 1.2f); return; }
            if (price > 0)
            {
                if (p.credits < price) { GreyboxHUD.Message($"Not enough credits (need ₡{price - p.credits} more)", 1.5f); return; }
                p.credits -= price;
            }
            Items.Apply(item, p);
            Sfx.Play(price > 0 ? "buy" : "item_use");
            Effects.Sparks(transform.position, heal ? Loot.HealCol : item == Item.Ammo ? Loot.AmmoCol : Loot.EnergyCol, 8, 2.5f);
            GreyboxHUD.Message($"{Items.Name(item)}: {Items.Effect(item)}", 1.4f);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 浮箱：积水区的补给箱，漂在水上被冲到街区里。清完小怪房出现，打开掉一样东西：
    /// 回血（缝合凝胶）/ 回电（黑市电芯）/ 随机武器。生命低于一半时回血概率翻倍，电量低于三成时回电概率翻倍。
    /// </summary>
    public class Chest : Interactable
    {
        SpriteRenderer stripe;

        public static Chest Spawn(Transform parent, Vector2 pos)
        {
            var go = Loot.Holder(parent, "浮箱", pos);
            Loot.Px(go.transform, 28, 14, Sprites.Hex("3b2f26"), new Vector2(0, 7));
            Loot.Px(go.transform, 28, 4, Sprites.Hex("4f3e30"), new Vector2(0, 15), 1);
            var c = go.AddComponent<Chest>();
            c.stripe = Loot.Px(go.transform, 28, 2, Sprites.Hex("c8642e"), new Vector2(0, 9), 2);
            Loot.Px(go.transform, 4, 4, Loot.CreditCol, new Vector2(0, 11), 3);
            GroundShadow.Attach(go.transform, 1.0f);
            Effects.Splash(pos, 6, 2f);
            return c;
        }

        public override string Prompt => "E  Open the drift crate";
        public override string Label => "Drift Crate";

        public override void Use(PlayerController p)
        {
            float wHeal = p.Hp.hp < p.Hp.max * 0.5f ? 2f : 1f, wEnergy = p.energy < p.maxEnergy * 0.3f ? 2f : 1f, wWeapon = 1f;
            float wAmmo = p.Ammo(p.Weapon) + p.Reserve(p.Weapon) < p.MagSize(p.Weapon) ? 1.5f : 0.5f;      // 子弹快没了就多开出弹药箱
            float r = UnityEngine.Random.value * (wHeal + wEnergy + wWeapon + wAmmo);
            Vector2 at = (Vector2)transform.position + new Vector2(0f, -0.9f);
            if (r < wHeal) ItemPickup.Spawn(transform.parent, at, Item.Gel);
            else if (r < wHeal + wEnergy) ItemPickup.Spawn(transform.parent, at, Item.Cell);
            else if (r < wHeal + wEnergy + wAmmo) ItemPickup.Spawn(transform.parent, at, Item.Ammo);
            else WeaponPickup.Spawn(transform.parent, at, Weapons.Roll(new System.Random(), Weapons.ChestOdds, p.Slots[0], p.Slots[1]));
            stripe.color = Sprites.Hex("2a211b");
            Sfx.Play("chest_open", 1f, transform.position);
            Effects.Sparks((Vector2)transform.position + Vector2.up * 0.4f, Loot.CreditCol, 10, 3f);
            CameraRig.Shake(0.05f);
            Destroy(this);                          // 打开后只剩空箱子
        }
    }

    /// <summary>通用的“按 E 做一件事”：商店刷新、随机事件。use 返回 true 表示用掉了（消失）。</summary>
    public class ActionPoint : Interactable
    {
        public string prompt, label;
        public Func<PlayerController, bool> use;

        public static ActionPoint Spawn(Transform parent, Vector2 pos, string label, string prompt, Func<PlayerController, bool> use)
        {
            var go = Loot.Holder(parent, label, pos);
            var a = go.AddComponent<ActionPoint>();
            a.label = label; a.prompt = prompt; a.use = use;
            return a;
        }

        public override string Prompt => prompt;
        public override string Label => label;
        public override Vector2 LabelPoint => (Vector2)transform.position + Vector2.up * 1.6f;
        public override void Use(PlayerController p) { if (use(p)) Destroy(gameObject); }
    }

    /// <summary>
    /// 梯子：靠在后墙（右上那面）房子上的消防梯。清完房间才出现，按 E 爬上去，黑屏转场到下一个房间。
    /// 房间之间互相看不到，只能靠爬梯子过去。
    /// </summary>
    public class Ladder : Interactable
    {
        public RoomKind target;
        public string title;
        public Vector2 Foot { get; private set; }
        Vector2 point, labelAt;

        public static Ladder Build(Transform parent, float tx, float y0, RoomKind target, string title, bool fork = false, float height = LadderArt.HeightUnits, bool locked = false)
        {
            var go = new GameObject("Ladder");
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Ladder>();
            l.target = target; l.title = title;
            const float W = LadderArt.LadderTiles;
            // 梯子背后的一面墙（不同去处样式不同），挡住后面的房子；盖在房子上，但永远在人和物件后面
            var art = LadderArt.Make(target, fork, UnityEngine.Random.Range(0, 100000), height);
            Vector2 wallStart = Iso.W(tx - LadderArt.LadderOffset, y0);
            // 和房子用同一套前后规则（按最靠前那个角排序）：右边的邻居盖在它上面，左边的邻居在它后面，人永远在它前面
            Vector2 wallFront = Iso.W(tx - LadderArt.LadderOffset + LadderArt.WallTiles, y0);
            int order = Buildings.BackLayer + Iso.Order(wallFront.y) + 3;
            var main = Sprites.Make(go.transform, "Wall", art.main, Color.white, order, Vector2.zero);
            main.transform.position = wallStart;
            var glow = Sprites.Make(go.transform, "Glow", art.glow, Color.white, order + 1, Vector2.zero);
            glow.transform.position = wallStart;
            glow.sharedMaterial = Glow.Mat();
            l.textures = new[] { art.main.texture, art.glow.texture, art.bareMain.texture, art.bareGlow.texture };
            l.mainSr = main; l.glowSr = glow; l.art = art;
            // 梯子顶上一盏灯，颜色跟着样式走
            var lamp = Atmosphere.Lamp(go.transform, wallStart + art.lamp, art.lampCol, 3f, 0.9f);
            if (art.flicker)
            {
                var fl = go.AddComponent<GlowFlicker>();
                fl.sr = glow; fl.light2d = lamp; fl.baseLight = 0.9f; fl.blink = target == RoomKind.Boss;
            }
            Vector2 a = Iso.W(tx, y0), b = Iso.W(tx + W, y0);
            l.Foot = Iso.W(tx + W * 0.5f, y0 + 0.35f);
            l.point = Iso.W(tx + W * 0.5f, y0 + 1.0f);
            l.labelAt = (a + b) * 0.5f + Vector2.up * 2.4f;
            l.radius = 1.5f;
            l.lamp = lamp;
            if (locked)                                                          // 墙先立着，梯子等房间清完才放下来
            {
                main.sprite = art.bareMain; glow.sprite = art.bareGlow;
                if (lamp != null) lamp.enabled = false;
                l.enabled = false;
            }
            return l;
        }

        SpriteRenderer mainSr, glowSr; LadderArt.Result art; UnityEngine.Rendering.Universal.Light2D lamp;
        public bool Locked => !enabled;

        /// <summary>房间清完：墙上放下梯子、点亮梯子顶上的灯，可以爬了。</summary>
        public void Unlock()
        {
            if (enabled) return;
            mainSr.sprite = art.main; glowSr.sprite = art.glow;
            if (lamp != null) lamp.enabled = true;
            enabled = true;
        }

        Texture2D[] textures;
        void OnDestroy() { if (textures != null) foreach (var t in textures) if (t != null) Destroy(t); }

        public override Vector2 Point => point;
        public override Vector2 LabelPoint => labelAt;
        public override string Label => title;
        public override string Prompt => $"E  Climb: {title}";
        public override void Use(PlayerController p) => GameRoot.I.Climb(this);
    }
}
