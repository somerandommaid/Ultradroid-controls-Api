using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
            Log.LogInfo(
                "[ULTRADROID Example] Loaded; waiting for scene load."
            );

            sceneLoadedCallback = OnSceneLoaded;

            sceneLoadedHandler =
                Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(
                        sceneLoadedCallback
                    );

            SceneManager.sceneLoaded += sceneLoadedHandler;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo(
                "[ULTRADROID Example] Scene loaded: " + scene.name
            );

            if (scene.name == "Bootstrap" ||
                scene.name == "Intro")
            {
                Log.LogInfo(
                    "[ULTRADROID Example] Skipping startup scene."
                );
                return;
            }

            if (buttonRegistered)
                return;

            buttonRegistered = true;

            SceneManager.sceneLoaded -= sceneLoadedHandler;

            Log.LogInfo(
                "[ULTRADROID Example] Loading icon in scene: " +
                scene.name
            );

            Sprite icon = LoadIcon("ultradroid-demo-icon.png");

            Log.LogInfo(
                "[ULTRADROID Example] LoadIcon returned: " +
                (icon != null)
            );

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
                    {
                        Log.LogInfo(
                            "[ULTRADROID Example] Demo button clicked."
                        );
                    }
                }
            );

            Log.LogInfo(
                "[ULTRADROID Example] Button registered: " +
                buttonHandle.Id
            );
        }

        private Sprite LoadIcon(string fileName)
        {
            string path = Path.Combine(
                Paths.PluginPath,
                fileName
            );

            Log.LogInfo(
                "[ULTRADROID Example] Looking for icon: " + path
            );

            try
            {
                if (!File.Exists(path))
                {
                    Log.LogWarning(
                        "[ULTRADROID Example] Icon file not found."
                    );
                    return null;
                }

                byte[] fileBytes = File.ReadAllBytes(path);

                Log.LogInfo(
                    "[ULTRADROID Example] PNG read; bytes: " +
                    fileBytes.Length
                );

                Log.LogInfo(
                    "[ULTRADROID Example] Creating Texture2D."
                );

                Texture2D texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false
                );

                Log.LogInfo(
                    "[ULTRADROID Example] Texture2D created."
                );

                Log.LogInfo(
                    "[ULTRADROID Example] Creating IL2CPP byte array."
                );

                Il2CppStructArray<byte> il2CppBytes =
                    new Il2CppStructArray<byte>(fileBytes.Length);

                Log.LogInfo(
                    "[ULTRADROID Example] IL2CPP byte array created."
                );

                Log.LogInfo(
                    "[ULTRADROID Example] Copying PNG bytes."
                );

                for (int i = 0; i < fileBytes.Length; i++)
                {
                    il2CppBytes[i] = fileBytes[i];
                }

                Log.LogInfo(
                    "[ULTRADROID Example] PNG bytes copied."
                );

                Log.LogInfo(
                    "[ULTRADROID Example] Calling LoadImage."
                );

                bool loaded = ImageConversion.LoadImage(
                    texture,
                    il2CppBytes,
                    false
                );

                Log.LogInfo(
                    "[ULTRADROID Example] LoadImage returned: " +
                    loaded
                );

                if (!loaded)
                {
                    Log.LogError(
                        "[ULTRADROID Example] PNG decoding failed."
                    );

                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                Log.LogInfo(
                    "[ULTRADROID Example] Creating sprite."
                );

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(
                        0f,
                        0f,
                        texture.width,
                        texture.height
                    ),
                    new Vector2(0.5f, 0.5f),
                    100f
                );

                Log.LogInfo(
                    "[ULTRADROID Example] Sprite.Create returned."
                );

                if (sprite == null)
                {
                    Log.LogError(
                        "[ULTRADROID Example] Sprite creation failed."
                    );

                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                Log.LogInfo(
                    "[ULTRADROID Example] Icon sprite created."
                );

                return sprite;
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[ULTRADROID Example] Icon loading exception: " +
                    ex
                );

                return null;
            }
        }
    }
}
