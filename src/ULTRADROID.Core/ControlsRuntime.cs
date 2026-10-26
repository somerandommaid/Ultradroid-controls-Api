using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ULTRADROID.API;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Il2CppInterop.Runtime.Injection;
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
        private TouchManager inspectedManager;

        private bool editableControlsRebuildPending;
        private bool rebuildMethodErrorLogged;

        private static readonly MethodInfo InitEditableControlsMethod =
            typeof(TouchManager).GetMethod(
                "InitEditableControls",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

        private readonly Dictionary<string, RuntimeButton> activeButtons =
            new Dictionary<string, RuntimeButton>(StringComparer.Ordinal);

        private readonly HashSet<string> templateFailureLogged =
            new HashSet<string>(StringComparer.Ordinal);

        private string lastReportedError = string.Empty;

        private static Sprite fallbackDemoIcon;
        private static Sprite roundedBackgroundSprite;

        private static readonly List<UnityEngine.Object> keepAlive =
            new List<UnityEngine.Object>();
        
        private float ClampOpacity(float opacity)
        {
            if (float.IsNaN(opacity))
                return 1f;
            if (float.IsInfinity(opacity))
                return 1f;
            
            return Mathf.Clamp01(opacity);
        }
        
        private void SetGraphicOpacity(Graphic graphic, float opacity)
        {
            if (graphic == null)
                return;
            
            Color color = graphic.color;
            color.a *= ClampOpacity(opacity);
            graphic.color = color;
        }

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
                    {
                        activeManager = null;
                        inspectedManager = null;
                    }

                    return;
                }

                if (activeManager != manager)
                    ResetForManager(manager);

                if (manager.inGameButtons == null)
                    return;

                InspectInGameButtons(manager);
                SynchronizeButtons(manager);
                RebuildEditableControlsIfNeeded(manager);

                foreach (RuntimeButton runtimeButton in activeButtons.Values)
                {
                    UpdateCooldown(runtimeButton);
                    UpdateButtonEffects(runtimeButton);
                    ApplyVisibility(runtimeButton);
                    UpdateButtonInteraction(runtimeButton);
                    ApplyToggleVisuals(runtimeButton);
                }

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

        private void InspectInGameButtons(TouchManager manager)
        {
            if (manager == null || inspectedManager == manager)
                return;

            inspectedManager = manager;

            ControlsRuntimeLog.Info(
                "[ULTRADROID] Beginning one-time inGameButtons inspection. " +
                "Count = " + manager.inGameButtons.Count);

            foreach (GameObject item in manager.inGameButtons)
            {
                if (item == null)
                {
                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] inGameButtons contains a NULL entry.");
                    continue;
                }

                ControlsRuntimeLog.Info(
                    "[ULTRADROID] Template candidate: " + item.name);

                try
                {
                    Component[] components =
                        item.GetComponents<Component>();

                    foreach (Component component in components)
                    {
                        ControlsRuntimeLog.Info(
                            "[ULTRADROID] Component on " + item.name + ": " +
                            (component != null
                                ? component.GetType().FullName
                                : "<null>"));
                    }

                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] Candidate hierarchy: " +
                        GetHierarchyPath(item.transform));

                    OnScreenButton onScreenButton =
                        item.GetComponent<OnScreenButton>();

                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] Direct OnScreenButton on " +
                        item.name + ": " +
                        (onScreenButton != null ? "YES" : "NO"));

                    OnScreenButton childButton =
                        item.GetComponentInChildren<OnScreenButton>(true);

                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] Child OnScreenButton on " +
                        item.name + ": " +
                        (childButton != null ? "YES" : "NO"));

                    Button uiButton =
                        item.GetComponent<Button>();

                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] Direct Unity UI Button on " +
                        item.name + ": " +
                        (uiButton != null ? "YES" : "NO"));

                    foreach (Graphic graphic in
                             item.GetComponentsInChildren<Graphic>(true))
                    {
                        if (graphic == null)
                            continue;

                        ControlsRuntimeLog.Info(
                            "[ULTRADROID] Candidate graphic: " +
                            graphic.GetType().FullName +
                            " | object=" + graphic.gameObject.name +
                            " | enabled=" + graphic.enabled);
                    }
                }
                catch (Exception ex)
                {
                    ControlsRuntimeLog.Error(
                        "[ULTRADROID] Inspection failed for " +
                        item.name + ": " + ex);
                }
            }

            ControlsRuntimeLog.Info(
                "[ULTRADROID] Finished inGameButtons inspection.");
        }

        private string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            string path = transform.name;
            Transform parent = transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
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

            // Never carry pending editor rebuilds across managers.
            editableControlsRebuildPending = false;
            rebuildMethodErrorLogged = false;

            activeManager = manager;
            inspectedManager = null;
        }

        private void RebuildEditableControlsIfNeeded(TouchManager manager)
        {
            if (!editableControlsRebuildPending ||
                manager == null ||
                manager.IsEditMode)
                return;

            if (manager.editableControls == null)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Cannot rebuild editor: editableControls is null.");
                return;
            }

            if (InitEditableControlsMethod == null)
            {
                if (!rebuildMethodErrorLogged)
                {
                    ControlsRuntimeLog.Error(
                        "[ULTRADROID] TouchManager.InitEditableControls was not found.");

                    rebuildMethodErrorLogged = true;
                }

                return;
            }

            try
            {
                InitEditableControlsMethod.Invoke(manager, null);

                editableControlsRebuildPending = false;
                rebuildMethodErrorLogged = false;

                ControlsRuntimeLog.Info(
                    "[ULTRADROID] Touch editor state rebuilt successfully.");
            }
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Editor rebuild failed: " +
                    (ex.InnerException ?? ex));
            }
        }

        private void SynchronizeButtons(TouchManager manager)
        {
            ButtonRegistration[] registrations =
                ULTRADROIDControls.GetRegistrations();

            Array.Sort(
                registrations,
                (a, b) => string.Compare(
                    a?.Config?.Id ?? string.Empty,
                    b?.Config?.Id ?? string.Empty,
                    StringComparison.Ordinal));

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
                    ApplyVisibility(created);

                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] Created HUD button: " + id);
                }
            }

            // Runtime button additions/removals request a native editor-state rebuild.
        }

        private RuntimeButton CreateButton(
            TouchManager manager,
            ButtonRegistration registration)
        {
            string id = registration?.Config?.Id ?? "<unknown>";

            if (manager == null)
            {
                LogTemplateFailure(id, "TouchManager is null.");
                return null;
            }

            if (manager.inGameButtons == null ||
                manager.inGameButtons.Count == 0)
            {
                LogTemplateFailure(
                    id,
                    "inGameButtons is null or empty.");

                return null;
            }

            GameObject sourceObject = null;
            OnScreenButton source = null;

            // Search both the list entry and its descendants.
            // The game puts OnScreenButton on a child of "InGame".
            foreach (GameObject candidate in manager.inGameButtons)
            {
                if (candidate == null)
                    continue;

                OnScreenButton found =
                    candidate.GetComponent<OnScreenButton>();

                if (found == null)
                {
                    found = candidate.GetComponentInChildren
                        <OnScreenButton>(true);
                }

                if (found == null)
                    continue;

                source = found;
                sourceObject = found.gameObject;
                break;
            }

            if (sourceObject == null || source == null)
            {
                LogTemplateFailure(
                    id,
                    "No OnScreenButton found on or beneath inGameButtons. " +
                    "Count = " + manager.inGameButtons.Count);

                return null;
            }

            templateFailureLogged.Remove(id);

            ButtonConfig config = registration.Config;

            if (config == null)
                return null;

            string gameObjectName = MakeGameObjectName(config.Id);
            Transform parent = sourceObject.transform.parent;

            if (parent == null)
            {
                LogTemplateFailure(
                    id,
                    "The OnScreenButton template has no parent.");

                return null;
            }

            GameObject clone = null;
            Transform existing = parent.Find(gameObjectName);

            if (existing != null)
                clone = existing.gameObject;

            bool newlyCloned = clone == null;

            if (newlyCloned)
            {
                clone = UnityEngine.Object.Instantiate(
                    sourceObject,
                    parent,
                    false);

                if (clone == null)
                    return null;

                clone.name = gameObjectName;
            }

            OnScreenButton control =
                clone.GetComponent<OnScreenButton>();

            if (control == null)
            {
                if (newlyCloned)
                    UnityEngine.Object.Destroy(clone);

                LogTemplateFailure(
                    id,
                    "Cloned object has no OnScreenButton component.");

                return null;
            }

            try
            {
                control.enabled = false;
                control.controlPathInternal = string.Empty;
            }
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Could not disable cloned input binding for '" +
                    id + "': " + ex.Message);
            }

            RectTransform sourceRect =
                sourceObject.GetComponent<RectTransform>();

            RectTransform cloneRect =
                clone.GetComponent<RectTransform>();

            if (newlyCloned && sourceRect != null && cloneRect != null)
            {
                cloneRect.anchorMin = sourceRect.anchorMin;
                cloneRect.anchorMax = sourceRect.anchorMax;
                cloneRect.pivot = sourceRect.pivot;
                cloneRect.sizeDelta = sourceRect.sizeDelta;
                cloneRect.anchoredPosition =
                    sourceRect.anchoredPosition + config.PositionOffset;
                cloneRect.localRotation = sourceRect.localRotation;
            }
            
            if (sourceRect != null && cloneRect != null)
            {
                float width = config.Width > 0f &&
                !float.IsNaN(config.Width) &&
                !float.IsInfinity(config.Width)
                ? config.Width
                : sourceRect.sizeDelta.x;

                float height = config.Height > 0f &&
                !float.IsNaN(config.Height) &&
                !float.IsInfinity(config.Height)
                ? config.Height
                : sourceRect.sizeDelta.y;

                cloneRect.sizeDelta = new Vector2(width, height);
            }


            ApplyVisuals(clone, config);

            Transform iconTransform =
                clone.transform.Find("ULTRADROID_Icon");

            Image iconImage = iconTransform != null
                ? iconTransform.GetComponent<Image>()
                : null;

            Sprite defaultIcon = iconImage != null
                ? iconImage.sprite
                : null;

            LogVisibilityDiagnostics(clone);

            // Register only the cloned button, never the whole InGame root.
            if (!manager.inGameButtons.Contains(clone))
                manager.inGameButtons.Add(clone);

            RectTransform editableRect =
                clone.GetComponent<RectTransform>();

            if (editableRect == null)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Custom button has no RectTransform: " +
                    config.Id);
            }
            else if (manager.editableControls == null)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] TouchManager.editableControls is null.");
            }
            else
            {
                EditableTouchControl editableComponent =
                    clone.GetComponent<EditableTouchControl>();

                if (editableComponent == null)
                    editableComponent =
                        clone.AddComponent<EditableTouchControl>();

                if (editableComponent != null)
                {
                    editableComponent.Init(manager, editableRect);

                    if (!manager.editableControls.Contains(editableRect))
                    {
                        manager.editableControls.Add(editableRect);

                        // Keep custom controls in a deterministic order.
                        var customControls = new List<RectTransform>();

                        foreach (RectTransform item in manager.editableControls)
                        {
                            if (item != null &&
                                item.name.StartsWith(
                                    "ULTRADROID_BUTTON_",
                                    StringComparison.Ordinal))
                            {
                                customControls.Add(item);
                            }
                        }

                        customControls.Sort((a, b) =>
                            string.Compare(a.name, b.name, StringComparison.Ordinal));

                        int insertIndex = manager.editableControls.Count - customControls.Count;

                        foreach (RectTransform item in customControls)
                        {
                            manager.editableControls.Remove(item);
                            manager.editableControls.Insert(insertIndex++, item);
                        }
                    }

                    editableControlsRebuildPending = true;

                    ControlsRuntimeLog.Info(
                        "[ULTRADROID] Registered editable control: " +
                        config.Id);
                }
                else
                {
                    ControlsRuntimeLog.Error(
                        "[ULTRADROID] Could not attach EditableTouchControl: " +
                        config.Id);
                }
            }

            Vector3 baseline = sourceObject.transform.localScale;

            if (!IsUsableScale(baseline))
                baseline = Vector3.one;

            clone.transform.localScale =
                baseline * Mathf.Max(0.01f, config.ScaleMultiplier);
            
            // Apply custom button rotation in degrees.
            if (cloneRect != null)
            {
                cloneRect.localRotation =
                    Quaternion.Euler(0f, 0f, config.Rotation);
            }
            
            clone.SetActive(true);

            var runtimeButton =
                new RuntimeButton(registration, control);

            runtimeButton.DefaultIcon = defaultIcon;
            runtimeButton.BaseScale = runtimeButton.Control.transform.localScale;

            runtimeButton.ToggledOnIconCache =
                config.ToggledOnIcon ??
                LoadIconFromPath(config.ToggledOnIconPath);

            runtimeButton.ToggledOffIconCache =
                config.ToggledOffIcon ??
                LoadIconFromPath(config.ToggledOffIconPath);

            runtimeButton.CooldownWasActive = config.IsOnCooldown;
            runtimeButton.CooldownCompletionReported = !config.IsOnCooldown;
            runtimeButton.CooldownDurationAtStart =
                config.ActiveCooldownDuration;

            runtimeButton.LastCooldownRequestVersion =
                config.CooldownRequestVersion;

            runtimeButton.LastCooldownCancelVersion =
                config.CooldownCancelVersion;
            runtimeButton.LastEffectRequestVersion = config.EffectRequestVersion;
            runtimeButton.LastFlashRequestVersion = config.FlashRequestVersion;

            AttachPointerHandler(clone, runtimeButton, manager);
            BindClick(clone, registration, manager, runtimeButton);
            ApplyToggleVisuals(runtimeButton);

            if (registration.Handle != null)
            {
                registration.Handle.GameObject = clone;
                registration.Handle.IsReady = true;
            }

            ControlsRuntimeLog.Info(
                "[ULTRADROID] HUD button ready: " + config.Id +
                " | source=" + sourceObject.name +
                " | parent=" + parent.name);

            return runtimeButton;
        }

        private void LogTemplateFailure(string id, string reason)
        {
            if (templateFailureLogged.Add(id))
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Cannot create button '" +
                    id + "': " + reason);
            }
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

        private Sprite LoadRoundedBackgroundSprite()
        {
            if (roundedBackgroundSprite != null)
                return roundedBackgroundSprite;

            Texture2D texture = new Texture2D(
                64, 64, TextureFormat.RGBA32, false);

            Color32[] pixels = new Color32[64 * 64];

            const float radius = 12f;

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float px = x < 32 ? radius : 63f - radius;
                    float py = y < 32 ? radius : 63f - radius;

                    float dx = Mathf.Max(0f, Mathf.Abs(x - 31.5f) - (31.5f - radius));
                    float dy = Mathf.Max(0f, Mathf.Abs(y - 31.5f) - (31.5f - radius));

                    bool inside = dx * dx + dy * dy <= radius * radius;

                    pixels[y * 64 + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            roundedBackgroundSprite = Sprite.Create(
                texture,
                new Rect(0, 0, 64, 64),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(20f, 20f, 20f, 20f));

            keepAlive.Add(texture);
            keepAlive.Add(roundedBackgroundSprite);

            return roundedBackgroundSprite;
        }

        private float ClampCornerRadius(float radius)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius))
                return 12f;

            return Mathf.Clamp(radius, 0f, 31f);
        }

        private void ApplyBackground(
            GameObject clone,
            ButtonConfig config)
        {
            if (clone == null || config == null)
                return;

            Sprite background = config.Background;

            if (background == null &&
                !string.IsNullOrEmpty(config.BackgroundPath))
            {
                background = LoadIconFromPath(config.BackgroundPath);
            }

            // Only generate the white rounded sprite when no custom
            // background image was supplied.
            if (background == null && config.RoundedCorners)
                background = LoadRoundedBackgroundSprite();

            if (background == null)
                return;

            Transform existing =
                clone.transform.Find("ULTRADROID_Background");

            GameObject backgroundObject;

            if (existing != null)
            {
                backgroundObject = existing.gameObject;
            }
            else
            {
                backgroundObject =
                    new GameObject("ULTRADROID_Background");

                backgroundObject.transform.SetParent(
                    clone.transform, false);
            }

            RectTransform rect =
                backgroundObject.GetComponent<RectTransform>();

            if (rect == null)
                rect = backgroundObject.AddComponent<RectTransform>();

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition = Vector2.zero;

            Image image = backgroundObject.GetComponent<Image>();

            if (image == null)
                image = backgroundObject.AddComponent<Image>();

            image.sprite = background;
            image.type = config.RoundedCorners
                ? Image.Type.Sliced
                : Image.Type.Simple;
            image.preserveAspect = !config.RoundedCorners &&
                                   config.BackgroundPreserveAspect;
            image.color = new Color(
                1f,
                1f,
                1f,
                config.TransparentButton
                    ? 0f
                    : ClampOpacity(config.BackgroundOpacity));
            image.raycastTarget = false;
            image.enabled = true;

            backgroundObject.SetActive(true);
            backgroundObject.transform.SetAsFirstSibling();
        }

        private void ApplyTransparentButton(
            GameObject clone,
            ButtonConfig config)
        {
            if (clone == null || config == null)
                return;

            if (!config.TransparentButton)
                return;

            // Hide the original background without disabling the root Graphic.
            Graphic rootGraphic = clone.GetComponent<Graphic>();

            if (rootGraphic != null)
            {
                Color color = rootGraphic.color;
                color.a = 0f;
                rootGraphic.color = color;
            }

            // Hide the optional custom background.
            Transform background =
                clone.transform.Find("ULTRADROID_Background");

            if (background != null)
            {
                Graphic backgroundGraphic =
                    background.GetComponent<Graphic>();

                if (backgroundGraphic != null)
                {
                    Color color = backgroundGraphic.color;
                    color.a = 0f;
                    backgroundGraphic.color = color;
                }
            }
        }

        private void ApplyVisuals(
            GameObject clone,
            ButtonConfig config)
        {
            if (clone == null || config == null)
                return;

            HideOriginalVisuals(clone);

            foreach (Graphic graphic in
                     clone.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                    continue;

                string spriteInfo = "";

                Image image = graphic.TryCast<Image>();
                if (image != null)
                    spriteInfo = " | sprite=" +
                        (image.sprite != null ? image.sprite.name : "null");

                ControlsRuntimeLog.Info(
                    "[ULTRADROID BG DEBUG] object=" +
                    graphic.gameObject.name +
                    " | type=" + graphic.GetType().FullName +
                    " | enabled=" + graphic.enabled +
                    spriteInfo);
            }

            if (config.HideBackground || config.TransparentButton)
            {
                foreach (Graphic graphic in
                         clone.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic == null)
                        continue;

                    string objectName = graphic.gameObject.name;

                    // Keep the custom icon and optional custom background.
                    if (objectName == "ULTRADROID_Icon" ||
                        objectName == "ULTRADROID_Background")
                        continue;

                    if (graphic.gameObject == clone)
                    {
                        // Keep the root Graphic for touch/editor behavior.
                        // Its alpha is reset after tinting below.
                        continue;
                    }

                    // Hide original template graphics.
                    graphic.enabled = false;
                }
            }

            ApplyBackground(clone, config);
            ApplyTransparentButton(clone, config);

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
                    if (graphic != null &&
                        graphic.gameObject.name != "ULTRADROID_Background" &&
                        graphic.gameObject.name != "ULTRADROID_Label")
                    {
                        graphic.color = config.Tint;
                    }
                }
            }

            Graphic rootGraphic = clone.GetComponent<Graphic>();

            if (rootGraphic != null)
            {
                Color color = rootGraphic.color;

                if (config.HideBackground || config.TransparentButton)
                {
                    color.a = 0f;
                }
                else
                {
                    color.a *= ClampOpacity(config.BackgroundOpacity);
                }

                rootGraphic.color = color;
            }

            if (icon != null)
            {
                RectTransform buttonRect =
                    clone.GetComponent<RectTransform>();

                if (buttonRect == null)
                {
                    ControlsRuntimeLog.Error(
                        "[ULTRADROID] Cannot position icon: button has no RectTransform.");
                }
                else
                {
                    Transform existingIcon =
                        clone.transform.Find("ULTRADROID_Icon");

                    GameObject iconObject;

                    if (existingIcon != null)
                    {
                        iconObject = existingIcon.gameObject;
                    }
                    else
                    {
                        iconObject = new GameObject("ULTRADROID_Icon");
                        iconObject.transform.SetParent(clone.transform, false);
                    }

                    RectTransform iconRect =
                        iconObject.GetComponent<RectTransform>();

                    if (iconRect == null)
                        iconRect = iconObject.AddComponent<RectTransform>();

                    Image iconImage = iconObject.GetComponent<Image>();

                    if (iconImage == null)
                        iconImage = iconObject.AddComponent<Image>();

                    float buttonWidth = buttonRect.rect.width;
                    float buttonHeight = buttonRect.rect.height;

                    float iconWidth =
                        config.IconWidth > 0f &&
                        !float.IsNaN(config.IconWidth) &&
                        !float.IsInfinity(config.IconWidth)
                            ? config.IconWidth
                            : buttonWidth;

                    float iconHeight =
                        config.IconHeight > 0f &&
                        !float.IsNaN(config.IconHeight) &&
                        !float.IsInfinity(config.IconHeight)
                            ? config.IconHeight
                            : buttonHeight;

                    // Center the icon relative to the button.
                    iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                    iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                    iconRect.pivot = new Vector2(0.5f, 0.5f);
                    iconRect.sizeDelta =
                        new Vector2(iconWidth, iconHeight);

                    // Zero means exactly centered.
                    iconRect.anchoredPosition = config.IconPosition;
                    iconRect.localScale = Vector3.one;
                    iconRect.localRotation = Quaternion.identity;

                    iconImage.sprite = icon;
                    iconImage.preserveAspect = config.IconPreserveAspect;

                    Color iconColor =
                        config.ApplyTint ? config.Tint : Color.white;

                    iconColor.a *= ClampOpacity(config.IconOpacity);
                    iconImage.color = iconColor;
                    iconImage.raycastTarget = false;
                    iconImage.enabled = true;

                    iconObject.SetActive(true);
                }
            }

            if (!string.IsNullOrEmpty(config.Label))
            {
                bool foundLabel = false;

                foreach (Text label in
                         clone.GetComponentsInChildren<Text>(true))
                {
                    if (label == null ||
                        label.gameObject.name == "ULTRADROID_Label")
                        continue;

                    label.text = config.Label;
                    label.enabled = true;
                    label.gameObject.SetActive(true);

                    float fontSize = Mathf.Clamp(
                        config.LabelFontSize, 1f, 300f);
                    label.fontSize = Mathf.RoundToInt(fontSize);
                    label.alignment = (TextAnchor)(int)config.LabelAlignment;

                    RectTransform rect =
                        label.GetComponent<RectTransform>();

                    if (rect != null)
                        rect.anchoredPosition = config.LabelPosition;

                    Color color = config.LabelColor;

                    color.a *= ClampOpacity(config.LabelOpacity);
                    label.color = color;
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

                    label.fontSize = Mathf.Clamp(
                        config.LabelFontSize, 1f, 300f);
                    label.alignment = config.LabelAlignment switch
                    {
                        ButtonLabelAlignment.UpperLeft => TextAlignmentOptions.TopLeft,
                        ButtonLabelAlignment.UpperCenter => TextAlignmentOptions.Top,
                        ButtonLabelAlignment.UpperRight => TextAlignmentOptions.TopRight,
                        ButtonLabelAlignment.MiddleLeft => TextAlignmentOptions.MidlineLeft,
                        ButtonLabelAlignment.MiddleCenter => TextAlignmentOptions.Center,
                        ButtonLabelAlignment.MiddleRight => TextAlignmentOptions.MidlineRight,
                        ButtonLabelAlignment.LowerLeft => TextAlignmentOptions.BottomLeft,
                        ButtonLabelAlignment.LowerCenter => TextAlignmentOptions.Bottom,
                        ButtonLabelAlignment.LowerRight => TextAlignmentOptions.BottomRight,
                        _ => TextAlignmentOptions.Center
                    };

                    RectTransform rect =
                        label.GetComponent<RectTransform>();

                    if (rect != null)
                        rect.anchoredPosition = config.LabelPosition;

                    Color color = config.LabelColor;

                    color.a *= ClampOpacity(config.LabelOpacity);
                    label.color = color;
                    label.raycastTarget = false;

                    foundLabel = true;
                }

                Transform customLabel =
                    clone.transform.Find("ULTRADROID_Label");

                if (!foundLabel && customLabel == null)
                {
                    CreateLegacyLabel(clone, config.Label, config);
                }
                else if (customLabel != null)
                {
                    Text customText = customLabel.GetComponent<Text>();

                    if (customText != null)
                    {
                        customText.text = config.Label;
                        customText.fontSize = Mathf.RoundToInt(
                            Mathf.Clamp(config.LabelFontSize, 1f, 300f));
                        customText.alignment = (TextAnchor)(int)config.LabelAlignment;

                        RectTransform rect =
                            customLabel.GetComponent<RectTransform>();

                        if (rect != null)
                            rect.anchoredPosition = config.LabelPosition;

                        Color color = config.LabelColor;

                        color.a *= ClampOpacity(config.LabelOpacity);
                        customText.color = color;
                    }
                }
            }

            clone.SetActive(true);
        }

        private void ApplyToggleVisuals(RuntimeButton rb)
        {
            if (rb?.Registration?.Config == null || rb.Control == null)
                return;

            ButtonConfig config = rb.Registration.Config;
            GameObject buttonObject = rb.Control.gameObject;

            Transform iconTransform = buttonObject.transform.Find("ULTRADROID_Icon");
            Image icon = iconTransform != null
                ? iconTransform.GetComponent<Image>()
                : null;

            bool isOn = config.IsToggled;

            Sprite stateIcon = isOn
                ? rb.ToggledOnIconCache
                : rb.ToggledOffIconCache;

            Sprite desiredIcon = stateIcon != null
                ? stateIcon
                : rb.DefaultIcon;

            if (icon != null)
            {
                icon.sprite = desiredIcon;

                Color tint = config.ApplyTint ? config.Tint : Color.white;

                bool stateTint = isOn
                    ? config.ApplyToggledOnTint
                    : config.ApplyToggledOffTint;

                if (stateTint)
                {
                    tint = isOn
                        ? config.ToggledOnTint
                        : config.ToggledOffTint;
                }

                if (!config.Interactable)
                {
                    tint = Color.Lerp(tint, config.DisabledTint, 0.75f);
                }
                else if (config.PressedVisualsEnabled && rb.IsPressed)
                {
                    tint = Color.Lerp(tint, config.PressedTint, 0.65f);
                }

                if (rb.FlashEndsAt > Time.unscaledTime)
                {
                    float duration = Mathf.Max(0.01f, config.FlashDuration);
                    float remaining = Mathf.Clamp01(
                        (rb.FlashEndsAt - Time.unscaledTime) / duration);

                    tint = Color.Lerp(tint, config.FlashColor, remaining);
                }

                tint.a *= ClampOpacity(config.IconOpacity);

                if (!config.Interactable)
                    tint.a *= ClampOpacity(config.DisabledOpacity);

                icon.color = tint;
                icon.preserveAspect = config.IconPreserveAspect;
                icon.raycastTarget = false;
                icon.enabled = true;
                icon.gameObject.SetActive(true);
            }

            float scaleMultiplier = 1f;

            if (config.PressedVisualsEnabled &&
                rb.IsPressed &&
                config.Interactable)
            {
                scaleMultiplier *= Mathf.Clamp(
                    config.PressedScaleMultiplier, 0.1f, 2f);
            }

            float now = Time.unscaledTime;

            if (config.PulseEnabled && rb.EffectEndsAt > now)
            {
                float duration = Mathf.Max(0.01f, config.PulseDuration);

                float progress = Mathf.Clamp01(
                    (now - rb.EffectStartedAt) / duration);

                float envelope = Mathf.Sin(progress * Mathf.PI);

                scaleMultiplier *= Mathf.Lerp(
                    1f,
                    Mathf.Clamp(config.PulseScaleMultiplier, 0.1f, 3f),
                    envelope);
            }

            buttonObject.transform.localScale = rb.BaseScale * scaleMultiplier;

            Image overlay = EnsureCooldownOverlay(rb);

            if (overlay != null)
            {
                bool show = config.ShowCooldownOverlay &&
                            config.IsOnCooldown &&
                            config.Interactable;

                overlay.color = config.CooldownOverlayColor;

                overlay.fillAmount = rb.CooldownDurationAtStart > 0f
                    ? Mathf.Clamp01(
                        config.CooldownRemaining /
                        rb.CooldownDurationAtStart)
                    : 0f;

                overlay.enabled = show;
                overlay.gameObject.SetActive(true);
                overlay.transform.SetAsLastSibling();
            }
        }

        private Image EnsureCooldownOverlay(RuntimeButton rb)
        {
            if (rb?.Control == null ||
                rb.Registration?.Config == null ||
                !rb.Registration.Config.ShowCooldownOverlay)
            {
                return null;
            }

            if (rb.CooldownOverlay != null)
                return rb.CooldownOverlay;

            Transform existing = rb.Control.transform.Find("ULTRADROID_Cooldown");

            if (existing != null)
            {
                rb.CooldownOverlay = existing.GetComponent<Image>();
                if (rb.CooldownOverlay != null)
                    return rb.CooldownOverlay;
            }

            GameObject overlayObject = new GameObject(
                "ULTRADROID_Cooldown",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

            overlayObject.transform.SetParent(rb.Control.transform, false);

            RectTransform rect = overlayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            Image overlay = overlayObject.GetComponent<Image>();
            overlay.raycastTarget = false;
            overlay.type = Image.Type.Filled;
            overlay.fillMethod = Image.FillMethod.Radial360;
            overlay.fillOrigin = (int)Image.Origin360.Top;
            overlay.fillClockwise = true;
            overlay.fillAmount = 0f;
            overlay.enabled = false;

            overlayObject.SetActive(true);
            overlay.transform.SetAsLastSibling();

            rb.CooldownOverlay = overlay;
            return overlay;
        }

        private void UpdateButtonEffects(RuntimeButton rb)
        {
            if (rb?.Registration?.Config == null)
                return;

            ButtonConfig config = rb.Registration.Config;

            int pulseVersion = config.EffectRequestVersion;

            if (pulseVersion != rb.LastEffectRequestVersion)
            {
                rb.LastEffectRequestVersion = pulseVersion;
                rb.EffectStartedAt = Time.unscaledTime;
                rb.EffectEndsAt = rb.EffectStartedAt +
                    Mathf.Max(0.01f, config.PulseDuration);
            }

            int flashVersion = config.FlashRequestVersion;

            if (flashVersion != rb.LastFlashRequestVersion)
            {
                rb.LastFlashRequestVersion = flashVersion;
                rb.FlashEndsAt = Time.unscaledTime +
                    Mathf.Max(0.01f, config.FlashDuration);
            }
        }

        private void UpdateCooldown(RuntimeButton rb)
        {
            if (rb?.Registration?.Config == null)
                return;

            ButtonConfig config = rb.Registration.Config;

            if (rb.LastCooldownCancelVersion != config.CooldownCancelVersion)
            {
                rb.LastCooldownCancelVersion = config.CooldownCancelVersion;
                rb.CooldownWasActive = false;
                rb.CooldownCompletionReported = true;
                rb.CooldownDurationAtStart = 0f;
            }

            if (rb.LastCooldownRequestVersion != config.CooldownRequestVersion)
            {
                rb.LastCooldownRequestVersion = config.CooldownRequestVersion;
                rb.CooldownWasActive = config.IsOnCooldown;
                rb.CooldownCompletionReported = !config.IsOnCooldown;
                rb.CooldownDurationAtStart = config.ActiveCooldownDuration;
            }

            bool active = config.IsOnCooldown;

            if (active)
            {
                rb.CooldownWasActive = true;
                rb.CooldownCompletionReported = false;
                rb.CooldownDurationAtStart = config.ActiveCooldownDuration;
                return;
            }

            if (rb.CooldownWasActive && !rb.CooldownCompletionReported)
            {
                rb.CooldownCompletionReported = true;
                rb.CooldownWasActive = false;
                rb.CooldownDurationAtStart = 0f;

                InvokeSafely(
                    config.Id,
                    "OnCooldownComplete",
                    () => config.OnCooldownComplete?.Invoke());
            }
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
                labelObject = new GameObject("ULTRADROID_Label");
                labelObject.transform.SetParent(clone.transform, false);

                RectTransform rect =
                    labelObject.AddComponent<RectTransform>();

                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = config.LabelPosition;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                Text label = labelObject.AddComponent<Text>();

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
                label.fontSize = Mathf.RoundToInt(
                    Mathf.Clamp(config.LabelFontSize, 1f, 300f));
                label.fontStyle = FontStyle.Bold;
                label.alignment = (TextAnchor)(int)config.LabelAlignment;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.resizeTextForBestFit = false;

                Color color = config.LabelColor;

                color.a *= ClampOpacity(config.LabelOpacity);
                label.color = color;

                label.raycastTarget = false;
                label.supportRichText = false;
                label.enabled = true;

                labelObject.SetActive(true);

                ControlsRuntimeLog.Info(
                    "[ULTRADROID] Created custom label '" +
                    labelText + "' on " + clone.name);
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

        private void AttachPointerHandler(
            GameObject clone,
            RuntimeButton runtimeButton,
            TouchManager manager)
        {
            if (clone == null || runtimeButton == null)
                return;

            EventTrigger trigger = clone.GetComponent<EventTrigger>();

            if (trigger == null)
                trigger = clone.AddComponent<EventTrigger>();

            if (trigger.triggers == null)
                trigger.triggers =
                    new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();

            trigger.triggers.Clear();

            ControlsPointerHandler handler =
                clone.GetComponent<ControlsPointerHandler>();

            if (handler == null)
                handler = clone.AddComponent<ControlsPointerHandler>();

            runtimeButton.PointerHandler = handler;

            handler.Pressed = () =>
            {
                if (IsLayoutEditorActive(manager) || IsGameplayMenuOpen())
                    return;

                ButtonConfig config = runtimeButton.Registration.Config;
                if (config == null)
                    return;

                if (!config.Interactable || config.IsOnCooldown)
                    return;

                runtimeButton.IsPressed = true;
                runtimeButton.HoldTriggered = false;
                runtimeButton.PressStartedAt = Time.unscaledTime;

                float holdDuration = Mathf.Max(0f, config.HoldDuration);
                runtimeButton.NextRepeatAt = Time.unscaledTime + holdDuration;

                InvokeSafely(config.Id, "OnPress", () =>
                    config.OnPress?.Invoke());
            };

            handler.Released = () =>
                EndButtonPress(runtimeButton, false);

            handler.Cancelled = () =>
                EndButtonPress(runtimeButton, true);

            EventTrigger.Entry downEntry = new EventTrigger.Entry();
            downEntry.eventID = EventTriggerType.PointerDown;
            downEntry.callback.AddListener(
                Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityAction<BaseEventData>>(
                        (Action<BaseEventData>)(data =>
                            handler.HandlePointerDown(data))));

            trigger.triggers.Add(downEntry);

            EventTrigger.Entry upEntry = new EventTrigger.Entry();
            upEntry.eventID = EventTriggerType.PointerUp;
            upEntry.callback.AddListener(
                Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityAction<BaseEventData>>(
                        (Action<BaseEventData>)(data =>
                            handler.HandlePointerUp(data))));

            trigger.triggers.Add(upEntry);

            EventTrigger.Entry exitEntry = new EventTrigger.Entry();
            exitEntry.eventID = EventTriggerType.PointerExit;
            exitEntry.callback.AddListener(
                Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityAction<BaseEventData>>(
                        (Action<BaseEventData>)(data =>
                            handler.HandlePointerUp(data))));

            trigger.triggers.Add(exitEntry);
        }

        private void EndButtonPress(
            RuntimeButton runtimeButton,
            bool cancelled)
        {
            if (runtimeButton == null || !runtimeButton.IsPressed)
                return;

            runtimeButton.IsPressed = false;

            ButtonConfig config = runtimeButton.Registration.Config;

            if (config != null)
            {
                // Release is also delivered on cancellation to prevent
                // mods from getting stuck in a held state.
                InvokeSafely(config.Id, "OnRelease", () =>
                    config.OnRelease?.Invoke());
            }
        }

        private void InvokeSafely(
            string id,
            string eventName,
            Action callback)
        {
            try
            {
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] " + eventName +
                    " callback failed for '" + id + "': " + ex);
            }
        }

        private void UpdateButtonInteraction(RuntimeButton runtimeButton)
        {
            if (runtimeButton == null ||
                runtimeButton.Registration?.Config == null)
                return;

            ButtonConfig config = runtimeButton.Registration.Config;

            if (!config.Interactable || config.IsOnCooldown)
            {
                if (runtimeButton.IsPressed)
                    EndButtonPress(runtimeButton, true);

                return;
            }

            if (!runtimeButton.IsPressed)
                return;

            if (IsLayoutEditorActive(activeManager) || IsGameplayMenuOpen())
            {
                EndButtonPress(runtimeButton, true);
                return;
            }

            float now = Time.unscaledTime;
            float holdDuration = Mathf.Max(0f, config.HoldDuration);

            if (!runtimeButton.HoldTriggered &&
                now - runtimeButton.PressStartedAt >= holdDuration)
            {
                runtimeButton.HoldTriggered = true;

                InvokeSafely(config.Id, "OnHold", () =>
                    config.OnHold?.Invoke());

                runtimeButton.NextRepeatAt =
                    now + Mathf.Max(0.01f, config.RepeatInterval);
            }

            if (config.RepeatWhileHeld &&
                runtimeButton.HoldTriggered &&
                now >= runtimeButton.NextRepeatAt)
            {
                float interval = Mathf.Max(0.01f, config.RepeatInterval);

                // Avoid firing a large backlog of repeats after a lag spike.
                runtimeButton.NextRepeatAt = now + interval;

                InvokeSafely(config.Id, "OnRepeat", () =>
                    config.OnRepeat?.Invoke());
            }
        }

        private void BindClick(
            GameObject clone,
            ButtonRegistration registration,
            TouchManager manager,
            RuntimeButton runtimeButton)
        {
            if (clone == null || registration == null)
                return;

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

                buttons = new[] { added };
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
                        if (IsLayoutEditorActive(manager))
                            return;

                        if (IsGameplayMenuOpen())
                            return;

                        ButtonConfig config = registration.Config;

                        if (config == null)
                            return;

                        if (!config.Interactable || config.IsOnCooldown)
                            return;

                        InvokeSafely(config.Id, "OnClick", () =>
                            config.OnClick?.Invoke());

                        float now = Time.unscaledTime;
                        float doubleClickInterval =
                            Mathf.Max(0.01f, config.DoubleClickInterval);

                        if (runtimeButton != null)
                        {
                            if (runtimeButton.HasLastClick &&
                                now - runtimeButton.LastClickAt <= doubleClickInterval)
                            {
                                runtimeButton.HasLastClick = false;

                                InvokeSafely(config.Id, "OnDoubleClick", () =>
                                    config.OnDoubleClick?.Invoke());
                            }
                            else
                            {
                                runtimeButton.HasLastClick = true;
                                runtimeButton.LastClickAt = now;
                            }

                            if (config.ToggleMode)
                            {
                                config.IsToggled = !config.IsToggled;

                                ApplyToggleVisuals(runtimeButton);

                                bool toggledState = config.IsToggled;

                                InvokeSafely(config.Id, "OnToggle", () =>
                                    config.OnToggle?.Invoke(toggledState));
                            }
                        }
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

        private bool IsLayoutEditorActive(TouchManager manager)
        {
            if (manager == null)
                return false;

            try
            {
                return manager.IsEditMode;
            }
            catch
            {
                return false;
            }
        }

        private bool IsGameplayMenuOpen()
        {
            try
            {
                OptionsManager options = OptionsManager.Instance;

                if (options == null)
                    return false;

                if (options.mainMenu || options.paused)
                    return true;

                if (options.optionsMenu != null &&
                    options.optionsMenu.gameObject.activeInHierarchy)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Could not read menu state: " + ex.Message);

                return false;
            }
        }

        private void ApplyVisibility(RuntimeButton runtimeButton)
        {
            if (runtimeButton?.Registration?.Config == null)
                return;

            GameObject buttonObject = runtimeButton.Control != null
                ? runtimeButton.Control.gameObject
                : null;

            if (buttonObject == null)
                return;

            EditableTouchControl editable =
                buttonObject.GetComponent<EditableTouchControl>();

            bool editing = IsLayoutEditorActive(activeManager);

            if (editable != null && editable.enabled != editing)
                editable.enabled = editing;

            if (editing)
            {
                if (!buttonObject.activeSelf)
                    buttonObject.SetActive(true);

                return;
            }

            if (IsGameplayMenuOpen())
            {
                if (buttonObject.activeSelf)
                    buttonObject.SetActive(false);

                return;
            }

            bool visible = true;

            try
            {
                Func<bool> predicate =
                    runtimeButton.Registration.Config.IsVisible;

                if (predicate != null)
                    visible = predicate();
            }
            catch (Exception ex)
            {
                ControlsRuntimeLog.Error(
                    "[ULTRADROID] Visibility check failed for '" +
                    runtimeButton.Registration.Config.Id +
                    "': " + ex);

                return;
            }

            if (buttonObject.activeSelf != visible)
                buttonObject.SetActive(visible);
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

            GameObject clone = rb.Control != null
                ? rb.Control.gameObject
                : null;

            if (manager != null)
            {
                if (clone != null)
                {
                    if (manager.inGameButtons != null)
                        manager.inGameButtons.Remove(clone);

                    RectTransform rect =
                        clone.GetComponent<RectTransform>();

                    if (manager.editableControls != null && rect != null)
                        manager.editableControls.Remove(rect);
                }

                // Rebuild even if the clone has already disappeared.
                editableControlsRebuildPending = true;
            }

            if (rb.Registration?.Handle != null)
            {
                rb.Registration.Handle.IsReady = false;
                rb.Registration.Handle.GameObject = null;
            }

            if (clone != null)
                UnityEngine.Object.Destroy(clone);

            ControlsRuntimeLog.Info(
                "[ULTRADROID] Removed runtime button: " + id);
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
                OnScreenButton control)
            {
                Registration = registration;
                Control = control;
            }

            internal ButtonRegistration Registration { get; }
            internal OnScreenButton Control { get; }

            internal ControlsPointerHandler PointerHandler;
            internal Sprite DefaultIcon;

            internal Vector3 BaseScale;
            internal Image CooldownOverlay;

            internal Sprite ToggledOnIconCache;
            internal Sprite ToggledOffIconCache;

            internal float EffectStartedAt;
            internal float EffectEndsAt;
            internal int LastEffectRequestVersion;

            internal float FlashEndsAt;
            internal int LastFlashRequestVersion;

            internal bool CooldownWasActive;
            internal bool CooldownCompletionReported;
            internal float CooldownDurationAtStart;
            internal int LastCooldownRequestVersion;
            internal int LastCooldownCancelVersion;

            internal bool IsPressed;
            internal bool HoldTriggered;
            internal bool HasLastClick;

            internal float PressStartedAt;
            internal float NextRepeatAt;
            internal float LastClickAt;
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
