using System;
using Game;

namespace HoboModPlugin.Framework.Events
{
    /// <summary>
    /// Event arguments for item-related events (pickup, use, drop)
    /// </summary>
    public class ItemEventArgs
    {
        public uint ItemId { get; }
        public string ItemName { get; }
        public int Count { get; }
        public BaseItem Item { get; }
        
        /// <summary>
        /// Set to true to cancel the event (e.g., prevent item pickup)
        /// </summary>
        public bool Cancelled { get; set; }
        
        public ItemEventArgs(BaseItem item)
        {
            Item = item;
            ItemId = item?.id ?? 0;
            ItemName = item?.title ?? "";
            Count = item?.actualStockCount ?? 1;
            Cancelled = false;
        }
        
        public ItemEventArgs(uint id, string name, int count)
        {
            ItemId = id;
            ItemName = name;
            Count = count;
            Cancelled = false;
        }
    }
    
    /// <summary>
    /// Event arguments for stat changes (health, food, energy, etc.)
    /// </summary>
    public class StatChangeEventArgs
    {
        public string StatName { get; }
        public float OldValue { get; }
        public float NewValue { get; set; }
        public ParameterRange Parameter { get; }
        
        /// <summary>
        /// Set to true to cancel the stat change
        /// </summary>
        public bool Cancelled { get; set; }
        
        public StatChangeEventArgs(string statName, float oldValue, float newValue, ParameterRange parameter = null)
        {
            StatName = statName;
            OldValue = oldValue;
            NewValue = newValue;
            Parameter = parameter;
            Cancelled = false;
        }
    }
    
    /// <summary>
    /// Event arguments for time-related events (hour, day changes)
    /// </summary>
    public class TimeEventArgs
    {
        public int Hour { get; }
        public int Day { get; }
        public int Season { get; }
        public int PreviousHour { get; }
        public int PreviousDay { get; }
        
        public TimeEventArgs(int hour, int day, int season, int prevHour = -1, int prevDay = -1)
        {
            Hour = hour;
            Day = day;
            Season = season;
            PreviousHour = prevHour;
            PreviousDay = prevDay;
        }
    }
    
    /// <summary>
    /// Event arguments for season changes
    /// </summary>
    public class SeasonEventArgs
    {
        public int Season { get; }
        public int PreviousSeason { get; }
        public string SeasonName { get; }
        
        public SeasonEventArgs(int season, int previousSeason)
        {
            Season = season;
            PreviousSeason = previousSeason;
            SeasonName = season switch
            {
                0 => "Spring",
                1 => "Summer",
                2 => "Autumn",
                3 => "Winter",
                _ => "Unknown"
            };
        }
    }
    
    /// <summary>
    /// Event arguments for game save/load events
    /// </summary>
    public class GameEventArgs
    {
        public DateTime Timestamp { get; }
        
        public GameEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }
    
    // ============================================================
    // PHASE 3A: NEW EVENT ARGS
    // ============================================================
    
    /// <summary>
    /// Event arguments for equipment events (equip/unequip gear or weapon)
    /// </summary>
    public class EquipEventArgs
    {
        public uint ItemId { get; }
        public string ItemName { get; }
        public string ItemType { get; }  // "Gear" or "Weapon"
        public BaseItem Item { get; }
        
        public EquipEventArgs(BaseItem item, string itemType)
        {
            Item = item;
            ItemId = item?.id ?? 0;
            ItemName = item?.title ?? "";
            ItemType = itemType;
        }
    }
    
    /// <summary>
    /// Event arguments for player events (death, respawn, etc.)
    /// </summary>
    public class PlayerEventArgs
    {
        public string EventType { get; }  // "Death", "Respawn", etc.
        public DateTime Timestamp { get; }
        
        public PlayerEventArgs(string eventType)
        {
            EventType = eventType;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for quest events
    /// </summary>
    public class QuestEventArgs
    {
        public string QuestId { get; }
        public string NodeId { get; }
        public bool IsSuccess { get; }
        public uint NumDone { get; }
        
        public QuestEventArgs(string questId, string nodeId, bool isSuccess, uint numDone)
        {
            QuestId = questId;
            NodeId = nodeId;
            IsSuccess = isSuccess;
            NumDone = numDone;
        }
    }
    
    // ============================================================
    // PHASE 3B: WEATHER EVENT ARGS
    // ============================================================
    
    /// <summary>
    /// Event arguments for weather change events
    /// </summary>
    public class WeatherEventArgs
    {
        public string CurrentWeather { get; }
        public string TargetWeather { get; }
        public int CurrentWeatherType { get; }
        public int TargetWeatherType { get; }
        
        public WeatherEventArgs(int currentType, int targetType)
        {
            CurrentWeatherType = currentType;
            TargetWeatherType = targetType;
            CurrentWeather = GetWeatherName(currentType);
            TargetWeather = GetWeatherName(targetType);
        }
        
        private static string GetWeatherName(int weatherType)
        {
            return weatherType switch
            {
                0 => "Foggy",
                1 => "MostlyClear",
                2 => "PartlyCloudy",
                3 => "MostlyCloudy",
                4 => "LightStorm",
                5 => "HeavyStorm",
                6 => "ExtremeStorm",
                _ => "Unknown"
            };
        }
    }
    
    // ============================================================
    // CRAFTING EVENT ARGS
    // ============================================================
    
    /// <summary>
    /// Event arguments for crafting events
    /// </summary>
    public class CraftEventArgs
    {
        public uint RecipeId { get; }
        public string RecipeName { get; }
        public uint ResultItemId { get; }
        public string ResultItemName { get; }
        public bool Success { get; }
        public DateTime Timestamp { get; }
        
        public CraftEventArgs(uint recipeId, string recipeName, uint resultItemId, string resultItemName, bool success)
        {
            RecipeId = recipeId;
            RecipeName = recipeName ?? "";
            ResultItemId = resultItemId;
            ResultItemName = resultItemName ?? "";
            Success = success;
            Timestamp = DateTime.Now;
        }
    }
    
    // ============================================================
    // CONVERSATION EVENT ARGS
    // ============================================================
    
    /// <summary>
    /// Event arguments for conversation end events.
    /// Use this to trigger quests or other actions based on NPC dialogue.
    /// </summary>
    public class ConversationEventArgs
    {
        /// <summary>
        /// The NPC archetype (e.g., "beggar_01", "cop_guard", etc.)
        /// </summary>
        public string NpcArchetype { get; }
        
        /// <summary>
        /// The ID of the last selected dialogue option
        /// </summary>
        public string LastOptionId { get; }
        
        /// <summary>
        /// The text of the last selected dialogue option
        /// </summary>
        public string LastOptionText { get; }
        
        /// <summary>
        /// The raw FMC_Option object for advanced usage
        /// </summary>
        public Game.FMC_Option LastOption { get; }
        
        /// <summary>
        /// Timestamp of when the conversation ended
        /// </summary>
        public DateTime Timestamp { get; }
        
        public ConversationEventArgs(string npcArchetype, Game.FMC_Option lastOption)
        {
            NpcArchetype = npcArchetype ?? "";
            LastOption = lastOption;
            LastOptionId = "";
            LastOptionText = "";
            Timestamp = DateTime.Now;
            
            // Extract option info safely
            if (lastOption != null)
            {
                try { LastOptionId = lastOption.index.ToString(); } catch { }
                // Use ActualizeText() for the actual displayed text, fallback to GetString()
                try { LastOptionText = lastOption.ActualizeText() ?? ""; } 
                catch 
                { 
                    try { LastOptionText = lastOption.GetString() ?? ""; } catch { }
                }
            }
        }
    }
}
