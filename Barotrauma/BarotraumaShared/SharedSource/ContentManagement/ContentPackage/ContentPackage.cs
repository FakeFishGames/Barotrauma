#nullable enable
using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Steam;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Color = Microsoft.Xna.Framework.Color;

namespace Barotrauma
{
    public abstract class ContentPackage
    {
        public class Dependency
        {
            /// <summary>
            /// Determines where in the mod list the content package will be placed in relation to this dependency.
            /// Since load order is bottom-to-top, in other words, content packages placed on top have higher priority, 
            /// setting this to Above means the dependency will have a higher priority/load after the content package, 
            /// while having it on Below would have the opposite effect, with the dependency loading before the content package is loaded.
            /// 
            /// This is especially useful for content packages that override changes made by another package, as those overrides only work 
            /// correctly when the overriding package is placed above the original in the mod list.
            /// </summary>
            public enum RelativeOrder
            {
                None,
                Below,
                Above
            }

            /// <summary>
            /// An identifier that is unique to this content package and that is used when evaluating file conditionals to determine
            /// whether the corresponding dependency is present. It also allows other content to reference files within this 
            /// dependency using the %ModDir:xxx% syntax
            /// </summary>
            public readonly Identifier DependencyIdentifier;

            // Although more unusual, sometimes there's multiple copies of
            // the same mod on the workshop (forks), so let's allow mod authors
            // to specify multiple names and ids here so it can match either of them.

            public readonly ImmutableArray<Identifier> PackageNames;
            public readonly ImmutableArray<ContentPackageId> PackageIds;
            public readonly ImmutableArray<Steamworks.AppId> SteamAppIds;

            /// <summary>
            /// Optional dependencies don't throw an error if they are missing.
            /// </summary>
            public readonly bool IsOptional;

            /// <summary>
            /// Determines where in the mod list the content package wants to be in relation to the dependency. See <see cref="RelativeOrder"/> for more details.
            /// </summary>
            public readonly RelativeOrder Order;

            public Dependency(ContentPackage package, XElement element)
            {
                DependencyIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                PackageNames = element.GetAttributeIdentifierArray("names", null)?.ToImmutableArray() ?? [];
                var ids = element.GetAttributeStringArray("ids", null)?.ToImmutableArray() ?? [];
                PackageIds = ids.Select(ContentPackageId.Parse).NotNone().ToImmutableArray();

                var appIds = element.GetAttributeUIntArray(nameof(SteamAppIds), []);
                SteamAppIds = appIds.Select(static appId => (Steamworks.AppId)appId).ToImmutableArray();

                IsOptional = element.GetAttributeBool("isoptional", false);
                Order = element.GetAttributeEnum("order", RelativeOrder.None);
            }

            /// <summary>
            /// Tries to resolve this dependency from the given content packages
            /// </summary>
            public bool TryResolve(IEnumerable<ContentPackage> packages, [NotNullWhen(returnValue: true)]out ContentPackage? outPackage)
            {
                foreach (ContentPackage package in packages)
                {
                    if (Matches(package))
                    {
                        outPackage = package;
                        return true;
                    }
                }

                outPackage = null;
                return false;
            }

            public bool Matches(ContentPackage contentPackage)
            {
                if (contentPackage.SteamDlcAppId.TryUnwrap(out Steamworks.AppId appId) && SteamAppIds.Contains(appId))
                {
                    return true;
                }

                if (PackageNames.Any(contentPackage.NameMatches))
                {
                    return true;
                }

                if (contentPackage.UgcId.TryUnwrap(out ContentPackageId? packageId))
                {
                    if (PackageIds.Any(id => id.StringRepresentation == packageId.StringRepresentation))
                    {
                        return true;
                    }
                }

                return false;
            }

            public override string ToString()
            {
                if (!SteamAppIds.IsDefaultOrEmpty)
                {
#if CLIENT
                    DlcInfo? dlcInfo = DlcInfo.RetrieveDLCsFromCatalog().FirstOrNull(info => info.AppId == SteamAppIds.First());

                    if (dlcInfo != null)
                    {
                        return dlcInfo.Value.Name;
                    }
#endif

                    return SteamAppIds.First().ToString();
                }
                if (!PackageNames.IsDefaultOrEmpty)
                {
                    return PackageNames.First().Value;
                }
                else if (!PackageIds.IsDefaultOrEmpty)
                {
                    var packageId = PackageIds.First();

                    if (packageId is SteamWorkshopId workshopId)
                    {
                        return workshopId.ToWorkshopURL();
                    }
                    else
                    {
                        return packageId.StringRepresentation;
                    }
                }
                else
                {
                    return "Unknown dependency";
                }
            }

