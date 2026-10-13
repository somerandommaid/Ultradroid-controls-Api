
using System;
using System.IO;

using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ULTRADROID.API;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

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

        private UnityAction<Scene, LoadSceneMode> sceneLoadedHandler;
        private bool initialized;

        public override void Load()
        {
            Log.LogInfo(
                "[ULTRADROID Example] Loaded; waiting for scene load."
            );

            sceneLoadedHandler = OnSceneLoaded;
            SceneManager.sceneLoaded += sceneLoadedHandler;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (initialized)
                return;

            initialized = true;
            SceneManager.sceneLoaded -= sceneLoadedHandler;

            Log.LogInfo(
                "[ULTRADROID Example] Scene loaded: " + scene.name
            );

            Sprite icon = LoadIcon("ultradroid-demo-icon.png");

            Log.LogInfo(
                "[ULTRADROID Example] Registering button."
            );

            buttonHandle = ULTRADROIDControls.AddButton(
                new ButtonConfig
                {
                    Id = "ULTRADROID.Example.Demo",
                    Label = "DEMO",
                    Icon = icon,

                    PositionOffset = new Vector2(120f, 90f),
                    ApplyTint = false,
                    ScaleMultiplier = 1f,

                    OnClick = () =>
                        Log.LogInfo(
                            "[ULTRADROID Example] Demo button clicked."
                        )
                }
            );

            Log.LogInfo(
                "[ULTRADROID Example] Registered button ID: "
                + buttonHandle.Id
            );
        }

        private Sprite LoadIcon(string fileName)
        {
            Texture2D texture = null;

            try
            {
                string assemblyPath =
                    typeof(ExampleButtonMod).Assembly.Location;

                string folder = Path.GetDirectoryName(assemblyPath);

                if (string.IsNullOrEmpty(folder))
                    folder = Path.Combine(Paths.PluginPath, "ULTRADROID");

                string iconPath = Path.Combine(folder, fileName);

                Log.LogInfo(
                    "[ULTRADROID Example] Looking for icon: " + iconPath
                );

                if (!File.Exists(iconPath))
                {
                    Log.LogWarning(
                        "[ULTRADROID Example] Icon not found; "
                        + "using text-only button."
                    );

                    return null;
                }

                byte[] pngData = File.ReadAllBytes(iconPath);

                Log.LogInfo(
                    "[ULTRADROID Example] PNG read; bytes: "
                    + pngData.Length
                );

                if (pngData.Length == 0)
                {
                    Log.LogWarning(
                        "[ULTRADROID Example] PNG is empty."
                    );

                    return null;
                }

                Log.LogInfo(
                    "[ULTRADROID Example] Creating Texture2D."
                );

                texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false
                );

                var il2cppData =
                    new Il2CppStructArray<byte>(pngData.Length);

                for (int i = 0; i < pngData.Length; i++)
                    il2cppData[i] = pngData[i];

                Log.LogInfo(
                    "[ULTRADROID Example] Calling ImageConversion.LoadImage."
                );

                bool loaded = ImageConversion.LoadImage(
                    texture,
                    il2cppData,
                    false
                );

                Log.LogInfo(
                    "[ULTRADROID Example] LoadImage returned: " + loaded
                );

                if (!loaded)
                {
                    Log.LogWarning(
                        "[ULTRADROID Example] PNG decoding failed."
                    );

                    return null;
                }

                texture.name = "ULTRADROID_DemoIcon";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;

                Log.LogInfo(
                    "[ULTRADROID Example] Creating Sprite."
                );

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );

                sprite.name = "ULTRADROID_DemoIcon";

                Log.LogInfo(
                    "[ULTRADROID Example] Sprite created successfully."
                );

                return sprite;
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[ULTRADROID Example] Icon loading failed: " + ex
                );

                return null;
            }
        }
    }
}
