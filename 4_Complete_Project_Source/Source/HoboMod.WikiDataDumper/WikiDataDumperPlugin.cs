using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using Newtonsoft.Json;
using HoboModPlugin.Features;

namespace HoboMod.WikiDataDumper
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency("com.hobomod.framework", BepInDependency.DependencyFlags.HardDependency)] // Dependency on Framework
    public class WikiDataDumperPlugin : BasePlugin
    {
        public const string PLUGIN_GUID = "com.hobomod.wikidatadumper";
        public const string PLUGIN_NAME = "HoboMod.WikiDataDumper";
        public const string PLUGIN_VERSION = "1.0.0";

        internal static new ManualLogSource Log;

        public override void Load()
        {
            Log = base.Log;
            AddComponent<WikiDataDumperUpdater>();
            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded and listening for '#' trigger.");
        }
    }

    public class WikiDataDumperUpdater : MonoBehaviour
    {
        public WikiDataDumperUpdater(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            // Listen for the '#' key anywhere in the input string
            if (Input.inputString.Contains("#"))
            {
                TriggerDataDump();
            }
        }

        private void TriggerDataDump()
        {
            WikiDataDumperPlugin.Log.LogInfo("=== Triggering Universal Data Dump ===");
            
            try
            {
                WikiDataDumperPlugin.Log.LogInfo("Extracting equipable items via Framework API...");
                var equipableItems = DataDumperAPI.GetAllEquipableItems(WikiDataDumperPlugin.Log);
                
                if (equipableItems != null && equipableItems.Count > 0)
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    
                    // Default BepInEx plugins path wrapper
                    string pluginPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    
                    // 1. JSON Export
                    string jsonPath = Path.Combine(pluginPath, $"DataDump_EquipableItems_{timestamp}.json");
                    WikiDataDumperPlugin.Log.LogInfo($"Serializing {equipableItems.Count} items to JSON format...");
                    string json = JsonConvert.SerializeObject(equipableItems, Formatting.Indented);
                    File.WriteAllText(jsonPath, json);
                    
                    // 2. TSV Export
                    string tsvPath = Path.Combine(pluginPath, $"DataDump_EquipableItems_{timestamp}.tsv");
                    WikiDataDumperPlugin.Log.LogInfo("Serializing items to TSV format...");
                    
                    // Gather all unique column headers across all items (since different item types have different properties)
                    var allKeys = new System.Collections.Generic.HashSet<string>();
                    foreach (var item in equipableItems)
                    {
                        foreach (var key in item.Keys)
                        {
                            allKeys.Add(key);
                        }
                    }
                    var columns = new System.Collections.Generic.List<string>(allKeys);
                    
                    // Sort columns: Prioritize high-value keys first, then alphabetical
                    var sortedColumns = new System.Collections.Generic.List<string>();
                    string[] priorityKeys = { "_ItemID", "_ItemType", "_LocalizedName", "_LocalizedDescription", "price", "weight" };
                    foreach (var pk in priorityKeys)
                    {
                        if (columns.Contains(pk))
                        {
                            sortedColumns.Add(pk);
                            columns.Remove(pk);
                        }
                    }
                    columns.Sort();
                    sortedColumns.AddRange(columns);

                    using (var writer = new StreamWriter(tsvPath))
                    {
                        // Write Header
                        writer.WriteLine(string.Join("\t", sortedColumns));
                        
                        // Write Rows
                        foreach (var item in equipableItems)
                        {
                            var rowValues = new System.Collections.Generic.List<string>();
                            foreach (var col in sortedColumns)
                            {
                                if (item.TryGetValue(col, out object val) && val != null)
                                {
                                    // Handle complex objects (like _GearStatBonuses list) by converting to compact JSON string
                                    string strVal;
                                    if (val is string s) strVal = s;
                                    else if (val.GetType().IsPrimitive || val is decimal) strVal = val.ToString();
                                    else strVal = JsonConvert.SerializeObject(val, Formatting.None);
                                    
                                    // Escape tabs and newlines so it doesn't break TSV formatting
                                    strVal = strVal.Replace("\t", "    ").Replace("\n", "\\n").Replace("\r", "");
                                    rowValues.Add(strVal);
                                }
                                else
                                {
                                    rowValues.Add(""); // Empty column for properties this item doesn't have
                                }
                            }
                            writer.WriteLine(string.Join("\t", rowValues));
                        }
                    }

                    WikiDataDumperPlugin.Log.LogInfo($"SUCCESS! Dumped to:\n{jsonPath}\n{tsvPath}");
                }
                else
                {
                    WikiDataDumperPlugin.Log.LogWarning("No equipable items found. Is the game ItemDatabase fully loaded?");
                }
            }
            catch (Exception ex)
            {
                WikiDataDumperPlugin.Log.LogError($"Error during data dump: {ex.Message}");
                WikiDataDumperPlugin.Log.LogError(ex.StackTrace);
            }
        }
    }
}
