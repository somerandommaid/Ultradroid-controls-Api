
using System;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ULTRADROID.Core
{
    public sealed class ControlsPointerHandler : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerExitHandler
    {
        public Action Pressed;
        public Action Released;
        public Action Cancelled;

        private bool isPressed;

        public ControlsPointerHandler(IntPtr ptr) : base(ptr)
        {
        }

        public static void Register()
        {
            ClassInjector.RegisterTypeInIl2Cpp<ControlsPointerHandler>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (isPressed)
                return;

            isPressed = true;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            EndPress();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Keep the press active while the finger moves outside.
            // Pointer-up or component disable ends the press.
        }

        private void EndPress()
        {
            if (!isPressed)
                return;

            isPressed = false;
            Released?.Invoke();
        }

        private void OnDisable()
        {
            if (!isPressed)
                return;

            isPressed = false;
            Cancelled?.Invoke();
        }
    }
}
