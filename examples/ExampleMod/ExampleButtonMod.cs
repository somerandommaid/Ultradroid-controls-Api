using BepInEx;
using BepInEx.Unity.IL2CPP;
using ULTRADROID.API;
using UnityEngine;

namespace ULTRADROID.ExampleMod
{
    [BepInPlugin("somerandommaid.ultradroid.example", "ULTRADROID Example Button", "0.1.0")]
    [BepInDependency(ULTRADROIDControls.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class ExampleButtonMod : BasePlugin
    {
        private ButtonHandle buttonHandle;

        public override void Load()
        {
            buttonHandle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Example.Demo",
                Label = "DEMO",
                PositionOffset = new Vector2(120f, 90f),
                ApplyTint = true,
                Tint = new Color(0.20f, 0.85f, 1.00f, 0.95f),
                ScaleMultiplier = 1f,
                OnClick = () => Log.LogInfo("[ULTRADROID Example] Demo button clicked.")
            });

            Log.LogInfo("[ULTRADROID Example] Registered button ID: " + buttonHandle.Id);
        }
    }
}
