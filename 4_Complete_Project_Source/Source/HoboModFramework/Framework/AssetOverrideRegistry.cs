using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Registry for asset overrides - enables mods to replace ANY game asset.
    /// This is the core of the Universal Asset Override System.
    /// 
    /// POWER OVER CONVENIENCE: This API prioritizes flexibility over simplicity.
    /// Supports multiple registration methods to never limit modders.
    /// </summary>
    public static class AssetOverrideRegistry
    {
        private static ManualLogSource _log;
        
        // Override entry - supports multiple types of overrides
        private class OverrideEntry
        {
            public string FilePath;                          // File-based override
            public UnityEngine.Object DirectAsset;           // Direct object override
            public Func<UnityEngine.Object> DynamicProvider; // Callback override
            public int Priority;                             // Higher = wins conflicts
            public OverrideType Type;
            
            public enum OverrideType { File, Direct, Dynamic }
        }
        
        // Mapping: normalized asset path → override entry
        private static readonly Dictionary<string, OverrideEntry> _overrides = new();
        
        // Cache: loaded assets to avoid reloading file-based overrides
        private static readonly Dictionary<string, UnityEngine.Object> _loadedAssets = new();
        
        /// <summary>
        /// Initialize the registry with logging
        /// </summary>
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            if (Plugin.EnableDebugMode.Value) _log.LogInfo("[AssetOverrideRegistry] Initialized (Enhanced API)");
        }
        
        // ==================== REGISTRATION METHODS ====================
        
        /// <summary>
        /// Register a FILE-BASED override (loads from disk)
        /// </summary>
        /// <param name="originalPath">Game's asset path to override</param>
        /// <param name="modFilePath">Full path to replacement file on disk</param>
        /// <param name="priority">Higher priority wins if multiple mods override same asset</param>
        public static void RegisterOverride(string originalPath, string modFilePath, int priority = 0)
        {
            if (string.IsNullOrEmpty(originalPath)) return;
            
            var normalizedPath = NormalizePath(originalPath);
            
            if (!File.Exists(modFilePath))
            {
                _log?.LogWarning($"[AssetOverrideRegistry] Override file not found: {modFilePath}");
                return;
            }
            
            // Check priority against existing
            if (_overrides.TryGetValue(normalizedPath, out var existing) && existing.Priority > priority)
            {
                _log?.LogInfo($"[AssetOverrideRegistry] Skipping lower priority override for: {normalizedPath}");
                return;
            }
            
            _overrides[normalizedPath] = new OverrideEntry
            {
                FilePath = modFilePath,
                Priority = priority,
                Type = OverrideEntry.OverrideType.File
            };
            
            _log?.LogInfo($"[AssetOverrideRegistry] Registered file override: {normalizedPath} (priority: {priority})");
        }
        
        /// <summary>
        /// Register a DIRECT OBJECT override (already-loaded asset)
        /// Use this for runtime-generated textures, procedural content, etc.
        /// </summary>
        /// <typeparam name="T">Asset type</typeparam>
        /// <param name="originalPath">Game's asset path to override</param>
        /// <param name="asset">The asset object to return</param>
        /// <param name="priority">Higher priority wins if multiple mods override same asset</param>
        public static void RegisterOverride<T>(string originalPath, T asset, int priority = 0) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(originalPath)) return;
            if (asset == null) return;
            
            var normalizedPath = NormalizePath(originalPath);
            
            // Check priority against existing
            if (_overrides.TryGetValue(normalizedPath, out var existing) && existing.Priority > priority)
            {
                _log?.LogInfo($"[AssetOverrideRegistry] Skipping lower priority override for: {normalizedPath}");
                return;
            }
            
            _overrides[normalizedPath] = new OverrideEntry
            {
                DirectAsset = asset,
                Priority = priority,
                Type = OverrideEntry.OverrideType.Direct
            };
            
            _log?.LogInfo($"[AssetOverrideRegistry] Registered direct override: {normalizedPath} ({typeof(T).Name}, priority: {priority})");
        }
        
        /// <summary>
        /// Register a DYNAMIC CALLBACK override (called each time asset is requested)
        /// Use this for state-dependent assets that change based on game conditions.
        /// </summary>
        /// <typeparam name="T">Asset type</typeparam>
        /// <param name="originalPath">Game's asset path to override</param>
        /// <param name="assetProvider">Function that returns the asset (called on each request)</param>
        /// <param name="priority">Higher priority wins if multiple mods override same asset</param>
        public static void RegisterDynamicOverride<T>(string originalPath, Func<T> assetProvider, int priority = 0) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(originalPath)) return;
            if (assetProvider == null) return;
            
            var normalizedPath = NormalizePath(originalPath);
            
            // Check priority against existing
            if (_overrides.TryGetValue(normalizedPath, out var existing) && existing.Priority > priority)
            {
                _log?.LogInfo($"[AssetOverrideRegistry] Skipping lower priority override for: {normalizedPath}");
                return;
            }
            
            _overrides[normalizedPath] = new OverrideEntry
            {
                DynamicProvider = () => assetProvider(),
                Priority = priority,
                Type = OverrideEntry.OverrideType.Dynamic
            };
            
            _log?.LogInfo($"[AssetOverrideRegistry] Registered dynamic override: {normalizedPath} (priority: {priority})");
        }
        
        /// <summary>
        /// Unregister a single override
        /// </summary>
        /// <param name="originalPath">Game's asset path to unregister</param>
        /// <returns>True if override was removed</returns>
        public static bool UnregisterOverride(string originalPath)
        {
            if (string.IsNullOrEmpty(originalPath)) return false;
            
            var normalizedPath = NormalizePath(originalPath);
            
            if (_overrides.Remove(normalizedPath))
            {
                // Also remove from cache if present
                if (_loadedAssets.TryGetValue(normalizedPath, out var cached))
                {
                    _loadedAssets.Remove(normalizedPath);
                    if (cached != null) UnityEngine.Object.Destroy(cached);
                    // Note: We don't destroy the asset - the mod that created it owns it
                }
                
                _log?.LogInfo($"[AssetOverrideRegistry] Unregistered override: {normalizedPath}");
                return true;
            }
            
            return false;
        }
        
        // ==================== QUERY METHODS ====================
        
        /// <summary>
        /// Check if an asset has an override registered
        /// </summary>
        public static bool HasOverride(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            return _overrides.ContainsKey(NormalizePath(assetPath));
        }
        
        /// <summary>
        /// Load and return the override asset.
        /// Handles all override types: file, direct, and dynamic.
        /// </summary>
        public static T LoadOverride<T>(string assetPath) where T : UnityEngine.Object
        {
            var normalizedPath = NormalizePath(assetPath);
            
            if (!_overrides.TryGetValue(normalizedPath, out var entry))
            {
                return null;
            }
            
            try
            {
                switch (entry.Type)
                {
                    case OverrideEntry.OverrideType.Direct:
                        return entry.DirectAsset as T;
                    
                    case OverrideEntry.OverrideType.Dynamic:
                        return entry.DynamicProvider?.Invoke() as T;
                    
                    case OverrideEntry.OverrideType.File:
                        // Check cache first for file-based
                        if (_loadedAssets.TryGetValue(normalizedPath, out var cached))
                        {
                            return cached as T;
                        }
                        
                        var asset = LoadAssetFromFile<T>(entry.FilePath);
                        if (asset != null)
                        {
                            _loadedAssets[normalizedPath] = asset;
                            _log?.LogInfo($"[AssetOverrideRegistry] Loaded file override: {normalizedPath}");
                        }
                        return asset;
                    
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetOverrideRegistry] Failed to load override for {normalizedPath}: {ex.Message}");
                return null;
            }
        }
        
        // ==================== ASSET LOADING ====================
        
        /// <summary>
        /// Load asset from file based on requested type.
        /// If type is generic Object, infers from file extension.
        /// </summary>
        private static T LoadAssetFromFile<T>(string filePath) where T : UnityEngine.Object
        {
            // If requested type is base Object, infer from file extension
            if (typeof(T) == typeof(UnityEngine.Object))
            {
                return LoadAssetByExtension(filePath) as T;
            }
            
            // Handle Texture2D
            if (typeof(T) == typeof(Texture2D) || typeof(T) == typeof(Texture))
            {
                return LoadTexture(filePath) as T;
            }
            
            // Handle Sprite
            if (typeof(T) == typeof(Sprite))
            {
                var texture = LoadTexture(filePath);
                if (texture != null)
                {
                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    sprite.name = texture.name;
                    return sprite as T;
                }
            }
            
            // Handle AudioClip
            if (typeof(T) == typeof(UnityEngine.AudioClip))
            {
                return LoadWavAudioClip(filePath) as T;
            }
            
            // TODO: Mesh, AnimationClip, etc.
            _log?.LogWarning($"[AssetOverrideRegistry] Unsupported asset type: {typeof(T).Name}");
            return null;
        }
        
        /// <summary>
        /// Infer asset type from file extension and load accordingly
        /// </summary>
        private static UnityEngine.Object LoadAssetByExtension(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            
            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".bmp":
                    return LoadTexture(filePath);
                    
                case ".wav":
                    return LoadWavAudioClip(filePath);
                    
                case ".ogg":
                    _log?.LogWarning($"[AssetOverrideRegistry] OGG audio requires async loading via UnityWebRequest - use WAV format instead");
                    return null;
                    
                case ".mp3":
                    _log?.LogWarning($"[AssetOverrideRegistry] MP3 audio requires async loading via UnityWebRequest - use WAV format instead");
                    return null;
                
                default:
                    _log?.LogWarning($"[AssetOverrideRegistry] Cannot infer type for extension: {ext}");
                    return null;
            }
        }
        
        /// <summary>
        /// Load a texture from a PNG/JPG file
        /// </summary>
        private static Texture2D LoadTexture(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            
            try
            {
                var imageData = File.ReadAllBytes(filePath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                
                if (ImageConversion.LoadImage(texture, imageData))
                {
                    texture.name = Path.GetFileNameWithoutExtension(filePath);
                    return texture;
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetOverrideRegistry] Texture load failed: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Load a WAV audio file from disk
        /// Supports: PCM 8/16/24/32-bit, mono/stereo, any sample rate
        /// </summary>
        private static UnityEngine.AudioClip LoadWavAudioClip(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            
            try
            {
                var wavData = File.ReadAllBytes(filePath);
                
                // Parse WAV header
                // RIFF header: "RIFF" (4) + file size (4) + "WAVE" (4)
                if (wavData.Length < 44)
                {
                    _log?.LogError($"[AssetOverrideRegistry] WAV file too small: {filePath}");
                    return null;
                }
                
                // Verify RIFF header
                if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F')
                {
                    _log?.LogError($"[AssetOverrideRegistry] Invalid WAV header (not RIFF): {filePath}");
                    return null;
                }
                
                // Verify WAVE format
                if (wavData[8] != 'W' || wavData[9] != 'A' || wavData[10] != 'V' || wavData[11] != 'E')
                {
                    _log?.LogError($"[AssetOverrideRegistry] Invalid WAV header (not WAVE): {filePath}");
                    return null;
                }
                
                // Find fmt chunk (skip chunks until we find it)
                int pos = 12;
                int channels = 0;
                int sampleRate = 0;
                int bitsPerSample = 0;
                int dataStart = 0;
                int dataSize = 0;
                
                while (pos < wavData.Length - 8)
                {
                    string chunkId = "" + (char)wavData[pos] + (char)wavData[pos+1] + (char)wavData[pos+2] + (char)wavData[pos+3];
                    int chunkSize = BitConverter.ToInt32(wavData, pos + 4);
                    
                    if (chunkId == "fmt ")
                    {
                        // Audio format (should be 1 for PCM)
                        int audioFormat = BitConverter.ToInt16(wavData, pos + 8);
                        if (audioFormat != 1)
                        {
                            _log?.LogWarning($"[AssetOverrideRegistry] WAV audio format {audioFormat} may not be supported (expected PCM=1)");
                        }
                        
                        channels = BitConverter.ToInt16(wavData, pos + 10);
                        sampleRate = BitConverter.ToInt32(wavData, pos + 12);
                        bitsPerSample = BitConverter.ToInt16(wavData, pos + 22);
                    }
                    else if (chunkId == "data")
                    {
                        dataStart = pos + 8;
                        dataSize = chunkSize;
                        break;
                    }
                    
                    pos += 8 + chunkSize;
                    // Align to word boundary
                    if (chunkSize % 2 == 1) pos++;
                }
                
                if (dataStart == 0 || channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                {
                    _log?.LogError($"[AssetOverrideRegistry] Invalid WAV format: {channels}ch, {sampleRate}Hz, {bitsPerSample}bit");
                    return null;
                }
                
                // Security: Validate data bounds before allocation
                if (dataStart + dataSize > wavData.Length)
                {
                    _log?.LogError($"[AssetOverrideRegistry] WAV data chunk ({dataSize} bytes) exceeds file size");
                    return null;
                }

                // Security: Prevent massive allocations (max 100MB buffer)
                if (dataSize > 100 * 1024 * 1024)
                {
                    _log?.LogError($"[AssetOverrideRegistry] WAV file too large: {dataSize} bytes");
                    return null;
                }
                
                // Calculate samples
                int bytesPerSample = bitsPerSample / 8;
                int totalSamples = dataSize / (bytesPerSample * channels);
                
                // Convert to float samples
                float[] samples = new float[totalSamples * channels];
                int sampleIndex = 0;
                
                for (int i = 0; i < dataSize && sampleIndex < samples.Length; i += bytesPerSample)
                {
                    int bytePos = dataStart + i;
                    if (bytePos + bytesPerSample > wavData.Length) break;
                    
                    float sample = 0f;
                    
                    switch (bitsPerSample)
                    {
                        case 8:
                            // 8-bit is unsigned
                            sample = (wavData[bytePos] - 128) / 128f;
                            break;
                        case 16:
                            sample = BitConverter.ToInt16(wavData, bytePos) / 32768f;
                            break;
                        case 24:
                            // 24-bit: read 3 bytes and sign-extend
                            int val24 = wavData[bytePos] | (wavData[bytePos + 1] << 8) | (wavData[bytePos + 2] << 16);
                            if ((val24 & 0x800000) != 0) val24 |= unchecked((int)0xFF000000);
                            sample = val24 / 8388608f;
                            break;
                        case 32:
                            sample = BitConverter.ToInt32(wavData, bytePos) / 2147483648f;
                            break;
                        default:
                            _log?.LogError($"[AssetOverrideRegistry] Unsupported WAV bit depth: {bitsPerSample}");
                            return null;
                    }
                    
                    samples[sampleIndex++] = sample;
                }
                
                // Create AudioClip
                var clip = AudioClip.Create(
                    Path.GetFileNameWithoutExtension(filePath),
                    totalSamples,
                    channels,
                    sampleRate,
                    false
                );
                
                clip.SetData(samples, 0);
                
                _log?.LogInfo($"[AssetOverrideRegistry] Loaded WAV: {Path.GetFileName(filePath)} ({channels}ch, {sampleRate}Hz, {bitsPerSample}bit, {totalSamples} samples)");
                
                return clip;
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AssetOverrideRegistry] WAV load failed: {ex.Message}");
                return null;
            }
        }
        
        // ==================== UTILITY METHODS ====================
        
        /// <summary>
        /// Normalize asset path for consistent lookups
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            
            return path
                .Replace("\\", "/")
                .ToLowerInvariant()
                .Trim()
                .TrimStart('/');
        }
        
        /// <summary>
        /// Clear all overrides and cached assets
        /// </summary>
        public static void Clear()
        {
            _overrides.Clear();
            
            // Destroy cached textures to free memory (only file-loaded ones)
            foreach (var asset in _loadedAssets.Values)
            {
                if (asset != null)
                {
                    UnityEngine.Object.Destroy(asset);
                }
            }
            _loadedAssets.Clear();
            
            _log?.LogInfo("[AssetOverrideRegistry] Cleared all overrides");
        }
        
        /// <summary>
        /// Get count of registered overrides
        /// </summary>
        public static int OverrideCount => _overrides.Count;
        
        /// <summary>
        /// Log all registered overrides (debug)
        /// </summary>
        public static void LogAllOverrides()
        {
            _log?.LogInfo($"[AssetOverrideRegistry] {_overrides.Count} overrides registered:");
            foreach (var kvp in _overrides)
            {
                var entry = kvp.Value;
                var typeStr = entry.Type.ToString();
                _log?.LogInfo($"  [{typeStr}] {kvp.Key} (priority: {entry.Priority})");
            }
        }
        
        // ==================== ADDRESSABLES INTEGRATION ====================
        
        /// <summary>
        /// Get all registered override keys (for IResourceLocator.Keys)
        /// </summary>
        public static IEnumerable<object> GetAllOverrideKeys()
        {
            return _overrides.Keys.Cast<object>();
        }
        
        /// <summary>
        /// Get the file path for a file-based override (for ModResourceLocator)
        /// Returns null if not a file-based override
        /// </summary>
        public static string GetOverrideFilePath(string assetPath)
        {
            var normalizedPath = NormalizePath(assetPath);
            
            if (_overrides.TryGetValue(normalizedPath, out var entry))
            {
                if (entry.Type == OverrideEntry.OverrideType.File)
                {
                    return entry.FilePath;
                }
            }
            
            return null;
        }
    }
}
