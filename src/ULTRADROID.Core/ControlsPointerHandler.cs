
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

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            if (isPressed)
                return;

            isPressed = true;
            Pressed?.Invoke();
        }

        void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
        {
            EndPress();
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            // Keep the press active when the finger moves outside.
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
