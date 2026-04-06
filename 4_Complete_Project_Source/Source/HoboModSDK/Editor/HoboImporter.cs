using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json; // Requires Newtonsoft.Json package

public class HoboImporter : EditorWindow
{
    private string _jsonPath = "Select LevelDump.json";
    private string _assetRoot = "Assets/HoboData"; // Default path where user put AssetRipper output
    
    // Index: CleanName -> List of AssetPaths
    // We use a list because collisions might happen (e.g. Models vs Prefabs), we prefer Prefabs.
    private Dictionary<string, List<string>> _assetIndex = new Dictionary<string, List<string>>();
    
    private Vector2 _scrollPos;
    private bool _isIndexed = false;
    private string _log = "";

    [MenuItem("HoboMod/Scene Importer")]
    public static void ShowWindow()
    {
        GetWindow<HoboImporter>("Hobo Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("HoboMod Scene Reconstruction", EditorStyles.boldLabel);
        
        // 1. Asset Indexing
        EditorGUILayout.Space();
        GUILayout.Label("1. Asset Indexing", EditorStyles.label);
        _assetRoot = EditorGUILayout.TextField("Asset Root Folder", _assetRoot);
        
        if (GUILayout.Button("Index Assets (Recursive)"))
        {
            IndexAssets();
        }
        
        if (_isIndexed)
        {
            EditorGUILayout.HelpBox($"Index Ready: {_assetIndex.Count} unique names found.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Please index assets first.", MessageType.Warning);
        }

        // 2. JSON Selection
        EditorGUILayout.Space();
        GUILayout.Label("2. Layout Parsing", EditorStyles.label);
        
        EditorGUILayout.BeginHorizontal();
        _jsonPath = EditorGUILayout.TextField(_jsonPath);
        if (GUILayout.Button("Browse", GUILayout.Width(75)))
        {
            string path = EditorUtility.OpenFilePanel("Select Level Dump", "", "json");
            if (!string.IsNullOrEmpty(path)) _jsonPath = path;
        }
        EditorGUILayout.EndHorizontal();

        // 3. Reconstruction
        EditorGUILayout.Space();
        GUILayout.Label("3. Execution", EditorStyles.label);
        
        if (GUILayout.Button("Reconstruct Scene"))
        {
            if (!_isIndexed) 
            {
                Debug.LogError("Index assets first!");
            }
            else if (!File.Exists(_jsonPath))
            {
                Debug.LogError("JSON file not found!");
            }
            else
            {
                ReconstructScene();
            }
        }
        
        // Utilities
        EditorGUILayout.Space();
        if (GUILayout.Button("Fix Pink Shaders (Set to Standard)"))
        {
            FixShaders();
        }

        // Log View
        EditorGUILayout.Space();
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(200));
        GUILayout.TextArea(_log);
        EditorGUILayout.EndScrollView();
    }

    private void Log(string msg)
    {
        _log += msg + "\n";
        Debug.Log($"[HoboImporter] {msg}");
        Repaint(); // Force UI update
    }

    // ===================================================================================
    // PHASE 1: INDEXING
    // ===================================================================================

    private void IndexAssets()
    {
        _assetIndex.Clear();
        _log = "Indexing...\n";
        
        if (!_assetRoot.StartsWith("Assets"))
        {
            Log($"ERROR: Path '{_assetRoot}' is invalid. You MUST copy the AssetRipper output INTO your Unity Project's Assets folder.");
            Log("Example: Assets/HoboData");
            return;
        }

        // Search for BOTH prefabs AND models (glb/fbx)
        // t:GameObject = .prefab files
        // t:Model = .glb, .fbx, .obj files
        var prefabGuids = AssetDatabase.FindAssets("t:GameObject", new[] { _assetRoot });
        var modelGuids = AssetDatabase.FindAssets("t:Model", new[] { _assetRoot });
        
        Log($"Found {prefabGuids.Length} prefabs + {modelGuids.Length} models in {_assetRoot}. Processing...");
        
        // Combine and process all
        var allGuids = new HashSet<string>(prefabGuids);
        foreach (var g in modelGuids) allGuids.Add(g);
        
        int count = 0;
        foreach (var guid in allGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string filename = Path.GetFileNameWithoutExtension(path);
            
            // Fuzzy Matching Logic
            string cleanName = CleanName(filename);
            
            if (!_assetIndex.ContainsKey(cleanName))
            {
                _assetIndex[cleanName] = new List<string>();
            }
            _assetIndex[cleanName].Add(path);
            count++;
            
            if (count % 1000 == 0) Log($"Indexed {count}...");
        }
        
        _isIndexed = true;
        Log($"Indexing Complete. {_assetIndex.Count} unique asset names.");
    }
    
