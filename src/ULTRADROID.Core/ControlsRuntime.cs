
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
using TMPro;

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

        private static readonly List<UnityEngine.Object> keepAlive =
            new List<UnityEngine.Object>();

        internal void Tick()
        {
            try
            {
                TouchManager manager =
                    UnityEngine.Object.FindObjectOfType<TouchManager>();

                if (manager == null)
                {
                    if (activeButtons.Count > 0)
                        ResetForManager(null);
                    else
                        activeManager = null;

                    return;
                }

                if (activeManager != manager)
                    ResetForManager(manager);

                if (manager.buttons == null || manager.buttons.Count == 0)
                    return;

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
            TouchManager previousManager = activeManager;

            if (activeButtons.Count > 0)
            {
                string[] ids = new string[activeButtons.Count];
                activeButtons.Keys.CopyTo(ids, 0);

                foreach (string id in ids)
                    RemoveRuntimeButton(previousManager, id);
            }

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
                {
                    continue;
                }

                if (activeButtons.ContainsKey(id))
                    RemoveRuntimeButton(manager, id);

                RuntimeButton created =
                    CreateButton(manager, registration);

                if (created != null)
                {
                    activeButtons[id] = created;
                    createdAny = true;

                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] Created HUD button: " + id);
                }
            }

            if (createdAny)
            {
                ApplySavedLayout(manager);

                foreach (RuntimeButton rb in activeButtons.Values)
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
            {
                return null;
            }

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
            catch
            {
                // Some game versions expose this member differently.
            }

            RectTransform rect =
                clone.GetComponent<RectTransform>();

            if (newlyCloned && rect != null)
                rect.anchoredPosition += config.PositionOffset;

            ApplyVisuals(clone, config);

            LogVisibilityDiagnostics(clone);

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

            ControlsRuntimeLog.Info(
                "[ULTRADROID] HUD button ready: " + config.Id);

            return runtimeButton;
        }

        private void LogVisibilityDiagnostics(GameObject clone)
        {
            if (clone == null)
                return;

            try
            {
                RectTransform rt = clone.GetComponent<RectTransform>();
                Canvas canvas = clone.GetComponentInParent<Canvas>();

                ControlsRuntimeLog.Info(
                    "[W7 DEBUG] Root=" + clone.name +
                    " | activeSelf=" + clone.activeSelf +
                    " | activeInHierarchy=" + clone.activeInHierarchy);

                ControlsRuntimeLog.Info(
                    "[W7 DEBUG] Parent=" +
                    (clone.transform.parent != null
                        ? clone.transform.parent.name
                        : "NULL") +
                    " | localPosition=" + clone.transform.localPosition +
                    " | localScale=" + clone.transform.localScale);

                if (rt != null)
                {
                    ControlsRuntimeLog.Info(
                        "[W7 DEBUG] anchoredPosition=" +
                        rt.anchoredPosition +
                        " | rectSize=" + rt.rect.size +
                        " | anchorMin=" + rt.anchorMin +
                        " | anchorMax=" + rt.anchorMax);
                }
                else
                {
                    ControlsRuntimeLog.Info(
                        "[W7 DEBUG] WARNING: no RectTransform");
                }

                if (canvas != null)
                {
                    ControlsRuntimeLog.Info(
                        "[W7 DEBUG] Canvas=" + canvas.name +
                        " | enabled=" + canvas.enabled +
                        " | active=" + canvas.gameObject.activeInHierarchy +
                        " | renderMode=" + canvas.renderMode);
                }
                else
                {
                    ControlsRuntimeLog.Info(
                        "[W7 DEBUG] WARNING: no parent Canvas found");
                }

                foreach (Graphic graphic in
                         clone.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic == null)
                        continue;

                    ControlsRuntimeLog.Info(
                        "[W7 DEBUG] Graphic=" +
                        graphic.GetType().FullName +
                        " | object=" + graphic.gameObject.name +
                        " | enabled=" + graphic.enabled +
                        " | active=" + graphic.gameObject.activeInHierarchy +
                        " | color=" + graphic.color);
                }

                foreach (TMP_Text label in
                         clone.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label == null)
                        continue;

                    ControlsRuntimeLog.Info(
                        "[W7 DEBUG] TMP=" + label.gameObject.name +
                        " | text=" + label.text +
                        " | enabled=" + label.enabled +
                        " | active=" + label.gameObject.activeInHierarchy);
                }

                foreach (Text label in
                         clone.GetComponentsInChildren<Text>(true))
                {
                    if (label == null)
                        continue;

                    ControlsRuntimeLog.Info(
                        "[W7 DEBUG] LegacyText=" + label.gameObject.name +
                        " | text=" + label.text +
                        " | enabled=" + label.enabled +
                        " | active=" + label.gameObject.activeInHierarchy);
                }
            }
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[W7 DEBUG] Inspection failed: " + ex);
            }
        }

        private void ApplyVisuals(
            GameObject clone,
            ButtonConfig config)
        {
            if (clone == null || config == null)
                return;

            // Preserve the existing visuals. Hiding them made the
            // custom button invisible during the previous test.
            HideOriginalVisuals(clone);

            Sprite icon = null;

            if (!string.IsNullOrEmpty(config.IconPath))
                icon = LoadIconFromPath(config.IconPath);

            if (icon == null)
                icon = config.Icon;

            if (icon == null)
                icon = LoadFallbackIcon(config.Id);

            if (config.ApplyTint)
            {
                foreach (Graphic graphic in
                         clone.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic != null)
                        graphic.color = config.Tint;
                }
            }

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

                        iconGo.transform.SetParent(clone.transform, false);

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

                        Image rootImage = iconGo.AddComponent<Image>();

                        if (rootImage != null)
                        {
                            rootImage.preserveAspect = true;
                            rootImage.raycastTarget = false;
                            rootImage.enabled = true;

                            images = new[] { rootImage };
                            addedRootImage = true;
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(iconGo);
                        }
                    }
                    catch (Exception ex)
                    {
                        ControlsRuntimeLog.Error(
                            "[ULTRADROID] Failed to create icon object: " +
                            ex.Message);
                    }
                }

                if (images.Length > 0 && images[0] != null)
                {
                    Image image = images[0];

                    image.sprite = icon;
                    image.preserveAspect = true;
                    image.color = config.ApplyTint
                        ? config.Tint
                        : Color.white;

                    image.enabled = true;
                    image.gameObject.SetActive(true);

                    if (addedRootImage)
                        image.raycastTarget = false;
                }
            }

            if (!string.IsNullOrEmpty(config.Label))
            {
                bool foundLabel = false;

                foreach (Text label in
                         clone.GetComponentsInChildren<Text>(true))
                {
                    if (label == null)
                        continue;

                    label.text = config.Label;
                    label.enabled = true;
                    label.gameObject.SetActive(true);
                    label.color = config.ApplyTint
                        ? config.Tint
                        : Color.white;
                    label.raycastTarget = false;

                    foundLabel = true;
                }

                foreach (TMP_Text label in
                         clone.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label == null)
                        continue;

                    label.text = config.Label;
                    label.enabled = true;
                    label.gameObject.SetActive(true);
                    label.color = config.ApplyTint
                        ? config.Tint
                        : Color.white;
                    label.raycastTarget = false;

                    foundLabel = true;
                }

                // The cloned Fire button may not contain a text component.
                // Create a separate label instead of replacing its visuals.
                if (!foundLabel)
                    CreateLegacyLabel(clone, config.Label, config);
            }

            clone.SetActive(true);
        }

        private void CreateLegacyLabel(
            GameObject clone,
            string labelText,
            ButtonConfig config)
        {
            if (clone == null || string.IsNullOrEmpty(labelText))
                return;

            GameObject labelObject = null;

            try
            {
                labelObject = new GameObject(
                    "ULTRADROID_Label",
                    typeof(RectTransform));

                labelObject.transform.SetParent(clone.transform, false);

                RectTransform rect =
                    labelObject.GetComponent<RectTransform>();

                if (rect != null)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = Vector2.zero;
                    rect.localScale = Vector3.one;
                    rect.localRotation = Quaternion.identity;
                }

                Text label = labelObject.AddComponent<Text>();

                if (label == null)
                {
                    ControlsRuntimeLog.Error(
                        "[ULTRADROID] Could not create UI Text for label: " +
                        labelText);
                    UnityEngine.Object.Destroy(labelObject);
                    return;
                }

                Font builtinFont =
                    Resources.GetBuiltinResource<Font>("Arial.ttf");

                if (builtinFont == null)
                {
                    ControlsRuntimeLog.Error(
                        "[ULTRADROID] Built-in Arial font was unavailable.");
                    UnityEngine.Object.Destroy(labelObject);
                    return;
                }

                label.font = builtinFont;
                label.text = labelText;
                label.fontSize = 18;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 24;
                label.color = config.ApplyTint
                    ? config.Tint
                    : Color.white;
                label.raycastTarget = false;
                label.supportRichText = false;
                label.enabled = true;

                labelObject.SetActive(true);

                ControlsRuntimeLog.Info(
                    "[ULTRADROID] Created label '" + labelText +
                    "' on " + clone.name);
            }
            catch (Exception ex)
            {
                if (labelObject != null)
                    UnityEngine.Object.Destroy(labelObject);

                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Failed to create label '" +
                    labelText + "': " + ex);
            }
        }

        private void HideOriginalVisuals(GameObject clone)
        {
            if (clone == null)
                return;

            // Intentionally preserve the original cloned visuals.
            foreach (Image image in
                     clone.GetComponentsInChildren<Image>(true))
            {
                if (image == null)
                    continue;

                image.enabled = true;
                image.gameObject.SetActive(true);
            }

            foreach (Text text in
                     clone.GetComponentsInChildren<Text>(true))
            {
                if (text == null)
                    continue;

                text.enabled = true;
                text.gameObject.SetActive(true);
            }

            foreach (TMP_Text text in
                     clone.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null)
                    continue;

                text.enabled = true;
                text.gameObject.SetActive(true);
            }

            clone.SetActive(true);
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

                Sprite sprite = Sprite.Create(
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
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Failed to load icon '" +
                    path + "': " + ex.Message);

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

            fallbackDemoIcon = LoadIconFromPath(
                "ExampleMod/ultradroid-demo-icon.png");

            return fallbackDemoIcon;
        }

        private void BindClick(
            GameObject clone,
            ButtonRegistration registration)
        {
            Button[] buttons =
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

                UnityAction unityAction =
                    Il2CppInterop.Runtime.DelegateSupport
                        .ConvertDelegate<UnityAction>(click);

                added.onClick.AddListener(unityAction);
                return;
            }

            foreach (Button button in buttons)
            {
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

                UnityAction unityAction =
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
            catch
            {
                // Layout restoration is best-effort.
            }
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
                rb.Control.transform.localScale = target;
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
                scale /= factor;

            return TouchManager.SafeScale(scale);
        }

        private bool IsUsableScale(Vector3 scale)
        {
            return !float.IsNaN(scale.x) &&
                   !float.IsInfinity(scale.x) &&
                   !float.IsNaN(scale.y) &&
                   !float.IsInfinity(scale.y) &&
                   !float.IsNaN(scale.z) &&
                   !float.IsInfinity(scale.z) &&
                   Mathf.Abs(scale.x) > 0.0001f &&
                   Mathf.Abs(scale.y) > 0.0001f &&
                   Mathf.Abs(scale.z) > 0.0001f;
        }

        private void RemoveRuntimeButton(
            TouchManager manager,
            string id)
        {
            if (!activeButtons.TryGetValue(id, out RuntimeButton rb))
                return;

            activeButtons.Remove(id);

            OnScreenButton control = rb.Control;

            GameObject clone =
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
                manager.baseScaleLookup.Remove(clone.name);

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

    internal static class ControlsRuntimeLog
    {
        internal static ManualLogSource Source;

        internal static void Info(string message)
        {
            if (Source != null)
                Source.LogInfo(message);
            else
                Debug.Log(message);
        }

        internal static void Error(string message)
        {
            if (Source != null)
                Source.LogError(message);
            else
                Debug.LogError(message);
        }
    }
}
