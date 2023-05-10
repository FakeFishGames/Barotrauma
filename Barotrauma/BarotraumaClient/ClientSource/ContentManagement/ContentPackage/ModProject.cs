#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.IO;

namespace Barotrauma
{
    public class ModProject
    {
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
            }
            
            private File(ContentFile f)
            {
                Path = f.Path.RawValue ?? "";
                Type = f.GetType();
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

            public XElement ToXElement()
            {
                if (Type is null) { throw new InvalidOperationException("Type must be set before calling ToXElement"); }
                if (Path.IsNullOrEmpty()) { throw new InvalidOperationException("Path must be set before calling ToXElement"); }
                return new XElement(Type.Name.RemoveFromEnd("File"), new XAttribute("file", Path));
            }
        }

        public ModProject() { }
        
        public ModProject(ContentPackage? contentPackage)
        {
            if (contentPackage is null) { return; }
            Name = contentPackage.Name;
            AltNames = contentPackage.AltNames.ToList();
            files = contentPackage.Files.Select(File.FromContentFile).ToList();
            ModVersion = IncrementModVersion(contentPackage.ModVersion);
            IsCore = contentPackage is CorePackage;
            UgcId = contentPackage.UgcId;
            ExpectedHash = contentPackage.Hash;
            InstallTime = contentPackage.InstallTime;
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

        private readonly List<File> files = new List<File>();
        public IReadOnlyList<File> Files => files;

        public string ModVersion = ContentPackage.DefaultModVersion;

        public Md5Hash? ExpectedHash { get; private set; }

        public bool IsCore = false;

        public Option<ContentPackageId> UgcId = Option<ContentPackageId>.None();

        public Option<SerializableDateTime> InstallTime = Option<SerializableDateTime>.None();

        public bool HasFile(File file)
            => Files.Any(f =>
                string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase)
                && f.Type == file.Type);

        public void AddFile(File file)
        {
            if (!HasFile(file))
            {
                files.Add(file);
                DiscardHashAndInstallTime();
            }
        }

        public void RemoveFile(File file)
        {
            if (HasFile(file))
            {
                files.Remove(file);
                DiscardHashAndInstallTime();
            }
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

            files.ForEach(f => rootElement.Add(f.ToXElement()));
            
            doc.Add(rootElement);
            return doc;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ToXDocument().SaveSafe(path);
        }
    }
}
