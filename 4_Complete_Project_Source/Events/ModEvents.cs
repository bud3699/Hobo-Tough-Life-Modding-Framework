using System;
using BepInEx.Logging;

namespace HoboModPlugin.Framework.Events
{
    /// <summary>
    /// Central event hub for all mod events.
    /// Mods can subscribe to these events to react to game happenings.
    /// </summary>
    public static class ModEvents
    {
        private static ManualLogSource _log;
        
        /// <summary>
        /// Initialize the event system with logging
        /// </summary>
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _log?.LogInfo("[ModEvents] Event system initialized");
        }
        
        // ============================================================
        // INVENTORY EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when an item is about to be picked up.
        /// Set args.Cancelled = true to prevent pickup.
        /// </summary>
        public static event Action<ItemEventArgs> OnItemPickup;
        
        /// <summary>
        /// Fired when a consumable item is used.
        /// </summary>
        public static event Action<ItemEventArgs> OnItemUsed;
        
        /// <summary>
        /// Fired when an item is dropped from inventory.
        /// </summary>
        public static event Action<ItemEventArgs> OnItemDropped;
        
        // ============================================================
        // STAT EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when any player stat changes (health, food, energy, etc.)
        /// Set args.NewValue to modify the change, or args.Cancelled to block it.
        /// </summary>
        public static event Action<StatChangeEventArgs> OnStatChanged;
        
        // ============================================================
        // TIME EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when the game hour changes.
        /// </summary>
        public static event Action<TimeEventArgs> OnHourChanged;
        
        /// <summary>
        /// Fired when the game day changes.
        /// </summary>
        public static event Action<TimeEventArgs> OnDayChanged;
        
        /// <summary>
        /// Fired when the season changes.
        /// </summary>
        public static event Action<SeasonEventArgs> OnSeasonChanged;
        
        // ============================================================
        // PHASE 3A: EQUIPMENT EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when gear or weapon is equipped.
        /// </summary>
        public static event Action<EquipEventArgs> OnItemEquipped;
        
        /// <summary>
        /// Fired when gear or weapon is unequipped.
        /// </summary>
        public static event Action<EquipEventArgs> OnItemUnequipped;
        
        // ============================================================
        // PHASE 3A: PLAYER EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when the player dies.
        /// </summary>
        public static event Action<PlayerEventArgs> OnPlayerDeath;
        
        // ============================================================
        // PHASE 3A: QUEST EVENTS
        // ============================================================
        
        /// <summary>
        /// <summary>
        /// Fired when a quest node is completed.
        /// </summary>
        public static event Action<QuestEventArgs> OnQuestNodeCompleted;
        
        // ============================================================
        // PHASE 3B: WEATHER EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when the weather state changes.
        /// </summary>
        public static event Action<WeatherEventArgs> OnWeatherChanged;
        
        // ============================================================
        // CRAFTING EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when player successfully crafts an item.
        /// </summary>
        public static event Action<CraftEventArgs> OnItemCrafted;
        
        // ============================================================
        // GAME EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired after the game is saved.
        /// </summary>
        public static event Action<GameEventArgs> OnGameSaved;
        
        /// <summary>
        /// Fired after a save is loaded.
        /// </summary>
        public static event Action<GameEventArgs> OnGameLoaded;
        
        // ============================================================
        // CONVERSATION EVENTS
        // ============================================================
        
        /// <summary>
        /// Fired when a conversation with an NPC ends.
        /// Provides NPC info and the last selected option.
        /// Use this to trigger quests or other actions based on dialogue.
        /// </summary>
        public static event Action<ConversationEventArgs> OnConversationEnded;
        
        // ============================================================
        // INTERNAL FIRE METHODS (with exception handling)
        // ============================================================
        
