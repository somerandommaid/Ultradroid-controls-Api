
using System;
using System.Collections.Generic;
using BepInEx;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Attributes;
using ULTRADROID.API;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace ULTRADROID.Core
{
    public sealed class ControlsRuntime : MonoBehaviour
    {
        private TouchManager activeManager;

        private readonly Dictionary<string, RuntimeButton> activeButtons =
            new Dictionary<string, RuntimeButton>(StringComparer.Ordinal);

        private string lastReportedError = string.Empty;

        public ControlsRuntime(IntPtr ptr) : base(ptr) { }

        public void Update()
        {
            try
            {
                TouchManager manager =
                    UnityEngine.Object.FindObjectOfType<TouchManager>();

                if (manager == null || manager.buttons == null ||
                    manager.buttons.Count == 0)
                    return;

                if (activeManager != manager)
                    ResetForManager(manager);

                SynchronizeButtons(manager);

                foreach (RuntimeButton runtimeButton in activeButtons.Values)
                    MaintainScale(manager, runtimeButton);

                lastReportedError = string.Empty;
            }
            catch (Exception ex)
            {
                string message = ex.GetType().FullName + ": " + ex.Message;

                if (message != lastReportedError)
                {
                    Debug.LogError("[ULTRADROID] Runtime error: " + ex);
                    lastReportedError = message;
                }
            }
        }

        private void ResetForManager(TouchManager manager)
        {
            foreach (RuntimeButton runtimeButton in activeButtons.Values)
            {
                if (runtimeButton.Registration != null &&
                    runtimeButton.Registration.Handle != null)
                {
                    runtimeButton.Registration.Handle.IsReady = false;
                    runtimeButton.Registration.Handle.GameObject = null;
                }
            }

            activeButtons.Clear();
            activeManager = manager;
        }

        private void SynchronizeButtons(TouchManager manager)
        {
            ButtonRegistration[] registrations =
                ULTRADROIDControls.GetRegistrations();

            bool createdAny = false;

            foreach (ButtonRegistration registration in registrations)
            {
                if (registration == null)
                    continue;

                if (registration.IsRemoved)
                {
                    RemoveRuntimeButton(manager, registration.Config.Id);
                    ULTRADROIDControls.ForgetRemoved(registration);
                    continue;
                }

                if (activeButtons.TryGetValue(
                        registration.Config.Id, out RuntimeButton current) &&
                    ReferenceEquals(current.Registration, registration))
                    continue;

                if (activeButtons.ContainsKey(registration.Config.Id))
                    RemoveRuntimeButton(manager, registration.Config.Id);

                RuntimeButton created = CreateButton(manager, registration);

                if (created != null)
                {
                    activeButtons[registration.Config.Id] = created;
                    createdAny = true;
                }
            }

            if (createdAny)
            {
                ApplySavedLayout(manager);

                foreach (RuntimeButton runtimeButton in activeButtons.Values)
                {
                    RegisterBaseScale(manager, runtimeButton);
                    MaintainScale(manager, runtimeButton);
                }
            }
        }

        [HideFromIl2Cpp]
        private RuntimeButton CreateButton(
            TouchManager manager,
            ButtonRegistration registration)
        {
            OnScreenButton source = manager.buttons[0];

            if (source == null || source.gameObject == null)
            {
                Debug.LogWarning(
                    "[ULTRADROID] No usable native button was found yet.");
                return null;
            }

            string gameObjectName =
                MakeGameObjectName(registration.Config.Id);

            Transform parent = source.transform.parent;
            GameObject clone = null;

            if (parent != null)
            {
                Transform existing = parent.Find(gameObjectName);

                if (existing != null)
                    clone = existing.gameObject;
            }

            bool newlyCloned = clone == null;

            if (newlyCloned)
            {
                clone = UnityEngine.Object.Instantiate(
                    source.gameObject, parent);

                clone.name = gameObjectName;
                clone.SetActive(true);
            }

            OnScreenButton control =
                clone.GetComponent<OnScreenButton>();

            if (control == null)
            {
                Debug.LogError(
                    "[ULTRADROID] Native clone has no OnScreenButton; ID=" +
                    registration.Config.Id);

                UnityEngine.Object.Destroy(clone);
                return null;
            }

            // Prevent the cloned native button from sending its original input.
            try
            {
                control.controlPathInternal = "";
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[ULTRADROID] Couldn't clear control path for " +
                    registration.Config.Id + ": " + ex.Message);
            }

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (newlyCloned && rect != null)
                rect.anchoredPosition +=
                    registration.Config.PositionOffset;

            ApplyVisuals(clone, registration.Config);
            BindClick(clone, registration);

            if (!ContainsButton(manager, control))
                manager.buttons.Add(control);

            Vector3 baseline =
                GetBaseScale(manager, source) *
                registration.Config.ScaleMultiplier;

            baseline = TouchManager.SafeScale(baseline);

            var runtimeButton =
                new RuntimeButton(registration, control, baseline);

            RegisterBaseScale(manager, runtimeButton);

            registration.Handle.GameObject = clone;
            registration.Handle.IsReady = true;

            Debug.Log(
                "[ULTRADROID] Registered custom button: " +
                registration.Config.Id);

            return runtimeButton;
        }

        [HideFromIl2Cpp]
        private void ApplyVisuals(GameObject clone, ButtonConfig config)
        {
            if (config.ApplyTint)
            {
                Graphic[] graphics =
                    clone.GetComponentsInChildren<Graphic>(true);

                for (int i = 0; i < graphics.Length; i++)
                {
                    if (graphics[i] != null)
                        graphics[i].color = config.Tint;
                }
            }

            if (config.Icon != null)
            {
                Transform existingIcon =
                    clone.transform.Find("ULTRADROID_CustomIcon");

                GameObject iconObject;

                if (existingIcon != null)
                {
                    iconObject = existingIcon.gameObject;
                }
                else
                {
                    // CanvasRenderer is omitted because it is unavailable
                    // in this project's current build references.
                    iconObject = new GameObject(
                        "ULTRADROID_CustomIcon",
                        Il2CppType.Of<RectTransform>(),
                        Il2CppType.Of<Image>());

                    iconObject.transform.SetParent(clone.transform, false);
                }

                RectTransform iconRect =
                    iconObject.GetComponent<RectTransform>();

                if (iconRect != null)
                {
                    iconRect.anchorMin = new Vector2(0.2f, 0.2f);
                    iconRect.anchorMax = new Vector2(0.8f, 0.8f);
                    iconRect.offsetMin = Vector2.zero;
                    iconRect.offsetMax = Vector2.zero;
                    iconRect.localScale = Vector3.one;
                    iconRect.localRotation = Quaternion.identity;
                    iconRect.SetAsLastSibling();
                }

                Image iconImage = iconObject.GetComponent<Image>();

                if (iconImage != null)
                {
                    iconImage.sprite = config.Icon;
                    iconImage.preserveAspect = true;
                    iconImage.raycastTarget = false;
                    iconImage.color = Color.white;
                    iconImage.enabled = true;
                    iconObject.SetActive(true);

                    Debug.Log(
                        "[ULTRADROID] Icon assigned to " + config.Id);
                }
                else
                {
                    Debug.LogError(
                        "[ULTRADROID] Icon Image component missing for " +
                        config.Id);
                }
            }

            if (!string.IsNullOrEmpty(config.Label))
            {
                Text[] labels =
                    clone.GetComponentsInChildren<Text>(true);

                if (labels != null && labels.Length > 0 &&
                    labels[0] != null)
                    labels[0].text = config.Label;
            }
        }

        [HideFromIl2Cpp]
        private void BindClick(
            GameObject clone,
            ButtonRegistration registration)
        {
            Button[] buttons =
                clone.GetComponentsInChildren<Button>(true);

            if (buttons == null || buttons.Length == 0)
            {
                Button addedButton = clone.AddComponent<Button>();

                Graphic target = clone.GetComponent<Graphic>();

                if (target == null)
                    target = clone.GetComponentInChildren<Graphic>(true);

                if (target != null)
                    addedButton.targetGraphic = target;

                buttons = new Button[] { addedButton };
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                    continue;

                buttons[i].onClick.RemoveAllListeners();

                Action click = () =>
                {
                    try
                    {
                        if (registration.Config.OnClick != null)
                            registration.Config.OnClick.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            "[ULTRADROID] Button callback failed for " +
                            registration.Config.Id + ": " + ex);
                    }
                };

                UnityAction unityAction =
                    Il2CppInterop.Runtime.DelegateSupport
                        .ConvertDelegate<UnityAction>(click);

                buttons[i].onClick.AddListener(unityAction);
            }
        }

        private void ApplySavedLayout(TouchManager manager)
        {
            try
            {
                string key = TouchManager.CurrentLayoutKey;

                if (string.IsNullOrEmpty(key))
                    return;

                TouchLayoutData saved =
                    manager.LoadLayoutFromPrefs(key);

                if (saved != null)
                    manager.ApplyLayout(saved);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[ULTRADROID] Couldn't reapply saved layout: " +
                    ex.Message);
            }
        }

        [HideFromIl2Cpp]
        private void MaintainScale(
            TouchManager manager,
            RuntimeButton runtimeButton)
        {
            if (manager == null || runtimeButton == null ||
                runtimeButton.Control == null ||
                runtimeButton.Control.gameObject == null)
                return;

            RegisterBaseScale(manager, runtimeButton);

            float factor = manager.currentButtonScale;

            if (float.IsNaN(factor) || float.IsInfinity(factor) ||
                Mathf.Abs(factor) <= 0.001f)
                return;

            Vector3 targetScale = runtimeButton.BaseScale * factor;
            Transform target = runtimeButton.Control.transform;

            if (target != null &&
                (target.localScale - targetScale).sqrMagnitude > 0.000001f)
                target.localScale = targetScale;
        }

        [HideFromIl2Cpp]
        private void RegisterBaseScale(
            TouchManager manager,
            RuntimeButton runtimeButton)
        {
            if (manager == null || runtimeButton == null ||
                runtimeButton.Control == null ||
                runtimeButton.Control.gameObject == null ||
                manager.baseScaleLookup == null)
                return;

            string key = runtimeButton.Control.gameObject.name;
            var lookup = manager.baseScaleLookup;

            if (lookup.ContainsKey(key))
                lookup[key] = runtimeButton.BaseScale;
            else
                lookup.Add(key, runtimeButton.BaseScale);
        }

        private Vector3 GetBaseScale(
            TouchManager manager,
            OnScreenButton button)
        {
            if (button == null || button.transform == null)
                return Vector3.one;

            string key = button.gameObject.name;

            if (manager != null &&
                manager.baseScaleLookup != null &&
                manager.baseScaleLookup.ContainsKey(key))
            {
                Vector3 known = manager.baseScaleLookup[key];

                if (IsUsableScale(known))
                    return known;
            }

            Vector3 currentScale = button.transform.localScale;
            float factor = manager != null
                ? manager.currentButtonScale
                : 1f;

            if (!float.IsNaN(factor) && !float.IsInfinity(factor) &&
                Mathf.Abs(factor) > 0.001f)
                currentScale /= factor;

            return TouchManager.SafeScale(currentScale);
        }

        private bool IsUsableScale(Vector3 scale)
        {
            return !float.IsNaN(scale.x) && !float.IsInfinity(scale.x) &&
                   !float.IsNaN(scale.y) && !float.IsInfinity(scale.y) &&
                   !float.IsNaN(scale.z) && !float.IsInfinity(scale.z) &&
                   Mathf.Abs(scale.x) > 0.0001f &&
                   Mathf.Abs(scale.y) > 0.0001f &&
                   Mathf.Abs(scale.z) > 0.0001f;
        }

        private void RemoveRuntimeButton(TouchManager manager, string id)
        {
            if (!activeButtons.TryGetValue(
                    id, out RuntimeButton runtimeButton))
                return;

            activeButtons.Remove(id);

            OnScreenButton control = runtimeButton.Control;
            GameObject clone =
                control != null ? control.gameObject : null;

            if (manager != null && manager.buttons != null &&
                control != null)
            {
                for (int i = manager.buttons.Count - 1; i >= 0; i--)
                {
                    if (manager.buttons[i] == control)
                        manager.buttons.RemoveAt(i);
                }
            }

            if (manager != null && manager.baseScaleLookup != null &&
                clone != null &&
                manager.baseScaleLookup.ContainsKey(clone.name))
                manager.baseScaleLookup.Remove(clone.name);

            if (clone != null)
                UnityEngine.Object.Destroy(clone);

            if (runtimeButton.Registration != null &&
                runtimeButton.Registration.Handle != null)
            {
                runtimeButton.Registration.Handle.IsReady = false;
                runtimeButton.Registration.Handle.GameObject = null;
            }
        }

        private bool ContainsButton(
            TouchManager manager,
            OnScreenButton target)
        {
            if (manager == null || manager.buttons == null ||
                target == null)
                return false;

            for (int i = 0; i < manager.buttons.Count; i++)
            {
                if (manager.buttons[i] == target)
                    return true;
            }

            return false;
        }

        private string MakeGameObjectName(string id)
        {
            string encoded = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(id));

            encoded = encoded.TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            return "ULTRADROID_BUTTON_" + encoded;
        }

        private sealed class RuntimeButton
        {
            internal RuntimeButton(
                ButtonRegistration registration,
                OnScreenButton control,
                Vector3 baseScale)
            {
                Registration = registration;
                Control = control;
                BaseScale = baseScale;
            }

            internal ButtonRegistration Registration { get; }
            internal OnScreenButton Control { get; }
            internal Vector3 BaseScale { get; }
        }
    }
}
