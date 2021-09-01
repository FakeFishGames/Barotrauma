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

    struct OrderInfo
    {
        public Order Order { get; }
        public string OrderOption { get; }
        public int ManualPriority { get; }
        public OrderType Type { get; }
        public AIObjective Objective { get; }
        public bool IsCurrentOrder => Type == OrderType.Current;

        public enum OrderType
        {
            Current,
            Previous
        }

        private OrderInfo(Order order, string orderOption, int manualPriority, OrderType orderType, AIObjective objective)
        {
            Order = order;
            OrderOption = orderOption;
            ManualPriority = Math.Min(manualPriority, CharacterInfo.HighestManualOrderPriority);
            Type = orderType;
            Objective = objective;
        }

        public OrderInfo(Order order, string orderOption, int manualPriority) : this(order, orderOption, manualPriority, OrderType.Current, null) { }

        public OrderInfo(Order order, string orderOption, int manualPriority, AIObjective objective) : this(order, orderOption, manualPriority, OrderType.Current, objective) { }

        public OrderInfo(OrderInfo orderInfo, int manualPriority) : this(orderInfo.Order, orderInfo.OrderOption, manualPriority, orderInfo.Type, orderInfo.Objective) { }

        public OrderInfo(OrderInfo orderInfo, OrderType type) : this(orderInfo.Order, orderInfo.OrderOption, orderInfo.ManualPriority, type, orderInfo.Objective) { }

        public bool MatchesOrder(string orderIdentifier, string orderOption) =>
            (orderIdentifier == Order?.Identifier || (string.IsNullOrEmpty(orderIdentifier) && string.IsNullOrEmpty(Order?.Identifier))) &&
            (orderOption == OrderOption || (string.IsNullOrEmpty(orderOption) && string.IsNullOrEmpty(OrderOption)));

        public bool MatchesOrder(Order order, string option) =>
            MatchesOrder(order?.Identifier, option);

        public bool MatchesOrder(OrderInfo orderInfo) =>
            MatchesOrder(orderInfo.Order?.Identifier, orderInfo.OrderOption);

        public bool MatchesDismissedOrder(string dismissOrderOption)
        {
            string[] dismissedOrder = dismissOrderOption?.Split('.');
            if (dismissedOrder != null && dismissedOrder.Length > 0)
            {
                string dismissedOrderIdentifier = dismissedOrder.Length > 0 ? dismissedOrder[0] : null;
                if (dismissedOrderIdentifier == null || dismissedOrderIdentifier != Order?.Identifier) { return false; }
                string dismissedOrderOption = dismissedOrder.Length > 1 ? dismissedOrder[1] : null;
                if (dismissedOrderOption == null && string.IsNullOrEmpty(OrderOption)) { return true; }
                return dismissedOrderOption == OrderOption;
            }
            else
            {
                return false;
            }
        }
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
        /// <summary>
        /// Name that can be used with the contextual version of the order
        /// </summary>
        public readonly string ContextualName;

        public readonly Sprite SymbolSprite;

        public readonly Type ItemComponentType;
        public readonly bool CanTypeBeSubclass;
        public readonly string[] TargetItems;

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
        public bool TargetAllCharacters { get; }
        public bool IsReport => TargetAllCharacters && !MustSetTarget;

        public readonly float FadeOutTime;

        public Entity TargetEntity;
        public ItemComponent TargetItemComponent;
        public readonly bool UseController;
        public Controller ConnectedController;

        public Character OrderGiver;

        public OrderCategory? Category { get; private set; }

        //legacy support
        public readonly string[] AppropriateJobs;
        public readonly string[] Options;
        public readonly string[] HiddenOptions;
        public readonly string[] AllOptions;
        private readonly Dictionary<string, string> OptionNames;

        public readonly Dictionary<string, Sprite> OptionSprites;

        public readonly bool MustSetTarget;
        /// <summary>
        /// Can the order be turned into a non-entity-targeting one if it was originally created with a target entity.
        /// Note: if MustSetTarget is true, CanBeGeneralized will always be false.
        /// </summary>
        public readonly bool CanBeGeneralized;
        public readonly string AppropriateSkill;
        public readonly bool Hidden;
        public readonly bool IgnoreAtOutpost;

        public bool HasOptions => (IsPrefab ? Options : Prefab.Options).Length > 1;
        public bool IsPrefab { get; private set; }
        public readonly bool MustManuallyAssign;

        public readonly OrderTarget TargetPosition;

        private ISpatialEntity targetSpatialEntity;
        public ISpatialEntity TargetSpatialEntity
        {
            get
            {
                if (targetSpatialEntity == null)
                {
                    if (TargetType == OrderTargetType.WallSection && WallSectionIndex.HasValue)
                    {
                        targetSpatialEntity = (TargetEntity as Structure)?.Sections[WallSectionIndex.Value];
                    }
                    else
                    {
                        targetSpatialEntity = TargetEntity ?? TargetPosition as ISpatialEntity;
                    }
                }
                return targetSpatialEntity;
            }
        }

        public enum OrderTargetType
        {
            Entity,
            Position,
            WallSection
        }
        public OrderTargetType TargetType { get; }
        public int? WallSectionIndex { get; }
        public bool IsIgnoreOrder { get; }

        /// <summary>
        /// Should the order icon be drawn when the order target is inside a container
        /// </summary>
        public bool DrawIconWhenContained { get; }

        public static void Init()
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
            Name = TextManager.Get("OrderName." + Identifier, returnNull: true) ?? "Name not found";
            ContextualName = TextManager.Get("OrderNameContextual." + Identifier, returnNull: true) ?? Name;

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
            CanTypeBeSubclass = orderElement.GetAttributeBool("cantypebesubclass", false);

            TargetItems = orderElement.GetAttributeStringArray("targetitems", new string[0], trim: true, convertToLowerInvariant: true);
            color = orderElement.GetAttributeColor("color");
            FadeOutTime = orderElement.GetAttributeFloat("fadeouttime", 0.0f);
            UseController = orderElement.GetAttributeBool("usecontroller", false);
            TargetAllCharacters = orderElement.GetAttributeBool("targetallcharacters", false);
            AppropriateJobs = orderElement.GetAttributeStringArray("appropriatejobs", new string[0]);
            Options = orderElement.GetAttributeStringArray("options", new string[0]);
            HiddenOptions = orderElement.GetAttributeStringArray("hiddenoptions", new string[0]);
            AllOptions = Options.Concat(HiddenOptions).ToArray();
            var category = orderElement.GetAttributeString("category", null);
            if (!string.IsNullOrWhiteSpace(category)) { this.Category = (OrderCategory)Enum.Parse(typeof(OrderCategory), category, true); }
            MustSetTarget = orderElement.GetAttributeBool("mustsettarget", false);
            CanBeGeneralized = !MustSetTarget && orderElement.GetAttributeBool("canbegeneralized", true);
            AppropriateSkill = orderElement.GetAttributeString("appropriateskill", null);
            Hidden = orderElement.GetAttributeBool("hidden", false);
            IgnoreAtOutpost = orderElement.GetAttributeBool("ignoreatoutpost", false);

            var optionNames = TextManager.Get("OrderOptions." + Identifier, true)?.Split(',', '，') ??
                orderElement.GetAttributeStringArray("optionnames", new string[0]);
            OptionNames = new Dictionary<string, string>();
            for (int i = 0; i < Options.Length && i < optionNames.Length; i++)
            {
                OptionNames.Add(Options[i], optionNames[i].Trim());
            }
            if (OptionNames.Count != Options.Length)
            {
                DebugConsole.ThrowError("Error in Order " + Name + " - the number of option names doesn't match the number of options.");
                OptionNames.Clear();
                Options.ForEach(o => OptionNames.Add(o, o));
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
            IsIgnoreOrder = Identifier == "ignorethis" || Identifier == "unignorethis";
            DrawIconWhenContained = orderElement.GetAttributeBool("displayiconwhencontained", false);
        }

        /// <summary>
        /// Constructor for order instances
        /// </summary>
        public Order(Order prefab, Entity targetEntity, ItemComponent targetItem, Character orderGiver = null)
        {
            Prefab = prefab.Prefab ?? prefab;

            Name                  = prefab.Name;
            ContextualName        = prefab.ContextualName;
            Identifier            = prefab.Identifier;
            ItemComponentType     = prefab.ItemComponentType;
            CanTypeBeSubclass     = prefab.CanTypeBeSubclass;
            TargetItems           = prefab.TargetItems;
            Options               = prefab.Options;
            SymbolSprite          = prefab.SymbolSprite;
            Color                 = prefab.Color;
            UseController         = prefab.UseController;
            TargetAllCharacters   = prefab.TargetAllCharacters;
            AppropriateJobs       = prefab.AppropriateJobs;
            FadeOutTime           = prefab.FadeOutTime;
            MustSetTarget         = prefab.MustSetTarget;
            CanBeGeneralized      = prefab.CanBeGeneralized;
            AppropriateSkill      = prefab.AppropriateSkill;
            Category              = prefab.Category;
            MustManuallyAssign    = prefab.MustManuallyAssign;
            IsIgnoreOrder         = prefab.IsIgnoreOrder;
            DrawIconWhenContained = prefab.DrawIconWhenContained;
            Hidden                = prefab.Hidden;
            IgnoreAtOutpost       = prefab.IgnoreAtOutpost;

            OrderGiver = orderGiver;
            TargetEntity = targetEntity;
            if (targetItem != null)
            {
                if (UseController)
                {
                    ConnectedController = targetItem.Item?.FindController();
                    if (ConnectedController == null)
                    {
                        DebugConsole.AddWarning("AI: Tried to use a controller for operating an item, but couldn't find any.");
                        UseController = false;
                    }
                }
                TargetEntity = targetItem.Item;
                TargetItemComponent = targetItem;
            }

            TargetType = OrderTargetType.Entity;

            IsPrefab = false;
        }

        /// <summary>
        /// Constructor for order instances
        /// </summary>
        public Order(Order prefab, OrderTarget target, Character orderGiver = null) : this(prefab, targetEntity: null, targetItem: null, orderGiver)
        {
            TargetPosition = target;
            TargetType = OrderTargetType.Position;
        }

        /// <summary>
        /// Constructor for order instances
        /// </summary>
        public Order(Order prefab, Structure wall, int? sectionIndex, Character orderGiver = null) : this(prefab, targetEntity: wall, null, orderGiver: orderGiver)
        {
            WallSectionIndex = sectionIndex;
            TargetType = OrderTargetType.WallSection;
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
            if (Identifier != "dismissed" && !string.IsNullOrEmpty(orderOption)) { messageTag += "." + orderOption; }

            if (targetCharacterName == null) { targetCharacterName = ""; }
            if (targetRoomName == null) { targetRoomName = ""; }
            string msg = TextManager.GetWithVariables(messageTag, new string[2] { "[name]", "[roomname]" }, new string[2] { targetCharacterName, targetRoomName }, new bool[2] { false, true }, true);
            if (msg == null) { return ""; }

            return msg;
        }

        /// <summary>
        /// Get the target item component based on the target item type
        /// </summary>
        public ItemComponent GetTargetItemComponent(Item item)
        {
            if (item?.Components == null || ItemComponentType == null) { return null; }
            foreach (ItemComponent component in item.Components)
            {
                if (component?.GetType() is Type componentType)
                {
                    if (componentType == ItemComponentType) { return component; }
                    if (CanTypeBeSubclass && componentType.IsSubclassOf(ItemComponentType)) { return component; }
                }
            }
            return null;
        }

        public bool TryGetTargetItemComponent(Item item, out ItemComponent firstMatchingComponent)
        {
            firstMatchingComponent = GetTargetItemComponent(item);
            return firstMatchingComponent != null;
        }

        /// <param name="interactableFor">Only returns items which are interactable for this character</param>
        public List<Item> GetMatchingItems(Submarine submarine, bool mustBelongToPlayerSub, CharacterTeamType? requiredTeam = null, Character interactableFor = null)
        {
            List<Item> matchingItems = new List<Item>();
            if (submarine == null) { return matchingItems; }
            if (ItemComponentType != null || TargetItems.Length > 0)
            {
                foreach (var item in Item.ItemList)
                {
                    if (TargetItems.Length > 0 && !TargetItems.Contains(item.Prefab.Identifier) && !item.HasTag(TargetItems)) { continue; }
                    if (TargetItems.Length == 0 && !TryGetTargetItemComponent(item, out _)) { continue; }
                    if (mustBelongToPlayerSub && item.Submarine?.Info != null && item.Submarine.Info.Type != SubmarineType.Player) { continue; }
                    if (item.Submarine != submarine && !submarine.DockedTo.Contains(item.Submarine)) { continue; }
                    if (requiredTeam.HasValue && (item.Submarine == null || item.Submarine.TeamID != requiredTeam.Value)) { continue; }
                    if (item.NonInteractable) { continue; }
                    if (ItemComponentType != null && item.Components.None(c => c.GetType() == ItemComponentType)) { continue; }
                    Controller controller = null;
                    if (UseController && !item.TryFindController(out controller)) { continue; }
                    if (interactableFor != null && (!item.IsInteractable(interactableFor) || (UseController && !controller.Item.IsInteractable(interactableFor)))) { continue; }
                    matchingItems.Add(item);
                }
            }
            return matchingItems;
        }


        /// <param name="interactableFor">Only returns items which are interactable for this character</param>
        public List<Item> GetMatchingItems(bool mustBelongToPlayerSub, Character interactableFor = null)
        {
            Submarine submarine = Character.Controlled != null && Character.Controlled.TeamID == CharacterTeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] :
                Submarine.MainSub;
            return GetMatchingItems(submarine, mustBelongToPlayerSub, interactableFor: interactableFor);
        }

        public string GetOptionName(string id)
        {
            if (Prefab == null)
            {
                if (OptionNames.ContainsKey(id)) { return OptionNames[id]; }
            }
            else
            {
                if (Prefab.OptionNames.ContainsKey(id)) { return Prefab.OptionNames[id]; }
            }
            return string.Empty;
        }

        public string GetOptionName(int index)
        {
            if (index < 0 || index >= Options.Length) { return null; }
            return GetOptionName(Options[index]);
        }

        /// <summary>
        /// Used to create the order option for the Dismiss order to know which order it targets
        /// </summary>
        /// <param name="orderInfo">The order to target with the dismiss order</param>
        public static string GetDismissOrderOption(OrderInfo orderInfo)
        {
            if (orderInfo.Order != null)
            {
                string option = orderInfo.Order.Identifier;
                if (!string.IsNullOrEmpty(orderInfo.OrderOption))
                {
                    option += $".{orderInfo.OrderOption}";
                }
                return option;
            }
            return "";
        }
    }
}
