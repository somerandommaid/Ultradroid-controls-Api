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
                ControlsRuntime runtime = AddComponent<ControlsRuntime>();
            }
            catch (System.Exception ex)
            {
                Log.LogError("[ULTRADROID] Runtime startup failed: " + ex);
            }
        }
    }
}