            public XElement ToXElement()
            {
                return new XElement("Dependency", 
                    new XAttribute("identifier", DependencyIdentifier),
                    new XAttribute("names", String.Join(",", PackageNames)),
                    new XAttribute("ids", String.Join(",", PackageIds)),
                    new XAttribute(nameof(SteamAppIds), String.Join(",", SteamAppIds)),
                    new XAttribute("isoptional", IsOptional.ToString()),
                    new XAttribute("order", Order.ToString()));
            }
        }

        public class FileConditional
        {
            private readonly ImmutableArray<Identifier> dependencyIdentifiers;
            private readonly PropertyConditional.LogicalOperatorType logicalOperator;

            public FileConditional(XElement element)
            {
                dependencyIdentifiers = element.GetAttributeIdentifierArray("dependencies", null)?.ToImmutableArray() ?? [];
                logicalOperator = element.GetAttributeEnum("operator", PropertyConditional.LogicalOperatorType.Or);
            }

            public XElement ToXElement()
            {
                return new XElement("Conditional", 
                    new XAttribute("dependencies", String.Join(",", dependencyIdentifiers)), 
                    new XAttribute("operator", logicalOperator.ToString()));
            }

            public bool Match(IEnumerable<Dependency> dependenciesPresent)
            {
                bool isPresent(Identifier identifier)
                {
                    return dependenciesPresent.Where(d => d.DependencyIdentifier == identifier).Any();
                }

                foreach (Identifier depId in dependencyIdentifiers)
                {
                    (PropertyConditional.ComparisonOperatorType comOp, string actualId) = PropertyConditional.ExtractComparisonOperatorFromConditionString(depId.Value);
                    bool shouldNegate = comOp == PropertyConditional.ComparisonOperatorType.NotEquals;
                    bool condition = isPresent(actualId.ToIdentifier()) ^ shouldNegate;

                    switch (logicalOperator)
                    {
                        case PropertyConditional.LogicalOperatorType.And:
                            if (!condition)
                            {
                                return false;
                            }
                            break;
                        case PropertyConditional.LogicalOperatorType.Or:
                            if (condition)
                            {
                                return true;
                            }
                            break;
                    }
                }

                // Return true if none of the ANDs failed, otherwise return false if none of the ORs were true
                return logicalOperator == PropertyConditional.LogicalOperatorType.And;
            }
        }

        /// <summary>
        /// A group of content files that can be conditionally loaded.
        /// </summary>
        public class FileGroup
        {
            public readonly ImmutableArray<ContentFile> Files;
            public readonly ImmutableArray<LoadError> FatalLoadErrors;
            public readonly ImmutableArray<FileConditional> Conditionals;

            public FileGroup(ContentPackage package, XElement element)
            {
                List<XElement> fileElements = new List<XElement>();
                List<XElement> conditionalElements = new List<XElement>();

                foreach (var child in element.Elements())
                {
                    switch (child.Name.LocalName)
                    {
                        case "Conditional":
                            conditionalElements.Add(child);
                            break;
                        default:
                            fileElements.Add(child);
                            break;
                    }
                }

                Conditionals = conditionalElements.Select(x => new FileConditional(x)).ToImmutableArray();

                var fileResults = fileElements
                    .Where(e => !ContentFile.IsLegacyContentType(e, package, logWarning: true))
                    .Select(e => ContentFile.CreateFromXElement(package, e))
                    .ToArray();

                Files = fileResults
                    .Successes()
                    .ToImmutableArray();

                FatalLoadErrors = fileResults
                    .Failures()
                    .ToImmutableArray();
            }

            public bool ShouldLoad(IEnumerable<Dependency> dependenciesPresent)
            {
                return Conditionals.All(c => c.Match(dependenciesPresent));
            }
        }

        public readonly record struct LoadError(string Message, Exception? Exception)
        {
            public override string ToString()
                => Message
                   + (Exception is { StackTrace: var stackTrace }
                       ? '\n' + stackTrace.CleanupStackTrace()
                       : string.Empty);
        }

        public static readonly Version MinimumHashCompatibleVersion = new Version(1, 1, 0, 0);

        public const string LocalModsDir = "LocalMods";
        public static readonly string DlcDir = GetDlcDir();

