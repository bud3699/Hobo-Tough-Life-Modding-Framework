# HoboModFramework - Modder Reference

## Item Properties (JSON)

### Basic
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| id | string | REQUIRED | Unique item ID |
| name | string | REQUIRED | Display name |
| description | string | REQUIRED | Item description |
| baseItem | int | REQUIRED | Vanilla item ID to clone |
| type | string | REQUIRED | consumable/gear/weapon |
| price | int | 0 | Buy/sell value |
| weight | float | 0.1 | Item weight |

### Inventory
| Property | Type | Default |
|----------|------|---------|
| isForHotSlot | bool | false |
| isStockable | bool | true |
| actualStockCount | int | 1 |
| sellable | bool | true |
| isStealable | bool | false |

### Visual/Category
| Property | Type | Default |
|----------|------|---------|
| rareColor | int | 0 |
| hint | string | NA |
| soundType | int | 0 |
| subCategory | string | Default |

### Survival
| Property | Type | Default |
|----------|------|---------|
| firerate | int | 0 |
| notForFire | bool | false |
| spawnDay | int | 0 |
| isPermanent | bool | false |
| heavyItem | bool | false |
| hardItem | bool | false |

### Quest/Salvage
| Property | Type | Default |
|----------|------|---------|
| questType | string | Nothing |
| salvageTableId | int | -1 |
| buyBackTime | float | 0 |

### Shop/Minigame
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| buffetGame | bool | false | Triggers buffet minigame |
| buffetDifficulty | int | 0 | Minigame difficulty |
| index | int | -1 | Menu ordering (-1=auto) |
| improRecipes | array | [] | Linked recipe IDs |

---

## Effect System (Consumables)

### effects (Framework Custom Effects)
```json
"effects": [
  { "stat": "health", "value": "+50" },
  { "stat": "energy", "value": "max" }
]
```

**Value Formats:** `"max"`, `"min"`, `"+50"`, `"-20"`, `"100"` (absolute)

**Valid Stats (lowercase):**

| Stat Name | Game Property | UI Display | Notes |
|-----------|---------------|------------|-------|
| `health` | c.Health | Health | Core stat, 0-100 |
| `food` | c.Food | Food | Core stat, 0-100 |
| `morale` | c.Morale | Morale | Core stat, 0-100 |
| `energy` / `sleep` | c.Freshness | Energy | Sleep/tiredness |
| `warmth` / `warm` | c.Warm | Warmth | Temperature |
| `stamina` | c.Stamina | Stamina | Running endurance |
| `wet` / `dryness` | c.Wet | Dryness | Wetness |
| `illness` | c.Illness | Illness | Sickness level |
| `toxicity` / `poisoning` | c.Toxicity | Poisoning | Poison level |
| `alcohol` | c.Alcohol | Alcohol | Intoxication |
| `smell` / `odour` | c.Smell | Odour | Body smell |
| `grit` / `willpower` | c.Grit | Willpower | Combat (0-2 range) |
| `greatneed` / `bathroom` | c.Greatneed | Number Two | Bathroom |

### statChanges (Native Game Format)
```json
"statChanges": [
  { "stat": "Food", "value": 50, "addictedValue": 25 }
]
```

**Valid Stats:** Health, Food, Morale, Warm, Wet, Illness, Toxicity, Alcohol, Smell, Freshness, Attack, Defense, Charisma, Courage, Grit, Capacity

### buffEffects (Native Buffs)
```json
"buffEffects": [
  { "buff": "Healing", "tier": 2, "addictedTier": 1, "addictionType": "NOTHING" }
]
```

**Valid Buff Types:**

| Buff | Effect |
|------|--------|
| `Healing` | Health regeneration over time |
| `Faith` | Morale-related buff |
| `Cure` | Cures illness |
| `Warming` | Warmth/temperature buff |
| `Confidence` | Charisma/social buff |
| `Deodor` | Reduces smell |
| `Raging` | Combat attack boost |
| `Hideout` | Stealth/hiding buff |
| `NOTHING` | No buff |

**Tiers:** 0 (none), 1 (weak), 2 (medium), 3 (strong)

### addictionType
```json
"addictionType": "Alcohol"
```

**Valid Types:** NOTHING, Alcohol, Drugs, Cigarettes

