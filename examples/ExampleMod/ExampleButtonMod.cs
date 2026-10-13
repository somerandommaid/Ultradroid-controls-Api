
using System;
using System.IO;

using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ULTRADROID.API;
using UnityEngine;

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

        public override void Load()
        {
            Sprite icon = LoadIcon("ultradroid-demo-icon.png");

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
            string assemblyPath =
                typeof(ExampleButtonMod).Assembly.Location;

            string folder = Path.GetDirectoryName(assemblyPath);

            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.Combine(Paths.PluginPath, "ULTRADROID");
            }

            string iconPath = Path.Combine(folder, fileName);

            if (!File.Exists(iconPath))
            {
                Log.LogWarning(
                    "[ULTRADROID Example] Icon not found: " + iconPath
                );

                return null;
            }

            Texture2D texture = null;

            try
            {
                byte[] pngData = File.ReadAllBytes(iconPath);

                texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false
                );

                // Convert the normal C# byte array into an IL2CPP array.
                var il2cppData =
                    new Il2CppStructArray<byte>(pngData.Length);

                for (int i = 0; i < pngData.Length; i++)
                {
                    il2cppData[i] = pngData[i];
                }

                bool loaded = ImageConversion.LoadImage(
                    texture,
                    il2cppData,
                    false
                );

                if (!loaded)
                {
                    Log.LogWarning(
                        "[ULTRADROID Example] Failed to decode PNG."
                    );

                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                texture.name = "ULTRADROID_DemoIcon";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;

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

                sprite.name = "ULTRADROID_DemoIcon";

                Log.LogInfo(
                    "[ULTRADROID Example] Custom icon loaded: "
                    + iconPath
                );

                return sprite;
            }
            catch (Exception ex)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                Log.LogWarning(
                    "[ULTRADROID Example] Icon loading failed: "
                    + ex
                );

                return null;
            }
        }
    }
}
