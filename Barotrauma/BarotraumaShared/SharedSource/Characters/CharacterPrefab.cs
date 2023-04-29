using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class CharacterPrefab : PrefabWithUintIdentifier, IImplementsVariants<CharacterPrefab>
    {
        public readonly static PrefabCollection<CharacterPrefab> Prefabs = new PrefabCollection<CharacterPrefab>();

        public override void Dispose()
        {
            Character.RemoveByPrefab(this);
        }

        public string Name => Identifier.Value;
        public Identifier VariantOf { get; }
        public CharacterPrefab ParentPrefab { get; set; }

        public void InheritFrom(CharacterPrefab parent)
        {
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
                if(Prefabs.Any(p=>p.Identifier == identifier)){
					res = Prefabs[identifier];
				}
                else{
                    res = null;
                }
            }
            else {
                if (Prefabs.AllPrefabs.Any(p => p.Key == identifier)) {
                    string best_effort_package_id = ContentPackage.GetBestEffortId();
					res = Prefabs.AllPrefabs.Where(p => p.Key == identifier)
		                .Single().Value
		                .GetPrevious(best_effort_package_id);
				}
                else{
                    res = null;
                }
			}
            return res;
        }
    }
}
