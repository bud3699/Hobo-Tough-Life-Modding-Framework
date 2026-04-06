using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using Game;
using Core.Strings;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Registry for custom items - handles loading from JSON and injection into game
    /// </summary>
    public class ItemRegistry
    {
        private readonly ManualLogSource _log;
        private readonly Dictionary<string, RegisteredItem> _items = new();
        private readonly Dictionary<uint, string> _idToStringId = new();
        
        // Deferred icon copies: itemId -> referenceItemId
        // Used when icons aren't loaded at injection time
        private readonly Dictionary<uint, uint> _pendingIconCopies = new();
        
        public ItemRegistry(ManualLogSource log)
        {
            _log = log;
        }
        
        /// <summary>
        /// Lookup numeric item ID from full item ID (e.g., "shrek_mod:shrek_hoodie" -> 60001)
        /// </summary>
        public bool TryGetItemId(string fullItemId, out uint numericId)
        {
            numericId = 0;
            if (string.IsNullOrEmpty(fullItemId)) return false;
            
            if (_items.TryGetValue(fullItemId, out var registered))
            {
                numericId = registered.NumericId;
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Try to copy icon for items that had deferred icon copies.
        /// Called from ShowItem patch when item is displayed.
        /// </summary>
        public void TryDeferredIconCopy(uint itemId)
        {
            if (!_pendingIconCopies.ContainsKey(itemId)) return;
            
            uint refId = _pendingIconCopies[itemId];
            var items = ItemDatabase.items;
            
            if (items == null) return;
            if (!items.ContainsKey(itemId) || !items.ContainsKey(refId)) return;
            
            var targetItem = items[itemId];
            var refItem = items[refId];
            
            if (targetItem != null && refItem != null && refItem.icon != null)
            {
                targetItem.icon = refItem.icon;
                targetItem._icon = refItem._icon;
                _pendingIconCopies.Remove(itemId);
                _log.LogInfo($"[DeferredIconCopy] Copied icon from {refId} to {itemId}");
            }
        }
        
        /// <summary>
        /// Process all pending deferred icon copies.
        /// Called when crafting UI loads to ensure recipe icons are correct.
        /// </summary>
        public void TryAllDeferredIconCopies()
        {
            if (_pendingIconCopies.Count == 0) return;
            
            var items = ItemDatabase.items;
            if (items == null) return;
            
            // Copy to list to avoid modifying dictionary during iteration
            var pending = new List<uint>(_pendingIconCopies.Keys);
            
            foreach (var itemId in pending)
            {
                TryDeferredIconCopy(itemId);
            }
            
            if (_pendingIconCopies.Count == 0)
            {
                _log.LogInfo("[DeferredIconCopy] All pending icons processed");
            }
        }
        
        /// <summary>
        /// Load all items from a mod's items folder
        /// </summary>
        public void LoadItemsFromMod(ModManifest mod)
        {
            var itemsPath = Path.Combine(mod.FolderPath, "items");
            if (!Directory.Exists(itemsPath)) return;
            
            var itemFiles = Directory.GetFiles(itemsPath, "*.json");
            _log.LogInfo($"  Loading {itemFiles.Length} item(s) from {mod.Name}");
            
            foreach (var itemFile in itemFiles)
            {
                try
                {
                    var json = File.ReadAllText(itemFile);
                    var definition = Newtonsoft.Json.JsonConvert.DeserializeObject<ItemDefinition>(json);
                    
                    if (definition == null) continue;
                    
                    RegisterItem(mod, definition);
                }
                catch (Exception ex)
                {
                    _log.LogError($"    Failed to load {Path.GetFileName(itemFile)}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Register an item definition
        /// </summary>
        private void RegisterItem(ModManifest mod, ItemDefinition definition)
        {
            var fullId = $"{mod.Id}:{definition.Id}";
            
            // Use deterministic hash instead of incremental ID to prevent save corruption
            // when mods are added/removed/reordered
            var numericId = GetDeterministicId(fullId);
            
            // Check for hash collision
            if (_idToStringId.ContainsKey(numericId))
            {
                _log.LogWarning($"    ID collision detected for {fullId} (ID {numericId}). Skipping.");
                return;
            }
            
            var registered = new RegisteredItem
            {
                FullId = fullId,
                NumericId = numericId,
                Mod = mod,
                Definition = definition,
                LocalizationKey = (uint)(990000 + (numericId % 100000) * 2)  // Deterministic localization key
            };
            
            _items[fullId] = registered;
            _idToStringId[numericId] = fullId;
            
            _log.LogInfo($"    Registered: {definition.Id} -> ID {numericId}");
        }
        
        /// <summary>
        /// Generate a deterministic numeric ID from a string (e.g., "mymod:my_item")
        /// Uses stable hash to ensure same ID across sessions regardless of load order
        /// </summary>
        private static uint GetDeterministicId(string fullId)
        {
            // Simple stable hash (not GetHashCode which can vary across runs)
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in fullId)
                {
                    hash = (hash ^ c) * 16777619;
                }
                // Map to range 60000-260000 (200k unique IDs)
                return 60000 + (hash % 200000);
            }
        }
        
        /// <summary>
        /// Inject all registered items into the game's ItemDatabase
        /// </summary>
        public void InjectAllItems()
        {
            var items = ItemDatabase.items;
            if (items == null || items.Count == 0)
            {
                _log.LogWarning("ItemDatabase not ready");
                return;
            }
            
            _log.LogInfo($"=== ItemRegistry: Injecting {_items.Count} items ===");
            
            foreach (var entry in _items)
            {
                InjectItem(entry.Value);
            }
        }
        
        private void InjectItem(RegisteredItem registered)
        {
            try
            {
                var definition = registered.Definition;
                var items = ItemDatabase.items;
                
                // === PHASE 3: Modify Existing Items ===
                if (definition.IsModify && definition.TargetItemId > 0)
                {
                    ModifyExistingItem(definition);
                    return;
                }
                
                // Skip if already injected
                if (items.ContainsKey(registered.NumericId)) return;
                
                // === FIX: Determine clone source ID ===
                // For gear/weapon/bag types, if baseItem is default (1) but referenceItemId is set,
                // use referenceItemId as the clone source (since it's the correct item type)
                uint cloneSourceId = definition.BaseItem;
                if (definition.BaseItem == 1 && definition.ReferenceItemId > 0)
                {
                    // Only override for non-consumable types
                    if (definition.Type == "gear" || definition.Type == "weapon" || definition.Type == "bag")
                    {
                        cloneSourceId = definition.ReferenceItemId;
                        _log.LogInfo($"    Using referenceItemId {cloneSourceId} as clone source for {definition.Type}");
                    }
                }
                
                // Find source item to clone
                if (!items.ContainsKey(cloneSourceId))
                {
                    _log.LogWarning($"    Clone source item {cloneSourceId} not found for {definition.Id}");
                    return;
                }
                
                var sourceItem = items[cloneSourceId];
                var cloned = sourceItem.Clone();
                
                // Configure basic properties
                cloned.id = registered.NumericId;
                cloned.titleKey = registered.LocalizationKey;
                cloned.descriptionKey = registered.LocalizationKey + 1;
                if (definition.Price.HasValue) cloned.price = definition.Price.Value;
                cloned.weight = definition.Weight;
                
                // === Extended Item Properties ===
                cloned.rareColor = definition.RareColor;
                cloned.sellable = definition.Sellable;
                cloned.notForFire = definition.NotForFire;
                cloned.soundType = (Game.BaseItem.ESoundType)definition.SoundType;
                cloned.firerate = definition.Firerate;
                
                // === Phase 2A Additional Properties ===
                cloned.isForHotSlot = definition.IsForHotSlot;
                cloned.isStealable = definition.IsStealable;
                cloned.heavyItem = definition.HeavyItem;
                cloned.hardItem = definition.HardItem;
                cloned.spawnDay = definition.SpawnDay;
                cloned.isPermanent = definition.IsPermanent;
                cloned.isStockable = definition.IsStockable;
                
                // Parse and apply hint enum
                if (!string.IsNullOrEmpty(definition.Hint))
                {
                    if (Enum.TryParse<Game.BaseItem.EHint>(definition.Hint, true, out var hintValue))
                    {
                        cloned.hint = hintValue;
                    }
                }
                
                // Parse and apply quest type enum
                if (!string.IsNullOrEmpty(definition.QuestType))
                {
                    if (Enum.TryParse<Game.BaseItem.EQuestType>(definition.QuestType, true, out var questValue))
                    {
                        cloned.questType = questValue;
                    }
                }
                
                // Salvage table ID
                if (definition.SalvageTableId >= 0)
                {
                    cloned.salvageTableID = definition.SalvageTableId;
                }
                
                // BuyBack time
                if (definition.BuyBackTime > 0)
                {
                    cloned.buyBackTime = definition.BuyBackTime;
                }
                
                // SubCategory
                if (!string.IsNullOrEmpty(definition.SubCategory))
                {
                    if (Enum.TryParse<Game.BaseItem.ESubCategory>(definition.SubCategory, true, out var subCatValue))
                    {
                        cloned.subCategory = subCatValue;
                    }
                }
                
                // Buffet minigame properties
                cloned.buffetGame = definition.BuffetGame;
                cloned.buffetDifficulty = definition.BuffetDifficulty;
                
                // Item ordering index
                if (definition.Index >= 0)
                {
                    cloned.index = definition.Index;
                }
                
                // Linked recipes (improRecipes)
                if (definition.ImproRecipes != null && definition.ImproRecipes.Count > 0)
                {
                    var recipeList = new Il2CppSystem.Collections.Generic.List<uint>();
                    foreach (var recipeId in definition.ImproRecipes)
                    {
                        recipeList.Add(recipeId);
                    }
                    cloned.improRecipes = recipeList;
                }
                
                // Handle stackable items
                if (cloned is Consumable || cloned is Scrap)
                {
                    try
                    {
                        var stackable = cloned.TryCast<Consumable>();
                        if (stackable != null)
                        {
                            stackable.isStockable = definition.IsStockable;
                            stackable.actualStockCount = definition.ActualStockCount;
                        }
                    }
                    catch { /* Property may not exist on all types */ }
                }
                
                // === Reference Item Icon ===
                Sprite icon = null;
                bool iconCopied = false;
                
                if (definition.ReferenceItemId > 0)
                {
                    if (items.ContainsKey(definition.ReferenceItemId))
                    {
                        var refItem = items[definition.ReferenceItemId];
                        try
                        {
                            if (refItem != null && refItem.icon != null)
                            {
                                cloned.icon = refItem.icon;
                                cloned._icon = refItem._icon;
                                _log.LogInfo($"    Copied icon from item ID {definition.ReferenceItemId}");
                                iconCopied = true;
                            }
                            else
                            {
                                // Icon not loaded yet - defer copy for later
                                _pendingIconCopies[registered.NumericId] = definition.ReferenceItemId;
                            }
                        }
                        catch (System.Exception)
                        {
                            // Icon getter threw exception - defer copy for later
                            _pendingIconCopies[registered.NumericId] = definition.ReferenceItemId;
                        }
                    }
                }
                
                if (!iconCopied && !string.IsNullOrEmpty(definition.Icon))
                {
                    // Load icon from file (custom icon path takes priority)
                    icon = LoadIcon(registered);
                    if (icon != null)
                    {
                        cloned.icon = icon;
                    }
                }
                
                // Handle Consumable items - clear base effects (shallow copy issue)
                var consumable = cloned.TryCast<Consumable>();
                if (consumable != null && definition.Type == "consumable")
                {
                    
                    // CRITICAL FIX: BaseItem.Clone() likely does a shallow copy of lists.
                    // Clearing the list on the clone wipes the ORIGINAL item's effects!
                    // Instead, assign NEW empty lists to decouple from the original.
                    
                    if (consumable.changes != null)
                    {
                        consumable.changes = new Il2CppSystem.Collections.Generic.List<Consumable.Change>();
                    }
                    
                    if (consumable.buffChanges != null)
                    {
                        consumable.buffChanges = new Il2CppSystem.Collections.Generic.List<Consumable.BuffChange>();
                    }
                    
                    if (consumable.parameterChanges != null)
                    {
                        consumable.parameterChanges = new Il2CppSystem.Collections.Generic.List<Consumable.ParameterChange>();
                    }
                    
                    // === Phase 2B: Populate native parameterChanges ===
                    if (definition.StatChanges != null && definition.StatChanges.Count > 0)
                    {
                        foreach (var stat in definition.StatChanges)
                        {
                            var paramType = ParseStatType(stat.Stat);
                            if (paramType.HasValue)
                            {
                                var change = new Consumable.ParameterChange(
                                    paramType.Value,
                                    stat.AddictedValue,
                                    stat.Value
                                );
                                consumable.parameterChanges.Add(change);
                                _log.LogInfo($"    Added stat change: {stat.Stat} = {stat.Value}");
                            }
                        }
                    }
                    
                    // === Phase 2B: Populate native buffChanges ===
                    if (definition.BuffEffects != null && definition.BuffEffects.Count > 0)
                    {
                        foreach (var buff in definition.BuffEffects)
                        {
                            if (Enum.TryParse<CharacterBuff.BuffType>(buff.Buff, true, out var buffType))
                            {
                                var tier = (CharacterBuff.Tier)Math.Clamp(buff.Tier, 0, 3);
                                var addictedTier = (CharacterBuff.Tier)Math.Clamp(buff.AddictedTier, 0, 3);
                                
                                Enum.TryParse<CharacterAddiction.TypeAddiction>(buff.AddictionType, true, out var addType);
                                
                                var buffChange = new Consumable.BuffChange(buffType, tier, addictedTier, addType);
                                consumable.buffChanges.Add(buffChange);
                                _log.LogInfo($"    Added buff: {buff.Buff} Tier{buff.Tier}");
                            }
                        }
                    }
                    
                    // === Phase 2B: Set addiction type ===
                    if (!string.IsNullOrEmpty(definition.AddictionType))
                    {
                        if (Enum.TryParse<CharacterAddiction.TypeAddiction>(definition.AddictionType, true, out var adType))
                        {
                            consumable.typeAddiction = adType;
                            _log.LogInfo($"    Set addiction type: {definition.AddictionType}");
                        }
                    }
                }
                
                // [DIAG-INJECT] Log consumable pipeline state
                if (consumable != null)
                {
                    int nativeParamCount = consumable.parameterChanges?.Count ?? 0;
                    int nativeBuffCount = consumable.buffChanges?.Count ?? 0;
                    int defEffects = definition.Effects?.Count ?? 0;
                    int defStatChanges = definition.StatChanges?.Count ?? 0;
                    int defBuffEffects = definition.BuffEffects?.Count ?? 0;
                    _log.LogInfo($"[DIAG-INJECT] '{definition.Id}': JSON has Effects={defEffects}, StatChanges={defStatChanges}, BuffEffects={defBuffEffects}");
                    _log.LogInfo($"[DIAG-INJECT] '{definition.Id}': Native object now has parameterChanges={nativeParamCount}, buffChanges={nativeBuffCount}");
                }
                
                var gear = cloned.TryCast<Gear>();
                if (gear != null && definition.Type == "gear")
                {
                    
                    // Set category
                    gear.category = ParseGearCategory(definition.Category);
                    
                    // Set resistances
                    gear.warmResistance = definition.WarmResistance;
                    gear.wetResistance = definition.WetResistance;
                    gear.durabilityResistance = definition.DurabilityResistance;
                    
                    // Set repair options
                    if (definition.RepairRecipe > 0)
                    {
                        gear.repairRecipe = definition.RepairRecipe;
                    }
                    if (definition.RepairCash > 0)
                    {
                        gear.repairCash = definition.RepairCash;
                    }
                    
                    // FIX: Do NOT touch parameterChanges list - IL2CPP list operations cause crashes
                    // The cloned item inherits parameterChanges from source, which is fine
                    // Only add new stats if specified (append to existing)
                    if (definition.Stats != null && definition.Stats.Count > 0)
                    {
                        // Ensure list exists (should from clone, but safety check)
                        if (gear.parameterChanges == null)
                        {
                            gear.parameterChanges = new Il2CppSystem.Collections.Generic.List<Gear.ParameterChange>();
                        }
                        
                        foreach (var stat in definition.Stats)
                        {
                            var paramType = ParseStatType(stat.Type);
                            if (paramType.HasValue)
                            {
                                var change = new Gear.ParameterChange();
                                change.influencedParameterType = paramType.Value;
                                change.value = stat.Value;
                                change.finalValue = stat.Value;
                                gear.parameterChanges.Add(change);
                                
                                _log.LogInfo($"    Added stat: {stat.Type} = {stat.Value}");
                            }
                        }
                    }
                    
                    // [DIAG-INJECT] Log gear pipeline state
                    int gearParamCount = gear.parameterChanges?.Count ?? 0;
                    _log.LogInfo($"[DIAG-INJECT] '{definition.Id}' (gear): Native object now has parameterChanges={gearParamCount}");
                }
                
                // Handle Weapon items
                var weapon = cloned.TryCast<Weapon>();
                if (weapon != null && definition.Type == "weapon")
                {
                    // Set weapon stats
                    weapon.attack = definition.Attack;
                    weapon.defense = definition.Defense;
                    weapon.criticalChance = definition.CriticalChance;
                    weapon.maxDurability = definition.MaxDurability;
                    weapon.actualDurability = definition.MaxDurability;
                }
                
                // Handle Bag items
                var bag = cloned.TryCast<Bag>();
                if (bag != null && definition.Type == "bag")
                {
                    // Set custom capacity if specified
                    if (definition.BagCapacity > 0)
                    {
                        bag.capacity = definition.BagCapacity;
                        _log.LogInfo($"    Set bag capacity: {definition.BagCapacity}");
                    }
                    
                    // Register as custom bag for Clone/Load patches
                    RegisterCustomBag(registered.NumericId);
                }
                
                // Inject localization
                InjectLocalization(registered);
                
                // Add to database dictionary
                items[registered.NumericId] = cloned;
                registered.GameItem = cloned;
                
                // TODO: Add to type-specific list for full compatibility (HoboEX pattern)
                // This code is currently disabled because ItemDatabase.Instance doesn't exist
                // in this version of the game. The items dictionary injection above is sufficient.
                // When/if the game API is better understood, this can be re-enabled with the
                // correct pattern to access type-specific lists (consumables, gears, etc.)
                
                _log.LogInfo($"    Injected: {definition.Id} (ID: {registered.NumericId})");
            }
            catch (Exception ex)
            {
                _log.LogError($"    Failed to inject {registered.Definition.Id}: {ex.Message}");
            }
        }
        
        private Sprite LoadIcon(RegisteredItem registered)
        {
            if (string.IsNullOrEmpty(registered.Definition.Icon)) return null;
            
            var iconPath = Path.Combine(registered.Mod.FolderPath, registered.Definition.Icon);
            if (!File.Exists(iconPath)) return null;
            
            try
            {
                var imageData = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, imageData)) return null;
                
                return Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            catch
            {
                return null;
            }
        }
        
        private void InjectLocalization(RegisteredItem registered)
        {
            try
            {
                var stringsItems = StringsManager.strings_items;
                if (stringsItems?.translatedInt == null) return;
                
                var dict = stringsItems.translatedInt;
                var def = registered.Definition;
                
                // Resolve localization key or use literal
                var name = ResolveLocalizedString(registered, def.Name);
                var desc = ResolveLocalizedString(registered, def.Description);
                
                dict[registered.LocalizationKey] = name;
                dict[registered.LocalizationKey + 1] = desc;
            }
            catch { }
        }
        
        private string ResolveLocalizedString(RegisteredItem registered, string key)
        {
            // If starts with @, look up in mod's localization
            if (key.StartsWith("@"))
            {
                var locKey = key.Substring(1);
                var mod = registered.Mod;
                
                // Load and cache localization if not already cached
                if (mod.LocalizationCache == null)
                {
                    mod.LocalizationCache = LoadModLocalization(mod.FolderPath);
                }
                
                // Look up in cache
                if (mod.LocalizationCache.TryGetValue(locKey, out var value))
                {
                    return value;
                }
                
                return locKey; // Fallback to key itself
            }
            
            return key; // Literal string
        }
        
        /// <summary>
        /// Load localization file for a mod (cached after first load)
        /// </summary>
        private Dictionary<string, string> LoadModLocalization(string modFolderPath)
        {
            var locPath = Path.Combine(modFolderPath, "localization", "en.json");
            
            if (File.Exists(locPath))
            {
                try
                {
                    var json = File.ReadAllText(locPath);
                    var loc = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    return loc ?? new Dictionary<string, string>();
                }
                catch 
                {
                    _log.LogWarning($"Failed to load localization: {locPath}");
                }
            }
            
            return new Dictionary<string, string>(); // Empty cache if no file
        }
        
        /// <summary>
        /// Get registered item by string ID
        /// </summary>
        public RegisteredItem GetItem(string fullId)
        {
            return _items.TryGetValue(fullId, out var item) ? item : null;
        }
        
        /// <summary>
        /// Find item by suffix (short name) - searches all registered items
        /// Returns first match where fullId ends with ":suffix"
        /// </summary>
        public RegisteredItem FindItemBySuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return null;
            
            var searchSuffix = ":" + suffix;
            foreach (var kvp in _items)
            {
                if (kvp.Key.EndsWith(searchSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Modify an existing vanilla item (isModify feature)
        /// </summary>
        private void ModifyExistingItem(ItemDefinition definition)
        {
            var items = ItemDatabase.items;
            
            if (!items.ContainsKey(definition.TargetItemId))
            {
                _log.LogWarning($"    [isModify] Target item {definition.TargetItemId} not found");
                return;
            }
            
            var targetItem = items[definition.TargetItemId];
            
            // Apply non-default values only
            if (definition.Price.HasValue) targetItem.price = definition.Price.Value;
            if (definition.Weight != 0.1f) targetItem.weight = definition.Weight;
            if (definition.RareColor != 0) targetItem.rareColor = definition.RareColor;
            targetItem.sellable = definition.Sellable;
            targetItem.notForFire = definition.NotForFire;
            if (definition.SoundType != 0) targetItem.soundType = (Game.BaseItem.ESoundType)definition.SoundType;
            if (definition.Firerate != 0) targetItem.firerate = definition.Firerate;
            
            // Copy icon if referenceItemId specified
            if (definition.ReferenceItemId > 0 && items.ContainsKey(definition.ReferenceItemId))
            {
                var refItem = items[definition.ReferenceItemId];
                if (refItem.icon != null)
                {
                    targetItem.icon = refItem.icon;
                    targetItem._icon = refItem._icon;
                }
            }
            
            _log.LogInfo($"    [isModify] Patched vanilla item ID {definition.TargetItemId}");
            
            // [DIAG-MODIFY] Log the state of the target after modification
            var gearTarget = targetItem.TryCast<Gear>();
            if (gearTarget != null)
            {
                int paramCount = gearTarget.parameterChanges?.Count ?? 0;
                int defStatCount = definition.Stats?.Count ?? 0;
                _log.LogInfo($"[DIAG-MODIFY] '{definition.Id}': Target {definition.TargetItemId} has parameterChanges={paramCount}. JSON defines {defStatCount} stats. {(defStatCount > 0 && paramCount == 0 ? "⚠ STATS NOT APPLIED" : "OK")}");
            }
            var consumTarget = targetItem.TryCast<Consumable>();
            if (consumTarget != null)
            {
                int paramCount = consumTarget.parameterChanges?.Count ?? 0;
                int defStatCount = definition.StatChanges?.Count ?? 0;
                _log.LogInfo($"[DIAG-MODIFY] '{definition.Id}': Target {definition.TargetItemId} has parameterChanges={paramCount}. JSON defines {defStatCount} statChanges. {(defStatCount > 0 && paramCount == 0 ? "⚠ STATS NOT APPLIED" : "OK")}");
            }
        }
        
        /// <summary>
        /// Get registered item by numeric ID
        /// </summary>
        public RegisteredItem GetItemByNumericId(uint id)
        {
            if (_idToStringId.TryGetValue(id, out var fullId))
            {
                return GetItem(fullId);
            }
            return null;
        }
        
        /// <summary>
        /// Parse gear category string to enum
        /// </summary>
        private Gear.ECategory ParseGearCategory(string category)
        {
            return category?.ToLower() switch
            {
                "hat" => Gear.ECategory.Hat,
                "jacket" => Gear.ECategory.Jacket,
                "trousers" => Gear.ECategory.Trousers,
                "shoes" => Gear.ECategory.Shoes,
                _ => Gear.ECategory.Jacket  // Default to jacket
            };
        }
        
        /// <summary>
        /// Parse stat type string to BaseParameter.Type
        /// </summary>
        private BaseParameter.Type? ParseStatType(string statType)
        {
            return statType?.ToLower() switch
            {
                // Core stats
                "health" => BaseParameter.Type.Health,
                "food" => BaseParameter.Type.Food,
                "morale" => BaseParameter.Type.Morale,
                
                // FIXED: Energy = Freshness (c.Freshness has title='Energy')
                "energy" or "freshness" or "sleep" or "tiredness" => BaseParameter.Type.Freshness,
                
                "warm" or "warmth" => BaseParameter.Type.Warm,
                "wet" or "dryness" => BaseParameter.Type.Wet,
                
                // Status effects
                "illness" => BaseParameter.Type.Illness,
                "toxicity" or "poisoning" => BaseParameter.Type.Toxicity,
                "alcohol" => BaseParameter.Type.Alcohol,
                "greatneed" or "bathroom" => BaseParameter.Type.Greatneed,
                
                // FIXED: Smell/Odour = Smell (c.Smell has title='Odour')
                "smell" or "odour" or "odor" => BaseParameter.Type.Smell,
                
                // Resistances
                "smellresistance" => BaseParameter.Type.SmellResistance,
                "wetresistance" => BaseParameter.Type.WetResistance,
                "warmresistance" => BaseParameter.Type.WarmResistance,
                "toxicityresistance" => BaseParameter.Type.ToxicityResistance,
                "immunity" => BaseParameter.Type.Immunity,
                
                // Combat stats
                "attack" => BaseParameter.Type.Attack,
                "defense" => BaseParameter.Type.Defense,
                "charism" or "charisma" => BaseParameter.Type.Charism,
                
                // Capacity and willpower
                "capacity" => BaseParameter.Type.Capacity,
                "stamina" => BaseParameter.Type.Stamina,
                "gearsmell" => BaseParameter.Type.GearSmell,
                
                // FIXED: Grit = Willpower (c.Grit has title='Willpower', max=2)
                "grit" or "willpower" => BaseParameter.Type.Grit,
                "gritmax" => BaseParameter.Type.GritMax,
                "courage" => BaseParameter.Type.Courage,
                "couragemax" => BaseParameter.Type.CourageMax,
                
                _ => null
            };
        }
        
        /// <summary>
        /// Check if a numeric ID belongs to a mod item
        /// </summary>
        public bool IsModItem(uint id) => _idToStringId.ContainsKey(id);
        
        /// <summary>
        /// Clear all registered items.
        /// Called on game load to prevent duplicates when loading saves.
        /// </summary>
        public void Clear()
        {
            _log.LogInfo("[ItemRegistry] Clearing registry for fresh injection...");
            _items.Clear();
            _idToStringId.Clear();
            _pendingIconCopies.Clear();
            // Note: No ID counter reset needed - we use deterministic hashes now
            ClearCustomBags();
            _log.LogInfo("[ItemRegistry] Registry cleared.");
        }
        
        // === Custom Bag Registry ===
        private static HashSet<uint> _customBagIds = new HashSet<uint>();
        
        public static void RegisterCustomBag(uint id)
        {
            _customBagIds.Add(id);
        }
        
        public static bool IsCustomBag(uint id)
        {
            return _customBagIds.Contains(id);
        }
        
        public static void ClearCustomBags()
        {
            _customBagIds.Clear();
        }
    }
    
    /// <summary>
    /// Runtime registered item data
    /// </summary>
    public class RegisteredItem
    {
        public string FullId { get; set; } = "";
        public uint NumericId { get; set; }
        public uint LocalizationKey { get; set; }
        public ModManifest Mod { get; set; }
        public ItemDefinition Definition { get; set; }
        public BaseItem GameItem { get; set; }
    }
}
