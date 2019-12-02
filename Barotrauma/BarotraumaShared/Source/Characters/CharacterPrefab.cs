using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class CharacterPrefab : IPrefab, IDisposable
    {
        public readonly static PrefabCollection<CharacterPrefab> Prefabs = new PrefabCollection<CharacterPrefab>();

        private bool disposed = false;
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }

        public string OriginalName { get; private set; }
        public string Name { get; private set; }
        public string Identifier { get; private set; }
        public string FilePath { get; private set; }
        public ContentPackage ContentPackage { get; private set; }

        public XDocument XDocument { get; private set; }


        public static IEnumerable<string> ConfigFilePaths => Prefabs.Select(p => p.FilePath);
        public static IEnumerable<XDocument> ConfigFiles => Prefabs.Select(p => p.XDocument);

        public const string HumanSpeciesName = "human";
        public static string HumanConfigFile => FindBySpeciesName(HumanSpeciesName).FilePath;

        /// <summary>
        /// Searches for a character config file from all currently selected content packages, 
        /// or from a specific package if the contentPackage parameter is given.
        /// </summary>
        public static CharacterPrefab FindBySpeciesName(string speciesName)
        {
            speciesName = speciesName.ToLowerInvariant();
            if (!Prefabs.ContainsKey(speciesName)) { return null; }
            return Prefabs[speciesName];
        }

        public static CharacterPrefab FindByFilePath(string filePath)
        {
            return Prefabs.Find(p => p.FilePath.CleanUpPath() == filePath.CleanUpPath());
        }

        public static CharacterPrefab Find(Predicate<CharacterPrefab> predicate)
        {
            return Prefabs.Find(predicate);
        }

        public static void RemoveByFile(string file)
        {
            Prefabs.RemoveByFile(file);
        }

        public static bool LoadFromFile(ContentFile file, bool forceOverride=false)
        {
            return LoadFromFile(file.Path, file.ContentPackage, forceOverride);
        }

        public static bool LoadFromFile(string filePath, ContentPackage contentPackage, bool forceOverride=false)
        {
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null)
            {
                DebugConsole.ThrowError($"Loading character file failed: {filePath}");
                return false;
            }
            if (Prefabs.AllPrefabs.Any(kvp => kvp.Value.Any(cf => cf?.FilePath == filePath)))
            {
                DebugConsole.ThrowError($"Duplicate path: {filePath}");
                return false;
            }
            XElement mainElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;
            var name = mainElement.GetAttributeString("name", null);
            if (name != null)
            {
                DebugConsole.NewMessage($"Error in {filePath}: 'name' is deprecated! Use 'speciesname' instead.", Color.Orange);
            }
            else
            {
                name = mainElement.GetAttributeString("speciesname", string.Empty);
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                DebugConsole.ThrowError($"No species name defined for: {filePath}");
                return false;
            }
            var identifier = name.ToLowerInvariant();
            Prefabs.Add(new CharacterPrefab
            {
                Name = name,
                OriginalName = name,
                Identifier = identifier,
                FilePath = filePath,
                ContentPackage = contentPackage,
                XDocument = doc
            }, forceOverride || doc.Root.IsOverride());

            return true;
        }

        public static void LoadAll()
        {
            foreach (ContentFile file in ContentPackage.GetFilesOfType(GameMain.Config.SelectedContentPackages, ContentType.Character))
            {
                LoadFromFile(file);
            }
        }
    }
}
