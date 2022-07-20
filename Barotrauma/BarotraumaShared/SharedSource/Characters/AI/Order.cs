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

    class OrderCategoryIcon : Prefab
    {
        public readonly static PrefabCollection<OrderCategoryIcon> OrderCategoryIcons = new PrefabCollection<OrderCategoryIcon>();

        public OrderCategoryIcon(ContentXElement element, OrdersFile file) : base(file, element.GetAttributeIdentifier("category", ""))
        {
            Category = Enum.Parse<OrderCategory>(Identifier.Value, true);
            var spriteElement = element.GetChildElement("sprite");
            Sprite = new Sprite(spriteElement, lazyLoad: true);
            Color = element.GetAttributeColor("color", Color.White);
        }

        public readonly OrderCategory Category;
        public readonly Sprite Sprite;
        public readonly Color Color;

        public override void Dispose() { Sprite?.Remove(); }
    }

    class OrderPrefab : PrefabWithUintIdentifier
    {
        public readonly static PrefabCollection<OrderPrefab> Prefabs = new PrefabCollection<OrderPrefab>();

        public readonly static Identifier DismissalIdentifier = "dismissed".ToIdentifier();
        public static OrderPrefab Dismissal => Prefabs[DismissalIdentifier];

        public readonly OrderCategory? Category;
        public readonly Identifier CategoryIdentifier;

        public readonly LocalizedString Name;
        /// <summary>
        /// Name that can be used with the contextual version of the order
        /// </summary>
        public readonly LocalizedString ContextualName;

        public readonly Sprite SymbolSprite;

        public readonly Type ItemComponentType;
        public readonly bool CanTypeBeSubclass;
        public readonly ImmutableArray<Identifier> TargetItems;
        public readonly ImmutableArray<Identifier> RequireItems;
        private readonly ImmutableDictionary<Identifier, ImmutableArray<Identifier>> OptionTargetItems;
        public bool HasOptionSpecificTargetItems => OptionTargetItems != null && OptionTargetItems.Any();

        private readonly Color? color;
        public Color Color
        {
            get
            {
                if (color.HasValue)
                {
                    return color.Value;
                }
                else if (OrderCategoryIcon.OrderCategoryIcons.ContainsKey(CategoryIdentifier))
                {
                    return OrderCategoryIcon.OrderCategoryIcons[Category.ToIdentifier()].Color;
                }
                else
                {
                    return Color.White;
                }
            }
        }

        //if true, the order is issued to all available characters
        public readonly bool TargetAllCharacters;
        public bool IsReport => TargetAllCharacters && !MustSetTarget;

        public bool IsDismissal => Identifier == DismissalIdentifier;

        public readonly float FadeOutTime;

        public readonly bool UseController;

        public readonly ImmutableArray<Identifier> ControllerTags;

        /// <summary>
        /// If defined, the order can only be quick-assigned to characters with these jobs. Or if it's a report, the icon will only be displayed to characters with these jobs.
        /// </summary>
        public readonly ImmutableArray<Identifier> AppropriateJobs;
        public readonly ImmutableArray<Identifier> Options;
        public readonly ImmutableArray<Identifier> HiddenOptions;
        public readonly ImmutableArray<Identifier> AllOptions;
        public readonly ListDictionary<Identifier, LocalizedString> OptionNames;

        public readonly ImmutableDictionary<Identifier, Sprite> OptionSprites;

        public readonly bool MustSetTarget;
        /// <summary>
        /// Can the order be turned into a non-entity-targeting one if it was originally created with a target entity.
        /// Note: if MustSetTarget is true, CanBeGeneralized will always be false.
        /// </summary>
        public readonly bool CanBeGeneralized;
        public readonly Identifier AppropriateSkill;
        public readonly bool Hidden;
        public readonly bool IgnoreAtOutpost;

        public bool HasOptions => Options.Length > 1;
        public readonly bool MustManuallyAssign;
        public readonly bool AutoDismiss;

        /// <summary>
        /// If defined, the order will be quick-assigned to characters with these jobs before characters with other jobs.
        /// </summary>
        public readonly ImmutableArray<Identifier> PreferredJobs;

        public enum OrderTargetType
        {
            Entity,
            Position,
            WallSection
        }
        public OrderTargetType TargetType { get; }
        public int? WallSectionIndex { get; }
        public bool IsIgnoreOrder => Identifier == "ignorethis" || Identifier == "unignorethis";

        /// <summary>
        /// Should the order icon be drawn when the order target is inside a container
        /// </summary>
        public bool DrawIconWhenContained { get; }

        /// <summary>
        /// Affects how high on the order list the order will be placed (i.e. the manual priority order when it's given) when it's first given.
        /// Manually rearranging orders will override this priority.
        /// </summary>
        public int AssignmentPriority { get; }

        public bool ColoredWhenControllingGiver { get; }
        public bool DisplayGiverInTooltip { get; }

        public OrderPrefab(ContentXElement orderElement, OrdersFile file) : base(file, orderElement.GetAttributeIdentifier("identifier", ""))
        {
            Name = TextManager.Get($"OrderName.{Identifier}");
            ContextualName = TextManager.Get($"OrderNameContextual.{Identifier}");

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
            ControllerTags = orderElement.GetAttributeIdentifierArray("controllertags", Array.Empty<Identifier>()).ToImmutableArray();
            TargetAllCharacters = orderElement.GetAttributeBool("targetallcharacters", false);
            AppropriateJobs = orderElement.GetAttributeIdentifierArray("appropriatejobs", Array.Empty<Identifier>()).ToImmutableArray();
            PreferredJobs = orderElement.GetAttributeIdentifierArray("preferredjobs", Array.Empty<Identifier>()).ToImmutableArray();
            Options = orderElement.GetAttributeIdentifierArray("options", Array.Empty<Identifier>()).ToImmutableArray();
            HiddenOptions = orderElement.GetAttributeIdentifierArray("hiddenoptions", Array.Empty<Identifier>()).ToImmutableArray();
            AllOptions = Options.Concat(HiddenOptions).ToImmutableArray();
            
            var optionTargetItems = new Dictionary<Identifier, ImmutableArray<Identifier>>();
            if (orderElement.GetAttributeString("targetitems", "") is string targetItems && targetItems.Contains(';'))
            {
                string[] splitTargetItems = targetItems.Split(';');
#if DEBUG
                if (splitTargetItems.Length != AllOptions.Length)
                {
                    DebugConsole.ThrowError($"Order \"{Identifier}\" has option-specific target items, but the option count doesn't match the target item count");
                }
#endif
                var allTargetItems = new List<Identifier>();
                for (int i = 0; i < AllOptions.Length; i++)
                {
                    Identifier[] optionTargetItemsSplit = i < splitTargetItems.Length ? splitTargetItems[i].Split(',', '，').ToIdentifiers() : Array.Empty<Identifier>();
                    for (int j = 0; j < optionTargetItemsSplit.Length; j++)
                    {
                        optionTargetItemsSplit[j] = optionTargetItemsSplit[j].Value.Trim().ToIdentifier();
                        allTargetItems.Add(optionTargetItemsSplit[j]);
                    }
                    optionTargetItems.Add(AllOptions[i], optionTargetItemsSplit.ToImmutableArray());
                }
                TargetItems = allTargetItems.ToImmutableArray();
            }
            else
            {
                TargetItems = orderElement.GetAttributeIdentifierArray("targetitems", Array.Empty<Identifier>(), trim: true).ToImmutableArray();
            }
            RequireItems = orderElement.GetAttributeIdentifierArray("requireitems", Array.Empty<Identifier>(), trim: true).ToImmutableArray();
            OptionTargetItems = optionTargetItems.ToImmutableDictionary();
            
            var category = orderElement.GetAttributeString("category", null);
            this.Category = !string.IsNullOrWhiteSpace(category) ? Enum.Parse<OrderCategory>(category, true) : (OrderCategory?)null;
            this.CategoryIdentifier = (this.Category?.ToString() ?? string.Empty).ToIdentifier();
            MustSetTarget = orderElement.GetAttributeBool("mustsettarget", false);
            CanBeGeneralized = !MustSetTarget && orderElement.GetAttributeBool("canbegeneralized", true);
            AppropriateSkill = orderElement.GetAttributeIdentifier("appropriateskill", Identifier.Empty);
            Hidden = orderElement.GetAttributeBool("hidden", false);
            IgnoreAtOutpost = orderElement.GetAttributeBool("ignoreatoutpost", false);

            OptionNames =
                new ListDictionary<Identifier, LocalizedString>(
                    TextManager.Get("OrderOptions." + Identifier).Split(',', '，'), Options.Length, i => Options[i]);

            var spriteElement = orderElement.GetChildElement("sprite");
            if (spriteElement != null)
            {
                SymbolSprite = new Sprite(spriteElement, lazyLoad: true);
            }

            var optionSprites = new Dictionary<Identifier, Sprite>();
            if (Options != null && Options.Length > 0)
            {
                var optionSpriteElements = orderElement.GetChildElement("optionsprites")?.GetChildElements("sprite");
                if (optionSpriteElements != null && optionSpriteElements.Any())
                {
                    for (int i = 0; i < Options.Length; i++)
                    {
                        if (i >= optionSpriteElements.Count()) { break; };
                        var sprite = new Sprite(optionSpriteElements.ElementAt(i), lazyLoad: true);
                        optionSprites.Add(Options[i], sprite);
                    }
                }
            }
            OptionSprites = optionSprites.ToImmutableDictionary();

            MustManuallyAssign = orderElement.GetAttributeBool("mustmanuallyassign", false);
            DrawIconWhenContained = orderElement.GetAttributeBool("displayiconwhencontained", false);
            AutoDismiss = orderElement.GetAttributeBool("autodismiss", Category == OrderCategory.Operate || Category == OrderCategory.Movement);
            AssignmentPriority = Math.Clamp(orderElement.GetAttributeInt("assignmentpriority", 100), 0, 100);
            ColoredWhenControllingGiver = orderElement.GetAttributeBool("coloredwhencontrollinggiver", false);
            DisplayGiverInTooltip = orderElement.GetAttributeBool("displaygiverintooltip", false);
        }

        private bool HasSpecifiedJob(Character character, IReadOnlyList<Identifier> jobs)
        {
            if (jobs == null || jobs.Count == 0) { return false; }
            Identifier jobIdentifier = character?.Info?.Job?.Prefab?.Identifier ?? Identifier.Empty;
            if (jobIdentifier.IsEmpty) { return false; }
            for (int i = 0; i < jobs.Count; i++)
            {
                if (jobIdentifier == jobs[i]) { return true; }
            }
            return false;
        }

        public bool HasAppropriateJob(Character character) => HasSpecifiedJob(character, AppropriateJobs);

        public bool HasPreferredJob(Character character) => HasSpecifiedJob(character, PreferredJobs);

        public string GetChatMessage(string targetCharacterName, string targetRoomName, bool givingOrderToSelf, Identifier orderOption = default, bool isNewOrder = true)
        {
            if (!TargetAllCharacters && !isNewOrder && Identifier != "dismissed")
            {
                // Use special dialogue when we're rearranging character orders
                if (!givingOrderToSelf)
                {
                    return TextManager.GetWithVariable("rearrangedorders", "[name]", targetCharacterName ?? string.Empty).Value;
                }
                else
                {
                    // Say nothing when rearranging the orders of the character you're controlling
                    return string.Empty;
                }
            }
            string messageTag = $"{(givingOrderToSelf && !TargetAllCharacters ? "OrderDialogSelf" : "OrderDialog")}.{Identifier}";
            if (!orderOption.IsEmpty)
            {
                if (Identifier != "dismissed")
                {
                    messageTag += $".{orderOption}";
                }
                else
                {
                    string[] splitOption = orderOption.Value.Split('.');
                    if (splitOption.Length > 0)
                    {
                        messageTag += $".{splitOption[0]}";
                    }
                }
            }
            return TextManager.GetWithVariables(messageTag,
                ("[name]", targetCharacterName ?? string.Empty, FormatCapitals.No),
                ("[roomname]", targetRoomName ?? string.Empty, FormatCapitals.Yes)).Fallback("").Value;
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
        public List<Item> GetMatchingItems(Submarine submarine, bool mustBelongToPlayerSub, CharacterTeamType? requiredTeam = null, Character interactableFor = null, Identifier orderOption = default)
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
        public List<Item> GetMatchingItems(bool mustBelongToPlayerSub, Character interactableFor = null, Identifier orderOption = default)
        {
            Submarine submarine = Character.Controlled != null && Character.Controlled.TeamID == CharacterTeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] :
                Submarine.MainSub;
            return GetMatchingItems(submarine, mustBelongToPlayerSub, interactableFor: interactableFor, orderOption: orderOption);
        }

        public LocalizedString GetOptionName(string id)
        {
            return GetOptionName(id.ToIdentifier());
        }

        public LocalizedString GetOptionName(Identifier id)
        {
            if (OptionNames.ContainsKey(id)) { return OptionNames[id]; }
            return string.Empty;
        }

        public LocalizedString GetOptionName(int index)
        {
            if (index < 0 || index >= Options.Length) { return null; }
            return OptionNames[Options[index]];
        }

        /// <summary>
        /// Used to create the order option for the Dismiss order to know which order it targets
        /// </summary>
        /// <param name="order">The order to target with the dismiss order</param>
        public static Identifier GetDismissOrderOption(Order order)
        {
            Identifier option = order.Identifier;
            if (order.Option != Identifier.Empty)
            {
                option = $"{option}.{order.Option}".ToIdentifier();
            }
            return option;
        }

        public ImmutableArray<Identifier> GetTargetItems(Identifier option = default)
        {
            if (option.IsEmpty || !OptionTargetItems.TryGetValue(option, out ImmutableArray<Identifier> optionTargetItems))
            {
                return TargetItems;
            }
            else
            {
                return optionTargetItems;
            }
        }

        public bool TargetItemsMatchItem(Item item, Identifier option = default)
        {
            if (item == null) { return false; }
            ImmutableArray<Identifier> targetItems = GetTargetItems(option);
            return TargetItemsMatchItem(targetItems, item);
        }

        public static bool TargetItemsMatchItem(ImmutableArray<Identifier> targetItems, Item item)
        {
            return item != null && targetItems != null && targetItems.Length > 0 && (targetItems.Contains(item.Prefab.Identifier) || item.HasTag(targetItems));
        }
        
        public override void Dispose() { }

        /// <summary>
        /// Create an Order instance with a null target
        /// </summary>
        public Order CreateInstance(OrderTargetType targetType, Character orderGiver = null, bool isAutonomous = false)
        {
            try
            {
                return targetType switch
                {
                    OrderTargetType.Entity => new Order(this, targetEntity: null, targetItem: null, orderGiver, isAutonomous),
                    OrderTargetType.Position => new Order(this, target: null, orderGiver),
                    OrderTargetType.WallSection => new Order(this, wall: null, sectionIndex: null, orderGiver),
                    _ => throw new NotImplementedException()
                };
            }
            catch (NotImplementedException e)
            {
                DebugConsole.ShowError($"Error creating a new Order instance: unexpected target type \"{targetType}\".\n{e.StackTrace.CleanupStackTrace()}");
                return null;
            }
        }
    }

    class Order
    {
        public readonly OrderPrefab Prefab;
        public readonly Identifier Option;
        public readonly int ManualPriority;
        public readonly OrderType Type;
        public readonly AIObjective Objective;
        public bool IsCurrentOrder => Type == OrderType.Current;
        public bool IsDismissal => Prefab.IsDismissal;

        public enum OrderType
        {
            Current,
            Previous
        }

        public readonly Entity TargetEntity;
        public readonly ItemComponent TargetItemComponent;
        public readonly Controller ConnectedController;

        public readonly Character OrderGiver;

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

        public Hull TargetHull => TargetEntity as Hull;

        public enum OrderTargetType
        {
            Entity,
            Position,
            WallSection
        }
        public readonly OrderTargetType TargetType;
        public readonly int? WallSectionIndex;

        public LocalizedString Name => Prefab.Name;
        public LocalizedString ContextualName => Prefab.ContextualName;
        public Identifier Identifier => Prefab.Identifier;
        public Type ItemComponentType => Prefab.ItemComponentType;
        public bool CanTypeBeSubclass => Prefab.CanTypeBeSubclass;
        public ref readonly ImmutableArray<Identifier> ControllerTags => ref Prefab.ControllerTags;
        public ref readonly ImmutableArray<Identifier> TargetItems => ref Prefab.TargetItems;
        public ref readonly ImmutableArray<Identifier> RequireItems => ref Prefab.RequireItems;
        public ref readonly ImmutableArray<Identifier> Options => ref Prefab.Options;
        public ref readonly ImmutableArray<Identifier> HiddenOptions => ref Prefab.HiddenOptions;
        public ref readonly ImmutableArray<Identifier> AllOptions => ref Prefab.AllOptions;
        public Sprite SymbolSprite => Prefab.SymbolSprite;
        public Color Color => Prefab.Color;
        public bool TargetAllCharacters => Prefab.TargetAllCharacters;
        public ref readonly ImmutableArray<Identifier> AppropriateJobs => ref Prefab.AppropriateJobs;
        public float FadeOutTime => Prefab.FadeOutTime;
        public bool MustSetTarget => Prefab.MustSetTarget;
        public Identifier AppropriateSkill => Prefab.AppropriateSkill;
        public OrderCategory? Category => Prefab.Category;
        public bool MustManuallyAssign => Prefab.MustManuallyAssign;
        public bool IsIgnoreOrder => Prefab.IsIgnoreOrder;
        public bool DrawIconWhenContained => Prefab.DrawIconWhenContained;
        public bool Hidden => Prefab.Hidden;
        public bool IgnoreAtOutpost => Prefab.IgnoreAtOutpost;
        public bool IsReport => Prefab.IsReport;
        public bool AutoDismiss => Prefab.AutoDismiss;
        public int AssignmentPriority => Prefab.AssignmentPriority;
        
        public bool ColoredWhenControllingGiver => Prefab.ColoredWhenControllingGiver;
        public bool DisplayGiverInTooltip => Prefab.DisplayGiverInTooltip;


        public readonly bool UseController;

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.Entity
        /// </summary>
        public Order(OrderPrefab prefab, Entity targetEntity, ItemComponent targetItem, Character orderGiver = null, bool isAutonomous = false)
            : this(prefab, Identifier.Empty, 0, OrderType.Current, null, targetEntity, targetItem, orderGiver, isAutonomous) { }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.Entity
        /// </summary>
        public Order(OrderPrefab prefab, Identifier option, Entity targetEntity, ItemComponent targetItem, Character orderGiver = null, bool isAutonomous = false)
            : this(prefab, option, 0, OrderType.Current, null, targetEntity, targetItem, orderGiver, isAutonomous) { }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.Position
        /// </summary>
        public Order(OrderPrefab prefab, OrderTarget target, Character orderGiver = null)
            : this(prefab, prefab.Options.FirstOrDefault(), 0, OrderType.Current, null, target, orderGiver) { }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.Position
        /// </summary>
        public Order(OrderPrefab prefab, Identifier option, OrderTarget target, Character orderGiver = null)
            : this(prefab, option, 0, OrderType.Current, null, target, orderGiver) { }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.WallSection
        /// </summary>
        public Order(OrderPrefab prefab, Structure wall, int? sectionIndex, Character orderGiver = null)
            : this(prefab, Identifier.Empty, 0, OrderType.Current, null, wall, sectionIndex, orderGiver) { }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.WallSection
        /// </summary>
        public Order(OrderPrefab prefab, Identifier option, Structure wall, int? sectionIndex, Character orderGiver = null)
            : this(prefab, option, 0, OrderType.Current, null, wall, sectionIndex, orderGiver) { }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.Entity
        /// </summary>
        private Order(OrderPrefab prefab, Identifier option, int manualPriority, OrderType orderType, AIObjective aiObjective, Entity targetEntity, ItemComponent targetItem, Character orderGiver = null, bool isAutonomous = false)
        {
            Prefab = prefab;
            Option = option;
            ManualPriority = manualPriority;
            Type = orderType;
            Objective = aiObjective;

            UseController = Prefab.UseController;

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
        }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.Position
        /// </summary>
        private Order(OrderPrefab prefab, Identifier option, int manualPriority, OrderType orderType, AIObjective aiObjective, OrderTarget target, Character orderGiver = null)
            : this(prefab, option, manualPriority, orderType, aiObjective, targetEntity: null, targetItem: null, orderGiver)
        {
            TargetPosition = target;
            TargetType = OrderTargetType.Position;
        }

        /// <summary>
        /// Constructor for orders with the target type OrderTargetType.WallSection
        /// </summary>
        private Order(OrderPrefab prefab, Identifier option, int manualPriority, OrderType orderType, AIObjective aiObjective, Structure wall, int? sectionIndex, Character orderGiver = null)
            : this(prefab, option, manualPriority, orderType, aiObjective, targetEntity: wall, null, orderGiver: orderGiver)
        {
            WallSectionIndex = sectionIndex;
            TargetType = OrderTargetType.WallSection;
        }

        private Order(
            Order other,
            OrderPrefab prefab = null,
            Identifier option = default,
            int? manualPriority = null,
            OrderType? type = null,
            AIObjective objective = null,
            Entity targetEntity = null,
            ItemComponent targetItemComponent = null,
            Controller connectedController = null,
            Character orderGiver = null,
            OrderTarget targetPosition = null,
            OrderTargetType? targetType = null,
            int? wallSectionIndex = null,
            bool? useController = null)
        {
            Prefab = prefab ?? other.Prefab;
            Option = option.IfEmpty(other.Option);
            ManualPriority = manualPriority ?? other.ManualPriority;
            Type = type ?? other.Type;
            Objective = objective ?? other.Objective;

            TargetEntity = targetEntity ?? other.TargetEntity;
            TargetItemComponent = targetItemComponent ?? other.TargetItemComponent;
            ConnectedController = connectedController ?? other.ConnectedController;

            OrderGiver = orderGiver ?? other.OrderGiver;

            TargetPosition = targetPosition ?? other.TargetPosition;

            TargetType = targetType ?? other.TargetType;
            WallSectionIndex = wallSectionIndex ?? other.WallSectionIndex;

            UseController = useController ?? other.UseController;
        }

        public Order WithOption(Identifier option)
        {
            return new Order(this, option: option);
        }

        public Order WithManualPriority(int newPriority)
        {
            return new Order(this, manualPriority: newPriority);
        }

        public Order WithOrderGiver(Character orderGiver)
        {
            return new Order(this, orderGiver: orderGiver);
        }

        public Order WithObjective(AIObjective objective)
        {
            return new Order(this, objective: objective);
        }

        public Order WithTargetEntity(Entity entity)
        {
            return new Order(this, targetEntity: entity, targetType: OrderTargetType.Entity);
        }

        public Order WithTargetSpatialEntity(ISpatialEntity spatialEntity)
        {
            if (spatialEntity is WallSection wallSection)
            {
                Structure wall = wallSection.Wall;
                int sectionIndex = wall.Sections.IndexOf(wallSection);
                return WithWallSection(wall, sectionIndex);
            }
            else if (spatialEntity is Entity entity)
            {
                return WithTargetEntity(entity);
            }
            else if (spatialEntity is OrderTarget orderTarget)
            {
                return WithTargetPosition(orderTarget);
            }

            throw new InvalidOperationException($"Unexpected input type: {spatialEntity.GetType().Name}");
        }

        public Order WithItemComponent(Item item, ItemComponent component = null)
        {
            return new Order(this, targetEntity: item, targetItemComponent: component ?? GetTargetItemComponent(item));
        }

        public Order WithWallSection(Structure wall, int? sectionIndex)
        {
            return new Order(this, targetEntity: wall, wallSectionIndex: sectionIndex, targetType: OrderTargetType.WallSection);
        }

        public Order WithType(OrderType type)
        {
            return new Order(this, type: type);
        }

        public Order WithTargetPosition(OrderTarget targetPosition)
        {
            return new Order(this, targetPosition: targetPosition, targetType: OrderTargetType.Position);
        }

        public Order Clone()
        {
            return new Order(this);
        }

        public Order GetDismissal()
        {
            if (IsDismissal) { throw new InvalidOperationException("Attempted to dismiss a dismissal order"); }
            return new Order(this, prefab: OrderPrefab.Prefabs["dismissed"], option: GetDismissOrderOption(this));
        }
        
        public bool HasAppropriateJob(Character character)
            => Prefab.HasAppropriateJob(character);
        
        public bool HasPreferredJob(Character character)
            => Prefab.HasPreferredJob(character);

        public string GetChatMessage(
            string targetCharacterName, string targetRoomName, bool givingOrderToSelf, Identifier orderOption = default, bool isNewOrder = true)
            => Prefab.GetChatMessage(targetCharacterName, targetRoomName, givingOrderToSelf, orderOption, isNewOrder);

        /// <summary>
        /// Get the target item component based on the target item type
        /// </summary>
        public ItemComponent GetTargetItemComponent(Item item)
        {
            return Prefab.GetTargetItemComponent(item);
        }

        public bool TryGetTargetItemComponent(Item item, out ItemComponent firstMatchingComponent)
        {
            return Prefab.TryGetTargetItemComponent(item, out firstMatchingComponent);
        }

        /// <param name="interactableFor">Only returns items which are interactable for this character</param>
        public List<Item> GetMatchingItems(Submarine submarine, bool mustBelongToPlayerSub, CharacterTeamType? requiredTeam = null, Character interactableFor = null)
        {
            return Prefab.GetMatchingItems(submarine, mustBelongToPlayerSub, requiredTeam, interactableFor);
        }


        /// <param name="interactableFor">Only returns items which are interactable for this character</param>
        public List<Item> GetMatchingItems(bool mustBelongToPlayerSub, Character interactableFor = null)
        {
            return Prefab.GetMatchingItems(mustBelongToPlayerSub, interactableFor);
        }

        public LocalizedString GetOptionName(string id)
        {
            return Prefab.GetOptionName(id);
        }

        public LocalizedString GetOptionName(Identifier id)
        {
            return Prefab.GetOptionName(id);
        }

        public LocalizedString GetOptionName(int index)
        {
            return Prefab.GetOptionName(index);
        }

        /// <summary>
        /// Used to create the order option for the Dismiss order to know which order it targets
        /// </summary>
        /// <param name="orderInfo">The order to target with the dismiss order</param>
        public static Identifier GetDismissOrderOption(Order order)
        {
            return OrderPrefab.GetDismissOrderOption(order);
        }

        public bool MatchesOrder(Identifier orderIdentifier, Identifier orderOption) =>
            orderIdentifier == Identifier && orderOption == Option;

        /*public bool MatchesOrder(Order order, Identifier option) =>
            order != null && MatchesOrder(order.Identifier, option);*/

        public bool MatchesOrder(Order order) =>
            order != null && MatchesOrder(order.Identifier, order.Option);

        public bool MatchesDismissedOrder(Identifier dismissOrderOption)
        {
            Identifier[] dismissedOrder = dismissOrderOption.Value.Split('.').Select(s => s.ToIdentifier()).ToArray();
            if (dismissedOrder != null && dismissedOrder.Length > 0)
            {
                Identifier dismissedOrderIdentifier = dismissedOrder.Length > 0 ? dismissedOrder[0] : Identifier.Empty;
                if (dismissedOrderIdentifier == Identifier.Empty || dismissedOrderIdentifier != Identifier) { return false; }
                Identifier dismissedOrderOption = dismissedOrder.Length > 1 ? dismissedOrder[1] : Identifier.Empty;
                if (dismissedOrderOption == Identifier.Empty && Option == Identifier.Empty) { return true; }
                return dismissedOrderOption == Option;
            }
            else
            {
                return false;
            }
        }

        public ImmutableArray<Identifier> GetTargetItems(Identifier option = default)
            => Prefab.GetTargetItems(option);

        public override string ToString()
        {
            return $"Order ({Name})";
        }
    }
}
