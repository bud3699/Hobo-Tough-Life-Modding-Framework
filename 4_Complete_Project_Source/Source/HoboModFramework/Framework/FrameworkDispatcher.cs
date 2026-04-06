using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Safe Airlock for background threads.
    /// This file is a STUB. It must be implemented by the Next Agent.
    /// </summary>
    public class FrameworkDispatcher : MonoBehaviour
    {
        // TODO: ConcurrentQueue<Action>
        // TODO: Enqueue(Action)
        // TODO: Update() loop
        
        public static FrameworkDispatcher Instance { get; private set; }

        public void Enqueue(Action action)
        {
            throw new NotImplementedException("Dispatcher.Enqueue not implemented");
        }

        private void Update()
        {
            // Execute pending actions
        }
    }
}
