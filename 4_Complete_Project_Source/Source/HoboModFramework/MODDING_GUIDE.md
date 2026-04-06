# 🎮 How to Create Mods for Hobo: Tough Life

> **No programming knowledge required!** This guide teaches you how to create mods using simple text files.

---

## 📦 What You Need

1. **The game** - Hobo: Tough Life
2. **HoboModFramework** - Installed in your game's BepInEx folder
3. **A text editor** - Notepad, VS Code, or any text editor
4. **An image editor** - For creating icons (Paint, GIMP, etc.)

---

## 🚀 Quick Start: Your First Mod

### Step 1: Create Your Mod Folder

Navigate to:
```
[Game Folder]/BepInEx/plugins/HoboMods/
```

Create a new folder with your mod's name (no spaces):
```
HoboMods/
└── MyAwesomeMod/
```

### Step 2: Create mod.json

Every mod needs a `mod.json` file. Create this file in your mod folder:

```json
{
  "id": "my_awesome_mod",
  "name": "My Awesome Mod",
  "version": "1.0.0",
  "author": "YourName",
  "description": "Adds cool new items to the game!"
}
```

| Field | What It Means |
|-------|---------------|
| `id` | Unique ID for your mod (lowercase, underscores OK) |
| `name` | Display name shown in logs |
| `version` | Your mod's version number |
| `author` | Your name or username |
| `description` | What your mod does |

---

## 🧪 Adding a Custom Item

### Step 1: Create the Items Folder

```
MyAwesomeMod/
├── mod.json
└── items/          ← Create this folder
```

### Step 2: Create an Item File

Create `items/healing_soup.json`:

```json
{
  "id": "healing_soup",
  "baseItem": 1,
  "type": "consumable",
  "name": "@healing_soup.name",
  "description": "@healing_soup.desc",
  "icon": "assets/icons/healing_soup.png",
  "price": 50,
  "effects": [
    { "stat": "health", "value": "+25" },
    { "stat": "food", "value": "+50" }
  ]
}
```

### Understanding the Item File

| Field | What It Means | Example |
|-------|---------------|---------|
| `id` | Unique item ID | `"healing_soup"` |
| `baseItem` | Game item to copy from (1 = Roll) | `1` |
| `type` | Item type | `"consumable"` |
| `name` | Display name (use @ for translations) | `"@healing_soup.name"` |
| `icon` | Path to your icon image | `"assets/icons/soup.png"` |
| `price` | How much it costs | `50` |
| `effects` | What happens when used | See below |

### Effect Values Explained

| Value | What It Does |
|-------|--------------|
| `"max"` | Sets stat to maximum |
| `"0"` | Sets stat to zero |
| `"+50"` | Adds 50 to current value |
| `"-10"` | Subtracts 10 from current value |
| `"75"` | Sets stat to exactly 75 |

### Available Stats

| Stat Name | What It Affects |
|-----------|-----------------|
| `health` | Health points |
| `food` | Hunger level |
| `morale` | Happiness |
| `freshness` | Cleanliness |
| `warmth` | Body temperature |
| `stamina` | Energy for running |
| `illness` | Sickness level (0 = healthy) |
| `toxicity` | Alcohol/drug poisoning |
| `wet` | Wetness from rain |

---

## 👕 Adding Gear (Clothing) Items

Gear items are wearable clothing that give stat bonuses when equipped.

### Example: Custom Jacket

Create `items/combat_jacket.json`:

```json
{
  "id": "combat_jacket",
  "type": "gear",
  "category": "jacket",
  "baseItem": 117,
  "name": "Combat Jacket",
  "description": "A reinforced jacket for tough situations",
  "price": 500,
  "icon": "Assets/combat_jacket.png",
  "warmResistance": 10,
  "wetResistance": 5,
  "stats": [
    { "type": "attack", "value": 5 },
    { "type": "defense", "value": 10 },
    { "type": "charisma", "value": -3 }
  ]
}
```

### Gear Properties

| Field | What It Means |
|-------|---------------|
| `type` | Must be `"gear"` |
| `category` | Clothing slot (see below) |
| `baseItem` | ID of gear item to clone (use F11 in-game to find IDs) |
| `warmResistance` | Cold protection (0-20) |
| `wetResistance` | Rain protection (0-20) |
| `stats` | Array of stat bonuses (see below) |

### Gear Categories

