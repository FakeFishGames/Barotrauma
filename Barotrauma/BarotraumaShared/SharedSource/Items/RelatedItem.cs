using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

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

        public bool RequireEmpty { get; set; }

        public bool IgnoreInEditor { get; set; }

        public ImmutableHashSet<Identifier> ExcludedIdentifiers { get; private set; }

        private RelationType type;

        public List<StatusEffect> statusEffects;
        
        public LocalizedString Msg;
        public Identifier MsgTag;

        /// <summary>
        /// Should broken (0 condition) items be excluded
        /// </summary>
        public bool ExcludeBroken { get; private set; }

        /// <summary>
        /// Should full condition (100%) items be excluded
        /// </summary>
        public bool ExcludeFullCondition { get; private set; }

        public bool AllowVariants { get; private set; } = true;

        public RelationType Type
        {
            get { return type; }
        }

        /// <summary>
        /// Index of the slot the target must be in when targeting a Contained item
        /// </summary>
        public int TargetSlot = -1;

        /// <summary>
        /// The slot type the target must be in when targeting an item contained inside a character's inventory
        /// </summary>
        public InvSlotType CharacterInventorySlotType;

        /// <summary>
        /// Overrides the position defined in ItemContainer.
        /// </summary>
        public Vector2? ItemPos;

        /// <summary>
        /// Only affects when ItemContainer.hideItems is false. Doesn't override the value.
        /// </summary>
        public bool? Hide;

        public float Rotation;

        public bool SetActive;

        public string JoinedIdentifiers
        {
            get { return string.Join(",", Identifiers); }
            set
            {
                if (value == null) return;

                Identifiers = value.Split(',').Select(s => s.Trim()).ToIdentifiers().ToImmutableHashSet();
            }
        }

        public ImmutableHashSet<Identifier> Identifiers { get; private set; }

        public string JoinedExcludedIdentifiers
        {
            get { return string.Join(",", ExcludedIdentifiers); }
            set
            {
                if (value == null) return;

                ExcludedIdentifiers = value.Split(',').Select(s => s.Trim()).ToIdentifiers().ToImmutableHashSet();
            }
        }

        public bool MatchesItem(Item item)
        {
            if (item == null) { return false; }
            if (ExcludedIdentifiers.Contains(item.Prefab.Identifier)) { return false; }
            if ((item.ParentInventory?.Owner is Character character) && (CharacterInventorySlotType != InvSlotType.None))
            {
                if (!character.HasEquippedItem(item, CharacterInventorySlotType)) { return false; }
            }
            foreach (var excludedIdentifier in ExcludedIdentifiers)
            {
                if (item.HasTag(excludedIdentifier)) { return false; }
            }
            if (Identifiers.Contains(item.Prefab.Identifier)) { return true; }
            foreach (var identifier in Identifiers)
            {
                if (item.HasTag(identifier)) { return true; }
            }
            if (AllowVariants && !item.Prefab.VariantOf.IsEmpty)
            {
                if (Identifiers.Contains(item.Prefab.VariantOf)) { return true; }
            }
            return false;
        }
        public bool MatchesItem(ItemPrefab itemPrefab)
        {
            if (itemPrefab == null) { return false; }
            if (ExcludedIdentifiers.Contains(itemPrefab.Identifier)) { return false; }
            foreach (var excludedIdentifier in ExcludedIdentifiers)
            {
                if (itemPrefab.Tags.Contains(excludedIdentifier)) { return false; }
            }
            if (Identifiers.Contains(itemPrefab.Identifier)) { return true; }
            foreach (var identifier in Identifiers)
            {
                if (itemPrefab.Tags.Contains(identifier)) { return true; }
            }
            if (AllowVariants && !itemPrefab.VariantOf.IsEmpty)
            {
                if (Identifiers.Contains(itemPrefab.VariantOf)) { return true; }
            }
            return false;
        }

        public RelatedItem(Identifier[] identifiers, Identifier[] excludedIdentifiers)
        {
            this.Identifiers = identifiers.Select(id => id.Value.Trim().ToIdentifier()).ToImmutableHashSet();
            this.ExcludedIdentifiers = excludedIdentifiers.Select(id => id.Value.Trim().ToIdentifier()).ToImmutableHashSet();

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
                    if (parentItem == null || parentItem.Container == null) { return MatchOnEmpty || RequireEmpty; }
                    return CheckItem(parentItem.Container, this);
                case RelationType.Equipped:
                    if (character == null) { return false; }
                    var heldItems = character.HeldItems;
                    if ((RequireEmpty || MatchOnEmpty) && heldItems.None()) { return true; }
                    foreach (Item equippedItem in heldItems)
                    {
                        if (equippedItem == null) { continue; }
                        if (CheckItem(equippedItem, this))
                        {
                            if (RequireEmpty && equippedItem.Condition > 0) { return false; }
                            return true;
                        }
                    }
                    break;
                case RelationType.Picked:
                    if (character == null) { return false; }
                    if (character.Inventory == null) { return MatchOnEmpty || RequireEmpty; }
                    var allItems = character.Inventory.AllItems;
                    if ((RequireEmpty || MatchOnEmpty) && allItems.None()) { return true; }
                    foreach (Item pickedItem in allItems)
                    {
                        if (pickedItem == null) { continue; }
                        if (CheckItem(pickedItem, this))
                        {
                            if (RequireEmpty && pickedItem.Condition > 0) { return false; }
                            return true;
                        }
                    }
                    break;
                default:
                    return true;
            }

            static bool CheckItem(Item i, RelatedItem ri) => (!ri.ExcludeBroken || ri.RequireEmpty || i.Condition > 0.0f) && (!ri.ExcludeFullCondition || !i.IsFullCondition) && ri.MatchesItem(i);

            return false;
        }

        private bool CheckContained(Item parentItem)
        {
            if (parentItem.OwnInventory == null) { return false; }
            bool isEmpty = parentItem.OwnInventory.IsEmpty();
            if (RequireEmpty && !isEmpty) { return false; }
            if (MatchOnEmpty && isEmpty) { return true; }
            foreach (Item contained in parentItem.ContainedItems)
            {
                if (TargetSlot > -1 && parentItem.OwnInventory.FindIndex(contained) != TargetSlot) { continue; }
                if ((!ExcludeBroken || contained.Condition > 0.0f) && (!ExcludeFullCondition || !contained.IsFullCondition) && MatchesItem(contained)) { return true; }
                if (CheckContained(contained)) { return true; }
            }
            return false;
        }

        public void Save(XElement element)
        {
            element.Add(
                new XAttribute("items", JoinedIdentifiers),
                new XAttribute("type", type.ToString()),
                new XAttribute("characterinventoryslottype", CharacterInventorySlotType.ToString()),
                new XAttribute("optional", IsOptional),
                new XAttribute("ignoreineditor", IgnoreInEditor),
                new XAttribute("excludebroken", ExcludeBroken),
                new XAttribute("requireempty", RequireEmpty),
                new XAttribute("excludefullcondition", ExcludeFullCondition),
                new XAttribute("targetslot", TargetSlot),
                new XAttribute("allowvariants", AllowVariants),
                new XAttribute("rotation", Rotation),
                new XAttribute("setactive", SetActive));

            if (Hide.HasValue)
            {
                element.Add(new XAttribute(nameof(Hide), Hide.Value));
            }
            if (ItemPos.HasValue)
            {
                element.Add(new XAttribute(nameof(ItemPos), ItemPos.Value));
            }

            if (ExcludedIdentifiers.Count > 0)
            {
                element.Add(new XAttribute("excludedidentifiers", JoinedExcludedIdentifiers));
            }

            if (!Msg.IsNullOrWhiteSpace()) { element.Add(new XAttribute("msg", MsgTag.IsEmpty ? Msg : MsgTag.Value)); }
        }

        public static RelatedItem Load(ContentXElement element, bool returnEmpty, string parentDebugName)
        {
            Identifier[] identifiers;
            if (element.GetAttribute("name") != null)
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

            InvSlotType slottype;
            try
            {
                slottype = Enum.Parse<InvSlotType>(element.GetAttributeString("characterinventoryslottype", "None"));
            }
            catch (ArgumentException)
            {
                DebugConsole.ThrowError("Error in RelatedItem config (" + parentDebugName + ") - invalid characterinventoryslottype (" + (element.GetAttributeString("characterinventoryslottype", "None")) + "). Check spelling/capitalization.");
                slottype = InvSlotType.None;
            }


            RelatedItem ri = new RelatedItem(identifiers, excludedIdentifiers)
            {
                ExcludeBroken = element.GetAttributeBool("excludebroken", true),
                CharacterInventorySlotType = slottype,
                RequireEmpty = element.GetAttributeBool("requireempty", false),
                ExcludeFullCondition = element.GetAttributeBool("excludefullcondition", false),
                AllowVariants = element.GetAttributeBool("allowvariants", true),
                Rotation = element.GetAttributeFloat("rotation", 0f),
                SetActive = element.GetAttributeBool("setactive", false)
            };
            if (element.GetAttribute(nameof(Hide)) != null)
            {
                ri.Hide = element.GetAttributeBool(nameof(Hide), false);
            }
            if (element.GetAttribute(nameof(ItemPos)) != null)
            {
                ri.ItemPos = element.GetAttributeVector2(nameof(ItemPos), Vector2.Zero);
            }
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
