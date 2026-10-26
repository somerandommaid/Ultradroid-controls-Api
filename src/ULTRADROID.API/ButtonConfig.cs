
using System;
using UnityEngine;

namespace ULTRADROID.API
{
    public enum ButtonLabelAlignment
    {
        UpperLeft,
        UpperCenter,
        UpperRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        LowerLeft,
        LowerCenter,
        LowerRight
    }

    /// <summary>
    /// Configuration for a custom ULTRAKILL mobile HUD button.
    /// </summary>
    public sealed class ButtonConfig
    {
        public string Id { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string IconPath { get; set; }

        public Sprite Icon { get; set; }

        public bool ApplyTint { get; set; } = false;

        public Color Tint { get; set; } = Color.white;

        public Vector2 PositionOffset { get; set; } =
            new Vector2(0f, 90f);

        public float ScaleMultiplier { get; set; } = 1f;

        public string ControlPath { get; set; } = "<Keyboard>/f15";

        public Action OnClick { get; set; }

        /// <summary>
        /// Optional condition determining whether this button is visible.
        /// A null condition means the button is always visible.
        /// </summary>
        public Func<bool> IsVisible { get; set; }

        /// <summary>
        /// Optional width in UI layout units. Zero uses the template width.
        /// </summary>
        public float Width { get; set; } = 0f;

        /// <summary>
        /// Optional height in UI layout units. Zero uses the template height.
        /// </summary>
        public float Height { get; set; } = 0f;

        /// <summary>
        /// Optional icon width in UI layout units.
        /// Zero uses the button's width.
        /// </summary>
        public float IconWidth { get; set; } = 0f;

        /// <summary>
        /// Optional icon height in UI layout units.
        /// Zero uses the button's height.
        /// </summary>
        public float IconHeight { get; set; } = 0f;

        /// <summary>
        /// Icon position relative to the center of the button.
        /// </summary>
        public Vector2 IconPosition { get; set; } = Vector2.zero;

        /// <summary>
        /// Preserve the icon image's aspect ratio.
        /// </summary>
        public bool IconPreserveAspect { get; set; } = true;

        /// <summary>
        /// Optional custom background image file path.
        /// Relative paths are resolved from the BepInEx plugins folder.
        /// </summary>
        public string BackgroundPath { get; set; }

        /// <summary>
        /// Optional custom background sprite.
        /// Takes priority over BackgroundPath unless RoundedCorners is enabled.
        /// </summary>
        public Sprite Background { get; set; }

        /// <summary>
        /// Preserve the background sprite's aspect ratio.
        /// False stretches it to fill the button.
        /// </summary>
        public bool BackgroundPreserveAspect { get; set; } = false;

        /// <summary>
        /// Hides the original button background.
        /// </summary>
        public bool HideBackground { get; set; } = false;

        /// <summary>
        /// Background opacity from 0 (transparent) to 1 (opaque).
        /// </summary>
        public float BackgroundOpacity { get; set; } = 1f;

        /// <summary>
        /// Icon opacity from 0 (transparent) to 1 (opaque).
        /// </summary>
        public float IconOpacity { get; set; } = 1f;

        /// <summary>
        /// Label opacity from 0 (transparent) to 1 (opaque).
        /// </summary>
        public float LabelOpacity { get; set; } = 1f;

        /// <summary>
        /// Enables rounded background corners.
        /// </summary>
        public bool RoundedCorners { get; set; } = false;

        /// <summary>
        /// Corner radius in UI units.
        /// </summary>
        public float CornerRadius { get; set; } = 12f;

        /// <summary>
        /// Makes the button background invisible while preserving its click area.
        /// </summary>
        public bool TransparentButton { get; set; } = false;

        /// <summary>
        /// Label font size.
        /// </summary>
        public float LabelFontSize { get; set; } = 18f;

        /// <summary>
        /// Label text color.
        /// </summary>
        public Color LabelColor { get; set; } = Color.white;

        /// <summary>
        /// Label alignment, independent of Unity's TextAnchor API type.
        /// </summary>
        public ButtonLabelAlignment LabelAlignment { get; set; } =
            ButtonLabelAlignment.MiddleCenter;

        /// <summary>
        /// Label position relative to its RectTransform anchors.
        /// </summary>
        public Vector2 LabelPosition { get; set; } = Vector2.zero;
    }
}
