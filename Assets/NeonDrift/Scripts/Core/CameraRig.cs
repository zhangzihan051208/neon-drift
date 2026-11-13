using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace NeonDrift
{
    /// <summary>960×540 像素完美摄像机：跟随玩家、略微偏向瞄准方向、震屏、限制在房间范围内。</summary>
    public class CameraRig : MonoBehaviour
    {
        public const int RefW = 960, RefH = 540;

        public static CameraRig I { get; private set; }

        [Tooltip("跟随平滑时间（秒）")] public float smooth = 0.12f;
        [Tooltip("镜头往瞄准方向偏移的比例")] public float aimLead = 0.18f;
        [Tooltip("偏移上限（单位）")] public float maxLead = 2.5f;

        Camera cam;
        Transform target;
        Rect bounds;
        Vector3 vel;
        float shake;
        Vector3 smoothPos;      // 没取整的镜头位置；平滑跟随用它算，显示时再对齐到像素格

        public static void Setup()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.allowHDR = true;                 // 发光层亮度超过 1，泛光靠它分辨
            cam.orthographicSize = RefH / 2f / Sprites.PPU;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;      // 没有贴图的地方一律是黑色（以后换成水面背景）
            // 前后顺序相同的东西按画面高低排（越靠上越靠后）。默认按离镜头远近排，
            // 2D 里大家一样远，谁在前每帧都可能变，护栏、桩子就会忽明忽暗
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);

            var ppc = cam.GetComponent<PixelPerfectCamera>();
            if (ppc == null) ppc = cam.gameObject.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = Sprites.PPU;
            ppc.refResolutionX = RefW;
            ppc.refResolutionY = RefH;
            ppc.gridSnapping = PixelPerfectCamera.GridSnapping.None;
            ppc.cropFrame = PixelPerfectCamera.CropFrame.None;

            I = cam.GetComponent<CameraRig>();
            if (I == null) I = cam.gameObject.AddComponent<CameraRig>();
            I.cam = cam;
        }

        public void SetTarget(Transform t, Rect roomBounds)
        {
            target = t;
            bounds = roomBounds;
            var p = Clamp(t.position);
            transform.position = new Vector3(p.x, p.y, -10f);
            vel = Vector3.zero;
            smoothPos = transform.position;
        }

        public static void Shake(float amount)
        {
            if (I != null) I.shake = Mathf.Max(I.shake, amount);
        }

        void LateUpdate()
        {
            if (target == null) return;
            Vector2 goal = target.position;
            var pc = target.GetComponent<PlayerController>();
            if (pc != null) goal += Vector2.up * 0.9f + Vector2.ClampMagnitude(pc.AimPoint - (Vector2)target.position, maxLead / aimLead) * aimLead;      // 镜头对准人物腰部而不是脚底
            goal = Clamp(goal);
            smoothPos = Vector3.SmoothDamp(smoothPos, new Vector3(goal.x, goal.y, -10f), ref vel, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
            var p = smoothPos;
            if (shake > 0.001f)
            {
                p += (Vector3)(Random.insideUnitCircle * shake);
                shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 2.5f);
            }
            // 对齐到像素格：镜头停在半个像素上时，护栏这类 3–4 像素的细线会忽粗忽细地闪
            float u = 1f / Sprites.PPU;
            if (shake <= 0.001f) p = new Vector3(Mathf.Round(p.x / u) * u, Mathf.Round(p.y / u) * u, p.z);
            transform.position = p;
        }

        Vector2 Clamp(Vector2 p)
        {
            float hw = RefW / 2f / Sprites.PPU, hh = RefH / 2f / Sprites.PPU;
            float x = bounds.width <= hw * 2 ? bounds.center.x : Mathf.Clamp(p.x, bounds.xMin + hw, bounds.xMax - hw);
            float y = bounds.height <= hh * 2 ? bounds.center.y : Mathf.Clamp(p.y, bounds.yMin + hh, bounds.yMax - hh);
            return new Vector2(x, y);
        }

        public Vector2 MouseWorld()
        {
            var m = Mouse.current;
            if (m == null || cam == null) return transform.position;
            Vector3 sp = m.position.ReadValue();
            sp.z = -cam.transform.position.z;
            return cam.ScreenToWorldPoint(sp);
        }
    }
}
