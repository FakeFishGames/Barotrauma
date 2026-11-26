#nullable enable

using Barotrauma.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum PluginApprovalStatus
    {
        Unknown,
        Approved,
        New,
        Moved,
        Modified
    }

    public sealed class PluginInfoFile : ContentFile
    {
        public override bool NotSyncedInMultiplayer => notSyncedInMultiplayer;

        public readonly AssemblyVerificationResult VerificationResult;

        internal bool PromptWarning => VerificationResult is not AssemblyVerificationResult.Success && ContentPackageManager.GetApprovalStatus(this).Any(static s => s.Value != PluginApprovalStatus.Approved);

        private bool isLoaded;

        public override bool CanBeDownloadedFromServer => false;
        public override int MaxAllowedInstances => 1;

        public IEnumerable<PluginAssemblyInfo> AssemblyInfos => MainAssemblyInfos.Concat(ExtraAssemblyInfos);

        public PluginAssemblyInfo MainPluginInfo => MainAssemblyInfos.Where(static p => p.ShoudLoad()).First();

        public readonly ImmutableArray<PluginAssemblyInfo> MainAssemblyInfos;
        public readonly ImmutableArray<PluginAssemblyInfo> ExtraAssemblyInfos;

        private readonly bool notSyncedInMultiplayer;

        public PluginInfoFile(ContentPackage contentPackage, ContentPath path)
            : base(contentPackage, path)
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc?.Root is null)
            {
                MainAssemblyInfos = ImmutableArray<PluginAssemblyInfo>.Empty;
                ExtraAssemblyInfos = ImmutableArray<PluginAssemblyInfo>.Empty;
                return;
            }

            ContentXElement rootElement = doc.Root.FromPackage(ContentPackage);

            MainAssemblyInfos = rootElement.Elements()
                .Where(static e => e.Name.LocalName.ToLowerInvariant() == "mainassemblyinfo")
                .Select(static e => new PluginAssemblyInfo(e)).ToImmutableArray();

            ExtraAssemblyInfos = rootElement.Elements()
                .Where(static e => e.Name.LocalName.ToLowerInvariant() == "extraassemblyinfo")
                .Select(static e => new PluginAssemblyInfo(e)).ToImmutableArray();

            if (rootElement.GetAttribute(nameof(notSyncedInMultiplayer)) == null)
            {
                // User has not explicitly specified if they want this to be synced in multiplayer or not,
                // assume that if they only defined server-side or only client-side plugins then it shouldn't be synced.
                notSyncedInMultiplayer =
                    !AssemblyInfos.Any(p => p.Targets.HasFlag(PluginTargets.Client)) ||
                    !AssemblyInfos.Any(p => p.Targets.HasFlag(PluginTargets.Server));
            }
            else
            {
                notSyncedInMultiplayer = rootElement.GetAttributeBool(nameof(notSyncedInMultiplayer), false);
            }

            VerificationResult = PluginLoader.CheckAssembliesSignature(contentPackage.Dir);
        }

        public override void LoadFile()
        {
            int mainPluginCount = MainAssemblyInfos.Count(static p => p.ShoudLoad());
            if (mainPluginCount == 0) { return; }
            if (mainPluginCount > 1)
            {
                throw new Exception("Loading multiple main plugin assemblies at the same time is not supported.");
            }

            var result = PluginLoader.TryLoadPlugin(this);
            if (result is not PluginLoadResult.Success)
            {
                DebugConsole.ThrowError($"Failed to load plugin \"{MainPluginInfo.AssemblyPath.Value}\": {result}", contentPackage: ContentPackage);
            }
            else
            {
                isLoaded = true;
            }
        }

        public void FinalizeLoad()
        {
            if (!isLoaded) { return; }

            PluginLoader.FinalizePluginLoad(this);

            var settingsService = PluginData.SettingsServices.FirstOrDefault(c => c.ContentPackage == ContentPackage);
            if (settingsService != null)
            {
                settingsService.Load();
            }
        }

        public override void Sort() { }

        public override void UnloadFile()
        {
            if (!isLoaded) { return; }

            PluginAssemblyInfo? pluginInfo = AssemblyInfos.FirstOrDefault(static p => p.ShoudLoad());

            if (pluginInfo == null)
            {
                isLoaded = false;
                return;
            }

            PluginLoader.UnloadPlugin(this);

            isLoaded = false;
        }
    }
}