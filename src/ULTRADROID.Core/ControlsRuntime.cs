
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
                    ApplyVisibility(runtimeButton);

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
            LogVisibilityDiagnostics(clone);
            BindClick(clone, registration, manager);

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

            clone.SetActive(true);

            var runtimeButton =
                new RuntimeButton(registration, control);

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

            // No custom sprite: leave the existing background unchanged.
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
            image.preserveAspect = config.BackgroundPreserveAspect;
            image.color = Color.white;
            image.raycastTarget = false;
            image.enabled = true;

            backgroundObject.SetActive(true);

            // Draw the custom background behind the other button contents.
            backgroundObject.transform.SetAsFirstSibling();
        }

        private void ApplyVisuals(
            GameObject clone,
            ButtonConfig config)
        {
            if (clone == null || config == null)
                return;

            HideOriginalVisuals(clone);

            if (config.HideBackground)
            {
                // Hide only the original root Image on the cloned button.
                Image originalBackground = clone.GetComponent<Image>();

                if (originalBackground != null &&
                    originalBackground.gameObject.name != "ULTRADROID_Background" &&
                    originalBackground.gameObject.name != "ULTRADROID_Icon")
                {
                    originalBackground.enabled = false;
                }
            }

            ApplyBackground(clone, config);

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
                        graphic.gameObject.name != "ULTRADROID_Background")
                    {
                        graphic.color = config.Tint;
                    }
                }
            }

            if (icon != null)
            {
                List<Image> imageList = new List<Image>();

                foreach (Image candidate in
                         clone.GetComponentsInChildren<Image>(true))
                {
                    if (candidate != null &&
                        candidate.gameObject.name != "ULTRADROID_Background")
                    {
                        imageList.Add(candidate);
                    }
                }

                Image[] images = imageList.ToArray();

                if (images.Length == 0)
                {
                    try
                    {
                        GameObject iconGo = new GameObject("ULTRADROID_Icon");
                        iconGo.transform.SetParent(clone.transform, false);

                        RectTransform rt = iconGo.AddComponent<RectTransform>();
                        rt.anchorMin = Vector2.zero;
                        rt.anchorMax = Vector2.one;
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                        rt.localScale = Vector3.one;
                        rt.localPosition = Vector3.zero;

                        Image rootImage = iconGo.AddComponent<Image>();

                        if (rootImage != null)
                        {
                            rootImage.preserveAspect = true;
                            rootImage.raycastTarget = false;
                            rootImage.enabled = true;

                            images = new Image[] { rootImage };
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
                    image.preserveAspect = config.IconPreserveAspect;
                    image.color = config.ApplyTint
                        ? config.Tint
                        : Color.white;

                    image.enabled = true;
                    image.gameObject.SetActive(true);
                    image.raycastTarget = false;

                    RectTransform iconRect =
                        image.GetComponent<RectTransform>();

                    RectTransform buttonRect =
                        clone.GetComponent<RectTransform>();

                    if (iconRect != null && buttonRect != null)
                    {
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

                        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                        iconRect.pivot = new Vector2(0.5f, 0.5f);
                        iconRect.sizeDelta = new Vector2(iconWidth, iconHeight);
                        iconRect.anchoredPosition = config.IconPosition;
                        iconRect.localScale = Vector3.one;
                    }
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
                labelObject = new GameObject("ULTRADROID_Label");
                labelObject.transform.SetParent(clone.transform, false);

                RectTransform rect =
                    labelObject.AddComponent<RectTransform>();

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
            ButtonRegistration registration,
            TouchManager manager)
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

            if (IsLayoutEditorActive(activeManager))
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
