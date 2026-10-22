using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GridRun.Solo
{
    // Small uGUI builders shared by the solo menus and HUD (1920x1080 reference canvas).
    public static class SoloUi
    {
        static Font font;
        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot; rect.anchoredPosition = offset; rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rect = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            return rect;
        }

        public static Text Label(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset,
            Vector2 size, int pointSize, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null, bool bold = false)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font; label.fontSize = pointSize; label.text = value;
            label.color = color ?? UiSkin.Text; label.alignment = align;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        public static Button Button(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset,
            Vector2 size, UnityAction action, bool primary = false, int pointSize = 26)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            UiSkin.Button(button, image, primary);
            if (action != null) button.onClick.AddListener(action);
            var label = Label(name + " label", value, rect, Vector2.zero, Vector2.zero, Vector2.zero, size, pointSize, TextAnchor.MiddleCenter, null, true);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = new Vector2(-16, 0);
            label.rectTransform.anchoredPosition = Vector2.zero;
            return button;
        }

        public static Text Caption(Button button) => button.GetComponentInChildren<Text>();

        // Pressed-and-held control (steering, throttle, fire, on-foot movement). PointerDown also fires `down`.
        public static HoldControl Hold(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset,
            Vector2 size, UnityAction down = null, int pointSize = 26)
        {
            var button = Button(name, value, parent, anchor, pivot, offset, size, null, false, pointSize);
            var hold = button.gameObject.AddComponent<HoldControl>();
            if (down != null)
            {
                var trigger = button.gameObject.AddComponent<EventTrigger>();
                var pointer = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                pointer.callback.AddListener(_ => down());
                trigger.triggers.Add(pointer);
                var submit = new EventTrigger.Entry { eventID = EventTriggerType.Submit };
                submit.callback.AddListener(_ => down());
                trigger.triggers.Add(submit);
            }
            return hold;
        }

        public static RectTransform Panel(string name, Transform parent, Vector2 size)
        {
            var rect = Rect(name, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, size);
            UiSkin.Panel(rect.gameObject.AddComponent<Image>());
            return rect;
        }

        // Full-screen dim that also blocks touches to anything behind a modal.
        public static RectTransform Dim(string name, Transform parent, float alpha = .82f)
        {
            var rect = Stretch(name, parent);
            rect.gameObject.AddComponent<Image>().color = new Color(0, .004f, .01f, alpha);
            return rect;
        }

        public static Image Bar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, Color color)
        {
            var image = Rect(name, parent, anchor, pivot, offset, size).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }

        public static void Select(GameObject target)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
