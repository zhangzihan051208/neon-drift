using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>敌人基类：生成外观、受击击退、死亡、通知房间。</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        protected Rigidbody2D rb;
        protected Health hp;
        protected SpriteRenderer body;
        protected float stun;
        protected Color baseColor;

        protected PlayerController Player => GameRoot.I != null ? GameRoot.I.Player : null;
        protected Vector2 Pos => transform.position;
        protected Vector2 Center => (Vector2)transform.position + Vector2.up * 0.7f;

        public static T Spawn<T>(Vector2 pos) where T : Enemy
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.position = pos;
            go.AddComponent<Rigidbody2D>();
            var e = go.AddComponent<T>();
            e.Build();
            return e;
        }

        protected abstract float MaxHp { get; }
        protected abstract Color BodyColor { get; }
        protected virtual Vector2Int BodyPixels => new Vector2Int(24, 44);

        void Build()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearDamping = 0f;

            var feet = gameObject.AddComponent<CircleCollider2D>();
            feet.radius = 0.32f;
            feet.offset = new Vector2(0f, 0.25f);
            var hurt = gameObject.AddComponent<BoxCollider2D>();
            hurt.isTrigger = true;
            float h = BodyPixels.y / (float)Sprites.PPU;
            hurt.size = new Vector2(BodyPixels.x / (float)Sprites.PPU, h);
            hurt.offset = new Vector2(0f, h / 2f);

            baseColor = BodyColor;
            body = Sprites.Make(transform, "Body", Sprites.Box(BodyPixels.x, BodyPixels.y, feet: true), baseColor, 0, Vector2.zero);
            Sprites.Make(transform, "Eyes", Sprites.Box(12, 3), Sprites.Hex("ff3b3b"), 1, new Vector2(0f, h - 0.3f));
            gameObject.AddComponent<YSort>();

            hp = gameObject.AddComponent<Health>();
            hp.Init(Team.Enemy, MaxHp);
            hp.Damaged += (amt, dir) => { stun = 0.12f; rb.linearVelocity = dir.normalized * 5f; };
            hp.Died += Die;
            OnBuilt();
        }

        protected virtual void OnBuilt() { }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Die()
        {
            Effects.Burst(Center, baseColor);
            CameraRig.Shake(0.12f);
            GameRoot.Hitstop(0.035f);
            GameRoot.I?.Room?.OnEnemyKilled(this);
            Destroy(gameObject);
        }

        protected virtual void FixedUpdate()
        {
            if (stun > 0f)
            {
                stun -= Time.fixedDeltaTime;
                rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 40f * Time.fixedDeltaTime);
                return;
            }
            var p = Player;
            if (p == null || p.IsDead) { rb.linearVelocity = Vector2.zero; return; }
            Think(p);
        }

        protected abstract void Think(PlayerController p);

        /// <summary>出招前的预警色（教玩家读招）。</summary>
        protected void Telegraph(bool on)
        {
            hp.SetBase(body, on ? Sprites.Hex("ffd23f") : baseColor);
        }

        public static Enemy Nearest(Vector2 from, float maxDist)
        {
            Enemy best = null;
            float bd = maxDist * maxDist;
            foreach (var e in All)
            {
                float d = ((Vector2)e.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }
    }
}
