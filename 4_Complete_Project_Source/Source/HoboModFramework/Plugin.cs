using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Game;
using HoboModPlugin.Features;
using HoboModPlugin.Framework;


namespace HoboModPlugin
{
    /// <summary>
    /// HoboModFramework - Pure modding enabler for Hobo: Tough Life.
    /// Uses BepInEx 6 for IL2CPP games.
    /// 
    /// CORE PRINCIPLE: The framework does NOTHING on its own.
    /// All features (items, recipes, hotkeys, etc.) come from mods.
    /// </summary>
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        public const string PLUGIN_GUID = "com.hobomod.framework";
        public const string PLUGIN_NAME = "HoboModFramework";
        public const string PLUGIN_VERSION = "1.0.0";

        internal static new ManualLogSource Log;
        private Harmony _harmony;
        
        // Framework components
        public static FrameworkManager Framework;
        
        // Configuration
        internal static ConfigEntry<bool> EnableDebugMode;

        public override void Load()
        {
            Log = base.Log;
            
            // Configuration - Debug mode OFF by default for players
            EnableDebugMode = Config.Bind(
                "Debug", 
                "EnableDebugMode", 
                false, 
                "Legacy setting - debug hotkeys now require debug_mod to be installed"
            );
            
            // Initialize framework
            var pluginPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            
            // Fix doorstop config to prevent Steam restart from disabling BepInEx console
            DoorstopConfigFixer.Initialize(Log, System.IO.Path.GetDirectoryName(pluginPath));
            
            Framework = new FrameworkManager(Log, pluginPath);
            
            // ShaderRegistry is DEPRECATED — the game uses a proprietary
            // "Render Pipeline/DitherLit" shader for all animated NPCs. The generic
            // Standard shader has its skinning math stripped by IL2CPP. We now clone
            // the native DitherLit shader directly from vanilla NPCs at runtime.
            // ShaderRegistry.Initialize(Log);
            
            // Discover mods early (before game databases load)
            Framework.DiscoverMods();
            
            // Register custom MonoBehaviours with IL2CPP BEFORE patches run
            // (patches may call AddComponent<T> which requires prior registration)
            ClassInjector.RegisterTypeInIl2Cpp<PropertyBlockSyncer>();
            
            // Apply Harmony patches (from Patches/ folder AND Framework/)
            _harmony = new Harmony(PLUGIN_GUID);
            _harmony.PatchAll();
            
            // Register our Update method to run every frame
            AddComponent<ModUpdater>();
            
            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded successfully!");
            Log.LogInfo($"Mods discovered: {Framework.ModLoader.LoadedMods.Count}");
            
            // Debug mode config is legacy - kept for backwards compatibility
            // Actual debug features now require debug_mod to be loaded
        }
    }
    
    /// <summary>
    /// MonoBehaviour that runs Update every frame to handle hotkeys.
    /// Framework does nothing on its own - all features come from mods.
    /// </summary>
    public class ModUpdater : MonoBehaviour
    {

        
        void Update()
        {
            // Check for custom object spawn hotkeys
            CustomObjectRegistry.CheckHotkeys();
            
            // Check for external tweaker tool config updates
            ModelOverrideRegistry.CheckExternalConfig();
            

        }
        

    }
}
