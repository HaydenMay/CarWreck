using UnityEngine;

namespace DamageLab.UI
{
    /// <summary>Keeps a bottom-anchored element horizontally centred in the viewport area left of the side panel.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class ViewportCenter : MonoBehaviour
    {
        public float rightInset = 470f;

        void LateUpdate() => Apply();

        public void Apply()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float width = parent.rect.width;
            var p = rt.anchoredPosition;
            float x = (width - rightInset) * 0.5f;
            if (!Mathf.Approximately(p.x, x)) rt.anchoredPosition = new Vector2(x, p.y);
        }
    }
}
