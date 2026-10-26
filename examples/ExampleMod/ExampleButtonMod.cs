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
"0.2.0"
)]
[BepInDependency(
ULTRADROIDControls.PluginGuid,
BepInDependency.DependencyFlags.HardDependency
)]
public sealed class ExampleButtonMod : BasePlugin
{
private Action<Scene, LoadSceneMode> sceneLoadedCallback;
private UnityAction<Scene, LoadSceneMode> sceneLoadedHandler;
private bool buttonsRegistered;

    private ButtonConfig cooldownConfig;
    private ButtonConfig effectsConfig;

    public override void Load()
    {
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
        if (scene.name == "Bootstrap" || scene.name == "Intro")
            return;

        if (buttonsRegistered)
            return;

        buttonsRegistered = true;
        SceneManager.sceneLoaded -= sceneLoadedHandler;

        RegisterInteractionButton();
        RegisterCooldownButton();
        RegisterEffectsButton();
        RegisterVisualsButton();

        Log.LogInfo(
            "[ULTRADROID Example] Registered four feature-test buttons."
        );
    }

    private void RegisterInteractionButton()
    {
        ULTRADROIDControls.AddButton(new ButtonConfig
        {
            Id = "ULTRADROID.Example.Interaction",
            Label = "INTERACTION",

            Width = 150f,
            Height = 55f,
            PositionOffset = new Vector2(-240f, 100f),
            ScaleMultiplier = 1f,

            BackgroundPath = "Background.png",
            TransparentButton = false,
            ApplyTint = true,
            Tint = new Color(0.15f, 0.55f, 1f, 1f),

            IconPath = "ultradroid-demo-icon.png",
            IconWidth = 42f,
            IconHeight = 42f,
            IconPosition = Vector2.zero,
            IconPreserveAspect = true,
            IconOpacity = 1f,

            LabelFontSize = 12f,
            LabelColor = Color.white,
            LabelAlignment = ButtonLabelAlignment.MiddleCenter,
            LabelPosition = new Vector2(0f, -35f),
            LabelOpacity = 1f,

            RoundedCorners = true,
            CornerRadius = 12f,

            HoldDuration = 0.5f,
            RepeatInterval = 0.5f,
            DoubleClickInterval = 0.3f,

            RepeatWhileHeld = true,
            ToggleMode = true,

            OnClick = () =>
                Log.LogInfo("[Interaction] CLICK"),

            OnPress = () =>
                Log.LogInfo("[Interaction] PRESS"),

            OnRelease = () =>
                Log.LogInfo("[Interaction] RELEASE"),

            OnHold = () =>
                Log.LogInfo("[Interaction] HOLD"),

            OnRepeat = () =>
                Log.LogInfo("[Interaction] REPEAT"),

            OnDoubleClick = () =>
                Log.LogInfo("[Interaction] DOUBLE CLICK"),

            OnToggle = enabled =>
                Log.LogInfo("[Interaction] TOGGLE: " + enabled)
        });
    }

    private void RegisterCooldownButton()
    {
        cooldownConfig = new ButtonConfig
        {
            Id = "ULTRADROID.Example.Cooldown",
            Label = "COOLDOWN",

            Width = 150f,
            Height = 55f,
            PositionOffset = new Vector2(-80f, 100f),
            ScaleMultiplier = 1f,

            BackgroundPath = "Background.png",
            TransparentButton = false,
            ApplyTint = true,
            Tint = new Color(1f, 0.55f, 0.12f, 1f),

            IconPath = "ultradroid-demo-icon.png",
            IconWidth = 42f,
            IconHeight = 42f,
            IconPosition = Vector2.zero,
            IconPreserveAspect = true,

            LabelFontSize = 12f,
            LabelColor = Color.white,
            LabelAlignment = ButtonLabelAlignment.MiddleCenter,
            LabelPosition = new Vector2(0f, -35f),

            RoundedCorners = true,
            CornerRadius = 12f,

            CooldownDuration = 3f,
            ShowCooldownOverlay = true,

            OnClick = () =>
            {
                if (cooldownConfig.IsOnCooldown)
                {
                    Log.LogInfo(
                        "[Cooldown] Still cooling down: " +
                        cooldownConfig.CooldownRemaining.ToString("F1") +
                        " seconds remaining"
                    );
                    return;
                }

                cooldownConfig.StartCooldown(3f);
                Log.LogInfo("[Cooldown] Started 3-second cooldown.");
            },

            OnCooldownComplete = () =>
                Log.LogInfo("[Cooldown] COMPLETE")
        };

        ULTRADROIDControls.AddButton(cooldownConfig);
    }

