using System.Collections.Generic;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// The Rosetta Stone: Maps Unity's standard HumanBodyBones enum to the exact
    /// string names used by Hobo Tough Life's native Vanilla skeleton.
    ///
    /// These names were harvested live from the game's bone hierarchy via Unity Explorer
    /// and confirmed against the Ghidra-decompiled Game.HumanoidAnimator boneStructure array.
    ///
    /// This dictionary is the bridge that allows ANY standard Humanoid asset (Mixamo,
    /// Daz, custom rigs) to be remapped onto the Vanilla Hobo skeleton without needing
    /// the modder to rename their bones manually.
    /// </summary>
    public static class HoboBoneDictionary
    {
        /// <summary>
        /// Maps Unity HumanBodyBones → Vanilla Hobo bone Transform names.
        /// Only bones confirmed to exist on the Vanilla Hobo skeleton are included.
        /// Bones not in this dictionary will fall back to the custom skeleton.
        /// </summary>
        public static readonly Dictionary<HumanBodyBones, string> BoneNameMap =
            new Dictionary<HumanBodyBones, string>
        {
            // ── Spine ────────────────────────────────────────────────────
            { HumanBodyBones.Hips,              "Base HumanPelvis" },
            { HumanBodyBones.Spine,             "Base HumanRibcage" },
            { HumanBodyBones.Chest,             "Base HumanRibcage" },
            { HumanBodyBones.UpperChest,        "Base HumanRibcage" },
            { HumanBodyBones.Neck,              "Base HumanNeck" },
            { HumanBodyBones.Head,              "Base HumanHead" },

            // ── Left Arm ─────────────────────────────────────────────────
            { HumanBodyBones.LeftShoulder,      "Base LCollarbone" },
            { HumanBodyBones.LeftUpperArm,      "Base LUpperarm" },
            { HumanBodyBones.LeftLowerArm,      "Base LForearm" },
            { HumanBodyBones.LeftHand,          "Base LHand" },

            // ── Right Arm ────────────────────────────────────────────────
            { HumanBodyBones.RightShoulder,     "Base RCollarbone" },
            { HumanBodyBones.RightUpperArm,     "Base RUpperarm" },
            { HumanBodyBones.RightLowerArm,     "Base RForearm" },
            { HumanBodyBones.RightHand,         "Base RHand" },

            // ── Left Leg ─────────────────────────────────────────────────
            { HumanBodyBones.LeftUpperLeg,      "Base LThigh" },
            { HumanBodyBones.LeftLowerLeg,      "Base LCalf" },
            { HumanBodyBones.LeftFoot,          "Base LFoot" },
            { HumanBodyBones.LeftToes,          "Base LToe0" },

            // ── Right Leg ────────────────────────────────────────────────
            { HumanBodyBones.RightUpperLeg,     "Base RThigh" },
            { HumanBodyBones.RightLowerLeg,     "Base RCalf" },
            { HumanBodyBones.RightFoot,         "Base RFoot" },
            { HumanBodyBones.RightToes,         "Base RToe0" },

            // ── Left Fingers ─────────────────────────────────────────────
            { HumanBodyBones.LeftIndexProximal,     "Base LFinger1" },
            { HumanBodyBones.LeftIndexIntermediate, "Base LFinger11" },
            { HumanBodyBones.LeftIndexDistal,       "Base LFinger12" },
            { HumanBodyBones.LeftMiddleProximal,    "Base LFinger2" },
            { HumanBodyBones.LeftMiddleIntermediate,"Base LFinger21" },
            { HumanBodyBones.LeftMiddleDistal,      "Base LFinger22" },
            { HumanBodyBones.LeftRingProximal,      "Base LFinger3" },
            { HumanBodyBones.LeftRingIntermediate,  "Base LFinger31" },
            { HumanBodyBones.LeftRingDistal,        "Base LFinger32" },
            { HumanBodyBones.LeftLittleProximal,    "Base LFinger4" },
            { HumanBodyBones.LeftLittleIntermediate,"Base LFinger41" },
            { HumanBodyBones.LeftLittleDistal,      "Base LFinger42" },
            { HumanBodyBones.LeftThumbProximal,     "Base LFinger0" },
            { HumanBodyBones.LeftThumbIntermediate, "Base LFinger01" },
            { HumanBodyBones.LeftThumbDistal,       "Base LFinger02" },

            // ── Right Fingers ────────────────────────────────────────────
            { HumanBodyBones.RightIndexProximal,     "Base RFinger1" },
            { HumanBodyBones.RightIndexIntermediate, "Base RFinger11" },
            { HumanBodyBones.RightIndexDistal,       "Base RFinger12" },
            { HumanBodyBones.RightMiddleProximal,    "Base RFinger2" },
            { HumanBodyBones.RightMiddleIntermediate,"Base RFinger21" },
            { HumanBodyBones.RightMiddleDistal,      "Base RFinger22" },
            { HumanBodyBones.RightRingProximal,      "Base RFinger3" },
            { HumanBodyBones.RightRingIntermediate,  "Base RFinger31" },
            { HumanBodyBones.RightRingDistal,        "Base RFinger32" },
            { HumanBodyBones.RightLittleProximal,    "Base RFinger4" },
            { HumanBodyBones.RightLittleIntermediate,"Base RFinger41" },
            { HumanBodyBones.RightLittleDistal,      "Base RFinger42" },
            { HumanBodyBones.RightThumbProximal,     "Base RFinger0" },
            { HumanBodyBones.RightThumbIntermediate, "Base RFinger01" },
            { HumanBodyBones.RightThumbDistal,       "Base RFinger02" },

            // ── Eyes/Jaw (optional, may not exist on all Hobo variants) ──
            { HumanBodyBones.LeftEye,            "Base Leye" },
            { HumanBodyBones.RightEye,           "Base Reye" },
            { HumanBodyBones.Jaw,                "Base HumanJaw" },
        };
    }
}