        /// <summary>
        /// Steam installs DLC depots relative to the game's install root. On macOS the game runs from inside
        /// the app bundle (Barotrauma.app/Contents/MacOS), so the DLC ends up next to Barotrauma.app, not inside it.
        /// </summary>
        private static string GetDlcDir()
        {
            const string dlcDirName = "DLC";
            if (!OperatingSystem.IsMacOS()) { return dlcDirName; }

            var macOsDir = new System.IO.DirectoryInfo(AppContext.BaseDirectory.TrimEnd('/', '\\'));
            var bundleDir = macOsDir.Parent?.Parent;
            if (macOsDir.Name != "MacOS"
                || bundleDir is null
                || !bundleDir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                || bundleDir.Parent is not { } installRoot)
            {
                // Not running from an app bundle (e.g. a dev build), use the same relative path as other platforms
                return dlcDirName;
            }

            return System.IO.Path.Combine(installRoot.FullName, dlcDirName);
        }

        public static readonly string WorkshopModsDir = Barotrauma.IO.Path.Combine(
            SaveUtil.DefaultSaveFolder,
            "WorkshopMods",
            "Installed");

        public const string FileListFileName = "filelist.xml";
        public const string DefaultModVersion = "1.0.0";

        public string Name { get; private set; }
        public readonly ImmutableArray<string> AltNames;
        public string Path { get; private set; }
        public string Dir => Barotrauma.IO.Path.GetDirectoryName(Path) ?? "";
        public readonly Option<ContentPackageId> UgcId;

        public readonly Option<ContentPath> ModIconPath;

        /// <summary>
        /// When set to true, package must be enabled from the start of a campaign.
        /// Loading a save that was created without it shows a warning.
        ///
        /// NOTE: This only works if the content package is marked as having multiplayer synced content.
        /// </summary>
        public readonly bool RequiresFreshSave;

        /// <summary>
        /// Accent color used to display the content package and e.g. subs and missions from it in UIs.
        /// Local mods and Workshop mods are forced to specific colors (otherwise they could make the UIs messy/unreadable and hard to tell what the colors mean).
        /// </summary>
        private readonly Color accentColor;
        public IEnumerable<PluginInfoFile> FilesThatShouldPromptWarning
            => AllFiles
               .OfType<PluginInfoFile>()
               .Where(static f => f.PromptWarning)
               .Select(static f => f);

        public readonly bool CheckSteamAppOwnership;
        public readonly Option<Steamworks.AppId> SteamDlcAppId;
        public readonly Version GameVersion;
        public readonly string ModVersion;

        public enum UgcStatus
        {
            NotFetched = 0,
            Fetching = 1,
            Fetched = 2,
            Unavailable = 3
        }

        public UgcStatus UgcItemStatus { get; private set; }

        /// <summary>
        /// Ugc item (workshop item) data that this content package corresponds to. Needs to be fetched with <see cref="TryFetchUgcItem(Action)"/>.
        /// You can also check <see cref="UgcItemStatus"/> to see if the item is available or not.
        /// </summary>
        public Option<Steamworks.Ugc.Item> UgcItem
        {
            get;
            private set;
        }

        public Md5Hash Hash { get; private set; }
        public readonly Option<SerializableDateTime> InstallTime;

        public ImmutableArray<ContentFile> Files { get; private set; }

        /// <summary>
        /// Returns every single file in this content package, including the ones in FileGroups that might not be loaded.
        /// </summary>
        public IEnumerable<ContentFile> AllFiles => Files.Concat(FileGroups.SelectMany(g => g.Files));

        /// <summary>
        /// Returns every single file that is currently active, that is, all files in this 
        /// content package and all files from every single file group that has all conditionals met.
        /// </summary>
        public IEnumerable<ContentFile> ActiveFiles => Files
            .Concat(FileGroups
            .Where(group => group.ShouldLoad(dependenciesPresent))
            .SelectMany(static dep => dep.Files));

        /// <summary>
        /// Errors that occurred when loading this content package.
        /// Currently, all errors are considered fatal and the game
        /// will refuse to load a content package that has any errors.
        /// </summary>
        public ImmutableArray<LoadError> FatalLoadErrors { get; private set; }

        /// <summary>
        /// An error that occurred when trying to enable this mod.
        /// This field doesn't directly affect whether or not this mod
        /// can be enabled, but if it's been set to anything other than
        /// Option.None then the game has already refused to enable it
        /// at least once.
        /// </summary>
        public Option<ContentPackageManager.LoadProgress.Error> EnableError { get; private set; }
            = Option.None;

        public readonly ImmutableArray<Dependency> Dependencies;
        public readonly ImmutableArray<FileGroup> FileGroups;

        private List<Dependency> dependenciesPresent = new List<Dependency>();
        public IEnumerable<Dependency> DependenciesPresent => dependenciesPresent;

