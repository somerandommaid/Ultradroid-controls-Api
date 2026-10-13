# ULTRADROID Controls API

A reusable API for ULTRAKILL IL2CPP mods that need custom mobile HUD buttons.

## Status

**Starter / experimental.** The original ULTRADROID prototype proved that a cloned `OnScreenButton` can appear, be moved by the native mobile layout editor, respond to touches, and follow the global scale slider. This repository separates that integration into a reusable API and includes a consumer example. The new framework version has not yet been compiled or tested in-game.

## Repository structure

- `src/ULTRADROID.API/` — public `ButtonConfig`, `ButtonHandle`, and `ULTRADROIDControls.AddButton()` API.
- `src/ULTRADROID.Core/` — BepInEx IL2CPP plugin that creates controls and integrates them with `TouchManager`.
- `examples/ExampleMod/` — minimal mod that adds a demo button using only the public API.
- `lib/` — expected build-time reference assemblies; not bundled with this source package.

## API example

```csharp
using ULTRADROID.API;
using UnityEngine;

ButtonHandle handle = ULTRADROIDControls.AddButton(new ButtonConfig
{
    Id = "MyMod.Dash", // unique and stable; used by saved layouts
    Label = "DASH",
    PositionOffset = new Vector2(120f, 90f),
    Tint = new Color(0.2f, 0.85f, 1f, 0.95f),
    ScaleMultiplier = 1f,
    OnClick = () => ActivateDash()
});
```

`AddButton()` registers a request. `ULTRADROID.Core` creates it when the game's `TouchManager` is ready. `handle.IsReady` becomes true once its Unity object exists. Calling `handle.Dispose()` requests removal.

### Important behavior

- `Id` must be unique across every mod that uses ULTRADROID. Use a prefix such as `MyMod.Dash`.
- `OnClick` runs on the Unity UI click event (typically pointer release), not on pointer-down. Visual tinting is off by default so native colors are preserved unless `ApplyTint = true`.
- `Icon` accepts a Unity `Sprite`; the calling mod must load/create that sprite.
- `PositionOffset` is relative to the cloned native button's starting position.
- Keep ULTRADROID installed as a framework dependency; consumer mods should reference `ULTRADROID.API.dll` while compiling and should not ship a duplicate copy alongside each mod.
- Custom button creation and layout persistence need to be verified on the exact Android ULTRAKILL build.

## Build references

The code targets `net10.0` to match the framework/runtime reference versions used by the working prototype. The build expects these exact reference files in `lib/`:

- `BepInEx.Core.dll`
- `BepInEx.Unity.IL2CPP.dll`
- `Il2CppInterop.Runtime.dll`
- `Il2Cppmscorlib.dll`
- `Assembly-CSharp.dll` from the matching ULTRAKILL version
- `Unity.InputSystem.dll`
- `UnityEngine.CoreModule.dll`
- `UnityEngine.IMGUIModule.dll`
- `UnityEngine.UI.dll`
- `UnityEngine.TextRenderingModule.dll`

They are not bundled or copied to the release artifact. The uploaded GitHub Actions workflow intentionally reports missing reference assemblies clearly instead of silently building against mismatched versions. Use the matching, legally obtained local/runtime references and configure a private source for CI rather than committing proprietary game/runtime DLLs to a public repository.

## First test checklist

1. Confirm both `ULTRADROID.API.dll` and `ULTRADROID.Core.dll` build successfully.
2. Install those two DLLs in one ULTRADROID plugin folder.
3. Install `ULTRADROID.ExampleMod.dll` separately without making a duplicate API assembly.
4. Confirm the DEMO button appears, moves/resizes in the native editor, and logs the callback.
5. Save the layout, restart the game, and check whether its position and size persist.

## License

The original source in this repository is MIT licensed. This license does not apply to BepInEx, Il2CppInterop, Unity, or ULTRAKILL reference assemblies.
