using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// ShaderRegistry — Core Shader Injection System
    ///
    /// Solves the IL2CPP shader stripping problem once and for all.
    /// On initialization, this extracts a pre-compiled AssetBundle
    /// (embedded directly inside HoboModPlugin.dll) containing fully-baked
    /// Unity shaders with all lighting variants intact.
    ///
    /// Other framework systems (like ShaderRemapper) can then request
    /// pristine, working Shader objects from this registry instead of
    /// trying to steal shaders from random game objects.
    /// </summary>
    public static class ShaderRegistry
    {
        private static ManualLogSource _log;
        private static bool _initialized = false;

        /// <summary>
        /// Global cache of fully-compiled shaders extracted from the embedded bundle.
        /// Key = shader name (e.g. "Standard", "Mobile/Diffuse")
        /// Value = the live Shader object
        /// </summary>
        public static readonly Dictionary<string, Shader> CoreShaders = new(StringComparer.OrdinalIgnoreCase);

        // Keep the bundle alive so Unity doesn't garbage-collect the shaders
        private static AssetBundle _coreBundle;

        /// <summary>
        /// The embedded resource name inside HoboModPlugin.dll.
        /// .NET convention: {RootNamespace}.{Folder}.{FileName} with dots replacing path separators.
        /// </summary>
        private const string EmbeddedResourceName = "HoboModFramework.Resources.hobomod_core_shaders";

        /// <summary>
        /// Initialize the shader registry. Must be called during Plugin.Load()
        /// BEFORE any mods or custom objects attempt to load.
        /// </summary>
        public static void Initialize(ManualLogSource log)
        {
            _log = log;

            if (_initialized)
            {
                _log?.LogWarning("[ShaderRegistry] Already initialized, skipping.");
                return;
            }

            try
            {
                _log?.LogInfo("[ShaderRegistry] Extracting embedded core shader bundle...");

                // 1. Extract the raw bytes from inside the DLL
                byte[] bundleBytes = ExtractEmbeddedResource();
                if (bundleBytes == null || bundleBytes.Length == 0)
                {
                    _log?.LogError("[ShaderRegistry] FATAL: Embedded resource is empty or missing!");
                    return;
                }

                _log?.LogInfo($"[ShaderRegistry] Extracted {bundleBytes.Length} bytes from embedded resource.");

                // 2. Load the AssetBundle directly from memory
                _coreBundle = AssetBundle.LoadFromMemory(bundleBytes);
                if (_coreBundle == null)
                {
                    _log?.LogError("[ShaderRegistry] FATAL: AssetBundle.LoadFromMemory returned null!");
                    return;
                }

                // 3. Extract shaders from the bundle.
                //    The bundle contains Materials with compiled shaders attached,
                //    plus potentially raw Shader assets and ShaderVariantCollections.
                //    We harvest shaders from ALL of these sources.
                var allAssets = _coreBundle.LoadAllAssets();
                _log?.LogInfo($"[ShaderRegistry] Bundle contains {allAssets.Length} asset(s):");

                foreach (var asset in allAssets)
                {
                    _log?.LogInfo($"[ShaderRegistry]   - '{asset.name}' (type: {asset.GetIl2CppType().Name})");

                    // Strategy 1: Direct Shader assets
                    var shader = asset.TryCast<Shader>();
                    if (shader != null && !string.IsNullOrEmpty(shader.name))
                    {
                        if (!CoreShaders.ContainsKey(shader.name))
                        {
                            CoreShaders[shader.name] = shader;
                            _log?.LogInfo($"[ShaderRegistry]   ✓ Cached direct shader: '{shader.name}'");
                        }
                        continue;
                    }

                    // Strategy 2: Material assets — extract their .shader reference
                    var mat = asset.TryCast<Material>();
                    if (mat != null && mat.shader != null && !string.IsNullOrEmpty(mat.shader.name))
                    {
                        if (!CoreShaders.ContainsKey(mat.shader.name))
                        {
                            CoreShaders[mat.shader.name] = mat.shader;
                            _log?.LogInfo($"[ShaderRegistry]   ✓ Cached shader from material '{mat.name}': '{mat.shader.name}'");
                        }
                        continue;
                    }

                    // Strategy 3: ShaderVariantCollection — log it for diagnostics
                    var svc = asset.TryCast<ShaderVariantCollection>();
                    if (svc != null)
                    {
                        _log?.LogInfo($"[ShaderRegistry]   Found ShaderVariantCollection: '{svc.name}' (warmup aid)");
                    }
                }

                _initialized = CoreShaders.Count > 0;

                if (_initialized)
                {
                    _log?.LogInfo($"[ShaderRegistry] SUCCESS: {CoreShaders.Count} shader(s) ready for injection.");
                }
                else
                {
                    _log?.LogWarning("[ShaderRegistry] WARNING: No shaders were cached. ShaderRemapper will fall back to game shaders.");
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[ShaderRegistry] EXCEPTION during initialization: {ex}");
            }
        }




        /// <summary>
        /// Extracts the embedded AssetBundle bytes from the running assembly.
        /// </summary>
        private static byte[] ExtractEmbeddedResource()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Debug: list all embedded resources so we can verify the name
            if (Plugin.EnableDebugMode != null && Plugin.EnableDebugMode.Value)
            {
                var names = assembly.GetManifestResourceNames();
                _log?.LogInfo($"[ShaderRegistry] Embedded resources in assembly ({names.Length}):");
                foreach (var n in names)
                    _log?.LogInfo($"[ShaderRegistry]   - {n}");
            }

            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream == null)
            {
                _log?.LogError($"[ShaderRegistry] Could not find embedded resource: '{EmbeddedResourceName}'");

                // List available resources to help diagnose
                var names = assembly.GetManifestResourceNames();
                _log?.LogError($"[ShaderRegistry] Available resources ({names.Length}):");
                foreach (var n in names)
                    _log?.LogError($"[ShaderRegistry]   - {n}");

                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Try to get a working shader by name from our cache.
        /// Returns null if we don't have it.
        /// </summary>
        public static Shader GetShader(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return null;
            CoreShaders.TryGetValue(shaderName, out var shader);
            return shader;
        }

        /// <summary>
        /// Whether the registry has successfully loaded at least one shader.
        /// </summary>
        public static bool IsReady => _initialized && CoreShaders.Count > 0;
    }
}
