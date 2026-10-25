
using System;
using UnityEngine;

namespace ULTRADROID.API
{
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
        /// Evaluated by the Controls runtime every frame.
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
        /// Preserve the image's aspect ratio when displaying it.
        /// </summary>
        public bool IconPreserveAspect { get; set; } = true;

        /// <summary>
        /// Optional custom background image file path.
        /// Relative paths are resolved from the BepInEx plugins folder.
        /// </summary>
        public string BackgroundPath { get; set; }
        
        /// <summary>
        /// Optional custom background sprite.
        /// Takes priority over BackgroundPath.
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

        // Opacity values range from 0.0 (transparent) to 1.0 (opaque).
        public float BackgroundOpacity { get; set; } = 1f;
        public float IconOpacity { get; set; } = 1f;
        public float LabelOpacity { get; set; } = 1f;
    }
}
