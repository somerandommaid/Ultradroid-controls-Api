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
            sceneLoadedHandler = Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(sceneLoadedCallback);

            SceneManager.sceneLoaded += sceneLoadedHandler;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Bootstrap" || scene.name == "Intro")
                return;

            if (buttonRegistered)
                return;

            buttonRegistered = true;
            SceneManager.sceneLoaded -= sceneLoadedHandler;

            buttonHandle = ULTRADROIDControls.AddButton(new ButtonConfig
            {
                Id = "ULTRADROID.Example.Demo",
                Label = "",
                Width = 100f,
                Height = 40f,
                IconWidth = 500f,
                IconHeight = 200f,
                IconPosition = Vector2.zero,
                IconPreserveAspect = true,
                IconPath = "ultradroid-demo-icon.png",   // Core will load this
                PositionOffset = new Vector2(120f, 90f),
                HideBackground = true,
                BackgroundPath = null,
                ApplyTint = true,
                ScaleMultiplier = 1f,
                OnClick = () =>
                {
                    Log.LogInfo("[ULTRADROID Example] Demo button clicked.");
                }
            });
        }
    }
}
