
using BepInEx;
using BepInEx.Unity.IL2CPP;
using ULTRADROID.API;

namespace ULTRADROID.Core
{
    [BepInPlugin(
        ULTRADROIDControls.PluginGuid,
        "ULTRADROID Controls API",
        ULTRADROIDControls.PluginVersion)]
    public sealed class ULTRADROIDPlugin : BasePlugin
    {
        public override void Load()
        {
            ControlsRuntimeLog.Source = Log;

            try
            {
                ControlsPointerHandler.Register();

                AddComponent<ControlsRuntime>();

                Log.LogInfo("[ULTRADROID] Controls API runtime started.");
            }
            catch (System.Exception ex)
            {
                Log.LogError(
                    "[ULTRADROID] Runtime startup failed: " + ex);
            }
        }
    }
}
