using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma.Plugins
{
    internal abstract class SimpleRegisteringService<T> : ITransientService
    {
        public bool Disposed;

        public Assembly PluginAssembly { get; }
        public PluginData PluginData { get; }

        protected SimpleRegisteringService(PluginData data, Assembly assembly)
        {
            PluginAssembly = assembly;
            PluginData = data;
            PluginLoader.AddTransientService(assembly, service: this);
        }

        protected readonly HashSet<T> Registered = [];

        protected void Register(T item)
        {
            ObjectDisposedException.ThrowIf(Disposed, "Attempted to register an item after the service was disposed.");

            if (!Registered.Add(item))
            {
                throw new InvalidOperationException("Attempted to register an item that was already registered.");
            }
        }

        protected void Deregister(T item)
        {
            Registered.Remove(item);
            OnDeregister(item);
        }

        public abstract void OnDeregister(T item);

        public void Dispose()
        {
            foreach (T item in Registered.ToHashSet())
            {
                Deregister(item);
            }

            Registered.Clear();

            Disposed = true;
        }
    }
}