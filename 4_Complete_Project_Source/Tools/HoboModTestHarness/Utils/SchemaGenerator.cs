using System;
using System.IO;
using NJsonSchema;
using NJsonSchema.Generation;

namespace HoboModTestHarness.Utils;

/// <summary>
/// Generates JSON schemas from C# definition classes.
/// These schemas are used to validate mod JSON files.
/// </summary>
public static class SchemaGenerator
{
    /// <summary>
    /// Generates all schemas and saves them to the specified directory
    /// </summary>
    public static void GenerateAll(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        
        // Generate mod.schema.json (ModManifest)
        GenerateModSchema(Path.Combine(outputDirectory, "mod.schema.json"));
        
        // Generate item.schema.json (ItemDefinition)
        GenerateItemSchema(Path.Combine(outputDirectory, "item.schema.json"));
        
        // Generate recipe.schema.json (RecipeDefinition)
        GenerateRecipeSchema(Path.Combine(outputDirectory, "recipe.schema.json"));
        
        // Generate quest.schema.json (QuestDefinition)
        GenerateQuestSchema(Path.Combine(outputDirectory, "quest.schema.json"));
    }
    
    private static void GenerateModSchema(string outputPath)
    {
        var schema = CreateSchemaWithSettings<ModManifestSchema>();
        schema.Title = "HoboMod Manifest Schema";
        schema.Description = "Schema for mod.json - the main mod manifest file";
        File.WriteAllText(outputPath, schema.ToJson());
        Console.WriteLine($"Generated: {outputPath}");
    }
    
    private static void GenerateItemSchema(string outputPath)
    {
        var schema = CreateSchemaWithSettings<ItemDefinitionSchema>();
        schema.Title = "HoboMod Item Schema";
        schema.Description = "Schema for item definition files (items/*.json)";
        File.WriteAllText(outputPath, schema.ToJson());
        Console.WriteLine($"Generated: {outputPath}");
    }
    
    private static void GenerateRecipeSchema(string outputPath)
    {
        var schema = CreateSchemaWithSettings<RecipeDefinitionSchema>();
        schema.Title = "HoboMod Recipe Schema";
        schema.Description = "Schema for recipe definition files (recipes/*.json)";
        File.WriteAllText(outputPath, schema.ToJson());
        Console.WriteLine($"Generated: {outputPath}");
    }
    
    private static void GenerateQuestSchema(string outputPath)
    {
        var schema = CreateSchemaWithSettings<QuestDefinitionSchema>();
        schema.Title = "HoboMod Quest Schema";
        schema.Description = "Schema for quest definition files (quests/*.json)";
        File.WriteAllText(outputPath, schema.ToJson());
        Console.WriteLine($"Generated: {outputPath}");
    }
    
    private static JsonSchema CreateSchemaWithSettings<T>()
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull,
            SerializerOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            }
        };
        // Allow additional properties since modders may have custom extensions
        var schema = JsonSchema.FromType<T>(settings);
        schema.AllowAdditionalProperties = true;
        
        // Also allow additional properties on all nested definitions
        foreach (var definition in schema.Definitions.Values)
        {
            definition.AllowAdditionalProperties = true;
        }
        
        return schema;
    }
}

// ========================================
// Schema Definition Classes
// These mirror the framework classes but are standalone for schema generation
// ========================================

#region ModManifest Schema

public class ModManifestSchema
{
    /// <summary>Unique mod identifier (e.g., "mymod")</summary>
    public string Id { get; set; } = "";
    
    /// <summary>Display name of the mod</summary>
    public string Name { get; set; } = "";
    
    /// <summary>Mod version (semver recommended)</summary>
    public string Version { get; set; } = "1.0.0";
    
    /// <summary>Mod author name</summary>
    public string Author { get; set; } = "Unknown";
    
    /// <summary>Mod description</summary>
    public string Description { get; set; } = "";
    
    /// <summary>Target game version</summary>
    public string? GameVersion { get; set; }
    
    /// <summary>List of required mod IDs</summary>
    public List<string>? Dependencies { get; set; }
    
    /// <summary>Development hotkeys for testing</summary>
    public List<DevHotkeySchema>? DevHotkeys { get; set; }
    
    /// <summary>Dialogue triggers for starting quests from NPC conversations</summary>
    public List<DialogueTriggerSchema>? DialogueTriggers { get; set; }
}

public class DevHotkeySchema
{
    /// <summary>Key to press (e.g., "F6", "Insert")</summary>
    public string Key { get; set; } = "";
    
