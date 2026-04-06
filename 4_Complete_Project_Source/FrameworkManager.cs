using BepInEx.Logging;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game;
using HoboModPlugin.Framework.Events;
using HoboModPlugin.Framework.Addressables;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Main framework manager - coordinates all framework components
    /// </summary>
    public class FrameworkManager
    {
        private readonly ManualLogSource _log;
        private readonly string _pluginPath;
        
        public ModLoader ModLoader { get; private set; }
        public ItemRegistry ItemRegistry { get; private set; }
        public RecipeRegistry RecipeRegistry { get; private set; }
        public EffectHandler EffectHandler { get; private set; }
        public QuestRegistry QuestRegistry { get; private set; }
        
        private bool _initialized = false;
        
        public FrameworkManager(ManualLogSource log, string pluginPath)
        {
            _log = log;
            _pluginPath = pluginPath;
            
            // Initialize components
            ModLoader = new ModLoader(log, pluginPath);
            ItemRegistry = new ItemRegistry(log);
            RecipeRegistry = new RecipeRegistry(log, ItemRegistry);
            EffectHandler = new EffectHandler(log, ItemRegistry);
            QuestRegistry = new QuestRegistry(log);
            
            // Initialize patches with references
            FrameworkPatches.Initialize(log, ItemRegistry, EffectHandler, RecipeRegistry);
            FrameworkUtils.Initialize(log);
            
            // === Phase 1: Initialize Event System ===
            ModEvents.Initialize(log);
            EventHooks.Initialize(log);
            
            // === Initialize Asset Override System ===
            AssetOverrideRegistry.Initialize(log);
            AssetLoadPatches.Initialize(log);  // Phase 1: Resources.Load
            AddressablePatches.Initialize(log);  // Phase 2: Addressables
            GUIDatabasePatches.Initialize(log);  // Phase 3: GUIDatabase sprites
            
            // === Initialize Model Override System ===
            ModelOverrideRegistry.Initialize(log);
            
            // === Initialize AssetBundle Loading System ===
            AssetBundleLoader.Initialize(log);
            
            // === Initialize Shader Remapping System ===
            ShaderRemapper.Initialize(log);
            
            // === Initialize Custom Object Spawning System ===
            CustomObjectRegistry.Initialize(log);
            
            // === Initialize Static Object Placement System ===
            StaticObjectManager.Initialize(log);
            
            // === Scene Load Hook ===
            // Fires when any scene loads (Main, Graphics, Game, etc.)
            // Used to apply model overrides at the right time
            SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
            _log.LogInfo("[Framework] Scene-load hook registered for model overrides");
        }
        
        /// <summary>
        /// Discover all mods (call early on startup)
        /// </summary>
        public void DiscoverMods()
        {
            _log.LogInfo("=== HoboModFramework: Discovering Mods ===");
            ModLoader.DiscoverMods();
            
            // Load content definitions from each mod
            foreach (var mod in ModLoader.LoadedMods)
            {
                ItemRegistry.LoadItemsFromMod(mod);
                RecipeRegistry.LoadRecipesFromMod(mod);
                QuestRegistry.LoadQuestsFromMod(mod);
                LoadAssetOverridesFromMod(mod);
                LoadModelOverridesFromMod(mod);
                LoadCustomObjectsFromMod(mod);
                StaticObjectManager.LoadPlacementsFromMod(mod.FolderPath, mod.Id);
            }
            
            // === Setup mesh interception ===
            // Process pending bundle overrides first (loads replacement meshes)
            // Then install the Harmony patch for future mesh loads
            ModelOverrideRegistry.ProcessPendingBundleOverrides();
            ModelOverrideRegistry.SetupMeshLoadInterception();
        }
        
        /// <summary>
        /// Load and register asset overrides from a mod
        /// Supports three types:
        /// 1. Path-based (original + replacement) - Resources.Load / Addressables
        /// 2. Item sprite by ID (itemId + replacement) - GUIDatabase.spriteItemsCollection
        /// 3. Named sprite field (spriteField + replacement) - GUIDatabase fields
        /// </summary>
        private void LoadAssetOverridesFromMod(ModManifest mod)
        {
            if (mod.AssetOverrides == null || mod.AssetOverrides.Count == 0) return;
            
            _log.LogInfo($"  Loading {mod.AssetOverrides.Count} asset override(s) from {mod.Name}");
            
            foreach (var ov in mod.AssetOverrides)
            {
                if (string.IsNullOrEmpty(ov.Replacement))
                    continue;
                
                var modFilePath = System.IO.Path.Combine(mod.FolderPath, ov.Replacement);
                
                // Security check
                if (!IsPathSafe(mod.FolderPath, modFilePath))
                {
                   _log.LogWarning($"    Security Check Failed: Asset path {ov.Replacement} attempts to traverse outside mod folder!");
                   continue;
                }
                
                if (!System.IO.File.Exists(modFilePath))
                {
                    _log.LogWarning($"    Override file not found: {modFilePath}");
                    continue;
                }
                
                // Type 1: Path-based override (Resources.Load / Addressables)
                if (!string.IsNullOrEmpty(ov.Original))
                {
                    AssetOverrideRegistry.RegisterOverride(ov.Original, modFilePath);
                    _log.LogInfo($"    Registered path override: {ov.Original}");
                }
                // Type 2: Item sprite by ID (GUIDatabase)
                else if (ov.ItemId.HasValue)
                {
                    var sprite = LoadSpriteFromFile(modFilePath);
                    if (sprite != null)
                    {
                        GUIDatabasePatches.RegisterSpriteOverride(ov.ItemId.Value, sprite);
                        _log.LogInfo($"    Registered sprite override for item ID: {ov.ItemId.Value}");
                    }
                }
                // Type 3: Named sprite field (GUIDatabase)
                else if (!string.IsNullOrEmpty(ov.SpriteField))
                {
                    var sprite = LoadSpriteFromFile(modFilePath);
                    if (sprite != null)
                    {
                        GUIDatabasePatches.RegisterSpriteOverrideByName(ov.SpriteField, sprite);
                        _log.LogInfo($"    Registered sprite override for field: {ov.SpriteField}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Load and register model (3D mesh) overrides from a mod
        /// </summary>
        private void LoadModelOverridesFromMod(ModManifest mod)
        {
            if (mod.ModelOverrides == null || mod.ModelOverrides.Count == 0) return;
            
            _log.LogInfo($"  Loading {mod.ModelOverrides.Count} model override(s) from {mod.Name}");
            
            foreach (var ov in mod.ModelOverrides)
            {
                if (string.IsNullOrEmpty(ov.Target))
                    continue;
                
                // Check if this is an AssetBundle override
                if (ov.IsBundle)
                {
                    if (string.IsNullOrEmpty(ov.Bundle))
                    {
                        _log.LogWarning($"    Bundle override missing 'bundle' property for target: {ov.Target}");
                        continue;
                    }
                    
                    var bundlePath = System.IO.Path.Combine(mod.FolderPath, ov.Bundle);
                    
                    // Security check
                    if (!IsPathSafe(mod.FolderPath, bundlePath))
                    {
                        _log.LogWarning($"    Security Check Failed: Bundle path {ov.Bundle} attempts to traverse outside mod folder!");
                        continue;
                    }
                    
                    if (!System.IO.File.Exists(bundlePath))
                    {
                        _log.LogWarning($"    Bundle file not found: {bundlePath}");
                        continue;
                    }
                    
                    // Register bundle override with asset info
                    ModelOverrideRegistry.RegisterBundleOverride(
                        ov.Target, 
                        bundlePath, 
                        ov.Asset, 
                        ov.AssetType, 
                        ov.Instantiate,
                        ov.Scale, 
                        ov.RotX, 
                        ov.RotY, 
                        ov.RotZ
                    );
                }
                else
                {
                    // Regular file-based override (OBJ, GLB, GLTF)
                    if (string.IsNullOrEmpty(ov.File))
                        continue;
                    
                    var modFilePath = System.IO.Path.Combine(mod.FolderPath, ov.File);
                    
                    // Security check
                    if (!IsPathSafe(mod.FolderPath, modFilePath))
                    {
                        _log.LogWarning($"    Security Check Failed: Model path {ov.File} attempts to traverse outside mod folder!");
                        continue;
                    }
                    
                    if (!System.IO.File.Exists(modFilePath))
                    {
                        _log.LogWarning($"    Model file not found: {modFilePath}");
                        continue;
                    }
                    
                    ModelOverrideRegistry.RegisterOverride(ov.Target, modFilePath, ov.Scale, ov.RotX, ov.RotY, ov.RotZ);
                }
            }
        }
        
        /// <summary>
        /// Load and register custom objects from a mod (spawn new objects, not replace)
        /// </summary>
        private void LoadCustomObjectsFromMod(ModManifest mod)
        {
            if (mod.CustomObjects == null || mod.CustomObjects.Count == 0) return;
            
            _log.LogInfo($"  Loading {mod.CustomObjects.Count} custom object(s) from {mod.Name}");
            
            foreach (var obj in mod.CustomObjects)
            {
                // Security check for custom object bundles
                if (!string.IsNullOrEmpty(obj.Bundle))
                {
                    var bundlePath = System.IO.Path.Combine(mod.FolderPath, obj.Bundle);
                     if (!IsPathSafe(mod.FolderPath, bundlePath))
                    {
                        _log.LogWarning($"    Security Check Failed: Custom object bundle {obj.Bundle} attempts to traverse outside mod folder!");
                        continue;
                    }
                }
                
                CustomObjectRegistry.RegisterObject(mod.FolderPath, obj);
            }
        }
        
        /// <summary>
        /// Security check to prevent Directory Traversal attacks (e.g. "../../../Windows/System32")
        /// </summary>
        private bool IsPathSafe(string rootPath, string fullPath)
        {
            try
            {
                string fullRoot = System.IO.Path.GetFullPath(rootPath).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                string fullTarget = System.IO.Path.GetFullPath(fullPath);
                
                return fullTarget.StartsWith(fullRoot, System.StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Load a sprite from an image file
        /// </summary>
        private Sprite LoadSpriteFromFile(string filePath)
        {
            try
            {
                var imageData = System.IO.File.ReadAllBytes(filePath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                
                if (ImageConversion.LoadImage(texture, imageData))
                {
                    texture.name = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    sprite.name = texture.name;
                    return sprite;
                }
            }
            catch (System.Exception ex)
            {
                _log.LogError($"    Failed to load sprite from {filePath}: {ex.Message}");
            }
            return null;
        }
        
        /// <summary>
        /// Inject all content into game (call when game databases are ready)
        /// Uses database content check instead of flag to handle re-injection on world changes
        /// </summary>
        public void InjectContent()
        {
            // FIX: Check if content actually exists in databases instead of using static flag
            // This handles re-injection when databases are reloaded (e.g., switching worlds)
            try
            {
                var recipes = RecipeDatabase.recipes;
                if (recipes != null && recipes.ContainsKey(51000))
                {
                    _log.LogInfo("=== HoboModFramework: Content already exists in database, skipping ===");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                _log.LogWarning($"Could not check database state: {ex.Message}");
            }
            
            _log.LogInfo("=== HoboModFramework: Injecting Content (Items/Recipes) ===");
            ItemRegistry.InjectAllItems();
            RecipeRegistry.InjectAllRecipes();
            _log.LogInfo("=== HoboModFramework: Content Injection Complete ===");
        }
        
        /// <summary>
        /// Clear all registries and reload content from mod files.
        /// Called on game load to ensure fresh state and prevent duplicates.
        /// </summary>
        public void ClearAndReloadContent()
        {
            _log.LogInfo("=== HoboModFramework: Clearing registries for fresh load ===");
            
            // Clear all registries
            ItemRegistry.Clear();
            RecipeRegistry.Clear();
            QuestRegistry.Clear();
            
            // Re-load definitions from mod files
            foreach (var mod in ModLoader.LoadedMods)
            {
                ItemRegistry.LoadItemsFromMod(mod);
                RecipeRegistry.LoadRecipesFromMod(mod);
                QuestRegistry.LoadQuestsFromMod(mod);
            }
            
            _log.LogInfo("=== HoboModFramework: Registries cleared and reloaded ===");
        }

        public void InjectQuests()
        {
            _log.LogInfo("=== HoboModFramework: Injecting Quests ===");
            QuestRegistry.InjectAllQuests();
        }
        
        /// <summary>
        /// Unlock pending recipes for player (call when crafting UI opens)
        /// </summary>
        public void UnlockRecipes()
        {
            var characters = Object.FindObjectsOfType<Character>();
            if (characters != null && characters.Length > 0)
            {
                RecipeRegistry.UnlockPendingRecipes(characters[0]);
            }
        }
        
        public bool IsInitialized => _initialized;
        
        /// <summary>
        /// Called when any scene finishes loading.
        /// Applies model overrides using two strategies:
        ///   1. FindAndReplaceLoadedMeshes — scans all meshes in memory (for batched props)
        ///   2. ApplyAllOverrides — scans scene MeshFilters/SkinnedMeshRenderers (for non-batched)
        /// Skips the preloader scene.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Skip preloader — no game content there
            if (scene.name == "SHPreloader") return;
            
            _log.LogInfo($"[Framework] Scene loaded: {scene.name} — applying model overrides...");
            
            // Prong 2: Scan all loaded meshes in memory (catches batched props)
            ModelOverrideRegistry.FindAndReplaceLoadedMeshes();
            
            // Original approach: scan scene hierarchy (catches characters and non-batched objects)
            ModelOverrideRegistry.ApplyAllOverrides();
        }
    }
}
