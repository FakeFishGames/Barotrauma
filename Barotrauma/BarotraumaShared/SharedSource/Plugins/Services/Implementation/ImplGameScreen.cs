using System;
using System.Reflection;

namespace Barotrauma.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed class ImplGameScreen : IGameScreen, ITransientService
    {
        public Assembly PluginAssembly { get; }
        public PluginData PluginData { get; }

        public ImplGameScreen(PluginData data, Assembly assembly)
        {
            PluginAssembly = assembly;
            PluginData = data;
            PluginLoader.AddTransientService(assembly, service: this);
        }

        public void RegisterResolutionChangeEvent(Identifier identifier, Action onResolutionChanged)
        {
            DeregisterResolutionChangeEvent(identifier);
            PluginData.OnResolutionChangedEvents.Add(identifier, onResolutionChanged);
        }

        public void DeregisterResolutionChangeEvent(Identifier identifier)
            => PluginData.OnResolutionChangedEvents.Remove(identifier);


        public void Dispose()
            => PluginData.OnResolutionChangedEvents.Clear();
    }
}