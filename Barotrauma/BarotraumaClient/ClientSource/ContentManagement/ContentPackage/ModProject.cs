#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    /// <summary>
    /// Represents a mod project and provides functionality for constructing and saving content packages at runtime.
    /// </summary>
    public class ModProject
    {
        /// <summary>
        /// A mod project file that can be saved, corresponding to a &lt;ContentFile&gt; element.
        /// </summary>
        public class File
        {
            private File(string path, Type type)
            {
                Path = path.CleanUpPathCrossPlatform(correctFilenameCase: false);
                Type = type switch
                {
                    _ when !type.IsSubclassOf(typeof(ContentFile)) => throw new ArgumentException($"{type.Name} does not derive from {nameof(ContentFile)}"),
                    { IsAbstract: true } => throw new ArgumentException($"{type.Name} is abstract"),
                    _ => type
                };
                ContentName = type.Name.RemoveFromEnd("File");
            }

            private File(ContentFile f)
            {
                Path = f.Path.RawValue ?? "";
                Type = f.GetType();
                ContentName = f switch
                {
                    DynamicallyLoadedFile dlf => dlf.ContentName,
                    _                         => Type.Name.RemoveFromEnd("File")
                };
            }

            public static File FromContentFile(ContentFile file)
                => new File(file);

            public static File FromPath<T>(string path) where T : ContentFile
                => new File(path, typeof(T));

            /// <summary>
            /// Prefer FromPath&lt;T&gt; when possible, this just exists
            /// for cases where the type can only be decided at runtime
            /// </summary>
            public static File FromPath(string path, Type type)
                => new File(path, type);

            public readonly string Path;
            public readonly Type Type;
            public readonly string ContentName;

            public bool IsSameFile(File otherFile)
            {
                return string.Equals(Path, otherFile.Path, StringComparison.OrdinalIgnoreCase) && Type == otherFile.Type;
            }

            public XElement ToXElement()
            {
                if (Type is null) { throw new InvalidOperationException("Type must be set before calling ToXElement"); }
                if (Path.IsNullOrEmpty()) { throw new InvalidOperationException("Path must be set before calling ToXElement"); }
                return new XElement(ContentName, new XAttribute("file", Path));
            }
        }

        /// <summary>
        /// Represents a group of files and conditionals within a mod project, corresponding to a &gt;FileGroup&lt; element.
        /// </summary>
        public class Group
        {
            public readonly List<File> Files = new List<File>();
            public readonly ImmutableArray<ContentPackage.FileConditional> Conditionals;

            private Group(ContentPackage.FileGroup fileGroup)
            {
                Files = fileGroup.Files.Select(File.FromContentFile).ToList();
                Conditionals = fileGroup.Conditionals.ToImmutableArray();
            }

            public static Group FromFileGroup(ContentPackage.FileGroup file)
                => new Group(file);

            public XElement ToXElement()
            {
                XElement element = new XElement("FileGroup");
                Conditionals.ForEach(c => element.Add(c.ToXElement()));
                Files.ForEach(f => element.Add(f.ToXElement()));
                return element;
            }
        }

        public ModProject() { }

        public ModProject(ContentPackage? contentPackage)
        {
            if (contentPackage is null) { return; }
            Name = contentPackage.Name;
            AltNames = contentPackage.AltNames.ToList();
            dependencies = contentPackage.Dependencies.ToList();
            files = contentPackage.Files.Select(File.FromContentFile).ToList();
            groups = contentPackage.FileGroups.Select(Group.FromFileGroup).ToList();
            ModVersion = IncrementModVersion(contentPackage.ModVersion);
            IsCore = contentPackage is CorePackage;
            UgcId = contentPackage.UgcId;
            ExpectedHash = contentPackage.Hash;
            InstallTime = contentPackage.InstallTime;
            AccentColor = contentPackage.GetAccentColor();
            SteamDlcAppId = contentPackage.SteamDlcAppId;
        }

        private string name = "";
        public string Name
        {
            get => name;
            set
            {
                var charsToRemove = Path.GetInvalidFileNameCharsCrossPlatform();
                name = string.Concat(value.Where(c => !charsToRemove.Contains(c)));
            }
        }

        public readonly List<string> AltNames = new List<string>();

        private readonly List<ContentPackage.Dependency> dependencies = new List<ContentPackage.Dependency>();

        private readonly List<Group> groups = new List<Group>();

        private readonly List<File> files = new List<File>();
        public IReadOnlyList<File> Files => files;
        public IReadOnlyList<Group> Groups => groups;
        public IEnumerable<File> AllFiles => files.Concat(groups.SelectMany(g => g.Files));

        public string ModVersion = ContentPackage.DefaultModVersion;

        public Md5Hash? ExpectedHash { get; set; }

        public bool IsCore = false;

        public Option<Steamworks.AppId> SteamDlcAppId = Option<Steamworks.AppId>.None();

        public Option<ContentPackageId> UgcId = Option<ContentPackageId>.None();

        public Option<SerializableDateTime> InstallTime = Option<SerializableDateTime>.None();

        public Color AccentColor;

        public bool HasFile(File file)
            => AllFiles.Any(f => f.IsSameFile(file));

        public Group? FindFileGroup(File file)
            => groups.Where(g => g.Files.Any(f => f.IsSameFile(file))).FirstOrDefault();

        public void AddFile(File file, Group? group = null)
        {
            if (HasFile(file)) { return; }

            if (group != null)
            {
                group.Files.Add(file);
            }
            else
            {
                files.Add(file);
            }
            DiscardHashAndInstallTime();
        }

        public void RemoveFile(File file)
        {
            if (!HasFile(file)) { return; }

            Group? group = FindFileGroup(file);
            if (group  == null)
            {
                files.Remove(file);
            }
            else
            {
                group.Files.Remove(file);
            }

            DiscardHashAndInstallTime();
        }

        public void DiscardHashAndInstallTime()
        {
            ExpectedHash = null;
            InstallTime = Option<SerializableDateTime>.None();
        }
        
        public static string IncrementModVersion(string modVersion)
        {
            if (string.IsNullOrWhiteSpace(modVersion)) { return string.Empty; }

            //look for an integer at the end of the string and increment it
            int startIndex = modVersion.Length - 1;
            while (startIndex > 0 && char.IsDigit(modVersion[startIndex])) { startIndex--; }
            startIndex++;

            if (startIndex >= modVersion.Length
                || !char.IsDigit(modVersion[startIndex])
                || !int.TryParse(
                    modVersion[startIndex..],
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out int theFinalInteger))
            {
                return modVersion;
            }

            return $"{modVersion[..startIndex]}{(theFinalInteger + 1).ToString(CultureInfo.InvariantCulture)}";
        }

        public XDocument ToXDocument()
        {
            XDocument doc = new XDocument();
            XElement rootElement = new XElement("contentpackage");

            void addRootAttribute<T>(string name, T value) where T : notnull
                => rootElement.Add(new XAttribute(name, value.ToString() ?? ""));
            
            addRootAttribute("name", Name);
            if (!ModVersion.IsNullOrEmpty()) { addRootAttribute("modversion", ModVersion); }
            addRootAttribute("corepackage", IsCore);
            if (UgcId.TryUnwrap(out var ugcId) && ugcId is SteamWorkshopId steamWorkshopId) { addRootAttribute("steamworkshopid", steamWorkshopId.Value); }
            addRootAttribute("gameversion", GameMain.Version);
            if (AltNames.Any()) { addRootAttribute("altnames", string.Join(",", AltNames)); }
            if (ExpectedHash != null) { addRootAttribute("expectedhash", ExpectedHash.StringRepresentation); }
            if (InstallTime.TryUnwrap(out var installTime)) { addRootAttribute("installtime", installTime); }
            addRootAttribute(nameof(AccentColor), AccentColor);
            if (SteamDlcAppId.TryUnwrap(out var steamDlcAppId))
            {
                addRootAttribute("steamappid", steamDlcAppId.Value);
            }

            dependencies.ForEach(d => rootElement.Add(d.ToXElement()));
            groups.ForEach(f => rootElement.Add(f.ToXElement()));

            files.ForEach(f => rootElement.Add(f.ToXElement()));
            

            doc.Add(rootElement);
            return doc;
        }

        public void Save(string path, bool catchUnauthorizedAccessExceptions = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!, catchUnauthorizedAccessExceptions);
            ToXDocument().SaveSafe(path);
        }
    }
}
