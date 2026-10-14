using System;
using UnityEngine;

namespace ULTRADROID.API
{
    public sealed class ButtonConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// **Summary:**

        /// Preferred way to set an icon.
        /// Path relative to BepInEx/plugins or absolute.
        /// Example: "ultradroid-demo-icon.png"
        /// 
        public string IconPath { get; set; }

        /// **Summary:**
Legacy – prefer IconPath
        public Sprite Icon { get; set; }

        public bool ApplyTint { get; set; } = false;
        public Color Tint { get; set; } = Color.white;
        public Vector2 PositionOffset { get; set; } = new Vector2(0f, 90f);
        public float ScaleMultiplier { get; set; } = 1f;
        public string ControlPath { get; set; } = "<Keyboard>/f15";
        public Action OnClick { get; set; }
    }
}
