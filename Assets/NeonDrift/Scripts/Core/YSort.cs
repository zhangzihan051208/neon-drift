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
