using System;
using System.Threading;
using BepInEx.Logging;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Thread Safety Guardrails.
    /// This file is a STUB. It must be implemented by the Next Agent.
    /// </summary>
    public static class FrameworkConsistency
    {
        // TODO: Capture Main Thread ID in Initialize()
        // TODO: Implement EnsureMainThread(string context)
        
        public static void Initialize(ManualLogSource log)
        {
            throw new NotImplementedException("FrameworkConsistency.Initialize not implemented");
        }

        public static void EnsureMainThread(string context)
        {
            throw new NotImplementedException("FrameworkConsistency.EnsureMainThread not implemented");
        }
    }
}