| Category | Slot |
|----------|------|
| `jacket` | Upper body |
| `trousers` | Lower body |
| `shoes` | Feet |
| `gloves` | Hands |
| `hat` | Head |

### Gear Stat Types

| Stat | Effect |
|------|--------|
| `attack` | Combat damage bonus |
| `defense` | Damage reduction |
| `charisma` | Social interaction bonus |
| `speed` | Movement speed |
| `agility` | Dodge chance |
| `luck` | Random events |

> ⚠️ **Note:** Gear stats have slight random variance (±1) like vanilla items.

---

## 🗡️ Adding Weapon Items

Weapons are equippable combat items.

### Example: Custom Weapon

```json
{
  "id": "steel_pipe",
  "type": "weapon",
  "baseItem": 200,
  "name": "Steel Pipe",
  "description": "A sturdy metal pipe for self-defense",
  "price": 100,
  "icon": "Assets/steel_pipe.png",
  "damage": 15,
  "attackSpeed": 1.2
}
```

### Weapon Properties

| Field | What It Means |
|-------|---------------|
| `type` | Must be `"weapon"` |
| `baseItem` | ID of weapon to clone |
| `damage` | Attack power |
| `attackSpeed` | How fast it swings |

---

## 🔍 Finding Base Item IDs

Press **F11** in-game to list all Gear items with their IDs in the log:

```
GEAR ID[117] 'Worn Jacket' Category:Jacket Warm:2 Wet:2
GEAR ID[118] 'Leather Jacket' Category:Jacket Warm:3 Wet:2
...
```

Check `BepInEx/LogOutput.log` for the full list.

---

## 🌍 Adding Translations

### Step 1: Create Localization Folder

```
MyAwesomeMod/
├── mod.json
├── items/
│   └── healing_soup.json
└── localization/          ← Create this folder
```

### Step 2: Create Language File

Create `localization/en.json`:

```json
{
  "healing_soup.name": "Grandma's Healing Soup",
  "healing_soup.desc": "A warm bowl of soup that heals the body and soul."
}
```

The `@` symbol in your item file points to these translations!

---

## 🎨 Adding a Custom Icon

### Step 1: Create Assets Folder

```
MyAwesomeMod/
├── mod.json
├── items/
├── localization/
└── assets/
    └── icons/          ← Create this folder
```

### Step 2: Create Your Icon

- **Size:** 128 x 128 pixels
- **Format:** PNG (with transparency)
- **Save as:** The filename you specified in your item (e.g., `healing_soup.png`)

---

## ✅ Complete Mod Structure

Here's what your finished mod should look like:

```
MyAwesomeMod/
├── mod.json
├── items/
│   └── healing_soup.json
├── localization/
│   └── en.json
└── assets/
    └── icons/
        └── healing_soup.png
```

---

## 🎯 Example: Super Healing Potion

### mod.json
```json
{
  "id": "super_potion_mod",
  "name": "Super Potion Mod",
  "version": "1.0.0",
  "author": "YourName",
  "description": "Adds a super healing potion"
}
```

### items/super_potion.json
```json
{
  "id": "super_potion",
  "baseItem": 1,
  "type": "consumable",
  "name": "@super_potion.name",
  "description": "@super_potion.desc",
  "icon": "assets/icons/super_potion.png",
  "price": 500,
  "effects": [
    { "stat": "health", "value": "max" },
    { "stat": "food", "value": "max" },
    { "stat": "morale", "value": "max" },
    { "stat": "illness", "value": "0" }
  ]
}
```

### localization/en.json
```json
{
  "super_potion.name": "Super Potion",
  "super_potion.desc": "Completely restores health, food, and morale. Cures all illness!"
}
```

---

## ❓ Troubleshooting

### "My mod doesn't appear"
- Check that `mod.json` exists and is valid JSON
- Make sure folder is inside `HoboMods/`

### "Item doesn't work"
- Check item JSON for typos
- Make sure `baseItem` is a valid item ID (1 is safe)

### "Icon not showing"
- Check icon path matches exactly
- Icon must be PNG format
- Size should be 128x128 pixels

### How to Check Logs
Look at: `[Game Folder]/BepInEx/LogOutput.log`

Search for your mod name to see if it loaded!

---

## 🎉 That's It!

You just learned how to create mods without writing any code!

**Coming Soon:**
- Recipe system (craft your items)
- NPC modifications
- Quest creation
- Custom 3D models
