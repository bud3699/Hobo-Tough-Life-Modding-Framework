using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Game;
using Core.Strings;

namespace HoboModPlugin.Features
{
    /// <summary>
    /// Universal Data Extraction API for dumping game data.
    /// This class ONLY extracts and structures data — it does NOT write files or listen for input.
    /// Designed to be called by external mods (e.g., WikiDataDumper).
    /// </summary>
    public static class DataDumperAPI
    {
        /// <summary>
        /// Extract ALL equipable items from the game's ItemDatabase.
        /// Includes: Gear (hats, jackets, trousers, shoes), Weapons, and Bags.
        /// Uses reflection to dynamically capture every field/property on each item,
        /// so nothing is missed even if the game adds hidden stats.
        /// </summary>
        /// <param name="log">Optional logger for diagnostics</param>
        /// <returns>A list of dictionaries, one per equipable item, with all discovered fields</returns>
        public static List<Dictionary<string, object>> GetAllEquipableItems(ManualLogSource log = null)
        {
            var results = new List<Dictionary<string, object>>();

            try
            {
                var items = ItemDatabase.items;
                if (items == null || items.Count == 0)
                {
                    log?.LogWarning("[DataDumperAPI] ItemDatabase.items is null or empty — game not loaded?");
                    return results;
                }

                // Grab localization dictionary once
                var stringsItems = StringsManager.strings_items;
                var locDict = stringsItems?.translatedInt;

                int gearCount = 0, weaponCount = 0, bagCount = 0;

                foreach (var entry in items)
                {
                    var item = entry.Value;
                    if (item == null) continue;

                    // Determine item subtype
                    var gear = item.TryCast<Gear>();
                    var weapon = item.TryCast<Weapon>();
                    var bag = item.TryCast<Bag>();

                    // We only care about equipable items
                    bool isEquipable = (gear != null || weapon != null || bag != null);
                    if (!isEquipable) continue;

                    var dict = new Dictionary<string, object>();

                    // ── Manual high-value keys (need special handling) ──
                    dict["_ItemID"] = entry.Key;
                    dict["_ItemType"] = gear != null ? "Gear"
                                     : weapon != null ? "Weapon"
                                     : bag != null ? "Bag"
                                     : "Unknown";

                    // Localized name
                    string localizedName = "";
                    string localizedDesc = "";
                    try
                    {
                        if (locDict != null)
                        {
                            if (locDict.ContainsKey(item.titleKey))
                                localizedName = locDict[item.titleKey];
                            if (locDict.ContainsKey(item.descriptionKey))
                                localizedDesc = locDict[item.descriptionKey];
                        }
                    }
                    catch { /* localization may not be ready */ }

                    dict["_LocalizedName"] = string.IsNullOrEmpty(localizedName) ? item.title : localizedName;
                    dict["_LocalizedDescription"] = localizedDesc;

                    // ── Reflection sweep: grab EVERY property on the concrete type ──
                    object targetObj = gear ?? (object)weapon ?? (object)bag ?? item;
                    Type targetType = targetObj.GetType();

                    dict["_ReflectedType"] = targetType.Name;

                    // Sweep all public instance properties
                    try
                    {
                        var props = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var prop in props)
                        {
                            if (dict.ContainsKey(prop.Name)) continue; // don't overwrite manual keys

                            try
                            {
                                // Skip indexer properties (they have parameters)
                                if (prop.GetIndexParameters().Length > 0) continue;

                                var value = prop.GetValue(targetObj);
                                dict[prop.Name] = SafeSerialize(value);
                            }
                            catch
                            {
                                dict[prop.Name] = "<error reading>";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        dict["_PropertySweepError"] = ex.Message;
                    }

                    // Sweep all public instance fields
                    try
                    {
                        var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var field in fields)
                        {
                            if (dict.ContainsKey(field.Name)) continue;

                            try
                            {
                                var value = field.GetValue(targetObj);
                                dict[field.Name] = SafeSerialize(value);
                            }
                            catch
                            {
                                dict[field.Name] = "<error reading>";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        dict["_FieldSweepError"] = ex.Message;
                    }

                    // ── Special: extract parameterChanges list for Gear ──
                    if (gear != null)
                    {
                        try
                        {
                            var paramChanges = gear.parameterChanges;
                            if (paramChanges != null && paramChanges.Count > 0)
                            {
                                var statList = new List<Dictionary<string, object>>();
                                for (int i = 0; i < paramChanges.Count; i++)
                                {
                                    var change = paramChanges[i];
                                    statList.Add(new Dictionary<string, object>
                                    {
                                        ["parameterType"] = change.influencedParameterType.ToString(),
                                        ["value"] = change.value,
                                        ["finalValue"] = change.finalValue
                                    });
                                }
                                dict["_GearStatBonuses"] = statList;
                            }
                        }
                        catch { dict["_GearStatBonuses"] = "<error reading>"; }
                    }

                    results.Add(dict);

                    if (gear != null) gearCount++;
                    else if (weapon != null) weaponCount++;
                    else if (bag != null) bagCount++;
                }

                log?.LogInfo($"[DataDumperAPI] Extracted {results.Count} equipable items (Gear:{gearCount}, Weapon:{weaponCount}, Bag:{bagCount})");
            }
            catch (Exception ex)
            {
                log?.LogError($"[DataDumperAPI] Fatal error: {ex.Message}");
                log?.LogError(ex.StackTrace);
            }

            return results;
        }

        /// <summary>
        /// Safely convert a value to a JSON-friendly representation.
        /// Handles IL2CPP objects, Unity objects, enums, primitives, and lists.
        /// </summary>
        private static object SafeSerialize(object value)
        {
            if (value == null) return null;

            var type = value.GetType();

            // Primitives and strings are fine as-is
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return value;

            // Enums → string name
            if (type.IsEnum)
                return value.ToString();

            // Unity Vector types → readable string
            if (type == typeof(UnityEngine.Vector2) || type == typeof(UnityEngine.Vector3) ||
                type == typeof(UnityEngine.Vector4) || type == typeof(UnityEngine.Quaternion) ||
                type == typeof(UnityEngine.Color) || type == typeof(UnityEngine.Rect) ||
                type == typeof(UnityEngine.Bounds))
                return value.ToString();

            // Unity Object types → just the name (to avoid serializing huge graphs)
            if (value is UnityEngine.Object unityObj)
            {
                try { return unityObj != null ? unityObj.name : null; }
                catch { return "<unity obj>"; }
            }

            // IL2CPP list types → try to get count
            try
            {
                var countProp = type.GetProperty("Count");
                if (countProp != null)
                {
                    var count = countProp.GetValue(value);
                    return $"<list count={count}>";
                }
            }
            catch { }

            // Fallback: ToString
            try { return value.ToString(); }
            catch { return "<unreadable>"; }
        }
    }
}
