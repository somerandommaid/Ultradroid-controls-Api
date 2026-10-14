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
            Log.LogInfo("[ULTRADROID] Controls API loading");

            try
            {
                // Use BepInEx's IL2CPP host for injected MonoBehaviours.
                // It registers the type and attaches it to the persistent host object.
                ControlsRuntime runtime = AddComponent<ControlsRuntime>();

                Log.LogInfo("[ULTRADROID] ControlsRuntime attached via BasePlugin.AddComponent: " +
                    (runtime != null));

                if (runtime != null)
                {
                    Log.LogInfo("[ULTRADROID] Runtime host object: " + runtime.gameObject.name);
                    Log.LogInfo("[ULTRADROID] Runtime enabled: " + runtime.enabled);
                }
            }
            catch (System.Exception ex)
            {
                Log.LogError("[ULTRADROID] Runtime startup failed: " + ex);
            }
        }
    }
}
