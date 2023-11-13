#nullable enable

using System;
using System.Collections.Generic;

namespace Barotrauma
{
    internal class LeakyBucket
    {
        private readonly Queue<Action> queue;
        private readonly int capacity;
        private readonly float cooldownInSeconds;
        private float timer;

        public LeakyBucket(float cooldownInSeconds, int capacity)
        {
            this.cooldownInSeconds = cooldownInSeconds;
            this.capacity = capacity;
            queue = new Queue<Action>(capacity);
        }

        public void Update(float deltaTime)
        {
            if (timer > 0f)
            {
                timer -= deltaTime;
                return;
            }

            if (queue.Count is 0) { return; }

            TryDequeue();
        }

        private void TryDequeue()
        {
            timer = cooldownInSeconds;
            if (queue.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }

        public bool TryEnqueue(Action item)
        {
            if (queue.Count >= capacity) { return false; }
            queue.Enqueue(item);
            return true;
        }
    }
}