    private void RegisterEffectsButton()
    {
        effectsConfig = new ButtonConfig
        {
            Id = "ULTRADROID.Example.Effects",
            Label = "EFFECTS",

            Width = 150f,
            Height = 55f,
            PositionOffset = new Vector2(80f, 100f),
            ScaleMultiplier = 1f,

            BackgroundPath = "Background.png",
            TransparentButton = false,
            ApplyTint = true,
            Tint = new Color(0.7f, 0.25f, 1f, 1f),

            IconPath = "ultradroid-demo-icon.png",
            IconWidth = 42f,
            IconHeight = 42f,
            IconPosition = Vector2.zero,
            IconPreserveAspect = true,

            LabelFontSize = 12f,
            LabelColor = Color.white,
            LabelAlignment = ButtonLabelAlignment.MiddleCenter,
            LabelPosition = new Vector2(0f, -35f),

            RoundedCorners = true,
            CornerRadius = 12f,

            CooldownDuration = 5f,
            ShowCooldownOverlay = true,

            OnClick = () =>
            {
                if (effectsConfig.IsOnCooldown)
                {
                    effectsConfig.CancelCooldown();
                    Log.LogInfo("[Effects] Cooldown cancelled.");
                }
                else
                {
                    effectsConfig.StartCooldown(5f);
                    Log.LogInfo("[Effects] Started 5-second cooldown.");
                }

                effectsConfig.Pulse();
                effectsConfig.Flash();

                Log.LogInfo("[Effects] Pulse and flash requested.");
            },

            OnCooldownComplete = () =>
                Log.LogInfo("[Effects] Cooldown completed.")
        };

        ULTRADROIDControls.AddButton(effectsConfig);
    }

    private void RegisterVisualsButton()
    {
        ULTRADROIDControls.AddButton(new ButtonConfig
        {
            Id = "ULTRADROID.Example.Visuals",
            Label = "VISUALS",

            Width = 150f,
            Height = 55f,
            PositionOffset = new Vector2(240f, 100f),
            ScaleMultiplier = 1f,

            BackgroundPath = "Background.png",
            TransparentButton = false,
            ApplyTint = true,
            Tint = new Color(0.1f, 0.8f, 0.45f, 1f),

            IconPath = "ultradroid-demo-icon.png",
            IconWidth = 42f,
            IconHeight = 42f,
            IconPosition = Vector2.zero,
            IconPreserveAspect = true,
            IconOpacity = 0.65f,

            LabelFontSize = 12f,
            LabelColor = Color.yellow,
            LabelAlignment = ButtonLabelAlignment.MiddleCenter,
            LabelPosition = new Vector2(0f, -35f),
            LabelOpacity = 0.8f,

            RoundedCorners = true,
            CornerRadius = 20f,
            Rotation = 5f,

            ToggleMode = true,

            ToggledOnIconPath = "ultradroid-demo-icon.png",
            ToggledOffIconPath = "ultradroid-demo-icon.png",

            ApplyToggledOnTint = true,
            ToggledOnTint = Color.yellow,

            ApplyToggledOffTint = true,
            ToggledOffTint = Color.white,

            OnClick = () =>
                Log.LogInfo("[Visuals] Clicked."),

            OnToggle = enabled =>
                Log.LogInfo(
                    "[Visuals] Toggle state: " + enabled
                )
        });
    }
}

}
