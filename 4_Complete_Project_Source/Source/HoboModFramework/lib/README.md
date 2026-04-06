# Library References

This folder should contain DLLs needed to build the mod. These are NOT included in the repository because they are copyrighted.

## Required DLLs

### From BepInEx 6 IL2CPP
Download from: https://builds.bepinex.dev/projects/bepinex_be

After extracting BepInEx to your game folder, copy from `[Game]\BepInEx\core\`:
- `BepInEx.Core.dll`
- `BepInEx.Unity.IL2CPP.dll`
- `0Harmony.dll`
- `Il2CppInterop.Runtime.dll`

### From Game Installation
Copy from `[Game]\BepInEx\interop\`:
- `Assembly-CSharp.dll`
- `Il2Cppmscorlib.dll`
- `Il2CppSystem.dll`
- `Il2CppSystem.Core.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `UnityEngine.InputLegacyModule.dll`
- `UnityEngine.ImageConversionModule.dll`
- `UnityEngine.PhysicsModule.dll`
- `UnityEngine.UI.dll`
- `Unity.TextMeshPro.dll`

## Quick Setup

1. Install BepInEx 6 IL2CPP to your game folder
2. Run the game once (generates interop assemblies)
3. Copy the DLLs listed above into this `lib/` folder
4. Build with `dotnet build --configuration Release`
