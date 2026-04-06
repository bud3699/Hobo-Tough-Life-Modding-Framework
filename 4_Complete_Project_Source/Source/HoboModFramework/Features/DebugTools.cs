using BepInEx.Logging;
using UnityEngine;
using Game;
using Core.Graphics;
using Core.Strings;

namespace HoboModPlugin.Features
{
    /// <summary>
    /// Debug and exploration tools for development and testing
    /// </summary>
    public static class DebugTools
    {
        /// <summary>
        /// Explore and log items from the database
        /// </summary>
        public static void ExploreItemDatabase()
        {
            try
            {
                var items = ItemDatabase.items;
                if (items == null)
                {
                    Plugin.Log.LogWarning("ItemDatabase.items is null - game might not be loaded");
                    return;
                }

                Plugin.Log.LogInfo($"Total items in database: {items.Count}");
                
                int count = 0;
                foreach (var entry in items)
                {
                    if (count >= 15) break;
                    var item = entry.Value;
                    if (item != null)
                    {
                        var itemType = item.GetItemType();
                        Plugin.Log.LogInfo($"  ID[{entry.Key}] '{item.title}' - Type:{itemType}, Price:{item.price}, Weight:{item.weight}");
                    }
                    count++;
                }
                
                Plugin.Log.LogInfo("SUCCESS! Item database is accessible!");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Dump ALL items from the database to a text file for easy searching
        /// Press F11 to trigger this
        /// </summary>
        public static void DumpAllItemsToFile()
        {
            try
            {
                var items = ItemDatabase.items;
                if (items == null)
                {
                    Plugin.Log.LogWarning("ItemDatabase.items is null - game might not be loaded");
                    return;
                }

                string filePath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "ItemDump.txt");

                // Get localization dictionary
                var stringsItems = StringsManager.strings_items;
                var locDict = stringsItems?.translatedInt;

                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"=== ITEM DATABASE DUMP ({System.DateTime.Now}) ===");
                lines.Add($"Total items: {items.Count}");
                lines.Add("");

                foreach (var entry in items)
                {
                    var item = entry.Value;
                    if (item != null)
                    {
                        var itemType = item.GetItemType();
                        
                        // Get localized name from StringsManager
                        string localizedName = "";
                        if (locDict != null && locDict.ContainsKey(item.titleKey))
                        {
                            localizedName = locDict[item.titleKey];
                        }
                        else
                        {
                            localizedName = item.title; // Fallback
                        }
                        
                        string line = $"ID:{entry.Key,5} | Type:{itemType,-12} | Price:{item.price,5} | Weight:{item.weight,5:F1} | Name: {localizedName}";
                        lines.Add(line);
                    }
                }

                System.IO.File.WriteAllLines(filePath, lines);
                Plugin.Log.LogInfo($"=== DUMPED {items.Count} ITEMS TO: {filePath} ===");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error dumping items: {ex.Message}");
            }
        }

        /// <summary>
        /// Dump all bag items with their capacity values to understand game mechanics
        /// Press F9 (with debug mode) to trigger
        /// </summary>
        public static void DumpBagCapacities()
        {
            try
            {
                var items = ItemDatabase.items;
                if (items == null)
                {
                    Plugin.Log.LogWarning("ItemDatabase.items is null");
                    return;
                }

                Plugin.Log.LogInfo("=== BAG CAPACITY RESEARCH ===");
                Plugin.Log.LogInfo("ID      | Price | Weight | Capacity | Title");
                Plugin.Log.LogInfo("--------|-------|--------|----------|------");

                string filePath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "BagCapacityDump.txt");

                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"=== BAG CAPACITY DUMP ({System.DateTime.Now}) ===");
                lines.Add("ID      | Price | Weight | Capacity | Title");
                lines.Add("--------|-------|--------|----------|------");

                int count = 0;
                foreach (var entry in items)
                {
                    var item = entry.Value;
                    if (item == null) continue;

                    var bag = item.TryCast<Bag>();
                    if (bag != null)
                    {
                        string line = $"ID:{entry.Key,4} | {item.price,5} | {item.weight,6:F1} | {bag.capacity,8:F1} | {item.title}";
                        Plugin.Log.LogInfo(line);
                        lines.Add(line);
                        count++;
                    }
                }