    private string CleanName(string name)
    {
        // Example: "Bench_Park_LOD0" -> "Bench_Park"
        // Example: "Bench(Clone)" -> "Bench"
        // Case insensitive key storage handled by dictionary lookup later? No, Dictionary is strict by default.
        // We will store keys as LowerCase for fuzzy matching.
        
        string clean = name;
        
        // Remove (Clone) and InstanceIDs if somehow present
        int cloneIndex = clean.IndexOf("(Clone)");
        if (cloneIndex > 0) clean = clean.Substring(0, cloneIndex);
        
        // Remove LOD suffixes
        if (clean.EndsWith("_LOD0", System.StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 5);
        if (clean.EndsWith("_LOD1", System.StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 5);
        if (clean.EndsWith("_LOD2", System.StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 5);
        
        return clean.Trim().ToLowerInvariant();
    }

    // ===================================================================================
    // PHASE 2: RECONSTRUCTION
    // ===================================================================================

    private void ReconstructScene()
    {
        Log("Starting Reconstruction...");
        
        string json = File.ReadAllText(_jsonPath);
        SceneDump dump = JsonConvert.DeserializeObject<SceneDump>(json);
        
        if (dump == null || dump.Roots == null)
        {
            Log("Failed to parse JSON.");
            return;
        }
        
        Log($"Loaded Dump: {dump.SceneName} ({dump.Timestamp}) with {dump.Roots.Count} roots.");
        
        // Create Root Parent
        GameObject worldRoot = new GameObject($"Imported_{dump.SceneName}");
        
        foreach (var node in dump.Roots)
        {
            BuildNode(node, worldRoot.transform);
        }
        
        Log("Reconstruction Complete!");
    }

    private void BuildNode(SceneNode node, Transform parent)
    {
        GameObject go = null;
        
        // Try to find matching asset
        string cleanParams = CleanName(node.Name);
        string cleanMesh = !string.IsNullOrEmpty(node.MeshName) ? CleanName(node.MeshName) : "";
        string assetPath = null;
        
        // Strategy 1: Match by Node Name (e.g. "Bench(Clone)" -> "bench")
        if (_assetIndex.ContainsKey(cleanParams))
        {
            assetPath = SelectBestAsset(_assetIndex[cleanParams]);
        }
        
        // Strategy 2: Match by Mesh Name (if Node Name failed or was generic like "GameObject")
        if (assetPath == null && !string.IsNullOrEmpty(cleanMesh) && _assetIndex.ContainsKey(cleanMesh))
        {
            assetPath = SelectBestAsset(_assetIndex[cleanMesh]);
        }
        
        if (assetPath != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null)
            {
                // Verify prefab type? PrefabUtility.InstantiatePrefab is better for connection
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
        }
        
        // Fallback: Create Empty
        if (go == null)
        {
            go = new GameObject(node.Name);
            go.transform.SetParent(parent, false);
            
            // Visual indicator for missing asset if it had a mesh
            if (!string.IsNullOrEmpty(node.MeshName))
            {
                // Draw a red cube gizmo? Or add a placeholder mesh
                // For now just keep it empty but name it clearly
                go.name = $"[MISSING: {node.MeshName}] {node.Name}";
            }
        }
        
        // Apply Transform
        go.transform.localPosition = node.Pos;
        go.transform.localRotation = node.Rot;
        go.transform.localScale = node.Scale;
        
        // Rename to match dump
        if (!go.name.StartsWith("[MISSING")) go.name = node.Name;
        
        // Recursion
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                BuildNode(child, go.transform);
            }
        }
    }
    
    private string SelectBestAsset(List<string> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        
        // Priority: .prefab > .glb > .gltf > .fbx
        foreach (var p in candidates) { if (p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) return p; }
        foreach (var p in candidates) { if (p.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase)) return p; }
        foreach (var p in candidates) { if (p.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase)) return p; }
        
        return candidates[0];
    }
    
    // ===================================================================================
    // UTILITIES
    // ===================================================================================
    
    private void FixShaders()
    {
        var renderers = FindObjectsOfType<Renderer>();
        var shader = Shader.Find("Standard");
        int count = 0;
        
        Undo.RecordObjects(renderers, "Fix Shaders");
        
        foreach (var r in renderers)
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat != null && (mat.shader.name == "Hidden/InternalErrorShader" || mat.shader.name == "Legacy Shaders/Diffuse")) 
                {
                    mat.shader = shader;
                    count++;
                }
            }
        }
        Log($"Fixed {count} materials.");
    }

    // Data Structures (Match SceneDumper.cs)
    public class SceneDump
    {
        public string SceneName;
        public string Timestamp;
        public List<SceneNode> Roots;
    }

    public class SceneNode
    {
        public string Name;
        // Newtonsoft handles Vector3 if we just map x,y,z matching the input JSON
        public SerializableVector3 Pos;
        public SerializableQuaternion Rot;
        public SerializableVector3 Scale;
        
        public string MeshName;
        public List<string> Materials;
        public List<SceneNode> Children;
    }
    
    // Helper classes to match the custom converters in SceneDumper
    // SceneDumper uses {x:..., y:..., z:...}
    public struct SerializableVector3
    {
        public float x, y, z;
        public static implicit operator Vector3(SerializableVector3 v) => new Vector3(v.x, v.y, v.z);
    }
    
    public struct SerializableQuaternion
    {
        public float x, y, z, w;
        public static implicit operator Quaternion(SerializableQuaternion q) => new Quaternion(q.x, q.y, q.z, q.w);
    }
}
