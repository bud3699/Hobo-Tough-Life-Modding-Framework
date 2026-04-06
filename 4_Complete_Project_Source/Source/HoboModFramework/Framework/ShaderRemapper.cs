using System;
using BepInEx.Logging;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// ShaderRemapper v3 — Native Shader Injection
    ///
    /// DEFINITIVE FIX for the invisible skinned mesh bug.
    ///
    /// ROOT CAUSE (confirmed via live memory dump):
    ///   Hobo: Tough Life uses a proprietary shader called "Render Pipeline/DitherLit"
    ///   for all animated NPC characters. Unity's IL2CPP build permanently stripped
    ///   bone-skinning math from the generic "Standard" shader because the developers
    ///   never used it on SkinnedMeshRenderers. Any AssetBundle material requesting
    ///   "Standard" collapses all vertices to (0,0,0), making the mesh invisible.
    ///
    /// SOLUTION:
    ///   Clone the vanilla NPC's live Material (which carries DitherLit with full
    ///   skinning math), inject the modder's albedo texture into _MainTex, and
    ///   replace _PBRMap with a dynamically generated 1×1 neutral texture to
    ///   prevent the sweaty/clay appearance caused by Hobo's packed PBR channel.
    /// </summary>
    public static class ShaderRemapper
    {
        private static ManualLogSource _log;

        // Cached 1×1 neutral PBR map — generated once in memory, reused forever.
        // Hobo's _PBRMap packs: R=Metallic, G=AmbientOcclusion, B=Detail, A=Smoothness
        // Neutral values: 0% Metal, 100% AO, 0% Detail, 0% Smoothness (matte finish)
        private static Texture2D _neutralPBR;

        /// <summary>
        /// Initialize the shader remapper.
        /// </summary>
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _log?.LogInfo("[ShaderRemapper] Native shader injection system ready.");
        }

        /// <summary>
        /// Fix all materials on a GameObject by cloning the vanilla donor material
        /// (which carries the DitherLit shader with compiled skinning math) and
        /// injecting the modder's original textures.
        /// </summary>
        /// <param name="obj">The custom model root GameObject.</param>
        /// <param name="vanillaDonor">The vanilla NPC's sharedMaterial to clone from.</param>
        /// <returns>Number of materials fixed.</returns>
        public static int FixMaterials(GameObject obj, Material vanillaDonor)
        {
            if (obj == null) return 0;

            if (vanillaDonor == null)
            {
                _log?.LogError("[ShaderRemapper] No vanilla donor material provided! Cannot fix materials.");
                return 0;
            }

            // Lazily create the neutral PBR map once
            EnsureNeutralPBR();

            _log?.LogInfo($"[ShaderRemapper] Fixing materials on '{obj.name}' using donor shader '{vanillaDonor.shader.name}'");

            int fixedCount = 0;

            // Fix SkinnedMeshRenderer materials (the primary target)
            var skinnedRenderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMaterial == null) continue;
                var fixedMat = CreateNativeMaterial(smr.sharedMaterial, vanillaDonor);
                if (fixedMat != null)
                {
                    smr.material = fixedMat;
                    fixedCount++;
                }
            }

            // Also fix MeshRenderers (accessories, props attached to the model)
            var meshRenderers = obj.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in meshRenderers)
            {
                if (mr.sharedMaterial == null) continue;
                var fixedMat = CreateNativeMaterial(mr.sharedMaterial, vanillaDonor);
                if (fixedMat != null)
                {
                    mr.material = fixedMat;
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                _log?.LogInfo($"[ShaderRemapper] SUCCESS: Fixed {fixedCount} material(s) on '{obj.name}' → native DitherLit shader");

            return fixedCount;
        }

        /// <summary>
        /// Creates a new material by cloning the vanilla donor (preserving the
        /// DitherLit shader and all its compiled skinning variants), then injects
        /// the modder's original albedo texture and a neutral PBR map.
        /// </summary>
        private static Material CreateNativeMaterial(Material modderMat, Material vanillaDonor)
        {
            // Save the modder's original albedo texture before we clone over it
            Texture modderAlbedo = null;
            if (modderMat.HasProperty("_MainTex"))
                modderAlbedo = modderMat.GetTexture("_MainTex");

            // Clone the vanilla donor — this inherits DitherLit with full bone math
            var fixedMat = new Material(vanillaDonor);
            fixedMat.name = modderMat.name + "_NativeFixed";

            // Inject the modder's albedo texture
            if (modderAlbedo != null && fixedMat.HasProperty("_MainTex"))
            {
                fixedMat.SetTexture("_MainTex", modderAlbedo);
                _log?.LogInfo($"[ShaderRemapper]   Injected modder albedo into _MainTex ({modderAlbedo.name})");
            }
            else
            {
                _log?.LogWarning($"[ShaderRemapper]   No _MainTex found on modder material '{modderMat.name}'");
            }

            // Neutralize the PBR map so the model doesn't inherit the vanilla
            // NPC's sweat/dirt/blood overlays
            if (fixedMat.HasProperty("_PBRMap"))
            {
                fixedMat.SetTexture("_PBRMap", _neutralPBR);
                _log?.LogInfo("[ShaderRemapper]   Injected neutral _PBRMap (matte, no metallic)");
            }

            // Ensure full opacity
            if (fixedMat.HasProperty("_Transparency"))
            {
                fixedMat.SetFloat("_Transparency", 1f);
            }

            return fixedMat;
        }

        /// <summary>
        /// Generates a 1×1 pixel neutral PBR texture in memory.
        /// This is cached and reused for all materials.
        /// </summary>
        private static void EnsureNeutralPBR()
        {
            if (_neutralPBR != null) return;

            // 'true' at the end forces Linear color space, which is required for normal math
            _neutralPBR = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            _neutralPBR.name = "NeutralPBR_Generated";

            // Hobo's proprietary DitherLit shader packs data differently:
            // R(0.5) & G(0.5) = Flat Normal Map. 
            // B(0.0) = Zero Metallic. 
            // A(0.0) = Zero Smoothness (Matte).
            _neutralPBR.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.0f, 0.0f));
            _neutralPBR.Apply();

            _log?.LogInfo("[ShaderRemapper] Generated 1×1 neutral PBR map in linear memory.");
        }

        /// <summary>
        /// Legacy compatibility — parameterless FixMaterials for non-skinned objects.
        /// Falls back to Shader.Find for simple props that don't need skinning.
        /// </summary>
        public static int FixMaterials(GameObject obj)
        {
            if (obj == null) return 0;

            // For non-skinned objects, try to use the native Standard shader
            var fallback = Shader.Find("Render Pipeline/DitherLit");
            if (fallback == null)
                fallback = Shader.Find("Standard");

            if (fallback == null)
            {
                _log?.LogError("[ShaderRemapper] No fallback shader available for non-donor FixMaterials call!");
                return 0;
            }

            int fixedCount = 0;
            var meshRenderers = obj.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in meshRenderers)
            {
                if (mr.sharedMaterial != null && IsShaderBroken(mr.sharedMaterial.shader))
                {
                    Texture albedo = null;
                    if (mr.sharedMaterial.HasProperty("_MainTex"))
                        albedo = mr.sharedMaterial.GetTexture("_MainTex");

                    var fixedMat = new Material(fallback);
                    fixedMat.name = mr.sharedMaterial.name + "_Fallback";

                    if (albedo != null && fixedMat.HasProperty("_MainTex"))
                        fixedMat.SetTexture("_MainTex", albedo);

                    mr.material = fixedMat;
                    fixedCount++;
                }
            }

            return fixedCount;
        }

        /// <summary>
        /// Check if a shader is broken (stripped, error shader, or unsupported).
        /// </summary>
        private static bool IsShaderBroken(Shader shader)
        {
            if (shader == null) return true;
            string name = shader.name.ToLower();
            if (name.Contains("error") || name.Contains("hidden/internalerror"))
                return true;
            return false;
        }

        /// <summary>
        /// Check if remapper is ready.
        /// </summary>
        public static bool IsReady => true;
    }
}
