using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Subsurface
{
    class RelatedItem
    {
        [Flags]
        public enum RelationType
        {
            None = 0,
            Contained = 1,
            Equipped = 2,
            Picked = 4
        }

        string[] names;

        RelationType type;

        public List<StatusEffect> statusEffects;

        //public string[] Names
        //{
        //    get { return names; }
        //}

        public string Msg;

        public RelationType Type
        {
            get { return type; }
        }

        public bool MatchesItem(Item item)
        {
            if (item == null) return false;
            return names.Any(name => item.Name == name || item.HasTag(name));
        }

        public string JoinedNames
        {
            get { return string.Join(",", names); }
            set
            {                
                if (value == null) return;

                names = value.Split(',');
                for (int i = 0; i < names.Length;i++ )
                {
                    names[i] = names[i].Trim();
                }
            }
        }


        public string[] Names
        {
            get { return names; }
        }

        public RelatedItem(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = names[i].Trim();
            }
            this.names = names;
            statusEffects = new List<StatusEffect>();
        }

        public void Save(XElement element)
        {
            element.Add(
                new XAttribute("name", JoinedNames),
                new XAttribute("type", type.ToString()));

            if (!string.IsNullOrWhiteSpace("msg")) element.Add(new XAttribute("msg", Msg));
        }

        public static RelatedItem Load(XElement element)
        {
            string nameString = ToolBox.GetAttributeString(element, "name", "");
            if (nameString == "") return null;

            string[] names = nameString.Split(',');
                        
            RelatedItem ri = new RelatedItem(names);

            try
            {
                ri.type = (RelationType)Enum.Parse(typeof(RelationType), ToolBox.GetAttributeString(element, "type", "None"));
            }

            catch
            {
                ri.type = RelationType.None;
            }

            ri.Msg = ToolBox.GetAttributeString(element, "msg", "");

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "statuseffect") continue;

                ri.statusEffects.Add(StatusEffect.Load(subElement));
            }
            
            return ri;
        }
    }
}
