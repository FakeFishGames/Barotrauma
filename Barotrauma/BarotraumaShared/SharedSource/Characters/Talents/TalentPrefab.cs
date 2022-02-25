using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class TalentPrefab : PrefabWithUintIdentifier
    {
        public string OriginalName => Identifier.Value;

        public LocalizedString DisplayName { get; private set; }

        public LocalizedString Description { get; private set; }

        public readonly Sprite Icon;

        public static readonly PrefabCollection<TalentPrefab> TalentPrefabs = new PrefabCollection<TalentPrefab>();

        public ContentXElement ConfigElement
        {
            get;
            private set;
        }

        public TalentPrefab(ContentXElement element, TalentsFile file) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        {
            ConfigElement = element;

            DisplayName = TextManager.Get($"talentname.{Identifier}").Fallback(Identifier.Value);

            Description = "";
            
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement);
                        break;
                    case "description":
                        var tempDescription = Description;
                        TextManager.ConstructDescription(ref tempDescription, subElement);
                        Description = tempDescription;
                        break;
                }
            }

            if (element.Attribute("description") != null)
            {
                string description = element.GetAttributeString("description", string.Empty);
                Description = Description.Fallback(TextManager.Get(description)).Fallback(description);
            }
            else
            {
                Description = Description.Fallback(TextManager.Get($"talentdescription.{Identifier}")).Fallback(string.Empty);
            }
        }

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
        }
    }
}
