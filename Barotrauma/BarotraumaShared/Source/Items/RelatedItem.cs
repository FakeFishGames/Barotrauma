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

        private string[] identifiers;

        private RelationType type;

        public List<StatusEffect> statusEffects;
        
        public string Msg;

        public RelationType Type
        {
            get { return type; }
        }

        public bool MatchesItem(Item item)
        {
            if (item == null) return false;
            return identifiers.Any(name => item.Prefab.Identifier == name || item.HasTag(name));
        }

        public string JoinedIdentifiers
        {
            get { return string.Join(",", identifiers); }
            set
            {
                if (value == null) return;

                identifiers = value.Split(',');
                for (int i = 0; i < identifiers.Length; i++)
                {
                    identifiers[i] = identifiers[i].Trim();
                }
            }
        }
        
        public string[] Identifiers
        {
            get { return identifiers; }
        }

        public RelatedItem(string[] identifiers)
        {
            for (int i = 0; i < identifiers.Length; i++)
            {
                identifiers[i] = identifiers[i].Trim();
            }
            this.identifiers = identifiers;
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

                        if (equippedItem.Condition > 0.0f && MatchesItem(equippedItem)) return true;
                    }
                    break;
                case RelationType.Picked:
                    if (character == null || character.Inventory == null) return false;
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
                new XAttribute("identifiers", JoinedIdentifiers),
                new XAttribute("type", type.ToString()));

            if (!string.IsNullOrWhiteSpace("msg")) element.Add(new XAttribute("msg", Msg));
        }

        public static RelatedItem Load(XElement element, string parentDebugName)
        {
            string[] identifiers;
            if (element.Attribute("name") != null)
            {
                //backwards compatibility + a console warning
                DebugConsole.ThrowError("Error in RelatedItem config (" + (string.IsNullOrEmpty(parentDebugName) ? element.ToString() : parentDebugName) + ") - use item identifiers or tags instead of names.");
                string[] itemNames = element.GetAttributeStringArray("name", new string[0]);
                //attempt to convert to identifiers and tags
                List<string> convertedIdentifiers = new List<string>();
                foreach (string itemName in itemNames)
                {
                    if (MapEntityPrefab.List.Find(me => me.Name == itemName) is ItemPrefab matchingItem)
                    {
                        convertedIdentifiers.Add(matchingItem.Identifier);
                    }
                    else
                    {
                        //no matching item found, this must be a tag
                        convertedIdentifiers.Add(itemName);
                    }
                }
                identifiers = convertedIdentifiers.ToArray();
            }
            else
            {
                identifiers = element.GetAttributeStringArray("identifiers", new string[0]);
                if (identifiers.Length == 0) identifiers = element.GetAttributeStringArray("identifier", new string[0]);
            }
            
            if (identifiers.Length == 0) return null;

            RelatedItem ri = new RelatedItem(identifiers);
            try
            {
                ri.type = (RelationType)Enum.Parse(typeof(RelationType), element.GetAttributeString("type", "None"));
            }
            catch
            {
                ri.type = RelationType.None;
            }

            ri.Msg = element.GetAttributeString("msg", "");

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "statuseffect") continue;
                ri.statusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
            }
            
            return ri;
        }
    }
}
