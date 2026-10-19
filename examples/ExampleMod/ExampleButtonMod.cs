
using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using ULTRADROID.API;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

namespace ULTRADROID.ExampleMod
{
    [BepInPlugin(
        "somerandommaid.ultradroid.example",
        "ULTRADROID Example Button",
        "0.1.0"
    )]
    [BepInDependency(
        ULTRADROIDControls.PluginGuid,
        BepInDependency.DependencyFlags.HardDependency
    )]
    public sealed class ExampleButtonMod : BasePlugin
    {
        private ButtonHandle buttonHandle;

        private Action<Scene, LoadSceneMode> sceneLoadedCallback;
        private UnityAction<Scene, LoadSceneMode> sceneLoadedHandler;

        private bool buttonRegistered;

        public override void Load()
        {
            sceneLoadedCallback = OnSceneLoaded;

            sceneLoadedHandler =
                Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(
                        sceneLoadedCallback
                    );

            SceneManager.sceneLoaded += sceneLoadedHandler;

            Log.LogInfo(
                "[ULTRADROID Example] Waiting for gameplay scene."
            );
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Ignore startup scenes.
            if (scene.name == "Bootstrap" ||
                scene.name == "Intro")
            {
                return;
            }

            // Do not register the same example button twice.
            if (buttonRegistered)
                return;

            try
            {
                buttonHandle = ULTRADROIDControls.AddButton(
                    new ButtonConfig
                    {
                        Id = "ULTRADROID.Example.Demo",
                        Label = "DEMO",

                        // Relative to BepInEx/plugins.
                        // Install the image at:
                        // BepInEx/plugins/ExampleMod/ultradroid-demo-icon.png
                        IconPath =
                            "ExampleMod/ultradroid-demo-icon.png",

                        PositionOffset = new Vector2(120f, 90f),

                        ApplyTint = false,
                        ScaleMultiplier = 1f,

                        OnClick = () =>
                        {
                            Log.LogInfo(
                                "[ULTRADROID Example] Demo button clicked."
                            );
                        }
                    }
                );

                // Mark registration successful only after AddButton returns.
                buttonRegistered = true;

                // No more scene callbacks are needed after success.
                SceneManager.sceneLoaded -= sceneLoadedHandler;

                Log.LogInfo(
                    "[ULTRADROID Example] Demo button registered."
                );
            }
            catch (Exception ex)
            {
                // Keep listening so another scene load can retry.
                Log.LogError(
                    "[ULTRADROID Example] Button registration failed: " +
                    ex
                );
            }
        }
    }
}