        private readonly List<Dependency> missingDependencies = new List<Dependency>();
        /// <summary>
        /// An error caused by missing non-optional dependencies
        /// </summary>
        public IEnumerable<Dependency> MissingDependencies => missingDependencies;

        public bool HasAnyErrors => FatalLoadErrors.Length > 0 || EnableError.IsSome() || missingDependencies.Any();

        public async Task<bool> IsUpToDate()
        {
            if (!UgcId.TryUnwrap(out var ugcId)) { return true; }
            if (ugcId is not SteamWorkshopId steamWorkshopId) { return true; }
            if (!InstallTime.TryUnwrap(out var installTime)) { return true; }
            
            Option<Steamworks.Ugc.Item> itemOption = await SteamManager.Workshop.GetItem(steamWorkshopId.Value);
            if (!itemOption.TryUnwrap(out var item)) { return true; }
            return item.LatestUpdateTime <= installTime.ToUtcValue();
        }

        public int Index => ContentPackageManager.EnabledPackages.IndexOf(this);

        /// <summary>
        /// Does the content package include some content that needs to match between all players in multiplayer.
        /// </summary>
        public bool HasMultiplayerSyncedContent { get; }

        protected ContentPackage(XDocument doc, string path)
        {
            using var errorCatcher = DebugConsole.ErrorCatcher.Create();
            
            Path = path.CleanUpPathCrossPlatform();
            XElement rootElement = doc.Root ?? throw new NullReferenceException("XML document is invalid: root element is null.");

            Name = rootElement.GetAttributeString("name", "").Trim();
            AltNames = rootElement.GetAttributeStringArray("altnames", Array.Empty<string>())
                .Select(n => n.Trim()).ToImmutableArray();
            UInt64 steamWorkshopId = rootElement.GetAttributeUInt64("steamworkshopid", 0);

            if (Name.IsNullOrWhiteSpace() && AltNames.Any())
            {
                Name = AltNames.First();
            }

            UgcId = steamWorkshopId != 0
                ? Option<ContentPackageId>.Some(new SteamWorkshopId(steamWorkshopId))
                : Option<ContentPackageId>.None();

            GameVersion = rootElement.GetAttributeVersion("gameversion", GameMain.Version);
            ModVersion = rootElement.GetAttributeString("modversion", DefaultModVersion);
            InstallTime = rootElement.GetAttributeDateTime("installtime");

            ModIconPath = rootElement.GetAttributeContentPath("modicon", this) is { } iconPath
                ? Option.Some(iconPath)
                : Option.None;

            RequiresFreshSave = rootElement.GetAttributeBool("requiresfreshsave", false);

            accentColor = rootElement.GetAttributeColor("accentcolor", Color.MediumPurple);

            uint steamDlcAppId = rootElement.GetAttributeUInt("steamappid", 0);
            SteamDlcAppId = steamDlcAppId > 0
                ? Option.Some<Steamworks.AppId>(steamDlcAppId)
                : Option.None;

            CheckSteamAppOwnership = rootElement.GetAttributeBool("checksteamappownership", SteamDlcAppId.IsSome());

            SeparatePackageElements(rootElement, out var fileElements, out var groupElements, out var dependencyElements);

            Dependencies = dependencyElements.Select(element => new Dependency(this, element)).ToImmutableArray();
            FileGroups = groupElements.Select(element => new FileGroup(this, element)).ToImmutableArray();

            var fileResults = fileElements
                .Where(e => !ContentFile.IsLegacyContentType(e, this, logWarning: true))
                .Select(e => ContentFile.CreateFromXElement(this, e))
                .ToArray();

            Files = fileResults
                .Successes()
                .ToImmutableArray();

            FatalLoadErrors = fileResults
                .Failures()
                .Concat(FileGroups.SelectMany(d => d.FatalLoadErrors))
                .ToImmutableArray();

            AssertCondition(!string.IsNullOrEmpty(Name), $"{nameof(Name)} is null or empty");

            HasMultiplayerSyncedContent = AllFiles.Any(static f => !f.NotSyncedInMultiplayer);

            Dictionary<ContentFile, int> fileCounts = AllFiles
                                                      .GroupBy(static f => f)
                                                      .ToDictionary(static g => g.Key,
                                                                    static g => g.Count());

            foreach (var (file, count) in fileCounts)
            {
                if (count > file.MaxAllowedInstances)
                {
                    FatalLoadErrors = FatalLoadErrors.Add(
                        new LoadError(
                            Message: $"Content type {file.GetType().Name} is present {count} times, but only {file.MaxAllowedInstances} instances are allowed.",
                            Exception: null
                        ));
                }
            }

            Hash = CalculateHash();
            var expectedHash = rootElement.GetAttributeString("expectedhash", "");
            if (HashMismatches(expectedHash))
            {
                FatalLoadErrors = FatalLoadErrors.Add(
                    new LoadError(
                        Message: $"Hash calculation returned {Hash.StringRepresentation}, expected {expectedHash}",
                        Exception: null
                    ));
            }

            FatalLoadErrors = FatalLoadErrors
                .Concat(errorCatcher.Errors.Select(err => new LoadError(err.Text, null)))
                .ToImmutableArray();
        }

