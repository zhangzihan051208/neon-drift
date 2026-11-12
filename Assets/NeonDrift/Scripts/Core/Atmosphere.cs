using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonDrift
{
    /// <summary>
    /// 积水区的氛围：压暗的冷色环境光、点光源（窗、路灯、火桶、闸门）、雨、地面水花、泛光和暗角。
    /// 配色取自 images/关卡01/概念图_积水区_基准.png。
    /// </summary>
    public class Atmosphere : MonoBehaviour
    {
        static Atmosphere inst;

        public static void Setup(Camera cam)
        {
            if (inst != null || cam == null) return;
            var go = new GameObject("[Atmosphere]");
            inst = go.AddComponent<Atmosphere>();

            // 环境光：很暗的青蓝色，地面和墙只靠点光源照亮
            // 场景里已有 Global Light 2D 就直接调它（同一层不能有两个全局光）
            Light2D g = null;
            foreach (var l in Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None))
                if (l.lightType == Light2D.LightType.Global) { g = l; break; }
            if (g == null)
            {
                g = new GameObject("GlobalLight").AddComponent<Light2D>();
                g.transform.SetParent(go.transform, false);
                g.lightType = Light2D.LightType.Global;
            }
            g.color = Sprites.Hex("8e959c");     // 环境光：接近中性的灰，只带一点点冷，不再把整片地面染蓝
            g.intensity = 0.6f;       // 地面保持深色，路灯和火桶附近才亮起来

            Rain(cam);
            Splashes(cam);

            // 泛光 + 暗角
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = prof.Add<Bloom>(true);
            bloom.intensity.Override(1.3f);
            bloom.threshold.Override(1.0f);      // 只有发光层（亮度超过 1）会晕开，普通的亮色不会
            bloom.scatter.Override(0.72f);
            var vig = prof.Add<Vignette>(true);
            vig.intensity.Override(0.32f);
            vig.color.Override(Sprites.Hex("05100f"));
            var ca = prof.Add<ColorAdjustments>(true);
            ca.contrast.Override(12f);
            ca.colorFilter.Override(Sprites.Hex("e6f2f2"));
            vol.sharedProfile = prof;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        /// <summary>一个点光源。flicker = 火桶那样轻微闪烁。</summary>
        public static Light2D Lamp(Transform parent, Vector2 pos, Color color, float radius, float intensity, bool flicker = false)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightOuterRadius = radius;
            l.pointLightInnerRadius = radius * 0.1f;
            l.falloffIntensity = 0.7f;
            if (flicker) go.AddComponent<Flicker>().baseIntensity = intensity;
            return l;
        }

        /// <summary>火桶里会动的火：往上窜的火苗 + 零星火星。挂在火桶下面，跟着火桶一起排前后。</summary>
        public static void Fire(Transform parent, Vector2 pos)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);       // 火桶约 26 像素高，火苗跟着小一点
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.15f;                                  // 往上飘
            var em = ps.emission;
            em.rateOverTime = 45f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 12f;
            sh.radius = 0.13f;
            sh.rotation = new Vector3(-90f, 0f, 0f);                        // 锥口朝上
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Sprites.Hex("fff2a8"), 0f), new GradientColorKey(Sprites.Hex("ffb030"), 0.25f),
                              new GradientColorKey(Sprites.Hex("ff5a1e"), 0.6f), new GradientColorKey(Sprites.Hex("8a1e10"), 1f) },
                      new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.85f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = ps.noise;                                           // 火苗左右摆
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 2.5f;
            noise.scrollSpeed = 1.5f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = Glow.Mat();
            r.sortingOrder = 5;                                             // 在火桶的排序组里，画在桶上面

            // 火星：少量、更快、飞得更高
            var sgo = new GameObject("Sparks");
            sgo.transform.SetParent(go.transform, false);
            var sp = sgo.AddComponent<ParticleSystem>();
            sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = sp.main;
            sm.loop = true;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            sm.startColor = Sprites.Hex("ffd27a");
            sm.maxParticles = 20;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var sem = sp.emission;
            sem.rateOverTime = 5f;
            var ssh = sp.shape;
            ssh.shapeType = ParticleSystemShapeType.Cone;
            ssh.angle = 25f;
            ssh.radius = 0.12f;
            ssh.rotation = new Vector3(-90f, 0f, 0f);
            var sn = sp.noise;
            sn.enabled = true;
            sn.strength = 1.2f;
            sn.frequency = 1.5f;
            var sc = sp.colorOverLifetime;
            sc.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            sc.color = sg;
            var sr = sgo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = Glow.Mat();
            sr.sortingOrder = 6;
            ps.Play();
            sp.Play();
        }

        static void Rain(Camera cam)
        {
            float hh = cam.orthographicSize, hw = hh * 16f / 9f;
            var go = new GameObject("Rain");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(hw * 0.15f, hh + 1.5f, 10f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.0f;
            main.startSpeed = 0f;
            main.startSize = 0.025f;
            main.startColor = new Color(0.62f, 0.76f, 0.82f, 0.13f);
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 420f;              // 小雨：密度减半多
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(hw * 2.6f, 0.1f, 0.1f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-3f, -4f);
            vel.y = new ParticleSystem.MinMaxCurve(-24f, -28f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.016f;            // 雨丝短一点
            r.lengthScale = 1f;
            r.sharedMaterial = Sprites.Unlit;
            r.sortingOrder = 32000;
            ps.Play();
        }

        static void Splashes(Camera cam)
        {
            float hh = cam.orthographicSize, hw = hh * 16f / 9f;
            var go = new GameObject("Splashes");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 10f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.18f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new Color(0.7f, 0.82f, 0.88f, 0.4f);
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 110f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(hw * 2f, hh * 2f, 0.1f);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.6f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = Sprites.Unlit;
            r.sortingOrder = -29000;      // 在地面之上、角色和墙之下
            ps.Play();
        }
    }

    /// <summary>火桶的光：亮度随机轻微抖动。</summary>
    public class Flicker : MonoBehaviour
    {
        public float baseIntensity = 1f;
        Light2D l;
        float seed;

        void Awake() { l = GetComponent<Light2D>(); seed = Random.value * 100f; }

        void Update()
        {
            if (l == null) return;
            float n = Mathf.PerlinNoise(seed, Time.time * 7f);
            l.intensity = baseIntensity * (0.75f + n * 0.5f);
        }
    }
}
