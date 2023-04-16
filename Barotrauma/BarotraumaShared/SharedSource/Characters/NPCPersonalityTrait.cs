using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCPersonalityTrait : PrefabWithUintIdentifier
    {
        public readonly static PrefabCollection<NPCPersonalityTrait> Traits = new PrefabCollection<NPCPersonalityTrait>();

        public readonly LocalizedString DisplayName;

        public readonly List<string> AllowedDialogTags;

        private readonly float commonness;
        public float Commonness
        {
            get { return commonness; }
        }

        public NPCPersonalityTrait(XElement element, NPCPersonalityTraitsFile file)
             : base(file, element.GetAttributeIdentifier("identifier", element.GetAttributeIdentifier("name", Identifier.Empty)))
        {
            string name = element.GetAttributeString("name", null);
            if (name == null)
            {
                DisplayName = TextManager.Get("personalitytrait." + Identifier)
                    .Fallback(Identifier.ToString());
            }
            else
            {
                DisplayName = name;
            }
            AllowedDialogTags = new List<string>(element.GetAttributeStringArray("alloweddialogtags", Array.Empty<string>()));
            commonness = element.GetAttributeFloat("commonness", 1.0f);
        }

        public static NPCPersonalityTrait GetRandom(string seed)
        {
            var rand = new MTRandom(ToolBox.StringToInt(seed));
            return ToolBox.SelectWeightedRandom(Traits.OrderBy(t => t.UintIdentifier), t => t.commonness, rand);
        }

        public override void Dispose() { }
    }
}
