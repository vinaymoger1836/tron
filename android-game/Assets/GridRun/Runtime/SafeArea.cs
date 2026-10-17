using UnityEngine;

namespace GridRun
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        private Rect previous;
        private Vector2Int size;
        private void Update()
        {
            Rect safe = Screen.safeArea;
            var current = new Vector2Int(Screen.width, Screen.height);
            if (current.x <= 0 || current.y <= 0 || (safe == previous && current == size)) return;
            previous = safe; size = current;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / current.x, safe.yMin / current.y);
            rect.anchorMax = new Vector2(safe.xMax / current.x, safe.yMax / current.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
