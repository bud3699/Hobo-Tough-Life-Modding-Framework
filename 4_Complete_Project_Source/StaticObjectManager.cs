using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Manages static object placements loaded from mod JSON files.
    /// Objects are spawned automatically when their target scene loads.
    /// </summary>
    public class StaticObjectManager
    {
        private static ManualLogSource _log;
        private static List<ScenePlacement> _allPlacements = new List<ScenePlacement>();
        private static List<GameObject> _spawnedObjects = new List<GameObject>();
        private static bool _initialized = false;

        // ============================================================
        // DATA STRUCTURES
        // ============================================================

        /// <summary>
        /// Root structure for a placements JSON file.
        /// </summary>
        [Serializable]
        public class PlacementsFile
        {
            public List<ScenePlacement> Placements { get; set; } = new List<ScenePlacement>();
        }

        /// <summary>
        /// A group of objects for a specific scene.
        /// </summary>
        [Serializable]
        public class ScenePlacement
        {
            public string SceneName { get; set; }
            public List<StaticObject> Objects { get; set; } = new List<StaticObject>();
        }

        /// <summary>
        /// A flexible Vector3 that can be deserialized from either:
        /// - Array format: [x, y, z]
        /// - Object format: {"x": 1.0, "y": 2.0, "z": 3.0}
        /// </summary>
        [Serializable]
        [JsonConverter(typeof(FlexibleVectorConverter))]
        public struct SerializableVector3
        {
            public float x { get; set; }
            public float y { get; set; }
            public float z { get; set; }

            public Vector3 ToUnity() => new Vector3(x, y, z);
            public static SerializableVector3 FromUnity(Vector3 v) => new SerializableVector3 { x = v.x, y = v.y, z = v.z };
        }

        /// <summary>
        /// JsonConverter that handles both array [x,y,z] and object {x,y,z} formats.
        /// </summary>
        public class FlexibleVectorConverter : JsonConverter<SerializableVector3>
        {
            public override SerializableVector3 ReadJson(JsonReader reader, Type objectType, SerializableVector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.StartArray)
                {
                    // Array format: [x, y, z]
                    var arr = serializer.Deserialize<float[]>(reader);
                    if (arr != null && arr.Length >= 3)
                        return new SerializableVector3 { x = arr[0], y = arr[1], z = arr[2] };
                    return default;
                }
                else if (reader.TokenType == JsonToken.StartObject)
                {
                    // Object format: {x, y, z}
                    var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
                    return new SerializableVector3
                    {
                        x = (float?)obj["x"] ?? 0f,
                        y = (float?)obj["y"] ?? 0f,
                        z = (float?)obj["z"] ?? 0f
                    };
                }
                return default;
            }

            public override void WriteJson(JsonWriter writer, SerializableVector3 value, JsonSerializer serializer)
            {
                // Always write as object format (the modern standard)
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WriteEndObject();
            }
        }

        /// <summary>
        /// Definition of a single static object to spawn.
        /// </summary>
        [Serializable]
        public class StaticObject
        {
            public string AssetId { get; set; }                    // "bundle:asset" or "primitive:Cube"
            public SerializableVector3? Position { get; set; }     // Supports both [x,y,z] and {x,y,z}
            public SerializableVector3? Rotation { get; set; }     // Euler angles
            public SerializableVector3? Scale { get; set; }        // Defaults to (1,1,1)
            public string Name { get; set; }                       // Optional display name
        }

        // ============================================================
        // INITIALIZATION
        // ============================================================

        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _log?.LogInfo("[StaticObjectManager] Initialized");
            
            // Hook into scene loading (IL2CPP requires explicit Action wrapper)
            SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
            _initialized = true;
        }

        // ============================================================
        // LOADING FROM MODS
        // ============================================================

        /// <summary>
        /// Load placement files from a mod folder.
        /// Called by ModLoader during discovery.
        /// </summary>
        public static void LoadPlacementsFromMod(string modFolderPath, string modId)
        {
            var files = Directory.GetFiles(modFolderPath, "*.placements.json", SearchOption.TopDirectoryOnly);
            
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonConvert.DeserializeObject<PlacementsFile>(json);
                    
                    if (data?.Placements != null)
                    {
                        foreach (var placement in data.Placements)
                        {
                            _allPlacements.Add(placement);
                            _log?.LogInfo($"  Loaded {placement.Objects.Count} object(s) for scene '{placement.SceneName}' from {Path.GetFileName(file)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[StaticObjectManager] Failed to load {file}: {ex.Message}");
                }
            }
        }

        // ============================================================
        // SCENE HANDLING
        // ============================================================

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_initialized) return;
            
            _log?.LogInfo($"[StaticObjectManager] Scene loaded: {scene.name}");
            
            // Find placements for this scene
            foreach (var placement in _allPlacements)
            {
                if (string.Equals(placement.SceneName, scene.name, StringComparison.OrdinalIgnoreCase))
                {
                    SpawnPlacement(placement);
                }
            }
        }

        private static void SpawnPlacement(ScenePlacement placement)
        {
            foreach (var obj in placement.Objects)
            {
                try
                {
                    SpawnObject(obj);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[StaticObjectManager] Failed to spawn '{obj.AssetId}': {ex.Message}");
                }
            }
        }

        private static void SpawnObject(StaticObject def)
        {
            GameObject spawned = null;
            
            // Parse asset ID
            if (def.AssetId.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase))
            {
                // Built-in primitive (for testing)
                var primType = def.AssetId.Substring("primitive:".Length);
                if (Enum.TryParse<PrimitiveType>(primType, true, out var prim))
                {
                    spawned = GameObject.CreatePrimitive(prim);
                }
            }
            else if (def.AssetId.Contains(":"))
            {
                // Bundle:Asset format
                var parts = def.AssetId.Split(':');
                if (parts.Length == 2)
                {
                    var bundlePath = parts[0];
                    var assetName = parts[1];
                    var bundle = AssetBundleLoader.LoadBundle(bundlePath);
                    if (bundle != null)
                    {
                        spawned = AssetBundleLoader.InstantiatePrefab(bundle, assetName);
                    }
                }
            }
            
            if (spawned == null)
            {
                _log?.LogWarning($"[StaticObjectManager] Could not create object for '{def.AssetId}'");
                return;
            }
            
            // Apply transform
            var pos = def.Position.HasValue ? def.Position.Value.ToUnity() : Vector3.zero;
            var rot = def.Rotation.HasValue 
                ? Quaternion.Euler(def.Rotation.Value.ToUnity()) 
                : Quaternion.identity;
            var scale = def.Scale.HasValue ? def.Scale.Value.ToUnity() : Vector3.one;
            
            spawned.transform.position = pos;
            spawned.transform.rotation = rot;
            spawned.transform.localScale = scale;
            
            // Name it
            spawned.name = !string.IsNullOrEmpty(def.Name) ? def.Name : $"Static_{def.AssetId}";
            
            // Fix shaders if from bundle
            if (def.AssetId.Contains(":") && !def.AssetId.StartsWith("primitive:"))
            {
                ShaderRemapper.FixMaterials(spawned);
            }
            
            _spawnedObjects.Add(spawned);
            _log?.LogInfo($"[StaticObjectManager] Spawned '{spawned.name}' at {pos}");
        }

        // ============================================================
        // CLEANUP
        // ============================================================

        public static void Clear()
        {
            foreach (var obj in _spawnedObjects)
            {
                if (obj != null)
                {
                    UnityEngine.Object.Destroy(obj);
                }
            }
            _spawnedObjects.Clear();
            _allPlacements.Clear();
            _log?.LogInfo("[StaticObjectManager] Cleared all static placements");
        }
    }
}
