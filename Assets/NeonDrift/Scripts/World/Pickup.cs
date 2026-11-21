using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>增益三选一：走上去拿走一个，同一组的其余几个消失。</summary>
    public class Pickup : MonoBehaviour
    {
        public static readonly List<Pickup> All = new List<Pickup>();

        public BuffDef buff;
        int group;
        public string Label => buff.ai >= 0 ? $"{buff.name}\n[{buff.slot}] {buff.desc}" : $"{buff.name}\n{buff.desc}";
        public Color LabelColor => buff.ai >= 0 ? Protocols.AiColor[buff.ai] : Buffs.GroupColor[(int)buff.group];

        public static Pickup Spawn(Transform parent, Vector2 pos, BuffDef b, int group = 0)
        {
            Color c = b.ai >= 0 ? Protocols.AiColor[b.ai] : Buffs.GroupColor[(int)b.group];
            var sr = Sprites.Make(parent, "Buff", Sprites.Box(14, 14), c, 0, pos);
            sr.transform.localRotation = Quaternion.Euler(0, 0, 45);
            sr.sortingOrder = Iso.Order(pos.y);
            sr.gameObject.AddComponent<Pulse>();
            var p = sr.gameObject.AddComponent<Pickup>();
            p.buff = b; p.group = group;
            return p;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            var pl = GameRoot.I?.Player;
            if (pl == null || pl.IsDead) return;
            Vector2 d = (Vector2)transform.position - ((Vector2)pl.transform.position + Vector2.up * 0.4f);
            if (d.magnitude < 0.8f)
            {
                pl.ApplyBuff(buff);
                Sfx.Play("buff");
                Effects.Burst(transform.position, GetComponent<SpriteRenderer>().color);
                GreyboxHUD.Message($"{Buffs.GroupName[(int)buff.group]}: {buff.name}  {buff.desc}", 2.5f);
                foreach (var p in All.ToArray()) if (p.group == group) Destroy(p.gameObject);
            }
        }
    }
}
