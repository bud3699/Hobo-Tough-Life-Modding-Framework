// DEPRECATED — DiagnosticProbe has served its purpose.
//
// This class was a temporary investigative tool used to confirm:
//   1. The Avatar fails to bind to the custom skeleton (returns NULL for GetBoneTransform).
//   2. The custom skeleton bones are frozen at (0,0,0) due to lacking animation data.
//   3. The SwitchLOD call does NOT overwrite the bones array (confirmed by Unity Explorer Hook).
//
// All three hypotheses were confirmed. The permanent fix is in:
//   - BoneRemapper.cs          — Remaps custom bones onto the live Vanilla skeleton (once per NPC).
//   - HoboBoneDictionary.cs    — Provides the HumanBodyBones → Vanilla string name lookup.
//   - SkinnedMeshHijack.cs     — Calls BoneRemapper, sets updateWhenOffscreen, and restores on LOD.
//
// This file is kept as documentation only. Do not re-enable.

namespace HoboModPlugin.Framework
{
    // Empty — class intentionally removed.
}
