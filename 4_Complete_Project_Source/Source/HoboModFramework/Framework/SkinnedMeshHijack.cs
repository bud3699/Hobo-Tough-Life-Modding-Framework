using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Game.AI;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Skinned Mesh Replacement — Multi-Mesh "Assemble the Zombie" Edition
    ///
    /// Replaces a vanilla NPC mesh with a modular custom character that may consist of
    /// many individual SkinnedMeshRenderer pieces (head, torso, arms, legs, etc.)
    ///
    /// STRATEGY:
    ///   1. Load the custom prefab from its AssetBundle.
    ///   2. Instantiate the entire prefab hierarchy as a child of the NPC root so all
    ///      custom bones are live scene transforms.
    ///   3. Disable the vanilla SMR (hide the hobo body).
    ///   4. For every custom SMR piece found in the instantiated hierarchy:
    ///      a. Run BoneRemapper to translate its bone array onto live Vanilla bones.
    ///      b. Set the remapped bones array, mesh, and materials on the piece.
    ///      c. Enable the piece so Unity renders it.
    ///   5. Hide the duplicate SMR objects we don't need from the old prefab clone.
    ///
    /// BONE REMAPPING (THE MEATBALL FIX):
    ///   Vanilla Hobo bones are actively animated by the game's HumanoidAnimator script.
    ///   Our custom bones are ignored by that script and collapse to (0,0,0).
    ///   BoneRemapper translates each custom bone slot to its Vanilla equivalent, so the
    ///   custom mesh pieces are driven by the game's animation system for free.
    /// </summary>
    public static class SkinnedMeshHijack
    {
        private static ManualLogSource _log;
        private static Harmony _harmony;

        // ── Config ──────────────────────────────────────────────────────
        // Key   = vanilla mesh name prefix (e.g. "SHMajsner")
        // Value = (bundlePath, prefabName)
        private static readonly Dictionary<string, (string bundlePath, string prefabName)>
            _overrideMap = new(StringComparer.OrdinalIgnoreCase);

        // ── Prefab Cache ────────────────────────────────────────────────
        // Template prefabs loaded from AssetBundles; never modified in place.
        private static readonly Dictionary<string, GameObject>
            _prefabCache = new(StringComparer.OrdinalIgnoreCase);

        // ── Runtime Tracking ───────────────────────────────────────────
        // Tracks which NPC instances are hijacked for LOD defence.
        private struct HijackState
        {
            public SkinnedMeshRenderer VanillaSMR;    // The original vanilla SMR (kept, but disabled)
            public GameObject          CustomRoot;     // Root of the instantiated custom hierarchy
        }
        private static readonly Dictionary<int, HijackState> _hijackedNPCs = new();

        // ================================================================
        //  PUBLIC API
        // ================================================================

        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _harmony = new Harmony("com.hobomod.skinnedmeshhijack");
            _log.LogInfo("[SkinnedMeshHijack] Initializing...");
            ApplyPatches();
        }

        private static void ApplyPatches()
        {
            try
            {
                var targetType    = typeof(NPCModelBehavior);
                int patchedCount  = 0;

                // Hook 1: InitializeNPCModel — main hijack entry point
                var initMethod = targetType.GetMethod("InitializeNPCModel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (initMethod != null)
                {
                    _harmony.Patch(initMethod, postfix: new HarmonyMethod(
                        typeof(SkinnedMeshHijack).GetMethod(nameof(InitializeNPCModel_Postfix),
                        BindingFlags.Static | BindingFlags.NonPublic)));
                    _log?.LogInfo($"[SkinnedMeshHijack] Hooked {initMethod.Name}");
                    patchedCount++;
                }
                else _log?.LogError("[SkinnedMeshHijack] FAILED to find InitializeNPCModel");

                // Hook 2: OnModelLoaded — sonar logging
                var loadedMethod = targetType.GetMethod("OnModelLoaded",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (loadedMethod != null)
                {
                    _harmony.Patch(loadedMethod, postfix: new HarmonyMethod(
                        typeof(SkinnedMeshHijack).GetMethod(nameof(OnModelLoaded_Postfix),
                        BindingFlags.Static | BindingFlags.NonPublic)));
                    _log?.LogInfo($"[SkinnedMeshHijack] Hooked {loadedMethod.Name}");
                    patchedCount++;
                }
                else _log?.LogError("[SkinnedMeshHijack] FAILED to find OnModelLoaded");

                // Hook 3: SwitchLOD — LOD defender
                var switchMethod = targetType.GetMethod("SwitchLOD",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (switchMethod != null)
                {
                    _harmony.Patch(switchMethod, postfix: new HarmonyMethod(
                        typeof(SkinnedMeshHijack).GetMethod(nameof(SwitchLOD_Postfix),
                        BindingFlags.Static | BindingFlags.NonPublic)));
                    _log?.LogInfo($"[SkinnedMeshHijack] Hooked {switchMethod.Name}");
                    patchedCount++;
                }
                else _log?.LogError("[SkinnedMeshHijack] FAILED to find SwitchLOD");

                _log?.LogInfo($"[SkinnedMeshHijack] Applied {patchedCount} patches.");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[SkinnedMeshHijack] Fatal error in ApplyPatches: {ex}");
            }
        }

        /// <summary>Registers a mesh override from mod.json processing.</summary>
        public static void RegisterOverride(string targetMeshName, string bundlePath, string prefabName)
        {
            _overrideMap[targetMeshName] = (bundlePath, prefabName);
            _log?.LogInfo($"[SkinnedMeshHijack] Registered: {targetMeshName} -> " +
                          $"{System.IO.Path.GetFileName(bundlePath)}:{prefabName}");
        }

        // ================================================================
        //  HARMONY PATCH 1: InitializeNPCModel Postfix
        // ================================================================

        private static void InitializeNPCModel_Postfix(NPCModelBehavior __instance)
        {
            try
            {
                // ── 1: Identify the vanilla SMR and mesh ───────────────────
                var vanillaSMR = __instance.GetComponentInChildren<SkinnedMeshRenderer>();
                if (vanillaSMR == null || vanillaSMR.sharedMesh == null) return;

                string vanillaMeshName = vanillaSMR.sharedMesh.name;
                if (Plugin.EnableDebugMode.Value)
                    _log?.LogInfo($"[SONAR] NPC Spawned | GO: {__instance.gameObject.name} | Mesh: {vanillaMeshName}");

                // ── 2: Check if this NPC has a registered override ─────────
                (string bundlePath, string prefabName) overrideInfo = default;
                bool isMapped = false;
                foreach (var kvp in _overrideMap)
                {
                    if (vanillaMeshName.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        overrideInfo = kvp.Value;
                        isMapped     = true;
                        break;
                    }
                }
                if (!isMapped) return;

                _log?.LogInfo($"[SkinnedMeshHijack] HIJACKING '{vanillaMeshName}' on {__instance.gameObject.name}");

                // ── 3: Load the custom prefab template ────────────────────
                var prefabTemplate = GetOrLoadPrefab(overrideInfo.bundlePath, overrideInfo.prefabName);
                if (prefabTemplate == null)
                {
                    _log?.LogError($"[SkinnedMeshHijack] Failed to load prefab '{overrideInfo.prefabName}'");
                    return;
                }

                // ── 4: Instantiate the ENTIRE prefab hierarchy into the scene ──
                // This makes all custom bones live scene Transforms so BoneRemapper
                // can safely reference them as SMR fallback bones.
                var customRoot = UnityEngine.Object.Instantiate(prefabTemplate, __instance.transform);
                customRoot.name              = $"CustomModel_{overrideInfo.prefabName}";
                customRoot.transform.localPosition = Vector3.zero;
                customRoot.transform.localRotation = Quaternion.identity;
                customRoot.transform.localScale    = Vector3.one;

                // ── CRITICAL: Native Shader Injection ────────────────────────
                // The game uses "Render Pipeline/DitherLit" for all animated NPCs.
                // Unity's IL2CPP build permanently stripped bone-skinning math from
                // the generic "Standard" shader. We clone the vanilla NPC's live
                // material (which carries DitherLit with full skinning) and inject
                // the modder's textures into it.
                ShaderRemapper.FixMaterials(customRoot, vanillaSMR.sharedMaterial);

                // ── 5: Grab the custom Animator (for Avatar-based bone translation) ─
                var customAnimator = customRoot.GetComponentInChildren<Animator>();

                // ── 6: Collect ALL SMR pieces from the instantiated hierarchy ─
                var customSMRs = customRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
                _log?.LogInfo($"[SkinnedMeshHijack]   Found {customSMRs.Length} custom SMR piece(s).");

                if (customSMRs.Length == 0)
                {
                    _log?.LogError("[SkinnedMeshHijack] No SMRs found in instantiated custom prefab!");
                    UnityEngine.Object.Destroy(customRoot);
                    return;
                }

                // ── 7: Hide the vanilla SMR (the hobo body underneath) ───────
                // We keep it alive because the game's LOD and clothing systems
                // may still depend on it structurally. We just make it invisible.
                vanillaSMR.enabled = false;

                // ── 8: Remap and enable each custom SMR piece ──────────────
                int pieceCount = 0;
                foreach (var piece in customSMRs)
                {
                    if (piece.sharedMesh == null)
                    {
                        _log?.LogWarning($"[SkinnedMeshHijack]   Skipping SMR with null mesh: {piece.name}");
                        continue;
                    }

                    _log?.LogInfo($"[SkinnedMeshHijack]   Processing piece: {piece.name} " +
                                  $"({piece.sharedMesh.vertexCount} verts, {piece.bones?.Length ?? 0} bones)");

                    // Run the BoneRemapper on this piece's bone array
                    var remappedBones = BoneRemapper.CreateRemappedArray(
                        vanillaSmr:          vanillaSMR,
                        customAnimator:      customAnimator,
                        instancedCustomBones: piece.bones,
                        log:                 _log);

                    piece.bones             = remappedBones;
                    piece.updateWhenOffscreen = true;  // Prevent frustum-culling invisibility
                    piece.enabled           = true;    // Ensure the piece renders

                    // ── CRITICAL: Copy rendering metadata from the native SMR ──
                    // If we don't do this, the custom pieces stay on the 'Default' layer
                    // and the game's character cameras completely ignore them!
                    piece.gameObject.layer = vanillaSMR.gameObject.layer;
                    piece.gameObject.tag   = vanillaSMR.gameObject.tag;
                    piece.shadowCastingMode = vanillaSMR.shadowCastingMode;
                    piece.receiveShadows    = vanillaSMR.receiveShadows;

                    // Also set the root bone to the vanilla root for correct bounds calculation
                    if (vanillaSMR.rootBone != null)
                        piece.rootBone = vanillaSMR.rootBone;

                    pieceCount++;
                }

                // ── 9: Track the hijacked NPC for LOD defence ──────────────
                int instanceId = __instance.GetInstanceID();
                _hijackedNPCs[instanceId] = new HijackState
                {
                    VanillaSMR = vanillaSMR,
                    CustomRoot  = customRoot,
                };

                _log?.LogInfo($"[SkinnedMeshHijack] SUCCESS: Assembled {pieceCount} piece(s) on {__instance.gameObject.name}");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[SkinnedMeshHijack] InitializeNPCModel_Postfix EXCEPTION: {ex}");
            }
        }

        // ================================================================
        //  HARMONY PATCH 1B: OnModelLoaded Postfix (Sonar)
        // ================================================================

        private static void OnModelLoaded_Postfix(NPCModelBehavior __instance)
        {
            try
            {
                var smr = __instance.GetComponentInChildren<SkinnedMeshRenderer>();
                string meshName = smr?.sharedMesh != null ? smr.sharedMesh.name : "NULL_MESH";
                if (Plugin.EnableDebugMode.Value)
                    _log?.LogInfo($"[SONAR] OnModelLoaded | GO: {__instance.gameObject.name} | Mesh: {meshName}");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[SkinnedMeshHijack] OnModelLoaded_Postfix EXCEPTION: {ex}");
            }
        }

        // ================================================================
        //  HARMONY PATCH 2: SwitchLOD Postfix (LOD Defender)
        // ================================================================

        /// <summary>
        /// Fires after the native engine swaps to a vanilla LOD mesh.
        /// Re-disables the vanilla SMR and ensures our custom pieces stay enabled.
        /// (Confirmed from Ghidra C source: SwitchLOD calls set_sharedMesh on the
        ///  vanilla SMR only — it does not know about our injected custom pieces.)
        /// </summary>
        private static void SwitchLOD_Postfix(NPCModelBehavior __instance)
        {
            try
            {
                int instanceId = __instance.GetInstanceID();
                if (!_hijackedNPCs.TryGetValue(instanceId, out var state)) return;

                // Re-disable the vanilla body (SwitchLOD may have re-enabled it)
                if (state.VanillaSMR != null)
                    state.VanillaSMR.enabled = false;

                // Re-enable all custom SMR pieces and their offscreen flag
                if (state.CustomRoot != null)
                {
                    var pieces = state.CustomRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
                    foreach (var p in pieces)
                    {
                        if (p.sharedMesh != null)
                        {
                            p.enabled             = true;
                            p.updateWhenOffscreen = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[SkinnedMeshHijack] SwitchLOD_Postfix EXCEPTION: {ex}");
            }
        }

        // ================================================================
        //  PRIVATE HELPERS
        // ================================================================

        private static GameObject GetOrLoadPrefab(string bundlePath, string prefabName)
        {
            string cacheKey = $"{bundlePath}::{prefabName}".ToLowerInvariant();
            if (_prefabCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var bundle = AssetBundleLoader.LoadBundle(bundlePath);
            if (bundle == null) return null;

            var prefab = bundle.LoadAsset<GameObject>(prefabName);
            if (prefab == null)
            {
                _log?.LogError($"[SkinnedMeshHijack] Asset '{prefabName}' not found. Available assets:");
                foreach (var name in bundle.GetAllAssetNames())
                    _log?.LogError($"  - {name}");
                return null;
            }

            _prefabCache[cacheKey] = prefab;
            _log?.LogInfo($"[SkinnedMeshHijack] Cached prefab '{prefabName}' from '{System.IO.Path.GetFileName(bundlePath)}'");
            return prefab;
        }
    }
}