    /// <summary>Action to perform (spawn_item, spawn_vanilla, explore_items, etc.)</summary>
    public string Action { get; set; } = "";
    
    /// <summary>Item ID for spawn actions</summary>
    public string? ItemId { get; set; }
}

public class DialogueTriggerSchema
{
    /// <summary>NPC identifier to match (partial, case-insensitive)</summary>
    public string NpcId { get; set; } = "";
    
    /// <summary>When to trigger: "option_selected" or "conversation_ended"</summary>
    public string TriggerOn { get; set; } = "conversation_ended";
    
    /// <summary>Dialogue option text to match (for option_selected trigger)</summary>
    public string? OptionText { get; set; }
    
    /// <summary>Dialogue option textKey to match (exact)</summary>
    public uint? OptionTextKey { get; set; }
    
    /// <summary>Action to perform (e.g., "start_quest")</summary>
    public string Action { get; set; } = "";
    
    /// <summary>Quest ID for start_quest action</summary>
    public string? QuestId { get; set; }
}

#endregion

#region ItemDefinition Schema

public class ItemDefinitionSchema
{
    /// <summary>Unique item ID (e.g., "mymod:apple")</summary>
    public string Id { get; set; } = "";
    
    /// <summary>Base game item ID to clone properties from</summary>
    public uint BaseItem { get; set; } = 1;
    
    /// <summary>Item type: consumable, gear, weapon, bag</summary>
    public string Type { get; set; } = "consumable";
    
    /// <summary>Display name</summary>
    public string Name { get; set; } = "";
    
    /// <summary>Item description</summary>
    public string? Description { get; set; }
    
    /// <summary>Path to icon image (relative to mod folder)</summary>
    public string? Icon { get; set; }
    
    /// <summary>Buy/sell price</summary>
    public int Price { get; set; } = 100;
    
    /// <summary>Item weight</summary>
    public float Weight { get; set; } = 0.1f;
    
    /// <summary>Legacy effect definitions</summary>
    public List<EffectSchema>? Effects { get; set; }
    
    /// <summary>Native stat changes applied when consumed</summary>
    public List<StatChangeSchema>? StatChanges { get; set; }
    
    /// <summary>Buff effects applied when consumed</summary>
    public List<BuffEffectSchema>? BuffEffects { get; set; }
    
    /// <summary>Addiction type: NOTHING, Alcohol, Drugs, Cigarettes</summary>
    public string? AddictionType { get; set; }
    
    /// <summary>Rarity color (0=common)</summary>
    public int RareColor { get; set; } = 0;
    
    /// <summary>Can be sold to vendors</summary>
    public bool Sellable { get; set; } = true;
    
    /// <summary>Cannot be used as fire fuel</summary>
    public bool NotForFire { get; set; } = false;
    
    /// <summary>Can stack in inventory</summary>
    public bool IsStockable { get; set; } = true;
    
    /// <summary>Default stack size</summary>
    public int ActualStockCount { get; set; } = 1;
    
    /// <summary>Sound type when used</summary>
    public int SoundType { get; set; } = 0;
    
    /// <summary>Burn duration as fuel</summary>
    public int Firerate { get; set; } = 0;
    
    /// <summary>Can go in hotbar</summary>
    public bool IsForHotSlot { get; set; } = false;
    
    /// <summary>Category hint (NA, Material, CanBeSold, Equipment, Healing, etc.)</summary>
    public string? Hint { get; set; }
    
    /// <summary>Can be stolen by NPCs</summary>
    public bool IsStealable { get; set; } = false;
    
    /// <summary>Heavy item flag</summary>
    public bool HeavyItem { get; set; } = false;
    
    /// <summary>Hard item flag</summary>
    public bool HardItem { get; set; } = false;
    
    /// <summary>Day when item starts appearing</summary>
    public int SpawnDay { get; set; } = 0;
    
    /// <summary>Item is never removed</summary>
    public bool IsPermanent { get; set; } = false;
    
    /// <summary>Quest type: Nothing, Private, Shared</summary>
    public string? QuestType { get; set; }
    
    /// <summary>Salvage loot table ID</summary>
    public int SalvageTableId { get; set; } = -1;
    
    /// <summary>Shop buyback timer (seconds)</summary>
    public float BuyBackTime { get; set; } = 0f;
    
    /// <summary>Sub-category: Default, Nothing, CraftingMaterial, Food, Usable, Companion</summary>
    public string? SubCategory { get; set; }
    
