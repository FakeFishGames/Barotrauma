#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Barotrauma.Plugins
{
    [Flags]
    public enum PluginPlatforms
    {
        None = 0,
        Windows = 1,
        Linux = 2,
        Mac = 4,
        Any = Windows | Linux | Mac
    }

    [Flags]
    public enum PluginTargets
    {
        None = 0,
        Client = 1,
        Server = 2,
        Any = Client | Server
    }

    public sealed class PluginAssemblyInfo
    {
        public readonly ContentPackage ContentPackage;

        public readonly ContentPath AssemblyPath;

        public readonly Md5Hash Hash;

        /// <summary>
        /// Specifies the platforms in which this assembly should be loaded.
        /// </summary>
        public readonly PluginPlatforms Platforms;
        /// <summary>
        /// Specifies the targets in which this assembly should be loaded.
        /// </summary>
        public readonly PluginTargets Targets;

        public bool MatchesTarget => Targets.HasFlag(GetCurrentTarget());
        public bool MatchesPlatform => Platforms.HasFlag(GetCurrentPlatform());

        public readonly ImmutableArray<ContentPackage.FileConditional> Conditionals;

        public PluginAssemblyInfo(ContentXElement element)
        {
            if (element.ContentPackage == null)
            {
                throw new ArgumentException($"{nameof(ContentXElement.ContentPackage)} must not be null.");
            }

            AssemblyPath = element.GetAttributeContentPath(nameof(AssemblyPath)) ?? throw new Exception($"Tried to define plugin info without a {nameof(AssemblyPath)}");

            Hash = File.Exists(AssemblyPath.FullPath)
                       ? Md5Hash.CalculateForFile(AssemblyPath.FullPath, Md5Hash.StringHashOptions.BytePerfect) 
                       : Md5Hash.Blank;

            ContentPackage = element.ContentPackage;

            Platforms = element.GetAttributeEnum(nameof(Platforms), PluginPlatforms.Any);
            Targets = element.GetAttributeEnum(nameof(Targets), PluginTargets.Any);

            List<ContentPackage.FileConditional> conditionalElements = new List<ContentPackage.FileConditional>();

            foreach (var child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "Conditional":
                        conditionalElements.Add(new ContentPackage.FileConditional(child));
                        break;
                }
            }

            Conditionals = conditionalElements.ToImmutableArray();
        }

        public bool ShoudLoad()
        {
            if (!MatchesTarget)
            {
                return false;
            }

            if (!MatchesPlatform)
            {
                return false;
            }

            return Conditionals.All(c => c.Match(ContentPackage.DependenciesPresent)); ;
        }

        public static PluginTargets GetCurrentTarget()
        {
#if SERVER
            return PluginTargets.Server;
#else
            return PluginTargets.Client;
#endif
        }

        public static PluginPlatforms GetCurrentPlatform()
        {
#if WINDOWS
            return PluginPlatforms.Windows;
#elif LINUX
            return PluginPlatforms.Linux;
#elif OSX
            return PluginPlatforms.Mac;
#else
            return PluginPlatforms.None;
#endif
        }
    }
}