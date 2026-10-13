using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using ULTRADROID.API;

namespace ULTRADROID.Core
{
    [BepInPlugin(ULTRADROIDControls.PluginGuid, "ULTRADROID Controls API", ULTRADROIDControls.PluginVersion)]
    public sealed class ULTRADROIDPlugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo("[ULTRADROID] Controls API 0.1.0 loading");
            ClassInjector.RegisterTypeInIl2Cpp<ControlsRuntime>();
            AddComponent<ControlsRuntime>();
            Log.LogInfo("[ULTRADROID] Runtime registered; waiting for TouchManager");
        }
    }
}
