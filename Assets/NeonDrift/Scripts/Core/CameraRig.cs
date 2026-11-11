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

        public static void Setup()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = RefH / 2f / Sprites.PPU;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sprites.Hex("0b0b14");

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
            if (pc != null) goal += Vector2.ClampMagnitude(pc.AimPoint - (Vector2)target.position, maxLead / aimLead) * aimLead;
            goal = Clamp(goal);
            var p = Vector3.SmoothDamp(transform.position, new Vector3(goal.x, goal.y, -10f), ref vel, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
            if (shake > 0.001f)
            {
                p += (Vector3)(Random.insideUnitCircle * shake);
                shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 2.5f);
            }
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
