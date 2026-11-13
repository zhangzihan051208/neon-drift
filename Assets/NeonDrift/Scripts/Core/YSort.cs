using UnityEngine;
using UnityEngine.Rendering;

namespace NeonDrift
{
    /// <summary>俯视角的前后遮挡：脚越靠下，画得越靠前。</summary>
    [RequireComponent(typeof(SortingGroup))]
    public class YSort : MonoBehaviour
    {
        SortingGroup group;
        public bool isStatic;

        void Awake() { group = GetComponent<SortingGroup>(); Apply(); }
        void LateUpdate() { if (!isStatic) Apply(); }
        void Apply() => group.sortingOrder = 1000 - Mathf.RoundToInt(transform.position.y * 20f);
    }
}

namespace NeonDrift
{
    /// <summary>
    /// 有占地的物件（车、掩体、电话亭、杂物）和角色之间的前后关系。
    /// 只按物件最前面那个角排序的话，车这种又宽又长的东西，人站在它侧面时脚比那个角靠上，
    /// 就会被判成“在车后面”而被车盖住。这里在人靠近时按占地来判断：
    /// 人在占地前面那两条边的外侧 → 人在前；在后面那两条边的外侧 → 人在后。
    /// </summary>
    [RequireComponent(typeof(SortingGroup))]
    public class IsoBlock : MonoBehaviour
    {
        public float x0, y0, x1, y1;          // 占地（格子坐标），x1、y1 是靠镜头的一侧
        SortingGroup group;
        int baseOrder;

        void Start() { group = GetComponent<SortingGroup>(); baseOrder = group.sortingOrder; }

        void LateUpdate()
        {
            var pl = GameRoot.I != null ? GameRoot.I.Player : null;
            if (pl == null || group == null) return;
            Vector2 wp = pl.transform.position;
            if ((wp - (Vector2)transform.position).sqrMagnitude > 64f) { group.sortingOrder = baseOrder; return; }
            int po = 1000 - Mathf.RoundToInt(wp.y * 20f);              // 和 YSort 同一个公式
            Vector2 t = Iso.Tile(wp);
            const float e = 0.05f;
            if (t.x > x1 - e || t.y > y1 - e) group.sortingOrder = po - 1;          // 人在前
            else if (t.x < x0 + e || t.y < y0 + e) group.sortingOrder = po + 1;     // 人在后
            else group.sortingOrder = baseOrder;                                   // 穿过不挡路的杂物时，照旧
        }
    }
}
