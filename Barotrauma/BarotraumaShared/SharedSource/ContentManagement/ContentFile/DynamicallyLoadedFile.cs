#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Plugins;

namespace Barotrauma
{
    /// <summary>
    /// Dynamically loaded files do not have a type defined in the vanilla game,
    /// It could be an erroneous or misspelled type name, or a type defined by a plugin.
    ///
    /// Since ContentFiles are loaded before the content package is loaded, even if the content package
    /// is disabled, we need this type for plugin-defined content types since the plugin code is not
    /// loaded until the content package is loaded.
    /// </summary>
    public class DynamicallyLoadedFile(string contentName, ContentPackage contentPackage, ContentPath path) : ContentFile(contentPackage, path)
    {
        public readonly string ContentName = contentName;

        public DynamicallyLoadedFile(ContentPackage contentPackage, ContentPath path) : this(string.Empty, contentPackage, path)
            => throw new InvalidOperationException("This content type should not be loaded with reflection.");

        private bool TryFindFile([NotNullWhen(true)] out ContentFile? file)
        {
            TypeInfo? info = PluginData.AllDynamicTypes.FirstOrDefault(dt => dt.Names.Contains(ContentName));
            if (info is null)
            {
                file = null;
                return false;
            }

            foreach (ContentFile dynamicFile in PluginData.AllDynamicContentFiles)
            {
                if (dynamicFile.GetType() == info.Type && dynamicFile.Path == Path)
                {
                    file = dynamicFile;
                    return true;
                }
            }

            ContentFile newInstance = info.CreateInstance(ContentPackage, Path) ?? throw new UnreachableCodeException();

            PluginData? targetData = PluginLoader.TryFindPluginDataForAssembly(newInstance.GetType().Assembly);

            if (targetData != null)
            {
                targetData.DynamicFiles.Add(newInstance);
            }
            else
            {
                DebugConsole.ThrowError($"Failed to find plugin data for dynamically loaded file \"{ContentName}\"");
            }

            if (targetData?.ContentPackage != null && targetData?.ContentPackage != ContentPackage)
            {
                ContentPackage otherPackage = targetData!.ContentPackage;
                if (!ContentPackage.DependenciesPresent.Any(dep => dep.Matches(otherPackage)))
                {
                    string dependencySuggestion = "";
                    if (otherPackage.SteamDlcAppId.TryUnwrap(out var appid))
                    {
                        dependencySuggestion = $"<Dependency isoptional=\"false\" order=\"Above\" steamappids=\"{appid}\" />";
                    }
                    else if (otherPackage.UgcId.TryUnwrap(out var ugcId))
                    {
                        dependencySuggestion = $"<Dependency isoptional=\"false\" order=\"Above\" ids=\"{ugcId}\" />";
                    }
                    else
                    {
                        dependencySuggestion = $"<Dependency isoptional=\"false\" order=\"Above\" names=\"{otherPackage.Name}\" />";
                    }

                    DebugConsole.ThrowError(
                        $"The content package is using a content file from {DebugConsole.ContentPackageLogName(otherPackage)}, but that package is not configured as a dependency. " +
                        $"This can cause load issues depending on the order that these content packages are loaded.\n" +
                        $"Configure a dependency by adding the following to the filelist:\n" +
                        $"{dependencySuggestion}", contentPackage: ContentPackage);
                }
            }

            file = newInstance;
            return true;
        }

        public override void LoadFile()
        {
            if (!TryFindFile(out var file))
            {
                Fail();
                return;
            }

            file.LoadFile();
        }

        public override void UnloadFile()
        {
            // If the content type isn't registered by any plugin, nothing could've been loaded from this file,
            // so there's nothing to unload (e.g. the file is in a file group whose conditionals weren't met).
            if (!TryFindFile(out var file)) { return; }

            file.UnloadFile();
        }

        public override void Sort()
        {
            if (!TryFindFile(out var file))
            {
                Fail();
                return;
            }

            file.Sort();
        }

        private void Fail()
            => DebugConsole.ThrowError($"Invalid content type \"{ContentName}\". Content type not found or registered by a plugin.", contentPackage: ContentPackage);

        /// <summary>
        /// We cannot be sure if the content is synced in multiplayer since the
        /// underlying content file is not known at the point when this is checked.
        /// So the best we can do is assume it based on if the plugin info is
        /// synced in multiplayer or not.
        /// </summary>
        public override bool NotSyncedInMultiplayer
            => ContentPackage.GetFiles<PluginInfoFile>().Any(p => p.NotSyncedInMultiplayer);
    }
}