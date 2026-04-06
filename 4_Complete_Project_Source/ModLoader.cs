using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Discovers and loads mod folders from HoboMods directory
    /// </summary>
    public class ModLoader
    {
        private readonly ManualLogSource _log;
        private readonly string _modsPath;
        private readonly List<ModManifest> _loadedMods = new();
        
        public IReadOnlyList<ModManifest> LoadedMods => _loadedMods;
        public string ModsPath => _modsPath;
        
        /// <summary>
        /// Check if a mod with the given ID is loaded
        /// </summary>
        public bool IsModLoaded(string modId)
        {
            return _loadedMods.Any(m => m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase));
        }
        
        public ModLoader(ManualLogSource log, string pluginPath)
        {
            _log = log;
            string pluginDir = Path.GetDirectoryName(pluginPath) ?? "";
            
            // If the plugin is inside HoboMods folder, use that as root.
            // Otherwise assumes it's in plugins/ root and appends HoboMods.
            if (Path.GetFileName(pluginDir).Equals("HoboMods", StringComparison.OrdinalIgnoreCase))
            {
                _modsPath = pluginDir;
            }
            else
            {
                _modsPath = Path.Combine(pluginDir, "HoboMods");
            }
        }
        
        /// <summary>
        /// Discover and load all mods from HoboMods folder
        /// </summary>
        public void DiscoverMods()
        {
            _log.LogInfo($"=== ModLoader: Discovering mods in {_modsPath} ===");
            
            if (!Directory.Exists(_modsPath))
            {
                Directory.CreateDirectory(_modsPath);
                _log.LogInfo("  Created HoboMods folder");
                CreateExampleMod();
                return;
            }
            
            var modFolders = Directory.GetDirectories(_modsPath);
            _log.LogInfo($"  Found {modFolders.Length} mod folder(s)");
            
            foreach (var modFolder in modFolders)
            {
                LoadMod(modFolder);
            }
            
            _log.LogInfo($"=== ModLoader: {_loadedMods.Count} mod(s) loaded ===");
        }
        
        private void LoadMod(string modFolder)
        {
            var modJsonPath = Path.Combine(modFolder, "mod.json");
            
            if (!File.Exists(modJsonPath))
            {
                // No mod.json at root — treat as a mod pack folder.
                // Scan sub-directories for individual mods (e.g., ShowcaseMod/ModelReplacement/mod.json)
                var subFolders = Directory.GetDirectories(modFolder);
                if (subFolders.Length > 0)
                {
                    _log.LogInfo($"  {Path.GetFileName(modFolder)} has no mod.json — scanning {subFolders.Length} sub-folder(s)");
                    foreach (var subFolder in subFolders)
                    {
                        LoadMod(subFolder);
                    }
                }
                else
                {
                    _log.LogWarning($"  Skipping {Path.GetFileName(modFolder)}: no mod.json");
                }
                return;
            }
            
            try
            {
                var json = File.ReadAllText(modJsonPath);
                var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
                
                if (manifest == null)
                {
                    _log.LogWarning($"  Failed to parse mod.json in {Path.GetFileName(modFolder)}");
                    return;
                }
                
                manifest.FolderPath = modFolder;
                
                // Prevent duplicate mod IDs
                if (_loadedMods.Any(m => m.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _log.LogWarning($"  Skipping duplicate mod ID: {manifest.Id}");
                    return;
                }
                
                _loadedMods.Add(manifest);
                
                _log.LogInfo($"  Loaded: {manifest.Name} v{manifest.Version} by {manifest.Author}");
            }
            catch (Exception ex)
            {
                _log.LogError($"  Error loading {Path.GetFileName(modFolder)}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create example mod structure for new users
        /// </summary>
        private void CreateExampleMod()
        {
            var examplePath = Path.Combine(_modsPath, "_ExampleMod");
            Directory.CreateDirectory(examplePath);
            Directory.CreateDirectory(Path.Combine(examplePath, "items"));
            Directory.CreateDirectory(Path.Combine(examplePath, "recipes"));
            Directory.CreateDirectory(Path.Combine(examplePath, "assets", "icons"));
            Directory.CreateDirectory(Path.Combine(examplePath, "localization"));
            
            // Create example mod.json
            var manifest = new ModManifest
            {
                Id = "example_mod",
                Name = "Example Mod",
                Version = "1.0.0",
                Author = "YourName",
                Description = "An example mod to get you started",
                GameVersion = "1.0"
            };
            
            File.WriteAllText(
                Path.Combine(examplePath, "mod.json"),
                JsonConvert.SerializeObject(manifest, Formatting.Indented)
            );
            
            // Create example item
            var exampleItem = new ItemDefinition
            {
                Id = "example_potion",
                BaseItem = 1,
                Type = "consumable",
                Name = "@example_potion.name",
                Description = "@example_potion.desc",
                Icon = "assets/icons/example_potion.png",
                Price = 100,
                Effects = new List<EffectDefinition>
                {
                    new() { Stat = "health", Value = "50" },
                    new() { Stat = "food", Value = "25" }
                }
            };
            
            File.WriteAllText(
                Path.Combine(examplePath, "items", "example_potion.json"),
                JsonConvert.SerializeObject(exampleItem, Formatting.Indented)
            );
            
            // Create example localization
            var localization = new Dictionary<string, string>
            {
                ["example_potion.name"] = "Example Potion",
                ["example_potion.desc"] = "A simple healing potion"
            };
            
            File.WriteAllText(
                Path.Combine(examplePath, "localization", "en.json"),
                JsonConvert.SerializeObject(localization, Formatting.Indented)
            );
            
            _log.LogInfo("  Created _ExampleMod template");
        }
    }
    
    /// <summary>
    /// Mod manifest from mod.json
    /// </summary>
    public class ModManifest
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";
        
        [JsonProperty("name")]
        public string Name { get; set; } = "";
        
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0.0";
        
        [JsonProperty("author")]
        public string Author { get; set; } = "Unknown";
        
        [JsonProperty("description")]
        public string Description { get; set; } = "";
        
        [JsonProperty("gameVersion")]
        public string GameVersion { get; set; } = "";
        
        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; } = new();
        
        // Runtime
        [JsonIgnore]
        public string FolderPath { get; set; } = "";
        
        // Cached localization dictionary (prevents reading file every time)
        [JsonIgnore]
        public Dictionary<string, string> LocalizationCache { get; set; } = null;
        
        // Dev hotkeys defined in mod.json
        [JsonProperty("devHotkeys")]
        public List<DevHotkeyDefinition> DevHotkeys { get; set; } = new();
        
        // Dialogue triggers - start quests when talking to NPCs
        [JsonProperty("dialogueTriggers")]
        public List<DialogueTrigger> DialogueTriggers { get; set; } = new();
        
        // Asset overrides - replace game assets with mod files
        [JsonProperty("assetOverrides")]
        public List<AssetOverrideDefinition> AssetOverrides { get; set; } = new();
        
        // Model overrides - replace 3D meshes with custom models
        [JsonProperty("modelOverrides")]
        public List<ModelOverrideDefinition> ModelOverrides { get; set; } = new();
        
        // Custom objects - spawn NEW objects from AssetBundles (not replacing existing)
        [JsonProperty("customObjects")]
        public List<CustomObjectDefinition> CustomObjects { get; set; } = new();
    }
    
    /// <summary>
    /// Dialogue trigger definition - starts actions when conversation with NPC ends or option selected
    /// </summary>
    public class DialogueTrigger
    {
        /// <summary>
        /// NPC identifier to match (partial, case-insensitive match)
        /// E.g., "maisner", "beggar", "cop_guard"
        /// </summary>
        [JsonProperty("npcId")]
        public string NpcId { get; set; } = "";
        
        /// <summary>
        /// When to trigger: "option_selected" or "conversation_ended"
        /// Default: "conversation_ended"
        /// </summary>
        [JsonProperty("triggerOn")]
        public string TriggerOn { get; set; } = "conversation_ended";
        
        /// <summary>
        /// Dialogue option text to match (partial, case-insensitive)
        /// Only used when triggerOn = "option_selected"
        /// </summary>
        [JsonProperty("optionText")]
        public string OptionText { get; set; } = "";
        
        /// <summary>
        /// Dialogue option textKey to match (exact uint match)
        /// For advanced users who know the exact key
        /// </summary>
        [JsonProperty("optionTextKey")]
        public uint? OptionTextKey { get; set; } = null;
        
        /// <summary>
        /// Action to perform: "start_quest"
        /// </summary>
        [JsonProperty("action")]
        public string Action { get; set; } = "";
        
        /// <summary>
        /// Quest ID for start_quest action (e.g., "mymod:my_quest")
        /// </summary>
        [JsonProperty("questId")]
        public string QuestId { get; set; } = "";
    }
    
    /// <summary>
    /// Hotkey definition for mod development/testing
    /// Allows mods to define their own hotkeys in mod.json
    /// </summary>
    public class DevHotkeyDefinition
    {
        [JsonProperty("key")]
        public string Key { get; set; } = "";  // "Insert", "F6", etc.
        
        [JsonProperty("action")]
        public string Action { get; set; } = "";  // "spawn_item", "explore_items", etc.
        
        [JsonProperty("itemId")]  
        public string ItemId { get; set; } = "";  // Item ID (optional for some actions)
    }
    
    /// <summary>
    /// Item definition from items/*.json
    /// </summary>
    public class ItemDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";
        
        [JsonProperty("baseItem")]
        public uint BaseItem { get; set; } = 1;
        
        [JsonProperty("type")]
        public string Type { get; set; } = "consumable";
        
        [JsonProperty("name")]
        public string Name { get; set; } = "";
        
        [JsonProperty("description")]
        public string Description { get; set; } = "";
        
        [JsonProperty("icon")]
        public string Icon { get; set; } = "";
        
        [JsonProperty("price")]
        public int? Price { get; set; } = null;
        
        [JsonProperty("weight")]
        public float Weight { get; set; } = 0.1f;
        
        [JsonProperty("effects")]
        public List<EffectDefinition> Effects { get; set; } = new();
        
        // === Phase 2B: Native Effect System ===
        
        [JsonProperty("statChanges")]
        public List<StatChangeDefinition> StatChanges { get; set; } = new();  // Native parameterChanges
        
        [JsonProperty("buffEffects")]
        public List<BuffEffectDefinition> BuffEffects { get; set; } = new();  // Native buffChanges
        
        [JsonProperty("addictionType")]
        public string AddictionType { get; set; } = "";  // NOTHING, Alcohol, Drugs, Cigarettes
        
        // === Extended Item Properties (from DEV) ===
        
        [JsonProperty("rareColor")]
        public int RareColor { get; set; } = 0;  // Item rarity color (0=common)
        
        [JsonProperty("sellable")]
        public bool Sellable { get; set; } = true;  // Can be sold to vendors
        
        [JsonProperty("notForFire")]
        public bool NotForFire { get; set; } = false;  // Cannot be used as fire fuel
        
        [JsonProperty("isStockable")]
        public bool IsStockable { get; set; } = true;  // Can stack in inventory
        
        [JsonProperty("actualStockCount")]
        public int ActualStockCount { get; set; } = 1;  // Default stack size
        
        [JsonProperty("soundType")]
        public int SoundType { get; set; } = 0;  // Sound when used/picked up
        
        [JsonProperty("firerate")]
        public int Firerate { get; set; } = 0;  // Burn duration as fuel
        
        // === Phase 2A Additional Properties ===
        
        [JsonProperty("isForHotSlot")]
        public bool IsForHotSlot { get; set; } = false;  // Can go in hotbar
        
        [JsonProperty("hint")]
        public string Hint { get; set; } = "";  // Category hint (NA, Material, CanBeSold, Equipment, Healing, etc.)
        
        [JsonProperty("isStealable")]
        public bool IsStealable { get; set; } = false;  // Can be stolen by NPCs
        
        [JsonProperty("heavyItem")]
        public bool HeavyItem { get; set; } = false;  // Heavy item flag
        
        [JsonProperty("hardItem")]
        public bool HardItem { get; set; } = false;  // Hard item flag
        
        [JsonProperty("spawnDay")]
        public int SpawnDay { get; set; } = 0;  // Day when item starts appearing in world
        
        [JsonProperty("isPermanent")]
        public bool IsPermanent { get; set; } = false;  // Item is never removed
        
        [JsonProperty("questType")]
        public string QuestType { get; set; } = "";  // Nothing, Private, Shared
        
        [JsonProperty("salvageTableId")]
        public int SalvageTableId { get; set; } = -1;  // Salvage loot table ID
        
        [JsonProperty("buyBackTime")]
        public float BuyBackTime { get; set; } = 0f;  // Shop buyback timer (seconds)
        
        [JsonProperty("subCategory")]
        public string SubCategory { get; set; } = "";  // Default, Nothing, CraftingMaterial, Food, Usable, Companion
        
        // === Additional Modder Properties ===
        
        [JsonProperty("buffetGame")]
        public bool BuffetGame { get; set; } = false;  // Triggers buffet minigame when consumed
        
        [JsonProperty("buffetDifficulty")]
        public int BuffetDifficulty { get; set; } = 0;  // Buffet minigame difficulty level
        
        [JsonProperty("index")]
        public int Index { get; set; } = -1;  // Item ordering in menus (-1 = auto)
        
        [JsonProperty("improRecipes")]
        public List<uint> ImproRecipes { get; set; } = new();  // Linked recipe IDs for this item
        
        // === Reference Item Icon ===
        
        [JsonProperty("referenceItemId")]
        public uint ReferenceItemId { get; set; } = 0;  // Copy icon from existing game item
        
        // === Modify Existing Items ===
        
        [JsonProperty("isModify")]
        public bool IsModify { get; set; } = false;  // Patch existing item instead of creating new
        
        [JsonProperty("targetItemId")]
        public uint TargetItemId { get; set; } = 0;  // ID of vanilla item to modify
        
        // Gear-specific properties
        [JsonProperty("category")]
        public string Category { get; set; } = "";  // "hat", "jacket", "trousers", "shoes"
        
        [JsonProperty("warmResistance")]
        public int WarmResistance { get; set; } = 0;
        
        [JsonProperty("wetResistance")]
        public int WetResistance { get; set; } = 0;
        
        [JsonProperty("durabilityResistance")]
        public int DurabilityResistance { get; set; } = 100;
        
        [JsonProperty("repairRecipe")]
        public uint RepairRecipe { get; set; } = 0;  // Recipe ID for repairing this gear
        
        [JsonProperty("repairCash")]
        public int RepairCash { get; set; } = 0;  // Cash cost to repair at vendors
        
        // Weapon-specific properties
        [JsonProperty("attack")]
        public int Attack { get; set; } = 0;
        
        [JsonProperty("defense")]
        public int Defense { get; set; } = 0;
        
        [JsonProperty("criticalChance")]
        public int CriticalChance { get; set; } = 0;
        
        [JsonProperty("maxDurability")]
        public int MaxDurability { get; set; } = 100;
        
        // Gear stat bonuses (applies when equipped)
        [JsonProperty("stats")]
        public List<StatDefinition> Stats { get; set; } = new();
        
        // Bag-specific properties
        [JsonProperty("bagCapacity")]
        public float BagCapacity { get; set; } = 0f;
    }
    
    /// <summary>
    /// Stat bonus definition for gear items
    /// </summary>
    public class StatDefinition
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "";
        
        [JsonProperty("value")]
        public int Value { get; set; } = 0;
    }
    
    /// <summary>
    /// Effect definition for consumables (legacy, for runtime effects)
    /// </summary>
    public class EffectDefinition
    {
        [JsonProperty("stat")]
        public string Stat { get; set; } = "";
        
        // Alias: Support "type" in JSON as fallback for "stat"
        [JsonProperty("type")]
        public string Type { set => Stat = string.IsNullOrEmpty(Stat) ? value : Stat; }
        
        [JsonProperty("value")]
        public string Value { get; set; } = "0";
    }
    
    /// <summary>
    /// Phase 2B: Stat change for native consumable parameterChanges
    /// </summary>
    public class StatChangeDefinition
    {
        [JsonProperty("stat")]
        public string Stat { get; set; } = "";
        
        [JsonProperty("value")]
        public int Value { get; set; } = 0;  // normalValue
        
        [JsonProperty("addictedValue")]
        public int AddictedValue { get; set; } = 0;  // isAddictedValue
    }
    
    /// <summary>
    /// Phase 2B: Buff effect for native consumable buffChanges
    /// </summary>
    public class BuffEffectDefinition
    {
        [JsonProperty("buff")]
        public string Buff { get; set; } = "";  // CharacterBuff.BuffType
        
        [JsonProperty("tier")]
        public int Tier { get; set; } = 1;  // 0-3
        
        [JsonProperty("addictedTier")]
        public int AddictedTier { get; set; } = 0;
        
        [JsonProperty("addictionType")]
        public string AddictionType { get; set; } = "NOTHING";
    }
    
    /// <summary>
    /// Recipe definition from recipes/*.json
    /// </summary>
    public class RecipeDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";
        
        [JsonProperty("result")]
        public string Result { get; set; } = "";
        
        [JsonProperty("resultCount")]
        public int ResultCount { get; set; } = 1;
        
        [JsonProperty("name")]
        public string Name { get; set; } = "";
        
        [JsonProperty("bench")]
        public string Bench { get; set; } = "none";
        
        [JsonProperty("skillRequired")]
        public int SkillRequired { get; set; } = 0;
        
        [JsonProperty("autoUnlock")]
        public bool AutoUnlock { get; set; } = true;
        
        [JsonProperty("ingredients")]
        public List<IngredientDefinition> Ingredients { get; set; } = new();
        
        // NEW: Recipe type (Structure, Cook, Item, Improvisation, Weapon, ForRepair, ForUpgrade)
        [JsonProperty("type")]
        public string Type { get; set; } = "Item";
        
        // NEW: Crafting difficulty level
        [JsonProperty("craftingDifficulty")]
        public int CraftingDifficulty { get; set; } = 0;
        
        // NEW: Secondary/alternate ingredients (optional)
        [JsonProperty("secondaryIngredients")]
        public List<IngredientDefinition> SecondaryIngredients { get; set; } = new();
        
        // NEW: Recipe disabled/not active
        [JsonProperty("notActive")]
        public bool NotActive { get; set; } = false;  // If true, recipe is disabled
        
        // NEW: Required street knowledge
        [JsonProperty("requireSK")]
        public string RequireSK { get; set; } = "";  // NOTHING, SURVIVAL, BEGGING, etc.
        
        // NEW: Associated bench (for recipes that show on multiple benches)
        [JsonProperty("associatedBench")]
        public string AssociatedBench { get; set; } = "";  // none, normal, kitchen, druglab
    }
    
    /// <summary>
    /// Ingredient for recipes
    /// </summary>
    public class IngredientDefinition
    {
        [JsonProperty("item")]
        public uint Item { get; set; }
        
        [JsonProperty("count")]
        public int Count { get; set; } = 1;
    }
    
    /// <summary>
    /// Asset override definition - replace game assets with mod files
    /// Part of the Universal Asset Override System
    /// 
    /// Supports three override types:
    /// 1. Path-based (original + replacement) - Resources.Load / Addressables
    /// 2. Item sprite by ID (itemId + replacement) - GUIDatabase.spriteItemsCollection
    /// 3. Named sprite field (spriteField + replacement) - GUIDatabase fields
    /// </summary>
    public class AssetOverrideDefinition
    {
        /// <summary>
        /// Original game asset path to override (for Resources.Load / Addressables)
        /// E.g., "Textures/Player/Jacket_Diffuse"
        /// </summary>
        [JsonProperty("original")]
        public string Original { get; set; } = "";
        
        /// <summary>
        /// Path to replacement file relative to mod folder
        /// E.g., "textures/my_jacket.png"
        /// </summary>
        [JsonProperty("replacement")]
        public string Replacement { get; set; } = "";
        
        /// <summary>
        /// Item ID for sprite replacement (GUIDatabase.spriteItemsCollection index)
        /// Use this to replace item icons by their numeric ID
        /// </summary>
        [JsonProperty("itemId")]
        public int? ItemId { get; set; } = null;
        
        /// <summary>
        /// Named sprite field in GUIDatabase to override
        /// E.g., "questIcon_sprite", "parameterSpriteHealth"
        /// </summary>
        [JsonProperty("spriteField")]
        public string SpriteField { get; set; } = "";
        
        /// <summary>
        /// Optional comment for documentation
        /// </summary>
        [JsonProperty("comment")]
        public string Comment { get; set; } = "";
    }
    
    /// <summary>
    /// Model override definition - replace 3D meshes with custom model files
    /// Supports OBJ, GLTF, GLB formats, and Unity AssetBundles
    /// </summary>
    public class ModelOverrideDefinition
    {
        /// <summary>
        /// Target mesh name to replace (use F11 discovery to find names)
        /// E.g., "HoboCommon_FPS" for first-person player body
        /// </summary>
        [JsonProperty("target")]
        public string Target { get; set; } = "";
        
        /// <summary>
        /// Path to replacement model file relative to mod folder (OBJ/GLB/GLTF)
        /// E.g., "models/custom_body.obj"
        /// Mutually exclusive with 'bundle' property
        /// </summary>
        [JsonProperty("file")]
        public string File { get; set; } = "";
        
        /// <summary>
        /// Path to AssetBundle file relative to mod folder
        /// E.g., "character.bundle"
        /// Use with 'asset' property to specify asset name inside bundle
        /// </summary>
        [JsonProperty("bundle")]
        public string Bundle { get; set; } = "";
        
        /// <summary>
        /// Asset name inside the AssetBundle
        /// E.g., "MyCharacter" or "Assets/Prefabs/Character.prefab"
        /// </summary>
        [JsonProperty("asset")]
        public string Asset { get; set; } = "";
        
        /// <summary>
        /// Type of asset to extract from bundle
        /// "mesh" = just mesh, "skinned" = SkinnedMeshRenderer with bones, "prefab" = full GameObject
        /// Default: "skinned"
        /// </summary>
        [JsonProperty("assetType")]
        public string AssetType { get; set; } = "skinned";
        
        /// <summary>
        /// Whether to instantiate prefab (for "prefab" assetType)
        /// If true, creates a new instance; if false, just extracts data
        /// Default: false
        /// </summary>
        [JsonProperty("instantiate")]
        public bool Instantiate { get; set; } = false;
        
        /// <summary>
        /// Scale factor for the model (default 1.0)
        /// Use smaller values like 0.01 if model is too big
        /// </summary>
        [JsonProperty("scale")]
        public float Scale { get; set; } = 1.0f;
        
        /// <summary>
        /// Rotation around X axis in degrees (common: -90 for Blender Z-up to Unity Y-up)
        /// </summary>
        [JsonProperty("rotX")]
        public float RotX { get; set; } = 0f;
        
        /// <summary>
        /// Rotation around Y axis in degrees
        /// </summary>
        [JsonProperty("rotY")]
        public float RotY { get; set; } = 0f;
        
        /// <summary>
        /// Rotation around Z axis in degrees
        /// </summary>
        [JsonProperty("rotZ")]
        public float RotZ { get; set; } = 0f;
        
        /// <summary>
        /// Optional comment for documentation
        /// </summary>
        [JsonProperty("comment")]
        public string Comment { get; set; } = "";
        
        /// <summary>
        /// Check if this override uses an AssetBundle (vs regular file)
        /// </summary>
        public bool IsBundle => !string.IsNullOrEmpty(Bundle);
    }
    
    /// <summary>
    /// Custom object definition - spawn NEW objects from AssetBundles
    /// Unlike model overrides which replace existing meshes, this spawns new objects
    /// </summary>
    public class CustomObjectDefinition
    {
        /// <summary>
        /// Unique identifier for this custom object
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; } = "";
        
        /// <summary>
        /// Display name for the object
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = "";
        
        /// <summary>
        /// Path to AssetBundle file relative to mod folder
        /// </summary>
        [JsonProperty("bundle")]
        public string Bundle { get; set; } = "";
        
        /// <summary>
        /// Prefab name inside the AssetBundle
        /// </summary>
        [JsonProperty("prefab")]
        public string Prefab { get; set; } = "";
        
        /// <summary>
        /// Spawn location type: "player", "world", "camera"
        /// - player: Spawn in front of player
        /// - world: Spawn at specified coordinates
        /// - camera: Spawn where player is looking
        /// </summary>
        [JsonProperty("spawnAt")]
        public string SpawnAt { get; set; } = "player";
        
        /// <summary>
        /// Offset from spawn location (x, y, z)
        /// </summary>
        [JsonProperty("offsetX")]
        public float OffsetX { get; set; } = 0f;
        
        [JsonProperty("offsetY")]
        public float OffsetY { get; set; } = 0f;
        
        [JsonProperty("offsetZ")]
        public float OffsetZ { get; set; } = 2f;  // Default: 2 meters in front
        
        /// <summary>
        /// Hotkey to spawn this object (e.g., "F8", "Insert")
        /// If empty, can only be spawned via API
        /// </summary>
        [JsonProperty("hotkey")]
        public string Hotkey { get; set; } = "";
        
        /// <summary>
        /// Scale factor for the spawned object
        /// </summary>
        [JsonProperty("scale")]
        public float Scale { get; set; } = 1.0f;
        
        /// <summary>
        /// Whether to persist the object (survives scene changes)
        /// Default: false (temporary)
        /// </summary>
        [JsonProperty("persistent")]
        public bool Persistent { get; set; } = false;
        
        /// <summary>
        /// Optional comment
        /// </summary>
        [JsonProperty("comment")]
        public string Comment { get; set; } = "";
    }
}
