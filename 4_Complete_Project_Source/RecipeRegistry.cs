using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using Game;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Registry for custom recipes - handles loading from JSON and injection into game
    /// </summary>
    public class RecipeRegistry
    {
        private readonly ManualLogSource _log;
        private readonly ItemRegistry _itemRegistry;
        private readonly Dictionary<string, RegisteredRecipe> _recipes = new();
        
        private uint _nextRecipeId = 51000;
        
        // Static lookup for result counts - enables multi-item crafting via patches
        private static readonly Dictionary<uint, int> _recipeResultCounts = new();
        
        /// <summary>
        /// Get the result count for a mod recipe. Returns 1 if not a mod recipe or count not set.
        /// Used by crafting patches to spawn extra items.
        /// </summary>
        public static int GetResultCount(uint recipeId)
        {
            return _recipeResultCounts.TryGetValue(recipeId, out int count) && count > 0 ? count : 1;
        }
        
        public RecipeRegistry(ManualLogSource log, ItemRegistry itemRegistry)
        {
            _log = log;
            _itemRegistry = itemRegistry;
        }
        
        /// <summary>
        /// Load all recipes from a mod's recipes folder
        /// </summary>
        public void LoadRecipesFromMod(ModManifest mod)
        {
            var recipesPath = Path.Combine(mod.FolderPath, "recipes");
            if (!Directory.Exists(recipesPath)) return;
            
            var recipeFiles = Directory.GetFiles(recipesPath, "*.json");
            _log.LogInfo($"  Loading {recipeFiles.Length} recipe(s) from {mod.Name}");
            
            foreach (var recipeFile in recipeFiles)
            {
                try
                {
                    var json = File.ReadAllText(recipeFile);
                    var definition = Newtonsoft.Json.JsonConvert.DeserializeObject<RecipeDefinition>(json);
                    
                    if (definition == null) continue;
                    
                    RegisterRecipe(mod, definition);
                }
                catch (Exception ex)
                {
                    _log.LogError($"    Failed to load {Path.GetFileName(recipeFile)}: {ex.Message}");
                }
            }
        }
        
        private void RegisterRecipe(ModManifest mod, RecipeDefinition definition)
        {
            var numericId = _nextRecipeId++;
            var fullId = $"{mod.Id}:{definition.Id}";
            
            var registered = new RegisteredRecipe
            {
                FullId = fullId,
                NumericId = numericId,
                Mod = mod,
                Definition = definition
            };
            
            _recipes[fullId] = registered;
            
            // Track result count for multi-item crafting support
            if (definition.ResultCount > 1)
            {
                _recipeResultCounts[numericId] = definition.ResultCount;
            }
            
            _log.LogInfo($"    Registered recipe: {definition.Id} -> ID {numericId}" + 
                (definition.ResultCount > 1 ? $" (ResultCount: {definition.ResultCount})" : ""));
        }
        
        /// <summary>
        /// Inject all registered recipes into the game's RecipeDatabase
        /// </summary>
        public void InjectAllRecipes()
        {
            var recipes = RecipeDatabase.recipes;
            if (recipes == null || recipes.Count == 0)
            {
                _log.LogWarning("RecipeDatabase not ready");
                return;
            }
            
            _log.LogInfo($"=== RecipeRegistry: Injecting {_recipes.Count} recipes ===");
            
            foreach (var entry in _recipes)
            {
                InjectRecipe(entry.Value);
            }
        }
        
        private void InjectRecipe(RegisteredRecipe registered)
        {
            try
            {
                var definition = registered.Definition;
                var recipes = RecipeDatabase.recipes;
                
                // Skip if already injected
                if (recipes.ContainsKey(registered.NumericId)) return;
                
                // Find an Item-type recipe to clone
                Recipe sourceRecipe = null;
                foreach (var r in recipes.Values)
                {
                    if (r != null && r.type == Recipe.RecipeType.Item)
                    {
                        sourceRecipe = r;
                        break;
                    }
                }
                
                if (sourceRecipe == null)
                {
                    _log.LogWarning($"    No source recipe found for {definition.Id}");
                    return;
                }
                
                // Clone the recipe
                var clonedObj = sourceRecipe.Clone();
                var customRecipe = clonedObj?.TryCast<Recipe>();
                if (customRecipe == null) return;
                
                // Configure basic properties
                customRecipe.id = registered.NumericId;
                customRecipe.type = ParseRecipeType(definition.Type);
                customRecipe.index = 40 + (int)(registered.NumericId - 51000);
                customRecipe.requireSkillLvl = definition.SkillRequired;
                customRecipe.notActive = definition.NotActive;
                customRecipe.craftingDifficulty = definition.CraftingDifficulty;
                
                // Parse requireSK from string
                if (!string.IsNullOrEmpty(definition.RequireSK))
                {
                    if (Enum.TryParse<StreetKnowledgeManager.Keys>(definition.RequireSK, true, out var skValue))
                    {
                        customRecipe.requireSK = skValue;
                    }
                    else
                    {
                        customRecipe.requireSK = StreetKnowledgeManager.Keys.NOTHING;
                    }
                }
                else
                {
                    customRecipe.requireSK = StreetKnowledgeManager.Keys.NOTHING;
                }
                
                // Parse associated bench
                if (!string.IsNullOrEmpty(definition.AssociatedBench))
                {
                    customRecipe.myAsociatedBench = ParseBenchType(definition.AssociatedBench);
                }
                
                // Set recipe name
                var recipeName = ResolveLocalizedString(registered, definition.Name);
                customRecipe.Title = recipeName;
                customRecipe.title = recipeName;
                
                // Resolve result item
                var resultItem = ResolveResultItem(registered);
                if (resultItem == null)
                {
                    _log.LogWarning($"    Result item not found for recipe {definition.Id}");
                    return;
                }
                
                customRecipe.resultItemId = resultItem.NumericId;
                customRecipe._resultItem = resultItem.GameItem;
                // NOTE: RecipeDefinition.ResultCount is documented but Recipe class has no resultCount property
                // The game handles result count differently (through item stacking)
                
                // Parse bench type
                customRecipe.myBench = ParseBenchType(definition.Bench);
                
                // Set primary ingredients
                var reqList = new Il2CppSystem.Collections.Generic.List<RecipeRequireItem>();
                foreach (var ing in definition.Ingredients)
                {
                    var req = new RecipeRequireItem();
                    req.itemID = ing.Item;
                    req.itemCount = ing.Count;
                    reqList.Add(req);
                }
                customRecipe.RequireItemsPrimary = reqList;
                
                // Set secondary/alternate ingredients (if any)
                var secList = new Il2CppSystem.Collections.Generic.List<RecipeRequireItem>();
                if (definition.SecondaryIngredients != null && definition.SecondaryIngredients.Count > 0)
                {
                    foreach (var ing in definition.SecondaryIngredients)
                    {
                        var req = new RecipeRequireItem();
                        req.itemID = ing.Item;
                        req.itemCount = ing.Count;
                        secList.Add(req);
                    }
                }
                customRecipe.RequireItemsSecondary = secList;
                
                // Add to database
                recipes[registered.NumericId] = customRecipe;
                registered.GameRecipe = customRecipe;
                
                _log.LogInfo($"    Injected recipe: {definition.Id} (ID: {registered.NumericId})");
                
                // Auto-unlock if specified
                if (definition.AutoUnlock)
                {
                    registered.PendingUnlock = true;
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"    Failed to inject recipe {registered.Definition.Id}: {ex.Message}");
            }
        }
        
        private RegisteredItem ResolveResultItem(RegisteredRecipe registered)
        {
            var resultId = registered.Definition.Result;
            
            // Try full mod ID first (mod_id:item_id)
            var fullId = $"{registered.Mod.Id}:{resultId}";
            var item = _itemRegistry.GetItem(fullId);
            if (item != null) return item;
            
            // Try just the item ID
            item = _itemRegistry.GetItem(resultId);
            if (item != null) return item;
            
            // Search all items for matching ID suffix
            // This allows recipes to reference items by short name
            return null; // Item not found
        }
        
        private Recipe.BenchType ParseBenchType(string bench)
        {
            if (string.IsNullOrEmpty(bench)) return Recipe.BenchType.Nothing;
            
            return bench.ToLowerInvariant() switch
            {
                "none" => Recipe.BenchType.Nothing,
                "nothing" => Recipe.BenchType.Nothing,
                "normal" => Recipe.BenchType.Normal,
                "kitchen" => Recipe.BenchType.Kitchen,
                "druglab" => Recipe.BenchType.DrugLab,
                "drug" => Recipe.BenchType.DrugLab,
                _ => Recipe.BenchType.Nothing
            };
        }
        
        private Recipe.RecipeType ParseRecipeType(string type)
        {
            if (string.IsNullOrEmpty(type)) return Recipe.RecipeType.Item;
            
            return type.ToLowerInvariant() switch
            {
                "structure" => Recipe.RecipeType.Structure,
                "cook" => Recipe.RecipeType.Cook,
                "item" => Recipe.RecipeType.Item,
                "improvisation" => Recipe.RecipeType.Improvisation,
                "weapon" => Recipe.RecipeType.Weapon,
                "forrepair" => Recipe.RecipeType.ForRepair,
                "repair" => Recipe.RecipeType.ForRepair,
                "forupgrade" => Recipe.RecipeType.ForUpgrade,
                "upgrade" => Recipe.RecipeType.ForUpgrade,
                _ => Recipe.RecipeType.Item
            };
        }
        
        private string ResolveLocalizedString(RegisteredRecipe registered, string key)
        {
            if (string.IsNullOrEmpty(key)) return "Unknown Recipe";
            
            if (key.StartsWith("@"))
            {
                var locKey = key.Substring(1);
                var locPath = Path.Combine(registered.Mod.FolderPath, "localization", "en.json");
                
                if (File.Exists(locPath))
                {
                    try
                    {
                        var json = File.ReadAllText(locPath);
                        var loc = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                        if (loc != null && loc.TryGetValue(locKey, out var value))
                        {
                            return value;
                        }
                    }
                    catch { }
                }
                
                return locKey;
            }
            
            return key;
        }
        
        /// <summary>
        /// Unlock auto-unlock recipes for the player
        /// Should be called on every save load, not just first time
        /// </summary>
        public void UnlockPendingRecipes(Character player)
        {
            foreach (var entry in _recipes)
            {
                var registered = entry.Value;
                // Use Definition.AutoUnlock (persists) instead of PendingUnlock flag (gets cleared)
                if (registered.Definition.AutoUnlock && registered.GameRecipe != null)
                {
                    if (!player.HasRecipe(registered.NumericId))
                    {
                        try
                        {
                            player.AddRecipe(registered.GameRecipe, false, false);
                            _log.LogInfo($"  Auto-unlocked recipe: {registered.Definition.Id}");
                        }
                        catch { }
                    }
                }
            }
        }
        
        /// <summary>
        /// Clear all registered recipes and reset ID counter.
        /// Called on game load to prevent duplicates when loading saves.
        /// </summary>
        public void Clear()
        {
            _log.LogInfo("[RecipeRegistry] Clearing registry for fresh injection...");
            _recipes.Clear();
            _recipeResultCounts.Clear();
            _nextRecipeId = 51000;
            _log.LogInfo("[RecipeRegistry] Registry cleared.");
        }
        
        public int Count => _recipes.Count;
    }
    
    /// <summary>
    /// Runtime registered recipe data
    /// </summary>
    public class RegisteredRecipe
    {
        public string FullId { get; set; } = "";
        public uint NumericId { get; set; }
        public ModManifest Mod { get; set; }
        public RecipeDefinition Definition { get; set; }
        public Recipe GameRecipe { get; set; }
        public bool PendingUnlock { get; set; }
    }
}
