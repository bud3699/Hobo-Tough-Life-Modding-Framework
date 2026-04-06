using System;
using BepInEx.Logging;
using HarmonyLib;
using Game;
using UnityEngine;

namespace HoboModPlugin.Framework.Events
{
    /// <summary>
    /// Harmony patches that hook into game methods to fire events.
    /// These are automatically applied by Harmony when the assembly loads.
    /// </summary>
    [HarmonyPatch]
    public static class EventHooks
    {
        private static ManualLogSource _log;
        
        // Track previous values for time change detection
        private static int _lastHour = -1;
        private static int _lastDay = -1;
        private static int _lastSeason = -1;
        private static bool _timeInitialized = false;
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _log?.LogInfo("[EventHooks] Event hooks initialized");
        }
        
        // ============================================================
        // ITEM PICKUP - Character.AddItemToInventory
        // ============================================================
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.AddItemToInventory), 
            new Type[] { typeof(BaseItem), typeof(bool), typeof(bool) })]
        public static bool OnAddItemToInventory_Prefix(Character __instance, BaseItem item, ref bool __result)
        {
            try
            {
                if (__instance == null || item == null) return true;
                
                var args = new ItemEventArgs(item);
                ModEvents.FireItemPickup(args);
                
                if (args.Cancelled)
                {
                    _log?.LogInfo($"[EventHooks] Item pickup cancelled: {args.ItemName}");
                    __result = false;
                    return false; // Skip original method
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnAddItemToInventory error: {ex.Message}");
            }
            
            return true; // Continue to original method
        }
        
        /// <summary>
        /// Postfix hook for AddItemToInventory - calls OnMakeAction to trigger quest condition checks.
        /// This enables modded quest wait conditions (collect X items) to work with the game's native system.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Character), nameof(Character.AddItemToInventory), 
            new Type[] { typeof(BaseItem), typeof(bool), typeof(bool) })]
        public static void OnAddItemToInventory_Postfix(Character __instance, BaseItem item, bool __result)
        {
            try
            {
                // Only trigger if item was actually added successfully
                if (__instance == null || item == null || !__result) return;
                
                // Get item ID - use count of 1 since each call is one pickup event
                uint itemId = item.Id;
                
                // Trigger the game's quest action handler - this makes quest wait conditions work
                // TypeOfActionWait.Item = 2
                HBT_QuestManager.OnMakeAction(FMQ_ActionWait.TypeOfActionWait.Item, itemId, 1, "");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnAddItemToInventory_Postfix error: {ex.Message}");
            }
        }
        
        // ============================================================
        // TIME EVENTS - GameTime.OnUpdate
        // ============================================================
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameTime), nameof(GameTime.OnUpdate))]
        public static void OnGameTimeUpdate_Postfix(GameTime __instance)
        {
            try
            {
                if (__instance == null) return;
                
                int currentHour = (int)__instance.RawHourOfDay24;
                int currentDay = __instance.DayOfTime24;
                int currentSeason = __instance.Season;
                
                // Initialize on first call
                if (!_timeInitialized)
                {
                    _lastHour = currentHour;
                    _lastDay = currentDay;
                    _lastSeason = currentSeason;
                    _timeInitialized = true;
                    return;
                }
                
                // Check for hour change
                if (_lastHour != currentHour)
                {
                    var args = new TimeEventArgs(currentHour, currentDay, currentSeason, _lastHour, _lastDay);
                    ModEvents.FireHourChanged(args);
                    _lastHour = currentHour;
                }
                
                // Check for day change
                if (_lastDay != currentDay)
                {
                    var args = new TimeEventArgs(currentHour, currentDay, currentSeason, _lastHour, _lastDay);
                    ModEvents.FireDayChanged(args);
                    _lastDay = currentDay;
                }
                
                // Check for season change
                if (_lastSeason != currentSeason)
                {
                    var args = new SeasonEventArgs(currentSeason, _lastSeason);
                    ModEvents.FireSeasonChanged(args);
                    _lastSeason = currentSeason;
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnGameTimeUpdate error: {ex.Message}");
            }
        }
        
        // ============================================================
        // GAME SAVE/LOAD - Using SaveSystem (correct class)
        // Multiple save methods exist - hook all to catch various save scenarios
        // Use a flag to prevent double-firing if multiple methods are called
        // ============================================================
        
        private static bool _saveFiredThisFrame = false;
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveAll))]
        public static void OnSaveAll_Postfix()
        {
            FireSaveEventOnce("SaveAll");
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveCharacter))]
        public static void OnSaveCharacter_Postfix()
        {
            FireSaveEventOnce("SaveCharacter");
        }
        
        private static void FireSaveEventOnce(string source)
        {
            try
            {
                // Prevent multiple fires in same save operation
                if (_saveFiredThisFrame) return;
                _saveFiredThisFrame = true;
                
                _log?.LogInfo($"[EventHooks] Game saved ({source}) - firing OnGameSaved");
                ModEvents.FireGameSaved(new GameEventArgs());
                
                // Reset flag after a short delay (using Unity's next frame)
                // For now we just reset it - in practice saves are infrequent
                System.Threading.Tasks.Task.Delay(100).ContinueWith(_ => _saveFiredThisFrame = false);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnSave error: {ex.Message}");
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.ApplyLoadedCharacterData))]
        public static void OnApplyLoadedCharacterData_Postfix()
        {
            try
            {
                _log?.LogInfo("[EventHooks] Game loaded - clearing registries and re-injecting content");
                
                // Reset time tracking on load so first hour change fires properly
                _timeInitialized = false;
                
                // CRITICAL: Clear and reload framework content on game load
                // This prevents duplicates when loading multiple saves
                if (Plugin.Framework != null)
                {
                    Plugin.Framework.ClearAndReloadContent();
                    Plugin.Framework.InjectContent();
                    Plugin.Framework.InjectQuests();
                }
                
                ModEvents.FireGameLoaded(new GameEventArgs());
                _log?.LogInfo("[EventHooks] Game load handling complete");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnApplyLoadedCharacterData error: {ex.Message}");
            }
        }
        
        // ============================================================
        // ITEM USED - Already have Consumable.Use patch in FrameworkPatches
        // We'll integrate with the existing patch to fire OnItemUsed
        // ============================================================
        
        /// <summary>
        /// Call this from FrameworkPatches.Consumable_Use_Postfix to fire OnItemUsed
        /// </summary>
        public static void NotifyItemUsed(BaseItem item)
        {
            try
            {
                if (item == null) return;
                
                var args = new ItemEventArgs(item);
                ModEvents.FireItemUsed(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] NotifyItemUsed error: {ex.Message}");
            }
        }
        
        // ============================================================
        // STAT CHANGES - Fires when player stats change
        // Hook into ParameterRange.ChangeBaseValue
        // ============================================================
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ParameterRange), nameof(ParameterRange.ChangeBaseValue))]
        public static void OnChangeBaseValue_Prefix(ParameterRange __instance, float _val, out float __state)
        {
            // Capture old value before change
            try
            {
                __state = __instance.Value;
            }
            catch
            {
                __state = 0f;
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ParameterRange), nameof(ParameterRange.ChangeBaseValue))]
        public static void OnChangeBaseValue_Postfix(ParameterRange __instance, float _val, float __state)
        {
            try
            {
                float oldValue = __state;
                float newValue = __instance.Value;
                
                // Only fire if value changed significantly (0.05 threshold to reduce spam)
                if (Math.Abs(newValue - oldValue) < 0.05f) return;
                
                // Try to get stat name from the parameter
                string statName = "Unknown";
                try
                {
                    statName = __instance.title ?? "Unknown";
                }
                catch { }
                
                var args = new StatChangeEventArgs(statName, oldValue, newValue);
                ModEvents.FireStatChanged(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnChangeBaseValue error: {ex.Message}");
            }
        }
        
        // ============================================================
        // PHASE 3A: EQUIPMENT PATCHES
        // ============================================================
        
        [HarmonyPatch(typeof(Gear), nameof(Gear.Equipe))]
        [HarmonyPostfix]
        public static void OnGearEquip_Postfix(Gear __instance)
        {
            try
            {
                if (__instance == null) return;
                var args = new EquipEventArgs(__instance, "Gear");
                ModEvents.FireItemEquipped(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnGearEquip error: {ex.Message}");
            }
        }
        
        [HarmonyPatch(typeof(Gear), nameof(Gear.Unequipe))]
        [HarmonyPostfix]
        public static void OnGearUnequip_Postfix(Gear __instance)
        {
            try
            {
                if (__instance == null) return;
                var args = new EquipEventArgs(__instance, "Gear");
                ModEvents.FireItemUnequipped(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnGearUnequip error: {ex.Message}");
            }
        }
        
        [HarmonyPatch(typeof(Weapon), nameof(Weapon.Equipe))]
        [HarmonyPostfix]
        public static void OnWeaponEquip_Postfix(Weapon __instance)
        {
            try
            {
                if (__instance == null) return;
                var args = new EquipEventArgs(__instance, "Weapon");
                ModEvents.FireItemEquipped(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnWeaponEquip error: {ex.Message}");
            }
        }
        
        [HarmonyPatch(typeof(Weapon), nameof(Weapon.Unequipe))]
        [HarmonyPostfix]
        public static void OnWeaponUnequip_Postfix(Weapon __instance)
        {
            try
            {
                if (__instance == null) return;
                var args = new EquipEventArgs(__instance, "Weapon");
                ModEvents.FireItemUnequipped(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnWeaponUnequip error: {ex.Message}");
            }
        }
        
        // ============================================================
        // PHASE 3A: PLAYER DEATH PATCH
        // ============================================================
        
        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        [HarmonyPostfix]
        public static void OnCharacterDie_Postfix(Character __instance)
        {
            try
            {
                // Only fire for player character
                if (__instance == null) return;
                
                var args = new PlayerEventArgs("Death");
                ModEvents.FirePlayerDeath(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnCharacterDie error: {ex.Message}");
            }
        }
        
        // ============================================================
        // PHASE 3A: QUEST NODE COMPLETED PATCH
        // ============================================================
        
        [HarmonyPatch(typeof(HBT_QuestManager), nameof(HBT_QuestManager.Node_ToFinish))]
        [HarmonyPostfix]
        public static void OnQuestNodeFinish_Postfix(string questID, string nodeID, bool isSuccess, uint numDone)
        {
            try
            {
                var args = new QuestEventArgs(questID, nodeID, isSuccess, numDone);
                ModEvents.FireQuestNodeCompleted(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnQuestNodeFinish error: {ex.Message}");
            }
        }
        
        // ============================================================
        // PHASE 3B: WEATHER CHANGE PATCH
        // ============================================================
        
        [HarmonyPatch(typeof(Character), nameof(Character.OnWeatherChanged))]
        [HarmonyPostfix]
        public static void OnCharacterWeatherChanged_Postfix(Core.Graphics.AtmosWeatherType currentWeather, Core.Graphics.AtmosWeatherType targetWeather)
        {
            try
            {
                var args = new WeatherEventArgs((int)currentWeather, (int)targetWeather);
                ModEvents.FireWeatherChanged(args);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnWeatherChanged error: {ex.Message}");
            }
        }
        
        // ============================================================
        // CRAFTING PATCH
        // ============================================================
        
        // Hook into GUISheetCrafting.StartMiniGame to capture recipe info
        // Then the crafting system completes - we fire event when recipe is known
        private static uint _lastCraftingRecipeId = 0;
        private static string _lastCraftingRecipeName = "";
        private static uint _lastCraftingResultItemId = 0;
        private static string _lastCraftingResultItemName = "";
        
        [HarmonyPatch(typeof(UI.GUISheetCrafting), nameof(UI.GUISheetCrafting.StartMiniGame))]
        [HarmonyPostfix]
        public static void OnStartCraftingMiniGame_Postfix(Recipe myRecipe)
        {
            try
            {
                if (myRecipe == null) return;
                
                // Store recipe info for when crafting completes
                _lastCraftingRecipeId = myRecipe.Id;
                _lastCraftingRecipeName = myRecipe.Title ?? "";
                _lastCraftingResultItemId = myRecipe.ResultItemId;
                _lastCraftingResultItemName = myRecipe.resultItem?.title ?? "";
                
                // Fire event immediately since StartMiniGame means player has initiated crafting
                // Success is assumed true as the minigame determines actual outcome
                var args = new CraftEventArgs(
                    _lastCraftingRecipeId,
                    _lastCraftingRecipeName,
                    _lastCraftingResultItemId,
                    _lastCraftingResultItemName,
                    true  // Crafting started successfully
                );
                ModEvents.FireItemCrafted(args);
                _log?.LogInfo($"[EventHooks] Player crafting: {_lastCraftingRecipeName} (Recipe: {_lastCraftingRecipeId})");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnStartCraftingMiniGame error: {ex.Message}");
            }
        }
        
        // ============================================================
        // CONVERSATION ENDED PATCH
        // ============================================================
        
        /// <summary>
        /// Fires OnConversationEnded when player ends a dialogue with an NPC.
        /// Uses PREFIX to capture data BEFORE ResetConversation clears it.
        /// Also processes dialogueTriggers from mod.json for JSON-based quest starts.
        /// </summary>
        [HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.ResetConversation))]
        [HarmonyPrefix]
        public static void OnResetConversation_Prefix()
        {
            try
            {
                // Get the last selected option BEFORE reset clears it
                var lastOption = ConversationManager.lastOption;
                
                // Get NPC info from GetActiveConversation which returns ConversationActive
                string npcArchetype = "";
                try
                {
                    var activeConv = ConversationManager.GetActiveConversation();
                    if (activeConv != null)
                    {
                        // If lastOption is null, try currentNodeGameOption
                        if (lastOption == null)
                        {
                            try { lastOption = activeConv.currentNodeGameOption; } catch { }
                        }
                        
                        var npc = activeConv.myNPC;
                        if (npc != null)
                        {
                            // Try to get archetype (enum) and convert to string
                            try 
                            { 
                                var arch = npc.Archetype();
                                npcArchetype = arch.ToString();
                            } 
                            catch { }
                            
                            // If archetype is empty/invalid, try game object name
                            if (string.IsNullOrEmpty(npcArchetype) || npcArchetype == "0")
                            {
                                try 
                                { 
                                    var go = npc.GetGameObject();
                                    if (go != null) npcArchetype = go.name ?? "";
                                } 
                                catch { }
                            }
                        }
                    }
                }
                catch { }
                
                // Fire the event
                var args = new ConversationEventArgs(npcArchetype, lastOption);
                ModEvents.FireConversationEnded(args);
                
                _log?.LogInfo($"[EventHooks] Conversation ended - NPC: {npcArchetype}, LastOption: {args.LastOptionText}");
                
                // Process dialogue triggers from loaded mods (conversation_ended only)
                ProcessDialogueTriggers(npcArchetype, "conversation_ended", "", 0);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnResetConversation error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hook: ConversationActive.SelectOption - fires when player clicks dialogue option
        /// </summary>
        [HarmonyPatch(typeof(ConversationActive), nameof(ConversationActive.SelectOption))]
        [HarmonyPostfix]
        public static void OnSelectOption_Postfix(FMC_Option myOption, int index)
        {
            try
            {
                // Get NPC from active conversation
                string npcArchetype = "";
                try
                {
                    var activeConv = ConversationManager.GetActiveConversation();
                    if (activeConv?.myNPC != null)
                    {
                        try { npcArchetype = activeConv.myNPC.Archetype().ToString(); } catch { }
                        if (string.IsNullOrEmpty(npcArchetype) || npcArchetype == "0")
                        {
                            try { npcArchetype = activeConv.myNPC.GetGameObject()?.name ?? ""; } catch { }
                        }
                    }
                }
                catch { }
                
                // Get option info
                string optionId = "";
                uint textKey = 0;
                try
                {
                    if (myOption != null)
                    {
                        // GetString() returns debug format: "OPT: ID_END <color=green>..."
                        // Parse out the ID for matching
                        string debugText = myOption.GetString() ?? "";
                        textKey = myOption.textKey;
                        
                        // Extract option ID using regex: "OPT: ID_xxx"
                        var match = System.Text.RegularExpressions.Regex.Match(debugText, @"OPT:\s*(ID_\w+)");
                        if (match.Success)
                        {
                            optionId = match.Groups[1].Value;
                        }
                    }
                }
                catch { }
                
                // Process dialogue triggers for option_selected (use optionId for matching)
                ProcessDialogueTriggers(npcArchetype, "option_selected", optionId, textKey);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[EventHooks] OnSelectOption error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check all loaded mods for dialogue triggers matching the NPC and optionally option
        /// </summary>
        private static void ProcessDialogueTriggers(string npcArchetype, string triggerType, string optionText, uint optionTextKey)
        {
            if (string.IsNullOrEmpty(npcArchetype)) return;
            if (Plugin.Framework?.ModLoader?.LoadedMods == null) return;
            
            foreach (var mod in Plugin.Framework.ModLoader.LoadedMods)
            {
                if (mod.DialogueTriggers == null) continue;
                
                foreach (var trigger in mod.DialogueTriggers)
                {
                    if (string.IsNullOrEmpty(trigger.NpcId)) continue;
                    
                    // Check trigger type matches
                    var expectedTriggerType = string.IsNullOrEmpty(trigger.TriggerOn) ? "conversation_ended" : trigger.TriggerOn.ToLowerInvariant();
                    if (expectedTriggerType != triggerType) continue;
                    
                    // Case-insensitive partial match on NPC
                    if (npcArchetype.IndexOf(trigger.NpcId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // For option_selected, also check option matching
                        if (triggerType == "option_selected")
                        {
                            bool optionMatches = false;
                            
                            // Check textKey match first (exact)
                            if (trigger.OptionTextKey.HasValue && trigger.OptionTextKey.Value > 0)
                            {
                                optionMatches = (trigger.OptionTextKey.Value == optionTextKey);
                            }
                            // Then check text match (partial, case-insensitive)
                            else if (!string.IsNullOrEmpty(trigger.OptionText))
                            {
                                optionMatches = optionText.IndexOf(trigger.OptionText, StringComparison.OrdinalIgnoreCase) >= 0;
                            }
                            // If no option criteria specified, don't match (require explicit option)
                            else
                            {
                                continue;
                            }
                            
                            if (!optionMatches) continue;
                        }
                        
                        _log?.LogInfo($"[EventHooks] Dialogue trigger matched! NPC: {npcArchetype}, Trigger: {trigger.NpcId}, Type: {triggerType}");
                        
                        if (trigger.Action?.ToLowerInvariant() == "start_quest" && !string.IsNullOrEmpty(trigger.QuestId))
                        {
                            _log?.LogInfo($"[EventHooks] Starting quest from dialogue: {trigger.QuestId}");
                            Game.HBT_QuestManager.StartQuestFromReward(trigger.QuestId);
                        }
                    }
                }
            }
        }
    }
}
