using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// The Universal Rosetta Stone BoneRemapper.
    ///
    /// TRANSLATION PIPELINE (runs once per SMR per NPC spawn):
    ///   Pass 1 — Avatar Rosetta Stone:
    ///     For each custom bone, ask the custom Animator's Avatar which HumanBodyBones
    ///     identity that bone corresponds to. Then look up the Vanilla bone string name
    ///     from HoboBoneDictionary and slot in the live Vanilla bone.
    ///
    ///   Pass 2 — String Match Bypass (fallback):
    ///     If the Avatar bake failed (e.g. modular mesh, too few bones to qualify as
    ///     Humanoid), directly compare the custom bone name against every bone in the
    ///     Vanilla index. If the name matches, slot in that Vanilla bone.
    ///     This handles models that deliberately use Vanilla bone names, or partial rigs.
    ///
    ///   Pass 3 — Last resort:
    ///     If neither pass finds a match (cape bones, hair bones etc.), the original
    ///     INSTANTIATED custom bone is kept. This requires the caller to instantiate
    ///     the custom skeleton into the scene before calling this method.
    ///
    /// PERFORMANCE: O(n) algorithm. Runs exactly ONCE per SMR per NPC spawn. Zero per-
    ///              frame cost after the array is set on the SkinnedMeshRenderer.
    /// </summary>
    public static class BoneRemapper
    {
        /// <summary>
        /// Builds a correctly-indexed Transform array for a custom SMR piece by remapping
        /// every custom bone to its equivalent active Vanilla Hobo bone.
        /// </summary>
        /// <param name="vanillaSmr">The live, animated native Vanilla SMR on the NPC.</param>
        /// <param name="customAnimator">
        ///     The Animator from the instantiated custom prefab. Used to query the baked
        ///     Avatar for bone-to-HumanBodyBones translation. May be null.
        /// </param>
        /// <param name="instancedCustomBones">
        ///     The bones array from the INSTANTIATED custom SMR piece (transforms that exist
        ///     in the scene, not in an un-loaded AssetBundle). Used as last-resort fallback.
        /// </param>
        /// <param name="log">Logger for diagnostic output.</param>
        /// <returns>
        ///     A new Transform[] parallel to instancedCustomBones, containing live Vanilla
        ///     bone Transforms wherever a mapping was found, or the original instantiated
        ///     bone as a last-resort fallback.
        /// </returns>
        public static Transform[] CreateRemappedArray(
            SkinnedMeshRenderer vanillaSmr,
            Animator             customAnimator,
            Transform[]          instancedCustomBones,
            ManualLogSource      log)
        {
            // ── Guard ─────────────────────────────────────────────────────────────
            if (vanillaSmr == null)
            {
                log?.LogError("[BoneRemapper] vanillaSmr is null — cannot remap.");
                return instancedCustomBones ?? Array.Empty<Transform>();
            }
            if (instancedCustomBones == null || instancedCustomBones.Length == 0)
            {
                log?.LogError("[BoneRemapper] instancedCustomBones is null/empty — nothing to remap.");
                return Array.Empty<Transform>();
            }

            // ── Step 1: Build the Vanilla bone index (name → Transform) ───────────
            var vanillaByName = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            BuildNameIndex(vanillaSmr.transform.root, vanillaByName);
            if (vanillaSmr.bones != null)
            {
                foreach (var b in vanillaSmr.bones)
                    if (b != null && !vanillaByName.ContainsKey(b.name))
                        vanillaByName[b.name] = b;
            }
            log?.LogInfo($"[BoneRemapper] Vanilla bone index: {vanillaByName.Count} entries.");

            // ── Step 2: Build Avatar reverse dictionary (Transform → HumanBodyBones) ─
            var customBoneToHumanBone = new Dictionary<Transform, HumanBodyBones>();
            if (customAnimator != null && customAnimator.avatar != null && customAnimator.avatar.isHuman)
            {
                foreach (HumanBodyBones humanBone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (humanBone == HumanBodyBones.LastBone) continue;
                    try
                    {
                        var t = customAnimator.GetBoneTransform(humanBone);
                        if (t != null) customBoneToHumanBone[t] = humanBone;
                    }
                    catch { /* Optional bones may throw; skip silently */ }
                }
            }
            log?.LogInfo($"[BoneRemapper] Avatar resolved {customBoneToHumanBone.Count} human bone mappings.");

            // ── Step 3: Build the remapped array ──────────────────────────────────
            var remapped     = new Transform[instancedCustomBones.Length];
            int avatarMapped = 0;
            int nameMapped   = 0;
            int fallback     = 0;

            for (int i = 0; i < instancedCustomBones.Length; i++)
            {
                var customBone = instancedCustomBones[i];
                Transform resolved = null;

                // Pass 1 — Avatar Rosetta Stone
                if (customBone != null &&
                    customBoneToHumanBone.TryGetValue(customBone, out var humanBone) &&
                    HoboBoneDictionary.BoneNameMap.TryGetValue(humanBone, out var vanillaName) &&
                    vanillaByName.TryGetValue(vanillaName, out var vanillaBone))
                {
                    resolved = vanillaBone;
                    avatarMapped++;
                }

                // Pass 2 — String-Match Bypass (for failed Avatars / modular meshes)
                if (resolved == null && customBone != null)
                {
                    if (vanillaByName.TryGetValue(customBone.name, out var directMatch))
                    {
                        resolved = directMatch;
                        nameMapped++;
                    }
                }

                // Pass 3 — Keep the instantiated custom bone (capes, hair, extras)
                if (resolved == null)
                {
                    resolved = customBone;
                    fallback++;
                    if (customBone != null)
                        log?.LogInfo($"[BoneRemapper]   Fallback [{i}] '{customBone?.name}' — no Vanilla equivalent.");
                }

                remapped[i] = resolved;
            }

            log?.LogInfo($"[BoneRemapper] Done: {avatarMapped} avatar-mapped, {nameMapped} name-matched, {fallback} fallback.");
            return remapped;
        }

        /// <summary>
        /// Recursively indexes all transforms in a hierarchy by their name.
        /// </summary>
        private static void BuildNameIndex(Transform root, Dictionary<string, Transform> index)
        {
            if (root == null) return;
            if (!index.ContainsKey(root.name))
                index[root.name] = root;
            for (int i = 0; i < root.childCount; i++)
                BuildNameIndex(root.GetChild(i), index);
        }
    }
}