        public bool HashMismatches(string expectedHash)
            => GameVersion >= MinimumHashCompatibleVersion &&
               !expectedHash.IsNullOrWhiteSpace() &&
               !expectedHash.Equals(Hash.StringRepresentation, StringComparison.OrdinalIgnoreCase);

        public IEnumerable<T> GetFiles<T>() where T : ContentFile => ActiveFiles.OfType<T>();

        public IEnumerable<ContentFile> GetFiles(Type type)
            => !type.IsSubclassOf(typeof(ContentFile))
                ? throw new ArgumentException($"Type must be subclass of ContentFile, got {type.Name}")
                : ActiveFiles.Where(f => f.GetType() == type || f.GetType().IsSubclassOf(type));

        public bool NameMatches(Identifier name)
            => Name == name || AltNames.Any(n => n == name);

        public bool NameMatches(string name)
            => NameMatches(name.ToIdentifier());
        
        public static Result<ContentPackage, Exception> TryLoad(string path)
        {
            var (success, failure) = Result<ContentPackage, Exception>.GetFactoryMethods();
            
            XDocument doc = XMLExtensions.TryLoadXml(path);

            try
            {
                ContentPackage contentPackage = doc.Root.GetAttributeBool("corepackage", false)
                    ? new CorePackage(doc, path)
                    : new RegularPackage(doc, path);
                
                if (System.IO.Path.GetFileNameWithoutExtension(path)?.Any(char.IsUpper) is true)
                {
                    DebugConsole.ThrowError($"Invalid filename casing. Please rename \"filelist.xml\" so it is entirely lowercase.", contentPackage: contentPackage);
                }

                return success(contentPackage);
            }
            catch (Exception e)
            {
                return failure(e.GetInnermost());
            }
        }

        public Md5Hash CalculateHash(bool logging = false, string? name = null, string? modVersion = null)
        {
            using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            
            if (logging)
            {
                DebugConsole.NewMessage("****************************** Calculating content package hash " + Name);
            }

            foreach (ContentFile file in AllFiles)
            {
                try
                {
                    var hash = file.Hash;
                    if (logging)
                    {
                        DebugConsole.NewMessage("   " + file.Path + ": " + hash.StringRepresentation);
                    }                    
                    incrementalHash.AppendData(hash.ByteRepresentation);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error while calculating the MD5 hash of the content package \"{Name}\" (file path: {Path}). The content package may be corrupted. You may want to delete or reinstall the package.", e);
                    break;
                }             
            }

            string selectedName = name ?? Name;
            if (!selectedName.IsNullOrEmpty())
            {
                incrementalHash.AppendData(Encoding.UTF8.GetBytes(selectedName));
            }
            incrementalHash.AppendData(Encoding.UTF8.GetBytes(modVersion ?? ModVersion));

            var md5Hash = Md5Hash.BytesAsHash(incrementalHash.GetHashAndReset());
            if (logging)
            {
                DebugConsole.NewMessage("****************************** Package hash: " + md5Hash.StringRepresentation);
            }

            return md5Hash;
        }

        protected void AssertCondition(bool condition, string errorMsg)
        {
            if (!condition)
            {
                FatalLoadErrors = FatalLoadErrors.Add(new LoadError(errorMsg, null));
            }
        }

        private static void SeparatePackageElements(XElement rootElement, out IReadOnlyList<XElement> fileElements, out IReadOnlyList<XElement> groupElements, out IReadOnlyList<XElement> dependencyElements)
        {
            var files = new List<XElement>();
            var groups = new List<XElement>();
            var dependencies = new List<XElement>();

            foreach (var child in rootElement.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "Dependency":
                        dependencies.Add(child);
                        break;
                    case "FileGroup":
                        groups.Add(child);
                        break;
                    default:
                        files.Add(child);
                        break;
                }
            }

