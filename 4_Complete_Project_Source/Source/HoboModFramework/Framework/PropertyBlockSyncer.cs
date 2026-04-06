using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Mirrors the <see cref="MaterialPropertyBlock"/> and enabled state from a
    /// hidden vanilla <see cref="Renderer"/> onto one or more injected custom renderers.
    ///
    /// Attach this component to the root of an injected Avatar prefab so that
    /// the game's native fading, weather, and shader effects are automatically
    /// forwarded to the custom mesh every frame.
    ///
    /// This avoids the need for Harmony patches on per-frame update loops.
    /// </summary>
    public class PropertyBlockSyncer : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        //  FIELDS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The hidden vanilla renderer whose PropertyBlock we read.</summary>
        private Renderer _sourceRenderer;

        /// <summary>The custom renderers that receive the copied PropertyBlock.</summary>
        private Renderer[] _targetRenderers;

        /// <summary>
        /// Reusable buffer to avoid allocating a new MaterialPropertyBlock every frame.
        /// Unity's GetPropertyBlock/SetPropertyBlock accept a pre-allocated block.
        /// </summary>
        private MaterialPropertyBlock _block;

        /// <summary>
        /// Tracks whether the source renderer was destroyed or nulled by the game.
        /// If so, we self-destruct to avoid null-reference spam in the log.
        /// </summary>
        private bool _dead;

        // ─────────────────────────────────────────────────────────────────────
        //  INITIALIZATION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Configures the syncer with a source renderer to read from and
        /// an array of target renderers to write to.
        /// </summary>
        /// <param name="source">
        /// The vanilla SkinnedMeshRenderer that the game's NPCModelBehavior
        /// natively updates with MaterialPropertyBlock data (fading, weather, etc.).
        /// </param>
        /// <param name="targets">
        /// The custom SkinnedMeshRenderers on the injected Avatar prefab.
        /// </param>
        public void Setup(Renderer source)
        {
            _sourceRenderer = source;
            _targetRenderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SYNC LOOP
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs after all Update/LateUpdate calls, ensuring the game has
        /// already written its final PropertyBlock state for this frame.
        /// We then copy that state onto every target renderer.
        /// </summary>
        private void LateUpdate()
        {
            // Guard: if the source was destroyed by the game, clean up.
            if (_dead) return;

            if (_sourceRenderer == null)
            {
                _dead = true;
                Destroy(this);
                return;
            }

            // Read the current property block from the vanilla renderer.
            _sourceRenderer.GetPropertyBlock(_block);

            // Copy enabled state: if the game disables the vanilla renderer
            // (e.g. during cutscenes or despawn), our custom renderers follow.
            bool isEnabled = _sourceRenderer.enabled;

            for (int i = 0; i < _targetRenderers.Length; i++)
            {
                var target = _targetRenderers[i];
                if (target == null) continue;

                target.SetPropertyBlock(_block);
                target.enabled = isEnabled;
            }
        }
    }
}
