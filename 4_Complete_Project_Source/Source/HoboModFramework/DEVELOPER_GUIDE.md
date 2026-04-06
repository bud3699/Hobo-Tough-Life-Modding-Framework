# Developer Guide - Creating Mods

This guide is for mod creators who want to make custom items, recipes, and quests.

---

## Prerequisites

- Basic understanding of JSON
- A text editor (VS Code recommended)
- Hobo: Tough Life installed
- BepInEx 6 IL2CPP installed

---

## Mod Structure

Every mod needs this folder structure:

```
BepInEx/plugins/HoboMods/YourModName/
├── mod.json              # Required: Mod metadata
├── items/                # Optional: Custom items
│   └── item_name.json
├── recipes/              # Optional: Crafting recipes
│   └── recipe_name.json
├── quests/               # Optional: Custom quests
│   └── quest_name.json
└── assets/               # Optional: Icons, images
    └── icons/
        └── item_icon.png
```

---

## mod.json (Required)

```json
{
    "id": "your_mod_id",
    "name": "Your Mod Name",
    "version": "1.0.0",
    "author": "YourName",
    "description": "What your mod does"
}
```

**Rules:**
- `id` must be lowercase, no spaces (use underscores)
- `id` must be unique across all mods

---

## Creating Items

### Basic Item Template

`items/my_item.json`:
```json
{
    "id": "super_drink",
    "baseItem": 4,
    "type": "consumable",
    "name": "Super Energy Drink",
    "description": "Restores energy and health",
    "price": 50,
    "weight": 0.3,
    "effects": [
        {"stat": "health", "value": "max"},
        {"stat": "energy", "value": "+50"}
    ]
}
```

### Base Items (What to Clone)

| ID | Item | Best For |
|----|------|----------|
| 1 | Roll | Food items |
| 4 | Beer | Drinks/consumables |
| 7 | Cigarette | Smoked items |
| 46 | Medicine | Healing items |

### Effect Stats

| Stat Name | Description | Good Value |
|-----------|-------------|------------|
| `health` | Health points | max, +50 |
| `energy` | Energy/tiredness | max, +30 |
| `food` | Hunger | max, +40 |
| `morale` | Happiness | max, +25 |
| `warmth` | Body temperature | max, +20 |
| `odour` | Smell (0=clean) | min, -50 |
| `illness` | Sickness (0=healthy) | min, -100 |
| `toxicity` | Alcohol/poison | min, -50 |

### Value Formats

| Format | Example | Meaning |
|--------|---------|---------|
| `"max"` | `"max"` | Set to maximum |
| `"min"` | `"min"` | Set to zero |
| `"+50"` | `"+50"` | Add 50 |
| `"-30"` | `"-30"` | Subtract 30 |
| `"100"` | `"100"` | Set to exactly 100 |

---

## Creating Recipes

`recipes/my_recipe.json`:
```json
{
    "id": "super_drink_recipe",
    "result": "super_drink",
    "resultCount": 1,
    "name": "Super Energy Drink",
    "bench": "none",
    "ingredients": [
        {"item": 4, "count": 1},
        {"item": 46, "count": 1}
    ]
}
```

### Bench Types

| Bench | Description |
|-------|-------------|
| `"none"` | No crafting station needed |
| `"fire"` | Requires campfire |
| `"stove"` | Requires cooking stove |

### Finding Item IDs

Check `modding_resources/ItemDump.txt` for all game item IDs, or use F11 with debug_mod to generate a fresh dump.

---

## Creating Quests

`quests/my_quest.json`:
```json
{
    "id": "find_food",
    "title": "Find Some Food",
    "autoStart": false,
    "stages": [
        {
            "id": "start",
            "description": "Find 3 rolls to complete the quest",
            "wait": {
                "type": "item",
                "item_id": 1,
                "count": 3
            }
        }
    ]
}
```

---

## Testing Your Mod

1. Place your mod folder in `BepInEx/plugins/HoboMods/`
2. Start the game
3. Check `BepInEx/LogOutput.log` for errors
4. Look for `Loaded: YourModName` message

---

## Common Mistakes

| Problem | Solution |
|---------|----------|
| Item not appearing | Check `id` matches in item and recipe files |
| Recipe not showing | Make sure `result` matches item `id` |
| Effects not working | Use correct stat names (case-sensitive) |
| JSON parse error | Validate JSON at jsonlint.com |

---

## Building the Framework (Advanced)

If you want to modify the framework itself:

1. Clone this repository
2. Copy DLLs to `lib/` folder (see `lib/README.md`)
3. Build: `dotnet build --configuration Release`
4. Copy `bin/Release/net6.0/HoboModPlugin.dll` to game

---

## Need Help?

- Check `MODDING_GUIDE.md` for detailed modding tutorials
- Open an issue on GitHub
- Join the community discussions
