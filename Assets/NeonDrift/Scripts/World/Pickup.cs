using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    public enum RewardType { FireRate, Bounce, MaxHp, Damage }

    /// <summary>三选一奖励（占位版）：拿走一个，其余消失。之后会换成 AI 协议、武器、义体。</summary>
    public class Pickup : MonoBehaviour
    {
        public static readonly List<Pickup> All = new List<Pickup>();

        public RewardType type;
        public string Label => type switch
        {
            RewardType.FireRate => "射速 +20%",
            RewardType.Bounce => "反弹 +1",
            RewardType.MaxHp => "生命上限 +1",
            RewardType.Damage => "伤害 +25%",
            _ => ""
        };

        public static Pickup Spawn(Transform parent, Vector2 pos, RewardType t)
        {
            Color c = t switch
            {
                RewardType.FireRate => Sprites.Hex("05d9e8"),
                RewardType.Bounce => Sprites.Hex("ffb347"),
                RewardType.MaxHp => Sprites.Hex("e43b44"),
                _ => Sprites.Hex("b026ff"),
            };
            var sr = Sprites.Make(parent, "Pickup", Sprites.Box(18, 18), c, 0, pos);
            sr.transform.localRotation = Quaternion.Euler(0, 0, 45);
            sr.gameObject.AddComponent<Pulse>();
            var p = sr.gameObject.AddComponent<Pickup>();
            p.type = t;
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
                pl.ApplyReward(type);
                Effects.Burst(transform.position, GetComponent<SpriteRenderer>().color);
                GreyboxHUD.Message($"获得：{Label}", 2f);
                foreach (var p in All.ToArray()) Destroy(p.gameObject);
            }
        }
    }
}
