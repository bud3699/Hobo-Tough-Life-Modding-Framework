# Deep Dive: SkinnedMeshRenderer Overrides in v1.1

## Overview
The v1.1 architecture for replacing `SkinnedMeshRenderers` relies strictly on swapping the geometric mesh data (`renderer.sharedMesh`) *without* modifying the underlying `Transform` hierarchy or the `bones` weight array. 

By replacing the geometry while leaving the original game's bone structure intact, the substitute mesh perfectly rides the animations of the original character structure. This requires the substitute mesh to have been authored for that exact skeleton (which makes "Internal-to-Internal" swapping easy but custom avatar injection extremely difficult).

---

## Step-by-Step Execution Pipeline

### 1. Registration (`ModelOverrideRegistry.RegisterBundleOverride`)
When a mod wants to replace a skinned mesh, they invoke:
```csharp
ModelOverrideRegistry.RegisterBundleOverride("target_mesh_name", "mybundle", "replacement_asset", "skinned", false);
```
- This adds the override to a pending queue (`_pendingBundleOverrides`) mapping the original mesh name to the instructions on how to load the replacement.

### 2. Loading & Extraction (`AssetBundleLoader.GetSkinnedMeshObject`)
Before the game finishes rendering a frame or loading a chunk, the framework executes `ProcessPendingBundleOverrides()`:
1. It opens the AssetBundle.
2. It uses `bundle.LoadAsset<GameObject>(assetName)` to load the full character prefab from the bundle.
3. It calls `GetComponentsInChildren<SkinnedMeshRenderer>()` on the loaded prefab.
4. It extracts both `smr.sharedMesh` (the geometry) and `smr.sharedMaterial` (the texture data).
5. These raw Unity objects are then cached in memory in two internal dictionaries: `_overrides` and `_materials`.

### 3. Execution (The Application Phase)
The actual swapping is performed in two places depending on what the game is currently doing.
*   **Targeted Chunk Loading:** `ApplyOverridesToScene(Scene scene)` - Triggers when the player walks into a new part of the map. It scans only the newly loaded `scene.GetRootGameObjects()`.
*   **Global Fallback:** `ApplyAllOverrides()` - Scans everything in `UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>()`.

For every `SkinnedMeshRenderer` found in the scan, the logic is identical:

#### A. Name Matching
If the renderer's `sharedMesh.name` perfectly matches a key in our `_overrides` dictionary, the replacement sequence begins.

#### B. Instance Cloning (Memory Safety)
```csharp
var meshInstance = UnityEngine.Object.Instantiate(replacement);
```
**CRITICAL:** The framework never applies the cached replacement mesh directly to the game character. Because `sharedMesh` modifies a global singleton in Unity, repeatedly warping it would slowly distort the mesh beyond recognition. Instead, it creates an isolated `Instantiate()` clone for that specific character.

#### C. Bounds Conformation (Auto-Match)
```csharp
AutoMatchMesh(meshInstance, originalBounds);
```
The framework physically measures the 3D bounding box (height, width, depth) of the original character. It then scales ancend ters all vertices inside the cloned replacement mesh so it fits perfectly inside the exact same spatial bounds. This ensures scaling issues from Blender or Unity export never cause a character to become a giant.

#### D. The Clean Swap
```csharp
renderer.sharedMesh = meshInstance;
```
The framework simply re-assigns the pointer. Because it never touched the `renderer.bones` array or the `Animator` component, the new geometry instantly inherits all the walking, talking, and waving animations the original mesh possessed.

#### E. Texture Injection (Shader Preservation)
```csharp
var newMat = new Material(renderer.sharedMaterial);
newMat.mainTexture = replacementTexture;
renderer.material = newMat;
```
If the mod provided custom textures, the framework does **not** override the whole material. Instead, it clones the game's original Hobo material (which contains the custom shaders for rain, lighting, and dirt) and slips the mod's texture inside of it.

---

