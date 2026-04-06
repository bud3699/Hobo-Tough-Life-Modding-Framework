# HoboModFramework

A modding framework for **Hobo: Tough Life** that lets you add custom items, recipes, and quests using simple JSON files.

## What's in this folder?

| File | What it's for |
|------|---------------|
| **HoboModPlugin.dll** | The main plugin - drop this in your game's BepInEx/plugins folder |
| **PLAYER_GUIDE.txt** | Step-by-step installation instructions |
| **MODDING_GUIDE.md** | How to create mods (no coding required!) |
| **DEVELOPER_GUIDE.md** | Technical details for framework developers |
| **modding_resources/** | Item IDs and reference data for modding |
| **LICENSE** | MIT License (do whatever you want with this) |

### For Developers Only
| Folder/File | What it's for |
|-------------|---------------|
| **Framework/** | The source code that makes everything work |
| **Patches/** | Harmony patches that hook into the game |
| **lib/** | Where you put DLLs for building (see lib/README.md) |
| **Plugin.cs** | Entry point for the mod |
| **.csproj** | Project file for building |

## Quick Start

**Players:** Read the [Player Guide](PLAYER_GUIDE.txt)  
**Modders:** Read the [Modding Guide](MODDING_GUIDE.md)  
**Developers:** Read the [Developer Guide](DEVELOPER_GUIDE.md)
