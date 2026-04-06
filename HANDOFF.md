# HoboModFramework — Agent Handoff (Deep Technical Context)

## Current Status
**Phase 5: Avatar Retargeting / Skinned Mesh Replacement**
Currently in the **Verification / User Testing** stage. We have fixed the major shader/visibility bugs, but the **T-Pose animation bug remains unresolved**. The next agent must focus exclusively on why animations are not playing on the injected Avatar.

## The Goal
To allow modders to inject custom 3D character models (Avatars) into the IL2CPP game *Hobo: Tough Life*, completely replacing vanilla NPC models while retaining the vanilla game's animations, fading logic, and weather shaders.

## System Architecture (How It Works)
1. **The Hook:** We patch `Game.AI.NPCModelBehavior.OnModelLoaded` via Harmony. When an NPC spawns and their native SkinnedMeshRenderer (SMR) is populated, our postfix fires.
2. **Registry Matching:** `AvatarInjectionPatches.cs` checks the native mesh name. Since Hobo uses LODs (e.g., `M_Majsner_LOD0`), we strip the LOD suffix so overrides are agnostic to spawn distance.
3. **The Core Swap (`ApplyBundleOverride`):**
   - We **DO NOT disable** the vanilla SMR. Doing so breaks the game's proprietary fading out/in logic and renderer tracking.
   - Instead, we **hide** the vanilla mesh by assigning a new, empty `Mesh()` to `sharedMesh`.
   - We instantiate the custom prefab.
   - We swap the `Animator.avatar` of the NPC's root Animator with the `avatar` from the custom prefab, then call `Animator.Rebind()`.

## Recently Solved Critical Bugs

### BUG 1: Invisibility & Missing Fading/Weather (Solved via Material Stealing + IL2CPP MonoBehaviour)
- **Issue:** Custom meshes instantiated at runtime were visible but didn't receive the game's fading/depth/weather effects, because the game modifies a `MaterialPropertyBlock` exclusively on the original vanilla SMR.
- **Fix Part 1 (Material Stealing):** We clone the vanilla `Material` (e.g., `RP_DitherLit`) and assign it to the custom SMRs, injecting the custom texture back into it. This puts our mesh in the game's native render pipeline.
- **Fix Part 2 (`PropertyBlockSyncer.cs`):** To copy dynamic effects (like fading alpha) frame-by-frame, we created a custom `MonoBehaviour` that reads the property block from the hidden vanilla SMR and writes it to the custom SMRs in `LateUpdate`.
- **IL2CPP Nuance:** Simply using `AddComponent<PropertyBlockSyncer>()` crashed the game. BepInEx IL2CPP requires the class to be registered first. We fixed this in `Plugin.cs` via `ClassInjector.RegisterTypeInIl2Cpp<PropertyBlockSyncer>()`.

### BUG 2: Black/Missing Textures (Solved via _BaseMap)
- **Issue:** When doing "Material Stealing", setting `stolenMat.mainTexture = customTexture` resulted in black models in-game.
- **Root Cause Verified in Ghidra/AssetRipper:** Hobo uses a Universal Render Pipeline (URP) derived shader called `Render Pipeline/DitherLit`. It does not use Unity's default `_MainTex` property for the albedo; it explicitly uses `_BaseMap`.
- **Fix:** We updated Step 6 in `AvatarInjectionPatches.cs` to explicitly read/write the `_BaseMap` property via `stolenMat.SetTexture("_BaseMap", texture)`.

## 🚨 UNRESOLVED BLOCKER: The T-Pose Bug 🚨

### The Issue
The custom Avatar instantiates and textures correctly, but it spawns in a continuous T-Pose. No animations are mapped or playing.

### What We've Tried So Far (Failed Hypothesis)
- **Hypothesis:** We thought the path hierarchy was wrong. The native game calls `Animator.Rebind()` on `npcRoot`. Originally, we instantiated our prefab *as a child* of `npcRoot` (e.g., `npcRoot/CustomAvatar(Clone)/Armature/Hips`). We thought that extracting the children and reparenting them directly to `npcRoot` would align the bone paths and fix the issue.
- **Result:** The user tested this reparenting logic, and the T-Pose **remains**. That means the issue is NOT merely an offset hierarchy path.

### Next Agent Investigation Vectors for the T-Pose 
The next agent must figure out why `Animator.Rebind()` is failing or if the vanilla `RuntimeAnimatorController` is being stripped/overwritten. Potential vectors to investigate:
1. **Is the Controller Null?** When we call `originalAnimator.avatar = customAvatar`, does Unity wipe the `runtimeAnimatorController`? We attempt to restore it, but maybe it's not actually binding.
2. **Bone Naming Matching:** Does the `Avatar` inside the custom AssetBundle actually have human-mapped bones that correspond to the standard Unity Humanoid mapping, or are the Hobo native animations generic/legacy instead of humanoid?
3. **Are Native Animations Generic or Humanoid?** If Hobo relies on `AnimationType.Generic` rather than `AnimationType.Humanoid`, then replacing the `Avatar` will break everything, as Generic avatars require exact string matching of bone names, whereas Humanoid retargets internally. You must confirm the animation type used by Hobo.
4. **Is `Rebind()` sufficient?** Do we need to also disable and re-enable the Animator, or call `Update(0)` to force the state machine to evaluate the new avatar?
5. **Runtime Override:** Does the game have a native component (like `NPCModelBehavior`) that manually scales/moves bones in `LateUpdate` or `OnAnimatorIK`, conflicting with our injected bones?

## Current Files to Review
- `Framework/AvatarInjectionPatches.cs`: The core logic that hides the mesh, swaps the avatar, and performs Material Stealing.
- **Ghidra Export:** Read `Game.AI.NPCModelBehavior.c` to see exactly how the game manages bones and IK.

## Where the User Is Right Now
The user tested the latest build and reported the T-Pose is still present. They have requested this handoff document so a fresh agent can focus solely on solving the T-Pose mystery.
