
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ULTRADROID.API;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace ULTRADROID.Core
{
    public sealed class ControlsRuntime : MonoBehaviour
    {
        private ControlsRuntimeLogic logic;

        public ControlsRuntime(IntPtr ptr) : base(ptr)
        {
            logic = new ControlsRuntimeLogic();
        }

        public void Update()
        {
            try
            {
                if (logic == null)
                    logic = new ControlsRuntimeLogic();

                logic.Tick();
            }
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] ControlsRuntime.Update failed: " + ex);
            }
        }
    }

    internal sealed class ControlsRuntimeLogic
    {
        private TouchManager activeManager;

        private readonly Dictionary<string, RuntimeButton> activeButtons =
            new Dictionary<string, RuntimeButton>(StringComparer.Ordinal);

        private string lastReportedError = string.Empty;

        private static Sprite fallbackDemoIcon;
        private static Texture2D fallbackDemoTexture;

        private static readonly List<UnityEngine.Object> keepAlive =
            new List<UnityEngine.Object>();

        internal void Tick()
        {
            try
            {
                TouchManager manager =
                    UnityEngine.Object.FindObjectOfType<TouchManager>();

                if (manager == null ||
                    manager.buttons == null ||
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
                string message =
                    ex.GetType().FullName + ": " + ex.Message;

                if (message != lastReportedError)
                {
                    ControlsRuntimeLog.Error(
                        "[ULTRADROID] Runtime logic error: " + ex);

                    lastReportedError = message;
                }
            }
        }

        private void ResetForManager(TouchManager manager)
        {
            foreach (RuntimeButton runtimeButton in activeButtons.Values)
            {
                if (runtimeButton.Registration?.Handle != null)
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
                if (registration?.Config == null)
                    continue;

                string id = registration.Config.Id;

                if (string.IsNullOrEmpty(id))
                    continue;

                if (registration.IsRemoved)
                {
                    RemoveRuntimeButton(manager, id);
                    ULTRADROIDControls.ForgetRemoved(registration);
                    continue;
                }

                if (activeButtons.TryGetValue(
                        id, out RuntimeButton current) &&
                    ReferenceEquals(current.Registration, registration))
                    continue;

                if (activeButtons.ContainsKey(id))
                    RemoveRuntimeButton(manager, id);

                RuntimeButton created = CreateButton(manager, registration);

                if (created != null)
                {
                    activeButtons[id] = created;
                    createdAny = true;
                }
            }

            if (createdAny)
            {
                ApplySavedLayout(manager);

                foreach (var rb in activeButtons.Values)
                {
                    RegisterBaseScale(manager, rb);
                    MaintainScale(manager, rb);
                }
            }
        }

        private RuntimeButton CreateButton(
            TouchManager manager,
            ButtonRegistration registration)
        {
            if (manager?.buttons == null ||
                manager.buttons.Count == 0)
                return null;

            OnScreenButton source = manager.buttons[0];

            if (source?.gameObject == null)
                return null;

            ButtonConfig config = registration.Config;

            if (config == null)
                return null;

            string gameObjectName = MakeGameObjectName(config.Id);

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

                if (clone == null)
                    return null;

                clone.name = gameObjectName;
                clone.SetActive(true);
            }

            OnScreenButton control =
                clone.GetComponent<OnScreenButton>();

            if (control == null)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            try
            {
                control.enabled = false;
                control.controlPathInternal = string.Empty;
            }
            catch { }

            RectTransform rect =
                clone.GetComponent<RectTransform>();

            if (newlyCloned && rect != null)
                rect.anchoredPosition += config.PositionOffset;

            ApplyVisuals(clone, config);

            BindClick(clone, registration);

            if (!ContainsButton(manager, control))
                manager.buttons.Add(control);

            Vector3 baseline =
                GetBaseScale(manager, source) *
                config.ScaleMultiplier;

            baseline = TouchManager.SafeScale(baseline);

            var runtimeButton =
                new RuntimeButton(registration, control, baseline);

            RegisterBaseScale(manager, runtimeButton);

            if (registration.Handle != null)
            {
                registration.Handle.GameObject = clone;
                registration.Handle.IsReady = true;
            }

            return runtimeButton;
        }

        private void ApplyVisuals(
            GameObject clone,
            ButtonConfig config)
        {
            if (clone == null || config == null)
                return;

            // 1. Load icon (IconPath is preferred)
            Sprite icon = null;

            if (!string.IsNullOrEmpty(config.IconPath))
                icon = LoadIconFromPath(config.IconPath);

            if (icon == null)
                icon = config.Icon; // legacy

            if (icon == null)
                icon = LoadFallbackIcon(config.Id); // demo only

            // 2. Tint
            if (config.ApplyTint)
            {
                foreach (var g in clone.GetComponentsInChildren<Graphic>(true))
                {
                    if (g != null)
                        g.color = config.Tint;
                }
            }

            // 3. Apply icon
            if (icon != null)
            {
                Image[] images =
                    clone.GetComponentsInChildren<Image>(true);

                bool addedRootImage = false;

                if (images.Length == 0)
                {
                    try
                    {
                        GameObject iconGo =
                            new GameObject("ULTRADROID_Icon");

                        iconGo.transform.SetParent(
                            clone.transform, false);

                        RectTransform rt =
                            iconGo.AddComponent<RectTransform>();

                        if (rt != null)
                        {
                            rt.anchorMin = Vector2.zero;
                            rt.anchorMax = Vector2.one;
                            rt.offsetMin = Vector2.zero;
                            rt.offsetMax = Vector2.zero;
                            rt.localScale = Vector3.one;
                            rt.localPosition = Vector3.zero;
                        }

                        Image rootImage =
                            iconGo.AddComponent<Image>();

                        if (rootImage == null)
                            rootImage = iconGo.GetComponent<Image>();

                        if (rootImage != null)
                        {
                            rootImage.color = Color.white;
                            rootImage.preserveAspect = true;
                            rootImage.raycastTarget = true;
                            rootImage.enabled = true;

                            images = new[] { rootImage };
                            addedRootImage = true;
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(iconGo);
                        }
                    }
                    catch { }
                }

                if (images.Length > 0 && images[0] != null)
                {
                    var image = images[0];

                    image.sprite = icon;
                    image.preserveAspect = true;
                    image.color = Color.white;
                    image.enabled = true;
                    image.gameObject.SetActive(true);

                    image.raycastTarget =
                        addedRootImage || image.raycastTarget;
                }
            }

            // 4. Hide original Fire graphics (only on the clone)
            HideOriginalVisuals(clone);

            // 5. Label
            if (!string.IsNullOrEmpty(config.Label))
            {
                var labels =
                    clone.GetComponentsInChildren<Text>(true);

                if (labels != null &&
                    labels.Length > 0 &&
                    labels[0] != null)
                {
                    labels[0].text = config.Label;
                }
            }
        }

        private void HideOriginalVisuals(GameObject clone)
        {
            if (clone == null)
                return;

            foreach (var img in clone.GetComponentsInChildren<Image>(true))
            {
                if (img == null ||
                    img.gameObject.name == "ULTRADROID_Icon")
                    continue;

                img.enabled = false;
                img.gameObject.SetActive(false);
            }

            foreach (var txt in clone.GetComponentsInChildren<Text>(true))
            {
                if (txt == null)
                    continue;

                txt.enabled = false;
                txt.gameObject.SetActive(false);
            }

            foreach (var g in clone.GetComponentsInChildren<Graphic>(true))
            {
                if (g == null ||
                    g.gameObject.name == "ULTRADROID_Icon")
                    continue;

                g.enabled = false;
            }
        }

        private Sprite LoadIconFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                string fullPath = Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(Paths.PluginPath, path);

                if (!File.Exists(fullPath))
                    return null;

                byte[] bytes = File.ReadAllBytes(fullPath);

                if (bytes == null || bytes.Length == 0)
                    return null;

                var texture =
                    new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!ImageConversion.LoadImage(
                        texture,
                        new Il2CppStructArray<byte>(bytes),
                        false))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                var sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);

                if (sprite == null)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                keepAlive.Add(texture);
                keepAlive.Add(sprite);

                return sprite;
            }
            catch
            {
                return null;
            }
        }

        private Sprite LoadFallbackIcon(string id)
        {
            if (!string.Equals(
                    id,
                    "ULTRADROID.Example.Demo",
                    StringComparison.Ordinal))
                return null;

            if (fallbackDemoIcon != null)
                return fallbackDemoIcon;

            return LoadIconFromPath("ultradroid-demo-icon.png");
        }

        // NEW BindClick implementation replaces the original method.
        private void BindClick(
            GameObject clone,
            ButtonRegistration registration)
        {
            var buttons =
                clone.GetComponentsInChildren<Button>(true);

            if (buttons == null || buttons.Length == 0)
            {
                Button added = clone.GetComponent<Button>();

                if (added == null)
                    added = clone.AddComponent<Button>();

                Graphic target = clone.GetComponent<Graphic>();

                if (target == null)
                    target = clone.GetComponentInChildren<Graphic>(true);

                if (target != null)
                    added.targetGraphic = target;

                // Call the listener directly on the single button we just created
                added.onClick.RemoveAllListeners();

                Action click = () =>
                {
                    try
                    {
                        registration.Config?.OnClick?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ControlsRuntimeLog.Error(
                            "[ULTRADROID] Button callback failed: " + ex);
                    }
                };

                var unityAction =
                    Il2CppInterop.Runtime.DelegateSupport
                        .ConvertDelegate<UnityAction>(click);

                added.onClick.AddListener(unityAction);
                return;
            }

            // Normal path – buttons already exist
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];

                if (button == null)
                    continue;

                button.onClick.RemoveAllListeners();

                Action click = () =>
                {
                    try
                    {
                        registration.Config?.OnClick?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ControlsRuntimeLog.Error(
                            "[ULTRADROID] Button callback failed: " + ex);
                    }
                };

                var unityAction =
                    Il2CppInterop.Runtime.DelegateSupport
                        .ConvertDelegate<UnityAction>(click);

                button.onClick.AddListener(unityAction);
            }
        }

        private void ApplySavedLayout(TouchManager manager)
        {
            try
            {
                string key = TouchManager.CurrentLayoutKey;

                if (string.IsNullOrEmpty(key))
                    return;

                var saved = manager.LoadLayoutFromPrefs(key);

                if (saved != null)
                    manager.ApplyLayout(saved);
            }
            catch { }
        }

        private void MaintainScale(
            TouchManager manager,
            RuntimeButton rb)
        {
            if (manager == null ||
                rb?.Control?.gameObject == null)
                return;

            RegisterBaseScale(manager, rb);

            float factor = manager.currentButtonScale;

            if (float.IsNaN(factor) ||
                float.IsInfinity(factor) ||
                Mathf.Abs(factor) <= 0.001f)
                return;

            Vector3 target = rb.BaseScale * factor;

            if ((rb.Control.transform.localScale - target).sqrMagnitude >
                0.000001f)
            {
                rb.Control.transform.localScale = target;
            }
        }

        private void RegisterBaseScale(
            TouchManager manager,
            RuntimeButton rb)
        {
            if (manager?.baseScaleLookup == null ||
                rb?.Control?.gameObject == null)
                return;

            string key = rb.Control.gameObject.name;

            if (manager.baseScaleLookup.ContainsKey(key))
                manager.baseScaleLookup[key] = rb.BaseScale;
            else
                manager.baseScaleLookup.Add(key, rb.BaseScale);
        }

        private Vector3 GetBaseScale(
            TouchManager manager,
            OnScreenButton button)
        {
            if (button?.transform == null)
                return Vector3.one;

            string key = button.gameObject.name;

            if (manager?.baseScaleLookup != null &&
                manager.baseScaleLookup.ContainsKey(key))
            {
                Vector3 known = manager.baseScaleLookup[key];

                if (IsUsableScale(known))
                    return known;
            }

            Vector3 scale = button.transform.localScale;

            float factor =
                manager != null ? manager.currentButtonScale : 1f;

            if (!float.IsNaN(factor) &&
                !float.IsInfinity(factor) &&
                Mathf.Abs(factor) > 0.001f)
            {
                scale /= factor;
            }

            return TouchManager.SafeScale(scale);
        }

        private bool IsUsableScale(Vector3 s)
        {
            return !float.IsNaN(s.x) && !float.IsInfinity(s.x) &&
                   !float.IsNaN(s.y) && !float.IsInfinity(s.y) &&
                   !float.IsNaN(s.z) && !float.IsInfinity(s.z) &&
                   Mathf.Abs(s.x) > 0.0001f &&
                   Mathf.Abs(s.y) > 0.0001f &&
                   Mathf.Abs(s.z) > 0.0001f;
        }

        private void RemoveRuntimeButton(
            TouchManager manager,
            string id)
        {
            if (!activeButtons.TryGetValue(id, out var rb))
                return;

            activeButtons.Remove(id);

            var control = rb.Control;

            var clone =
                control != null ? control.gameObject : null;

            if (manager?.buttons != null && control != null)
            {
                for (int i = manager.buttons.Count - 1; i >= 0; i--)
                {
                    if (manager.buttons[i] == control)
                        manager.buttons.RemoveAt(i);
                }
            }

            if (manager?.baseScaleLookup != null &&
                clone != null &&
                manager.baseScaleLookup.ContainsKey(clone.name))
            {
                manager.baseScaleLookup.Remove(clone.name);
            }

            if (clone != null)
                UnityEngine.Object.Destroy(clone);

            if (rb.Registration?.Handle != null)
            {
                rb.Registration.Handle.IsReady = false;
                rb.Registration.Handle.GameObject = null;
            }
        }

        private bool ContainsButton(
            TouchManager manager,
            OnScreenButton target)
        {
            if (manager?.buttons == null || target == null)
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
                ButtonRegistration reg,
                OnScreenButton control,
                Vector3 baseScale)
            {
                Registration = reg;
                Control = control;
                BaseScale = baseScale;
            }

            internal ButtonRegistration Registration { get; }
            internal OnScreenButton Control { get; }
            internal Vector3 BaseScale { get; }
        }
    }

    internal static class ControlsRuntimeLog
    {
        internal static ManualLogSource Source;

        internal static void Error(string message)
        {
            if (Source != null)
                Source.LogError(message);
            else
                Debug.LogError(message);
        }
    }
}
