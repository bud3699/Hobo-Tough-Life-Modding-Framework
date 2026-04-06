using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Loads 3D model files and converts them to Unity Mesh objects.
    /// Supports multiple formats to never limit modders.
    /// </summary>
    public static class MeshLoader
    {
        private static ManualLogSource _log;
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            if (Plugin.EnableDebugMode.Value) _log.LogInfo("[MeshLoader] Initialized - Supported formats: .obj, .gltf, .glb");
        }
        
        /// <summary>
        /// Load a mesh from file. Auto-detects format by extension.
        /// </summary>
        public static Mesh LoadMesh(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _log?.LogWarning($"[MeshLoader] File not found: {filePath}");
                return null;
            }
            
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            
            try
            {
                switch (ext)
                {
                    case ".obj":
                        return LoadOBJ(filePath);
                    case ".gltf":
                        _log?.LogWarning($"[MeshLoader] GLTF (text) not yet supported, use .glb instead. File: {filePath}");
                        return null;
                    case ".glb":
                        return LoadGLB(filePath);
                    default:
                        _log?.LogWarning($"[MeshLoader] Unsupported format: {ext}. Supported: .obj, .glb");
                        return null;
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[MeshLoader] Failed to load {filePath}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Load an OBJ file and convert to Unity Mesh.
        /// OBJ format reference: https://en.wikipedia.org/wiki/Wavefront_.obj_file
        /// </summary>
        private static Mesh LoadOBJ(string filePath)
        {
            _log?.LogInfo($"[MeshLoader] Loading OBJ: {Path.GetFileName(filePath)}");
            
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var faceVertexIndices = new List<int>();
            var faceNormalIndices = new List<int>();
            var faceUvIndices = new List<int>();
            
            var lines = File.ReadAllLines(filePath);
            
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;
                
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                
                switch (parts[0].ToLowerInvariant())
                {
                    case "v": // Vertex position
                        if (parts.Length >= 4)
                        {
                            float x = ParseFloat(parts[1]);
                            float y = ParseFloat(parts[2]);
                            float z = ParseFloat(parts[3]);
                            vertices.Add(new Vector3(x, y, z));
                        }
                        break;
                        
                    case "vn": // Vertex normal
                        if (parts.Length >= 4)
                        {
                            float nx = ParseFloat(parts[1]);
                            float ny = ParseFloat(parts[2]);
                            float nz = ParseFloat(parts[3]);
                            normals.Add(new Vector3(nx, ny, nz));
                        }
                        break;
                        
                    case "vt": // Texture coordinate
                        if (parts.Length >= 3)
                        {
                            float u = ParseFloat(parts[1]);
                            float v = ParseFloat(parts[2]);
                            uvs.Add(new Vector2(u, v));
                        }
                        break;
                        
                    case "f": // Face
                        // Faces can be triangles (3 verts) or quads (4 verts)
                        // Format: f v1/vt1/vn1 v2/vt2/vn2 v3/vt3/vn3 [v4/vt4/vn4]
                        var faceVerts = new List<int>();
                        var faceNorms = new List<int>();
                        var faceUVs = new List<int>();
                        
                        for (int i = 1; i < parts.Length; i++)
                        {
                            ParseFaceVertex(parts[i], out int vi, out int ti, out int ni);
                            faceVerts.Add(vi);
                            faceNorms.Add(ni);
                            faceUVs.Add(ti);
                        }
                        
                        // Triangulate (fan triangulation for quads/n-gons)
                        for (int i = 1; i < faceVerts.Count - 1; i++)
                        {
                            faceVertexIndices.Add(faceVerts[0]);
                            faceVertexIndices.Add(faceVerts[i]);
                            faceVertexIndices.Add(faceVerts[i + 1]);
                            
                            faceNormalIndices.Add(faceNorms[0]);
                            faceNormalIndices.Add(faceNorms[i]);
                            faceNormalIndices.Add(faceNorms[i + 1]);
                            
                            faceUvIndices.Add(faceUVs[0]);
                            faceUvIndices.Add(faceUVs[i]);
                            faceUvIndices.Add(faceUVs[i + 1]);
                        }
                        break;
                }
            }
            
            _log?.LogInfo($"[MeshLoader] Parsed: {vertices.Count} verts, {normals.Count} normals, {uvs.Count} UVs, {faceVertexIndices.Count / 3} tris");
            
            // Build Unity mesh
            // OBJ uses shared vertices with different indices per attribute
            // Unity needs per-vertex data, so we expand
            var meshVertices = new Vector3[faceVertexIndices.Count];
            var meshNormals = new Vector3[faceVertexIndices.Count];
            var meshUVs = new Vector2[faceVertexIndices.Count];
            var meshTriangles = new int[faceVertexIndices.Count];
            
            for (int i = 0; i < faceVertexIndices.Count; i++)
            {
                int vi = faceVertexIndices[i] - 1; // OBJ is 1-indexed
                int ni = faceNormalIndices[i] - 1;
                int ti = faceUvIndices[i] - 1;
                
                meshVertices[i] = (vi >= 0 && vi < vertices.Count) ? vertices[vi] : Vector3.zero;
                meshNormals[i] = (ni >= 0 && ni < normals.Count) ? normals[ni] : Vector3.up;
                meshUVs[i] = (ti >= 0 && ti < uvs.Count) ? uvs[ti] : Vector2.zero;
                meshTriangles[i] = i;
            }
            
            var mesh = new Mesh();
            mesh.name = Path.GetFileNameWithoutExtension(filePath);
            mesh.vertices = meshVertices;
            mesh.normals = meshNormals;
            mesh.uv = meshUVs;
            mesh.triangles = meshTriangles;
            mesh.RecalculateBounds();
            
            // Recalculate normals if none were provided
            if (normals.Count == 0)
            {
                mesh.RecalculateNormals();
            }
            
            _log?.LogInfo($"[MeshLoader] Created mesh '{mesh.name}': {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris");
            
            return mesh;
        }
        
        /// <summary>
        /// Parse a float using invariant culture (handles both . and , decimal separators)
        /// </summary>
        private static float ParseFloat(string s)
        {
            // Try parsing with invariant culture first (uses .)
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            
            // Fallback: try current culture
            if (float.TryParse(s, out result))
                return result;
                
            return 0f;
        }
        
        /// <summary>
        /// Parse OBJ face vertex format: v/vt/vn or v//vn or v/vt or v
        /// Returns 1-indexed values, 0 means not specified
        /// </summary>
        private static void ParseFaceVertex(string s, out int vertexIndex, out int texIndex, out int normalIndex)
        {
            vertexIndex = 0;
            texIndex = 0;
            normalIndex = 0;
            
            var parts = s.Split('/');
            
            if (parts.Length >= 1 && int.TryParse(parts[0], out int v))
                vertexIndex = v;
                
            if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]) && int.TryParse(parts[1], out int t))
                texIndex = t;
                
            if (parts.Length >= 3 && int.TryParse(parts[2], out int n))
                normalIndex = n;
        }
        
        // ============================================================
        // GLB (Binary glTF) LOADER
        // Reference: https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
        // ============================================================
        
        /// <summary>
        /// Load a GLB file and convert to Unity Mesh.
        /// GLB is the binary format of glTF 2.0.
        /// </summary>
        private static Mesh LoadGLB(string filePath)
        {
            _log?.LogInfo($"[MeshLoader] Loading GLB: {Path.GetFileName(filePath)}");
            
            var bytes = File.ReadAllBytes(filePath);
            
            // GLB Header (12 bytes)
            // - magic: "glTF" (4 bytes)
            // - version: uint32 (should be 2)
            // - length: uint32 (total file size)
            
            if (bytes.Length < 12)
            {
                _log?.LogError("[MeshLoader] GLB file too small");
                return null;
            }
            
            uint magic = BitConverter.ToUInt32(bytes, 0);
            if (magic != 0x46546C67) // "glTF" in little-endian
            {
                _log?.LogError("[MeshLoader] Invalid GLB magic number");
                return null;
            }
            
            uint version = BitConverter.ToUInt32(bytes, 4);
            if (version != 2)
            {
                _log?.LogWarning($"[MeshLoader] GLB version {version} (expected 2), attempting anyway");
            }
            
            // Parse chunks
            byte[] jsonBytes = null;
            byte[] binBytes = null;
            
            int offset = 12;
            while (offset < bytes.Length)
            {
                if (offset + 8 > bytes.Length) break;
                
                uint chunkLength = BitConverter.ToUInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                offset += 8;
                
                if (offset + chunkLength > bytes.Length)
                {
                    _log?.LogWarning("[MeshLoader] Chunk exceeds file bounds");
                    break;
                }
                
                if (chunkType == 0x4E4F534A) // "JSON"
                {
                    jsonBytes = new byte[chunkLength];
                    Array.Copy(bytes, offset, jsonBytes, 0, (int)chunkLength);
                }
                else if (chunkType == 0x004E4942) // "BIN\0"
                {
                    binBytes = new byte[chunkLength];
                    Array.Copy(bytes, offset, binBytes, 0, (int)chunkLength);
                }
                
                offset += (int)chunkLength;
            }
            
            if (jsonBytes == null)
            {
                _log?.LogError("[MeshLoader] No JSON chunk in GLB");
                return null;
            }
            
            if (binBytes == null)
            {
                _log?.LogError("[MeshLoader] No BIN chunk in GLB");
                return null;
            }
            
            // Parse JSON using simple string parsing (avoid dependency on JSON library for this)
            string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            
            return ParseGltfJson(json, binBytes, Path.GetFileNameWithoutExtension(filePath));
        }
        
        /// <summary>
        /// Parse glTF JSON and extract mesh data from binary buffer.
        /// This is a simplified parser that handles common cases.
        /// </summary>
        private static Mesh ParseGltfJson(string json, byte[] binData, string meshName)
        {
            // Use Newtonsoft.Json for proper parsing
            var gltf = Newtonsoft.Json.JsonConvert.DeserializeObject<GltfRoot>(json);
            
            if (gltf?.meshes == null || gltf.meshes.Length == 0)
            {
                _log?.LogError("[MeshLoader] No meshes in glTF");
                return null;
            }
            
            // Get first mesh, first primitive
            var meshDef = gltf.meshes[0];
            if (meshDef.primitives == null || meshDef.primitives.Length == 0)
            {
                _log?.LogError("[MeshLoader] No primitives in mesh");
                return null;
            }
            
            var prim = meshDef.primitives[0];
            
            // Get accessor indices
            int posAccessor = prim.attributes?.POSITION ?? -1;
            int normAccessor = prim.attributes?.NORMAL ?? -1;
            int uvAccessor = prim.attributes?.TEXCOORD_0 ?? -1;
            int indicesAccessor = prim.indices ?? -1;
            
            if (posAccessor < 0)
            {
                _log?.LogError("[MeshLoader] No POSITION attribute");
                return null;
            }
            
            // Extract data from binary buffer
            var vertices = ExtractVec3Array(gltf, binData, posAccessor);
            var normals = normAccessor >= 0 ? ExtractVec3Array(gltf, binData, normAccessor) : null;
            var uvs = uvAccessor >= 0 ? ExtractVec2Array(gltf, binData, uvAccessor) : null;
            var indices = indicesAccessor >= 0 ? ExtractIndices(gltf, binData, indicesAccessor) : null;
            
            // Debug logging for UVs
            _log?.LogInfo($"[MeshLoader] GLB parsed: {vertices?.Length ?? 0} verts, {normals?.Length ?? 0} normals, {uvs?.Length ?? 0} UVs, {indices?.Length / 3 ?? 0} tris");
            _log?.LogInfo($"[MeshLoader] UV accessor index: {uvAccessor}");
            
            // Build Unity mesh
            var mesh = new Mesh();
            mesh.name = meshName;
            mesh.vertices = vertices;
            
            if (normals != null && normals.Length == vertices.Length)
                mesh.normals = normals;
            
            // Set UVs with more detailed logging
            if (uvs != null)
            {
                if (uvs.Length == vertices.Length)
                {
                    mesh.uv = uvs;
                    _log?.LogInfo($"[MeshLoader] UVs set successfully: {uvs.Length} coordinates");
                }
                else
                {
                    _log?.LogWarning($"[MeshLoader] UV count mismatch! UVs: {uvs.Length}, Vertices: {vertices.Length}");
                    // Try to set anyway - Unity might handle it
                    mesh.uv = uvs;
                }
            }
            else
            {
                _log?.LogWarning("[MeshLoader] No UVs found in GLB file!");
            }
            
            if (indices != null)
                mesh.triangles = indices;
            
            mesh.RecalculateBounds();
            
            if (normals == null || normals.Length != vertices.Length)
                mesh.RecalculateNormals();
            
            _log?.LogInfo($"[MeshLoader] Created mesh '{mesh.name}': {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris");
            
            return mesh;
        }
        
        private static Vector3[] ExtractVec3Array(GltfRoot gltf, byte[] binData, int accessorIndex)
        {
            var accessor = gltf.accessors[accessorIndex];
            var bufferView = gltf.bufferViews[accessor.bufferView];
            
            int offset = (bufferView.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
            int count = accessor.count;
            int stride = bufferView.byteStride ?? 12; // 3 floats * 4 bytes
            
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * stride;
                float x = BitConverter.ToSingle(binData, pos);
                float y = BitConverter.ToSingle(binData, pos + 4);
                float z = BitConverter.ToSingle(binData, pos + 8);
                result[i] = new Vector3(x, y, z);
            }
            
            return result;
        }
        
        private static Vector2[] ExtractVec2Array(GltfRoot gltf, byte[] binData, int accessorIndex)
        {
            var accessor = gltf.accessors[accessorIndex];
            var bufferView = gltf.bufferViews[accessor.bufferView];
            
            int offset = (bufferView.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
            int count = accessor.count;
            int stride = bufferView.byteStride ?? 8; // 2 floats * 4 bytes
            
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * stride;
                float u = BitConverter.ToSingle(binData, pos);
                float v = BitConverter.ToSingle(binData, pos + 4);
                // IMPORTANT: glTF uses top-left UV origin, Unity uses bottom-left
                // Must flip V coordinate: v = 1 - v
                result[i] = new Vector2(u, 1.0f - v);
            }
            
            return result;
        }
        
        private static int[] ExtractIndices(GltfRoot gltf, byte[] binData, int accessorIndex)
        {
            var accessor = gltf.accessors[accessorIndex];
            var bufferView = gltf.bufferViews[accessor.bufferView];
            
            int offset = (bufferView.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
            int count = accessor.count;
            int componentType = accessor.componentType;
            
            var result = new int[count];
            
            for (int i = 0; i < count; i++)
            {
                switch (componentType)
                {
                    case 5121: // UNSIGNED_BYTE
                        result[i] = binData[offset + i];
                        break;
                    case 5123: // UNSIGNED_SHORT
                        result[i] = BitConverter.ToUInt16(binData, offset + i * 2);
                        break;
                    case 5125: // UNSIGNED_INT
                        result[i] = (int)BitConverter.ToUInt32(binData, offset + i * 4);
                        break;
                    default:
                        result[i] = 0;
                        break;
                }
            }
            
            return result;
        }
        
        // ============================================================
        // glTF JSON Structure Classes (minimal for mesh extraction)
        // ============================================================
        
        private class GltfRoot
        {
            public GltfMesh[] meshes { get; set; }
            public GltfAccessor[] accessors { get; set; }
            public GltfBufferView[] bufferViews { get; set; }
            public GltfImage[] images { get; set; }
            public GltfTexture[] textures { get; set; }
            public GltfMaterial[] materials { get; set; }
        }
        
        private class GltfMesh
        {
            public GltfPrimitive[] primitives { get; set; }
        }
        
        private class GltfPrimitive
        {
            public GltfAttributes attributes { get; set; }
            public int? indices { get; set; }
            public int? material { get; set; }
        }
        
        private class GltfAttributes
        {
            public int? POSITION { get; set; }
            public int? NORMAL { get; set; }
            public int? TEXCOORD_0 { get; set; }
        }
        
        private class GltfAccessor
        {
            public int bufferView { get; set; }
            public int? byteOffset { get; set; }
            public int componentType { get; set; }
            public int count { get; set; }
            public string type { get; set; }
        }
        
        private class GltfBufferView
        {
            public int buffer { get; set; }
            public int? byteOffset { get; set; }
            public int byteLength { get; set; }
            public int? byteStride { get; set; }
        }
        
        private class GltfImage
        {
            public int? bufferView { get; set; }
            public string mimeType { get; set; }
            public string uri { get; set; }
        }
        
        private class GltfTexture
        {
            public int? source { get; set; }
        }
        
        private class GltfMaterial
        {
            public GltfPbrMetallicRoughness pbrMetallicRoughness { get; set; }
            public string name { get; set; }
        }
        
        private class GltfPbrMetallicRoughness
        {
            public GltfTextureInfo baseColorTexture { get; set; }
            public float[] baseColorFactor { get; set; }
        }
        
        private class GltfTextureInfo
        {
            public int index { get; set; }
        }
        
        // ============================================================
        // MESH + MATERIAL LOADING
        // ============================================================
        
        /// <summary>
        /// Result of loading a GLB with material data
        /// </summary>
        public class MeshLoadResult
        {
            public Mesh Mesh { get; set; }
            public Material Material { get; set; }
            public Texture2D Texture { get; set; }
        }
        
        /// <summary>
        /// Load a mesh with embedded material/texture from GLB
        /// </summary>
        public static MeshLoadResult LoadMeshWithMaterial(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _log?.LogWarning($"[MeshLoader] File not found: {filePath}");
                return null;
            }
            
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".glb")
            {
                _log?.LogWarning($"[MeshLoader] Material loading only supported for .glb files");
                return new MeshLoadResult { Mesh = LoadMesh(filePath) };
            }
            
            try
            {
                return LoadGLBWithMaterial(filePath);
            }
            catch (Exception ex)
            {
                _log?.LogError($"[MeshLoader] Failed to load with material: {ex.Message}");
                return null;
            }
        }
        
        private static MeshLoadResult LoadGLBWithMaterial(string filePath)
        {
            _log?.LogInfo($"[MeshLoader] Loading GLB with material: {Path.GetFileName(filePath)}");
            
            var bytes = File.ReadAllBytes(filePath);
            
            // Parse header
            if (bytes.Length < 12 || bytes[0] != 'g' || bytes[1] != 'l' || bytes[2] != 'T' || bytes[3] != 'F')
            {
                _log?.LogError("[MeshLoader] Invalid GLB header");
                return null;
            }
            
            // Parse chunks
            byte[] jsonBytes = null;
            byte[] binBytes = null;
            int offset = 12;
            
            while (offset + 8 <= bytes.Length)
            {
                uint chunkLength = BitConverter.ToUInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                offset += 8;
                
                if (offset + chunkLength > bytes.Length) break;
                
                if (chunkType == 0x4E4F534A) // "JSON"
                {
                    jsonBytes = new byte[chunkLength];
                    Array.Copy(bytes, offset, jsonBytes, 0, (int)chunkLength);
                }
                else if (chunkType == 0x004E4942) // "BIN\0"
                {
                    binBytes = new byte[chunkLength];
                    Array.Copy(bytes, offset, binBytes, 0, (int)chunkLength);
                }
                
                offset += (int)chunkLength;
            }
            
            if (jsonBytes == null || binBytes == null)
            {
                _log?.LogError("[MeshLoader] Missing JSON or BIN chunk");
                return null;
            }
            
            string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            var gltf = Newtonsoft.Json.JsonConvert.DeserializeObject<GltfRoot>(json);
            
            // Load mesh
            var mesh = ParseGltfJson(json, binBytes, Path.GetFileNameWithoutExtension(filePath));
            if (mesh == null) return null;
            
            var result = new MeshLoadResult { Mesh = mesh };
            
            // Try to load texture
            try
            {
                Texture2D texture = ExtractFirstTexture(gltf, binBytes);
                if (texture != null)
                {
                    result.Texture = texture;
                    
                    // Create material with texture - try multiple shaders for compatibility
                    Shader shader = Shader.Find("Unlit/Texture") 
                                 ?? Shader.Find("Mobile/Diffuse")
                                 ?? Shader.Find("Legacy Shaders/Diffuse")
                                 ?? Shader.Find("Standard");
                    
                    if (shader != null)
                    {
                        var mat = new Material(shader);
                        mat.mainTexture = texture;
                        mat.color = Color.white;
                        result.Material = mat;
                        _log?.LogInfo($"[MeshLoader] Created material with shader: {shader.name}");
                    }
                    else
                    {
                        _log?.LogWarning("[MeshLoader] No suitable shader found!");
                    }
                }
                else
                {
                    // Check for base color factor (solid color)
                    Color? baseColor = GetBaseColor(gltf);
                    if (baseColor.HasValue)
                    {
                        Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                        if (shader != null)
                        {
                            var mat = new Material(shader);
                            mat.color = baseColor.Value;
                            result.Material = mat;
                            _log?.LogInfo($"[MeshLoader] Created material with color: {baseColor.Value}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[MeshLoader] Could not extract texture: {ex.Message}");
            }
            
            return result;
        }
        
        private static Texture2D ExtractFirstTexture(GltfRoot gltf, byte[] binData)
        {
            if (gltf.images == null || gltf.images.Length == 0) return null;
            
            var image = gltf.images[0];
            if (!image.bufferView.HasValue) return null;
            
            var bv = gltf.bufferViews[image.bufferView.Value];
            int start = bv.byteOffset ?? 0;
            int length = bv.byteLength;
            
            byte[] imageData = new byte[length];
            Array.Copy(binData, start, imageData, 0, length);
            
            // Create texture from PNG/JPEG data
            var tex = new Texture2D(2, 2);
            if (ImageConversion.LoadImage(tex, imageData))
            {
                _log?.LogInfo($"[MeshLoader] Loaded texture: {tex.width}x{tex.height}");
                return tex;
            }
            
            UnityEngine.Object.Destroy(tex);
            return null;
        }
        
        private static Color? GetBaseColor(GltfRoot gltf)
        {
            if (gltf.materials == null || gltf.materials.Length == 0) return null;
            
            var mat = gltf.materials[0];
            if (mat.pbrMetallicRoughness?.baseColorFactor != null)
            {
                var c = mat.pbrMetallicRoughness.baseColorFactor;
                if (c.Length >= 3)
                {
                    return new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f);
                }
            }
            
            return null;
        }
    }
}
