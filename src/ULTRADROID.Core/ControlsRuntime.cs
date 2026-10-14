using System;
using System.Collections.Generic;
using BepInEx;
using ULTRADROID.API;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace ULTRADROID.Core
{
    /// <summary>
    /// IL2CPP-injected Unity component. Keep Unity-facing callbacks here and keep
    /// managed ULTRADROID types/method signatures in ControlsRuntimeLogic below.
    /// This avoids Il2CppInterop trying to expose helper methods to IL2CPP.
    /// </summary>
    public sealed class ControlsRuntime : MonoBehaviour
    {
        private ControlsRuntimeLogic logic;
        private int diagnosticFrames;

        public ControlsRuntime(IntPtr ptr) : base(ptr)
        {
            logic = new ControlsRuntimeLogic();
        }

        // Keep this injected component's callback signature IL2CPP-compatible.
        public void Update()
        {
            // Diagnostic only: log the first five calls without changing button logic.
            if (diagnosticFrames < 5)
            {
                diagnosticFrames++;
                Debug.Log("[ULTRADROID] ControlsRuntime Update: " + diagnosticFrames);
            }

            try
            {
                if (logic == null)
                    logic = new ControlsRuntimeLogic();

                // Keep the existing runtime tick so registration, visuals, and clicks remain active.
                logic.Tick();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ULTRADROID] ControlsRuntime.Update failed: " + ex);
            }
        }
    }

    /// <summary>
    /// Managed implementation for the Controls API. This class is not registered
    /// with ClassInjector, so its helper methods may use ULTRADROID managed types.
    /// </summary>
    internal sealed class ControlsRuntimeLogic
    {
        private TouchManager activeManager;
        private readonly Dictionary<string, RuntimeButton> activeButtons =
            new Dictionary<string, RuntimeButton>(StringComparer.Ordinal);
        private string lastReportedError = string.Empty;
        private int lastDiagnosticFrame = -300;
        private string lastManagerState = string.Empty;

        internal void Tick()
        {
            try
            {
                // Throttled heartbeat: proves the Unity Update callback is reaching this logic.
                if (Time.frameCount - lastDiagnosticFrame >= 300)
                {
                    Debug.Log("[ULTRADROID] ControlsRuntime.Update is running; frame=" + Time.frameCount);
                    lastDiagnosticFrame = Time.frameCount;
                }

                TouchManager manager = UnityEngine.Object.FindObjectOfType<TouchManager>();
                if (manager == null)
                {
                    ReportManagerState("TouchManager not found");
                    return;
                }

                if (manager.buttons == null)
                {
                    ReportManagerState("TouchManager found, but buttons list is null");
                    return;
                }

                if (manager.buttons.Count == 0)
                {
                    ReportManagerState("TouchManager found, but buttons list is empty");
                    return;
                }

                ReportManagerState("TouchManager found; native button count=" + manager.buttons.Count);

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
                    Debug.LogError("[ULTRADROID] Runtime logic error: " + ex);
                    lastReportedError = message;
                }
            }
        }

        private void ReportManagerState(string state)
        {
            if (state == lastManagerState)
                return;

            lastManagerState = state;
            Debug.Log("[ULTRADROID] Runtime state: " + state);
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
                if (registration == null || registration.Config == null)
                    continue;

                string id = registration.Config.Id;
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning("[ULTRADROID] Ignoring a button registration with an empty ID.");
                    continue;
                }

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
            {
                Debug.LogWarning("[ULTRADROID] Cannot create button: no native TouchManager button is available yet.");
                return null;
            }

            OnScreenButton source = manager.buttons[0];
            if (source == null || source.gameObject == null)
            {
                Debug.LogWarning("[ULTRADROID] No usable native button was found yet.");
                return null;
            }

            ButtonConfig config = registration.Config;
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
                if (clone == null)
                {
                    Debug.LogError("[ULTRADROID] Failed to clone native button for " + config.Id);
                    return null;
                }

                clone.name = gameObjectName;
                clone.SetActive(true);
            }

            OnScreenButton control = clone.GetComponent<OnScreenButton>();
            if (control == null)
            {
                Debug.LogError("[ULTRADROID] Native clone has no OnScreenButton component; ID=" + config.Id);
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            // The clone is only a visual/callback button: prevent it from invoking the
            // inherited gameplay action (usually Fire).
            try
            {
                control.enabled = false;
                control.controlPathInternal = string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ULTRADROID] Couldn't disable native input on " + config.Id + ": " + ex.Message);
            }

            RectTransform rect = clone.GetComponent<RectTransform>();
            if (newlyCloned && rect != null)
                rect.anchoredPosition += config.PositionOffset;

            Debug.Log("[ULTRADROID] Creating visual for " + config.Id + "; customIcon=" + (config.Icon != null));
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

            Debug.Log("[ULTRADROID] Registered custom button: " + config.Id);
            return runtimeButton;
        }

        private void ApplyVisuals(GameObject clone, ButtonConfig config)
        {
            Debug.Log("[ULTRADROID] ApplyVisuals started: " + config.Id + ", icon=" + (config.Icon != null));

            if (config.ApplyTint)
            {
                Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                {
                    if (graphics[i] != null)
                        graphics[i].color = config.Tint;
                }
            }

            if (config.Icon != null)
            {
                Image[] images = clone.GetComponentsInChildren<Image>(true);
                Debug.Log("[ULTRADROID] Found " + images.Length + " Image components on " + config.Id);

                for (int i = 0; i < images.Length; i++)
                {
                    Image candidate = images[i];
                    if (candidate == null)
                        continue;

                    string imagePath = candidate.gameObject.name;
                    Transform parent = candidate.transform.parent;
                    while (parent != null && parent != clone.transform)
                    {
                        imagePath = parent.name + "/" + imagePath;
                        parent = parent.parent;
                    }

                    Debug.Log("[ULTRADROID] IMAGE[" + i + "] path=" + imagePath +
                        ", sprite=" + (candidate.sprite != null ? candidate.sprite.name : "<null>") +
                        ", enabled=" + candidate.enabled + ", active=" + candidate.gameObject.activeInHierarchy);
                }

                // Preserve the current prototype behavior for now: set the first Image.
                // The diagnostics above will show the hierarchy if this isn't the icon image.
                if (images.Length > 0 && images[0] != null)
                {
                    Image image = images[0];
                    image.sprite = config.Icon;
                    image.preserveAspect = true;
                    image.color = Color.white;
                    image.enabled = true;
                    image.gameObject.SetActive(true);
                    image.raycastTarget = false;
                    Debug.Log("[ULTRADROID] Assigned custom sprite to " + config.Id + " on " + image.gameObject.name);
                }
                else
                {
                    Debug.LogWarning("[ULTRADROID] No Image component found for icon on " + config.Id);
                }
            }

            if (!string.IsNullOrEmpty(config.Label))
            {
                Text[] labels = clone.GetComponentsInChildren<Text>(true);
                if (labels != null && labels.Length > 0 && labels[0] != null)
                    labels[0].text = config.Label;
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
                if (button == null)
                    continue;

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
                        Debug.LogError("[ULTRADROID] Button callback failed for " + registration.Config.Id + ": " + ex);
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
                if (string.IsNullOrEmpty(key))
                    return;

                TouchLayoutData saved = manager.LoadLayoutFromPrefs(key);
                if (saved != null)
                    manager.ApplyLayout(saved);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ULTRADROID] Couldn't reapply the saved layout: " + ex.Message);
            }
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
            // Base64 URL-safe encoding makes the object name stable and avoids slashes in Transform.Find paths.
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
}
