using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>扔出去的投掷物：落在地上或扎在墙上，偶尔闪一下；走近会被吸回来，回到那把投掷武器的数量里。</summary>
    public class ThrownItem : MonoBehaviour
    {
        WeaponDef def; float t, glint;

        public static void Spawn(Vector2 at, WeaponDef def, Sprite sprite, Vector2 dir, bool stuck)
        {
            var parent = GameRoot.I != null && GameRoot.I.World != null && GameRoot.I.World.Current != null ? GameRoot.I.World.Current.transform : null;
            var go = new GameObject("回收 " + def.name);
            go.transform.SetParent(parent, true);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = Sprites.Unlit;
            sr.color = new Color(0.8f, 0.78f, 0.74f);
            sr.sortingOrder = stuck ? Iso.Order(at.y) : -29950;               // 扎在墙上的立着，掉在地上的贴地
            go.transform.right = stuck ? (Vector3)dir : Quaternion.Euler(0, 0, Random.Range(0f, 360f)) * Vector3.right;
            go.AddComponent<ThrownItem>().def = def;
        }

        void Update()
        {
            t += Time.deltaTime; glint -= Time.deltaTime;
            if (glint <= 0f) { glint = 1.2f + Random.value; Effects.Sparks(transform.position, Color.white, 1, 0.5f); }   // 闪一下，告诉玩家这里能捡
            var p = GameRoot.I != null ? GameRoot.I.Player : null;
            if (p == null || p.IsDead || t < 0.4f) return;
            if (p.Ammo(def) >= p.MagSize(def)) return;                           // 已经拿满了就留在地上
            Vector2 to = (Vector2)p.transform.position + Vector2.up * 0.6f - (Vector2)transform.position;
            float range = 1.8f + 1.2f * p.N("hidden");
            if (to.magnitude < 0.45f) { p.ReturnThrown(def, 1); Destroy(gameObject); return; }
            if (to.magnitude < range) transform.position += (Vector3)(to.normalized * Mathf.Min(to.magnitude, 10f * Time.deltaTime));
        }
    }

    /// <summary>铁蒺藜撒出的一片：5 颗，敌人踩上去每 0.5 秒扎一下并绊一下；6 秒后收成一个可以捡回的。</summary>
    public class CaltropField : MonoBehaviour
    {
        WeaponDef def; float life = 6f, dmg;
        readonly List<Vector2> spikes = new List<Vector2>();
        readonly Dictionary<Enemy, float> next = new Dictionary<Enemy, float>();

        public static void Spawn(Vector2 at, WeaponDef def, float dmg)
        {
            var parent = GameRoot.I != null && GameRoot.I.World != null && GameRoot.I.World.Current != null ? GameRoot.I.World.Current.transform : null;
            var go = new GameObject("铁蒺藜");
            go.transform.SetParent(parent, true);
            go.transform.position = at;
            var f = go.AddComponent<CaltropField>();
            f.def = def; f.dmg = dmg;
            var spr = PixelFx.Bullet("caltrop");
            for (int i = 0; i < 5; i++)
            {
                Vector2 o = Random.insideUnitCircle * 0.9f; o.y *= 0.55f;
                f.spikes.Add(at + o);
                var sr = Sprites.Make(go.transform, "Spike", spr[i % 2], new Color(0.7f, 0.68f, 0.62f), -29950, o);
                sr.transform.localScale = Vector3.one * 1.3f;
            }
            Effects.Sparks(at, Color.white, 4, 2f);
        }

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f)
            {
                ThrownItem.Spawn(transform.position, def, PixelFx.Bullet("caltrop")[0], Vector2.right, false);
                Destroy(gameObject);
                return;
            }
            foreach (var e in Enemy.All.ToArray())
            {
                if (e == null || e.Allied) continue;
                Vector2 ep = e.transform.position;
                bool on = false;
                foreach (var s in spikes) if (Vector2.Distance(s, ep) < 0.45f) { on = true; break; }
                if (!on) continue;
                if (next.TryGetValue(e, out var tn) && Time.time < tn) continue;
                next[e] = Time.time + 0.5f;
                e.Hp.Damage(dmg, Vector2.zero, DamageKind.Melee);
                e.Paralyze(0.15f);                                                   // 踩到绊一下
                PixelFx.Play(PixelFx.Impact(3), ep + Vector2.up * 0.2f, 0f, Color.white, 28f);
            }
        }
    }
}
