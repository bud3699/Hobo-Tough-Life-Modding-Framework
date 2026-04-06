using System;
using System.Threading;
using BepInEx.Logging;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Thread-safety guardrails.
    /// Call Initialize() once on startup to capture the main thread ID.
    /// Call EnsureMainThread() anywhere you need to assert you're on the Unity thread.
    /// </summary>
    public static class FrameworkConsistency
    {
        private static int _mainThreadId;
        private static bool _initialized;
        private static ManualLogSource _log;

        /// <summary>
        /// Must be called once from the main thread during plugin Awake/Load.
        /// </summary>
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _initialized = true;
            _log?.LogInfo($"[FrameworkConsistency] Main thread captured (ID: {_mainThreadId})");
        }

        /// <summary>
        /// Logs a warning if the caller is NOT on the main thread.
        /// Use this to guard Unity API calls that must happen on the main thread.
        /// </summary>
        public static void EnsureMainThread(string context)
        {
            if (!_initialized) return; // Not yet initialized, skip check

            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                _log?.LogWarning(
                    $"[FrameworkConsistency] '{context}' called from thread {Thread.CurrentThread.ManagedThreadId}" +
                    $" but main thread is {_mainThreadId}. Use FrameworkDispatcher.Enqueue() instead.");
            }
        }
    }
}
