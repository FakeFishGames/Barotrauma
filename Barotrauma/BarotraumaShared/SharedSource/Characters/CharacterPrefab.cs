using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using static Barotrauma.CharacterInfo;

namespace Barotrauma
{
    class CharacterPrefab : PrefabWithUintIdentifier, IImplementsVariants<CharacterPrefab>
    {
        public readonly static PrefabCollection<CharacterPrefab> Prefabs = new PrefabCollection<CharacterPrefab>();

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
            Character.RemoveByPrefab(this);
        }

        public string Name => Identifier.Value;
        public PrefabInstance InheritParent { get; private set; }
        public List<PrefabInstance> InheritHistory { get; private set;  }
        public void InheritFrom(CharacterPrefab parent)
        {
            InheritHistory = new List<PrefabInstance>();
            // toolbox's createcopy throws for List<PrefabInstance>
            if (!(parent.InheritHistory is null))
            {
                foreach (PrefabInstance inst in parent.InheritHistory)
                {
                    InheritHistory.Add(inst);
                }
            }
            // xml may not specify package name, then this prefab parent here is important...
            InheritHistory.Add(new PrefabInstance(parent.Identifier, parent.ContentPackage.Name));
            // xml didn't specify
            if (InheritParent.package.IsNullOrEmpty())
            {
                InheritParent.package = parent.ContentPackage.Name;
            }

            ConfigElement = (this as IImplementsVariants<CharacterPrefab>).DoInherit(CharacterParams.CreateVariantXml_callback);
            ParseConfigElement();
        }

        private void ParseConfigElement()
        {
            var headsElement = ConfigElement.GetChildElement("Heads");
            var varsElement = ConfigElement.GetChildElement("Vars");
            var menuCategoryElement = ConfigElement.GetChildElement("MenuCategory");
            var pronounsElement = ConfigElement.GetChildElement("Pronouns");

            if (headsElement != null)
            {
                CharacterInfoPrefab = new CharacterInfoPrefab(headsElement, varsElement, menuCategoryElement, pronounsElement);
            }
        }

        public XElement originalElement { get; }
        public ContentXElement ConfigElement { get; private set; }

        public CharacterInfoPrefab CharacterInfoPrefab { get; private set; }

        public static IEnumerable<ContentXElement> ConfigElements => Prefabs.Select(p => p.ConfigElement);

        public static readonly Identifier HumanSpeciesName = "human".ToIdentifier();
        public static CharacterFile HumanConfigFile => HumanPrefab.ContentFile as CharacterFile;
        public static CharacterPrefab HumanPrefab => FindBySpeciesName(HumanSpeciesName);

        /// <summary>
        /// Searches for a character config file from all currently selected content packages, 
        /// or from a specific package if the contentPackage parameter is given.
        /// </summary>
        public static CharacterPrefab FindBySpeciesName(Identifier speciesName)
        {
            if (!Prefabs.ContainsKey(speciesName)) { return null; }
            return Prefabs[speciesName];
        }

        public static CharacterPrefab FindByFilePath(string filePath)
        {
            return Prefabs.Find(p => p.ContentFile.Path == filePath);
        }

        public static CharacterPrefab Find(Predicate<CharacterPrefab> predicate)
        {
            return Prefabs.Find(predicate);
        }

        public CharacterPrefab(ContentXElement mainElement, CharacterFile file) : base(file, ParseName(mainElement, file))
        {
            originalElement = mainElement;
            ConfigElement = mainElement;
            InheritParent = mainElement.InheritParent();

            ParseConfigElement();
        }

        public static Identifier ParseName(XElement element, CharacterFile file)
        {
            string name = element.GetAttributeString("name", null);
            if (!string.IsNullOrEmpty(name))
            {
                DebugConsole.NewMessage($"Error in {file.Path}: 'name' is deprecated! Use 'speciesname' instead.", Color.Orange);
            }
            else
            {
                name = element.GetAttributeString("speciesname", string.Empty);
            }
            return new Identifier(name);
        }

        public static bool CheckSpeciesName(XElement mainElement, CharacterFile file, out Identifier name)
        {
            name = ParseName(mainElement, file);
            if (name == Identifier.Empty)
            {
                DebugConsole.ThrowError($"No species name defined for: {file.Path}");
                return false;
            }
            return true;
        }

        public CharacterPrefab FindByPrefabInstance(PrefabInstance instance){
            Prefabs.TryGet(instance, out CharacterPrefab res);
            return res;
		}

        public CharacterPrefab GetPrevious(Identifier identifier)
        {
            CharacterPrefab res;
            if (identifier != Identifier)
            {
                res = Prefabs[identifier];
            }
            else{
                res = Prefabs.AllPrefabs.Where(p => p.Key == identifier)
                    .Single().Value
					.GetPrevious((ContentPackage.SteamWorkshopId != 0) ? ContentPackage.SteamWorkshopId.ToString() : ContentPackage.Name);
			}
            if (res is null) return null;
            if (originalElement.InheritParent().package.IsNullOrEmpty())
            {
                InheritParent.package = res.ContentPackage.Name;
            }
            return res;
        }
    }
}
