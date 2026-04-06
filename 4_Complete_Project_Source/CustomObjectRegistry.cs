using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Registry for spawning custom objects from AssetBundles.
    /// Unlike model overrides which replace existing meshes, this spawns NEW objects.
    /// </summary>
    public static class CustomObjectRegistry
    {
        private static ManualLogSource _log;
        private static Dictionary<string, CustomObjectInfo> _registeredObjects = new Dictionary<string, CustomObjectInfo>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<KeyCode, string> _hotkeyBindings = new Dictionary<KeyCode, string>();
        private static List<GameObject> _spawnedObjects = new List<GameObject>();
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _log?.LogInfo("[CustomObjects] Custom object spawning system initialized");
        }
        
        /// <summary>
        /// Register a custom object definition from a mod
        /// </summary>
        public static void RegisterObject(string modFolder, CustomObjectDefinition def)
        {
            if (string.IsNullOrEmpty(def.Id) || string.IsNullOrEmpty(def.Bundle) || string.IsNullOrEmpty(def.Prefab))
            {
                _log?.LogWarning($"[CustomObjects] Invalid definition: missing id, bundle, or prefab");
                return;
            }
            
            var bundlePath = Path.Combine(modFolder, def.Bundle);
            if (!File.Exists(bundlePath))
            {
                _log?.LogWarning($"[CustomObjects] Bundle not found: {bundlePath}");
                return;
            }
            
            var info = new CustomObjectInfo
            {
                Definition = def,
                BundlePath = bundlePath
            };
            
            _registeredObjects[def.Id] = info;
            _log?.LogInfo($"[CustomObjects] Registered: {def.Id} ({def.Name}) from {def.Bundle}:{def.Prefab}");
            
            // Register hotkey if specified
            if (!string.IsNullOrEmpty(def.Hotkey))
            {
                if (Enum.TryParse<KeyCode>(def.Hotkey, true, out var keyCode))
                {
                    _hotkeyBindings[keyCode] = def.Id;
                    _log?.LogInfo($"[CustomObjects] Hotkey {def.Hotkey} -> spawn {def.Id}");
                }
                else
                {
                    _log?.LogWarning($"[CustomObjects] Invalid hotkey: {def.Hotkey}");
                }
            }
        }
        
        /// <summary>
        /// Check for hotkey presses and spawn objects
        /// Call from Update loop
        /// </summary>
        public static void CheckHotkeys()
        {
            foreach (var kvp in _hotkeyBindings)
            {
                if (Input.GetKeyDown(kvp.Key))
                {
                    SpawnObject(kvp.Value);
                }
            }
        }
        
        /// <summary>
        /// Spawn a registered custom object by ID
        /// </summary>
        public static GameObject SpawnObject(string objectId)
        {
            if (!_registeredObjects.TryGetValue(objectId, out var info))
            {
                _log?.LogError($"[CustomObjects] Object not registered: {objectId}");
                return null;
            }
            
            return SpawnFromInfo(info);
        }
        
        private static GameObject SpawnFromInfo(CustomObjectInfo info)
        {
            var def = info.Definition;
            
            try
            {
                // Load bundle
                var bundle = AssetBundleLoader.LoadBundle(info.BundlePath);
                if (bundle == null)
                {
                    _log?.LogError($"[CustomObjects] Failed to load bundle for {def.Id}");
                    return null;
                }
                
                // Instantiate prefab
                var instance = AssetBundleLoader.InstantiatePrefab(bundle, def.Prefab);
                if (instance == null)
                {
                    _log?.LogError($"[CustomObjects] Failed to instantiate {def.Prefab} for {def.Id}");
                    return null;
                }
                
                // Calculate spawn position
                Vector3 spawnPos = CalculateSpawnPosition(def);
                instance.transform.position = spawnPos;
                
                // Apply scale
                if (Math.Abs(def.Scale - 1.0f) > 0.001f)
                {
                    instance.transform.localScale = Vector3.one * def.Scale;
                }
                
                // Name it
                instance.name = $"CustomObject_{def.Id}";
                
                // Ensure visibility - add default material if none exists
                EnsureVisibility(instance);
                
                // Track it
                _spawnedObjects.Add(instance);
                
                _log?.LogInfo($"[CustomObjects] Spawned {def.Id} at {spawnPos}");
                
                // Make persistent if requested
                if (def.Persistent)
                {
                    UnityEngine.Object.DontDestroyOnLoad(instance);
                }
                
                return instance;
            }
            catch (Exception ex)
            {
                _log?.LogError($"[CustomObjects] Exception spawning {def.Id}: {ex.Message}");
                return null;
            }
        }
        
        private static Vector3 CalculateSpawnPosition(CustomObjectDefinition def)
        {
            Vector3 basePos = Vector3.zero;
            Vector3 forward = Vector3.forward;
            
            // Try to find player
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                basePos = player.transform.position;
                forward = player.transform.forward;
            }
            else
            {
                // Fallback to camera
                var cam = Camera.main;
                if (cam != null)
                {
                    basePos = cam.transform.position;
                    forward = cam.transform.forward;
                }
            }
            
            Vector3 offset = new Vector3(def.OffsetX, def.OffsetY, def.OffsetZ);
            
            switch (def.SpawnAt.ToLowerInvariant())
            {
                case "player":
                    // Spawn in front of player
                    if (player != null)
                    {
                        return basePos + forward * def.OffsetZ + Vector3.up * def.OffsetY + player.transform.right * def.OffsetX;
                    }
                    return basePos + forward * def.OffsetZ + Vector3.up * def.OffsetY;
                    
                case "world":
                    // Spawn at specific world coordinates (offset IS the position)
                    return offset;
                    
                case "camera":
                    // Spawn in front of camera
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        return cam.transform.position + cam.transform.forward * def.OffsetZ + Vector3.up * def.OffsetY;
                    }
                    return basePos + offset;
                    
                default:
                    return basePos + forward * 2f + offset;
            }
        }
        
        /// <summary>
        /// Ensure the spawned object has visible renderers.
        /// Uses ShaderRemapper to fix broken bundle shaders automatically.
        /// </summary>
        private static void EnsureVisibility(GameObject obj)
        {
            // FIRST: Try to fix existing materials using ShaderRemapper
            // This preserves textures but swaps broken shaders with working ones
            int fixedCount = ShaderRemapper.FixMaterials(obj);
            
            Material defaultMat = null; // Lazy create only if needed for MISSING materials
            int addedCount = 0;
            
            // Check all MeshFilters - add MeshRenderer if missing
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                var mr = mf.gameObject.GetComponent<MeshRenderer>();
                if (mr == null)
                {
                    mr = mf.gameObject.AddComponent<MeshRenderer>();
                    _log?.LogInfo($"[CustomObjects] Added MeshRenderer to {mf.gameObject.name}");
                }
                
                // If material is STILL missing or broken after remapper tried, force default
                if (mr.sharedMaterial == null || mr.sharedMaterial.shader == null)
                {
                    if (defaultMat == null) defaultMat = CreateDefaultMaterial();
                    mr.material = defaultMat;
                    addedCount++;
                }
            }
            
            // Also check SkinnedMeshRenderers
            var smrs = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in smrs)
            {
                if (smr.sharedMaterial == null || smr.sharedMaterial.shader == null)
                {
                    if (defaultMat == null) defaultMat = CreateDefaultMaterial();
                    smr.material = defaultMat;
                    addedCount++;
                }
            }
            
            if (addedCount > 0)
            {
                _log?.LogInfo($"[CustomObjects] Applied default fallback material to {addedCount} renderer(s) with missing materials");
            }
        }

        
        /// <summary>
        /// Create a default visible material by stealing the shader from the Player character.
        /// This guarantees we use a valid, opaque, shadow-receiving shader.
        /// </summary>
        private static Material CreateDefaultMaterial()
        {
            Shader validShader = null;
            string sourceName = "None";

            // Strategy 1: Find the Player (Majsner)
            var player = GameObject.Find("SHMajsner_LOD0");
            if (player != null)
            {
                var smr = player.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMaterial != null)
                {
                    validShader = smr.sharedMaterial.shader;
                    sourceName = "Player(Majsner)";
                }
            }

            // Strategy 2: Find ANY SkinnedMeshRenderer (usually NPCs/Characters)
            if (validShader == null)
            {
                var smrs = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
                foreach (var smr in smrs)
                {
                    if (smr.sharedMaterial != null && smr.sharedMaterial.shader != null)
                    {
                        validShader = smr.sharedMaterial.shader;
                        sourceName = $"NPC({smr.gameObject.name})";
                        break;
                    }
                }
            }

            // Strategy 3: Find any Opaque MeshRenderer (avoid particles)
            if (validShader == null)
            {
                var mrs = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                foreach (var mr in mrs)
                {
                    if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null)
                    {
                        // Filter out likely particles or UI
                        string sName = mr.sharedMaterial.shader.name.ToLower();
                        if (!sName.Contains("particle") && !sName.Contains("ui") && !sName.Contains("skybox"))
                        {
                            validShader = mr.sharedMaterial.shader;
                            sourceName = $"Prop({mr.gameObject.name})";
                            break;
                        }
                    }
                }
            }

            // Fallback
            if (validShader == null)
            {
                _log?.LogWarning("[CustomObjects] Could not find ANY valid shader in scene. Trying Standard.");
                validShader = Shader.Find("Standard");
            }

            if (validShader == null) 
            {
                return new Material(Shader.Find("Hidden/InternalErrorShader")); // Pink failure
            }

            _log?.LogInfo($"[CustomObjects] Stealing shader '{validShader.name}' from {sourceName}");
            
            var mat = new Material(validShader);
            mat.name = "HoboMod_StealedMaterial";
            
            // Try setting color properties
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(1f, 0f, 1f));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0f, 1f));
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            
            return mat;
        }
        
        /// <summary>
        /// Get list of all registered object IDs
        /// </summary>
        public static IEnumerable<string> GetRegisteredObjectIds()
        {
            return _registeredObjects.Keys;
        }
        
        /// <summary>
        /// Remove all spawned objects
        /// </summary>
        public static void ClearSpawnedObjects()
        {
            foreach (var obj in _spawnedObjects)
            {
                if (obj != null)
                {
                    UnityEngine.Object.Destroy(obj);
                }
            }
            _spawnedObjects.Clear();
            _log?.LogInfo("[CustomObjects] Cleared all spawned objects");
        }
        
        /// <summary>
        /// Get count of spawned objects
        /// </summary>
        public static int SpawnedCount => _spawnedObjects.Count;
        
        private class CustomObjectInfo
        {
            public CustomObjectDefinition Definition { get; set; }
            public string BundlePath { get; set; }
        }
    }
}
