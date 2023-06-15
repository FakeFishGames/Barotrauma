using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    /// <summary>
    /// Used by various features to define different kinds of relations between items: 
    /// for example, which item a character must have equipped to interact with some item in some way, 
    /// which items can go inside a container, or which kind of item the target of a status effect must have for the effect to execute.
    /// </summary>
    class RelatedItem
    {
        public enum RelationType
        {
            None,
            /// <summary>
            /// The item must be contained inside the item this relation is defined in. 
            /// Can for example by used to make an item usable only when there's a specific kind of item inside it.
            /// </summary>
            Contained,
            /// <summary>
            /// The user must have equipped the item (i.e. held or worn).
            /// </summary>
            Equipped,
            /// <summary>
            /// The user must have picked up the item (i.e. the item needs to be in the user's inventory).
            /// </summary>
            Picked,
            /// <summary>
            /// The item this relation is defined in must be inside a specific kind of container. 
            /// Can for example by used to make an item do something when it's inside some other type of item.
            /// </summary>
            Container,
            /// <summary>
            /// Signifies an error (type could not be parsed)
            /// </summary>
            Invalid
        }

        /// <summary>
        /// Should an empty inventory be considered valid? Can be used to, for example, make an item do something if there's a specific item, or nothing, inside it.
        /// </summary>
        public bool MatchOnEmpty { get; set; }

        /// <summary>
        /// Should only an empty inventory be considered valid? Can be used to, for example, make an item do something when there's nothing inside it.
        /// </summary>
        public bool RequireEmpty { get; set; }

        /// <summary>
        /// Only valid for the RequiredItems of an ItemComponent. Can be used to ignore the requirement in the submarine editor, 
        /// making it easier to for example make rewire things that require some special tool to rewire.
        /// </summary>
        public bool IgnoreInEditor { get; set; }

        /// <summary>
        /// Identifier(s) or tag(s) of the items that are NOT considered valid. 
        /// Can be used to, for example, exclude some specific items when using tags that apply to multiple items.
        /// </summary>
        public ImmutableHashSet<Identifier> ExcludedIdentifiers { get; private set; }

        private readonly RelationType type;

        public List<StatusEffect> StatusEffects = new List<StatusEffect>();

        /// <summary>
        /// Only valid for the RequiredItems of an ItemComponent. A message displayed if the required item isn't found (e.g. a notification about lack of ammo or fuel).
        /// </summary>
        public LocalizedString Msg;

        /// <summary>
        /// Only valid for the RequiredItems of an ItemComponent. The localization tag of a message displayed if the required item isn't found (e.g. a notification about lack of ammo or fuel).
        /// </summary>
        public Identifier MsgTag;

        /// <summary>
        /// Should broken (0 condition) items be excluded?
        /// </summary>
        public bool ExcludeBroken { get; private set; }

        /// <summary>
        /// Should full condition (100%) items be excluded?
        /// </summary>
        public bool ExcludeFullCondition { get; private set; }

        /// <summary>
        /// Are item variants considered valid?
        /// </summary>
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
        /// Overrides the position defined in ItemContainer. Only valid when used in the Containable definitions of an ItemContainer.
        /// </summary>
        public Vector2? ItemPos;

        /// <summary>
        ///  Only valid when used in the Containable definitions of an ItemContainer.
        ///  Only affects when ItemContainer.hideItems is false. Doesn't override the value.
        /// </summary>
        public bool Hide;

        /// <summary>
        ///  Only valid when used in the Containable definitions of an ItemContainer. 
        ///  Can be used to override the rotation of specific items in the container.
        /// </summary>
        public float Rotation;

        /// <summary>
        ///  Only valid when used in the Containable definitions of an ItemContainer. 
        ///  Can be used to force specific items to stay active inside the container (such as flashlights attached to a gun).
        /// </summary>
        public bool SetActive;

        /// <summary>
        /// Only valid for the RequiredItems of an ItemComponent. Can be used to make the requirement optional, 
        /// meaning that you don't need to have the item to interact with something, but having it may still affect what the interaction does (such as using a crowbar on a door).
        /// </summary>
        public bool IsOptional { get; set; }

        public string JoinedIdentifiers
        {
            get { return string.Join(",", Identifiers); }
            set
            {
                if (value == null) return;

                Identifiers = value.Split(',').Select(s => s.Trim()).ToIdentifiers().ToImmutableHashSet();
            }
        }

        /// <summary>
        /// Identifier(s) or tag(s) of the items that are considered valid.
        /// </summary>
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
        }

        public RelatedItem(ContentXElement element, string parentDebugName)
        {
            Identifier[] identifiers;
            if (element.GetAttribute("name") != null)
            {
                //backwards compatibility + a console warning
                DebugConsole.ThrowError($"Error in RelatedItem config (" + (string.IsNullOrEmpty(parentDebugName) ? element.ToString() : parentDebugName) + ") - use item tags or identifiers instead of names.");
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
            this.Identifiers = identifiers.ToImmutableHashSet();

            Identifier[] excludedIdentifiers = element.GetAttributeIdentifierArray("excludeditems", null) ?? element.GetAttributeIdentifierArray("excludeditem", null);
            if (excludedIdentifiers == null)
            {
                excludedIdentifiers = element.GetAttributeIdentifierArray("excludedidentifiers", null) ?? element.GetAttributeIdentifierArray("excludedtags", null);
                if (excludedIdentifiers == null)
                {
                    excludedIdentifiers = element.GetAttributeIdentifierArray("excludedidentifier", null) ?? element.GetAttributeIdentifierArray("excludedtag", Array.Empty<Identifier>());
                }
            }
            this.ExcludedIdentifiers = excludedIdentifiers.ToImmutableHashSet();

            ExcludeBroken = element.GetAttributeBool("excludebroken", true);
            RequireEmpty = element.GetAttributeBool("requireempty", false);
            ExcludeFullCondition = element.GetAttributeBool("excludefullcondition", false);
            AllowVariants = element.GetAttributeBool("allowvariants", true);
            Rotation = element.GetAttributeFloat("rotation", 0f);
            SetActive = element.GetAttributeBool("setactive", false);

            if (element.GetAttribute(nameof(Hide)) != null)
            {
                Hide = element.GetAttributeBool(nameof(Hide), false);
            }
            if (element.GetAttribute(nameof(ItemPos)) != null)
            {
                ItemPos = element.GetAttributeVector2(nameof(ItemPos), Vector2.Zero);
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
            if (!Enum.TryParse(typeStr, true, out type))
            {
                DebugConsole.ThrowError("Error in RelatedItem config (" + parentDebugName + ") - \"" + typeStr + "\" is not a valid relation type.");
                type = RelationType.Invalid;
            }

            MsgTag = element.GetAttributeIdentifier("msg", Identifier.Empty);
            LocalizedString msg = TextManager.Get(MsgTag);
            if (!msg.Loaded)
            {
                Msg = MsgTag.Value;
            }
            else
            {
#if CLIENT
                foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
                {
                    msg = msg.Replace("[" + inputType.ToString().ToLowerInvariant() + "]", GameSettings.CurrentConfig.KeyMap.KeyBindText(inputType));
                }
                Msg = msg;
#endif
            }

            foreach (var subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("statuseffect", StringComparison.OrdinalIgnoreCase)) { continue; }
                StatusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
            }

            IsOptional = element.GetAttributeBool("optional", false);
            IgnoreInEditor = element.GetAttributeBool("ignoreineditor", false);
            MatchOnEmpty = element.GetAttributeBool("matchonempty", false);
            TargetSlot = element.GetAttributeInt("targetslot", -1);

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
            foreach (var container in parentItem.GetComponents<Items.Components.ItemContainer>())
            {
                foreach (Item contained in container.Inventory.AllItems)
                {
                    if (TargetSlot > -1 && parentItem.OwnInventory.FindIndex(contained) != TargetSlot) { continue; }
                    if ((!ExcludeBroken || contained.Condition > 0.0f) && (!ExcludeFullCondition || !contained.IsFullCondition) && MatchesItem(contained)) { return true; }
                    if (CheckContained(contained)) { return true; }
                }
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
                new XAttribute("requireempty", RequireEmpty),
                new XAttribute("excludefullcondition", ExcludeFullCondition),
                new XAttribute("targetslot", TargetSlot),
                new XAttribute("allowvariants", AllowVariants),
                new XAttribute("rotation", Rotation),
                new XAttribute("setactive", SetActive));

            if (Hide)
            {
                element.Add(new XAttribute(nameof(Hide), true));
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
            RelatedItem ri = new RelatedItem(element, parentDebugName);
            if (ri.Type == RelationType.Invalid) { return null; }
            if (ri.Identifiers.None() && ri.ExcludedIdentifiers.None() && !returnEmpty) { return null; }
            return ri;
        }
    }
}
