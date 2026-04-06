using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Game;

namespace HoboModPlugin.Patches
{
    /// <summary>
    /// Harmony patch to trigger framework content injection when RecipeDatabase loads
    /// </summary>
    [HarmonyPatch(typeof(RecipeDatabase), nameof(RecipeDatabase.OnAwake))]
    public static class RecipeDatabasePatch
    {
        // First mod recipe ID to check for injection
        private const uint FIRST_MOD_RECIPE_ID = 51000;
        
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var recipes = RecipeDatabase.recipes;
                if (recipes == null || recipes.Count == 0) return;
                
                // Skip if mod content already injected
                if (recipes.ContainsKey(FIRST_MOD_RECIPE_ID)) return;
                
                // Inject framework content
                if (Plugin.Framework != null)
                {
                    Plugin.Framework.InjectContent();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"RecipeDatabase postfix error: {ex.Message}");
            }
        }
    }
}
