using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using Game;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Handles applying effects from item definitions to the player
    /// </summary>
    public class EffectHandler
    {
        private readonly ManualLogSource _log;
        private readonly ItemRegistry _itemRegistry;
        
        // Effect applicators
        private readonly Dictionary<string, Action<Character, string>> _effectApplicators = new();
        
        public EffectHandler(ManualLogSource log, ItemRegistry itemRegistry)
        {
            _log = log;
            _itemRegistry = itemRegistry;
            
            RegisterBuiltInEffects();
        }
        
        private void RegisterBuiltInEffects()
        {
            // Positive stats (higher = better)
            _effectApplicators["health"] = ApplyRangedStat((c) => c.Health, "health->c.Health");
            _effectApplicators["food"] = ApplyRangedStat((c) => c.Food, "food->c.Food");
            _effectApplicators["morale"] = ApplyRangedStat((c) => c.Morale, "morale->c.Morale");
            
            // FIXED: Energy = Freshness (NOT Grit!)
            // c.Freshness has title='Energy' (sleep/tiredness stat)
            _effectApplicators["energy"] = ApplyRangedStat((c) => c.Freshness, "energy->c.Freshness");
            _effectApplicators["tiredness"] = ApplyRangedStat((c) => c.Freshness, "tiredness->c.Freshness");
            _effectApplicators["sleep"] = ApplyRangedStat((c) => c.Freshness, "sleep->c.Freshness");
            
            // Willpower = Grit (combat/mental fortitude, small 0-2 range)
            _effectApplicators["grit"] = ApplyRangedStat((c) => c.Grit, "grit->c.Grit");
            _effectApplicators["willpower"] = ApplyRangedStat((c) => c.Grit, "willpower->c.Grit");
            
            // Stamina = physical endurance (for running/sprinting)
            _effectApplicators["stamina"] = ApplyRangedStat((c) => c.Stamina, "stamina->c.Stamina");
            _effectApplicators["fatigue"] = ApplyRangedStat((c) => c.Stamina, "fatigue->c.Stamina");
            
            _effectApplicators["warmth"] = ApplyRangedStat((c) => c.Warm, "warmth->c.Warm");
            _effectApplicators["warm"] = ApplyRangedStat((c) => c.Warm, "warm->c.Warm");
            
            // Negative/Inverse stats (lower = better, 0=good, 100=bad)
            _effectApplicators["illness"] = ApplyRangedStat((c) => c.Illness, "illness->c.Illness");
            _effectApplicators["toxicity"] = ApplyRangedStat((c) => c.Toxicity, "toxicity->c.Toxicity");
            _effectApplicators["wet"] = ApplyRangedStat((c) => c.Wet, "wet->c.Wet");
            _effectApplicators["wetness"] = ApplyRangedStat((c) => c.Wet, "wetness->c.Wet");
            _effectApplicators["dryness"] = ApplyRangedStat((c) => c.Wet, "dryness->c.Wet");
            
            // Odour/Smell
            _effectApplicators["smell"] = ApplyRangedStat((c) => c.Smell, "smell->c.Smell");
            _effectApplicators["odour"] = ApplyRangedStat((c) => c.Smell, "odour->c.Smell");
            _effectApplicators["odor"] = ApplyRangedStat((c) => c.Smell, "odor->c.Smell");
            
            // Other discovered stats
            _effectApplicators["alcohol"] = ApplyRangedStat((c) => c.Alcohol, "alcohol->c.Alcohol");
            _effectApplicators["greatneed"] = ApplyRangedStat((c) => c.Greatneed, "greatneed->c.Greatneed");
            _effectApplicators["bathroom"] = ApplyRangedStat((c) => c.Greatneed, "bathroom->c.Greatneed");
        }
        
        private Action<Character, string> ApplyInverseRangedStat(Func<Character, ParameterRange> getter)
        {
            return (character, value) =>
            {
                var param = getter(character);
                if (param == null) return;
                
                // Invert: max -> 0, min -> max
                if (value.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    param.value = 0;
                }
                else if (value.Equals("min", StringComparison.OrdinalIgnoreCase))
                {
                    param.value = param.actualMax;
                }
                else
                {
                    param.value = ParseValue(value, param.value, param.actualMax);
                }
            };
        }
        
        private Action<Character, string> ApplyRangedStat(Func<Character, ParameterRange> getter, string statName = "Unknown")
        {
            return (character, value) =>
            {
                var param = getter(character);
                if (param == null)
                {
                    _log?.LogWarning($"[EffectHandler] STAT NULL: {statName} - getter returned null, effect cannot be applied");
                    return;
                }
                
                // Log diagnostic info for debugging
                string paramTitle = "?";
                try { paramTitle = param.title ?? "null"; } catch { }
                float oldValue = param.value;
                float targetValue = ParseValue(value, param.value, param.actualMax);
                
                // FIX: Clamp value to prevent overshoot (0 to max)
                float clampedValue = Math.Max(0, Math.Min(targetValue, param.actualMax));
                
                _log?.LogInfo($"[EffectHandler] Applying {statName}: title='{paramTitle}', old={oldValue:F1}, target={targetValue:F1}, clamped={clampedValue:F1}, max={param.actualMax:F1}");
                
                param.value = clampedValue;
                
                _log?.LogInfo($"[EffectHandler] After set: {statName} value is now {param.value:F1}");
            };
        }
        
        /// <summary>
        /// Parse effect value string
        /// </summary>
        private float ParseValue(string value, float current, float max)
        {
            // Special values
            if (value.Equals("max", StringComparison.OrdinalIgnoreCase))
                return max;
            
            if (value.Equals("min", StringComparison.OrdinalIgnoreCase))
                return 0;
            
            // Relative values
            if (value.StartsWith("add:"))
            {
                if (float.TryParse(value.Substring(4), out var addVal))
                    return current + addVal;
            }
            
            if (value.StartsWith("+"))
            {
                if (float.TryParse(value.Substring(1), out var addVal))
                    return current + addVal;
            }
            
            if (value.StartsWith("-"))
            {
                if (float.TryParse(value, out var subVal))
                    return current + subVal; // subVal is already negative
            }
            
            if (value.StartsWith("multiply:"))
            {
                if (float.TryParse(value.Substring(9), out var mulVal))
                    return current * mulVal;
            }
            
            // Absolute value
            if (float.TryParse(value, out var absVal))
                return absVal;
            
            return current; // No change if can't parse
        }
        
        /// <summary>
        /// Apply all effects from an item to the character
        /// </summary>
        public bool ApplyItemEffects(uint itemId, Character character)
        {
            var registered = _itemRegistry.GetItemByNumericId(itemId);
            if (registered == null) return false;
            
            _log.LogInfo($"=== Applying effects for {registered.Definition.Id} ===");
            
            foreach (var effect in registered.Definition.Effects)
            {
                ApplyEffect(character, effect);
            }
            
            return true;
        }
        
        private void ApplyEffect(Character character, EffectDefinition effect)
        {
            var statKey = effect.Stat.ToLowerInvariant();
            
            if (_effectApplicators.TryGetValue(statKey, out var applicator))
            {
                try
                {
                    applicator(character, effect.Value);
                    _log.LogInfo($"  {effect.Stat} -> {effect.Value}");
                }
                catch (Exception ex)
                {
                    _log.LogError($"  Failed to apply {effect.Stat}: {ex.Message}");
                }
            }
            else
            {
                _log.LogWarning($"  Unknown stat: {effect.Stat}");
            }
        }
        
        /// <summary>
        /// Register a custom effect applicator
        /// </summary>
        public void RegisterEffect(string statName, Action<Character, string> applicator)
        {
            _effectApplicators[statName.ToLowerInvariant()] = applicator;
        }
    }
}
