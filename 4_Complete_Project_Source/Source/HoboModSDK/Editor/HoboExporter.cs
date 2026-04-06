using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;

/// <summary>
/// Exports selected Unity objects to a format compatible with StaticObjectManager.
/// Output can be placed directly in a mod's *.placements.json file.
/// </summary>
public class HoboExporter : EditorWindow
{
    private string _exportPath = "mod_placements.json";
    private string _sceneName = "";
    private bool _exportSelectionOnly = true;
    
    [MenuItem("HoboMod/Scene Exporter")]
    public static void ShowWindow()
    {
        GetWindow<HoboExporter>("Hobo Exporter");
    }

    private void OnEnable()
    {
        // Default scene name to current scene
        _sceneName = SceneManager.GetActiveScene().name;
    }

    private void OnGUI()
    {
        GUILayout.Label("Export Placements to Mod", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Exports selected objects to a *.placements.json file compatible with StaticObjectManager.\n" +
            "Place the output in your mod folder and rename to something.placements.json", 
            MessageType.Info);
        
        EditorGUILayout.Space();
        _sceneName = EditorGUILayout.TextField("Target Scene Name", _sceneName);
        EditorGUILayout.HelpBox("This is the in-game scene where objects will spawn.", MessageType.None);
        
        EditorGUILayout.Space();
        _exportSelectionOnly = EditorGUILayout.Toggle("Export Selection Only", _exportSelectionOnly);
        
        EditorGUILayout.Space();
        GUILayout.Label("Output File:");
        EditorGUILayout.BeginHorizontal();
        _exportPath = EditorGUILayout.TextField(_exportPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.SaveFilePanel("Save Placements", "", "mod_placements", "json");
            if (!string.IsNullOrEmpty(path)) _exportPath = path;
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        GUI.enabled = Selection.gameObjects.Length > 0 || !_exportSelectionOnly;
        if (GUILayout.Button("Export!", GUILayout.Height(30)))
        {
            ExportPlacements();
        }
        GUI.enabled = true;
        
        if (Selection.gameObjects.Length > 0)
        {
            EditorGUILayout.LabelField($"Selected: {Selection.gameObjects.Length} object(s)");
        }
    }

    private void ExportPlacements()
    {
        GameObject[] targets;
        if (_exportSelectionOnly)
        {
            targets = Selection.gameObjects;
        }
        else
        {
            // Get all root objects in scene
            targets = SceneManager.GetActiveScene().GetRootGameObjects();
        }

        if (targets.Length == 0)
        {
            Debug.LogWarning("[HoboExporter] No objects to export!");
            return;
        }

        var objects = new List<StaticObject>();
        
        foreach (var go in targets)
        {
            // Skip ignored objects
            if (go.tag == "EditorOnly") continue;
            if (go.name.StartsWith("[MISSING")) continue;
            
            var obj = new StaticObject();
            
            // 1. Identify Asset ID
            // Priority: prefab path > mesh name > object name
            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            
            if (!string.IsNullOrEmpty(prefabPath))
            {
                // Has prefab - use bundle:asset format or just asset name
                obj.AssetId = Path.GetFileNameWithoutExtension(prefabPath);
            }
            else
            {
                // No prefab - try mesh name
                var meshFilter = go.GetComponent<MeshFilter>();
                var skinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
                
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    obj.AssetId = meshFilter.sharedMesh.name;
                }
                else if (skinnedMesh != null && skinnedMesh.sharedMesh != null)
                {
                    obj.AssetId = skinnedMesh.sharedMesh.name;
                }
                else
                {
                    // Fallback to cleaned object name
                    obj.AssetId = CleanName(go.name);
                }
            }
            
            // 2. Transform (WORLD coordinates)
            var pos = go.transform.position;
            var rot = go.transform.rotation.eulerAngles;
            var scale = go.transform.localScale;
            
            // Use SerializableVector3 format {x, y, z} to match StaticObjectManager
            obj.Position = new Vec3 { x = pos.x, y = pos.y, z = pos.z };
            obj.Rotation = new Vec3 { x = rot.x, y = rot.y, z = rot.z };
            obj.Scale = new Vec3 { x = scale.x, y = scale.y, z = scale.z };
            
            // 3. Optional name
            obj.Name = go.name;
            
            objects.Add(obj);
        }

        // Build the correct structure for StaticObjectManager
        var scenePlacement = new ScenePlacement
        {
            SceneName = _sceneName,
            Objects = objects
        };

        var root = new PlacementsFile
        {
            Placements = new List<ScenePlacement> { scenePlacement }
        };
        
        // Serialize with proper formatting
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };
        
        string json = JsonConvert.SerializeObject(root, settings);
        File.WriteAllText(_exportPath, json);
        
        Debug.Log($"[HoboExporter] Exported {objects.Count} objects for scene '{_sceneName}' to {_exportPath}");
        EditorUtility.RevealInFinder(_exportPath);
    }
    
    private string CleanName(string name)
    {
        string clean = name;
        if (clean.Contains("(Clone)")) clean = clean.Substring(0, clean.IndexOf("(Clone)"));
        return clean.Trim();
    }

    // ========================================================
    // Data Structures - MUST MATCH StaticObjectManager.cs
    // ========================================================
    
    [System.Serializable]
    public class PlacementsFile
    {
        public List<ScenePlacement> Placements { get; set; }
    }

    [System.Serializable]
    public class ScenePlacement
    {
        public string SceneName { get; set; }
        public List<StaticObject> Objects { get; set; }
    }

    [System.Serializable]
    public class StaticObject
    {
        public string AssetId { get; set; }
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        public Vec3 Scale { get; set; }
        public string Name { get; set; }
    }

    [System.Serializable]
    public class Vec3
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
    }
}
