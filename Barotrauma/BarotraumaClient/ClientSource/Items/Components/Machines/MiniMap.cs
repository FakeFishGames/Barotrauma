#nullable enable
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma.Items.Components
{
    internal readonly struct MiniMapGUIComponent
    {
        public readonly GUIComponent RectComponent;
        public readonly GUIComponent BorderComponent;

        public MiniMapGUIComponent(GUIComponent rectComponent)
        {
            RectComponent = rectComponent;
            BorderComponent = rectComponent;
        }

        public MiniMapGUIComponent(GUIComponent frame, GUIComponent linkedHullComponent)
        {
            RectComponent = frame;
            BorderComponent = linkedHullComponent;
        }

        public void Deconstruct(out GUIComponent component, out GUIComponent borderComponent)
        {
            component = RectComponent;
            borderComponent = BorderComponent;
        }
    }

    internal readonly struct MiniMapSprite
    {
        public readonly Sprite? Sprite;
        public readonly Color Color;

        public MiniMapSprite(JobPrefab prefab)
        {
            Sprite = prefab.IconSmall;
            Color = prefab.UIColor;
        }

        public MiniMapSprite(Order order)
        {
            Sprite = order.SymbolSprite;
            Color = order.Color;
        }
    }

    internal readonly struct MiniMapHullData
    {
        public readonly List<List<Vector2>> Polygon;
        public readonly (RectangleF Rect, Hull Hull)[] RectDatas;
        public readonly RectangleF Bounds;
        public readonly Point ParentSize;

        public MiniMapHullData(List<List<Vector2>> polygon, RectangleF bounds, Point parentSize, ImmutableArray<RectangleF> rects, ImmutableArray<Hull> hulls)
        {
            ParentSize = parentSize;
            Bounds = bounds;
            Polygon = polygon;
            int count = Math.Min(rects.Length, hulls.Length);
            RectDatas = new (RectangleF Rect, Hull Hull)[count];
            for (int i = 0; i < count; i++)
            {
                RectDatas[i] = (rects[i], hulls[i]);
            }
        }
    }

    internal enum MiniMapMode
    {
        None,
        HullStatus,
        ElectricalView,
        ItemFinder
    }

    internal readonly struct RelativeEntityRect
    {
        public readonly Vector2 RelativePosition;
        public readonly Vector2 RelativeSize;

        public RelativeEntityRect(RectangleF worldBorders, RectangleF entityRect)
        {
            RelativePosition = new Vector2((entityRect.X - worldBorders.X) / worldBorders.Width, (worldBorders.Y - entityRect.Y) / worldBorders.Height);
            RelativeSize = new Vector2(entityRect.Width / worldBorders.Width, entityRect.Height / worldBorders.Height);
        }

        public Vector2 PositionRelativeTo(RectangleF frame, bool skipOffset = false)
        {
            if (skipOffset)
            {
                return RelativePosition * frame.Size;
            }

            return frame.Location + RelativePosition * frame.Size;
        }

        public Vector2 SizeRelativeTo(RectangleF frame)
        {
            return RelativeSize * frame.Size;
        }

        public RectangleF RectangleRelativeTo(RectangleF frame, bool skipOffset = false)
        {
            return new RectangleF(PositionRelativeTo(frame, skipOffset), SizeRelativeTo(frame));
        }

        public void Deconstruct(out float posX, out float posY, out float sizeX, out float sizeY)
        {
            posX = RelativePosition.X;
            posY = RelativePosition.Y;
            sizeX = RelativeSize.X;
            sizeY = RelativeSize.Y;
        }
    }

    internal readonly struct MiniMapSettings
    {
        public static MiniMapSettings Default = new MiniMapSettings
        (
            ignoreOutposts: false,
            createHullElements: true,
            elementColor: MiniMap.MiniMapBaseColor
        );

        public readonly bool IgnoreOutposts;
        public readonly bool CreateHullElements;
        public readonly Color ElementColor;

        public MiniMapSettings(bool ignoreOutposts = false, bool createHullElements = false, Color? elementColor = null)
        {
            IgnoreOutposts = ignoreOutposts;
            CreateHullElements = createHullElements;
            ElementColor = elementColor ?? MiniMap.MiniMapBaseColor;
        }
    }

    partial class MiniMap : Powered
    {
        private GUIFrame submarineContainer;

        private GUIFrame? hullInfoFrame;
        private GUIScissorComponent? scissorComponent;
        private GUIComponent? miniMapContainer;
        private GUIComponent miniMapFrame;
        private GUIComponent electricalFrame;
        private GUILayoutGroup reportFrame;
        private GUILayoutGroup searchBarFrame;
        private GUITextBox searchBar;
        private GUIComponent? searchAutoComplete;

        private ItemPrefab? searchedPrefab;

        private GUITextBlock tooltipHeader, tooltipFirstLine, tooltipSecondLine, tooltipThirdLine;

        private LocalizedString noPowerTip = string.Empty;

        private readonly List<Submarine> displayedSubs = new List<Submarine>();

        private Point prevResolution;
        private float cardRefreshTimer;
        private const float cardRefreshDelay = 3f;

        private readonly HashSet<MiniMapSprite> cardsToDraw = new HashSet<MiniMapSprite>();

        private List<MapEntity> subEntities = new List<MapEntity>();

        private Texture2D? submarinePreview;

        private MiniMapMode currentMode;
        private ImmutableArray<GUIButton> modeSwitchButtons;

        private Point elementSize;

        private ImmutableDictionary<MapEntity, MiniMapGUIComponent> hullStatusComponents;
        private ImmutableDictionary<MapEntity, MiniMapGUIComponent> electricalMapComponents;
        private ImmutableDictionary<MiniMapGUIComponent, GUIComponent> electricalChildren;
        private ImmutableDictionary<MiniMapGUIComponent, GUIComponent> doorChildren;
        private ImmutableDictionary<MiniMapGUIComponent, GUIComponent> weaponChildren;

        private ImmutableHashSet<ItemPrefab>? itemsFoundOnSub;

        private ImmutableHashSet<Vector2>? MiniMapBlips;
        private float blipState;
        private const float maxBlipState = 1f;

        private const float maxZoom = 10f,
                            minZoom = 0.5f,
                            defaultZoom = 1f;

        private float zoom = defaultZoom;

        private float Zoom
        {
            get => zoom;
            set => zoom = Math.Clamp(value, minZoom, maxZoom);
        }

        private Vector2 mapOffset = Vector2.Zero;
        private bool dragMap;
        private Vector2? dragMapStart;
        private const int dragTreshold = 8;

        private bool recalculate;

        public static readonly Color MiniMapBaseColor = new Color(15, 178, 107);

        private static readonly Color WetHullColor = new Color(11, 122, 205),
                                      DoorIndicatorColor = GUIStyle.Green,
                                      NoPowerDoorColor = DoorIndicatorColor * 0.1f,
                                      DefaultNeutralColor = MiniMapBaseColor * 0.8f,
                                      HoverColor = Color.White,
                                      BlueprintBlue = new Color(23, 38, 33),
                                      HullWaterColor = new Color(17, 173, 179) * 0.5f,
                                      HullWaterLineColor = Color.LightBlue * 0.5f,
                                      NoPowerColor = MiniMapBaseColor * 0.1f,
                                      ElectricalBaseColor = GUIStyle.Orange,
                                      NoPowerElectricalColor = ElectricalBaseColor * 0.1f;

        partial void InitProjSpecific()
        {
            SetDefaultMode();

            noPowerTip = TextManager.Get("SteeringNoPowerTip");
            CreateGUI();
        }

        private void SetDefaultMode()
        {
            currentMode = true switch
            {
                true when EnableHullStatus     => MiniMapMode.HullStatus,
                true when EnableElectricalView => MiniMapMode.ElectricalView,
                true when EnableItemFinder     => MiniMapMode.ItemFinder,
                _                              => MiniMapMode.None
            };
        }

        protected override void CreateGUI()
        {
            GuiFrame.ClearChildren();
            TryCreateDragHandle();

            GuiFrame.RectTransform.RelativeOffset = new Vector2(0.05f, 0.0f);
            GuiFrame.CanBeFocused = true;
            var submarineBack = new GUICustomComponent(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset }, DrawHUDBack, null);
            GUIFrame paddedContainer = new GUIFrame(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center), style: null);
            submarineContainer = new GUIFrame(new RectTransform(Vector2.One, paddedContainer.RectTransform, Anchor.Center), style: null);
            var submarineFront = new GUICustomComponent(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset }, DrawHUDFront, null)
            {
                CanBeFocused = false
            };

            GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 0.15f), paddedContainer.RectTransform) { MaxSize = new Point(int.MaxValue, GUI.IntScale(40)) }, isHorizontal: true) { CanBeFocused = true };

            modeSwitchButtons = ImmutableArray.Create
            (
                new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonLayout.RectTransform), string.Empty, style: "StatusMonitorButton.HullStatus") { UserData = MiniMapMode.HullStatus, Enabled = EnableHullStatus, ToolTip = TextManager.Get("StatusMonitorButton.HullStatus.Tooltip") },
                new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonLayout.RectTransform), string.Empty, style: "StatusMonitorButton.ElectricalView") { UserData = MiniMapMode.ElectricalView, Enabled = EnableElectricalView, ToolTip = TextManager.Get("StatusMonitorButton.ElectricalView.Tooltip") },
                new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonLayout.RectTransform), string.Empty, style: "StatusMonitorButton.ItemFinder") { UserData = MiniMapMode.ItemFinder, Enabled = EnableItemFinder, ToolTip = TextManager.Get("StatusMonitorButton.ItemFinder.Tooltip") }
            );

            foreach (GUIButton button in modeSwitchButtons)
            {
                button.OnClicked = (btn, o) =>
                {
                    if (!(o is MiniMapMode m)) { return false; }

                    currentMode = m;
                    Zoom = defaultZoom;
                    mapOffset = Vector2.Zero;
                    recalculate = true;

                    foreach (GUIButton otherButton in modeSwitchButtons)
                    {
                        otherButton.Selected = false;
                    }

                    btn.Selected = true;
                    return true;
                };

                if (button.UserData is MiniMapMode buttonMode)
                {
                    button.Selected = currentMode == buttonMode;
                }
            }

            OrderPrefab[] reports = OrderPrefab.Prefabs.Where(o => o.IsReport && o.SymbolSprite != null && !o.Hidden).ToArray();

            GUIFrame bottomFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.15f), paddedContainer.RectTransform, Anchor.BottomCenter) { MaxSize = new Point(int.MaxValue, GUI.IntScale(40)) }, style: null)
            {
                CanBeFocused = false
            };

            reportFrame = new GUILayoutGroup(new RectTransform(new Vector2(1), bottomFrame.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = GUI.IntScale(5)
            };

            if (reports.Any())
            {
                CrewManager.CreateReportButtons(GameMain.GameSession?.CrewManager, reportFrame, reports, true);
            }

            searchBarFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.5f, 1.0f), bottomFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.Center)
            {
                Visible = false
            };
            searchBar = new GUITextBox(new RectTransform(new Vector2(1), searchBarFrame.RectTransform), string.Empty, createClearButton: true, createPenIcon: true)
            {
                OnEnterPressed = (box, text) =>
                {
                    SearchItems(text);
                    return true;
                }
            };

            searchAutoComplete = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIToolTip")
            {
                Visible = false,
                CanBeFocused = false
            };

            SetAutoCompletePosition(searchAutoComplete, searchBar);

            GUIListBox listBox = new GUIListBox(new RectTransform(Vector2.One, searchAutoComplete.RectTransform))
            {
                PlaySoundOnSelect = true,
                OnSelected = (component, o) =>
                {
                    if (o is ItemPrefab prefab)
                    {
                        searchedPrefab = prefab;
                        searchBar.TextBlock.Text = prefab.Name;
                        searchBar.Deselect();
                        SearchItems(searchBar.Text);
                    }
                    return true;
                }
            };

            foreach (ItemPrefab prefab in ItemPrefab.Prefabs.OrderBy(prefab => prefab.Name))
            {
                if (prefab.HideInMenus) { continue; }
                CreateItemFrame(prefab, listBox.Content.RectTransform);
            }

            searchBar.OnDeselected += (sender, key) =>
            {
                searchAutoComplete.Visible = false;
            };

            searchBar.OnSelected += (sender, key) =>
            {
                itemsFoundOnSub = Item.ItemList.Where(it => VisibleOnItemFinder(it)).Select(it => it.Prefab).ToImmutableHashSet();
            };

            searchBar.OnKeyHit += ControlSearchTooltip;
            searchBar.OnTextChanged += UpdateSearchTooltip;

            hullInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.13f), GUI.Canvas, minSize: new Point(250, 150)), style: "GUIToolTip")
            {
                CanBeFocused = false,
                Visible = false
            };

            var hullInfoContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), hullInfoFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            tooltipHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), hullInfoContainer.RectTransform), string.Empty) { Wrap = true };
            tooltipFirstLine = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), string.Empty) { Wrap = true };
            tooltipSecondLine = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), string.Empty) { Wrap = true };
            tooltipThirdLine = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), string.Empty) { Wrap = true };

            hullInfoFrame.Children.ForEach(c =>
            {
                c.CanBeFocused = false;
                c.Children.ForEach(c2 => c2.CanBeFocused = false);
            });

            submarineBack.RectTransform.MaxSize =
                submarineFront.RectTransform.MaxSize =
                submarineContainer.RectTransform.MaxSize =
                new Point(int.MaxValue, paddedContainer.Rect.Height - bottomFrame.Rect.Height - buttonLayout.Rect.Height);
        }

        private bool VisibleOnItemFinder(Item it)
        {
            if (!item.Submarine.IsEntityFoundOnThisSub(it, includingConnectedSubs: true)) { return false; }
            if (it.NonInteractable || it.HiddenInGame) { return false; }
            if (it.GetComponent<Pickable>() == null) { return false; }

            var holdable = it.GetComponent<Holdable>();
            if (holdable != null && holdable.Attached) { return false; }

            var wire = it.GetComponent<Wire>();
            if (wire != null && wire.Connections.Any(c => c != null)) { return false; }

            if (it.Container?.GetComponent<ItemContainer>() is { DrawInventory: false }) { return false; }

            if (it.HasTag("traitormissionitem")) { return false; }

            return true;
        }

        public override void AddToGUIUpdateList(int order = 0)
        {
            base.AddToGUIUpdateList(order);
            hullInfoFrame?.AddToGUIUpdateList(order: order + 1);
            if (currentMode == MiniMapMode.ItemFinder && searchBar.Selected)
            {
                searchAutoComplete?.AddToGUIUpdateList(order: order + 1);
            }
        }

        private void CreateHUD()
        {
            subEntities.Clear();
            prevResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            submarineContainer.ClearChildren();

            if (item.Submarine is null) { return; }

            scissorComponent = new GUIScissorComponent(new RectTransform(Vector2.One, submarineContainer.RectTransform, Anchor.Center));
            miniMapContainer = new GUIFrame(new RectTransform(Vector2.One, scissorComponent.Content.RectTransform, Anchor.Center), style: null) { CanBeFocused = false };

            ImmutableHashSet<Item> hullPointsOfInterest = Item.ItemList.Where(it => item.Submarine.IsEntityFoundOnThisSub(it, includingConnectedSubs: true) && !it.HiddenInGame && !it.NonInteractable && it.Prefab.ShowInStatusMonitor && (it.GetComponent<Door>() != null || it.GetComponent<Turret>() != null)).ToImmutableHashSet();
            miniMapFrame = CreateMiniMap(item.Submarine, submarineContainer, MiniMapSettings.Default, hullPointsOfInterest, out hullStatusComponents);

            IEnumerable<Item> electrialPointsOfInterest = Item.ItemList.Where(it => item.Submarine.IsEntityFoundOnThisSub(it, includingConnectedSubs: true) && !it.HiddenInGame && !it.NonInteractable && it.GetComponent<Repairable>() != null);
            electricalFrame = CreateMiniMap(item.Submarine, miniMapContainer, new MiniMapSettings(createHullElements: false), electrialPointsOfInterest, out electricalMapComponents);

            Dictionary<MiniMapGUIComponent, GUIComponent> electricChildren = new Dictionary<MiniMapGUIComponent, GUIComponent>();

            foreach (var (entity, component) in electricalMapComponents)
            {
                GUIComponent parent = component.RectComponent;
                if (!(entity is Item it )) { continue; }
                Sprite? sprite = it.Prefab.UpgradePreviewSprite;
                if (sprite is null) { continue; }

                GUIImage child = new GUIImage(new RectTransform(Vector2.One, parent.RectTransform, Anchor.Center), sprite)
                {
                    OutlineColor = ElectricalBaseColor,
                    Color = ElectricalBaseColor,
                    HoverCursor = CursorState.Hand,
                    SpriteEffects = item.Rotation > 90.0f && item.Rotation < 270.0f ? SpriteEffects.FlipVertically : SpriteEffects.None
                };

                electricChildren.Add(component, child);
            }

            electricalChildren = electricChildren.ToImmutableDictionary();

            Dictionary<MiniMapGUIComponent, GUIComponent> doorChilds = new Dictionary<MiniMapGUIComponent, GUIComponent>();
            Dictionary<MiniMapGUIComponent, GUIComponent> weaponChilds = new Dictionary<MiniMapGUIComponent, GUIComponent>();

            foreach (var (entity, component) in hullStatusComponents)
            {
                if (!hullPointsOfInterest.Contains(entity)) { continue; }

                if (!(entity is Item it)) { continue; }
                const int borderMaxSize = 2;

                if (it.GetComponent<Door>() is { })
                {
                    const int minSize = 8;

                    Point size = component.BorderComponent.Rect.Size;

                    size.X = Math.Max(size.X, minSize);
                    size.Y = Math.Max(size.Y, minSize);
                    float width = Math.Min(borderMaxSize, Math.Min(size.X, size.Y) / 8f);

                    GUIFrame frame = new GUIFrame(new RectTransform(size, component.RectComponent.RectTransform, anchor: Anchor.Center), style: "ScanLines", color: DoorIndicatorColor)
                    {
                        OutlineColor = DoorIndicatorColor,
                        OutlineThickness = width
                    };
                    doorChilds.Add(component, frame);
                } 
                else if (it.GetComponent<Turret>() is { } turret)
                {
                    int parentWidth = (int) (submarineContainer.Rect.Width / 16f);
                    GUICustomComponent frame = new GUICustomComponent(new RectTransform(new Point(parentWidth, parentWidth), component.RectComponent.RectTransform, anchor: Anchor.Center), (batch, customComponent) =>
                    {
                        Vector2 center = customComponent.Center;
                        float rotation = turret.Rotation;

                        if (!hasPower)
                        {
                            float minRotation = MathHelper.ToRadians(Math.Min(turret.RotationLimits.X, turret.RotationLimits.Y)),
                                  maxRotation = MathHelper.ToRadians(Math.Max(turret.RotationLimits.X, turret.RotationLimits.Y));

                            rotation = (minRotation + maxRotation) / 2;
                        }

                        if (turret.WeaponIndicatorSprite is { } weaponSprite)
                        {
                            Vector2 origin = weaponSprite.Origin;
                            float scale = parentWidth / Math.Max(weaponSprite.size.X, weaponSprite.size.Y);
                            Color color = !hasPower ? NoPowerColor : turret.ActiveUser is null ? Color.DimGray : GUIStyle.Green;
                            weaponSprite.Draw(batch, center, color, origin, rotation, scale, SpriteEffects.None);
                        }
                    });

                    weaponChilds.Add(component, frame);
                }
            }

            doorChildren = doorChilds.ToImmutableDictionary();
            weaponChildren = weaponChilds.ToImmutableDictionary();

            Rectangle parentRect = miniMapFrame.Rect;

            displayedSubs.Clear();
            displayedSubs.Add(item.Submarine);
            displayedSubs.AddRange(item.Submarine.DockedTo);

            subEntities = MapEntity.mapEntityList.Where(me => (item.Submarine is { } sub && sub.IsEntityFoundOnThisSub(me, includingConnectedSubs: true, allowDifferentType: false)) && !me.HiddenInGame).OrderByDescending(w => w.SpriteDepth).ToList();

            BakeSubmarine(item.Submarine, parentRect);
            elementSize = GuiFrame.Rect.Size;
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            //recreate HUD if the subs we should display have changed
            if (item.Submarine == null && displayedSubs.Count > 0 ||                                         // item not inside a sub anymore, but display is still showing subs
                item.Submarine is { } itemSub &&
                (
                    !displayedSubs.Contains(itemSub) ||                                                                     // current sub not displayed
                    itemSub.DockedTo.Any(s => !displayedSubs.Contains(s) && itemSub.ConnectedDockingPorts[s].IsLocked) ||   // some of the docked subs not displayed
                    displayedSubs.Any(s => s != itemSub && !itemSub.DockedTo.Contains(s))                                   // displaying a sub that shouldn't be displayed
                ) ||
                prevResolution.X != GameMain.GraphicsWidth || prevResolution.Y != GameMain.GraphicsHeight || // resolution changed
                !submarineContainer.Children.Any())                                                          // We lack a GUI
            {
                CreateHUD();
            }

            if (scissorComponent != null)
            {
                if (PlayerInput.PrimaryMouseButtonDown() && currentMode != MiniMapMode.HullStatus)
                {
                    if (GUI.MouseOn == scissorComponent || scissorComponent.IsParentOf(GUI.MouseOn))
                    {
                        dragMapStart = PlayerInput.MousePosition;
                    }
                }
                
                if (currentMode != MiniMapMode.HullStatus && Math.Abs(PlayerInput.ScrollWheelSpeed) > 0 && (GUI.MouseOn == scissorComponent || scissorComponent.IsParentOf(GUI.MouseOn)))
                {
                    float newZoom = Math.Clamp(Zoom + PlayerInput.ScrollWheelSpeed / 1000.0f * Zoom, minZoom, maxZoom);
                    float distanceScale = newZoom / Zoom;
                    mapOffset *= distanceScale;
                    recalculate |= !MathUtils.NearlyEqual(Zoom, newZoom);
                    Zoom = newZoom;
                }
            }

            if (dragMapStart is { } dragStart)
            {
                if (dragMap || Vector2.DistanceSquared(dragStart, PlayerInput.MousePosition) > GUI.IntScale(dragTreshold * dragTreshold))
                {
                    mapOffset.X += PlayerInput.MouseSpeed.X;
                    mapOffset.Y += PlayerInput.MouseSpeed.Y;

                    recalculate = true;
                    dragMap = true;
                }
            }

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                dragMapStart = null;
                dragMap = false;
            }

            if (recalculate)
            {
                if (miniMapContainer != null)
                {
                    miniMapContainer.RectTransform.LocalScale = new Vector2(Zoom);
                    miniMapContainer.RectTransform.RecalculateChildren(true, true);
                    miniMapContainer.RectTransform.AbsoluteOffset = mapOffset.ToPoint();
                }
                recalculate = false;
            }

            // is there a better way to do this?
            if (GuiFrame.Rect.Size != elementSize)
            {
                CreateGUI();
                elementSize = GuiFrame.Rect.Size;
            }

            float distort = 1.0f - item.Condition / item.MaxCondition;
            foreach (HullData hullData in hullDatas.Values)
            {
                hullData.DistortionTimer -= deltaTime;
                if (hullData.DistortionTimer <= 0.0f)
                {
                    hullData.Distort = Rand.Range(0.0f, 1.0f) < distort * distort;
                    if (hullData.Distort)
                    {
                        hullData.ReceivedOxygenAmount = Rand.Range(0.0f, 100.0f);
                        hullData.ReceivedWaterAmount = Rand.Range(0.0f, 100.0f);
                    }
                    hullData.DistortionTimer = Rand.Range(1.0f, 10.0f);
                }
            }

            UpdateHUDBack();

            if (blipState > maxBlipState)
            {
                blipState = 0;
            }

            blipState += deltaTime;

            if (currentMode == MiniMapMode.HullStatus && !EnableHullStatus ||
                currentMode == MiniMapMode.ElectricalView && !EnableElectricalView ||
                currentMode == MiniMapMode.ItemFinder && !EnableItemFinder)
            {
                SetDefaultMode();
            }

            modeSwitchButtons[0].Enabled = EnableHullStatus;
            modeSwitchButtons[1].Enabled = EnableElectricalView;
            modeSwitchButtons[2].Enabled = EnableItemFinder;
        }

        private void UpdateIDCards(Submarine sub)
        {
            if (hullDatas is null) { return; }

            foreach (HullData data in hullDatas.Values)
            {
                data.Cards.Clear();
            }

            foreach (Item it in sub.GetItems(true))
            {
                if (it is { CurrentHull: { } hull } && it.GetComponent<IdCard>() is { } idCard && idCard.TeamID == sub.TeamID)
                {
                    if (!hullDatas.ContainsKey(hull)) { continue; }

                    hullDatas[hull].Cards.Add(idCard);
                }
            }
        }

        private void DrawHUDFront(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (Voltage < MinVoltage)
            {
                Vector2 textSize = GUIStyle.Font.MeasureString(noPowerTip);
                Vector2 textPos = GuiFrame.Rect.Center.ToVector2();
                Color noPowerColor = GUIStyle.Orange * (float)Math.Abs(Math.Sin(Timing.TotalTime));

                GUI.DrawString(spriteBatch, textPos - textSize / 2, noPowerTip, noPowerColor, Color.Black * 0.8f, font: GUIStyle.SubHeadingFont);
                return;
            }

            if (currentMode == MiniMapMode.HullStatus && item.Submarine != null)
            {
                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
                spriteBatch.GraphicsDevice.ScissorRectangle = submarineContainer.Rect;

                var sprite = GUIStyle.UIGlowSolidCircular.Value?.Sprite;
                float alpha = (MathF.Sin(blipState / maxBlipState * MathHelper.TwoPi) + 1.5f) * 0.5f;
                if (sprite != null && ShowHullIntegrity)
                {
                    Vector2 spriteSize = sprite.size;
                    Rectangle worldBorders = item.Submarine.GetDockedBorders();
                    worldBorders.Location += item.Submarine.WorldPosition.ToPoint();
                    foreach (Gap gap in Gap.GapList)
                    {
                        if (gap.IsRoomToRoom || gap.linkedTo.Count == 0 || gap.Submarine != item.Submarine || gap.ConnectedDoor != null || gap.HiddenInGame) { continue; }
                        RectangleF entityRect = ScaleRectToUI(gap, miniMapFrame.Rect, worldBorders);

                        Vector2 scale = new Vector2(entityRect.Size.X / spriteSize.X, entityRect.Size.Y / spriteSize.Y) * 2.0f;

                        Color color = ToolBox.GradientLerp(gap.Open, GUIStyle.HealthBarColorMedium, GUIStyle.HealthBarColorLow) * alpha;
                        sprite.Draw(spriteBatch,
                            miniMapFrame.Rect.Location.ToVector2() + entityRect.Center,
                            color, origin: sprite.Origin, rotate: 0.0f, scale: scale);
                    }
                }                

                if (currentMode == MiniMapMode.HullStatus && hullStatusComponents != null)
                {
                    foreach (var (entity, component) in hullStatusComponents)
                    {
                        if (!(entity is Hull hull)) { continue; }
                        if (!hullDatas.TryGetValue(hull, out HullData? hullData) || hullData is null) { continue; }
                        DrawHullCards(spriteBatch, hull, hullData, component.RectComponent);

                        if (item.CurrentHull is { } currentHull && currentHull == hull)
                        {
                            Sprite pingCircle = GUIStyle.YouAreHereCircle.Value.Sprite;
                            if (pingCircle is null) { continue; }

                            Vector2 charPos = item.WorldPosition;
                            Vector2 hullPos = hull.WorldRect.Location.ToVector2(),
                                    hullSize = hull.WorldRect.Size.ToVector2();
                            Vector2 relativePos = (charPos - hullPos) / hullSize * component.RectComponent.Rect.Size.ToVector2();
                            relativePos.Y = -relativePos.Y;

                            float parentWidth = submarineContainer.Rect.Width / 64f;
                            float spriteSize = pingCircle.size.X * (parentWidth / pingCircle.size.X);

                            Vector2 drawPos = component.RectComponent.Rect.Location.ToVector2() + relativePos;
                            drawPos -= new Vector2(spriteSize, spriteSize) / 2f;

                            pingCircle.Draw(spriteBatch, drawPos, GUIStyle.Red * 0.8f, Vector2.Zero, 0f, parentWidth / pingCircle.size.X);
                        }
                    }
                }

                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }
        }

        private void ControlSearchTooltip(GUITextBox sender, Keys key)
        {
            if (searchAutoComplete is null || !searchAutoComplete.Visible) { return; }
            GUIListBox listBox = searchAutoComplete.GetChild<GUIListBox>();
            if (listBox is null) { return; }

            if (key == Keys.Down)
            {
                listBox.SelectNext(force: GUIListBox.Force.Yes, playSelectSound: GUIListBox.PlaySelectSound.Yes);
            }
            else if (key == Keys.Up)
            {
                listBox.SelectPrevious(force: GUIListBox.Force.Yes, playSelectSound: GUIListBox.PlaySelectSound.Yes);
            }
            else if (key == Keys.Enter)
            {
                listBox.OnSelected?.Invoke(listBox, listBox.SelectedData);
                searchBar.Deselect();
            }
        }

        private bool UpdateSearchTooltip(GUITextBox box, string? text)
        {
            if (text is null || itemsFoundOnSub is null || searchAutoComplete is null) { return false; }

            MiniMapBlips = null;
            searchedPrefab = null;
            searchAutoComplete.Visible = true;
            SetAutoCompletePosition(searchAutoComplete, box);

            GUIListBox? listBox = searchAutoComplete.GetChild<GUIListBox>();
            if (listBox?.Content is null) { return false; }

            bool first = true;

            int i = 0;

            foreach (GUIComponent component in listBox.Content.Children)
            {
                component.Visible = false;
                if (component.UserData is ItemPrefab { Name: { } prefabName} prefab && itemsFoundOnSub.Contains(prefab))
                {
                    component.Visible = prefabName.ToLower().Contains(text.ToLower());

                    if (component.Visible && first)
                    {
                        listBox.Select(i, GUIListBox.Force.Yes, GUIListBox.AutoScroll.Disabled);
                        first = false;
                    }
                }

                i++;
            }

            listBox.BarScroll = 0f;
            listBox.RecalculateChildren();

            return true;
        }

        private void SetAutoCompletePosition(GUIComponent tooltip, GUITextBox box)
        {
            int height = GuiFrame.Rect.Height / 2;
            tooltip.RectTransform.NonScaledSize = new Point(box.Rect.Width, height);
            tooltip.RectTransform.ScreenSpaceOffset = new Point(box.Rect.X, box.Rect.Y - height);
        }

        private void CreateItemFrame(ItemPrefab prefab, RectTransform parent)
        {
            Sprite sprite = prefab.InventoryIcon ?? prefab.Sprite;
            if (sprite is null) { return; }
            GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1f, 0.25f), parent), style: "ListBoxElement")
            {
                UserData = prefab
            };

            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            new GUIImage(new RectTransform(Vector2.One, layout.RectTransform, scaleBasis: ScaleBasis.BothHeight), sprite)
            {
                Color = prefab.InventoryIconColor,
                UserData = prefab
            };
            
            var nameText = new GUITextBlock(new RectTransform(Vector2.One, layout.RectTransform), prefab.Name);
            nameText.RectTransform.SizeChanged += () =>
            {
                nameText.Text = ToolBox.LimitString(prefab.Name, nameText.Font, nameText.Rect.Width);
            };
        }

        private void SearchItems(string text)
        {
            if (searchedPrefab is null)
            {
                ItemPrefab? first = ItemPrefab.Prefabs.FirstOrDefault(p => p.Name.ToLower().Equals(text.ToLower()));

                if (first is null)
                {
                    searchBar.Flash(GUIStyle.Red);
                    return;
                }
                searchedPrefab = first;
            }

            if (item.Submarine is null) { return; }

            HashSet<Item> foundItems = new HashSet<Item>();

            foreach (Item it in Item.ItemList)
            {
                if (!VisibleOnItemFinder(it)) { continue; }

                if (it.Prefab == searchedPrefab)
                {
                    // ignore items on players and hidden inventories
                    if (it.FindParentInventory(inv => inv is CharacterInventory || inv is ItemInventory { Owner: Item { HiddenInGame: true }}) is { }) { continue; }

                    if (it.FindParentInventory(inventory => inventory is ItemInventory { Owner: Item { ParentInventory: null } }) is ItemInventory parent)
                    {
                        foundItems.Add((Item)parent.Owner);
                    }
                    else
                    {
                        foundItems.Add(it);
                    }
                }
            }


            RectangleF dockedBorders = item.Submarine.GetDockedBorders();
            dockedBorders.Location += item.Submarine.WorldPosition;
            RectangleF parentRect = miniMapFrame.Rect;

            HashSet<Vector2> positions = new HashSet<Vector2>();
            foreach (Item foundItem in foundItems)
            {
                RelativeEntityRect scaledRect = new RelativeEntityRect(dockedBorders, foundItem.WorldRect);
                Vector2 pos = scaledRect.PositionRelativeTo(parentRect, skipOffset: true) + scaledRect.SizeRelativeTo(parentRect) / 2f;
                positions.Add(pos);
            }

            MiniMapBlips = positions.ToImmutableHashSet();

            if (searchAutoComplete is null) { return; }
            searchAutoComplete.Visible = false;
        }

        private void UpdateHUDBack()
        {
            if (item.Submarine == null) { return; }

            if (hullInfoFrame != null) { hullInfoFrame.Visible = false; }
            reportFrame.Visible = false;
            searchBarFrame.Visible = false;
            electricalFrame.Visible = false;
            miniMapFrame.Visible = false;

            switch (currentMode)
            {
                case MiniMapMode.HullStatus:
                    UpdateHullStatus();
                    miniMapFrame.Visible = true;
                    reportFrame.Visible = true;
                    break;
                case MiniMapMode.ElectricalView:
                    UpdateElectricalView();
                    electricalFrame.Visible = true;
                    break;
                case MiniMapMode.ItemFinder:
                    searchBarFrame.Visible = true;
                    break;
            }
        }

        private void UpdateHullStatus()
        {
            bool canHoverOverHull = true;

            foreach (var (entity, component) in hullStatusComponents)
            {
                // we are only interested in non-hull components
                if (entity is Hull) { continue; }

                GUIComponent rectComponent = component.RectComponent;

                if (doorChildren.TryGetValue(component, out GUIComponent? child) && child != null)
                {
                    if (item.Submarine == null || !hasPower)
                    {
                        child.Color = child.OutlineColor = NoPowerDoorColor;
                    }

                    if (Voltage < MinVoltage) { continue; }

                    child.Color = child.OutlineColor = DoorIndicatorColor;
                    if (GUI.MouseOn == child)
                    {
                        SetTooltip(rectComponent.Rect.Center, entity.Name, string.Empty, string.Empty, string.Empty);
                        canHoverOverHull = false;
                        child.Color = child.OutlineColor = HoverColor;
                    }
                }
            }

            foreach (var (entity, (component, borderComponent)) in hullStatusComponents)
            {
                if (item.Submarine == null || !hasPower)
                {
                    component.Color = borderComponent.OutlineColor = NoPowerColor;
                }

                if (Voltage < MinVoltage) { continue; }

                if (!component.Visible) { continue; }
                if (!(entity is Hull hull)) { continue; }

                if (!submarineContainer.Rect.Contains(component.Rect))
                {
                    if (hull.Submarine.Info.Type != SubmarineType.Player)
                    {
                        component.Visible = borderComponent.Visible = false;
                        continue;
                    }
                }

                hullDatas.TryGetValue(hull, out HullData? hullData);
                if (hullData is null)
                {
                    hullData = new HullData();
                    GetLinkedHulls(hull, hullData.LinkedHulls);
                    hullDatas.Add(hull, hullData);
                }

                Color neutralColor = DefaultNeutralColor;
                Color borderColor = neutralColor;
                Color componentColor;

                if (hull.IsWetRoom)
                {
                    neutralColor = WetHullColor;
                }

                if (hullData.Distort)
                {
                    borderComponent.OutlineColor = neutralColor * 0.5f;
                    component.Color = Color.Lerp(Color.Black, Color.DarkGray * 0.5f, Rand.Range(0.0f, 1.0f));
                    continue;
                }

                if (RequireOxygenDetectors)
                {
                    hullData.HullOxygenAmount = hullData.ReceivedOxygenAmount;
                }
                else if (hullData.LinkedHulls.Any())
                {
                    hullData.HullOxygenAmount = 0.0f;
                    foreach (Hull linkedHull in hullData.LinkedHulls)
                    {
                        hullData.HullOxygenAmount += linkedHull.OxygenPercentage;
                    }
                    hullData.HullOxygenAmount /= hullData.LinkedHulls.Count;
                }
                else
                {
                    hullData.HullOxygenAmount = hull.OxygenPercentage;
                }
                if (RequireWaterDetectors)
                {
                    hullData.HullWaterAmount = hullData.ReceivedWaterAmount;
                }
                else if (hullData.LinkedHulls.Any())
                {
                    float waterVolume = 0.0f;
                    float totalVolume = 0.0f;
                    foreach (Hull linkedHull in hullData.LinkedHulls)
                    {
                        waterVolume += linkedHull.WaterVolume;
                        totalVolume += linkedHull.Volume;
                    }
                    hullData.HullWaterAmount = MathHelper.Clamp((int)Math.Ceiling(waterVolume / totalVolume * 100), 0, 100);
                }
                else
                {
                    hullData.HullWaterAmount = WaterDetector.GetWaterPercentage(hull);
                }

                float gapOpenSum = 0.0f;

                if (ShowHullIntegrity)
                {
                    float amount = 1f + hullData.LinkedHulls.Count;
                    gapOpenSum = hull.ConnectedGaps.Concat(hullData.LinkedHulls.SelectMany(h => h.ConnectedGaps)).Where(g => !g.IsRoomToRoom && !g.HiddenInGame).Sum(g => g.Open) / amount;
                    borderColor = Color.Lerp(neutralColor, GUIStyle.Red, Math.Min(gapOpenSum, 1.0f));
                }

                bool isHoveringOver = canHoverOverHull && GUI.MouseOn == component;

                // When drawing tooltip we are only interested in the component we are hovering over
                if (isHoveringOver)
                {
                    LocalizedString header = hull.DisplayName;

                    float? oxygenAmount = hullData.HullOxygenAmount,
                           waterAmount = hullData.HullWaterAmount;

                    LocalizedString line1 = gapOpenSum > 0.1f ? TextManager.Get("MiniMapHullBreach") : string.Empty;
                    Color line1Color = GUIStyle.Red;

                    LocalizedString line2 = oxygenAmount == null ?
                        TextManager.Get("MiniMapAirQualityUnavailable") :
                        TextManager.AddPunctuation(':', TextManager.Get("MiniMapAirQuality"), (int)Math.Round(oxygenAmount.Value) + "%");
                    Color line2Color = oxygenAmount == null ? GUIStyle.Red : Color.Lerp(GUIStyle.Red, Color.LightGreen, (float)oxygenAmount / 100.0f);

                    LocalizedString line3 = waterAmount == null ?
                        TextManager.Get("MiniMapWaterLevelUnavailable") :
                        TextManager.AddPunctuation(':', TextManager.Get("MiniMapWaterLevel"), (int)Math.Round(waterAmount.Value) + "%");
                    Color line3Color = waterAmount == null ? GUIStyle.Red : Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)waterAmount / 100.0f);

                    SetTooltip(borderComponent.Rect.Center, header, line1, line2, line3, line1Color, line2Color, line3Color);
                }

                bool draggingReport = GameMain.GameSession?.CrewManager?.DraggedOrderPrefab != null;
                // When setting the colors we want to know the linked hulls too or else the linked hull will not realize its being hovered over and reset the border color
                foreach (Hull linkedHull in hullData.LinkedHulls)
                {
                    if (!hullStatusComponents.ContainsKey(linkedHull)) { continue; }

                    isHoveringOver |=
                        canHoverOverHull &&
                        (hullStatusComponents[linkedHull].RectComponent == GUI.MouseOn || (draggingReport && hullStatusComponents[linkedHull].RectComponent.MouseRect.Contains(PlayerInput.MousePosition)));
                    if (isHoveringOver) { break; }
                }

                if (isHoveringOver || (draggingReport && component.MouseRect.Contains(PlayerInput.MousePosition)))
                {
                    borderColor = Color.Lerp(borderColor, Color.White, 0.5f);
                    componentColor = HoverColor;
                }
                else
                {
                    componentColor = neutralColor * 0.8f;
                }

                borderComponent.OutlineColor = borderColor;
                component.Color = componentColor;
            }
        }

        private void UpdateElectricalView()
        {
            foreach (var (entity, miniMapGuiComponent) in electricalMapComponents)
            {
                if (!(entity is Item it)) { continue; }
                if (!electricalChildren.TryGetValue(miniMapGuiComponent, out GUIComponent? component)) { continue; }

                if (entity.Removed)
                {
                    component.Visible = false;
                    continue;
                }

                if (item.Submarine == null || !hasPower)
                {
                    component.Color = component.OutlineColor = NoPowerElectricalColor;
                }

                if (Voltage < MinVoltage || !miniMapGuiComponent.RectComponent.Visible) { continue; }

                int durability = (int)(it.Condition / (it.MaxCondition / it.MaxRepairConditionMultiplier) * 100f);
                Color color = ToolBox.GradientLerp(durability / 100f, GUIStyle.Red, GUIStyle.Orange, GUIStyle.Green, GUIStyle.Green);

                if (GUI.MouseOn == component)
                {
                    LocalizedString line1 = string.Empty;
                    LocalizedString line2 = string.Empty;

                    if (it.GetComponent<PowerContainer>() is { } battery)
                    {
                        int batteryCapacity = (int)(battery.Charge / battery.GetCapacity() * 100f);
                        line2 = TextManager.GetWithVariable("statusmonitor.battery.tooltip", "[amount]", batteryCapacity.ToString());
                    }
                    else if (it.GetComponent<PowerTransfer>() is { } powerTransfer)
                    {
                        int current = 0, load = 0;
                        if (powerTransfer.PowerConnections.Count > 0 && powerTransfer.PowerConnections[0].Grid != null)
                        {
                            current = (int)powerTransfer.PowerConnections[0].Grid.Power;
                            load = (int)powerTransfer.PowerConnections[0].Grid.Load;
                        }

                        line1 = TextManager.GetWithVariable("statusmonitor.junctionpower.tooltip", "[amount]", current.ToString())
                            .Fallback(TextManager.GetWithVariable("statusmonitor.junctioncurrent.tooltip", "[amount]", current.ToString()));
                        line2 = TextManager.GetWithVariables("statusmonitor.junctionload.tooltip",
                                ("[amount]", load.ToString()),
                                ("[load]", load.ToString()));
                    }

                    LocalizedString line3 = TextManager.GetWithVariable("statusmonitor.durability.tooltip", "[amount]", durability.ToString());
                    SetTooltip(component.Rect.Center, it.Prefab.Name, line1, line2,  line3, line3Color: color);
                    color = HoverColor;
                }

                component.Color = component.OutlineColor = color;
            }
        }

        private void DrawHUDBack(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (item.Submarine == null) { return; }
            
            DrawSubmarine(spriteBatch);            

            if (Voltage < MinVoltage) { return; }
            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            spriteBatch.GraphicsDevice.ScissorRectangle = submarineContainer.Rect;

            if (currentMode == MiniMapMode.ItemFinder)
            {
                if (MiniMapBlips != null)
                {
                    foreach (Vector2 blip in MiniMapBlips)
                    {
                        Vector2 parentSize = miniMapFrame.Rect.Size.ToVector2();
                        Sprite pingCircle = GUIStyle.PingCircle.Value.Sprite;
                        Vector2 targetSize = new Vector2(parentSize.X / 4f);
                        Vector2 spriteScale = targetSize / pingCircle.size;
                        float scale = Math.Min(blipState, maxBlipState / 2f);
                        float alpha = 1.0f - Math.Clamp((blipState - maxBlipState * 0.25f) * 2f, 0f, 1f);
                        pingCircle.Draw(spriteBatch, electricalFrame.Rect.Location.ToVector2() + blip * Zoom, GUIStyle.Red * alpha, pingCircle.Origin, 0f, spriteScale * scale, SpriteEffects.None);
                    }
                }
            }
            else
            {
                bool hullsVisible = currentMode == MiniMapMode.HullStatus && item.Submarine != null;

                if (hullStatusComponents != null)
                {
                    foreach (var (entity, component) in hullStatusComponents)
                    {
                        if (!(entity is Hull hull)) { continue; }
                        if (!hullDatas.TryGetValue(hull, out HullData? hullData) || hullData is null) { continue; }

                        if (hullData.Distort) { continue; }

                        GUIComponent hullFrame = component.RectComponent;

                        if (hullsVisible && hullData.HullWaterAmount is { } waterAmount)
                        {
                            if (!RequireWaterDetectors) { waterAmount = WaterDetector.GetWaterPercentage(hull); }
                            waterAmount /= 100.0f;
                            if (hullFrame.Rect.Height * waterAmount > 1.0f)
                            {
                                RectangleF waterRect = new RectangleF(hullFrame.Rect.X, hullFrame.Rect.Y + hullFrame.Rect.Height * (1.0f - waterAmount), hullFrame.Rect.Width, hullFrame.Rect.Height * waterAmount);

                                const float width = 1f;

                                GUI.DrawFilledRectangle(spriteBatch, waterRect, HullWaterColor);

                                if (!MathUtils.NearlyEqual(waterAmount, 1.0f))
                                {
                                    Vector2 offset = new Vector2(0, width);
                                    GUI.DrawLine(spriteBatch, waterRect.Location + offset, new Vector2(waterRect.Right, waterRect.Y) + offset, HullWaterLineColor, width: width);
                                }
                            }
                        }

                        if (hullsVisible && hullData.HullOxygenAmount is { } oxygenAmount)
                        {
                            GUI.DrawRectangle(spriteBatch, hullFrame.Rect, Color.Lerp(GUIStyle.Red * 0.5f, GUIStyle.Green * 0.3f, oxygenAmount / 100.0f), true);
                        }
                    }
                }
            }

            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
        }

        private void SetTooltip(Point pos, LocalizedString header, LocalizedString line1, LocalizedString line2, LocalizedString line3, Color? line1Color = null, Color? line2Color = null, Color? line3Color = null)
        {
            if (hullInfoFrame == null) { return; }
            hullInfoFrame.RectTransform.ScreenSpaceOffset = pos;

            if (hullInfoFrame.Rect.Left > submarineContainer.Rect.Right) { hullInfoFrame.RectTransform.ScreenSpaceOffset = new Point(submarineContainer.Rect.Right, hullInfoFrame.RectTransform.ScreenSpaceOffset.Y); }
            if (hullInfoFrame.Rect.Top > submarineContainer.Rect.Bottom) { hullInfoFrame.RectTransform.ScreenSpaceOffset = new Point(hullInfoFrame.RectTransform.ScreenSpaceOffset.X, submarineContainer.Rect.Bottom); }

            if (hullInfoFrame.Rect.Right > GameMain.GraphicsWidth) { hullInfoFrame.RectTransform.ScreenSpaceOffset -= new Point(hullInfoFrame.Rect.Width, 0); }
            if (hullInfoFrame.Rect.Bottom > GameMain.GraphicsHeight) { hullInfoFrame.RectTransform.ScreenSpaceOffset -= new Point(0, hullInfoFrame.Rect.Height); }

            hullInfoFrame.Visible = true;
            tooltipHeader.Text = header;

            tooltipFirstLine.Text = line1;
            tooltipFirstLine.TextColor = line1Color ?? GUIStyle.TextColorNormal;

            tooltipSecondLine.Text = line2;
            tooltipSecondLine.TextColor = line2Color ?? GUIStyle.TextColorNormal;

            tooltipThirdLine.Text = line3;
            tooltipThirdLine.TextColor = line3Color ?? GUIStyle.TextColorNormal;
        }

        private void BakeSubmarine(Submarine sub, Rectangle container)
        {
            submarinePreview?.Dispose();
            Rectangle parentRect = new Rectangle(container.X, container.Y, container.Width, container.Height);
            const int inflate = 128;
            parentRect.Inflate(inflate, inflate);
            RenderTarget2D rt = new RenderTarget2D(GameMain.Instance.GraphicsDevice, parentRect.Width, parentRect.Height, false, SurfaceFormat.Color, DepthFormat.None);

            using SpriteBatch spriteBatch = new SpriteBatch(GameMain.Instance.GraphicsDevice);
            GameMain.Instance.GraphicsDevice.SetRenderTarget(rt);
            GameMain.Instance.GraphicsDevice.Clear(Color.Transparent);
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            Rectangle worldBorders = sub.GetDockedBorders();
            worldBorders.Location += sub.WorldPosition.ToPoint();

            parentRect.Inflate(-inflate, -inflate);

            foreach (MapEntity entity in subEntities)
            {
                if (entity is Structure wall)
                {
                    if (wall.IsPlatform) { continue; }
                    DrawStructure(spriteBatch, wall, parentRect, worldBorders, inflate);
                }

                if (entity is Item it)
                {
                    if (it.GetComponent<Pickable>() != null || it.ParentInventory != null) { continue; }
                    DrawItem(spriteBatch, it, parentRect, worldBorders, inflate);
                }
            }

            spriteBatch.End();
            GameMain.Instance.GraphicsDevice.SetRenderTarget(null);
            submarinePreview = rt;
        }

        private void DrawSubmarine(SpriteBatch spriteBatch)
        {
            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            if (submarinePreview is { } texture && miniMapContainer is { } mapContainer)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, blendState: BlendState.NonPremultiplied, effect: GameMain.GameScreen.BlueprintEffect, rasterizerState: GameMain.ScissorTestEnable);
                spriteBatch.GraphicsDevice.ScissorRectangle = submarineContainer.Rect;

                GameMain.GameScreen.BlueprintEffect.Parameters["width"].SetValue((float)texture.Width);
                GameMain.GameScreen.BlueprintEffect.Parameters["height"].SetValue((float)texture.Height);

                Color blueprintBlue = BlueprintBlue * currentMode switch { MiniMapMode.HullStatus => 0.1f, MiniMapMode.ElectricalView => 0.1f, _ => 0.5f };

                Vector2 origin = new Vector2(texture.Width / 2f, texture.Height / 2f);
                float scale = currentMode == MiniMapMode.HullStatus ? 1.0f : Zoom;
                spriteBatch.Draw(texture, mapContainer.Center, null, blueprintBlue, 0f, origin, scale, SpriteEffects.None, 0f);

                spriteBatch.End();
            }
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
        }

        private static void DrawItem(ISpriteBatch spriteBatch, Item item, Rectangle parent, Rectangle border, int inflate)
        {
            Sprite sprite = item.Sprite;
            if (sprite is null) { return; }

            RectangleF entityRect = ScaleRectToUI(item, parent, border);

            Vector2 spriteScale = new Vector2(entityRect.Size.X / sprite.size.X, entityRect.Size.Y / sprite.size.Y);
            Vector2 origin = new Vector2(sprite.Origin.X * spriteScale.X, sprite.Origin.Y * spriteScale.Y);

            if (!item.Prefab.ShowInStatusMonitor && item.GetComponent<Turret>() is { } turret)
            {
                Vector2 drawPos = turret.GetDrawPos();
                drawPos.Y = -drawPos.Y;
                if (turret.BarrelSprite is { } barrelSprite)
                {
                    DrawAdditionalSprite(drawPos, barrelSprite, turret.Rotation + MathHelper.PiOver2);
                }
            }

            Vector2 pos = entityRect.Location + origin;
            pos.X += inflate;
            pos.Y += inflate;

            sprite.Draw(spriteBatch, pos, item.SpriteColor, sprite.Origin, item.RotationRad, spriteScale, item.SpriteEffects);

            void DrawAdditionalSprite(Vector2 basePos, Sprite addSprite, float rotation)
            {
                RectangleF addRect = ScaleRectToUI(new RectangleF(basePos, addSprite.size * item.Scale), parent, border);
                Vector2 addScale = new Vector2(addRect.Size.X / addSprite.size.X, addRect.Size.Y / addSprite.size.Y);
                addSprite.Draw(spriteBatch, new Vector2(addRect.Location.X + inflate, addRect.Location.Y + inflate), item.SpriteColor, addSprite.Origin, rotation, addScale, item.SpriteEffects);
            }
        }

        private static void DrawStructure(ISpriteBatch spriteBatch, Structure structure, Rectangle parent, Rectangle border, int inflate)
        {
            Sprite sprite = structure.Sprite;
            if (sprite is null) { return; }

            RectangleF entityRect = ScaleRectToUI(structure, parent, border);
            Vector2 spriteScale = new Vector2(entityRect.Size.X / sprite.size.X, entityRect.Size.Y / sprite.size.Y);
            sprite.Draw(spriteBatch, new Vector2(entityRect.Location.X + inflate, entityRect.Location.Y + inflate), structure.SpriteColor, Vector2.Zero, 0f, spriteScale, sprite.effects ^ structure.SpriteEffects);
        }

        private static RectangleF ScaleRectToUI(MapEntity entity, RectangleF parentRect, RectangleF worldBorders)
        {
            return ScaleRectToUI(entity.WorldRect, parentRect, worldBorders);
        }

        private static RectangleF ScaleRectToUI(RectangleF rect, RectangleF parentRect, RectangleF worldBorders)
        {
            RelativeEntityRect relativeRect = new RelativeEntityRect(worldBorders, rect);
            return relativeRect.RectangleRelativeTo(parentRect, skipOffset: true);
        }

        private void DrawHullCards(SpriteBatch spriteBatch, Hull hull, HullData data, GUIComponent frame)
        {
            cardsToDraw.Clear();

            if (GameMain.GameSession?.CrewManager is { ActiveOrders: { } orders })
            {
                foreach (var activeOrder in orders)
                {
                    Order order = activeOrder.Order;
                    if (order is { SymbolSprite: { }, TargetEntity: Hull _ } && order.TargetEntity == hull)
                    {
                        cardsToDraw.Add(new MiniMapSprite(order));
                    }
                }
            }

            foreach (IdCard card in data.Cards)
            {
                if (card.OwnerJob is { Icon: { }} job)
                {
                    cardsToDraw.Add(new MiniMapSprite(job));
                }
            }

            if (!cardsToDraw.Any()) { return; }

            var (centerX, centerY) = frame.Center;

            const float padding = 8f;
            float totalWidth = 0f;

            float parentWidth = submarineContainer.Rect.Width / 24f;

            int i = 0;
            foreach (MiniMapSprite info in cardsToDraw)
            {
                if (info.Sprite is null) { continue; }

                float spriteSize = info.Sprite.size.X * (parentWidth / info.Sprite.size.X) + padding;
                if (totalWidth + spriteSize > frame.Rect.Width) { break; }

                totalWidth += spriteSize;
                i++;
            }

            if (i > 0) { totalWidth -= padding; }

            float adjustedCenterX = centerX - totalWidth / 2f;

            float offset = 0;
            int amount = 0;

            foreach (MiniMapSprite info in cardsToDraw)
            {
                Sprite? sprite = info.Sprite;
                if (sprite is null) { continue; }
                float scale = parentWidth / sprite.size.X;
                float spriteSize = sprite.size.X * scale;
                float posX = adjustedCenterX + offset;

                if (posX + spriteSize > frame.Rect.X + frame.Rect.Width && amount > 0)
                {
                    int amountLeft = cardsToDraw.Count - amount;
                    if (amountLeft > 0)
                    {
                        string text = $"+{amountLeft}"; // TODO localization
                        var (sizeX, sizeY) = GUIStyle.SubHeadingFont.MeasureString(text); // TODO expensive, move to a global variable
                        float maxWidth = Math.Max(sizeX, sizeY);
                        Vector2 drawPos = new Vector2(frame.Rect.Right - sizeX, frame.Rect.Y - sizeY / 2f);

                        UISprite icon = GUIStyle.IconOverflowIndicator;
                        if (icon != null)
                        {
                            const int iconPadding = 4;
                            icon.Draw(spriteBatch, new Rectangle((int)drawPos.X - iconPadding, (int)drawPos.Y - iconPadding, (int)maxWidth + iconPadding * 2, (int)maxWidth + iconPadding * 2), Color.White, SpriteEffects.None);
                        }
                        GUI.DrawString(spriteBatch, drawPos, text, GUIStyle.TextColorNormal, font: GUIStyle.SubHeadingFont);
                    }
                    break;
                }

                float halfSize = spriteSize / 2f;
                if (i > 0) { offset += halfSize; }
                Vector2 pos = new Vector2(adjustedCenterX + offset, centerY);
                sprite.Draw(spriteBatch, pos, info.Color * 0.8f, scale: scale, origin: sprite.size / 2f);
                offset += halfSize + padding;
                amount++;
            }
        }

        public static void GetLinkedHulls(Hull hull, List<Hull> linkedHulls)
        {
            foreach (var linkedEntity in hull.linkedTo)
            {
                if (linkedEntity is Hull linkedHull)
                {
                    if (linkedHulls.Contains(linkedHull) || linkedHull.HiddenInGame) { continue; }
                    linkedHulls.Add(linkedHull);
                    GetLinkedHulls(linkedHull, linkedHulls);
                }
            }
        }

        public static GUIFrame CreateMiniMap(Submarine sub, GUIComponent parent, MiniMapSettings settings)
        {
            return CreateMiniMap(sub, parent, settings, null, out _);
        }

        public static GUIFrame CreateMiniMap(Submarine sub, GUIComponent parent, MiniMapSettings settings, IEnumerable<MapEntity>? pointsOfInterest, out ImmutableDictionary<MapEntity, MiniMapGUIComponent> elements)
        {
            if (settings.Equals(default(MiniMapSettings)))
            {
                throw new ArgumentException($"Provided {nameof(MiniMapSettings)} is not valid, did you mean {nameof(MiniMapSettings)}.{nameof(MiniMapSettings.Default)}?", nameof(settings));
            }

            Dictionary<MapEntity, MiniMapGUIComponent> pointsOfInterestCollection = new Dictionary<MapEntity, MiniMapGUIComponent>();

            RectangleF worldBorders = sub.GetDockedBorders();
            worldBorders.Location += sub.WorldPosition;

            // create a container that has the same "aspect ratio" as the sub
            float aspectRatio = worldBorders.Width / worldBorders.Height;
            float parentAspectRatio = parent.Rect.Width / (float)parent.Rect.Height;

            const float elementPadding = 0.9f;

            Vector2 containerScale = parentAspectRatio > aspectRatio ? new Vector2(aspectRatio / parentAspectRatio, 1.0f) : new Vector2(1.0f, parentAspectRatio / aspectRatio);

            GUIFrame hullContainer = new GUIFrame(new RectTransform(containerScale * elementPadding, parent.RectTransform, Anchor.Center), style: null);

            ImmutableHashSet<Submarine> connectedSubs = sub.GetConnectedSubs().ToImmutableHashSet();
            ImmutableArray<Hull> hullList = ImmutableArray<Hull>.Empty;
            ImmutableDictionary<Hull, ImmutableArray<Hull>> combinedHulls = ImmutableDictionary<Hull, ImmutableArray<Hull>>.Empty;

            if (settings.CreateHullElements)
            {
                hullList = Hull.HullList.Where(IsPartofSub).ToImmutableArray();
                combinedHulls = CombinedHulls(hullList);
            }

            // Make components for non-linked hulls
            foreach (Hull hull in hullList.Where(IsStandaloneHull))
            {
                RelativeEntityRect relativeRect = new RelativeEntityRect(worldBorders, hull.WorldRect);

                GUIFrame hullFrame = new GUIFrame(new RectTransform(relativeRect.RelativeSize, hullContainer.RectTransform) { RelativeOffset = relativeRect.RelativePosition }, style: "ScanLines", color: settings.ElementColor)
                {
                    OutlineColor = settings.ElementColor,
                    OutlineThickness = 2,
                    UserData = hull
                };

                pointsOfInterestCollection.Add(hull, new MiniMapGUIComponent(hullFrame));
            }

            // Make components for linked hulls
            foreach (var (mainHull, linkedHulls) in combinedHulls)
            {
                MiniMapHullData data = ConstructHullPolygon(mainHull, linkedHulls, hullContainer, worldBorders);

                RelativeEntityRect relativeRect = new RelativeEntityRect(worldBorders, data.Bounds);

                float highestY = 0f,
                      highestX = 0f;

                foreach (var (r, _) in data.RectDatas)
                {
                    float y = r.Y - -r.Height,
                          x = r.X;

                    if (y > highestY) { highestY = y; }
                    if (x > highestX) { highestX = x; }
                }

                Dictionary<Hull, GUIFrame> hullsAndFrames = new Dictionary<Hull, GUIFrame>();

                foreach (var (snappredRect, hull) in data.RectDatas)
                {
                    RectangleF rect = snappredRect;
                    rect.Height = -rect.Height;
                    rect.Y -= rect.Height;

                    var (parentW, parentH) = hullContainer.Rect.Size.ToVector2();
                    Vector2 size = new Vector2(rect.Width / parentW, rect.Height / parentH);
                    Vector2 pos = new Vector2(rect.X / parentW, rect.Y / parentH);

                    GUIFrame hullFrame = new GUIFrame(new RectTransform(size, hullContainer.RectTransform) { RelativeOffset = pos }, style: "ScanLinesSeamless", color: settings.ElementColor)
                    {
                        UserData = hull,
                        UVOffset = new Vector2(highestX - rect.X, highestY - rect.Y)
                    };

                    hullsAndFrames.Add(hull, hullFrame);
                }

                /*
                 * This exists because the rectangle of GUIComponents still uses Rectangle instead of RectangleF
                 * and because of rounding sometimes it creates 1px gaps between which looks nasty so we snap
                 * the rectangles together if they are 2 pixels apart or less.
                 */
                foreach (var (hull1, frame1) in hullsAndFrames)
                {
                    Rectangle rect1 = frame1.Rect;
                    foreach (var (hull2, frame2) in hullsAndFrames)
                    {
                        if (hull2 == hull1) { continue; }

                        Rectangle rect2 = frame2.Rect;
                        Point size = frame1.RectTransform.NonScaledSize;

                        const int treshold = 2;

                        int diffY = rect2.Top - rect1.Bottom;
                        int diffX = rect2.Left - rect1.Right;

                        if (diffY <= treshold && diffY > 0)
                        {
                            size.Y += diffY;
                        }

                        if (diffX <= treshold && diffX > 0)
                        {
                            size.X += diffX;
                        }

                        frame1.RectTransform.NonScaledSize = size;
                    }
                }

                GUICustomComponent linkedHullFrame = new GUICustomComponent(new RectTransform(relativeRect.RelativeSize, hullContainer.RectTransform) { RelativeOffset = relativeRect.RelativePosition }, (spriteBatch, component) =>
                {
                    foreach (List<Vector2> list in data.Polygon)
                    {
                        spriteBatch.DrawPolygonInner(hullContainer.Rect.Location.ToVector2(), list,  component.OutlineColor, 2f);
                    }
                }, (deltaTime, component) =>
                {
                    if (component.Parent.Rect.Size != data.ParentSize)
                    {
                        data = ConstructHullPolygon(mainHull, linkedHulls, hullContainer, worldBorders);
                    }
                })
                {
                    UserData = hullsAndFrames.Values.ToHashSet(),
                    OutlineColor = settings.ElementColor,
                    CanBeFocused = false
                };

                foreach (var (hull, component) in hullsAndFrames)
                {
                    pointsOfInterestCollection.Add(hull, new MiniMapGUIComponent(component, linkedHullFrame));
                }
            }

            if (pointsOfInterest != null)
            {
                foreach (MapEntity entity in pointsOfInterest)
                {
                    RelativeEntityRect relativeRect = new RelativeEntityRect(worldBorders, entity.WorldRect);

                    GUIFrame poiComponent = new GUIFrame(new RectTransform(relativeRect.RelativeSize, hullContainer.RectTransform) { RelativeOffset = relativeRect.RelativePosition }, style: null)
                    {
                        CanBeFocused = false,
                        UserData = entity
                    };

                    pointsOfInterestCollection.Add(entity, new MiniMapGUIComponent(poiComponent));
                }
            }

            elements = pointsOfInterestCollection.ToImmutableDictionary();

            return hullContainer;

            bool IsPartofSub(MapEntity entity)
            {
                if (entity.Submarine != sub && !connectedSubs.Contains(entity.Submarine) || entity.HiddenInGame) { return false; }
                return !settings.IgnoreOutposts || sub.IsEntityFoundOnThisSub(entity, true);
            }

            bool IsStandaloneHull(Hull hull)
            {
                return !combinedHulls.ContainsKey(hull) && !combinedHulls.Values.Any(hh => hh.Contains(hull));
            }
        }

        private static ImmutableDictionary<Hull, ImmutableArray<Hull>> CombinedHulls(ImmutableArray<Hull> hulls)
        {
            Dictionary<Hull, HashSet<Hull>> combinedHulls = new Dictionary<Hull, HashSet<Hull>>();

            foreach (Hull hull in hulls)
            {
                if (combinedHulls.ContainsKey(hull) || combinedHulls.Values.Any(hh => hh.Contains(hull))) { continue; }

                List<Hull> linkedHulls = new List<Hull>();
                GetLinkedHulls(hull, linkedHulls);

                linkedHulls.Remove(hull);

                foreach (Hull linkedHull in linkedHulls)
                {
                    if (!combinedHulls.ContainsKey(hull))
                    {
                        combinedHulls.Add(hull, new HashSet<Hull>());
                    }

                    combinedHulls[hull].Add(linkedHull);
                }
            }

            return combinedHulls.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableArray());
        }

        private static MiniMapHullData ConstructHullPolygon(Hull mainHull, ImmutableArray<Hull> linkedHulls, GUIComponent parent, RectangleF worldBorders)
        {
            Rectangle parentRect = parent.Rect;

            Dictionary<Hull, Rectangle> rects = new Dictionary<Hull, Rectangle>();
            Rectangle worldRect = mainHull.WorldRect;
            worldRect.Y = -worldRect.Y;

            rects.Add(mainHull, worldRect);

            foreach (Hull hull in linkedHulls)
            {
                Rectangle rect = hull.WorldRect;
                rect.Y = -rect.Y;

                worldRect = Rectangle.Union(worldRect, rect);
                rects.Add(hull, rect);
            }

            worldRect.Y = -worldRect.Y;

            List<RectangleF> normalizedRects = new List<RectangleF>();
            List<Hull> hullRefs = new List<Hull>();

            foreach (var (hull, rect) in rects)
            {
                Rectangle wRect = rect;
                wRect.Y = -wRect.Y;

                var (posX, posY, sizeX, sizeY) = new RelativeEntityRect(worldBorders, wRect);

                RectangleF newRect = new RectangleF(posX * parentRect.Width, posY * parentRect.Height, sizeX * parentRect.Width, sizeY * parentRect.Height);

                normalizedRects.Add(newRect);
                hullRefs.Add(hull);
            }

            hullRefs.Reverse(); // I have no idea why this is required

            ImmutableArray<RectangleF> snappedRectangles = ToolBox.SnapRectangles(normalizedRects, treshold: 1);

            List<List<Vector2>> polygon = ToolBox.CombineRectanglesIntoShape(snappedRectangles);

            List<List<Vector2>> scaledPolygon = new List<List<Vector2>>();

            foreach (List<Vector2> list in polygon)
            {
                // scale down the polygon just a tiny bit
                var (polySizeX, polySizeY) = ToolBox.GetPolygonBoundingBoxSize(list);
                float sizeX = polySizeX - 1f,
                      sizeY = polySizeY - 1f;

                scaledPolygon.Add(ToolBox.ScalePolygon(list, new Vector2(sizeX / polySizeX, sizeY / polySizeY)));
            }

            return new MiniMapHullData(scaledPolygon, worldRect, parentRect.Size, snappedRectangles, hullRefs.ToImmutableArray());
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            if (searchAutoComplete != null)
            {
                searchAutoComplete.RectTransform.Parent = null;
                searchAutoComplete = null;
            }
            if (hullInfoFrame != null)
            {
                hullInfoFrame.RectTransform.Parent = null;
                hullInfoFrame = null;
            }
        }
    }
}
