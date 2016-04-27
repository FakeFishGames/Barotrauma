using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class RelatedItem
    {
        [Flags]
        public enum RelationType
        {
            None = 0,
            Contained = 1,
            Equipped = 2,
            Picked = 4,
            Container = 8
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

        public bool CheckRequirements(Character character, Item parentItem)
        {
            switch (type)
            {
                case RelationType.Contained:
                    if (parentItem == null) return false;

                    var containedItems = parentItem.ContainedItems;
                    if (containedItems == null) return false;

                    foreach (Item contained in containedItems)
                    {
                        if (contained.Condition > 0.0f && MatchesItem(contained)) return true;
                    }
                    break;
                case RelationType.Container:
                    if (parentItem == null || parentItem.Container == null) return false;

                    return parentItem.Container.Condition > 0.0f && MatchesItem(parentItem.Container);
                case RelationType.Equipped:
                    if (character == null) return false;
                    foreach (Item equippedItem in character.SelectedItems)
                    {
                        if (equippedItem == null) continue;

                        if (equippedItem.Condition>0.0f && MatchesItem(equippedItem)) return true;
                    }
                    break;
                case RelationType.Picked:
                    if (character == null || character.Inventory==null) return false;
                    foreach (Item pickedItem in character.Inventory.Items)
                    {
                        if (pickedItem == null) continue;

                        if (MatchesItem(pickedItem)) return true;
                    }
                    break;
                default:
                    return true;
            }

            return false;
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
                if (subElement.Name.ToString().ToLowerInvariant() != "statuseffect") continue;

                ri.statusEffects.Add(StatusEffect.Load(subElement));
            }
            
            return ri;
        }
    }
}