            fileElements = files;
            groupElements = groups;
            dependencyElements = dependencies;
        }

        /// <summary>
        /// Populates the list of missing dependencies and dependencies present, 
        /// returns false if there was no change in dependencies
        /// </summary>
        public bool CheckDependencies(IReadOnlyList<ContentPackage> packages)
        {
            List<Dependency> prevDeps = dependenciesPresent.ToList();
            List<Dependency> prevMissing = missingDependencies.ToList();

            dependenciesPresent.Clear();
            missingDependencies.Clear();
            foreach (Dependency dependency in Dependencies)
            {
                if (dependency.TryResolve(packages, out var package))
                {
                    dependenciesPresent.Add(dependency);
                }
                else if (!dependency.IsOptional)
                {
                    missingDependencies.Add(dependency);
                }
            }

            return !prevDeps.SequenceEqual(dependenciesPresent) || !prevDeps.SequenceEqual(missingDependencies);
        }

        public void LoadFilesOfType<T>() where T : ContentFile
        {
            ActiveFiles.Where(static f => f is T).ForEach(static f => f.LoadFile());
        }

        public void UnloadFilesOfType<T>() where T : ContentFile
        {
            ActiveFiles.Where(static f => f is T).ForEach(f => f.UnloadFile());
        }

        public enum LoadResult
        {
            Success,
            Failure
        }

        public LoadResult LoadContent()
        {
            foreach (var p in LoadContentEnumerable())
            {
                if (p.Result.IsFailure) { return LoadResult.Failure; }
            }
            return LoadResult.Success;
        }
        
        public IEnumerable<ContentPackageManager.LoadProgress> LoadContentEnumerable()
        {
            DebugConsole.Log($"Loading content package {Name}");

            using var errorCatcher = DebugConsole.ErrorCatcher.Create();

            ContentFile[] getFilesToLoad(Predicate<ContentFile> predicate)
                => Files
                        // Conditionally load file groups if the dependency is found on the enabled packages.
                        // Also, hi Juan! - Evil
                        .Concat(FileGroups.Where(group => group.ShouldLoad(dependenciesPresent)).SelectMany(static dep => dep.Files))
                        .Where(predicate.Invoke)
#if DEBUG
                        //The game should be able to work just fine with a completely arbitrary file load order.
                        //To make sure we don't mess this up, debug builds randomize it so it has a higher chance
                        //of breaking anything that's not implemented correctly.
                        .Randomize(Rand.RandSync.Unsynced)
#endif
                        // Plugins should be loaded first since they might register new content types.
                        // I'm sorry Juan, I've failed you - Markus
                        .OrderBy(static f => f is PluginInfoFile ? 0 : 1).ToArray();

            IEnumerable<ContentPackageManager.LoadProgress> loadFiles(ContentFile[] filesToLoad, int indexOffset)
            {
                using Janitor janitor = Janitor.Start();
                janitor.AddAction(FinalizePluginFileLoad);

                for (int i = 0; i < filesToLoad.Length; i++)
                {
                    Exception? exception = null;

                    try
                    {
                        //do not allow exceptions thrown here to crash the game
                        filesToLoad[i].LoadFile();
                    }
                    catch (Exception e)
                    {
                        var innermost = e.GetInnermost();
                        DebugConsole.LogError($"Failed to load \"{filesToLoad[i].Path}\": {innermost.Message}\n{innermost.StackTrace}", contentPackage: this);
                        exception = e;
                    }
                    if (exception != null)
                    {
                        yield return ContentPackageManager.LoadProgress.Failure(exception);
                        yield break;
                    }

                    if (errorCatcher.Errors.Any())
                    {
                        yield return ContentPackageManager.LoadProgress.Failure(errorCatcher.Errors.Select(e => e.Text));
                        yield break;
                    }
                    yield return ContentPackageManager.LoadProgress.Progress((i + indexOffset) / (float)Files.Length);
                }
                yield break;

                void FinalizePluginFileLoad()
                    => filesToLoad
                       .OfType<PluginInfoFile>()
                       .ForEach(static f => f.FinalizeLoad());
            }

            //Load the UI and text files first. This is to allow the game
            //to render the text in the loading screen as soon as possible.
            var priorityFiles = getFilesToLoad(f => f is UIStyleFile or TextFile);

            var remainder = getFilesToLoad(f => !priorityFiles.Contains(f));

            var loadEnumerable =
                loadFiles(priorityFiles, 0)
                    .Concat(loadFiles(remainder, priorityFiles.Length));

            foreach (var p in loadEnumerable)
            {
                if (p.Result.TryUnwrapFailure(out var failure))
                {
                    errorCatcher.Dispose();
                    UnloadContent();
                    EnableError = Option.Some(failure);
                    yield return p;
                    yield break;
                }
                yield return p;
            }
            errorCatcher.Dispose();
        }

        public void TryFetchUgcDescription(Action<string?> onFinished)
        {
            TryFetchUgcItem((Steamworks.Ugc.Item? item) =>
            {
                onFinished?.Invoke(item?.Description ?? string.Empty);
            });
        }

        public void TryFetchUgcChildren(Action<PublishedFileId[]?> onFinished)
        {
            TryFetchUgcItem((Steamworks.Ugc.Item? item) =>
            {
                onFinished?.Invoke(item?.Children ?? Array.Empty<PublishedFileId>());
            });
        }

        private void TryFetchUgcItem(Action<Steamworks.Ugc.Item?> onFinished)
        {
            switch (UgcItemStatus)
            {
                case UgcStatus.NotFetched:
                    TryFetchUgcItem(onFinished: () =>
                    {
                        if (UgcItemStatus == UgcStatus.Fetched &&
                            UgcItem.TryUnwrap(out var cachedItem))
                        {
                            onFinished?.Invoke(cachedItem);
                        }
                    });
                    break;
                case UgcStatus.Fetched when UgcItem.TryUnwrap(out var cachedItem):
                    onFinished?.Invoke(cachedItem);
                    break;
                default:
                    onFinished?.Invoke(null);
                    break;
            }
        }

        /// <summary>
        /// Attempts to fetch the UgcItem (workshop item) data from Steamworks, and if successful, caches it in <see cref="UgcItem"/>.
        /// </summary>
        /// <param name="onFinished">Triggers when the query finishes or fails (or immediately if the item has been already cached)</param>
        public void TryFetchUgcItem(Action onFinished)
        {
            if (UgcItemStatus != UgcStatus.NotFetched)
            {
                onFinished?.Invoke();
                return;
            }
            if (!UgcId.TryUnwrap(out var ugcId) || ugcId is not SteamWorkshopId workshopId) 
            {
                UgcItemStatus = UgcStatus.Unavailable;
                return;
            }
            
            UgcItemStatus = UgcStatus.Fetching;
            TaskPool.Add($"PrepareToShow{UgcId}Info", SteamManager.Workshop.GetItem(workshopId.Value),
                task =>
                {
                    if (!task.TryGetResult(out Option<Steamworks.Ugc.Item> itemOption) || !itemOption.TryUnwrap(out var item))
                    {
                        UgcItemStatus = UgcStatus.Unavailable;
                        return;
                    }
                    UgcItem = Option<Steamworks.Ugc.Item>.Some(item);
                    UgcItemStatus = UgcStatus.Fetched;
                    onFinished?.Invoke();
                });
        }

        public void UnloadContent()
        {
            AllFiles
                // Plugins should be unloaded last so content they implement can be unloaded.
                // Otherwise, we would unload the implementation to unload the content type.
                .OrderBy(static f => f is PluginInfoFile ? 1 : 0)
                .ForEach(static f => f.UnloadFile());
        }

        public void ReloadSubsAndItemAssemblies()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            List<ContentFile> newFileList = new List<ContentFile>();
            XElement rootElement = doc.Root ?? throw new NullReferenceException("XML document is invalid: root element is null.");

            SeparatePackageElements(rootElement, out var fileElements, out var groupElements, out var dependencyElements);

            var fileResults = fileElements
                .Where(e => !ContentFile.IsLegacyContentType(e, this, logWarning: true))
                .Select(e => ContentFile.CreateFromXElement(this, e))
                .ToArray();

            foreach (var file in fileResults.Successes())
            {
                if (file is BaseSubFile or ItemAssemblyFile)
                {
                    newFileList.Add(file);
                }
                else
                {
                    var existingFile = Files.FirstOrDefault(f => f.Path == file.Path);
                    newFileList.Add(existingFile ?? file);
                }
            }

            UnloadFilesOfType<BaseSubFile>();
            UnloadFilesOfType<ItemAssemblyFile>();
            Files = newFileList.ToImmutableArray();
            Hash = CalculateHash();
            LoadFilesOfType<BaseSubFile>();
            LoadFilesOfType<ItemAssemblyFile>();
        }

        public static bool PathAllowedAsLocalModFile(string path)
        {
#if DEBUG
            if (GameMain.VanillaContent.Files.Any(f => f.Path == path))
            {
                //file is in vanilla package, this is allowed
                return true;
            }
#endif

            while (true)
            {
                string temp = Barotrauma.IO.Path.GetDirectoryName(path) ?? "";
                if (string.IsNullOrEmpty(temp)) { break; }
                path = temp;
            }
            return path == LocalModsDir;
        }

        public void LogErrors()
        {
            if (!FatalLoadErrors.Any() && !missingDependencies.Any())
            {
                return;
            }

            var errors = FatalLoadErrors.Select(errorToStr).Concat(missingDependencies.Select(depMissingToStr));

            DebugConsole.AddWarning(
                $"The following errors occurred while loading the content package \"{Name}\". The package might not work correctly.\n" +
                string.Join('\n', errors),
                this);

            static string errorToStr(LoadError error)
                => error.ToString();

            static string depMissingToStr(Dependency dependency)
                => $"Missing dependency {dependency.ToString()}";
        }

        public bool TryRenameLocal(string newName)
        {
            if (!ContentPackageManager.LocalPackages.Contains(this)) { return false; }

            if (newName.IsNullOrWhiteSpace())
            {
                DebugConsole.ThrowError($"New name is blank!");
                return false;
            }

            string newDir = IO.Path.Combine(IO.Path.GetFullPath(LocalModsDir), File.SanitizeName(newName));
            if (ContentPackageManager.LocalPackages.Any(lp => lp.NameMatches(newName)) || Directory.Exists(newDir))
            {
                DebugConsole.ThrowError($"A local package with the name or directory \"{newName}\" already exists!");
                return false;
            }

            XDocument doc = XMLExtensions.TryLoadXml(Path);
            doc.Root!.SetAttributeValue("name", newName);
            using (IO.XmlWriter writer = IO.XmlWriter.Create(Path, new XmlWriterSettings { Indent = true }))
            {
                doc.WriteTo(writer);
                writer.Flush();
            }

            Directory.Move(Dir, newDir);
            return true;
        }

        public bool TryDeleteLocal() => ContentPackageManager.LocalPackages.Contains(this) && Directory.TryDelete(Dir);

        public bool TryCreateLocalFromWorkshop()
        {
            if (!ContentPackageManager.WorkshopPackages.Contains(this)) { return false; }

            string newDir = IO.Path.Combine(IO.Path.GetFullPath(LocalModsDir), File.SanitizeName(Name));
            if (ContentPackageManager.LocalPackages.Any(lp => lp.NameMatches(Name)) || Directory.Exists(newDir))
            {
                DebugConsole.ThrowError($"A local package with the name or directory \"{Name}\" already exists!");
                return false;
            }

            Directory.Copy(Dir, newDir);
            return true;
        }

        public void AddError(string message)
            => FatalLoadErrors = FatalLoadErrors.Add(new LoadError(message, null));

