using System;
using System.Collections.Concurrent;

namespace Barotrauma
{
    public sealed class NamedEvent<T> : IDisposable
    {
        private readonly ConcurrentDictionary<Identifier, Action<T>> events = new ConcurrentDictionary<Identifier, Action<T>>();

        public void Register(Identifier identifier, Action<T> action)
        {
            if (!events.TryAdd(identifier, action))
            {
                throw new ArgumentException($"Event with the identifier \"{identifier}\" has already been registered.", nameof(identifier));
            }
        }

        public void RegisterOverwriteExisting(Identifier identifier, Action<T> action)
        {
            events.AddOrUpdate(identifier, action, (k, v) => action);
        }

        public void Deregister(Identifier identifier)
        {
            events.TryRemove(identifier, out _);
        }

        public void TryDeregister(Identifier identifier)
        {
            if (!HasEvent(identifier)) { return; }
            Deregister(identifier);
        }

        public bool HasEvent(Identifier identifier)
            => events.ContainsKey(identifier);

        public void Invoke(T data)
        {
            foreach (var (_, action) in events)
            {
                action?.Invoke(data);
            }
        }

        public void Dispose()
        {
            events.Clear();
        }
    }
}