        internal static void FireItemPickup(ItemEventArgs args)
        {
            if (OnItemPickup == null) return;
            
            foreach (var handler in OnItemPickup.GetInvocationList())
            {
                try
                {
                    ((Action<ItemEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnItemPickup handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireItemUsed(ItemEventArgs args)
        {
            if (OnItemUsed == null) return;
            
            foreach (var handler in OnItemUsed.GetInvocationList())
            {
                try
                {
                    ((Action<ItemEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnItemUsed handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireItemDropped(ItemEventArgs args)
        {
            if (OnItemDropped == null) return;
            
            foreach (var handler in OnItemDropped.GetInvocationList())
            {
                try
                {
                    ((Action<ItemEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnItemDropped handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireStatChanged(StatChangeEventArgs args)
        {
            if (OnStatChanged == null) return;
            
            foreach (var handler in OnStatChanged.GetInvocationList())
            {
                try
                {
                    ((Action<StatChangeEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnStatChanged handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireHourChanged(TimeEventArgs args)
        {
            if (OnHourChanged == null) return;
            
            foreach (var handler in OnHourChanged.GetInvocationList())
            {
                try
                {
                    ((Action<TimeEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnHourChanged handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireDayChanged(TimeEventArgs args)
        {
            if (OnDayChanged == null) return;
            
            foreach (var handler in OnDayChanged.GetInvocationList())
            {
                try
                {
                    ((Action<TimeEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnDayChanged handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireSeasonChanged(SeasonEventArgs args)
        {
            if (OnSeasonChanged == null) return;
            
            foreach (var handler in OnSeasonChanged.GetInvocationList())
            {
                try
                {
                    ((Action<SeasonEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnSeasonChanged handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireGameSaved(GameEventArgs args)
        {
            if (OnGameSaved == null) return;
            
            foreach (var handler in OnGameSaved.GetInvocationList())
            {
                try
                {
                    ((Action<GameEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnGameSaved handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireGameLoaded(GameEventArgs args)
        {
            if (OnGameLoaded == null) return;
            
            foreach (var handler in OnGameLoaded.GetInvocationList())
            {
                try
                {
                    ((Action<GameEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnGameLoaded handler error: {ex.Message}");
                }
            }
        }
        
        // ============================================================
        // PHASE 3A: NEW FIRE METHODS
        // ============================================================
        
        internal static void FireItemEquipped(EquipEventArgs args)
        {
            if (OnItemEquipped == null) return;
            
            foreach (var handler in OnItemEquipped.GetInvocationList())
            {
                try
                {
                    ((Action<EquipEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnItemEquipped handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireItemUnequipped(EquipEventArgs args)
        {
            if (OnItemUnequipped == null) return;
            
            foreach (var handler in OnItemUnequipped.GetInvocationList())
            {
                try
                {
                    ((Action<EquipEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnItemUnequipped handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FirePlayerDeath(PlayerEventArgs args)
        {
            if (OnPlayerDeath == null) return;
            
            foreach (var handler in OnPlayerDeath.GetInvocationList())
            {
                try
                {
                    ((Action<PlayerEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnPlayerDeath handler error: {ex.Message}");
                }
            }
        }
        
        internal static void FireQuestNodeCompleted(QuestEventArgs args)
        {
            if (OnQuestNodeCompleted == null) return;
            
            foreach (var handler in OnQuestNodeCompleted.GetInvocationList())
            {
                try
                {
                    ((Action<QuestEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnQuestNodeCompleted handler error: {ex.Message}");
                }
            }
        }
        
        // ============================================================
        // PHASE 3B: WEATHER FIRE METHOD
        // ============================================================
        
        internal static void FireWeatherChanged(WeatherEventArgs args)
        {
            if (OnWeatherChanged == null) return;
            
            foreach (var handler in OnWeatherChanged.GetInvocationList())
            {
                try
                {
                    ((Action<WeatherEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnWeatherChanged handler error: {ex.Message}");
                }
            }
        }
        
        // ============================================================
        // CRAFTING FIRE METHOD
        // ============================================================
        
        internal static void FireItemCrafted(CraftEventArgs args)
        {
            if (OnItemCrafted == null) return;
            
            foreach (var handler in OnItemCrafted.GetInvocationList())
            {
                try
                {
                    ((Action<CraftEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnItemCrafted handler error: {ex.Message}");
                }
            }
        }
        
        // ============================================================
        // CONVERSATION FIRE METHOD
        // ============================================================
        
        internal static void FireConversationEnded(ConversationEventArgs args)
        {
            if (OnConversationEnded == null) return;
            
            foreach (var handler in OnConversationEnded.GetInvocationList())
            {
                try
                {
                    ((Action<ConversationEventArgs>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModEvents] OnConversationEnded handler error: {ex.Message}");
                }
            }
        }
    }
}
