using System;
using UnityEngine;

namespace ULTRADROID.API
{
    /// <summary>
    /// Describes a button a mod wants ULTRADROID to add to the mobile HUD.
    /// IDs must be unique across all mods, e.g. "MyMod.Dash".
    /// </summary>
    public sealed class ButtonConfig
    {
        /// <summary>A globally unique, stable ID. Used to identify the button in saved layouts.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Optional text to show on the button. If empty, cloned text is left unchanged.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Optional Unity sprite supplied by the mod. It replaces the first Image found under the button.
        /// The mod is responsible for loading/creating the sprite.
        /// </summary>
        public Sprite Icon { get; set; }

        /// <summary>Whether to tint the button's Graphics using Tint.</summary>
        public bool ApplyTint { get; set; } = false;

        /// <summary>Visual tint. White keeps the cloned button's colors unchanged.</summary>
        public Color Tint { get; set; } = Color.white;

        /// <summary>Offset from the cloned native button's starting position.</summary>
        public Vector2 PositionOffset { get; set; } = new Vector2(0f, 90f);

        /// <summary>Multiplier applied to the native button's baseline scale. Must be greater than zero.</summary>
        public float ScaleMultiplier { get; set; } = 1f;

        /// <summary>
        /// The Input System control path for the virtual on-screen button. F15 is the safe default.
        /// Most mods should use OnClick for their custom action instead of reusing a native key path.
        /// </summary>
        public string ControlPath { get; set; } = "<Keyboard>/f15";

        /// <summary>
        /// Called when the Unity UI Button is clicked (normally on pointer release), not on pointer-down.
        /// </summary>
        public Action OnClick { get; set; }
    }
}
