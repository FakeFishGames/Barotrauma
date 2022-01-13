using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System.Collections.Immutable;

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
        public readonly Order Order;
        public readonly string OrderOption;
        public readonly int ManualPriority;
        public readonly OrderType Type;
        public readonly AIObjective Objective;
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

        public OrderInfo(Order order, string orderOption) : this(order, orderOption, CharacterInfo.HighestManualOrderPriority, null) { }

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
        public readonly ImmutableArray<string> TargetItems;
        public readonly ImmutableArray<string> RequireItems;
        private readonly Dictionary<string, ImmutableArray<string>> OptionTargetItems;
        public bool HasOptionSpecificTargetItems => OptionTargetItems != null && OptionTargetItems.Any();

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
        public readonly string[] ControllerTags;
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
        public readonly bool AutoDismiss;

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

        /// <summary>
        /// Affects how high on the order list the order will be placed (i.e. the manual priority order when it's given) when it's first given.
        /// Manually rearranging orders will override this priority.
        /// </summary>
        public int AssignmentPriority { get; }

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
            color = orderElement.GetAttributeColor("color");
            FadeOutTime = orderElement.GetAttributeFloat("fadeouttime", 0.0f);
            UseController = orderElement.GetAttributeBool("usecontroller", false);
            ControllerTags = orderElement.GetAttributeStringArray("controllertags", new string[0]);
            TargetAllCharacters = orderElement.GetAttributeBool("targetallcharacters", false);
            AppropriateJobs = orderElement.GetAttributeStringArray("appropriatejobs", new string[0]);
            Options = orderElement.GetAttributeStringArray("options", new string[0]);
            HiddenOptions = orderElement.GetAttributeStringArray("hiddenoptions", new string[0]);
            AllOptions = Options.Concat(HiddenOptions).ToArray();

            OptionTargetItems = new Dictionary<string, ImmutableArray<string>>();
            if (orderElement.GetAttributeString("targetitems", "") is string targetItems && targetItems.Contains(';'))
            {
                string[] splitTargetItems = targetItems.Split(';');
#if DEBUG
                if (splitTargetItems.Length != AllOptions.Length)
                {
                    DebugConsole.ThrowError($"Order \"{Identifier}\" has option-specific target items, but the option count doesn't match the target item count");
                }
#endif
                var allTargetItems = new List<string>();
                for (int i = 0; i < AllOptions.Length; i++)
                {
                    string[] optionTargetItems = i < splitTargetItems.Length ? splitTargetItems[i].Split(',', '，') : new string[0];
                    for (int j = 0; j < optionTargetItems.Length; j++)
                    {
                        optionTargetItems[j] = optionTargetItems[j].ToLowerInvariant().Trim();
                        allTargetItems.Add(optionTargetItems[j]);
                    }
                    OptionTargetItems.Add(AllOptions[i], optionTargetItems.ToImmutableArray());
                }
                TargetItems = allTargetItems.ToImmutableArray();
            }
            else
            {
                TargetItems = orderElement.GetAttributeStringArray("targetitems", new string[0], trim: true, convertToLowerInvariant: true).ToImmutableArray();
            }
            RequireItems = orderElement.GetAttributeStringArray("requireitems", new string[0], trim: true, convertToLowerInvariant: true).ToImmutableArray();

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
                DebugConsole.AddWarning("Error in Order " + Name + " - the number of option names doesn't match the number of options.");
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
            AutoDismiss = orderElement.GetAttributeBool("autodismiss", Category == OrderCategory.Movement);
            AssignmentPriority = Math.Clamp(orderElement.GetAttributeInt("assignmentpriority", 100), 0, 100);
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
            OptionTargetItems     = prefab.OptionTargetItems;
            RequireItems          = prefab.RequireItems;
            Options               = prefab.Options;
            SymbolSprite          = prefab.SymbolSprite;
            Color                 = prefab.Color;
            UseController         = prefab.UseController;
            ControllerTags        = prefab.ControllerTags;
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
            AssignmentPriority    = prefab.AssignmentPriority;

            OrderGiver = orderGiver;
            TargetEntity = targetEntity;
            if (targetItem != null)
            {
                if (UseController)
                {
                    ConnectedController = targetItem.Item?.FindController(tags: ControllerTags);
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

        public string GetChatMessage(string targetCharacterName, string targetRoomName, bool givingOrderToSelf, string orderOption = "", bool isNewOrder = true)
        {
            if (!TargetAllCharacters && !isNewOrder && Identifier != "dismissed")
            {
                // Use special dialogue when we're rearranging character orders
                return TextManager.GetWithVariable("rearrangedorders", "[name]", targetCharacterName ?? string.Empty, returnNull: true) ?? string.Empty;
            }
            string messageTag = $"{(givingOrderToSelf && !TargetAllCharacters ? "OrderDialogSelf" : "OrderDialog")}.{Identifier}";
            if (!string.IsNullOrEmpty(orderOption))
            {
                if (Identifier != "dismissed")
                {
                    messageTag += $".{orderOption}";
                }
                else
                {
                    string[] splitOption = orderOption.Split('.');
                    if (splitOption.Length > 0)
                    {
                        messageTag += $".{splitOption[0]}";
                    }
                }
            }
            string msg = TextManager.GetWithVariables(messageTag,
                new string[2] { "[name]", "[roomname]" },
                new string[2] { targetCharacterName ?? string.Empty, targetRoomName ?? string.Empty },
                formatCapitals: new bool[2] { false, true },
                returnNull: true);
            return msg ?? string.Empty;
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
        public List<Item> GetMatchingItems(Submarine submarine, bool mustBelongToPlayerSub, CharacterTeamType? requiredTeam = null, Character interactableFor = null, string orderOption = null)
        {
            List<Item> matchingItems = new List<Item>();
            if (submarine == null) { return matchingItems; }
            if (ItemComponentType != null || TargetItems.Any() || RequireItems.Any())
            {
                foreach (var item in Item.ItemList)
                {
                    if (RequireItems.Any() && !TargetItemsMatchItem(RequireItems, item)) { continue; }
                    if (TargetItems.Any() && !TargetItemsMatchItem(item, orderOption)) { continue; }
                    if (RequireItems.None() && TargetItems.None() && !TryGetTargetItemComponent(item, out _)) { continue; }
                    if (mustBelongToPlayerSub && item.Submarine?.Info != null && item.Submarine.Info.Type != SubmarineType.Player) { continue; }
                    if (item.Submarine != submarine && !submarine.DockedTo.Contains(item.Submarine)) { continue; }
                    if (requiredTeam.HasValue && (item.Submarine == null || item.Submarine.TeamID != requiredTeam.Value)) { continue; }
                    if (item.NonInteractable) { continue; }
                    if (ItemComponentType != null && item.Components.None(c => c.GetType() == ItemComponentType)) { continue; }
                    Controller controller = null;
                    if (UseController && !item.TryFindController(out controller, tags: ControllerTags)) { continue; }
                    if (interactableFor != null && (!item.IsInteractable(interactableFor) || (UseController && !controller.Item.IsInteractable(interactableFor)))) { continue; }
                    matchingItems.Add(item);
                }
            }
            return matchingItems;
        }

        /// <param name="interactableFor">Only returns items which are interactable for this character</param>
        public List<Item> GetMatchingItems(bool mustBelongToPlayerSub, Character interactableFor = null, string orderOption = null)
        {
            Submarine submarine = Character.Controlled != null && Character.Controlled.TeamID == CharacterTeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] :
                Submarine.MainSub;
            return GetMatchingItems(submarine, mustBelongToPlayerSub, interactableFor: interactableFor, orderOption: orderOption);
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

        public override string ToString()
        {
            return $"Order ({Name})";
        }

        public ImmutableArray<string> GetTargetItems(string option = null)
        {
            if (string.IsNullOrEmpty(option) || !OptionTargetItems.TryGetValue(option, out ImmutableArray<string> optionTargetItems))
            {
                return TargetItems;
            }
            else
            {
                return optionTargetItems;
            }
        }

        public bool TargetItemsMatchItem(Item item, string option = null)
        {
            if (item == null) { return false; }
            ImmutableArray<string> targetItems = GetTargetItems(option);
            return TargetItemsMatchItem(targetItems, item);
        }

        public static bool TargetItemsMatchItem(ImmutableArray<string> targetItems, Item item)
        {
            return item != null && targetItems != null && targetItems.Length > 0 && (targetItems.Contains(item.Prefab.Identifier) || item.HasTag(targetItems));
        }
    }
}