## Why "Internal-to-Internal" Is the Safest Baseline Test
Since both Majsner and the Player Model were created by the original game developers:
1. They exist within the same Unity coordinate scale.
2. They possess the exact same human bone hierarchy (`Spine`, `Neck`, `Head`, `Clavicle`, etc.).
3. Our pipeline only swaps the `sharedMesh` property.

By swapping Majsner for the Player, we confirm that our scene traversal, dictionary lookups, Auto-Match bounds scaling, and `sharedMesh` pointer re-assignments are working flawlessly before we ever introduce the chaos of external third-party skeletons and bone re-parenting.

---

## Modder Capabilities: External Asset Support

If modders do **not** want to use internal game assets, what can they import? The v1.1 framework supports three types of external assets right now:

### 1. Unity AssetBundles (Highly Recommended)
*   **API:** `RegisterBundleOverride(target, bundleFile, assetName, "skinned", false)`
*   **What it is:** A compiled Unity `.bundle` file created via the Unity Editor.
*   **What it supports:** **Everything.** Meshes, materials, shaders, textures, and most importantly: **Bone weights, bind poses, and bone structures.**
*   **Caveat:** The replacement character must currently be rigged to the *exact same skeleton hierarchy* as the game's internal characters, because our pipeline only swaps the geometry over the existing bones.

### 2. Standalone GLB/GLTF Models (.glb)
*   **API:** `RegisterOverride(target, "custom_model.glb")`
*   **What it is:** A 3D model exported directly from Blender. Loaded dynamically at runtime by our `MeshLoader.cs`.
*   **What it supports:** Geometry (`vertices`, `normals`, `uvs`, `triangles`) and basic Textures/Materials.
*   **Caveat:** Our custom `MeshLoader.cs` GLB parser **does not read bone weights (`JOINTS_0`, `WEIGHTS_0`).** If a modder tries to swap a `.glb` onto a character, it will act like a static statue because the geometry has no instructions on how to bend with the game's invisible skeleton.

### 3. Standalone OBJ Models (.obj)
*   **API:** `RegisterOverride(target, "custom_model.obj")`
*   **What it is:** Legacy 3D model format. Loaded dynamically at runtime.
*   **What it supports:** Pure static geometry. 
*   **Caveat:** `OBJ` format physically cannot store bone weights. It will never animate.

### 4. Avatar Retargeting (Full Character Replacement) - NEW in v1.1
To allow modders to import an entirely custom character with *custom bones* (like importing Master Chief or a dog over a human), the framework now supports **Full Avatar Injection**.

**Step-by-Step Modder Guide for Avatar Replacement:**

**1. Prepare the Model in Unity**
1. Open a Unity project. **CRITICAL: You MUST use Unity version `2020.3.35f1`** (the exact version the game uses).
2. Import your custom 3D model (e.g., a `.fbx` file).
3. In the Unity Import Settings for the model, go to the **Rig** tab and change **Animation Type** to **Humanoid**. 
4. Click Apply. Unity will automatically scan the bones and create an `Avatar` map.

**2. Create the Prefab**
1. Drag the model into the scene.
2. Ensure its scale, rotation, and materials look correct.
3. Drag it down into the Project window to save it as a "Prefab".

**3. Build the AssetBundle**
1. Assign the Prefab to an AssetBundle (e.g., `my_character.bundle`).
2. Build the bundle using standard Unity AssetBundle build scripts for Windows.

**4. Write the Mod Code**
In your mod's C# entry point, register the override pointing to the mesh you want to replace, the path to your bundle, and the name of your Prefab:

```csharp
// Example: Replacing Majsner with your custom character
AvatarInjectionPatches.RegisterAvatarOverride(
    "Hobo_Majsner_Mesh",                      // The vanilla mesh to replace
    Path.Combine(mod.FolderPath, "my_character.bundle"), // Path to your bundle
    "MyCustomCharacterPrefab"                 // The name of the Prefab from Step 2
);
```

**How it works:** The framework intercepts the NPC when it spawns, hides the vanilla body, instantiates your custom Prefab, and reroutes the vanilla game's `Animator` to look at your custom Unity `Avatar`. All vanilla animations (walking, sitting, shivering) will automatically play perfectly on your custom skeleton.
