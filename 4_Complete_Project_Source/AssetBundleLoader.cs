using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Loads Unity AssetBundles and extracts assets for model replacement.
    /// AssetBundles provide full Unity-native asset support including:
    /// - Meshes with bone weights and bindposes
    /// - Materials with correct shaders
    /// - Complete prefabs with components
    /// </summary>
    public static class AssetBundleLoader
    {
        private static ManualLogSource _log;
        private static Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _log?.LogInfo("[AssetBundleLoader] Initialized");
        }
        
        /// <summary>
        /// Load an AssetBundle from file. Caches loaded bundles.
        /// </summary>
        public static AssetBundle LoadBundle(string bundlePath)
        {
            if (!File.Exists(bundlePath))
            {
                _log?.LogError($"[AssetBundleLoader] Bundle not found: {bundlePath}");
                return null;
            }
            
            // Check cache first
            string key = bundlePath.ToLowerInvariant();
            if (_loadedBundles.TryGetValue(key, out var cached))
            {
                _log?.LogInfo($"[AssetBundleLoader] Using cached bundle: {Path.GetFileName(bundlePath)}");
                return cached;
            }
            
            try
            {
                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    _log?.LogError($"[AssetBundleLoader] Failed to load bundle: {bundlePath}");
                    return null;
                }
                
                _loadedBundles[key] = bundle;
                _log?.LogInfo($"[AssetBundleLoader] Loaded bundle: {Path.GetFileName(bundlePath)}");
                
                // Log available assets
                var assetNames = bundle.GetAllAssetNames();
                _log?.LogInfo($"[AssetBundleLoader] Bundle contains {assetNames.Length} assets:");
                foreach (var name in assetNames)
                {
                    _log?.LogInfo($"  - {name}");
                }
                
                return bundle;
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetBundleLoader] Exception loading bundle: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Extract a Mesh from an AssetBundle
        /// </summary>
        public static Mesh GetMesh(AssetBundle bundle, string assetName)
        {
            if (bundle == null)
            {
                _log?.LogError("[AssetBundleLoader] Cannot get mesh: bundle is null");
                return null;
            }
            
            try
            {
                var mesh = bundle.LoadAsset<Mesh>(assetName);
                if (mesh == null)
                {
                    _log?.LogWarning($"[AssetBundleLoader] Mesh '{assetName}' not found, trying GameObject...");
                    
                    // Try loading as GameObject and extracting mesh
                    var go = bundle.LoadAsset<GameObject>(assetName);
                    if (go != null)
                    {
                        var mf = go.GetComponent<MeshFilter>();
                        if (mf != null) mesh = mf.sharedMesh;
                        
                        var smr = go.GetComponent<SkinnedMeshRenderer>();
                        if (smr != null) mesh = smr.sharedMesh;
                    }
                }
                
                if (mesh != null)
                {
                    _log?.LogInfo($"[AssetBundleLoader] Loaded mesh: {assetName} ({mesh.vertexCount} verts)");
                }
                else
                {
                    _log?.LogError($"[AssetBundleLoader] Could not find mesh: {assetName}");
                }
                
                return mesh;
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetBundleLoader] Exception getting mesh: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Extract a complete SkinnedMeshRenderer with bones, materials, etc.
        /// Returns the GameObject containing the SMR so caller can copy all data.
        /// </summary>
        public static GameObject GetSkinnedMeshObject(AssetBundle bundle, string assetName)
        {
            if (bundle == null)
            {
                _log?.LogError("[AssetBundleLoader] Cannot get skinned mesh: bundle is null");
                return null;
            }
            
            try
            {
                var go = bundle.LoadAsset<GameObject>(assetName);
                if (go == null)
                {
                    _log?.LogError($"[AssetBundleLoader] GameObject '{assetName}' not found in bundle");
                    return null;
                }
                
                // Check for SkinnedMeshRenderer
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr == null)
                {
                    _log?.LogWarning($"[AssetBundleLoader] GameObject '{assetName}' has no SkinnedMeshRenderer");
                }
                else
                {
                    _log?.LogInfo($"[AssetBundleLoader] Loaded skinned mesh: {assetName}");
                    _log?.LogInfo($"  Mesh: {smr.sharedMesh?.name ?? "null"} ({smr.sharedMesh?.vertexCount ?? 0} verts)");
                    _log?.LogInfo($"  Bones: {smr.bones?.Length ?? 0}");
                    _log?.LogInfo($"  Materials: {smr.sharedMaterials?.Length ?? 0}");
                }
                
                return go;
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetBundleLoader] Exception getting skinned mesh: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Load and instantiate a prefab from an AssetBundle.
        /// Returns the instantiated GameObject.
        /// </summary>
        public static GameObject InstantiatePrefab(AssetBundle bundle, string assetName)
        {
            if (bundle == null)
            {
                _log?.LogError("[AssetBundleLoader] Cannot instantiate prefab: bundle is null");
                return null;
            }
            
            try
            {
                var prefab = bundle.LoadAsset<GameObject>(assetName);
                if (prefab == null)
                {
                    _log?.LogError($"[AssetBundleLoader] Prefab '{assetName}' not found in bundle");
                    return null;
                }
                
                var instance = GameObject.Instantiate(prefab);
                instance.name = assetName; // Remove "(Clone)" suffix
                
                _log?.LogInfo($"[AssetBundleLoader] Instantiated prefab: {assetName}");
                
                // Log component info
                var components = instance.GetComponentsInChildren<Component>();
                _log?.LogInfo($"  Components: {components.Length}");
                
                return instance;
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetBundleLoader] Exception instantiating prefab: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Unload an AssetBundle
        /// </summary>
        public static void UnloadBundle(AssetBundle bundle, bool unloadAllLoadedObjects = false)
        {
            if (bundle == null) return;
            
            try
            {
                // Remove from cache
                string keyToRemove = null;
                foreach (var kvp in _loadedBundles)
                {
                    if (kvp.Value == bundle)
                    {
                        keyToRemove = kvp.Key;
                        break;
                    }
                }
                
                if (keyToRemove != null)
                    _loadedBundles.Remove(keyToRemove);
                
                bundle.Unload(unloadAllLoadedObjects);
                _log?.LogInfo($"[AssetBundleLoader] Unloaded bundle (unloadAssets={unloadAllLoadedObjects})");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetBundleLoader] Exception unloading bundle: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Unload all cached bundles
        /// </summary>
        public static void UnloadAllBundles(bool unloadAllLoadedObjects = false)
        {
            foreach (var bundle in _loadedBundles.Values)
            {
                try
                {
                    bundle?.Unload(unloadAllLoadedObjects);
                }
                catch { }
            }
            _loadedBundles.Clear();
            _log?.LogInfo("[AssetBundleLoader] Unloaded all bundles");
        }
    }
}
