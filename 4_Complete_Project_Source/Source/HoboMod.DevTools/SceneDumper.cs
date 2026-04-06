using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using Newtonsoft.Json;

namespace HoboMod.DevTools
{
    public static class SceneDumper
    {
        private static ManualLogSource _log;
        private static string _outputPath;
        
        public static void Initialize(ManualLogSource log, string rootPath)
        {
            _log = log;
            _outputPath = Path.Combine(rootPath, "SceneDumps");
            _log?.LogInfo($"[SceneDumper] Initialized. Output: {_outputPath}");
        }

        public static void DumpScene(bool fullDump = false)
        {
            try
            {
                _log?.LogInfo("[SceneDumper] Starting JSON Scene Dump...");

                var rootNodes = new List<SceneNode>();
                
                // Get all root objects in the scene
                // We use GetRootGameObjects from the active scene
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var roots = scene.GetRootGameObjects();

                foreach (var root in roots)
                {
                    // Optional: Filter out heavy systems or managers if needed
                    // For reconstruction, we generally want everything visual
                    if (root.name.Contains("BepInEx") || root.name.Contains("Steam")) continue;

                    var node = ProcessNode(root.transform);
                    if (node != null)
                    {
                        rootNodes.Add(node);
                    }
                }

                var dump = new SceneDump
                {
                    SceneName = scene.name,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Roots = rootNodes
                };

                // Serialize
                var json = JsonConvert.SerializeObject(dump, Formatting.Indented);

                // Save
                if (!Directory.Exists(_outputPath)) Directory.CreateDirectory(_outputPath);
                string filename = $"LevelDump_{scene.name}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string path = Path.Combine(_outputPath, filename);
                
                File.WriteAllText(path, json);
                _log?.LogInfo($"[SceneDumper] Dump saved: {filename} ({dump.Roots.Count} roots)");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[SceneDumper] Dump failed: {ex}");
            }
        }

        private static SceneNode ProcessNode(Transform t)
        {
            if (t == null) return null;
            if (!t.gameObject.activeInHierarchy) return null; // Skip inactive? User might want them. Let's keep Active only for now to reduce clutter.

            var node = new SceneNode
            {
                Name = t.name,
                Pos = t.position,      // World position for roots? No, let's use Local for reconstruction hierarchy
                Rot = t.rotation,      // Unity serializes Quaternion nicely usually, but we might want Euler or Array
                Scale = t.localScale
            };

            // Get Mesh info if available (helps identification)
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                node.MeshName = mf.sharedMesh.name;
            }

            // Get Material info
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterials != null)
            {
                node.Materials = new List<string>();
                foreach (var mat in mr.sharedMaterials)
                {
                    if (mat != null) node.Materials.Add(mat.name);
                }
            }

            // Recursion
            if (t.childCount > 0)
            {
                node.Children = new List<SceneNode>();
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = ProcessNode(t.GetChild(i));
                    if (child != null)
                    {
                        node.Children.Add(child);
                    }
                }
            }

            return node;
        }

        // --- Data Structures ---

        [Serializable]
        public class SceneDump
        {
            public string SceneName;
            public string Timestamp;
            public List<SceneNode> Roots;
        }

        [Serializable]
        public class SceneNode
        {
            public string Name;
            [JsonConverter(typeof(Vector3Converter))]
            public Vector3 Pos;
            [JsonConverter(typeof(QuaternionConverter))]
            public Quaternion Rot;
            [JsonConverter(typeof(Vector3Converter))]
            public Vector3 Scale;
            
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string MeshName;
            
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public List<string> Materials;
            
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public List<SceneNode> Children;
        }
        
        // Simple Converters to ensure clean JSON output
        public class Vector3Converter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WriteEndObject();
            }

            public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException(); // We only read in Editor, not here
            }
        }

        public class QuaternionConverter : JsonConverter<Quaternion>
        {
            public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WritePropertyName("w"); writer.WriteValue(value.w);
                writer.WriteEndObject();
            }

            public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }
    }
}
