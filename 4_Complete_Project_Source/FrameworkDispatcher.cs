using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Safe dispatcher for background threads.
    /// Queues actions from any thread and executes them on the Unity main thread.
    /// Attach this MonoBehaviour to a persistent GameObject during framework init.
    /// </summary>
    public class FrameworkDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public static FrameworkDispatcher Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Schedule an action to run on the main thread during the next Update.
        /// Safe to call from any thread.
        /// </summary>
        public void Enqueue(Action action)
        {
            if (action == null) return;
            _queue.Enqueue(action);
        }

        private void Update()
        {
            // Drain the queue each frame.
            // Limit per-frame work to prevent hitches if many actions are queued.
            int processed = 0;
            while (processed < 64 && _queue.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FrameworkDispatcher] Queued action threw: {ex}");
                }
                processed++;
            }
        }
    }
}
