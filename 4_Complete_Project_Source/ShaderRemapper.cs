using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Automatically fixes AssetBundle materials that have broken shaders.
    /// IL2CPP games strip unused shaders, so bundles built with "Standard" shader
    /// won't render. This class detects broken shaders and replaces them with
    /// working shaders borrowed from the game's existing objects.
    /// </summary>
    public static class ShaderRemapper
    {
        private static ManualLogSource _log;
        private static bool _initialized = false;
        
        // Cached working shaders from the game
        private static Shader _opaqueShader;
        private static Shader _skinShader;
        private static Material _templateOpaqueMaterial;
        private static Material _templateSkinMaterial;
        
        /// <summary>
        /// Initialize the shader remapper. Should be called after scene loads.
        /// </summary>
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _log?.LogInfo("[ShaderRemapper] Shader remapping system ready. Will cache shaders on first use.");
        }
        
        /// <summary>
        /// Cache working shaders from existing game objects.
        /// Called lazily on first FixMaterials call to ensure scene is loaded.
        /// </summary>
        private static void CacheGameShaders()
        {
            if (_initialized) return;
            
            _log?.LogInfo("[ShaderRemapper] Caching shaders from game objects...");
            
            // 1. Try to find Skin shader from Player or NPCs
            var skinnedRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMaterial != null && smr.sharedMaterial.shader != null)
                {
                    // Ignore ourselves or broken standard shaders
                    if (smr.gameObject.name.StartsWith("CustomObject_")) continue;
                    if (IsShaderBroken(smr.sharedMaterial.shader)) continue;

                    _skinShader = smr.sharedMaterial.shader;
                    _templateSkinMaterial = smr.sharedMaterial;
                    _log?.LogInfo($"[ShaderRemapper] Cached skin shader: {_skinShader.name} from {smr.gameObject.name}");
                    break;
                }
            }
            
            // 2. Try to find Opaque shader from World (avoid particles, UI, and custom objects)
            var meshRenderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (var mr in meshRenderers)
            {
                if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null)
                {
                    string shaderName = mr.sharedMaterial.shader.name.ToLower();
                    
                    // Filter out bad sources
                    if (mr.gameObject.name.StartsWith("CustomObject_")) continue;
                    
                    // Filter out bad shaders (Standard is what we want to REPLACE, so don't cache it as a source)
                    if (shaderName == "standard" || shaderName.Contains("standard")) continue; 
                    
                    if (!shaderName.Contains("particle") && 
                        !shaderName.Contains("ui") && 
                        !shaderName.Contains("skybox") &&
                        !shaderName.Contains("unlit") &&
                        !shaderName.Contains("error"))
                    {
                        _opaqueShader = mr.sharedMaterial.shader;
                        _templateOpaqueMaterial = mr.sharedMaterial;
                        _log?.LogInfo($"[ShaderRemapper] Cached opaque shader: {_opaqueShader.name} from {mr.gameObject.name}");
                        break;
                    }
                }
            }
            
            // 3. HARDCODED FALLBACKS (If scene scan failed or found nothing good)
            if (_opaqueShader == null)
            {
                // Try to find known good shaders directly
                var fallback = Shader.Find("Render Pipeline/DitherLit") ?? 
                              Shader.Find("Legacy Shaders/Diffuse") ?? 
                              Shader.Find("Mobile/Diffuse");
                              
                if (fallback != null)
                {
                    _opaqueShader = fallback;
                    _templateOpaqueMaterial = new Material(fallback); // Create dummy template
                    _log?.LogInfo($"[ShaderRemapper] Cached opaque shader (Fallback): {_opaqueShader.name}");
                }
            }

            // Fallback: use skin shader for opaque if still no good opaque found
            if (_opaqueShader == null && _skinShader != null)
            {
                _opaqueShader = _skinShader;
                _templateOpaqueMaterial = _templateSkinMaterial;
                _log?.LogWarning("[ShaderRemapper] No opaque shader found, using skin shader as fallback");
            }
            
            _initialized = _opaqueShader != null || _skinShader != null;
            
            if (!_initialized)
            {
                _log?.LogError("[ShaderRemapper] Failed to cache any shaders! Materials cannot be fixed.");
            }
        }
        
        /// <summary>
        /// Check if a shader is broken (stripped, error shader, or unsupported)
        /// </summary>
        private static bool IsShaderBroken(Shader shader)
        {
            if (shader == null) return true;
            
            string name = shader.name.ToLower();
            
            // Known broken shader indicators
            if (name.Contains("error") || name.Contains("hidden/internalerror"))
                return true;
            
            // "Standard" is almost always stripped in IL2CPP builds
            if (name == "standard" || name == "standard (specular setup)")
                return true;
            
            // Check if shader is actually supported
            // Note: isSupported can give false positives, so we use it as secondary check
            // if (!shader.isSupported) return true;
            
            return false;
        }
        
        /// <summary>
        /// Fix all materials on a GameObject and its children.
        /// Replaces broken shaders with working game shaders while preserving textures.
        /// </summary>
        public static int FixMaterials(GameObject obj)
        {
            if (obj == null) return 0;
            
            // Lazy initialization
            if (!_initialized)
            {
                CacheGameShaders();
            }
            
            if (!_initialized)
            {
                _log?.LogWarning("[ShaderRemapper] Cannot fix materials - no cached shaders available");
                return 0;
            }
            
            int fixedCount = 0;
            
            // Fix MeshRenderer materials
            var meshRenderers = obj.GetComponentsInChildren<MeshRenderer>();
            foreach (var mr in meshRenderers)
            {
                if (mr.sharedMaterial != null && IsShaderBroken(mr.sharedMaterial.shader))
                {
                    var fixedMat = CreateFixedMaterial(mr.sharedMaterial, false);
                    if (fixedMat != null)
                    {
                        mr.material = fixedMat;
                        fixedCount++;
                    }
                }
            }
            
            // Fix SkinnedMeshRenderer materials
            var skinnedRenderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMaterial != null && IsShaderBroken(smr.sharedMaterial.shader))
                {
                    var fixedMat = CreateFixedMaterial(smr.sharedMaterial, true);
                    if (fixedMat != null)
                    {
                        smr.material = fixedMat;
                        fixedCount++;
                    }
                }
            }
            
            if (fixedCount > 0)
            {
                _log?.LogInfo($"[ShaderRemapper] Fixed {fixedCount} material(s) on {obj.name}");
            }
            
            return fixedCount;
        }
        
        /// <summary>
        /// Create a new material with working shader, copying textures from original
        /// </summary>
        private static Material CreateFixedMaterial(Material brokenMat, bool isSkinned)
        {
            Shader targetShader = isSkinned ? _skinShader : _opaqueShader;
            Material templateMat = isSkinned ? _templateSkinMaterial : _templateOpaqueMaterial;
            
            if (targetShader == null)
            {
                _log?.LogWarning("[ShaderRemapper] No target shader available for material fix");
                return null;
            }
            
            // Create new material from template (preserves shader properties)
            var fixedMat = new Material(templateMat);
            fixedMat.name = brokenMat.name + "_Fixed";
            
            // Copy texture properties from broken material
            CopyTextureProperties(brokenMat, fixedMat);
            
            _log?.LogInfo($"[ShaderRemapper] Fixed material '{brokenMat.name}': {brokenMat.shader?.name ?? "null"} -> {targetShader.name}");
            
            return fixedMat;
        }
        
        /// <summary>
        /// Copy common texture properties from source to target material
        /// </summary>
        private static void CopyTextureProperties(Material source, Material target)
        {
            // Common texture property names
            string[] textureProps = { "_MainTex", "_BaseMap", "_BumpMap", "_NormalMap", "_MetallicGlossMap", "_OcclusionMap" };
            
            foreach (var prop in textureProps)
            {
                if (source.HasProperty(prop) && target.HasProperty(prop))
                {
                    var tex = source.GetTexture(prop);
                    if (tex != null)
                    {
                        target.SetTexture(prop, tex);
                    }
                }
            }
            
            // Copy color if available
            if (source.HasProperty("_Color") && target.HasProperty("_Color"))
            {
                target.SetColor("_Color", source.GetColor("_Color"));
            }
            if (source.HasProperty("_BaseColor") && target.HasProperty("_BaseColor"))
            {
                target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            }
        }
        
        /// <summary>
        /// Get cached opaque shader for external use
        /// </summary>
        public static Shader GetOpaqueShader()
        {
            if (!_initialized) CacheGameShaders();
            return _opaqueShader;
        }
        
        /// <summary>
        /// Check if remapper is ready to fix materials
        /// </summary>
        public static bool IsReady => _initialized && (_opaqueShader != null || _skinShader != null);
    }
}
