using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
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
Log.LogInfo("[ULTRADROID] Controls API loading");

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<ControlsRuntime>();
            Log.LogInfo("[ULTRADROID] ControlsRuntime type registered");

            GameObject runtimeObject =
                new GameObject("ULTRADROID_ControlsRuntime");

            Object.DontDestroyOnLoad(runtimeObject);

            ControlsRuntime runtime =
                runtimeObject.AddComponent<ControlsRuntime>();

            Log.LogInfo(
                "[ULTRADROID] Runtime GameObject created: " +
                runtimeObject.name);

            Log.LogInfo(
                "[ULTRADROID] Runtime component attached: " +
                (runtime != null));

            Log.LogInfo(
                "[ULTRADROID] Runtime enabled: " +
                (runtime != null && runtime.enabled));
        }
        catch (System.Exception ex)
        {
            Log.LogError("[ULTRADROID] Runtime startup failed: " + ex);
        }
    }
}

}