    /// <summary>Triggers buffet minigame when consumed</summary>
    public bool BuffetGame { get; set; } = false;
    
    /// <summary>Buffet minigame difficulty level</summary>
    public int BuffetDifficulty { get; set; } = 0;
    
    /// <summary>Item ordering in menus (-1 = auto)</summary>
    public int Index { get; set; } = -1;
    
    /// <summary>Linked recipe IDs for this item</summary>
    public List<uint>? ImproRecipes { get; set; }
    
    /// <summary>Copy icon from existing game item</summary>
    public uint ReferenceItemId { get; set; } = 0;
    
    /// <summary>Patch existing item instead of creating new</summary>
    public bool IsModify { get; set; } = false;
    
    /// <summary>ID of vanilla item to modify</summary>
    public uint TargetItemId { get; set; } = 0;
    
    // Gear-specific
    /// <summary>Gear category: hat, jacket, trousers, shoes</summary>
    public string? Category { get; set; }
    
    /// <summary>Warmth resistance</summary>
    public int WarmResistance { get; set; } = 0;
    
    /// <summary>Wet resistance</summary>
    public int WetResistance { get; set; } = 0;
    
    /// <summary>Durability resistance</summary>
    public int DurabilityResistance { get; set; } = 100;
    
    /// <summary>Recipe ID for repairing this gear</summary>
    public uint RepairRecipe { get; set; } = 0;
    
    /// <summary>Cash cost to repair at vendors</summary>
    public int RepairCash { get; set; } = 0;
    
    // Weapon-specific
    /// <summary>Attack power</summary>
    public int Attack { get; set; } = 0;
    
    /// <summary>Defense power</summary>
    public int Defense { get; set; } = 0;
    
    /// <summary>Critical hit chance</summary>
    public int CriticalChance { get; set; } = 0;
    
    /// <summary>Maximum durability</summary>
    public int MaxDurability { get; set; } = 100;
    
    /// <summary>Stat bonuses when equipped (gear)</summary>
    public List<StatSchema>? Stats { get; set; }
    
    // Bag-specific
    /// <summary>Bag capacity</summary>
    public float BagCapacity { get; set; } = 0f;
}

public class EffectSchema
{
    /// <summary>Stat name (or "type" as alias)</summary>
    public string Stat { get; set; } = "";
    
    /// <summary>Effect value</summary>
    public string Value { get; set; } = "0";
}

public class StatChangeSchema
{
    /// <summary>Stat name (Food, Drink, Health, Energy, etc.)</summary>
    public string Stat { get; set; } = "";
    
    /// <summary>Normal stat change value</summary>
    public int Value { get; set; } = 0;
    
    /// <summary>Value when addicted</summary>
    public int AddictedValue { get; set; } = 0;
}

public class BuffEffectSchema
{
    /// <summary>Buff type name</summary>
    public string Buff { get; set; } = "";
    
    /// <summary>Buff tier (0-3)</summary>
    public int Tier { get; set; } = 1;
    
    /// <summary>Tier when addicted</summary>
    public int AddictedTier { get; set; } = 0;
    
    /// <summary>Addiction type: NOTHING, Alcohol, Drugs, Cigarettes</summary>
    public string AddictionType { get; set; } = "NOTHING";
}

public class StatSchema
{
    /// <summary>Stat type</summary>
    public string Type { get; set; } = "";
    
    /// <summary>Stat value</summary>
    public int Value { get; set; } = 0;
}

#endregion

#region RecipeDefinition Schema

public class RecipeDefinitionSchema
{
    /// <summary>Unique recipe ID (e.g., "mymod:cook_apple")</summary>
    public string Id { get; set; } = "";
    
    /// <summary>Result item ID</summary>
    public string Result { get; set; } = "";
    
    /// <summary>Number of items produced</summary>
    public int ResultCount { get; set; } = 1;
    
    /// <summary>Display name</summary>
    public string? Name { get; set; }
    
    /// <summary>Crafting bench: none, normal, kitchen, druglab</summary>
    public string Bench { get; set; } = "none";
    
    /// <summary>Required skill level</summary>
    public int SkillRequired { get; set; } = 0;
    
    /// <summary>Auto-unlock on game start</summary>
    public bool AutoUnlock { get; set; } = true;
    
    /// <summary>Required ingredients</summary>
    public List<IngredientSchema>? Ingredients { get; set; }
    
