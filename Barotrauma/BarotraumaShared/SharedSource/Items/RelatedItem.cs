using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class RelatedItem
    {
        public enum RelationType
        {
            None,
            Contained,
            Equipped,
            Picked,
            Container
        }

        public bool IsOptional { get; set; }

        public bool MatchOnEmpty { get; set; }

        public bool IgnoreInEditor { get; set; }

        private Identifier[] excludedIdentifiers;

        private RelationType type;

        public List<StatusEffect> statusEffects;
        
        public LocalizedString Msg;
        public Identifier MsgTag;

        /// <summary>
        /// Should broken (0 condition) items be excluded
        /// </summary>
        public bool ExcludeBroken { get; private set; }

        public bool AllowVariants { get; private set; } = true;

        public RelationType Type
        {
            get { return type; }
        }

        /// <summary>
        /// Index of the slot the target must be in when targeting a Contained item
        /// </summary>
        public int TargetSlot = -1;

        public string JoinedIdentifiers
        {
            get { return string.Join(",", Identifiers); }
            set
            {
                if (value == null) return;

                Identifiers = value.Split(',').Select(s => s.Trim()).ToIdentifiers().ToArray();
            }
        }

        public Identifier[] Identifiers { get; private set; }

        public string JoinedExcludedIdentifiers
        {
            get { return string.Join(",", excludedIdentifiers); }
            set
            {
                if (value == null) return;

                excludedIdentifiers = value.Split(',').Select(s => s.Trim()).ToIdentifiers().ToArray();
            }
        }

        public bool MatchesItem(Item item)
        {
            if (item == null) { return false; }
            if (excludedIdentifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id))) { return false; }
            return Identifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id) || (AllowVariants && !item.Prefab.VariantOf.IsEmpty && item.Prefab.VariantOf == id));
        }
        public bool MatchesItem(ItemPrefab itemPrefab)
        {
            if (itemPrefab == null) { return false; }
            if (excludedIdentifiers.Any(id => itemPrefab.Identifier == id || itemPrefab.Tags.Contains(id))) { return false; }
            return Identifiers.Any(id => itemPrefab.Identifier == id || itemPrefab.Tags.Contains(id) || (AllowVariants && !itemPrefab.VariantOf.IsEmpty && itemPrefab.VariantOf == id));
        }

        public RelatedItem(Identifier[] identifiers, Identifier[] excludedIdentifiers)
        {
            this.Identifiers = identifiers.Select(id => id.Value.Trim().ToIdentifier()).ToArray();
            this.excludedIdentifiers = excludedIdentifiers.Select(id => id.Value.Trim().ToIdentifier()).ToArray();

            statusEffects = new List<StatusEffect>();
        }

        public bool CheckRequirements(Character character, Item parentItem)
        {
            switch (type)
            {
                case RelationType.Contained:
                    if (parentItem == null) { return false; }
                    return CheckContained(parentItem);
                case RelationType.Container:
                    if (parentItem == null || parentItem.Container == null) { return MatchOnEmpty; }
                    return (!ExcludeBroken || parentItem.Container.Condition > 0.0f) && MatchesItem(parentItem.Container);
                case RelationType.Equipped:
                    if (character == null) { return false; }
                    if (MatchOnEmpty && !character.HeldItems.Any()) { return true; }
                    foreach (Item equippedItem in character.HeldItems)
                    {
                        if (equippedItem == null) { continue; }
                        if ((!ExcludeBroken || equippedItem.Condition > 0.0f) && MatchesItem(equippedItem)) { return true; }
                    }
                    break;
                case RelationType.Picked:
                    if (character == null || character.Inventory == null) { return false; }
                    foreach (Item pickedItem in character.Inventory.AllItems)
                    {
                        if (MatchesItem(pickedItem)) { return true; }
                    }
                    break;
                default:
                    return true;
            }

            return false;
        }

        private bool CheckContained(Item parentItem)
        {
            if (parentItem.OwnInventory == null) { return false; }

            if (MatchOnEmpty && parentItem.OwnInventory.IsEmpty())
            {
                return true;
            }

            foreach (Item contained in parentItem.ContainedItems)
            {
                if (TargetSlot > -1 && parentItem.OwnInventory.FindIndex(contained) != TargetSlot) { continue; }
                if ((!ExcludeBroken || contained.Condition > 0.0f) && MatchesItem(contained)) { return true; }

                if (CheckContained(contained)) { return true; }
            }
            return false;
        }

        public void Save(XElement element)
        {
            element.Add(
                new XAttribute("items", JoinedIdentifiers),
                new XAttribute("type", type.ToString()),
                new XAttribute("optional", IsOptional),
                new XAttribute("ignoreineditor", IgnoreInEditor),
                new XAttribute("excludebroken", ExcludeBroken),
                new XAttribute("targetslot", TargetSlot),
                new XAttribute("allowvariants", AllowVariants));

            if (excludedIdentifiers.Length > 0)
            {
                element.Add(new XAttribute("excludedidentifiers", JoinedExcludedIdentifiers));
            }

            if (!Msg.IsNullOrWhiteSpace()) { element.Add(new XAttribute("msg", MsgTag.IsEmpty ? Msg : MsgTag.Value)); }
        }

        public static RelatedItem Load(ContentXElement element, bool returnEmpty, string parentDebugName)
        {
            Identifier[] identifiers;
            if (element.Attribute("name") != null)
            {
                //backwards compatibility + a console warning
                DebugConsole.ThrowError("Error in RelatedItem config (" + (string.IsNullOrEmpty(parentDebugName) ? element.ToString() : parentDebugName) + ") - use item tags or identifiers instead of names.");
                Identifier[] itemNames = element.GetAttributeIdentifierArray("name", Array.Empty<Identifier>());
                //attempt to convert to identifiers and tags
                List<Identifier> convertedIdentifiers = new List<Identifier>();
                foreach (Identifier itemName in itemNames)
                {
                    var matchingItem = ItemPrefab.Prefabs.Find(me => me.Name == itemName.Value);
                    if (matchingItem != null)
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
                identifiers = element.GetAttributeIdentifierArray("items", null) ?? element.GetAttributeIdentifierArray("item", null);
                if (identifiers == null)
                {
                    identifiers = element.GetAttributeIdentifierArray("identifiers", null) ?? element.GetAttributeIdentifierArray("tags", null);
                    if (identifiers == null)
                    {
                        identifiers = element.GetAttributeIdentifierArray("identifier", null) ?? element.GetAttributeIdentifierArray("tag", Array.Empty<Identifier>());
                    }
                }
            }

            Identifier[] excludedIdentifiers = element.GetAttributeIdentifierArray("excludeditems", null) ?? element.GetAttributeIdentifierArray("excludeditem", null);
            if (excludedIdentifiers == null)
            {
                excludedIdentifiers = element.GetAttributeIdentifierArray("excludedidentifiers", null) ?? element.GetAttributeIdentifierArray("excludedtags", null);
                if (excludedIdentifiers == null)
                {
                    excludedIdentifiers = element.GetAttributeIdentifierArray("excludedidentifier", null) ?? element.GetAttributeIdentifierArray("excludedtag", Array.Empty<Identifier>());
                }
            }


            if (identifiers.Length == 0 && excludedIdentifiers.Length == 0 && !returnEmpty) { return null; }

            RelatedItem ri = new RelatedItem(identifiers, excludedIdentifiers)
            {
                ExcludeBroken = element.GetAttributeBool("excludebroken", true),
                AllowVariants = element.GetAttributeBool("allowvariants", true)
            };
            string typeStr = element.GetAttributeString("type", "");
            if (string.IsNullOrEmpty(typeStr))
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "containable":
                        typeStr = "Contained";
                        break;
                    case "suitablefertilizer":
                    case "suitableseed":
                        typeStr = "None";
                        break;
                }
            }
            if (!Enum.TryParse(typeStr, true, out ri.type))
            {
                DebugConsole.ThrowError("Error in RelatedItem config (" + parentDebugName + ") - \"" + typeStr + "\" is not a valid relation type.");
                return null;
            }

            ri.MsgTag = element.GetAttributeIdentifier("msg", Identifier.Empty);
            LocalizedString msg = TextManager.Get(ri.MsgTag);
            if (!msg.Loaded)
            {
                ri.Msg = ri.MsgTag.Value;
            }
            else
            {
#if CLIENT
                foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
                {
                    msg = msg.Replace("[" + inputType.ToString().ToLowerInvariant() + "]", GameSettings.CurrentConfig.KeyMap.KeyBindText(inputType));
                }
                ri.Msg = msg;
#endif
            }

            foreach (var subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("statuseffect", StringComparison.OrdinalIgnoreCase)) { continue; }
                ri.statusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
            }

            ri.IsOptional = element.GetAttributeBool("optional", false);
            ri.IgnoreInEditor = element.GetAttributeBool("ignoreineditor", false);
            ri.MatchOnEmpty = element.GetAttributeBool("matchonempty", false);
            ri.TargetSlot = element.GetAttributeInt("targetslot", -1);

            return ri;
        }
    }
}
