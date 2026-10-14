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
                ControlsRuntimeLog.Error("[ULTRADROID] ControlsRuntime.Update failed: " + ex);
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
        private static readonly List<UnityEngine.Object> keepAlive = new List<UnityEngine.Object>();

        internal void Tick()
        {
            try
            {
                TouchManager manager = UnityEngine.Object.FindObjectOfType<TouchManager>();
                if (manager == null || manager.buttons == null || manager.buttons.Count == 0)
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
                    ControlsRuntimeLog.Error("[ULTRADROID] Runtime logic error: " + ex);
                    lastReportedError = message;
                }
            }
        }

        private void ResetForManager(TouchManager manager)
        {
            foreach (RuntimeButton runtimeButton in activeButtons.Values)
            {
                ButtonRegistration registration = runtimeButton.Registration;
                if (registration != null && registration.Handle != null)
                {
                    registration.Handle.IsReady = false;
                    registration.Handle.GameObject = null;
                }
            }

            activeButtons.Clear();
            activeManager = manager;
        }

        private void SynchronizeButtons(TouchManager manager)
        {
            ButtonRegistration[] registrations = ULTRADROIDControls.GetRegistrations();
            bool createdAny = false;

            foreach (ButtonRegistration registration in registrations)
            {
                if (registration == null || registration.Config == null) continue;

                string id = registration.Config.Id;
                if (string.IsNullOrEmpty(id)) continue;

                if (registration.IsRemoved)
                {
                    RemoveRuntimeButton(manager, id);
                    ULTRADROIDControls.ForgetRemoved(registration);
                    continue;
                }

                if (activeButtons.TryGetValue(id, out RuntimeButton current) &&
                    ReferenceEquals(current.Registration, registration))
                {
                    continue;
                }

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
                foreach (RuntimeButton runtimeButton in activeButtons.Values)
                {
                    RegisterBaseScale(manager, runtimeButton);
                    MaintainScale(manager, runtimeButton);
                }
            }
        }

        private RuntimeButton CreateButton(TouchManager manager, ButtonRegistration registration)
        {
            if (manager == null || manager.buttons == null || manager.buttons.Count == 0)
                return null;

            OnScreenButton source = manager.buttons[0];
            if (source == null || source.gameObject == null)
                return null;

            ButtonConfig config = registration.Config;
            if (config == null) return null;

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
                clone = UnityEngine.Object.Instantiate(source.gameObject, parent);
                if (clone == null) return null;

                clone.name = gameObjectName;
                clone.SetActive(true);
            }

            OnScreenButton control = clone.GetComponent<OnScreenButton>();
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

            RectTransform rect = clone.GetComponent<RectTransform>();
            if (newlyCloned && rect != null)
                rect.anchoredPosition += config.PositionOffset;

            ApplyVisuals(clone, config);
            BindClick(clone, registration);

            if (!ContainsButton(manager, control))
                manager.buttons.Add(control);

            Vector3 baseline = GetBaseScale(manager, source) * config.ScaleMultiplier;
            baseline = TouchManager.SafeScale(baseline);

            RuntimeButton runtimeButton = new RuntimeButton(registration, control, baseline);
            RegisterBaseScale(manager, runtimeButton);

            if (registration.Handle != null)
            {
                registration.Handle.GameObject = clone;
                registration.Handle.IsReady = true;
            }

            return runtimeButton;
        }

        private void ApplyVisuals(GameObject clone, ButtonConfig config)
        {
            if (clone == null || config == null) return;

            Sprite icon = config.Icon;

            if (icon == null)
                icon = LoadFallbackIcon(config.Id);

            if (config.ApplyTint)
            {
                Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                    if (graphics[i] != null) graphics[i].color = config.Tint;
            }

            if (icon != null)
            {
                Image[] images = clone.GetComponentsInChildren<Image>(true);
                bool addedRootImage = false;

                if (images.Length == 0)
                {
                    try
                    {
                        GameObject iconGo = new GameObject("ULTRADROID_Icon");
                        iconGo.transform.SetParent(clone.transform, false);

                        RectTransform rt = iconGo.AddComponent<RectTransform>();
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
                        if (rootImage == null)
                            rootImage = iconGo.GetComponent<Image>();

                        if (rootImage != null)
                        {
                            rootImage.color = Color.white;
                            rootImage.preserveAspect = true;
                            rootImage.raycastTarget = true;
                            rootImage.enabled = true;

                            images = new Image[] { rootImage };
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
                    Image image = images[0];
                    image.sprite = icon;
                    image.preserveAspect = true;
                    image.color = Color.white;
                    image.enabled = true;
                    image.gameObject.SetActive(true);
                    image.raycastTarget = addedRootImage || image.raycastTarget;
                }
            }

            // Hide the original Fire button graphics that came with the clone
            HideOriginalVisuals(clone);

            if (!string.IsNullOrEmpty(config.Label))
            {
                Text[] labels = clone.GetComponentsInChildren<Text>(true);
                if (labels != null && labels.Length > 0 && labels[0] != null)
                    labels[0].text = config.Label;
            }
        }

        private void HideOriginalVisuals(GameObject clone)
        {
            if (clone == null) return;

            Image[] images = clone.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] == null) continue;
                if (images[i].gameObject.name == "ULTRADROID_Icon") continue;

                images[i].enabled = false;
                images[i].gameObject.SetActive(false);
            }

            Text[] texts = clone.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null) continue;
                texts[i].enabled = false;
                texts[i].gameObject.SetActive(false);
            }

            Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null) continue;
                if (graphics[i].gameObject.name == "ULTRADROID_Icon") continue;
                graphics[i].enabled = false;
            }
        }

        private Sprite LoadFallbackIcon(string id)
        {
            if (!string.Equals(id, "ULTRADROID.Example.Demo", StringComparison.Ordinal))
                return null;

            if (fallbackDemoIcon != null)
                return fallbackDemoIcon;

            string path = Path.Combine(Paths.PluginPath, "ultradroid-demo-icon.png");
            try
            {
                if (!File.Exists(path)) return null;

                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0) return null;

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool loaded = ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes), false);
                if (!loaded)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                if (sprite == null)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                fallbackDemoTexture = texture;
                fallbackDemoIcon = sprite;
                keepAlive.Add(texture);
                keepAlive.Add(sprite);

                return fallbackDemoIcon;
            }
            catch
            {
                return null;
            }
        }

        private void BindClick(GameObject clone, ButtonRegistration registration)
        {
            Button[] buttons = clone.GetComponentsInChildren<Button>(true);
            if (buttons == null || buttons.Length == 0)
            {
                Button addedButton = clone.GetComponent<Button>();
                if (addedButton == null)
                    addedButton = clone.AddComponent<Button>();

                Graphic target = clone.GetComponent<Graphic>();
                if (target == null)
                    target = clone.GetComponentInChildren<Graphic>(true);
                if (target != null)
                    addedButton.targetGraphic = target;

                buttons = new Button[] { addedButton };
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null) continue;

                button.onClick.RemoveAllListeners();
                Action click = () =>
                {
                    try
                    {
                        if (registration.Config != null && registration.Config.OnClick != null)
                            registration.Config.OnClick.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ControlsRuntimeLog.Error("[ULTRADROID] Button callback failed for " + registration.Config.Id + ": " + ex);
                    }
                };

                UnityAction unityAction = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityAction>(click);
                button.onClick.AddListener(unityAction);
            }
        }

        private void ApplySavedLayout(TouchManager manager)
        {
            try
            {
                string key = TouchManager.CurrentLayoutKey;
                if (string.IsNullOrEmpty(key)) return;

                TouchLayoutData saved = manager.LoadLayoutFromPrefs(key);
                if (saved != null)
                    manager.ApplyLayout(saved);
            }
            catch { }
        }

        private void MaintainScale(TouchManager manager, RuntimeButton runtimeButton)
        {
            if (manager == null || runtimeButton == null || runtimeButton.Control == null ||
                runtimeButton.Control.gameObject == null)
                return;

            RegisterBaseScale(manager, runtimeButton);
            float factor = manager.currentButtonScale;
            if (float.IsNaN(factor) || float.IsInfinity(factor) || Mathf.Abs(factor) <= 0.001f)
                return;

            Vector3 targetScale = runtimeButton.BaseScale * factor;
            Transform target = runtimeButton.Control.transform;
            if (target != null && (target.localScale - targetScale).sqrMagnitude > 0.000001f)
                target.localScale = targetScale;
        }

        private void RegisterBaseScale(TouchManager manager, RuntimeButton runtimeButton)
        {
            if (manager == null || runtimeButton == null || runtimeButton.Control == null ||
                runtimeButton.Control.gameObject == null || manager.baseScaleLookup == null)
                return;

            string key = runtimeButton.Control.gameObject.name;
            var lookup = manager.baseScaleLookup;
            if (lookup.ContainsKey(key))
                lookup[key] = runtimeButton.BaseScale;
            else
                lookup.Add(key, runtimeButton.BaseScale);
        }

        private Vector3 GetBaseScale(TouchManager manager, OnScreenButton button)
        {
            if (button == null || button.transform == null)
                return Vector3.one;

            string key = button.gameObject.name;
            if (manager != null && manager.baseScaleLookup != null && manager.baseScaleLookup.ContainsKey(key))
            {
                Vector3 known = manager.baseScaleLookup[key];
                if (IsUsableScale(known))
                    return known;
            }

            Vector3 currentScale = button.transform.localScale;
            float factor = manager != null ? manager.currentButtonScale : 1f;
            if (!float.IsNaN(factor) && !float.IsInfinity(factor) && Mathf.Abs(factor) > 0.001f)
                currentScale /= factor;

            return TouchManager.SafeScale(currentScale);
        }

        private bool IsUsableScale(Vector3 scale)
        {
            return !float.IsNaN(scale.x) && !float.IsInfinity(scale.x) &&
                   !float.IsNaN(scale.y) && !float.IsInfinity(scale.y) &&
                   !float.IsNaN(scale.z) && !float.IsInfinity(scale.z) &&
                   Mathf.Abs(scale.x) > 0.0001f && Mathf.Abs(scale.y) > 0.0001f &&
                   Mathf.Abs(scale.z) > 0.0001f;
        }

        private void RemoveRuntimeButton(TouchManager manager, string id)
        {
            if (!activeButtons.TryGetValue(id, out RuntimeButton runtimeButton))
                return;

            activeButtons.Remove(id);
            OnScreenButton control = runtimeButton.Control;
            GameObject clone = control != null ? control.gameObject : null;

            if (manager != null && manager.buttons != null && control != null)
            {
                for (int i = manager.buttons.Count - 1; i >= 0; i--)
                {
                    if (manager.buttons[i] == control)
                        manager.buttons.RemoveAt(i);
                }
            }

            if (manager != null && manager.baseScaleLookup != null && clone != null &&
                manager.baseScaleLookup.ContainsKey(clone.name))
            {
                manager.baseScaleLookup.Remove(clone.name);
            }

            if (clone != null)
                UnityEngine.Object.Destroy(clone);

            if (runtimeButton.Registration != null && runtimeButton.Registration.Handle != null)
            {
                runtimeButton.Registration.Handle.IsReady = false;
                runtimeButton.Registration.Handle.GameObject = null;
            }
        }

        private bool ContainsButton(TouchManager manager, OnScreenButton target)
        {
            if (manager == null || manager.buttons == null || target == null)
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
            string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(id));
            encoded = encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return "ULTRADROID_BUTTON_" + encoded;
        }

        private sealed class RuntimeButton
        {
            internal RuntimeButton(ButtonRegistration registration, OnScreenButton control, Vector3 baseScale)
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
            if (Source != null) Source.LogInfo(message);
            else Debug.Log(message);
        }

        internal static void Warning(string message)
        {
            if (Source != null) Source.LogWarning(message);
            else Debug.LogWarning(message);
        }

        internal static void Error(string message)
        {
            if (Source != null) Source.LogError(message);
            else Debug.LogError(message);
        }
    }
}
