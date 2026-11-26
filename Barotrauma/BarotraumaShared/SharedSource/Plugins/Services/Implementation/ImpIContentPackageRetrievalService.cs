#nullable enable

using System.Linq;
using System.Reflection;

namespace Barotrauma.Plugins
{
    internal sealed class ImplContentPackageRetrievalService : IContentPackageRetrievalService, ITransientService
    {
        public Assembly PluginAssembly { get; private set; }
        public PluginData PluginData { get; private set; }

        public ContentPackage ContentPackage => PluginData.ContentPackage;

        public ImplContentPackageRetrievalService(PluginData data, Assembly assembly)
        {
            PluginAssembly = assembly;
            PluginData = data;
            PluginLoader.AddTransientService(assembly, service: this);
        }

        public bool IsDependencyPresent(Identifier dependencyIdentifier)
        {
            return ContentPackage.DependenciesPresent.Any(d => dependencyIdentifier == d.DependencyIdentifier);
        }

        public void Dispose() { }
    }
}