---

## Gear Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| category | string | Hat | Slot type |
| warmResistance | int | 0 | Cold protection |
| wetResistance | int | 0 | Rain protection |
| durabilityResistance | int | 100 | Durability rating |
| repairRecipe | int | 0 | Recipe ID for repair |
| repairCash | int | 0 | Cash cost to repair |

**Categories:** Hat, Jacket, Trousers, Shoes

---

## Weapon Properties

| Property | Type | Default |
|----------|------|---------|
| attack | int | 0 |
| defense | int | 0 |
| criticalChance | int | 0 |
| maxDurability | int | 100 |

---

## Recipe Properties (JSON)

### Basic
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| id | string | REQUIRED | Unique recipe ID |
| name | string | REQUIRED | Recipe display name |
| result | string | REQUIRED | Item ID to produce |
| resultCount | int | 1 | How many to craft |
| ingredients | array | REQUIRED | Primary ingredients |

### Advanced
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| type | string | Item | Recipe category |
| bench | string | none | Required crafting station |
| skillRequired | int | 0 | Skill level needed |
| craftingDifficulty | int | 0 | Difficulty level |
| autoUnlock | bool | true | Auto-unlock for player |
| secondaryIngredients | array | [] | Alternate ingredients |
| notActive | bool | false | Disable recipe |
| requireSK | string | NOTHING | Street Knowledge required |
| associatedBench | string | none | Secondary bench

**Types:** Structure, Cook, Item, Improvisation, Weapon, ForRepair, ForUpgrade

**Benches:** none, normal, kitchen, druglab

### Recipe Example
```json
{
  "id": "my_recipe",
  "name": "My Recipe",
  "result": "my_item",
  "type": "Cook",
  "bench": "kitchen",
  "skillRequired": 2,
  "craftingDifficulty": 1,
  "autoUnlock": true,
  "ingredients": [
    { "item": 7, "count": 2 }
  ],
  "secondaryIngredients": [
    { "item": 15, "count": 1 }
  ]
}
```

---

## Events

### Available Events
| Event | Args | Description |
|-------|------|-------------|
| OnItemPickup | ItemEventArgs | Item picked up |
| OnItemUsed | ItemEventArgs | Consumable used |
| OnItemDropped | ItemEventArgs | Item dropped |
| OnItemEquipped | EquipEventArgs | Gear/Weapon equipped |
| OnItemUnequipped | EquipEventArgs | Gear/Weapon unequipped |
| OnItemCrafted | CraftEventArgs | Player crafts item |
| OnStatChanged | StatChangeEventArgs | Any stat changes |
| OnHourChanged | TimeEventArgs | Game hour changes |
| OnDayChanged | TimeEventArgs | Game day changes |
| OnSeasonChanged | SeasonEventArgs | Season changes |
| OnGameSaved | GameEventArgs | Game saved |
| OnGameLoaded | GameEventArgs | Save loaded |
| OnPlayerDeath | PlayerEventArgs | Player dies |
| OnQuestNodeCompleted | QuestEventArgs | Quest node finished |
| OnWeatherChanged | WeatherEventArgs | Weather state changes |

### Event Args Properties

**ItemEventArgs:** ItemId, ItemName, Count, Item, Cancelled

**EquipEventArgs:** ItemId, ItemName, ItemType, Item

**CraftEventArgs:** RecipeId, RecipeName, ResultItemId, ResultItemName, Success, Timestamp

**StatChangeEventArgs:** StatName, OldValue, NewValue, Cancelled

**TimeEventArgs:** Hour, Day, Season, PreviousHour, PreviousDay

**QuestEventArgs:** QuestId, NodeId, IsSuccess, NumDone

**WeatherEventArgs:** CurrentWeather, TargetWeather

---

## Hotkeys (mod.json)

```json
"devHotkeys": [
  { "key": "F9", "action": "spawn_item", "itemId": "my_item" },
  { "key": "F6", "action": "spawn_vanilla", "itemId": "7" }
]
```

**Actions:** spawn_item, spawn_vanilla, explore_items

---

## Base Item IDs (Common)

| ID | Item |
|----|------|
| 7 | Apple |
| 165 | Wool Hat |
| 473 | Shopping Cart |
| 203 | Bandage |
| 208 | Lockpick |
