using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GridRun
{
    public sealed class HoldControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private readonly HashSet<int> pointers = new HashSet<int>();
        public bool Held => pointers.Count != 0;
        public void OnPointerDown(PointerEventData data) => pointers.Add(data.pointerId);
        public void OnPointerUp(PointerEventData data) => pointers.Remove(data.pointerId);
        public void OnPointerExit(PointerEventData data) => pointers.Remove(data.pointerId);
        public void Clear() => pointers.Clear();
        private void OnDisable() => Clear();
    }
}
