#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Barotrauma.Plugins
{
    public class PluginLoadContext : AssemblyLoadContext
    {
        private readonly PluginInfoFile pluginInfoFile;

        /// <summary>
        /// The assembly dependency resolver is a special class provided by dotnet that is used to do 
        /// most of the heavy lifting of dependency resolution, having all the logic that locates the 
        /// right versions of dependencies that need to be loaded.
        /// You can read more on <see href="https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support"/>
        /// </summary>
        private readonly AssemblyDependencyResolver resolver;

        public PluginLoadContext(PluginInfoFile pluginInfoFile, string? name, bool isCollectible = false) : base(name, isCollectible)
        {
            this.pluginInfoFile = pluginInfoFile;

            // Dependency resolution has the following order in the ALC:
            // 1. Try to get from the Assemblies list
            // 2. Call overriden Load()
            // 3. Look in the default ALC context
            // 4. Resolving event
            
            resolver = new AssemblyDependencyResolver(pluginInfoFile.MainPluginInfo.AssemblyPath.FullPath);

            Resolving += OnResolving;
            Unloading += OnUnloading;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Before proceeding to the rest of the dependency resolution, we !!need!! to first try to resolve this dependency locally,
            // if we don't do this first, we could be resolving an assembly from the default ALC that has a different version, potentially
            // causing weird issues in the future when we, for example, update our nuget packages!

            string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }

        private Assembly? OnResolving(AssemblyLoadContext assemblyLoadContext, AssemblyName assemblyName)
        {
            // At this point we failed all resolutions, lets try to see if this assembly is from a content package we depend on

            List<(ContentPackage ContentPackage, Assembly Assembly)> matchingDependencies = [];

            foreach (var loadedPlugin in PluginLoader.LoadedPlugins)
            {
                if (loadedPlugin.PluginLoadContext == this) { continue; }

                Assembly? dependencyAssembly = loadedPlugin.PluginLoadContext.Assemblies
                    .FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), assemblyName));

                if (dependencyAssembly != null)
                {
                    matchingDependencies.Add((loadedPlugin.Info.ContentPackage, dependencyAssembly));
                }
            }

            if (matchingDependencies.Count > 0)
            {
                // Do we actually depend on any of these we found?
                foreach (var matchingDependency in matchingDependencies)
                {
                    if (pluginInfoFile.ContentPackage.Dependencies.Any(d => d.Matches(matchingDependency.ContentPackage)))
                    {
                        // Matched, good to go!
                        return matchingDependency.Assembly;
                    }
                }

                // We found matching dependencies in other content packages, but they weren't explictly marked as dependencies,
                // fail here since the mod developer probably forgot to add an explicit dependency to the content package
                string packagesString = String.Join(", ", matchingDependencies.Select(m => m.ContentPackage.Name));
                DebugConsole.ThrowError($"Content package {pluginInfoFile.ContentPackage.Name} has a plugin assembly that depends " +
                    $"on another plugin assembly defined in another content package, but this content package doesn't explictly " +
                    $"define a dependency to this other content package. Matched content packages: {packagesString}");
            }

            return null;
        }

        private void OnUnloading(AssemblyLoadContext context)
        {
            Resolving -= OnResolving;
            Unloading -= OnUnloading;
        }
    }
}