    /// <summary>Recipe type: Structure, Cook, Item, Improvisation, Weapon, ForRepair, ForUpgrade</summary>
    public string Type { get; set; } = "Item";
    
    /// <summary>Crafting difficulty level</summary>
    public int CraftingDifficulty { get; set; } = 0;
    
    /// <summary>Secondary/alternate ingredients</summary>
    public List<IngredientSchema>? SecondaryIngredients { get; set; }
    
    /// <summary>Recipe is disabled</summary>
    public bool NotActive { get; set; } = false;
    
    /// <summary>Required street knowledge: NOTHING, SURVIVAL, BEGGING, etc.</summary>
    public string? RequireSK { get; set; }
    
    /// <summary>Associated bench for multi-bench recipes</summary>
    public string? AssociatedBench { get; set; }
}

public class IngredientSchema
{
    /// <summary>Item ID</summary>
    public uint Item { get; set; }
    
    /// <summary>Required count</summary>
    public int Count { get; set; } = 1;
}

#endregion

#region QuestDefinition Schema

public class QuestDefinitionSchema
{
    /// <summary>Unique quest ID</summary>
    public string Id { get; set; } = "";
    
    /// <summary>Quest title</summary>
    public string? Title { get; set; }
    
    /// <summary>Quest journal description</summary>
    public string? Description { get; set; }
    
    /// <summary>Quest persists after completion</summary>
    public bool IsPermanent { get; set; } = false;
    
    /// <summary>Quest type: "quest" or "favor"</summary>
    public string Type { get; set; } = "quest";
    
    /// <summary>Quest stages/nodes</summary>
    public List<QuestStageSchema>? Stages { get; set; }
    
    /// <summary>Completion rewards</summary>
    public List<RewardSchema>? Rewards { get; set; }
}

public class QuestStageSchema
{
    /// <summary>Stage ID</summary>
    public string Id { get; set; } = "";
    
    /// <summary>Stage type: dialogue, action, wait</summary>
    public string Type { get; set; } = "";
    
    /// <summary>Stage description</summary>
    public string? Description { get; set; }
    
    /// <summary>NPC ID for dialogue stages</summary>
    public string? Npc { get; set; }
    
    /// <summary>Dialogue definition</summary>
    public DialogueSchema? Dialogue { get; set; }
    
    /// <summary>Action definition</summary>
    public ActionSchema? Action { get; set; }
    
    /// <summary>Wait condition definition</summary>
    public WaitSchema? Wait { get; set; }
    
    /// <summary>Next stage IDs</summary>
    public List<string>? NextStages { get; set; }
    
    /// <summary>Actions to execute on stage completion</summary>
    public OnCompleteSchema? OnComplete { get; set; }
}

public class DialogueSchema
{
    /// <summary>Dialogue text</summary>
    public string? Text { get; set; }
    
    /// <summary>Dialogue options</summary>
    public List<DialogueOptionSchema>? Options { get; set; }
}

public class DialogueOptionSchema
{
    /// <summary>Option text</summary>
    public string? Text { get; set; }
    
    /// <summary>Next stage ID</summary>
    public string? NextStage { get; set; }
    
    /// <summary>Condition for showing option</summary>
    public string? Condition { get; set; }
    
    /// <summary>Action to trigger when selected</summary>
    public ActionSchema? Action { get; set; }
}

public class ActionSchema
{
    /// <summary>Action type: give_item, take_item, add_money, remove_money, start_quest, etc.</summary>
    public string Type { get; set; } = "";
    
    /// <summary>Action parameter</summary>
    public string? Param { get; set; }
    
    /// <summary>Amount for item/money actions</summary>
    public int Amount { get; set; } = 1;
}

public class WaitSchema
{
    /// <summary>Wait type: item, location, time</summary>
    public string Type { get; set; } = "";
    
    /// <summary>Item ID for item wait type</summary>
    public int ItemId { get; set; }
    
    /// <summary>Required count</summary>
    public int Count { get; set; } = 1;
}

public class RewardSchema
{
    /// <summary>Reward type: item, money, reputation</summary>
    public string Type { get; set; } = "";
    
    /// <summary>Item ID for item rewards</summary>
    public uint ItemId { get; set; }
    
    /// <summary>Amount</summary>
    public int Amount { get; set; } = 1;
}

public class OnCompleteSchema
{
    /// <summary>Actions to execute</summary>
    public List<ActionSchema>? Actions { get; set; }
}

#endregion
