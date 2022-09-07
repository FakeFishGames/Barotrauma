#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{
    public abstract class ContentPackage
    {
        public static readonly Version MinimumHashCompatibleVersion = new Version(0, 18, 13, 0);
        
        public const string LocalModsDir = "LocalMods";
        public static readonly string WorkshopModsDir = Barotrauma.IO.Path.Combine(
            SaveUtil.SaveFolder,
            "WorkshopMods",
            "Installed");

        public const string FileListFileName = "filelist.xml";
        public const string DefaultModVersion = "1.0.0";

        public readonly string Name;
        public readonly ImmutableArray<string> AltNames;
        public readonly string Path;
        public string Dir => Barotrauma.IO.Path.GetDirectoryName(Path) ?? "";
        public readonly UInt64 SteamWorkshopId;

        public readonly Version GameVersion;
        public readonly string ModVersion;
        public Md5Hash Hash { get; private set; }
        public readonly DateTime? InstallTime;

        public ImmutableArray<ContentFile> Files { get; private set; }
        public ImmutableArray<ContentFile.LoadError> Errors { get; private set; }

        public async Task<bool> IsUpToDate()
        {
            if (SteamWorkshopId != 0 && InstallTime.HasValue)
            {
                Steamworks.Ugc.Item? item = await SteamManager.Workshop.GetItem(SteamWorkshopId);
                if (item is null) { return true; }
                return item.Value.LatestUpdateTime <= InstallTime;
            }
            return true;
        }

        public int Index => ContentPackageManager.EnabledPackages.IndexOf(this);

        /// <summary>
        /// Does the content package include some content that needs to match between all players in multiplayer. 
        /// </summary>
        public bool HasMultiplayerSyncedContent { get; private set; }

        protected ContentPackage(XDocument doc, string path)
        {
            Path = path.CleanUpPathCrossPlatform();
            XElement rootElement = doc.Root ?? throw new NullReferenceException("XML document is invalid: root element is null.");

            Name = rootElement.GetAttributeString("name", "").Trim();
            AltNames = rootElement.GetAttributeStringArray("altnames", Array.Empty<string>())
                .Select(n => n.Trim()).ToImmutableArray();
            AssertCondition(!string.IsNullOrEmpty(Name), "Name is null or empty");
            SteamWorkshopId = rootElement.GetAttributeUInt64("steamworkshopid", 0);

            GameVersion = rootElement.GetAttributeVersion("gameversion", GameMain.Version);
            ModVersion = rootElement.GetAttributeString("modversion", DefaultModVersion);
            if (rootElement.Attribute("installtime") != null)
            {
                InstallTime = ToolBox.Epoch.ToDateTime(rootElement.GetAttributeUInt("installtime", 0));
            }
            else
            {
                InstallTime = null;
            }

            var fileResults = rootElement.Elements()
                .Select(e => ContentFile.CreateFromXElement(this, e))
                .ToArray();

            Files = fileResults
                .OfType<Success<ContentFile, ContentFile.LoadError>>()
                .Select(f => f.Value)
                .ToImmutableArray();

            Errors = fileResults
                .OfType<Failure<ContentFile, ContentFile.LoadError>>()
                .Select(f => f.Error)
                .ToImmutableArray();

            HasMultiplayerSyncedContent = Files.Any(f => !f.NotSyncedInMultiplayer);

            Hash = CalculateHash();
            var expectedHash = rootElement.GetAttributeString("expectedhash", "");
            if (HashMismatches(expectedHash))
            {
                DebugConsole.ThrowError($"Hash calculation for content package \"{Name}\" didn't match expected hash ({Hash.StringRepresentation} != {expectedHash})");
            }
        }

        public bool HashMismatches(string expectedHash)
            => GameVersion >= MinimumHashCompatibleVersion &&
               !expectedHash.IsNullOrWhiteSpace() &&
               !expectedHash.Equals(Hash.StringRepresentation, StringComparison.OrdinalIgnoreCase);

        public IEnumerable<T> GetFiles<T>() where T : ContentFile => Files.OfType<T>();

        public IEnumerable<ContentFile> GetFiles(Type type)
            => !type.IsSubclassOf(typeof(ContentFile))
                ? throw new ArgumentException($"Type must be subclass of ContentFile, got {type.Name}")
                : Files.Where(f => f.GetType() == type || f.GetType().IsSubclassOf(type));

        public bool NameMatches(Identifier name)
            => Name == name || AltNames.Any(n => n == name);

        public bool NameMatches(string name)
            => NameMatches(name.ToIdentifier());
        
        public bool StringMatches(string workshop_id_or_name)
            => (UInt64.TryParse(workshop_id_or_name, out UInt64 res)&& (res != 0 && res == SteamWorkshopId)) || NameMatches(workshop_id_or_name);

        public static ContentPackage? TryLoad(string path)
        {
            XDocument doc = XMLExtensions.TryLoadXml(path);

            try
            {
                return doc.Root.GetAttributeBool("corepackage", false)
                    ? (ContentPackage)new CorePackage(doc, path)
                    : new RegularPackage(doc, path);
            }
            catch (Exception e)
            {
                e = e.GetInnermost();
                DebugConsole.ThrowError($"{e.Message}: {e.StackTrace}");
                return null;
            }
        }

        public Md5Hash CalculateHash(bool logging = false)
        {
            using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            
            if (logging)
            {
                DebugConsole.NewMessage("****************************** Calculating content package hash " + Name);
            }

            foreach (ContentFile file in Files)
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
                throw new InvalidOperationException($"Failed to load \"{Name}\" at {Path}: {errorMsg}");
            }
        }

        public void LoadFilesOfType<T>() where T : ContentFile
        {
            Files.Where(f => f is T).ForEach(f => f.LoadFile());
        }

        public void UnloadFilesOfType<T>() where T : ContentFile
        {
            Files.Where(f => f is T).ForEach(f => f.UnloadFile());
        }

        public enum LoadResult
        {
            Success,
            Failure
        }

        public LoadResult LoadPackage()
        {
            foreach (var p in LoadPackageEnumerable())
            {
                if (p.Exception != null) { return LoadResult.Failure; }
            }
            return LoadResult.Success;
        }
        
        public IEnumerable<ContentPackageManager.LoadProgress> LoadPackageEnumerable()
        {
            ContentFile[] getFilesToLoad(Predicate<ContentFile> predicate)
                => Files.Where(predicate.Invoke).ToArray()
#if DEBUG
                        //The game should be able to work just fine with a completely arbitrary file load order.
                        //To make sure we don't mess this up, debug builds randomize it so it has a higher chance
                        //of breaking anything that's not implemented correctly.
                        .Randomize()
#endif
                    ;

            IEnumerable<ContentPackageManager.LoadProgress> loadFiles(ContentFile[] filesToLoad, int indexOffset)
            {
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
                        exception = e;
                    }
                    if (exception != null)
                    {
                        yield return ContentPackageManager.LoadProgress.Failure(exception);
                        break;
                    }
                    yield return new ContentPackageManager.LoadProgress((i + indexOffset) / (float)Files.Length);
                }
            }

            //Load the UI and text files first. This is to allow the game
            //to render the text in the loading screen as soon as possible.
            var priorityFiles = getFilesToLoad(f => f is UIStyleFile || f is TextFile);

            var remainder = getFilesToLoad(f => !priorityFiles.Contains(f));

            var loadEnumerable =
                loadFiles(priorityFiles, 0)
                    .Concat(loadFiles(remainder, priorityFiles.Length));

            foreach (var p in loadEnumerable)
            {
                if (p.Exception != null)
                {
                    HandleLoadException(p.Exception);
                    yield return p;
                    break;
                }
                yield return p;
            }
        }

        protected abstract void HandleLoadException(Exception e);

        public void UnloadPackage()
        {
            Files.ForEach(f => f.UnloadFile());
        }

        public void ReloadSubsAndItemAssemblies()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            List<ContentFile> newFileList = new List<ContentFile>();
            XElement rootElement = doc.Root ?? throw new NullReferenceException("XML document is invalid: root element is null.");
            
            var fileResults = rootElement.Elements()
                .Select(e => ContentFile.CreateFromXElement(this, e))
                .ToArray();

            foreach (var result in fileResults)
            {
                switch (result)
                {
                    case Success<ContentFile, ContentFile.LoadError> { Value: var file }:
                        if (file is BaseSubFile || file is ItemAssemblyFile)
                        {
                            newFileList.Add(file);
                        }
                        else
                        {
                            var existingFile = Files.FirstOrDefault(f => f.Path == file.Path);
                            newFileList.Add(existingFile ?? file);
                        }
                        break;
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
            if (!Errors.Any())
            {
                return;
            }

            DebugConsole.AddWarning(
                $"The following errors occurred while loading the content package \"{Name}\". The package might not work correctly.\n" +
                string.Join('\n', Errors.Select(errorToStr)));

            static string errorToStr(ContentFile.LoadError error)
                => error.ToString();
        }
    }
}