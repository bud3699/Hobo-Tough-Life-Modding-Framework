using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Game;
using UI;
using TMPro;
using Core.Strings;
using System.Collections.Generic;
using HoboModPlugin.Framework.Events;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Central Harmony patches for the framework
    /// Handles effect application for all mod items and UI fixes
    /// </summary>
    [HarmonyPatch]
    public static class FrameworkPatches
    {
        private static EffectHandler _effectHandler;
        private static ItemRegistry _itemRegistry;
        private static RecipeRegistry _recipeRegistry;
        private static ManualLogSource _log;
        
        public static void Initialize(ManualLogSource log, ItemRegistry itemRegistry, EffectHandler effectHandler, RecipeRegistry recipeRegistry)
        {
            _log = log;
            _itemRegistry = itemRegistry;
            _effectHandler = effectHandler;
            _recipeRegistry = recipeRegistry;
        }
        
        /// <summary>
        /// Intercept Consumable.Use() for all mod items
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Consumable), "Use")]
        public static bool Consumable_Use_Prefix(Consumable __instance)
        {
            // Check if this is a mod item
            if (_itemRegistry == null || !_itemRegistry.IsModItem(__instance.id))
            {
                return true; // Not a mod item, let original run
            }
            
            try
            {
                // BUG #5 FIX: Use FindObjectsOfType - PlayerManager.GetComponent returns NULL in IL2CPP
                var characters = UnityEngine.Object.FindObjectsOfType<Character>();
                if (characters == null || characters.Length == 0)
                {
                    _log?.LogWarning("No Character found in scene");
                    return true;
                }
                
                var character = characters[0];
                if (character == null)
                {
                    _log?.LogWarning("Character[0] is null");
                    return true;
                }
                
                // Apply effects from definition
                _log?.LogInfo($"Applying mod effects for item {__instance.id}");
                _effectHandler?.ApplyItemEffects(__instance.id, character);
                
                // === Phase 1: Fire OnItemUsed event ===
                EventHooks.NotifyItemUsed(__instance);
                
                // Let original run to handle item removal
                return true;
            }
            catch (System.Exception ex)
            {
                _log?.LogError($"FrameworkPatches error: {ex.Message}");
                return true;
            }
        }
        
        /// <summary>
        /// Hook into crafting UI to unlock framework recipes
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GUISheetScrollListFilterCrafting), "Load")]
        public static void CraftingUI_Load_Postfix()
        {
            try
            {
                if (_recipeRegistry == null || _recipeRegistry.Count == 0) return;
                
                var characters = Object.FindObjectsOfType<Character>();
                if (characters == null || characters.Length == 0) return;
                
                _recipeRegistry.UnlockPendingRecipes(characters[0]);
            }
            catch (System.Exception ex)
            {
                _log?.LogError($"Recipe unlock error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Override tooltip display for mod items to show correct effects
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GUISheetItemDetailContent), "ShowItem")]
        public static void ShowItem_Postfix(GUISheetItemDetailContent __instance, BaseItem _item)
        {
            try
            {
                if (_itemRegistry == null || _item == null) return;
                if (!_itemRegistry.IsModItem(_item.id)) return;
                
                var registered = _itemRegistry.GetItemByNumericId(_item.id);
                if (registered == null) return;
                
                var def = registered.Definition;
                
                // Get the consumable parameters UI slot array
                var consumableParams = __instance.parametersConsumable;
                if (consumableParams == null || consumableParams.Length == 0) return;
                
                // Hide all parameter slots first (clean slate)
                for (int i = 0; i < consumableParams.Length; i++)
                {
                    var param = consumableParams[i];
                    if (param != null)
                    {
                        param.SetActivity(false);
                    }
                }
                
                int slotIndex = 0;
                
                // --- PHASE 1: Legacy Effects (from Definition.Effects) ---
                // These are the original mod-defined "effects" strings in the JSON.
                // Kept for backward compatibility with older mod definitions.
                // NOTE: Legacy effects use string-based stat names, so we attempt to
                // parse and resolve the corresponding native icon from GUIDatabase.
                if (def?.Effects != null && def.Effects.Count > 0)
                {
                    foreach (var effect in def.Effects)
                    {
                        if (slotIndex >= consumableParams.Length) break;
                        
                        var param = consumableParams[slotIndex];
                        if (param == null) continue;
                        
                        string statName = GetStatDisplayName(effect.Stat);
                        string valueText = FormatEffectValue(effect.Value);
                        
                        // Try to resolve the native icon for this stat.
                        // We parse the string stat name to a BaseParameter.Type enum,
                        // then look up the correct icon (positive or negative variant)
                        // from GUIDatabase.Instance — the same singleton the game uses.
                        Sprite icon = TryGetLegacyEffectSprite(effect.Stat, effect.Value);
                        
                        param.SetValues(statName, valueText, icon);
                        param.SetActivity(true);
                        slotIndex++;
                    }
                }
                
                // --- PHASE 2: Native Consumable Stat Changes ---
                // [BUGFIX] The original code only checked Definition.Effects and exited early
                // if it was null/empty. Items using the newer "statChanges" JSON field (like
                // Junkie's Juice) had their stats correctly injected into the native Consumable
                // object during InjectItem, but the tooltip never read from those native lists.
                // This phase reads the ACTUAL game object's parameterChanges list.
                var nativeConsumable = _item.TryCast<Consumable>();
                if (nativeConsumable != null)
                {
                    // Show stat changes (e.g., Health +50)
                    if (nativeConsumable.parameterChanges != null)
                    {
                        for (int i = 0; i < nativeConsumable.parameterChanges.Count; i++)
                        {
                            if (slotIndex >= consumableParams.Length) break;
                            
                            var change = nativeConsumable.parameterChanges[i];
                            var param = consumableParams[slotIndex];
                            if (param == null) continue;
                            
                            string statName = GetParamTypeDisplayName(change.influencedParameterType);
                            string valueText = FormatFloatValue(change.normalValue);
                            
                            // [FIX] Fetch the correct native icon from GUIDatabase.Instance
                            // instead of passing null (which caused the white box gap).
                            // The game stores separate Positive and Negative variants of
                            // each stat icon (e.g. a green heart vs a red heart).
                            Sprite icon = GetParamSprite(change.influencedParameterType, change.normalValue);
                            
                            param.SetValues(statName, valueText, icon);
                            param.SetActivity(true);
                            slotIndex++;
                        }
                    }
                    
                    // NOTE: Phase 3 (buff effects like Healing/Confidence) was REMOVED.
                    // The game already renders buff icons natively in the top-right corner
                    // of the tooltip header (the medical cross icon for Healing, the star
                    // icon for Confidence, etc.). Displaying them again as text lines in
                    // the parameter list was redundant, and caused a second white gap
                    // because we had no icon to assign to them. The native buff icon
                    // display is the correct, intended behavior.
                }
                
                if (slotIndex > 0)
                {
                    // --- DEBUG LOG START ---
                    // _log?.LogInfo($"[Tooltip] Updated display for {def?.Id} with {slotIndex} effect(s)");
                    // --- DEBUG LOG END ---
                }
            }
            catch (System.Exception ex)
            {
                _log?.LogError($"ShowItem patch error: {ex.Message}");
            }
        }
        

        private static string GetStatDisplayName(string stat)
        {
            return stat?.ToLowerInvariant() switch
            {
                "health" => "Health",
                "food" => "Food",
                "morale" => "Morale",
                "freshness" => "Freshness",
                "warmth" => "Warmth",
                "stamina" => "Stamina",
                "illness" => "Illness",
                "toxicity" => "Toxicity",
                "wet" => "Wetness",
                _ => stat ?? "Unknown"
            };
        }
        
        private static string FormatEffectValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            
            if (value.ToLower() == "max") return "MAX";
            if (value == "0") return "0";
            
            if (value.StartsWith("+") || value.StartsWith("-"))
            {
                return value;
            }
            
            // Try to parse as number
            if (float.TryParse(value, out float num))
            {
                return num >= 0 ? $"+{num}" : num.ToString();
            }
            
            return value;
        }
        
        /// <summary>
        /// Converts a native BaseParameter.Type enum to a human-readable display name.
        /// Used by the tooltip renderer to show stat names for consumable items.
        /// </summary>
        private static string GetParamTypeDisplayName(BaseParameter.Type paramType)
        {
            return paramType switch
            {
                BaseParameter.Type.Health => "Health",
                BaseParameter.Type.Food => "Food",
                BaseParameter.Type.Morale => "Morale",
                BaseParameter.Type.Freshness => "Energy",
                BaseParameter.Type.Warm => "Warmth",
                BaseParameter.Type.Stamina => "Stamina",
                BaseParameter.Type.Illness => "Illness",
                BaseParameter.Type.Toxicity => "Toxicity",
                BaseParameter.Type.Greatneed => "Bathroom",
                BaseParameter.Type.Wet => "Wetness",
                BaseParameter.Type.Immunity => "Immunity",
                BaseParameter.Type.Charism => "Charisma",
                BaseParameter.Type.Attack => "Attack",
                BaseParameter.Type.Defense => "Defense",
                BaseParameter.Type.WarmResistance => "Warm Resist.",
                BaseParameter.Type.WetResistance => "Wet Resist.",
                BaseParameter.Type.ToxicityResistance => "Toxic Resist.",
                _ => paramType.ToString()
            };
        }
        
        /// <summary>
        /// Formats a float stat value for tooltip display with +/- prefix.
        /// </summary>
        private static string FormatFloatValue(float value)
        {
            if (value == 0f) return "0";
            return value > 0 ? $"+{value}" : value.ToString();
        }
        
        /// <summary>
        /// Retrieves the correct native stat icon (Sprite) from GUIDatabase.Instance
        /// based on the parameter type and whether the value is positive or negative.
        /// 
        /// HOW THIS WORKS:
        /// The game stores all UI icons in a singleton called GUIDatabase.Instance.
        /// For item detail tooltips, the game uses "parameterDetailSprite" variants.
        /// Each stat type (Health, Food, Morale, etc.) has TWO icon variants:
        ///   - A "Positive" variant (e.g., green upward arrow + heart for Health gain)
        ///   - A "Negative" variant (e.g., red downward arrow + heart for Health loss)
        /// The game picks which one to show based on the sign of the stat value.
        /// 
        /// For example, a Bandage with "Health +30" would get:
        ///   GUIDatabase.Instance.parameterDetailSpriteHealthPositive  (green heart)
        /// And a poison with "Health -20" would get:
        ///   GUIDatabase.Instance.parameterDetailSpriteHealthNegative  (red heart)
        /// 
        /// Previously, we were passing null for the sprite, which left the native
        /// Image component active but with no texture — resulting in a white box gap.
        /// Now we pass the actual icon, matching what the game does for vanilla items.
        /// 
        /// SAFETY: If GUIDatabase.Instance is null (e.g., during early loading),
        /// we gracefully return null with a warning log instead of crashing.
        /// </summary>
        private static Sprite GetParamSprite(BaseParameter.Type paramType, float value)
        {
            try
            {
                var db = GUIDatabase.Instance;
                if (db == null)
                {
                    _log?.LogWarning("[Tooltip] GUIDatabase.Instance is null — cannot fetch stat icon");
                    return null;
                }
                
                // Determine if we need the Positive or Negative icon variant.
                // The game uses this to show green (gain) vs red (loss) icons.
                bool isPositive = value >= 0f;
                
                return paramType switch
                {
                    // --- Core survival stats ---
                    BaseParameter.Type.Health    => isPositive ? db.parameterDetailSpriteHealthPositive    : db.parameterDetailSpriteHealthNegative,
                    BaseParameter.Type.Food      => isPositive ? db.parameterDetailSpriteFoodPositive      : db.parameterDetailSpriteFoodNegative,
                    BaseParameter.Type.Morale    => isPositive ? db.parameterDetailSpriteMoralePositive    : db.parameterDetailSpriteMoraleNegative,
                    BaseParameter.Type.Freshness => isPositive ? db.parameterDetailSpriteFreshnessPositive : db.parameterDetailSpriteFreshnessNegative,
                    BaseParameter.Type.Warm      => isPositive ? db.parameterDetailSpriteWarmPositive      : db.parameterDetailSpriteWarmNegative,
                    
                    // --- Status effect stats ---
                    BaseParameter.Type.Toxicity  => isPositive ? db.parameterDetailSpriteToxicityPositive  : db.parameterDetailSpriteToxicityNegative,
                    BaseParameter.Type.Illness   => isPositive ? db.parameterDetailSpriteIllnessPositive   : db.parameterDetailSpriteIllnessNegative,
                    BaseParameter.Type.Alcohol   => isPositive ? db.parameterDetailSpriteAlcoholPositive   : db.parameterDetailSpriteAlcoholNegative,
                    BaseParameter.Type.Smell     => isPositive ? db.parameterDetailSpriteSmellPositive     : db.parameterDetailSpriteSmellNegative,
                    BaseParameter.Type.Stamina   => isPositive ? db.parameterDetailSpriteStaminaPositive   : db.parameterDetailSpriteStaminaNegative,
                    
                    // --- Stats that only have a generic (non-directional) icon ---
                    // These use the "parameterSprite" (no Positive/Negative variant).
                    // The game shows the same icon regardless of value direction.
                    BaseParameter.Type.Wet              => db.parameterSpriteWet,
                    BaseParameter.Type.Greatneed        => db.parameterSpriteGreatneed,
                    BaseParameter.Type.Immunity         => db.parameterSpriteImmunity,
                    BaseParameter.Type.Charism          => db.parameterSpriteCharism,
                    BaseParameter.Type.Attack           => db.parameterSpriteAttack,
                    BaseParameter.Type.Defense          => db.parameterSpriteDefense,
                    BaseParameter.Type.WetResistance    => db.parameterSpriteWetResistance,
                    BaseParameter.Type.WarmResistance   => db.parameterSpriteWarmResistance,
                    BaseParameter.Type.ToxicityResistance => db.parameterSpriteToxicityResistance,
                    BaseParameter.Type.Capacity         => db.parameterSpriteDetailCapacity,
                    BaseParameter.Type.GearSmell        => db.parameterSpriteSmell,
                    BaseParameter.Type.Grit             => db.parameterSpriteGrit,
                    
                    // --- Fallback: use the generic "universal" parameter icon ---
                    // This covers any new stat types added in future game updates.
                    _ => db.parameterSpriteUniversal
                };
            }
            catch (System.Exception ex)
            {
                _log?.LogWarning($"[Tooltip] Failed to fetch sprite for {paramType}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Attempts to resolve a native stat icon for legacy "Effects" entries.
        /// Legacy effects use string-based stat names (e.g., "health", "food") rather
        /// than the native BaseParameter.Type enum. This method tries to parse the
        /// string into the enum and then calls GetParamSprite to get the icon.
        /// 
        /// If the stat name cannot be parsed (e.g., custom modder-defined stats),
        /// this returns null gracefully — the tooltip will still work, just without
        /// an icon for that particular line.
        /// </summary>
        private static Sprite TryGetLegacyEffectSprite(string statName, string valueStr)
        {
            try
            {
                // Parse the value string to determine positive/negative direction
                float value = 0f;
                if (!string.IsNullOrEmpty(valueStr) && valueStr.ToLower() != "max")
                {
                    float.TryParse(valueStr.Replace("+", ""), out value);
                }
                
                // Try to map the legacy string stat name to a BaseParameter.Type enum
                var paramType = statName?.ToLowerInvariant() switch
                {
                    "health"    => (BaseParameter.Type?)BaseParameter.Type.Health,
                    "food"      => (BaseParameter.Type?)BaseParameter.Type.Food,
                    "morale"    => (BaseParameter.Type?)BaseParameter.Type.Morale,
                    "freshness" or "energy" or "sleep" => (BaseParameter.Type?)BaseParameter.Type.Freshness,
                    "warm" or "warmth"  => (BaseParameter.Type?)BaseParameter.Type.Warm,
                    "wet" or "wetness"  => (BaseParameter.Type?)BaseParameter.Type.Wet,
                    "stamina"   => (BaseParameter.Type?)BaseParameter.Type.Stamina,
                    "illness"   => (BaseParameter.Type?)BaseParameter.Type.Illness,
                    "toxicity"  => (BaseParameter.Type?)BaseParameter.Type.Toxicity,
                    "alcohol"   => (BaseParameter.Type?)BaseParameter.Type.Alcohol,
                    "smell"     => (BaseParameter.Type?)BaseParameter.Type.Smell,
                    "bathroom" or "greatneed" => (BaseParameter.Type?)BaseParameter.Type.Greatneed,
                    "charisma" or "charism"   => (BaseParameter.Type?)BaseParameter.Type.Charism,
                    "attack"    => (BaseParameter.Type?)BaseParameter.Type.Attack,
                    "defense"   => (BaseParameter.Type?)BaseParameter.Type.Defense,
                    "immunity"  => (BaseParameter.Type?)BaseParameter.Type.Immunity,
                    _ => null  // Unknown stat — no icon available, returns null
                };
                
                if (paramType.HasValue)
                {
                    return GetParamSprite(paramType.Value, value);
                }
            }
            catch (System.Exception ex)
            {
                _log?.LogWarning($"[Tooltip] Failed to resolve legacy effect sprite for '{statName}': {ex.Message}");
            }
            
            return null;
        }
        
        // Quest injection via GetFMQ_Map - intercept when game looks up our quest
        [HarmonyPatch(typeof(HBT_QuestDatabase), "GetFMQ_Map")]
        public static class QuestDatabase_GetFMQ_Map_Patch
        {
            public static bool Prefix(string questID, ref FMQ_Map __result, ref bool isOK)
            {
                // Check if this is one of our custom quests
                var quest = Plugin.Framework.QuestRegistry.GetQuestByGameId(questID);
                if (quest != null)
                {
                    _log.LogInfo($"=== GetFMQ_Map INTERCEPTED: {questID} ===");
                    __result = quest;
                    isOK = true;
                    return false; // Skip original
                }
                return true; // Continue to original
            }
        }

        // NOTE: ItemInfo_MeetCondition_Debug removed for performance - was logging every condition check

        // OnMakeAction handler - handles content re-injection and mod quest completion
        [HarmonyPatch(typeof(HBT_QuestManager), "OnMakeAction")]
        public static class OnMakeAction_Handler
        {
            public static void Prefix(FMQ_ActionWait.TypeOfActionWait type, uint _itemID, int _reciveCountitem, string _key)
            {
                // Re-inject content when world loads (Type.All with 0s is sent on world load)
                if (type == FMQ_ActionWait.TypeOfActionWait.All && _itemID == 0 && _reciveCountitem == 0)
                {
                    TryReinjectContent();
                }
                // NOTE: Verbose quest logging removed for performance
            }
            
            // POSTFIX: Manually complete mod quest nodes since native evaluation doesn't work
            public static void Postfix(FMQ_ActionWait.TypeOfActionWait type, uint _itemID, int _reciveCountitem, string _key)
            {
                // Only handle Item type
                if (type != FMQ_ActionWait.TypeOfActionWait.Item || _itemID == 0)
                    return;
                    
                try
                {
                    var quests = HBT_QuestManager.quests;
                    if (quests == null) return;
                    
                    for (int q = 0; q < quests.Count; q++)
                    {
                        var questInfo = quests[q];
                        if (questInfo == null || questInfo.myQuest == null) continue;
                        
                        var questID = questInfo.myQuest.QuestID;
                        
                        // Only handle mod quests (contain ":")
                        if (!questID.Contains(":")) continue;
                        
                        var inProgress = questInfo.inProgressNodes;
                        if (inProgress == null || inProgress.Count == 0) continue;
                        
                        // Check each in-progress node
                        for (int i = 0; i < inProgress.Count; i++)
                        {
                            var node = inProgress[i];
                            if (node?.aW == null) continue;
                            
                            // Check if this node listens for our item
                            if (node.aW.action != FMQ_ActionWait.TypeOfActionWait.Item) continue;
                            
                            var itemIds = node.aW.itemIDs;
                            if (itemIds == null) continue;
                            
                            bool matchesItem = false;
                            for (int j = 0; j < itemIds.Count; j++)
                            {
                                if (itemIds[j] == _itemID)
                                {
                                    matchesItem = true;
                                    break;
                                }
                            }
                            
                            if (!matchesItem) continue;
                            
                            _log.LogInfo($"=== MOD QUEST NODE MATCH for item {_itemID} ===");
                            _log.LogInfo($"    Quest: {questID}");
                            
                            // Get the node ID
                            string nodeID = null;
                            try { nodeID = node.id; } catch { }
                            if (string.IsNullOrEmpty(nodeID))
                            {
                                try { nodeID = node.aW.questNodeID; } catch { }
                            }
                            
                            if (string.IsNullOrEmpty(nodeID))
                            {
                                _log.LogWarning("    Could not determine node ID!");
                                continue;
                            }
                            
                            _log.LogInfo($"    Node ID: {nodeID}");
                            
                            // Check player inventory for required item count
                            var characters = UnityEngine.Object.FindObjectsOfType<Character>();
                            if (characters == null || characters.Length == 0) continue;
                            
                            int itemCount = characters[0].GetCountOfItemFromInventory(_itemID);
                            _log.LogInfo($"    Player has {itemCount} of item {_itemID}");
                            
                            // Check comparator (assume >=1 for now)
                            int requiredCount = 1;
                            var comparator = node.aW.myItemInfos?[0]?.comparator;
                            if (!string.IsNullOrEmpty(comparator))
                            {
                                // Parse ">=X" format - use temp var to preserve default if parse fails
                                if (comparator.StartsWith(">="))
                                {
                                    if (int.TryParse(comparator.Substring(2), out int parsed))
                                    {
                                        requiredCount = parsed;
                                    }
                                }
                            }
                            
                            _log.LogInfo($"    Required: {requiredCount} (comparator: {comparator})");
                            
                            if (itemCount >= requiredCount)
                            {
                                _log.LogInfo($"    CONDITION MET! Completing node...");
                                
                                // Complete the node manually!
                                questInfo.Final_NodeToDone(nodeID, true, 1, true);
                                
                                _log.LogInfo($"    Node completed successfully!");
                                
                                // Process subsequent action nodes (game doesn't auto-execute for mod quests)
                                ProcessNextActionNodes(questID, nodeID, new HashSet<string>());
                            }
                            else
                            {
                                _log.LogInfo($"    Condition not met yet ({itemCount} < {requiredCount})");
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _log.LogError($"Error in mod quest completion handler: {ex.Message}\n{ex.StackTrace}");
                }
            }
            
            /// <summary>
            /// Re-inject mod content if it's missing from the databases
            /// Called when OnMakeAction(All,0,0) fires, which happens on world load
            /// </summary>
            private static void TryReinjectContent()
            {
                try
                {
                    // Check if recipes need re-injection (database reloaded without recreating object)
                    var recipes = RecipeDatabase.recipes;
                    if (recipes != null && !recipes.ContainsKey(51000))
                    {
                        Plugin.Framework?.InjectContent();
                    }
                }
                catch { }
            }
            
            /// <summary>
            /// Process action nodes that follow a completed wait node.
            /// The game doesn't auto-execute action nodes for mod quests, so we do it manually.
            /// </summary>
            private static void ProcessNextActionNodes(string questID, string completedNodeID, HashSet<string> visited)
            {
                try
                {
                    // Cycle detection - prevent infinite recursion from graph loops
                    if (visited.Contains(completedNodeID))
                    {
                        _log.LogWarning($"    Cycle detected in quest {questID} at node {completedNodeID}. Stopping.");
                        return;
                    }
                    visited.Add(completedNodeID);
                    
                    var registered = Plugin.Framework.QuestRegistry.GetRegisteredQuest(questID);
                    if (registered?.Definition == null) return;
                    
                    var def = registered.Definition;
                    
                    // Find the completed stage
                    var completedStage = def.Stages.Find(s => s.Id == completedNodeID);
                    if (completedStage == null) return;
                    
                    // Process each next stage
                    foreach (var nextStageId in completedStage.NextStages)
                    {
                        var nextStage = def.Stages.Find(s => s.Id == nextStageId);
                        if (nextStage == null) continue;
                        
                        // Only handle action nodes
                        if (nextStage.Type != "action" || nextStage.Action == null) continue;
                        
                        _log.LogInfo($"    Executing action node: {nextStageId} (type: {nextStage.Action.Type})");
                        
                        ExecuteAction(nextStage.Action, questID);
                        
                        // Recursively process next action nodes
                        ProcessNextActionNodes(questID, nextStageId, visited);
                    }
                }
                catch (System.Exception ex)
                {
                    _log.LogError($"Error processing action nodes: {ex.Message}");
                }
            }
            
            /// <summary>
            /// Execute a single action from a quest definition
            /// </summary>
            private static void ExecuteAction(ActionDefinition action, string questID)
            {
                try
                {
                    switch (action.Type.ToLowerInvariant())
                    {
                        case "start_quest":
                            if (!string.IsNullOrEmpty(action.Param))
                            {
                                _log.LogInfo($"      -> Starting quest: {action.Param}");
                                HBT_QuestManager.StartQuestFromReward(action.Param);
                            }
                            break;
                            
                        case "quest_done":
                            _log.LogInfo($"      -> Quest done: {questID}");
                            // Quest completion is handled by the game after nodes complete
                            break;
                            
                        case "quest_fail":
                            _log.LogInfo($"      -> Quest failed: {questID}");
                            break;
                            
                        default:
                            _log.LogInfo($"      -> Action type not handled: {action.Type}");
                            break;
                    }
                }
                catch (System.Exception ex)
                {
                    _log.LogError($"Error executing action {action.Type}: {ex.Message}");
                }
            }
        }


        // Translation bypass - modded quests use defaultText instead of loading from XML
        [HarmonyPatch(typeof(HBT_QuestTranslationManager), "GetTextQuestTitle")]
        public static class QuestTranslation_GetTextQuestTitle_Patch
        {
            public static bool Prefix(string questID, string defaultText, ref string __result)
            {
                // Modded quests have format "mod_id:quest_id"
                if (questID.Contains(":"))
                {
                    __result = defaultText ?? questID;
                    return false; // Skip original, use defaultText
                }
                return true; // Continue to original for vanilla quests
            }
        }

        // CRITICAL: Intercept translation loading to bypass file-based system
        // The game tries to load from: Assets/HoboThor/Quests/_translatedjson_/en/{questID}.json
        // For mod quests with ":" in the ID, this fails (invalid filename)
        [HarmonyPatch(typeof(HBT_QuestTranslationManager), "GetTranslationOfQuest")]
        public static class QuestTranslation_GetTranslationOfQuest_Patch
        {
            public static bool Prefix(string questID, ref HBT_QuestTranslationManager.QuestOneTranslation __result)
            {
                // Check if this is a mod quest
                if (!questID.Contains(":"))
                {
                    return true; // Vanilla quest, let original handle it
                }
                
                // Mod quest detected
                
                try
                {
                    // Get our quest definition
                    var registered = Plugin.Framework.QuestRegistry.GetRegisteredQuest(questID);
                    if (registered == null || registered.Definition == null)
                    {
                        __result = null;
                        return false;
                    }
                    
                    var def = registered.Definition;
                    
                    // Create QuestOneTranslation
                    var translation = new HBT_QuestTranslationManager.QuestOneTranslation();
                    translation.questID = questID;
                    
                    // Create TranslatedStrings with our text
                    var strings = new TranslatedStrings();
                    var keys = new Il2CppSystem.Collections.Generic.List<string>();
                    var values = new Il2CppSystem.Collections.Generic.List<string>();
                    var dict = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
                    
                    // Add title
                    keys.Add("title");
                    values.Add(def.Title ?? questID);
                    dict.Add("title", def.Title ?? questID);
                    
                    // Add all stage descriptions
                    foreach (var stage in def.Stages)
                    {
                        var nodeKey = stage.Id;
                        var nodeText = stage.Description ?? $"Stage: {stage.Id}";
                        
                        keys.Add(nodeKey);
                        values.Add(nodeText);
                        dict.Add(nodeKey, nodeText);
                        

                    }
                    
                    strings.keys = keys;
                    strings.values = values;
                    strings.translated = dict;
                    
                    translation.translatedStrings = strings;
                    

                    
                    __result = translation;
                    return false; // Skip original
                }
                catch (System.Exception ex)
                {
                    _log.LogError($"    Error creating translation: {ex.Message}\n{ex.StackTrace}");
                    __result = null;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(HBT_QuestTranslationManager), "GetTextQuestNode", new System.Type[] { typeof(string), typeof(string) })]
        public static class QuestTranslation_GetTextQuestNode_Patch
        {
            public static bool Prefix(string questID, string nodeID, ref string __result)
            {
                try
                {
                    // Modded quests have format "mod_id:quest_id"
                    if (questID.Contains(":"))
                    {
                        // Try to find the actual description from our registry
                        var quest = Plugin.Framework.QuestRegistry.GetRegisteredQuest(questID);
                        if (quest != null)
                        {
                            // Simplest approach: Use the Definition stages
                            if (quest.Definition != null && quest.Definition.Stages != null)
                            {
                                var stage = quest.Definition.Stages.Find(s => s.Id == nodeID);
                                if (stage != null && !string.IsNullOrEmpty(stage.Description))
                                {
                                    __result = stage.Description;
                                    return false;
                                }
                            }
                        }
                        
                        // Fallback
                        __result = $"Quest stage: {nodeID}";
                        return false;
                    }
                }
                catch (System.Exception ex)
                {
                    _log.LogError($"Error in QuestTranslation patch: {ex.Message}");
                    // Fallback on error
                    if (questID.Contains(":"))
                    {
                        __result = $"Error: {nodeID}";
                        return false;
                    }
                }
                return true;
            }
        }
        
        // Correct patch for GetTextQuestNode which takes FMQ_Node
        [HarmonyPatch(typeof(HBT_QuestTranslationManager), "GetTextQuestNode", new System.Type[] { typeof(FMQ_Node) })]
        public static class QuestTranslation_GetTextQuestNode_Node_Patch
        {
            public static bool Prefix(FMQ_Node node, ref string __result)
            {
                try
                {
                    if (node == null) return true;
                    
                    string questID = null;
                    string nodeID = null;
                    
                    // Try to read node.id directly
                    try { nodeID = node.id; } catch { }
                    
                    // From ActionWait
                    if (node.aW != null)
                    {
                        try { questID = node.aW.questID; } catch { }
                        if (string.IsNullOrEmpty(nodeID))
                        {
                            try { nodeID = node.aW.questNodeID; } catch { }
                        }
                    }
                    
                    // From QuestMap
                    if (string.IsNullOrEmpty(questID) && node.questMap != null)
                    {
                        try { questID = node.questMap.QuestID; } catch { }
                    }
                    
                    // Only handle mod quests
                    if (!string.IsNullOrEmpty(questID) && questID.Contains(":"))
                    {
                        var quest = Plugin.Framework.QuestRegistry.GetRegisteredQuest(questID);
                        if (quest != null && quest.Definition != null)
                        {
                            // Try to find specific stage
                            if (!string.IsNullOrEmpty(nodeID))
                            {
                                var stage = quest.Definition.Stages.Find(s => s.Id == nodeID);
                                if (stage != null && !string.IsNullOrEmpty(stage.Description))
                                {
                                    __result = stage.Description;
                                    return false;
                                }
                            }
                            
                            // Fallback to first stage
                            if (quest.Definition.Stages.Count > 0)
                            {
                                var firstStage = quest.Definition.Stages[0];
                                if (!string.IsNullOrEmpty(firstStage.Description))
                                {
                                    __result = firstStage.Description;
                                    return false;
                                }
                            }
                            
                            __result = "Quest Stage (No Description)";
                            return false;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _log.LogError($"Error in GetTextQuestNode_Node patch: {ex.Message}");
                }
                return true;
            }
        }
        
        // ==================== Custom Bag Patches ====================
        
        /// <summary>
        /// Patch Bag.Clone to handle custom bags properly
        /// Custom bags return the instance directly to avoid shallow copy issues
        /// </summary>
        [HarmonyPatch(typeof(Bag), nameof(Bag.Clone), new System.Type[] { typeof(bool) })]
        public static class Bag_Clone_Patch
        {
            static bool Prefix(Bag __instance, bool wantSame, ref BaseItem __result)
            {
                if (ItemRegistry.IsCustomBag(__instance.id))
                {
                    // For custom bags, return the instance directly and mark as unequipped
                    // This mimics the EA version behavior and prevents clone issues
                    __result = __instance;
                    __instance.isEquiped = false;
                    
                    _log?.LogDebug($"[BagPatch] Custom bag {__instance.id} clone handled");
                    return false; // Skip original
                }
                return true; // Run original for vanilla bags
            }
        }
        
        /// <summary>
        /// Patch Bag.Load to skip loading for custom bags
        /// Custom bags don't need the vanilla Load() behavior
        /// </summary>
        [HarmonyPatch(typeof(Bag), nameof(Bag.Load))]
        public static class Bag_Load_Patch
        {
            static bool Prefix(Bag __instance)
            {
                if (ItemRegistry.IsCustomBag(__instance.id))
                {
                    // Skip Load for custom bags (EA version Load was empty)
                    _log?.LogDebug($"[BagPatch] Custom bag {__instance.id} load skipped");
                    return false;
                }
                return true;
            }
        }
        
        // ==================== Gear Negative Stat Fix ====================
        
        /// <summary>
        /// Fixes the bug where negative stats on gear items (like the Conspiracy Crown's
        /// -60 Charisma) are incorrectly turned into +1 by the game's rarity/condition
        /// scaling system.
        /// 
        /// HOW THE BUG WORKS:
        /// When the game spawns or updates gear, it calls Gear.ActualizeParameterChanges().
        /// This native method applies a multiplier based on item condition (durability)
        /// and rarity tier. The formula was designed for POSITIVE buffs only, so it
        /// includes an internal Math.Max(1, result) that clamps the final value to at
        /// least +1. This means a -60 Charisma base value gets scaled and then clamped
        /// to +1, turning a deliberately designed penalty into a tiny buff.
        ///
        /// HOW THIS FIX WORKS:
        /// We run a Postfix AFTER the native method finishes. We loop through all the
        /// gear's parameter changes, and for any stat where the base value was negative
        /// (meaning it was intentionally designed as a penalty/curse), we override the
        /// final calculated value back to the original base value.
        ///
        /// TRADE-OFF (accepted by design):
        /// Negative stats will NOT scale with item condition or rarity. A pristine
        /// Conspiracy Crown and a broken one will both give exactly -60 Charisma.
        /// This is intentional — cursed stats are fixed penalties, not scaling bonuses.
        ///
        /// SAFETY:
        /// This patch ONLY touches stats where baseValue is negative. All positive stats
        /// are left completely untouched, so vanilla gear behavior is 100% preserved.
        /// </summary>
        [HarmonyPatch(typeof(Gear))]
        public static class Gear_NegativeStatFix_Patches
        {
            [HarmonyPatch(nameof(Gear.ActualizeParameterChanges))]
            [HarmonyPostfix]
            public static void Postfix_Actualize(Gear __instance) => FixNegativeStats(__instance, "Actualize");

            [HarmonyPatch(nameof(Gear.OnChangeDurability))]
            [HarmonyPostfix]
            public static void Postfix_Durability(Gear __instance) => FixNegativeStats(__instance, "Durability");

            [HarmonyPatch(nameof(Gear.Load))]
            [HarmonyPostfix]
            public static void Postfix_Load(Gear __instance) => FixNegativeStats(__instance, "Load");

            private static void FixNegativeStats(Gear __instance, string source)
            {
                try
                {
                    // Guard: skip if the gear object or its stats list is null
                    if (__instance == null || __instance.parameterChanges == null) return;
                    
                    for (int i = 0; i < __instance.parameterChanges.Count; i++)
                    {
                        var paramChange = __instance.parameterChanges[i];
                        if (paramChange == null) continue;
                        
                        // Only intervene for NEGATIVE base values (curses/penalties).
                        // Positive stats are left entirely to the native rarity system.
                        if (paramChange.value < 0)
                        {
                            // Override the clamped finalValue back to the intended
                            // negative base value, bypassing the native Math.Max(1, ...).
                            paramChange.finalValue = paramChange.value;
                            
                            _log?.LogDebug($"[GearFix:{source}] Restored negative stat: " +
                                $"{paramChange.InfluencedParameterType} = {paramChange.value}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _log?.LogError($"[GearFix] Error in FixNegativeStats ({source}): {ex.Message}");
                }
            }
        }
        
        // ==================== Multi-Item Crafting Support (resultCount) ====================
        
        // Static field to track current recipe being crafted (needed because nested listener can't access parent)
        private static Recipe _currentCraftingRecipe = null;
        
        /// <summary>
        /// Track the recipe when bench crafting starts
        /// </summary>
        [HarmonyPatch(typeof(GUIBench_Craft), nameof(GUIBench_Craft.StartMiniGame))]
        public static class BenchCraft_StartMiniGame_Patch
        {
            static void Prefix(GUIBench_Craft __instance)
            {
                _currentCraftingRecipe = __instance.actualRecipe;
            }
        }
        
        /// <summary>
        /// Spawn extra items after bench crafting completes for resultCount > 1
        /// </summary>
        [HarmonyPatch(typeof(GUIBench_Craft.CraftingMinigameListener), nameof(GUIBench_Craft.CraftingMinigameListener.OnMiniGameResult))]
        public static class BenchCraft_ResultCount_Patch
        {
            static void Postfix(MiniGameContent.Result result)
            {
                // Only process on success (good or excellent results)
                if (result != MiniGameContent.Result.good && result != MiniGameContent.Result.excellent) return;
                if (_currentCraftingRecipe == null) return;
                
                try
                {
                    var recipeId = _currentCraftingRecipe.id;
                    int resultCount = RecipeRegistry.GetResultCount(recipeId);
                    
                    // Only do extra work if resultCount > 1
                    if (resultCount <= 1)
                    {
                        _currentCraftingRecipe = null;
                        return;
                    }
                    
                    // Get player
                    var characters = Object.FindObjectsOfType<Character>();
                    if (characters == null || characters.Length == 0)
                    {
                        _currentCraftingRecipe = null;
                        return;
                    }
                    
                    var player = characters[0];
                    var resultItem = _currentCraftingRecipe.resultItem;
                    
                    if (resultItem == null)
                    {
                        _currentCraftingRecipe = null;
                        return;
                    }
                    
                    // Spawn extra items (game already spawned 1)
                    int extraCount = resultCount - 1;
                    for (int i = 0; i < extraCount; i++)
                    {
                        var cloned = resultItem.Clone()?.TryCast<BaseItem>();
                        if (cloned != null)
                        {
                            player.AddItemToInventory(cloned, true);
                        }
                    }
                    
                    _log?.LogInfo($"[ResultCount] Spawned {extraCount} extra item(s) for recipe {recipeId} (total: {resultCount})");
                }
                catch (System.Exception ex)
                {
                    _log?.LogError($"[ResultCount] Error spawning extra items: {ex.Message}");
                }
                finally
                {
                    _currentCraftingRecipe = null;
                }
            }
        }
        
        // Track recipe for sheet crafting too
        private static Recipe _currentSheetCraftingRecipe = null;
        
        /// <summary>
        /// Track the recipe when sheet crafting starts
        /// </summary>
        [HarmonyPatch(typeof(GUISheetCrafting), "StartMiniGame")]
        public static class SheetCraft_StartMiniGame_Patch
        {
            static void Prefix(GUISheetCrafting __instance)
            {
                _currentSheetCraftingRecipe = __instance.actualRecipe;
            }
        }
        
        /// <summary>
        /// Spawn extra items after sheet crafting completes for resultCount > 1
        /// </summary>
        [HarmonyPatch(typeof(GUISheetCrafting.CraftingMinigameListener), nameof(GUISheetCrafting.CraftingMinigameListener.OnMiniGameResult))]
        public static class SheetCraft_ResultCount_Patch
        {
            static void Postfix(MiniGameContent.Result result)
            {
                // Only process on success (good or excellent results)
                if (result != MiniGameContent.Result.good && result != MiniGameContent.Result.excellent) return;
                if (_currentSheetCraftingRecipe == null) return;
                
                try
                {
                    var recipeId = _currentSheetCraftingRecipe.id;
                    int resultCount = RecipeRegistry.GetResultCount(recipeId);
                    
                    // Only do extra work if resultCount > 1
                    if (resultCount <= 1)
                    {
                        _currentSheetCraftingRecipe = null;
                        return;
                    }
                    
                    // Get player
                    var characters = Object.FindObjectsOfType<Character>();
                    if (characters == null || characters.Length == 0)
                    {
                        _currentSheetCraftingRecipe = null;
                        return;
                    }
                    
                    var player = characters[0];
                    var resultItem = _currentSheetCraftingRecipe.resultItem;
                    
                    if (resultItem == null)
                    {
                        _currentSheetCraftingRecipe = null;
                        return;
                    }
                    
                    // Spawn extra items (game already spawned 1)
                    int extraCount = resultCount - 1;
                    for (int i = 0; i < extraCount; i++)
                    {
                        var cloned = resultItem.Clone()?.TryCast<BaseItem>();
                        if (cloned != null)
                        {
                            player.AddItemToInventory(cloned, true);
                        }
                    }
                    
                    _log?.LogInfo($"[ResultCount] (Sheet) Spawned {extraCount} extra item(s) for recipe {recipeId} (total: {resultCount})");
                }
                catch (System.Exception ex)
                {
                    _log?.LogError($"[ResultCount] (Sheet) Error spawning extra items: {ex.Message}");
                }
                finally
                {
                    _currentSheetCraftingRecipe = null;
                }
            }
        }
    }
}
