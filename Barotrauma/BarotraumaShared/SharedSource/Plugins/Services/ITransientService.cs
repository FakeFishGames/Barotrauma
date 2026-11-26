using System;
using System.Reflection;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Transient service is created per-plugin and is disposed when the plugin is unloaded.
    /// Classes implementing this interface must have a constructor that takes <see cref="Barotrauma.PluginData"/> and <see cref="System.Reflection.Assembly"/> as a parameter
    /// and call <see cref="PluginLoader.AddTransientService(System.Reflection.Assembly, ITransientService)"/>.
    /// </summary>
    internal interface ITransientService : IDisposable
    {
        public Assembly PluginAssembly { get; }
        public PluginData PluginData { get; }
    }
}