#if CLIENT
        private static Color LocalPackageColor => GUIStyle.TextColorBright;
#else
        private static Color LocalPackageColor => Color.White;
#endif
        private static Color WorkshopPackageColor => Color.MediumPurple;

        public static Color? GetAccentColorForEnabledPackages(IEnumerable<ContentPackage> enabledPackages)
        {
            bool anyWorkshop = false, anyLocal = false;
            foreach (ContentPackage package in enabledPackages)
            {
                if (ContentPackageManager.DlcPackages.Contains(package)) { return package.accentColor; }
                if (!anyWorkshop && ContentPackageManager.WorkshopPackages.Contains(package)) { anyWorkshop = true; }
                if (!anyLocal && ContentPackageManager.LocalPackages.Contains(package)) { anyLocal = true; }
            }
            return anyWorkshop ? WorkshopPackageColor : anyLocal ? LocalPackageColor : null;
        }

        /// <summary>
        /// Accent color used to display the content package and e.g. subs and missions from it in UIs.
        /// </summary>
        public Color GetAccentColor()
        {
            if (ContentPackageManager.LocalPackages.Contains(this)) { return LocalPackageColor; }
            if (ContentPackageManager.WorkshopPackages.Contains(this)) { return WorkshopPackageColor; }
            return accentColor;
        }

        /// <summary>
        /// Accent color used to display the content package and e.g. subs and missions from it in UIs.
        /// </summary>
        public static Color GetAccentColor(ContentPackage? package)
        {
            return package?.GetAccentColor() ?? LocalPackageColor;
        }

        public static LocalizedString? GetToolTip(ContentPackage? package)
        {
            if (package == null) { return $"‖color:GUI.Red‖{TextManager.Get("UnknownPackage")}‖end‖"; }
            if (package == ContentPackageManager.VanillaCorePackage) { return null; }
            return $"‖color:{GetAccentColor(package).ToStringHex()}‖{package.Name}‖end‖";
        }
    }
}