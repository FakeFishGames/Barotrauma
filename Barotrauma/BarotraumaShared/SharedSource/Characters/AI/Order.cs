using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    public enum OrderCategory
    {
        Emergency,
        Movement,
        Power,
        Maintenance,
        Operate
    }

    class Order
    {
        public static Dictionary<string, Order> Prefabs { get; private set; }
        public static Dictionary<OrderCategory, Tuple<Sprite, Color>> OrderCategoryIcons { get; private set; }
        public static List<Order> PrefabList { get; private set; }
        public static Order GetPrefab(string identifier)
        {
            if (!Prefabs.TryGetValue(identifier, out Order order))
            {
                DebugConsole.ThrowError($"Cannot find an order with the identifier '{identifier}'!");
            }
            return order;
        }
        
        public Order Prefab { get; private set; }

        public readonly string Name;

        public readonly Sprite SymbolSprite;

        public readonly Type ItemComponentType;
        public readonly string[] ItemIdentifiers;

        public readonly string Identifier;

        private Color? color;
        public Color Color
        {
            get
            {
                if (color.HasValue)
                {
                    return color.Value;
                }
                else if (Category.HasValue && OrderCategoryIcons.TryGetValue((OrderCategory)Category, out Tuple<Sprite, Color> sprite))
                {
                    return sprite.Item2;
                }
                else
                {
                    return Color.White;
                }
            }
            private set
            {
                color = value;
            }
        }
        

        //if true, the order is issued to all available characters
        public bool TargetAllCharacters;

        public readonly float FadeOutTime;

        public Entity TargetEntity; 
        public ItemComponent TargetItemComponent;
        public readonly bool UseController;
        public Controller ConnectedController;

        public Character OrderGiver;

        private readonly OrderCategory? category;
        public OrderCategory? Category => category;

        //legacy support
        public readonly string[] AppropriateJobs;
        public readonly string[] Options;
        public readonly string[] OptionNames;

        public readonly Dictionary<string, Sprite> OptionSprites;

        public readonly float Weight;
        public readonly bool MustSetTarget;
        public readonly string AppropriateSkill;

        public bool HasOptions
        {
            get
            {
                if (IsPrefab)
                {
                    return MustSetTarget || Options.Length > 1;
                }
                else
                {
                    return Prefab.MustSetTarget || Prefab.Options.Length > 1;
                }
            }
        }
        public bool IsPrefab { get; private set; }
        public readonly bool MustManuallyAssign;

        static Order()
        {
            Prefabs = new Dictionary<string, Order>();
            OrderCategoryIcons = new Dictionary<OrderCategory, Tuple<Sprite, Color>>();

            foreach (ContentFile file in GameMain.Instance.GetFilesOfType(ContentType.Orders))
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                bool allowOverriding = false;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    allowOverriding = true;
                }
                foreach (XElement sourceElement in mainElement.Elements())
                {
                    var element = sourceElement.IsOverride() ? sourceElement.FirstElement() : sourceElement;
                    string name = element.Name.ToString();
                    if (name.Equals("order", StringComparison.OrdinalIgnoreCase))
                    {
                        string identifier = element.GetAttributeString("identifier", null);
                        if (string.IsNullOrWhiteSpace(identifier))
                        {
                            DebugConsole.ThrowError($"Error in file {file.Path}: The order element '{name}' does not have an identifier! All orders must have a unique identifier.");
                            continue;
                        }
                        if (Prefabs.TryGetValue(identifier, out Order duplicate))
                        {
                            if (allowOverriding || sourceElement.IsOverride())
                            {
                                DebugConsole.NewMessage($"Overriding an existing order '{identifier}' with another one defined in '{file.Path}'", Color.Yellow);
                                Prefabs.Remove(identifier);
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Error in file {file.Path}: Duplicate element with the idenfitier '{identifier}' found in '{file.Path}'! All orders must have a unique identifier. Use <override></override> tags to override an order with the same identifier.");
                                continue;
                            }
                        }
                        var newOrder = new Order(element);
                        newOrder.Prefab = newOrder;
                        Prefabs.Add(identifier, newOrder);
                    }
                    else if (name.Equals("ordercategory", StringComparison.OrdinalIgnoreCase))
                    {
                        var category = (OrderCategory)Enum.Parse(typeof(OrderCategory), element.GetAttributeString("category", "undefined"), true);
                        if (OrderCategoryIcons.ContainsKey(category))
                        {
                            if (allowOverriding || sourceElement.IsOverride())
                            {
                                DebugConsole.NewMessage($"Overriding an existing icon for the '{category}' order category with another one defined in '{file}'", Color.Yellow);
                                OrderCategoryIcons.Remove(category);
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Error in file {file}: Duplicate element for the '{category}' order category found in '{file}'! All order categories must be unique. Use <override></override> tags to override an order category.");
                                continue;
                            }
                        }
                        var spriteElement = element.GetChildElement("sprite");
                        if (spriteElement != null)
                        {
                            var sprite = new Sprite(spriteElement, lazyLoad: true);
                            var color = element.GetAttributeColor("color", Color.White);
                            OrderCategoryIcons.Add(category, new Tuple<Sprite, Color>(sprite, color));
                        }
                    }
                }
            }
            PrefabList = new List<Order>(Prefabs.Values);
        }

        /// <summary>
        /// Constructor for order prefabs
        /// </summary>
        private Order(XElement orderElement)
        {
            Identifier = orderElement.GetAttributeString("identifier", "");
            Name = TextManager.Get("OrderName." + Identifier, true) ?? "Name not found";

            string targetItemType = orderElement.GetAttributeString("targetitemtype", "");
            if (!string.IsNullOrWhiteSpace(targetItemType))
            {
                try
                {
                    ItemComponentType = Type.GetType("Barotrauma.Items.Components." + targetItemType, true, true);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in the order definitions: item component type " + targetItemType + " not found", e);
                }
            }

            ItemIdentifiers = orderElement.GetAttributeStringArray("targetitemidentifiers", new string[0], trim: true, convertToLowerInvariant: true);
            color = orderElement.GetAttributeColor("color");
            FadeOutTime = orderElement.GetAttributeFloat("fadeouttime", 0.0f);
            UseController = orderElement.GetAttributeBool("usecontroller", false);
            TargetAllCharacters = orderElement.GetAttributeBool("targetallcharacters", false);
            AppropriateJobs = orderElement.GetAttributeStringArray("appropriatejobs", new string[0]);
            Options = orderElement.GetAttributeStringArray("options", new string[0]);
            var category = orderElement.GetAttributeString("category", null);
            if (!string.IsNullOrWhiteSpace(category)) { this.category = (OrderCategory)Enum.Parse(typeof(OrderCategory), category, true); }
            Weight = orderElement.GetAttributeFloat(0.0f, "weight");
            MustSetTarget = orderElement.GetAttributeBool("mustsettarget", false);
            AppropriateSkill = orderElement.GetAttributeString("appropriateskill", null);

            string translatedOptionNames = TextManager.Get("OrderOptions." + Identifier, true);
            if (translatedOptionNames == null)
            {
                OptionNames = orderElement.GetAttributeStringArray("optionnames", new string[0]);
            }
            else
            {
                string[] splitOptionNames = translatedOptionNames.Split(',', '，');
                OptionNames = new string[Options.Length];
                for (int i = 0; i < Options.Length && i < splitOptionNames.Length; i++)
                {
                    OptionNames[i] = splitOptionNames[i].Trim();
                }
            }

            if (OptionNames.Length != Options.Length)
            {
                DebugConsole.ThrowError("Error in Order " + Name + " - the number of option names doesn't match the number of options.");
                OptionNames = Options;
            }

            var spriteElement = orderElement.GetChildElement("sprite");
            if (spriteElement != null)
            {
                SymbolSprite = new Sprite(spriteElement, lazyLoad: true);
            }

            OptionSprites = new Dictionary<string, Sprite>();
            if (Options != null && Options.Length > 0)
            {
                var optionSpriteElements = orderElement.GetChildElement("optionsprites")?.GetChildElements("sprite");
                if (optionSpriteElements != null && optionSpriteElements.Any())
                {
                    for (int i = 0; i < Options.Length; i++)
                    {
                        if (i >= optionSpriteElements.Count()) { break; };
                        var sprite = new Sprite(optionSpriteElements.ElementAt(i), lazyLoad: true);
                        OptionSprites.Add(Options[i], sprite);
                    }
                }
            }

            IsPrefab = true;
            MustManuallyAssign = orderElement.GetAttributeBool("mustmanuallyassign", false);
        }
        
        /// <summary>
        /// Constructor for order instances
        /// </summary>
        public Order(Order prefab, Entity targetEntity, ItemComponent targetItem, Character orderGiver = null)
        {
            Prefab = prefab;

            Name                = prefab.Name;
            Identifier          = prefab.Identifier;
            ItemComponentType   = prefab.ItemComponentType;
            ItemIdentifiers     = prefab.ItemIdentifiers;
            Options             = prefab.Options;
            SymbolSprite        = prefab.SymbolSprite;
            Color               = prefab.Color;
            UseController       = prefab.UseController;
            TargetAllCharacters = prefab.TargetAllCharacters;
            AppropriateJobs     = prefab.AppropriateJobs;
            FadeOutTime         = prefab.FadeOutTime;
            Weight              = prefab.Weight;
            MustSetTarget       = prefab.MustSetTarget;
            AppropriateSkill    = prefab.AppropriateSkill;
            category            = prefab.Category;
            MustManuallyAssign  = prefab.MustManuallyAssign;

            OrderGiver = orderGiver;
            TargetEntity = targetEntity;
            if (targetItem != null)
            {
                if (UseController) { ConnectedController = FindController(targetItem); }
                TargetEntity = targetItem.Item;
                TargetItemComponent = targetItem;
            }

            IsPrefab = false;
        }

        private Controller FindController(ItemComponent targetComponent)
        {
            if (targetComponent?.Item == null) { return null; }
            //try finding the controller with the simpler non-recursive method first
            return targetComponent.Item.GetConnectedComponents<Controller>().FirstOrDefault() ??
                targetComponent.Item.GetConnectedComponents<Controller>(recursive: true).FirstOrDefault();
        }

        private bool TryFindController(ItemComponent targetComponent, out Controller controller)
        {
            controller = FindController(targetComponent);
            return controller != null;
        }
        
        public bool HasAppropriateJob(Character character)
        {
            if (character.Info == null || character.Info.Job == null) { return false; }
            if (character.Info.Job.Prefab.AppropriateOrders.Any(appropriateOrderId => Identifier == appropriateOrderId)) { return true; }

            if (!JobPrefab.Prefabs.Any(jp => jp.AppropriateOrders.Contains(Identifier)) &&
                (AppropriateJobs == null || AppropriateJobs.Length == 0))
            {
                return true;
            }
            for (int i = 0; i < AppropriateJobs.Length; i++)
            {
                if (character.Info.Job.Prefab.Identifier.Equals(AppropriateJobs[i], StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        public string GetChatMessage(string targetCharacterName, string targetRoomName, bool givingOrderToSelf, string orderOption = "")
        {
            orderOption ??= "";

            string messageTag = (givingOrderToSelf && !TargetAllCharacters ? "OrderDialogSelf." : "OrderDialog.") + Identifier;
            if (!string.IsNullOrEmpty(orderOption)) { messageTag += "." + orderOption; }

            if (targetCharacterName == null) { targetCharacterName = ""; }
            if (targetRoomName == null) { targetRoomName = ""; }
            string msg = TextManager.GetWithVariables(messageTag, new string[2] { "[name]", "[roomname]" }, new string[2] { targetCharacterName, targetRoomName }, new bool[2] { false, true }, true);
            if (msg == null) { return ""; }

            return msg;
        }

        public List<Item> GetMatchingItems(Submarine submarine, bool mustBelongToPlayerSub)
        {
            List<Item> matchingItems = new List<Item>();
            if (submarine == null) { return matchingItems; }
            if (ItemComponentType != null || ItemIdentifiers.Length > 0)
            {
                matchingItems = ItemIdentifiers.Length > 0 ?
                    Item.ItemList.FindAll(it => ItemIdentifiers.Contains(it.Prefab.Identifier) || it.HasTag(ItemIdentifiers)) :
                    Item.ItemList.FindAll(it => it.Components.Any(ic => ic.GetType() == ItemComponentType));
                if (mustBelongToPlayerSub)
                {
                    matchingItems.RemoveAll(it => it.Submarine?.Info != null && it.Submarine.Info.Type != SubmarineInfo.SubmarineType.Player);
                    matchingItems.RemoveAll(it => it.Submarine != submarine && !submarine.DockedTo.Contains(it.Submarine));
                }
                else
                {
                    matchingItems.RemoveAll(it => it.Submarine != submarine);
                }
                matchingItems.RemoveAll(it => it.NonInteractable);
                if (UseController)
                {
                    matchingItems.RemoveAll(i => i.Components.None(c => c.GetType() == ItemComponentType && TryFindController(c, out _)));
                }
            }
            return matchingItems;
        }

        public List<Item> GetMatchingItems(bool mustBelongToPlayerSub)
        {
            Submarine submarine = Character.Controlled != null && Character.Controlled.TeamID == Character.TeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] :
                Submarine.MainSub;
            return GetMatchingItems(submarine, mustBelongToPlayerSub);
        }
    }
}
