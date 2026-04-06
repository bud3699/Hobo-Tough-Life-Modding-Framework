using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Manages 3D model overrides - allows modders to replace any mesh in the game.
    /// </summary>
    public static class ModelOverrideRegistry
    {
        private static ManualLogSource _log;
        
        // Registry: target mesh name -> replacement mesh
        private static readonly Dictionary<string, Mesh> _overrides = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        
        // Registry: target mesh name -> replacement material (optional)
        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        
        // Registry: target mesh name -> replacement texture (optional)
        private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        
        // Pending overrides (file path + scale + rotation)
        private static readonly Dictionary<string, (string filePath, float scale, float rotX, float rotY, float rotZ)> _pendingOverrides = new Dictionary<string, (string, float, float, float, float)>(StringComparer.OrdinalIgnoreCase);
        
        // Pending bundle overrides (bundlePath, assetName, assetType, instantiate, scale, rotation)
        private static readonly Dictionary<string, BundleOverrideInfo> _pendingBundleOverrides = new Dictionary<string, BundleOverrideInfo>(StringComparer.OrdinalIgnoreCase);
        
        // Track last applied renderer for tweak mode
        private static SkinnedMeshRenderer _lastAppliedRenderer;
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            MeshLoader.Initialize(log);
            _log.LogInfo("[ModelOverrides] Model override system initialized");
        }
        
        /// <summary>
        /// Register a model override from a file path (lazy loading)
        /// </summary>
        public static void RegisterOverride(string targetMeshName, string replacementFilePath, float scale = 1.0f, float rotX = 0f, float rotY = 0f, float rotZ = 0f)
        {
            _pendingOverrides[targetMeshName] = (replacementFilePath, scale, rotX, rotY, rotZ);
            _log?.LogInfo($"[ModelOverrides] Registered: {targetMeshName} -> {Path.GetFileName(replacementFilePath)} (scale: {scale}, rot: {rotX},{rotY},{rotZ})");
        }
        
        /// <summary>
        /// Register a model override from an AssetBundle
        /// </summary>
        public static void RegisterBundleOverride(string targetMeshName, string bundlePath, string assetName, string assetType, bool instantiate, float scale = 1.0f, float rotX = 0f, float rotY = 0f, float rotZ = 0f)
        {
            _pendingBundleOverrides[targetMeshName] = new BundleOverrideInfo
            {
                BundlePath = bundlePath,
                AssetName = assetName,
                AssetType = assetType,
                Instantiate = instantiate,
                Scale = scale,
                RotX = rotX,
                RotY = rotY,
                RotZ = rotZ
            };
            _log?.LogInfo($"[ModelOverrides] Registered bundle: {targetMeshName} -> {Path.GetFileName(bundlePath)}:{assetName} (type: {assetType}, scale: {scale})");
        }
        
        /// <summary>
        /// Register a pre-loaded mesh as override
        /// </summary>
        public static void RegisterOverride(string targetMeshName, Mesh replacementMesh)
        {
            _overrides[targetMeshName] = replacementMesh;
            _log?.LogInfo($"[ModelOverrides] Registered (preloaded): {targetMeshName} -> {replacementMesh.name}");
        }
        
        /// <summary>
        /// Check if there's an override for a mesh name
        /// </summary>
        public static bool HasOverride(string meshName)
        {
            return _overrides.ContainsKey(meshName) || _pendingOverrides.ContainsKey(meshName);
        }
        
        /// <summary>
        /// Get the override mesh (loads from file if pending)
        /// </summary>
        public static Mesh GetOverride(string meshName)
        {
            // Check pre-loaded first
            if (_overrides.TryGetValue(meshName, out var mesh))
                return mesh;
            
            // Load pending if exists
            if (_pendingOverrides.TryGetValue(meshName, out var pending))
            {
                // Use LoadMeshWithMaterial for GLB files to get texture/material
                var ext = Path.GetExtension(pending.filePath).ToLowerInvariant();
                
                if (ext == ".glb")
                {
                    var result = MeshLoader.LoadMeshWithMaterial(pending.filePath);
                    if (result?.Mesh != null)
                    {
                        mesh = result.Mesh;
                        
                        // Store texture for later use (we'll apply it using the original material's shader)
                        if (result.Texture != null)
                        {
                            _textures[meshName] = result.Texture;
                            _log?.LogInfo($"[ModelOverrides] Stored texture for {meshName}");
                        }
                        
                        // Store material as backup
                        if (result.Material != null)
                        {
                            _materials[meshName] = result.Material;
                        }
                    }
                }
                else
                {
                    mesh = MeshLoader.LoadMesh(pending.filePath);
                }
                
                if (mesh != null)
                {
                    // Apply rotation first (e.g., -90 X for Blender Z-up to Unity Y-up)
                    if (Math.Abs(pending.rotX) > 0.001f || Math.Abs(pending.rotY) > 0.001f || Math.Abs(pending.rotZ) > 0.001f)
                    {
                        RotateMesh(mesh, pending.rotX, pending.rotY, pending.rotZ);
                        _log?.LogInfo($"[ModelOverrides] Rotated mesh by ({pending.rotX}, {pending.rotY}, {pending.rotZ})");
                    }
                    
                    // Apply scale if not 1.0
                    if (Math.Abs(pending.scale - 1.0f) > 0.0001f)
                    {
                        ScaleMesh(mesh, pending.scale);
                        _log?.LogInfo($"[ModelOverrides] Scaled mesh by {pending.scale}");
                    }
                    
                    _overrides[meshName] = mesh;
                    _pendingOverrides.Remove(meshName);
                    return mesh;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Scale all vertices in a mesh around the local origin (0,0,0)
        /// This preserves the pivot point set by the modeler
        /// </summary>
        private static void ScaleMesh(Mesh mesh, float scale)
        {
            var vertices = mesh.vertices;
            
            // Scale relative to local origin (0,0,0) to preserve pivot
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] *= scale;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
        
        /// <summary>
        /// Rotate mesh vertices (for Z-up to Y-up conversion)
        /// Common rotation: (-90, 0, 0) to convert Blender Z-up to Unity Y-up
        /// </summary>
        private static void RotateMesh(Mesh mesh, float rotX, float rotY, float rotZ)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var rotation = Quaternion.Euler(rotX, rotY, rotZ);
            
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = rotation * vertices[i];
            }
            
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = rotation * normals[i];
                }
                mesh.normals = normals;
            }
            
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
        
        /// <summary>
        /// Process pending bundle overrides - loads replacement meshes from AssetBundles.
        /// Must be called BEFORE mesh interception is active so replacement meshes are ready.
        /// Public so FrameworkManager can call this early during initialization.
        /// </summary>
        public static void ProcessPendingBundleOverrides()
        {
            if (_pendingBundleOverrides.Count == 0) return;
            
            _log?.LogInfo($"[ModelOverrides] Processing {_pendingBundleOverrides.Count} bundle override(s)...");
            
            var toRemove = new List<string>();
            
            foreach (var kvp in _pendingBundleOverrides)
            {
                var targetName = kvp.Key;
                var info = kvp.Value;
                
                try
                {
                    var bundle = AssetBundleLoader.LoadBundle(info.BundlePath);
                    if (bundle == null)
                    {
                        _log?.LogError($"[ModelOverrides] Failed to load bundle for {targetName}");
                        continue;
                    }
                    
                    Mesh mesh = null;
                    GameObject sourceObj = null;
                    
                    switch (info.AssetType.ToLowerInvariant())
                    {
                        case "mesh":
                            mesh = AssetBundleLoader.GetMesh(bundle, info.AssetName);
                            break;
                            
                        case "skinned":
                        case "prefab":
                            if (info.Instantiate)
                            {
                                sourceObj = AssetBundleLoader.InstantiatePrefab(bundle, info.AssetName);
                            }
                            else
                            {
                                sourceObj = AssetBundleLoader.GetSkinnedMeshObject(bundle, info.AssetName);
                            }
                            
                            if (sourceObj != null)
                            {
                                var smr = sourceObj.GetComponentInChildren<SkinnedMeshRenderer>();
                                if (smr != null)
                                {
                                    mesh = smr.sharedMesh;
                                    if (smr.sharedMaterials != null && smr.sharedMaterials.Length > 0)
                                    {
                                        _materials[targetName] = smr.sharedMaterial;
                                        _log?.LogInfo($"[ModelOverrides] Stored bundle material for {targetName}");
                                    }
                                }
                                else
                                {
                                    var mf = sourceObj.GetComponentInChildren<MeshFilter>();
                                    if (mf != null) mesh = mf.sharedMesh;
                                }
                                
                                if (info.Instantiate && sourceObj != null)
                                {
                                    sourceObj.SetActive(false);
                                }
                            }
                            break;
                    }
                    
                    if (mesh != null)
                    {
                        if (Math.Abs(info.RotX) > 0.001f || Math.Abs(info.RotY) > 0.001f || Math.Abs(info.RotZ) > 0.001f)
                        {
                            RotateMesh(mesh, info.RotX, info.RotY, info.RotZ);
                        }
                        
                        if (Math.Abs(info.Scale - 1.0f) > 0.0001f)
                        {
                            ScaleMesh(mesh, info.Scale);
                        }
                        
                        _overrides[targetName] = mesh;
                        _log?.LogInfo($"[ModelOverrides] Bundle override ready: {targetName} -> {mesh.name}");
                        toRemove.Add(targetName);
                    }
                    else
                    {
                        _log?.LogError($"[ModelOverrides] Could not extract mesh from bundle for {targetName}");
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModelOverrides] Exception processing bundle {targetName}: {ex.Message}");
                }
            }
            
            foreach (var key in toRemove)
            {
                _pendingBundleOverrides.Remove(key);
            }
        }
        
        /// <summary>
        /// Apply all registered overrides to SkinnedMeshRenderers in the scene
        /// Auto-matches replacement mesh to original mesh bounds
        /// </summary>
        public static void ApplyAllOverrides()
        {
            int totalOverrides = _pendingOverrides.Count + _overrides.Count + _pendingBundleOverrides.Count;
            
            if (totalOverrides == 0)
            {
                _log?.LogInfo("[ModelOverrides] No model overrides to apply");
                return;
            }
            
            _log?.LogInfo($"[ModelOverrides] Applying {totalOverrides} override(s) ({_pendingBundleOverrides.Count} bundles)...");
            
            // Process bundle overrides first - convert them to mesh overrides
            ProcessPendingBundleOverrides();
            
            var renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            int applied = 0;
            
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                
                var meshName = renderer.sharedMesh.name;
                if (HasOverride(meshName))
                {
                    // Capture original mesh bounds BEFORE replacement
                    var originalBounds = renderer.sharedMesh.bounds;
                    _log?.LogInfo($"[AutoMatch] Original bounds: center={originalBounds.center}, size={originalBounds.size}");
                    
                    // Store original material before replacing mesh
                    var originalMaterial = renderer.sharedMaterial;
                    
                    var replacement = GetOverride(meshName);
                    if (replacement != null)
                    {
                        // CRITICAL: Clone mesh before modifying to prevent singleton corruption
                        // Without this, repeated applications warp the mesh progressively
                        var meshInstance = UnityEngine.Object.Instantiate(replacement);
                        meshInstance.name = replacement.name; // Remove "(Clone)" suffix
                        
                        // Auto-match: scale and position replacement to fit original bounds
                        AutoMatchMesh(meshInstance, originalBounds);
                        
                        renderer.sharedMesh = meshInstance;
                        _lastAppliedRenderer = renderer; // Track for tweak mode
                        _log?.LogInfo($"[ModelOverrides] Applied: {meshName} -> {replacement.name}");
                        
                        // Apply texture if we have one - USE ORIGINAL SHADER for proper lighting/shadows
                        if (_textures.TryGetValue(meshName, out var texture) && texture != null)
                        {
                            // Clone the original material to preserve the game's shader and lighting
                            // This gives us proper shadows, ambient occlusion, etc.
                            var newMat = new Material(originalMaterial);
                            newMat.mainTexture = texture;
                            
                            // Log shader info for debugging
                            _log?.LogInfo($"[ModelOverrides] Applied texture using original shader: {newMat.shader.name}");
                            _log?.LogInfo($"[ModelOverrides] Original material properties preserved for lighting");
                            
                            renderer.material = newMat;
                        }
                        
                        applied++;
                    }
                }
            }
            
            // Collect ALL override names that weren't applied to skinned meshes
            var unappliedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _overrides.Keys)
                unappliedNames.Add(key);
            foreach (var key in _pendingOverrides.Keys)
                unappliedNames.Add(key);
            
            // Remove names that WERE applied to skinned meshes
            foreach (var renderer in renderers)
            {
                if (renderer?.sharedMesh != null)
                {
                    // If this skinned mesh now has one of our override names, it was applied
                    // Actually, we need to track by original name, not replaced name
                }
            }
            
            // Simpler approach: just check if any pending/override names exist that could be static meshes
            // We'll always do the static mesh scan if we have ANY overrides registered
            int totalRegistered = _overrides.Count + _pendingOverrides.Count;
            
            if (totalRegistered > 0)
            {
                _log?.LogInfo($"[ModelOverrides] Checking {totalRegistered} override(s) against static meshes...");
                
                // Create HashSet of all override names for fast lookup
                var overrideNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in _overrides.Keys)
                    overrideNames.Add(key);
                foreach (var key in _pendingOverrides.Keys)
                    overrideNames.Add(key);
                
                var meshFilters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
                int staticApplied = 0;
                
                foreach (var filter in meshFilters)
                {
                    if (filter == null || filter.sharedMesh == null) continue;
                    
                    var meshName = filter.sharedMesh.name;
                    
                    // Fast check: is this mesh name in our override set?
                    if (!overrideNames.Contains(meshName)) continue;
                    
                    if (HasOverride(meshName))
                    {
                        var originalBounds = filter.sharedMesh.bounds;
                        var replacement = GetOverride(meshName);
                        
                        if (replacement != null)
                        {
                            // CRITICAL FIX: Must Instantiate static mesh replacements too!
                            // AutoMatchMesh modifies vertices; without Instantiate, we corrupt the shared asset.
                            var meshInstance = UnityEngine.Object.Instantiate(replacement);
                            meshInstance.name = replacement.name;

                            AutoMatchMesh(meshInstance, originalBounds);
                            filter.sharedMesh = meshInstance;
                            _log?.LogInfo($"[ModelOverrides] Applied (static): {meshName} -> {replacement.name} (unique instance)");
                            
                            var meshRenderer = filter.GetComponent<MeshRenderer>();
                            if (meshRenderer != null && _textures.TryGetValue(meshName, out var texture) && texture != null)
                            {
                                var newMat = new Material(meshRenderer.sharedMaterial);
                                newMat.mainTexture = texture;
                                meshRenderer.material = newMat;
                                _log?.LogInfo($"[ModelOverrides] Applied texture to static mesh");
                            }
                            
                            staticApplied++;
                            applied++;
                            
                            // Early exit: if we've applied all registered overrides, stop searching
                            if (staticApplied >= totalRegistered)
                                break;
                        }
                    }
                }
            }
            
            _log?.LogInfo($"[ModelOverrides] Applied {applied} override(s) total");
        }
        
        /// <summary>
        /// Auto-match a mesh to fit within target bounds
        /// Scales uniformly and centers the mesh
        /// </summary>
        private static void AutoMatchMesh(Mesh mesh, Bounds targetBounds)
        {
            var sourceBounds = mesh.bounds;
            _log?.LogInfo($"[AutoMatch] Replacement bounds: center={sourceBounds.center}, size={sourceBounds.size}");
            
            // Calculate uniform scale factor (use smallest dimension to fit inside)
            float scaleX = targetBounds.size.x > 0.0001f ? targetBounds.size.x / sourceBounds.size.x : 1f;
            float scaleY = targetBounds.size.y > 0.0001f ? targetBounds.size.y / sourceBounds.size.y : 1f;
            float scaleZ = targetBounds.size.z > 0.0001f ? targetBounds.size.z / sourceBounds.size.z : 1f;
            
            // Use uniform scale (average) to maintain proportions
            float scale = (scaleX + scaleY + scaleZ) / 3f;
            _log?.LogInfo($"[AutoMatch] Scale factors: X={scaleX:F4}, Y={scaleY:F4}, Z={scaleZ:F4}, using uniform={scale:F4}");
            
            // Apply scale and translation
            var vertices = mesh.vertices;
            Vector3 sourceCenter = sourceBounds.center;
            Vector3 targetCenter = targetBounds.center;
            
            for (int i = 0; i < vertices.Length; i++)
            {
                // Scale around source center, then translate to target center
                Vector3 v = vertices[i];
                v = (v - sourceCenter) * scale + targetCenter;
                vertices[i] = v;
            }
            
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            _log?.LogInfo($"[AutoMatch] Final bounds: center={mesh.bounds.center}, size={mesh.bounds.size}");
        }
        
        /// <summary>
        /// List all SkinnedMeshRenderer meshes in the current scene (for discovery)
        /// </summary>
        public static void DiscoverMeshes()
        {
            _log?.LogInfo("=== MESH DISCOVERY ===");
            
            // Skinned meshes (characters, animated objects)
            var skinnedRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            _log?.LogInfo($"=== SKINNED MESHES ({skinnedRenderers?.Length ?? 0}) ===");
            
            if (skinnedRenderers != null && skinnedRenderers.Length > 0)
            {
                var byParent = new Dictionary<string, List<string>>();
                foreach (var renderer in skinnedRenderers)
                {
                    if (renderer == null) continue;
                    var meshName = renderer.sharedMesh?.name ?? "(null)";
                    var parentName = renderer.transform.parent?.name ?? "(root)";
                    var goName = renderer.gameObject.name;
                    
                    if (!byParent.ContainsKey(parentName))
                        byParent[parentName] = new List<string>();
                    byParent[parentName].Add($"{goName}: {meshName}");
                }
                
                foreach (var kvp in byParent)
                {
                    _log?.LogInfo($"  [{kvp.Key}]");
                    foreach (var mesh in kvp.Value)
                        _log?.LogInfo($"    -> {mesh}");
                }
            }
            
            // Static meshes - just show count and first few (discovery is on-demand via F11)
            var meshFilters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
            _log?.LogInfo($"=== STATIC MESHES ({meshFilters?.Length ?? 0}) ===");
            _log?.LogInfo($"  (Showing first 25 - use targetMesh in mod.json to replace)");
            
            if (meshFilters != null && meshFilters.Length > 0)
            {
                int count = 0;
                foreach (var filter in meshFilters)
                {
                    if (filter == null || filter.sharedMesh == null) continue;
                    if (count++ >= 25)
                    {
                        _log?.LogInfo($"  ... and {meshFilters.Length - 25} more static meshes");
                        break;
                    }
                    
                    var meshName = filter.sharedMesh.name;
                    var goName = filter.gameObject.name;
                    _log?.LogInfo($"  {goName}: \"{meshName}\"");
                }
            }
            
            _log?.LogInfo("=== END DISCOVERY ===");
        }
        
        /// <summary>
        /// Clear all registered overrides
        /// </summary>
        public static void Clear()
        {
            // Destroy runtime meshes
            foreach (var mesh in _overrides.Values)
            {
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            }
            _overrides.Clear();
            
            // Destroy runtime textures
            foreach (var tex in _textures.Values)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            _textures.Clear();
            
            // Destroy runtime materials
            foreach (var mat in _materials.Values)
            {
                if (mat != null) UnityEngine.Object.Destroy(mat);
            }
            _materials.Clear();
            
            _pendingOverrides.Clear();
            _pendingBundleOverrides.Clear();
            
            _log.LogInfo("[ModelOverrides] Cleared and destroyed all runtime assets");
        }
        
        // ============================================================
        // ASSET-LEVEL MESH INTERCEPTION
        // Handles statically-batched props (e.g., park benches) that
        // don't exist as individual MeshFilter GameObjects at runtime.
        //
        // Two-pronged approach:
        //   1. Harmony postfix on AssetBundle.LoadAsset — intercepts
        //      meshes as the game loads them from its own bundles.
        //   2. Resources.FindObjectsOfTypeAll<Mesh>() scan — catches
        //      meshes already loaded into memory.
        //
        // Both methods use CopyMeshData() to modify the game's mesh
        // object in-place, so all existing references (including
        // static batch sources) see the replacement geometry.
        // ============================================================
        
        private static Harmony _meshInterceptionHarmony;
        private static bool _interceptionActive = false;
        private static readonly HashSet<int> _alreadyReplacedMeshIds = new();
        
        /// <summary>
        /// Install Harmony patches to intercept mesh loading from the game's
        /// AssetBundles. When the game loads a Mesh whose name matches a
        /// registered override target, the mesh data is replaced in-place.
        ///
        /// IMPORTANT: Call ProcessPendingBundleOverrides() BEFORE this method
        /// so that replacement meshes are ready when interception fires.
        /// </summary>
        public static void SetupMeshLoadInterception()
        {
            if (_interceptionActive)
            {
                _log?.LogInfo("[ModelOverrides] Mesh interception already active, skipping");
                return;
            }
            
            if (_overrides.Count == 0 && _pendingOverrides.Count == 0)
            {
                _log?.LogInfo("[ModelOverrides] No overrides registered, skipping mesh interception setup");
                return;
            }
            
            _meshInterceptionHarmony = new Harmony("com.hobomod.meshinterception");
            
            try
            {
                var postfix = typeof(ModelOverrideRegistry).GetMethod(
                    nameof(LoadAsset_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic
                );
                
                // IL2CPP exposes different method names than Mono.
                // Try each candidate in order of specificity:
                //   1. LoadAsset_Internal — the actual IL2CPP internal method
                //   2. LoadAsset — the public-facing method
                //   3. Load — legacy Unity API alias
                string[] methodCandidates = { "LoadAsset_Internal", "LoadAsset", "Load" };
                bool patched = false;
                
                foreach (var methodName in methodCandidates)
                {
                    // Search public AND non-public instance methods
                    var allMethods = typeof(AssetBundle).GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    );
                    
                    foreach (var m in allMethods)
                    {
                        if (m.Name != methodName) continue;
                        if (m.IsGenericMethod) continue;  // Skip generic overloads
                        
                        var parms = m.GetParameters();
                        if (parms.Length != 2) continue;
                        
                        // Check parameter types — accept String + Type in any IL2CPP form
                        var p0Name = parms[0].ParameterType.Name;
                        var p1Name = parms[1].ParameterType.Name;
                        
                        if ((p0Name == "String" || p0Name == "Il2CppSystem.String") &&
                            (p1Name == "Type" || p1Name == "Il2CppSystem.Type"))
                        {
                            _meshInterceptionHarmony.Patch(m, postfix: new HarmonyMethod(postfix));
                            _log?.LogInfo($"[ModelOverrides] Patched {methodName}({p0Name}, {p1Name})");
                            patched = true;
                            break;
                        }
                    }
                    
                    if (patched) break;
                }
                
                if (!patched)
                {
                    _log?.LogWarning("[ModelOverrides] Could not find any AssetBundle.LoadAsset method to patch");
                    
                    // Log all available Load methods for debugging
                    var methods = typeof(AssetBundle).GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    );
                    foreach (var m in methods)
                    {
                        if (m.Name.Contains("Load"))
                        {
                            var parms = m.GetParameters();
                            var parmStr = string.Join(", ", Array.ConvertAll(parms, p => $"{p.ParameterType.Name}"));
                            _log?.LogInfo($"  Available: {m.Name}({parmStr}) -> {m.ReturnType.Name}");
                        }
                    }
                }
                else
                {
                    _interceptionActive = true;
                    _log?.LogInfo($"[ModelOverrides] Mesh load interception active for {_overrides.Count} target(s)");
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[ModelOverrides] Failed to setup mesh interception: {ex.Message}");
                _log?.LogError(ex.StackTrace);
            }
        }
        
        /// <summary>
        /// Harmony postfix for AssetBundle.LoadAsset.
        /// Fires after every asset load from the game's bundles.
        /// If the loaded asset is a Mesh matching a registered override,
        /// copies replacement mesh data into it in-place.
        /// </summary>
        private static void LoadAsset_Postfix(UnityEngine.Object __result)
        {
            if (__result == null) return;
            
            // Only care about Mesh objects
            var mesh = __result.TryCast<Mesh>();
            if (mesh == null) return;
            
            string meshName = mesh.name;
            if (string.IsNullOrEmpty(meshName)) return;
            
            // Check if this mesh name matches any registered override
            if (_overrides.TryGetValue(meshName, out var replacement) && replacement != null)
            {
                _log?.LogInfo($"[ModelOverrides] INTERCEPTED: '{meshName}' ({mesh.vertexCount} verts) → replacing with '{replacement.name}' ({replacement.vertexCount} verts)");
                CopyMeshData(replacement, mesh);
                _log?.LogInfo($"[ModelOverrides] REPLACED: '{meshName}' now has {mesh.vertexCount} verts");
            }
        }
        
        /// <summary>
        /// Scan ALL loaded Mesh assets in memory (including inactive, cached,
        /// and assets not attached to any active GameObject).
        ///
        /// Uses Resources.FindObjectsOfTypeAll which searches the full
        /// Unity asset database in memory, not just on-screen objects.
        ///
        /// If a mesh's name matches a registered override target, copies
        /// replacement data into it in-place. This is the fallback approach
        /// for meshes loaded before our Harmony patch was installed.
        /// </summary>
        public static void FindAndReplaceLoadedMeshes()
        {
            if (_overrides.Count == 0 && _pendingOverrides.Count == 0)
            {
                return;
            }
            
            try
            {
                var allMeshes = Resources.FindObjectsOfTypeAll<Mesh>();
                // Memory scan count only logged at Debug level to reduce noise
                
                int replaced = 0;
                
                foreach (var mesh in allMeshes)
                {
                    if (mesh == null) continue;
                    
                    string meshName = mesh.name;
                    if (string.IsNullOrEmpty(meshName)) continue;
                    
                    if (_overrides.TryGetValue(meshName, out var replacement) && replacement != null)
                    {
                        int meshId = mesh.GetInstanceID();
                        if (_alreadyReplacedMeshIds.Contains(meshId))
                        {
                            // Skip already-replaced meshes silently
                            continue;
                        }
                        
                        _log?.LogInfo($"[ModelOverrides] FOUND in memory: '{meshName}' ({mesh.vertexCount} verts, isReadable={mesh.isReadable}, id={meshId})");
                        CopyMeshData(replacement, mesh);
                        _alreadyReplacedMeshIds.Add(meshId);
                        _log?.LogInfo($"[ModelOverrides] POST-COPY: '{meshName}' vertexCount={mesh.vertexCount} (expected {replacement.vertexCount})");
                        replaced++;
                    }
                }
                
                if (replaced > 0)
                {
                    _log?.LogInfo($"[ModelOverrides] Memory scan complete: replaced {replaced} mesh(es)");
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[ModelOverrides] Memory scan failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Copy all mesh data from source into target, modifying target in-place.
        ///
        /// This preserves the target's object identity — all existing references
        /// to the target mesh (including from static batch combine lists, MeshFilter
        /// components, and renderer material bindings) will see the new geometry.
        ///
        /// Copies: vertices, normals, tangents, UVs (channels 0-3), colors,
        /// triangles, and sub-meshes.
        /// </summary>
        private static void CopyMeshData(Mesh source, Mesh target)
        {
            try
            {
                // CopyMeshData debug logs removed (were step-by-step noise)
                
                target.Clear();

                
                // Core geometry
                target.vertices = source.vertices;

                
                if (source.normals != null && source.normals.Length > 0)
                    target.normals = source.normals;
                
                if (source.tangents != null && source.tangents.Length > 0)
                    target.tangents = source.tangents;
                
                // UV channels
                if (source.uv != null && source.uv.Length > 0)
                    target.uv = source.uv;
                if (source.uv2 != null && source.uv2.Length > 0)
                    target.uv2 = source.uv2;
                if (source.uv3 != null && source.uv3.Length > 0)
                    target.uv3 = source.uv3;
                if (source.uv4 != null && source.uv4.Length > 0)
                    target.uv4 = source.uv4;
                
                // Vertex colors
                if (source.colors != null && source.colors.Length > 0)
                    target.colors = source.colors;
                
                // Triangles and sub-meshes
                target.subMeshCount = source.subMeshCount;
                for (int i = 0; i < source.subMeshCount; i++)
                {
                    target.SetTriangles(source.GetTriangles(i), i);
                }
                
                target.RecalculateBounds();

            }
            catch (Exception ex)
            {
                _log?.LogError($"[ModelOverrides] CopyMeshData failed: {ex.Message}");
            }
        }
        
        // ============================================================
        // TWEAK MODE API (for ModelTweaker mod)
        // ============================================================
        
        private static SkinnedMeshRenderer _tweakTarget;
        private static Vector3 _tweakOffset = Vector3.zero;
        private static Vector3 _tweakRotation = Vector3.zero;
        private static float _tweakScale = 1.0f;
        private static string _tweakMeshName = "";
        
        /// <summary>
        /// Enable tweak mode on the most recently applied override
        /// </summary>
        public static bool EnableTweakMode()
        {
            // Use last applied renderer if available
            if (_lastAppliedRenderer != null && _lastAppliedRenderer.sharedMesh != null)
            {
                _tweakTarget = _lastAppliedRenderer;
                _tweakMeshName = _lastAppliedRenderer.sharedMesh.name;
                _tweakScale = 1.0f;
                _tweakOffset = Vector3.zero;
                _log?.LogInfo($"[TweakMode] Enabled on: {_tweakMeshName}");
                return true;
            }
            
            // Fallback: search for any replaced mesh
            var renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var renderer in renderers)
            {
                // Check if mesh name matches any of our loaded overrides (values, not keys)
                if (renderer?.sharedMesh != null && _overrides.ContainsValue(renderer.sharedMesh))
                {
                    _tweakTarget = renderer;
                    _tweakMeshName = renderer.sharedMesh.name;
                    _tweakScale = 1.0f;
                    _tweakOffset = Vector3.zero;
                    _log?.LogInfo($"[TweakMode] Enabled on: {_tweakMeshName}");
                    return true;
                }
            }
            
            // Fallback: find any player-related mesh
            foreach (var renderer in renderers)
            {
                if (renderer?.sharedMesh != null)
                {
                    var name = renderer.sharedMesh.name.ToLower();
                    if (name.Contains("hobo") || name.Contains("player") || name.Contains("fps"))
                    {
                        _tweakTarget = renderer;
                        _tweakMeshName = renderer.sharedMesh.name;
                        _tweakScale = 1.0f;
                        _tweakOffset = Vector3.zero;
                        _log?.LogInfo($"[TweakMode] Enabled on (fallback): {_tweakMeshName}");
                        return true;
                    }
                }
            }
            
            _log?.LogWarning("[TweakMode] No suitable mesh found");
            return false;
        }
        
        /// <summary>
        /// Adjust scale in tweak mode
        /// </summary>
        public static void TweakScale(float delta)
        {
            if (_tweakTarget == null) return;
            
            _tweakScale += delta;
            _tweakScale = Mathf.Max(0.001f, _tweakScale); // Don't go negative
            
            // Re-apply mesh with new scale
            var mesh = _tweakTarget.sharedMesh;
            if (mesh != null)
            {
                var vertices = mesh.vertices;
                float scaleFactor = 1.0f + delta / _tweakScale;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] *= scaleFactor;
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
            
            _log?.LogInfo($"[TweakMode] Scale: {_tweakScale:F4}");
        }
        
        /// <summary>
        /// Adjust position offset in tweak mode
        /// </summary>
        public static void TweakPosition(Vector3 delta)
        {
            if (_tweakTarget == null) return;
            
            _tweakOffset += delta;
            
            // Move the mesh vertices
            var mesh = _tweakTarget.sharedMesh;
            if (mesh != null)
            {
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] += delta;
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
            
            _log?.LogInfo($"[TweakMode] Offset: ({_tweakOffset.x:F3}, {_tweakOffset.y:F3}, {_tweakOffset.z:F3})");
        }
        
        /// <summary>
        /// Rotate mesh vertices in tweak mode - for finding correct orientation
        /// </summary>
        public static void TweakRotation(float deltaX, float deltaY, float deltaZ)
        {
            if (_tweakTarget == null) return;
            
            _tweakRotation += new Vector3(deltaX, deltaY, deltaZ);
            
            // Apply incremental rotation to mesh
            var mesh = _tweakTarget.sharedMesh;
            if (mesh != null)
            {
                var rotation = Quaternion.Euler(deltaX, deltaY, deltaZ);
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = rotation * vertices[i];
                }
                
                if (normals != null && normals.Length == vertices.Length)
                {
                    for (int i = 0; i < normals.Length; i++)
                    {
                        normals[i] = rotation * normals[i];
                    }
                    mesh.normals = normals;
                }
                
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
            
            _log?.LogInfo($"[TweakMode] Rotation: ({_tweakRotation.x:F1}, {_tweakRotation.y:F1}, {_tweakRotation.z:F1})");
        }
        
        /// <summary>
        /// Get current tweak values for copying to mod.json
        /// </summary>
        public static (string meshName, float scale, Vector3 offset, Vector3 rotation) GetTweakValues()
        {
            return (_tweakMeshName, _tweakScale, _tweakOffset, _tweakRotation);
        }
        
        /// <summary>
        /// Print tweak values to log in JSON format
        /// </summary>
        public static void PrintTweakValues()
        {
            _log?.LogInfo("=== TWEAK VALUES (copy to mod.json) ===");
            _log?.LogInfo($"\"target\": \"{_tweakMeshName}\",");
            _log?.LogInfo($"\"scale\": {_tweakScale:F4},");
            _log?.LogInfo($"\"rotX\": {_tweakRotation.x:F1},");
            _log?.LogInfo($"\"rotY\": {_tweakRotation.y:F1},");
            _log?.LogInfo($"\"rotZ\": {_tweakRotation.z:F1},");
            _log?.LogInfo($"\"offsetX\": {_tweakOffset.x:F4},");
            _log?.LogInfo($"\"offsetY\": {_tweakOffset.y:F4},");
            _log?.LogInfo($"\"offsetZ\": {_tweakOffset.z:F4}");
            _log?.LogInfo("========================================");
        }
        
        /// <summary>
        /// Check if tweak mode is active
        /// </summary>
        public static bool IsTweakModeActive => _tweakTarget != null;
        
        // ============================================================
        // EXTERNAL TOOL HOT-RELOAD
        // ============================================================
        
        private static long _lastConfigTimestamp = 0;
        private static string _externalConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HoboMod", "tweak_config.json");
        
        /// <summary>
        /// Check for external config changes and apply if updated
        /// Call this from Update loop
        /// </summary>
        public static void CheckExternalConfig()
        {
            if (_tweakTarget == null) return;
            if (!File.Exists(_externalConfigPath)) return;
            
            try
            {
                string json;
                using (var fs = new FileStream(_externalConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }
                
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<ExternalTweakConfig>(json);
                
                if (config != null && config.Timestamp > _lastConfigTimestamp)
                {
                    _lastConfigTimestamp = config.Timestamp;
                    ApplyExternalConfig(config);
                }
            }
            catch { }
        }
        
        private static void ApplyExternalConfig(ExternalTweakConfig config)
        {
            if (_tweakTarget == null || _tweakTarget.sharedMesh == null) return;
            
            _log?.LogInfo($"[TweakTool] Applying: rot=({config.RotX}, {config.RotY}, {config.RotZ}), scale={config.Scale}");
            
            // Reload the original mesh and apply new transforms
            // For now, apply incremental rotation from current state
            var mesh = _tweakTarget.sharedMesh;
            
            // Calculate delta from stored tweak values
            float deltaRotX = config.RotX - _tweakRotation.x;
            float deltaRotY = config.RotY - _tweakRotation.y;
            float deltaRotZ = config.RotZ - _tweakRotation.z;
            
            if (Math.Abs(deltaRotX) > 0.1f || Math.Abs(deltaRotY) > 0.1f || Math.Abs(deltaRotZ) > 0.1f)
            {
                TweakRotation(deltaRotX, deltaRotY, deltaRotZ);
            }
            
            // Scale delta
            float scaleFactor = config.Scale / _tweakScale;
            if (Math.Abs(scaleFactor - 1.0f) > 0.001f)
            {
                var vertices = mesh.vertices;
                var center = mesh.bounds.center;
                
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = center + (vertices[i] - center) * scaleFactor;
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                _tweakScale = config.Scale;
                _log?.LogInfo($"[TweakTool] Scale: {_tweakScale:F4}");
            }
            
            // Offset delta
            Vector3 newOffset = new Vector3(config.OffsetX, config.OffsetY, config.OffsetZ);
            Vector3 offsetDelta = newOffset - _tweakOffset;
            if (offsetDelta.magnitude > 0.01f)
            {
                TweakPosition(offsetDelta);
                _tweakOffset = newOffset;
            }
        }
        
        private class ExternalTweakConfig
        {
            public string TargetMesh { get; set; } = "";
            public int RotX { get; set; }
            public int RotY { get; set; }
            public int RotZ { get; set; }
            public float Scale { get; set; } = 1.0f;
            public float OffsetX { get; set; }
            public float OffsetY { get; set; }
            public float OffsetZ { get; set; }
            public long Timestamp { get; set; }
        }
        
        /// <summary>
        /// Bundle override metadata
        /// </summary>
        private class BundleOverrideInfo
        {
            public string BundlePath { get; set; }
            public string AssetName { get; set; }
            public string AssetType { get; set; } = "skinned";
            public bool Instantiate { get; set; }
            public float Scale { get; set; } = 1.0f;
            public float RotX { get; set; }
            public float RotY { get; set; }
            public float RotZ { get; set; }
        }
    }
}