                lines.Add("");
                lines.Add($"Total bags: {count}");
                System.IO.File.WriteAllLines(filePath, lines);

                Plugin.Log.LogInfo($"=== FOUND {count} BAGS - Saved to {filePath} ===");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error dumping bags: {ex.Message}");
            }
        }


        /// <summary>
        /// Search for items by name keywords
        /// Searches for: cart, bag, advanced
        /// </summary>
        public static void SearchItemsByName()
        {
            try
            {
                var items = ItemDatabase.items;
                if (items == null)
                {
                    Plugin.Log.LogWarning("ItemDatabase.items is null");
                    return;
                }

                string[] keywords = { "cart", "bag", "advanced", "shopping" };
                Plugin.Log.LogInfo($"=== SEARCHING FOR ITEMS: {string.Join(", ", keywords)} ===");

                int found = 0;
                foreach (var entry in items)
                {
                    var item = entry.Value;
                    if (item == null || string.IsNullOrEmpty(item.title)) continue;

                    string title = item.title.ToLowerInvariant();
                    foreach (var keyword in keywords)
                    {
                        if (title.Contains(keyword.ToLowerInvariant()))
                        {
                            var itemType = item.GetItemType();
                            Plugin.Log.LogInfo($"  FOUND: ID[{entry.Key}] '{item.title}' Type:{itemType}");
                            found++;
                            break;
                        }
                    }
                }

                if (found == 0)
                {
                    Plugin.Log.LogInfo("  No items found matching keywords");
                    Plugin.Log.LogInfo("  Use F11 to dump ALL items to file for manual search");
                }
                else
                {
                    Plugin.Log.LogInfo($"=== Found {found} matching items ===");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error searching items: {ex.Message}");
            }
        }


        /// <summary>
        /// Find and log all Gear items in the database
        /// </summary>
        public static void ExploreGearItems()
        {
            try
            {
                var items = ItemDatabase.items;
                if (items == null)
                {
                    Plugin.Log.LogWarning("ItemDatabase.items is null");
                    return;
                }

                Plugin.Log.LogInfo("=== SEARCHING FOR GEAR ITEMS ===");
                int gearCount = 0;
                
                foreach (var entry in items)
                {
                    var item = entry.Value;
                    if (item == null) continue;
                    
                    var gear = item.TryCast<Gear>();
                    if (gear != null)
                    {
                        Plugin.Log.LogInfo($"  GEAR ID[{entry.Key}] '{item.title}' Category:{gear.category} Warm:{gear.warmResistance} Wet:{gear.wetResistance}");
                        gearCount++;
                        
                        if (gearCount >= 20) 
                        {
                            Plugin.Log.LogInfo("  ... (limited to 20 results)");
                            break;
                        }
                    }
                }
                
                Plugin.Log.LogInfo($"=== Found {gearCount} Gear items ===");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error exploring gear: {ex.Message}");
            }
        }

        /// <summary>
        /// Find and log all Weapon items in the database
        /// </summary>
        public static void ExploreWeaponItems()
        {
            try
            {
                var items = ItemDatabase.items;
                if (items == null)
                {
                    Plugin.Log.LogWarning("ItemDatabase.items is null");
                    return;
                }

                Plugin.Log.LogInfo("=== SEARCHING FOR WEAPON ITEMS ===");
                int weaponCount = 0;
                
                foreach (var entry in items)
                {
                    var item = entry.Value;
                    if (item == null) continue;
                    
                    var weapon = item.TryCast<Weapon>();
                    if (weapon != null)
                    {
                        Plugin.Log.LogInfo($"  WEAPON ID[{entry.Key}] '{item.title}' Atk:{weapon.attack} Def:{weapon.defense} Crit:{weapon.criticalChance}%");
                        weaponCount++;
                        
                        if (weaponCount >= 20) 
                        {
                            Plugin.Log.LogInfo("  ... (limited to 20 results)");
                            break;
                        }
                    }
                }
                
                Plugin.Log.LogInfo($"=== Found {weaponCount} Weapon items ===");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error exploring weapons: {ex.Message}");
            }
        }

        /// <summary>
        /// Spawn a mod item into player inventory
        /// Uses same pattern as working TryAddItem function
        /// </summary>
        public static void SpawnModItem(string fullItemId)
        {
            try
            {
                Plugin.Log.LogInfo("=== SpawnModItem ===");
                
                // CORRECT PATTERN: Use FindObjectsOfType like TryAddItem
                var characters = Object.FindObjectsOfType<Character>();
                if (characters == null || characters.Length == 0)
                {
                    Plugin.Log.LogWarning("No Character found in scene");
                    return;
                }
                
                var character = characters[0];
                if (character == null)
                {
                    Plugin.Log.LogWarning("Character is null");
                    return;
                }
                
                var items = ItemDatabase.items;
                if (items == null || items.Count == 0)
                {
                    Plugin.Log.LogWarning("ItemDatabase not available");
                    return;
                }
                
                Plugin.Log.LogInfo($"  Requested item: {fullItemId}");
                
                // Look up the item in our ItemRegistry by fullItemId
                var registered = Plugin.Framework.ItemRegistry.GetItem(fullItemId);
                if (registered == null)
                {
                    Plugin.Log.LogWarning($"Item '{fullItemId}' not found in ItemRegistry");
                    Plugin.Log.LogWarning("Check: Is the item defined in items/*.json?");
                    return;
                }
                
                uint modItemId = registered.NumericId;
                Plugin.Log.LogInfo($"  Found registered item: {fullItemId} -> ID {modItemId}");
                
                // CORRECT PATTERN: Use ItemDatabase.GetItem to create instance
                var newItem = ItemDatabase.GetItem(modItemId, 1, false, true);
                if (newItem == null)
                {
                    Plugin.Log.LogError("Failed to create item from ItemDatabase.GetItem!");
                    return;
                }
                
                Plugin.Log.LogInfo($"  Created item: '{newItem.title}'");
                
                // CORRECT PATTERN: Use AddItemToInventory, not inventory.Add
                bool success = character.AddItemToInventory(newItem, true, false);
                
                if (success)
                {
                    Plugin.Log.LogInfo($"SUCCESS: Added '{newItem.title}' to inventory!");
                }
                else
                {
                    Plugin.Log.LogWarning("AddItemToInventory returned false - trying fallback...");
                    success = character.AddItemToInventory(newItem, false);
                    if (success)
                    {
                        Plugin.Log.LogInfo($"SUCCESS (fallback): Added '{newItem.title}' to inventory!");
                    }
                    else
                    {
                        Plugin.Log.LogError("Both add methods failed!");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error spawning item: {ex.Message}");
            }
        }

        /// <summary>
        /// Spawn a vanilla game item by numeric ID
        /// </summary>
        public static void SpawnVanillaItem(uint itemId)
        {
            try
            {
                Plugin.Log.LogInfo($"=== SpawnVanillaItem ID:{itemId} ===");
                
                var characters = Object.FindObjectsOfType<Character>();
                if (characters == null || characters.Length == 0)
                {
                    Plugin.Log.LogWarning("No Character found in scene");
                    return;
                }
                
                var character = characters[0];
                var items = ItemDatabase.items;
                
                if (items == null || !items.ContainsKey(itemId))
                {
                    Plugin.Log.LogWarning($"Item ID {itemId} not found in database");
                    return;
                }
                
                var newItem = ItemDatabase.GetItem(itemId, 1, false, true);
                if (newItem == null)
                {
                    Plugin.Log.LogError("Failed to create item!");
                    return;
                }
                
                Plugin.Log.LogInfo($"  Created: '{newItem.title}'");
                
                bool success = character.AddItemToInventory(newItem, true, false);
                if (success)
                {
                    Plugin.Log.LogInfo($"SUCCESS: Added '{newItem.title}' to inventory!");
                }
                else
                {
                    Plugin.Log.LogInfo($"Item not added (may have been blocked by mod event)");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error spawning vanilla item: {ex.Message}");
            }
        }

        /// <summary>
        /// Get and log player information
        /// </summary>
        public static void GetPlayerInfo()
        {
            try
            {
                var playerManager = PlayerManager.instance;
                if (playerManager == null)
                {
                    Plugin.Log.LogWarning("PlayerManager.instance is null - not in game?");
                    return;
                }

                Plugin.Log.LogInfo("=== Player Manager Found! ===");
                Plugin.Log.LogInfo($"  Is Visible: {playerManager.isVisible}");
                Plugin.Log.LogInfo($"  In Conversation: {playerManager.inConversation}");
                Plugin.Log.LogInfo($"  Is Sprinting: {playerManager.inSprint}");
                Plugin.Log.LogInfo($"  Sprint Time: {playerManager.sprintTime}");
                
                var transform = playerManager.transform;
                if (transform != null)
                {
                    var pos = transform.position;
                    Plugin.Log.LogInfo($"  Position: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
                }
                
                var character = playerManager.GetComponent<Character>();
                if (character != null)
                {
                    Plugin.Log.LogInfo("=== Character Component Found! ===");
                }
                else
                {
                    Plugin.Log.LogInfo("Character component not directly on PlayerManager");
                    var charInChildren = playerManager.GetComponentInChildren<Character>();
                    if (charInChildren != null)
                    {
                        Plugin.Log.LogInfo("Found Character in children!");
                    }
                }
                
                Plugin.Log.LogInfo("SUCCESS! Player info retrieved!");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to add an item to player inventory
        /// </summary>
        public static void TryAddItem()
        {
            try
            {
                var characters = Object.FindObjectsOfType<Character>();
                if (characters == null || characters.Length == 0)
                {
                    Plugin.Log.LogWarning("No Character found in scene");
                    return;
                }

                var character = characters[0];
                if (character == null)
                {
                    Plugin.Log.LogWarning("Character is null");
                    return;
                }

                var items = ItemDatabase.items;
                if (items == null || items.Count == 0)
                {
                    Plugin.Log.LogWarning("ItemDatabase not available");
                    return;
                }

                uint itemIdToAdd = 0;
                string itemName = "";
                
                uint[] testIds = { 1, 2, 3, 5, 10, 15, 20, 25, 50, 100 };
                
                foreach (var id in testIds)
                {
                    var templateItem = ItemDatabase.GetItem(id);
                    if (templateItem != null)
                    {
                        itemIdToAdd = id;
                        itemName = templateItem.title;
                        break;
                    }
                }

                if (itemIdToAdd == 0)
                {
                    foreach (var entry in items)
                    {
                        if (entry.Value != null)
                        {
                            itemIdToAdd = entry.Key;
                            itemName = entry.Value.title;
                            break;
                        }
                    }
                }

                if (itemIdToAdd == 0)
                {
                    Plugin.Log.LogWarning("Could not find any item to add");
                    return;
                }

                Plugin.Log.LogInfo($"=== Adding Item to Inventory ===");
                Plugin.Log.LogInfo($"  Item ID: {itemIdToAdd}");
                Plugin.Log.LogInfo($"  Item Name: '{itemName}'");

                var newItem = ItemDatabase.GetItem(itemIdToAdd, 1, false, true);
                
                if (newItem == null)
                {
                    Plugin.Log.LogError("Failed to create item instance!");
                    return;
                }

                Plugin.Log.LogInfo($"  Created new item instance: '{newItem.title}'");

                // Don't use wantHandlerForQuest=true - it passes ItemID: 0!
                // Instead, manually call OnMakeAction with correct parameters
                bool success = character.AddItemToInventory(newItem, true, false);
                
                if (success)
                {
                    Plugin.Log.LogInfo("=== SUCCESS! Item added to inventory! ===");
                    // Manually notify quest system with CORRECT item ID and count
                    HBT_QuestManager.OnMakeAction(FMQ_ActionWait.TypeOfActionWait.Item, itemIdToAdd, 1, "");
                    Plugin.Log.LogInfo($"  Called OnMakeAction(Item, {itemIdToAdd}, 1)");
                }
                else
                {
                    Plugin.Log.LogWarning("AddItemToInventory returned false - trying alternative...");
                    success = character.AddItemToInventory(newItem, false);
                    if (success)
                    {
                        Plugin.Log.LogInfo("=== SUCCESS with fallback method! ===");
                        HBT_QuestManager.OnMakeAction(FMQ_ActionWait.TypeOfActionWait.Item, itemIdToAdd, 1, "");
                        Plugin.Log.LogInfo($"  Called OnMakeAction(Item, {itemIdToAdd}, 1)");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error adding item: {ex.Message}");
            }
        }

        /// <summary>
        /// List skills and buffs from CharacterDatabase
        /// </summary>
        public static void ListSkillsAndBuffs()
        {
            try
            {
                var charDb = CharacterDatabase.Instance;
                if (charDb == null)
                {
                    Plugin.Log.LogWarning("CharacterDatabase.Instance is null");
                    return;
                }

                Plugin.Log.LogInfo("=== Character Database ===");
                
                var skills = charDb.skills;
                if (skills != null)
                {
                    Plugin.Log.LogInfo($"Skills count: {skills.Count}");
                    int count = 0;
                    foreach (var skill in skills)
                    {
                        if (count >= 5) break;
                        Plugin.Log.LogInfo($"  Skill: {skill?.ToString() ?? "null"}");
                        count++;
                    }
                }

                var buffs = charDb.buffs;
                if (buffs != null)
                {
                    Plugin.Log.LogInfo($"Buffs count: {buffs.Count}");
                    int count = 0;
                    foreach (var buff in buffs)
                    {
                        if (count >= 5) break;
                        Plugin.Log.LogInfo($"  Buff: {buff?.ToString() ?? "null"}");
                        count++;
                    }
                }
                
                var parameters = charDb.parameters;
                if (parameters != null)
                {
                    Plugin.Log.LogInfo($"Parameters count: {parameters.Count}");
                }

                Plugin.Log.LogInfo("SUCCESS! CharacterDatabase is accessible!");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Show current climate/weather/season information
        /// Use this to verify winter mod is working
        /// </summary>
        public static void ShowClimateInfo()
        {
            try
            {
                Plugin.Log.LogInfo("=== CLIMATE DEBUG INFO ===");
                
                // Get GameTime info
                var gameTime = GameTime.Instance;
                if (gameTime != null)
                {
                    string[] seasonNames = { "Autumn", "Winter", "Spring", "Summer" };
                    int season = gameTime.Season;
                    string seasonName = season < seasonNames.Length ? seasonNames[season] : $"#{season}";
                    
                    Plugin.Log.LogInfo($"  GameTime.Season: {season} ({seasonName})");
                    Plugin.Log.LogInfo($"  Day: {gameTime.DayOfTime24}");
                    Plugin.Log.LogInfo($"  Hour: {gameTime.RawHourOfDay24:F1}");
                }
                else
                {
                    Plugin.Log.LogWarning("  GameTime.Instance is null");
                }
                
                // Get AtmosManager info
                var atmosManagers = Object.FindObjectsOfType<AtmosManager>();
                if (atmosManagers != null && atmosManagers.Length > 0)
                {
                    var atmos = atmosManagers[0];
                    
                    Plugin.Log.LogInfo($"  --- AtmosManager ---");
                    Plugin.Log.LogInfo($"  Temperature: {atmos.temperature:F1}°C");
                    Plugin.Log.LogInfo($"  Weather: {atmos.currentWeather}");
                    
                    // Check if temperature is in winter range
                    float temp = atmos.temperature;
                    if (temp < 5f)
                    {
                        Plugin.Log.LogInfo($"  *** COLD/WINTER CLIMATE CONFIRMED! ***");
                    }
                    else if (temp < 15f)
                    {
                        Plugin.Log.LogInfo($"  Climate: Cool/Autumn");
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"  Climate: Warm/Summer");
                    }
                }
                else
                {
                    Plugin.Log.LogWarning("  AtmosManager not found");
                }
                
                // Show season override status
                Plugin.Log.LogInfo($"  --- Mod Status ---");
                Plugin.Log.LogInfo($"  (Season mod not available in release build)");
                
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error getting climate info: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to start a quest via HBT_QuestManager
        /// </summary>
        public static void TryStartTestQuest(string questId)
        {
            try
            {
                Plugin.Log.LogInfo($"=== STARTING QUEST: {questId} ===");
                
                var questDb = HBT_QuestDatabase.Instance;
                if (questDb == null)
                {
                    Plugin.Log.LogWarning("HBT_QuestDatabase.Instance is null!");
                    return;
                }
                
                // Try to get the quest via GetFMQ_Map (this should trigger our patch)
                Plugin.Log.LogInfo($"  Calling GetFMQ_Map('{questId}')...");
                bool isOK = false;
                var questMap = questDb.GetFMQ_Map(questId, out isOK, true, null);
                
                if (questMap != null && isOK)
                {
                    Plugin.Log.LogInfo($"  Quest retrieved successfully! questID: {questMap.questID}");
                    Plugin.Log.LogInfo($"  Calling StartQuestFromReward...");
                    HBT_QuestManager.StartQuestFromReward(questId);
                    Plugin.Log.LogInfo("  Done! Check your quest journal (J key).");
                }
                else
                {
                    Plugin.Log.LogWarning($"  GetFMQ_Map returned null or isOK=false");
                    Plugin.Log.LogInfo($"  isOK: {isOK}");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error starting quest: {ex.Message}");
                Plugin.Log.LogError($"Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Dump all Character stats (ParameterRange properties) with their internal titles
        /// This helps identify the correct property name for each stat
        /// </summary>
        public static void DumpCharacterStats()
        {
            try
            {
                Plugin.Log.LogInfo("=== DUMPING ALL CHARACTER STATS ===");
                
                var characters = Object.FindObjectsOfType<Character>();
                if (characters == null || characters.Length == 0)
                {
                    Plugin.Log.LogWarning("No Character found in scene");
                    return;
                }
                
                var c = characters[0];
                if (c == null)
                {
                    Plugin.Log.LogWarning("Character is null");
                    return;
                }
                
                // Manually check each known property
                LogStatProperty("c.Health", c.Health);
                LogStatProperty("c.Food", c.Food);
                LogStatProperty("c.Morale", c.Morale);
                LogStatProperty("c.Warm", c.Warm);
                LogStatProperty("c.Wet", c.Wet);
                LogStatProperty("c.Illness", c.Illness);
                LogStatProperty("c.Toxicity", c.Toxicity);
                LogStatProperty("c.Freshness", c.Freshness);
                LogStatProperty("c.Stamina", c.Stamina);
                LogStatProperty("c.Grit", c.Grit);
                
                // Try to find the Energy property via reflection
                Plugin.Log.LogInfo("  --- Searching via Reflection ---");
                var type = c.GetType();
                foreach (var prop in type.GetProperties())
                {
                    try
                    {
                        if (prop.PropertyType.Name.Contains("ParameterRange") || 
                            prop.PropertyType.Name.Contains("BaseParameter"))
                        {
                            var val = prop.GetValue(c);
                            if (val != null)
                            {
                                var paramRange = val as ParameterRange;
                                if (paramRange != null)
                                {
                                    string title = paramRange.title ?? "null";
                                    float value = paramRange.value;
                                    float max = paramRange.actualMax;
                                    Plugin.Log.LogInfo($"  FOUND: c.{prop.Name} -> title='{title}', value={value:F1}, max={max:F1}");
                                }
                            }
                        }
                    }
                    catch { /* Skip inaccessible properties */ }
                }
                
                Plugin.Log.LogInfo("=== END STAT DUMP ===");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error dumping stats: {ex.Message}");
            }
        }
        
        private static void LogStatProperty(string name, ParameterRange param)
        {
            if (param == null)
            {
                Plugin.Log.LogInfo($"  {name} -> NULL");
            }
            else
            {
                string title = param.title ?? "null";
                float value = param.value;
                float max = param.actualMax;
                Plugin.Log.LogInfo($"  {name} -> title='{title}', value={value:F1}, max={max:F1}");
            }
        }
    }
}
