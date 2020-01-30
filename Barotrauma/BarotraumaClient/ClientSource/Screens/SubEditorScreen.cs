using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SubEditorScreen : Screen
    {
        private static readonly string[] crewExperienceLevels = new string[] 
        {
            "CrewExperienceLow",
            "CrewExperienceMid",
            "CrewExperienceHigh"
        };

        public enum Mode
        {
            Default,
            Character,
            Wiring
        }

        private readonly Point defaultPreviewImageSize = new Point(640, 368);

        private readonly Camera cam;

        private Point screenResolution;

        private bool lightingEnabled;

        public GUIComponent TopPanel;
        private GUIComponent showEntitiesPanel, entityCountPanel;
        private List<GUITickBox> showEntitiesTickBoxes = new List<GUITickBox>();

        private bool entityMenuOpen = true;
        private float entityMenuOpenState = 1.0f;
        public GUIComponent EntityMenu;
        private GUITextBox entityFilterBox;
        private GUIListBox entityList;
        private GUIButton toggleEntityMenuButton;

        private GUIComponent loadFrame, saveFrame;

        private GUITextBox nameBox, descriptionBox;

        private readonly List<GUIButton> entityCategoryButtons = new List<GUIButton>();

        private GUIFrame hullVolumeFrame;

        private GUIFrame saveAssemblyFrame;

        const int PreviouslyUsedCount = 10;
        private GUIFrame previouslyUsedPanel;
        private GUIListBox previouslyUsedList;

        private GUIDropDown linkedSubBox;

        private GUIRadioButtonGroup modeButtonGroup;

        //a Character used for picking up and manipulating items
        private Character dummyCharacter;

        private GUIFrame wiringToolPanel;

        private DateTime editorSelectedTime;

        private readonly string containerDeleteTag = "containerdelete";

        private GUIImage previewImage;
        
        private GUIListBox contextMenu;

        private const int submarineNameLimit = 30;
        private GUITextBlock submarineNameCharacterCount;

        private const int submarineDescriptionLimit = 500;
        private GUITextBlock submarineDescriptionCharacterCount;

        private Mode mode;

        public override Camera Cam
        {
            get { return cam; }
        }

        public string GetSubName()
        {
            return (Submarine.MainSub == null) ? "" : Submarine.MainSub.Name;
        }

        public string GetSubDescription()
        {
            string localizedDescription = TextManager.Get("submarine.description." + GetSubName(), true);
            if (localizedDescription != null) return localizedDescription;
            return (Submarine.MainSub == null) ? "" : Submarine.MainSub.Description;
        }

        private string GetTotalHullVolume()
        {
            return TextManager.Get("TotalHullVolume") + ":\n" + Hull.hullList.Sum(h => h.Volume);
        }

        private string GetSelectedHullVolume()
        {
            float buoyancyVol = 0.0f;
            float selectedVol = 0.0f;
            float neutralPercentage = SubmarineBody.NeutralBallastPercentage;
            Hull.hullList.ForEach(h =>
            {
                buoyancyVol += h.Volume;
                if (h.IsSelected)
                {
                    selectedVol += h.Volume;
                }
            });
            buoyancyVol *= neutralPercentage;
            string retVal = TextManager.Get("SelectedHullVolume") + ":\n" + selectedVol;
            if (selectedVol > 0.0f && buoyancyVol > 0.0f)
            {
                if (buoyancyVol / selectedVol < 1.0f)
                {
                    retVal += " (" + TextManager.GetWithVariable("OptimalBallastLevel", "[value]", (buoyancyVol / selectedVol).ToString("0.000")) + ")";
                }
                else
                {
                    retVal += " (" + TextManager.Get("InsufficientBallast") + ")";
                }
            }
            return retVal;
        }

        public bool CharacterMode { get { return mode == Mode.Character; } }

        public bool WiringMode { get { return mode == Mode.Wiring; } }

        public SubEditorScreen()
        {
            cam = new Camera();
            WayPoint.ShowWayPoints = false;
            WayPoint.ShowSpawnPoints = false;
            Hull.ShowHulls = false;
            Gap.ShowGaps = false;
            CreateUI();
        }

        private void CreateUI()
        {
            TopPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), GUI.Canvas) { MinSize = new Point(0, 35) }, "GUIFrameTop");

            GUILayoutGroup paddedTopPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.8f), TopPanel.RectTransform, Anchor.Center),
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.005f
            };

            new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonToggleLeft")
            {
                ToolTip = TextManager.Get("back"),
                OnClicked = (b, d) =>
                {
                    var msgBox = new GUIMessageBox("", TextManager.Get("PauseMenuQuitVerificationEditor"), new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                    {
                        UserData = "verificationprompt"
                    };
                    msgBox.Buttons[0].OnClicked = (yesBtn, userdata) =>
                    {
                        GUIMessageBox.CloseAll();
                        GameMain.MainMenuScreen.Select();
                        return true;
                    };
                    msgBox.Buttons[0].OnClicked += msgBox.Close;
                    msgBox.Buttons[1].OnClicked = (_, userdata) =>
                    {
                        msgBox.Close();
                        return true;
                    };
                    return true;
                }
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "OpenButton")
            {
                ToolTip = TextManager.Get("OpenSubButton"),
                OnClicked = (GUIButton btn, object data) =>
                {
                    saveFrame = null;
                    CreateLoadScreen();

                    return true;
                }
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");
            
            new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "SaveButton")
            {
                ToolTip = TextManager.Get("SaveSubButton"),
                OnClicked = (GUIButton btn, object data) =>
                {
                    loadFrame = null;
                    CreateSaveScreen();

                    return true;
                }
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            var nameLabel = new GUITextBlock(new RectTransform(new Vector2(0.1f, 0.9f), paddedTopPanel.RectTransform, Anchor.CenterLeft),
                "", font: GUI.LargeFont, textAlignment: Alignment.CenterLeft)
            {
                TextGetter = GetSubName
            };

            linkedSubBox = new GUIDropDown(new RectTransform(new Vector2(0.15f, 0.9f), paddedTopPanel.RectTransform),
                TextManager.Get("AddSubButton"), elementCount: 20)
            {
                ToolTip = TextManager.Get("AddSubToolTip")
            };
            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                linkedSubBox.AddItem(sub.Name, sub);
            }
            linkedSubBox.OnSelected += SelectLinkedSub;
            linkedSubBox.OnDropped += (component, obj) =>
            {
                MapEntity.SelectedList.Clear();
                return true;
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            modeButtonGroup = new GUIRadioButtonGroup();

            var defaultModeTickBox = new GUITickBox(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "EditSubButton")
            {
                ToolTip = TextManager.Get("SubEditorEditingMode"),
                OnSelected = (GUITickBox tBox) =>
                {
                    if (tBox.Selected)
                    {
                        SetMode(Mode.Default);
                    }
                    return true;
                }
            };

            var characterModeTickBox = new GUITickBox(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "CharacterModeButton")
            {
                ToolTip = TextManager.Get("CharacterModeButton") + '\n' + TextManager.Get("CharacterModeToolTip"),
                OnSelected = (GUITickBox tBox) =>
                {
                    if (tBox.Selected)
                    {
                        SetMode(Mode.Character);
                    }
                    return true;
                }
            };

            var wiringModeTickBox = new GUITickBox(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "WiringModeButton")
            {
                ToolTip = TextManager.Get("WiringModeButton") + '\n' + TextManager.Get("WiringModeToolTip"),
                OnSelected = (GUITickBox tBox) =>
                {
                    if (tBox.Selected)
                    {
                        SetMode(Mode.Wiring);
                    }
                    return true;
                }
            };

            modeButtonGroup.AddRadioButton((int)Mode.Default, defaultModeTickBox);
            modeButtonGroup.AddRadioButton((int)Mode.Character, characterModeTickBox);
            modeButtonGroup.AddRadioButton((int)Mode.Wiring, wiringModeTickBox);
            modeButtonGroup.Selected = (int)Mode.Default;

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "GenerateWaypointsButton")
            {
                ToolTip = TextManager.Get("GenerateWaypointsButton") + '\n' + TextManager.Get("GenerateWaypointsToolTip"),
                OnClicked = GenerateWaypoints
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            var visibilityButton = new GUIButton(new RectTransform(new Vector2(0.15f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "SetupVisibilityButton")
            {
                ToolTip = TextManager.Get("SubEditorVisibilityButton") + '\n' + TextManager.Get("SubEditorVisibilityToolTip"),
                OnClicked = (btn, userData) =>
                {
                    previouslyUsedPanel.Visible = false;
                    showEntitiesPanel.Visible = !showEntitiesPanel.Visible;
                    showEntitiesPanel.RectTransform.AbsoluteOffset = new Point(btn.Rect.X, TopPanel.Rect.Height);
                    return true;
                }
            };

            var previouslyUsedButton = new GUIButton(new RectTransform(new Vector2(0.15f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "RecentlyUsedButton")
            {
                ToolTip = TextManager.Get("PreviouslyUsedLabel"),
                OnClicked = (btn, userData) =>
                {
                    showEntitiesPanel.Visible = false;
                    previouslyUsedPanel.Visible = !previouslyUsedPanel.Visible;
                    previouslyUsedPanel.RectTransform.AbsoluteOffset = new Point(btn.Rect.X, TopPanel.Rect.Height);
                    return true;
                }
            };

            var disclaimerBtn = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), paddedTopPanel.RectTransform, Anchor.CenterRight), style: "GUINotificationButton")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { GameMain.Instance.ShowEditorDisclaimer(); return true; }
            };
            disclaimerBtn.RectTransform.MaxSize = new Point(disclaimerBtn.Rect.Height);

            TopPanel.RectTransform.MinSize = new Point(0, (int)(paddedTopPanel.RectTransform.Children.Max(c => c.MinSize.Y) / paddedTopPanel.RectTransform.RelativeSize.Y));
            paddedTopPanel.Recalculate();

            //-----------------------------------------------

            previouslyUsedPanel = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.2f), GUI.Canvas, Anchor.TopLeft) { MinSize = new Point(200, 200) })
            {
                Visible = false
            };
            previouslyUsedList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.9f), previouslyUsedPanel.RectTransform, Anchor.Center))
            {
                ScrollBarVisible = true,
                OnSelected = SelectPrefab
            };

            //-----------------------------------------------

            showEntitiesPanel = new GUIFrame(new RectTransform(new Vector2(0.08f, 0.5f), GUI.Canvas)
            {
                MinSize = new Point(170, 0),
                AbsoluteOffset = new Point(visibilityButton.Rect.X, TopPanel.Rect.Height)
            }) 
            { 
                Visible = false 
            };

            GUILayoutGroup paddedShowEntitiesPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), showEntitiesPanel.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowLighting"))
            {
                UserData = "lighting",
                Selected = lightingEnabled,
                OnSelected = (GUITickBox obj) =>
                {
                    lightingEnabled = obj.Selected;
                    if (lightingEnabled)
                    {
                        //turn off lights that are inside containers
                        foreach (Item item in Item.ItemList)
                        {
                            foreach (LightComponent lightComponent in item.GetComponents<LightComponent>())
                            {
                                lightComponent.Light.Color = item.Container != null || (item.body != null && !item.body.Enabled) ?
                                    Color.Transparent :
                                    lightComponent.LightColor;
                            }
                        }
                    }
                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowWalls"))
            {
                UserData = "wall",
                Selected = Structure.ShowWalls,
                OnSelected = (GUITickBox obj) => { Structure.ShowWalls = obj.Selected; return true; }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowStructures"))
            {
                UserData = "structure",
                Selected = Structure.ShowStructures,
                OnSelected = (GUITickBox obj) => { Structure.ShowStructures = obj.Selected; return true; }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowItems"))
            {
                UserData = "item",
                Selected = Item.ShowItems,
                OnSelected = (GUITickBox obj) => { Item.ShowItems = obj.Selected; return true; }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowWaypoints"))
            {
                UserData = "waypoint",
                Selected = WayPoint.ShowWayPoints,
                OnSelected = (GUITickBox obj) => { WayPoint.ShowWayPoints = obj.Selected; return true; }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowSpawnpoints"))
            {
                UserData = "spawnpoint",
                Selected = WayPoint.ShowSpawnPoints,
                OnSelected = (GUITickBox obj) => { WayPoint.ShowSpawnPoints = obj.Selected; return true; }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowLinks"))
            {
                UserData = "link",
                Selected = Item.ShowLinks,
                OnSelected = (GUITickBox obj) => { Item.ShowLinks = obj.Selected; return true; }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowHulls"))
            {
                UserData = "hull",
                Selected = Hull.ShowHulls,
                OnSelected = (GUITickBox obj) => { Hull.ShowHulls = obj.Selected; return true; }
            };
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowGaps"))
            {
                UserData = "gap",
                Selected = Gap.ShowGaps,
                OnSelected = (GUITickBox obj) => { Gap.ShowGaps = obj.Selected; return true; },
            };

            showEntitiesTickBoxes.AddRange(paddedShowEntitiesPanel.Children.Select(c => c as GUITickBox));

            showEntitiesPanel.RectTransform.NonScaledSize =
                new Point(
                    (int)(paddedShowEntitiesPanel.RectTransform.Children.Max(c => (int)((c.GUIComponent as GUITickBox)?.TextBlock.TextSize.X ?? 0)) / paddedShowEntitiesPanel.RectTransform.RelativeSize.X),
                    (int)(paddedShowEntitiesPanel.RectTransform.Children.Sum(c => c.MinSize.Y) / paddedShowEntitiesPanel.RectTransform.RelativeSize.Y));
            GUITextBlock.AutoScaleAndNormalize(paddedShowEntitiesPanel.Children.Where(c => c is GUITickBox).Select(c => ((GUITickBox)c).TextBlock));

            //-----------------------------------------------

            float longestTextWidth = GUI.SmallFont.MeasureString(TextManager.Get("SubEditorShadowCastingLights")).X;
            entityCountPanel = new GUIFrame(new RectTransform(new Vector2(0.08f, 0.5f), GUI.Canvas)
            {
                MinSize = new Point(Math.Max(170, (int)(longestTextWidth * 1.5f)), 0),
                AbsoluteOffset = new Point(0, TopPanel.Rect.Height)
            });

            GUILayoutGroup paddedEntityCountPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), entityCountPanel.RectTransform, Anchor.Center))
            {
                Stretch = true,
                AbsoluteSpacing = (int)(GUI.Scale * 4)
            };

            var itemCountText = new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.0f), paddedEntityCountPanel.RectTransform), TextManager.Get("Items"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SmallFont);
            var itemCount = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1.0f), itemCountText.RectTransform, Anchor.TopRight, Pivot.TopLeft), "", textAlignment: Alignment.CenterRight);
            itemCount.TextGetter = () =>
            {
                itemCount.TextColor = ToolBox.GradientLerp(Item.ItemList.Count / 5000.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
                return Item.ItemList.Count.ToString();
            };

            var structureCountText = new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.0f), paddedEntityCountPanel.RectTransform), TextManager.Get("Structures"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SmallFont);
            var structureCount = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1.0f), structureCountText.RectTransform, Anchor.TopRight, Pivot.TopLeft), "", textAlignment: Alignment.CenterRight);
            structureCount.TextGetter = () =>
            {
                int count = (MapEntity.mapEntityList.Count - Item.ItemList.Count - Hull.hullList.Count - WayPoint.WayPointList.Count - Gap.GapList.Count);
                structureCount.TextColor = ToolBox.GradientLerp(count / 1000.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
                return count.ToString();
            };

            var wallCountText = new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.0f), paddedEntityCountPanel.RectTransform), TextManager.Get("Walls"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SmallFont);
            var wallCount = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1.0f), wallCountText.RectTransform, Anchor.TopRight, Pivot.TopLeft), "", textAlignment: Alignment.CenterRight);
            wallCount.TextGetter = () =>
            {
                wallCount.TextColor = ToolBox.GradientLerp(Structure.WallList.Count / 500.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
                return Structure.WallList.Count.ToString();
            };
            
            var lightCountText = new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.0f), paddedEntityCountPanel.RectTransform), TextManager.Get("SubEditorLights"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SmallFont);
            var lightCount = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1.0f), lightCountText.RectTransform, Anchor.TopRight, Pivot.TopLeft), "", textAlignment: Alignment.CenterRight);
            lightCount.TextGetter = () =>
            {
                int disabledItemLightCount = 0;
                foreach (Item item in Item.ItemList)
                {
                    if (item.ParentInventory == null) { continue; }
                    disabledItemLightCount += item.GetComponents<LightComponent>().Count();
                }
                int count = GameMain.LightManager.Lights.Count() - disabledItemLightCount;
                lightCount.TextColor = ToolBox.GradientLerp(count / 250.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
                return count.ToString();
            };
            var shadowCastingLightCountText = new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.0f), paddedEntityCountPanel.RectTransform), TextManager.Get("SubEditorShadowCastingLights"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SmallFont, wrap: true);
            var shadowCastingLightCount = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1.0f), shadowCastingLightCountText.RectTransform, Anchor.TopRight, Pivot.TopLeft), "", textAlignment: Alignment.CenterRight);
            shadowCastingLightCount.TextGetter = () =>
            {
                int disabledItemLightCount = 0;
                foreach (Item item in Item.ItemList)
                {
                    if (item.ParentInventory == null) { continue; }
                    disabledItemLightCount += item.GetComponents<LightComponent>().Count();
                }
                int count = GameMain.LightManager.Lights.Count(l => l.CastShadows) - disabledItemLightCount;
                shadowCastingLightCount.TextColor = ToolBox.GradientLerp(count / 60.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
                return count.ToString();
            };
            entityCountPanel.RectTransform.NonScaledSize =
                new Point(
                    (int)(paddedEntityCountPanel.RectTransform.Children.Max(c => (int)(c.GUIComponent as GUITextBlock).TextSize.X / 0.75f) / paddedEntityCountPanel.RectTransform.RelativeSize.X),
                    (int)(paddedEntityCountPanel.RectTransform.Children.Sum(c => (int)(c.NonScaledSize.Y * 1.5f) + paddedEntityCountPanel.AbsoluteSpacing) / paddedEntityCountPanel.RectTransform.RelativeSize.Y));
            //GUITextBlock.AutoScaleAndNormalize(paddedEntityCountPanel.Children.Where(c => c is GUITextBlock).Cast<GUITextBlock>());

            //-----------------------------------------------

            hullVolumeFrame = new GUIFrame(new RectTransform(new Vector2(0.15f, 2.0f), TopPanel.RectTransform, Anchor.BottomLeft, Pivot.TopLeft, minSize: new Point(300, 85)) { AbsoluteOffset = new Point(entityCountPanel.Rect.Width, 0) }, "GUIToolTip")
            {
                Visible = false
            };
            GUITextBlock totalHullVolume = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), hullVolumeFrame.RectTransform), "", font: GUI.SmallFont)
            {
                TextGetter = GetTotalHullVolume
            };
            GUITextBlock selectedHullVolume = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), hullVolumeFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.5f) }, "", font: GUI.SmallFont)
            {
                TextGetter = GetSelectedHullVolume
            };

            saveAssemblyFrame = new GUIFrame(new RectTransform(new Vector2(0.08f, 0.5f), TopPanel.RectTransform, Anchor.BottomLeft, Pivot.TopLeft)
            { MinSize = new Point((int)(250 * GUI.Scale), (int)(80 * GUI.Scale)), AbsoluteOffset = new Point((int)(10 * GUI.Scale), -entityCountPanel.Rect.Height - (int)(10 * GUI.Scale)) }, "InnerFrame")
            {
                Visible = false
            };
            var saveAssemblyButton = new GUIButton(new RectTransform(new Vector2(0.9f, 0.8f), saveAssemblyFrame.RectTransform, Anchor.Center), TextManager.Get("SaveItemAssembly"));
            saveAssemblyButton.TextBlock.AutoScale = true;
            saveAssemblyButton.OnClicked += (btn, userdata) =>
            {
                CreateSaveAssemblyScreen();
                return true;
            };
            saveAssemblyFrame.RectTransform.MinSize = new Point(saveAssemblyFrame.Rect.Width, (int)(saveAssemblyButton.Rect.Height / saveAssemblyButton.RectTransform.RelativeSize.Y));


            //Entity menu
            //------------------------------------------------

            EntityMenu = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, (int)(359 * GUI.Scale)), GUI.Canvas, Anchor.BottomRight));

            toggleEntityMenuButton = new GUIButton(new RectTransform(new Vector2(0.15f, 0.08f), EntityMenu.RectTransform, Anchor.TopCenter, Pivot.BottomCenter) { MinSize = new Point(0, 15) },
                style: "UIToggleButtonVertical")
            {
                OnClicked = (btn, userdata) =>
                {
                    entityMenuOpen = !entityMenuOpen;
                    SetMode(Mode.Default);
                    foreach (GUIComponent child in btn.Children)
                    {
                        child.SpriteEffects = entityMenuOpen ? SpriteEffects.None : SpriteEffects.FlipVertically;
                    }
                    return true;
                }
            };

            var paddedTab = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.96f), EntityMenu.RectTransform, Anchor.BottomCenter), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            var entityMenuTop = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.13f), paddedTab.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            var selectedCategoryButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), entityMenuTop.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "CategoryButton.All")
            {
                CanBeFocused = false
            };
            var selectedCategoryText = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1.0f), entityMenuTop.RectTransform), TextManager.Get("MapEntityCategory.All"), font: GUI.LargeFont);

            entityCategoryButtons.Clear();
            entityCategoryButtons.Add(
                new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), entityMenuTop.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "CategoryButton.All")
                {
                    OnClicked = (btn, userdata) => 
                    { 
                        entityCategoryButtons.ForEach(b => b.Selected = b == btn);
                        selectedCategoryText.Text = TextManager.Get("MapEntityCategory.All");
                        selectedCategoryButton.ApplyStyle(GUI.Style.GetComponentStyle("CategoryButton.All"));
                        ClearFilter(); 
                        return true; 
                    }
                });

            foreach (MapEntityCategory category in Enum.GetValues(typeof(MapEntityCategory)))
            {
                entityCategoryButtons.Add(new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), entityMenuTop.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    "", style: "CategoryButton." + category.ToString())
                {
                    UserData = category,
                    ToolTip = TextManager.Get("MapEntityCategory." + category.ToString()),
                    OnClicked = (btn, userdata) =>
                    {
                        entityMenuOpen = true;
                        MapEntityCategory newCategory = (MapEntityCategory)userdata;
                        selectedCategoryText.Text = TextManager.Get("MapEntityCategory." + newCategory.ToString());
                        selectedCategoryButton.ApplyStyle(GUI.Style.GetComponentStyle("CategoryButton." + category.ToString()));
                        OpenEntityMenu(newCategory);
                        return true;
                    }
                });
            }

            var filterText = new GUITextBlock(new RectTransform(new Vector2(0.1f, 1.0f), entityMenuTop.RectTransform), TextManager.Get("serverlog.filter"), font: GUI.SubHeadingFont);
            filterText.RectTransform.MaxSize = new Point((int)(filterText.TextSize.X * 1.5f), int.MaxValue);
            entityFilterBox = new GUITextBox(new RectTransform(new Vector2(0.2f, 1.0f), entityMenuTop.RectTransform), font: GUI.Font, createClearButton: true);
            entityFilterBox.OnTextChanged += (textBox, text) => { FilterEntities(text); return true; };

            new GUIFrame(new RectTransform(new Vector2(0.8f, 0.01f), paddedTab.RectTransform), style: "HorizontalLine");

            entityList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.9f), paddedTab.RectTransform))
            {
                OnSelected = SelectPrefab,
                UseGridLayout = true,
                CheckSelected = MapEntityPrefab.GetSelected
            };
            
            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private void UpdateEntityList()
        {
            entityList.Content.ClearChildren();

            int entitiesPerRow = (int)Math.Ceiling(entityList.Content.Rect.Width / Math.Max(125 * GUI.Scale, 100));

            foreach (MapEntityPrefab ep in MapEntityPrefab.List)
            {
#if !DEBUG
                if (ep.HideInMenus) { continue; }                
#endif

                bool legacy = ep.Category.HasFlag(MapEntityCategory.Legacy);

                float relWidth = 1.0f / entitiesPerRow;
                GUIFrame frame = new GUIFrame(new RectTransform(
                    new Vector2(relWidth, relWidth * ((float)entityList.Content.Rect.Width / entityList.Content.Rect.Height)),
                    entityList.Content.RectTransform) { MinSize = new Point(0, 50) },
                    style: "GUITextBox")
                {
                    UserData = ep,
                };
                frame.RectTransform.MinSize = new Point(0, frame.Rect.Width);
                frame.RectTransform.MaxSize = new Point(int.MaxValue, frame.Rect.Width);


                string name = legacy ? ep.Name + " (legacy)" : ep.Name;
                frame.ToolTip = string.IsNullOrEmpty(ep.Description) ? name : name + '\n' + ep.Description;

                GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.8f), frame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
                {              
                    Stretch = true,
                    RelativeSpacing = 0.03f,
                    CanBeFocused = false
                };

                Sprite icon = ep.sprite;
                Color iconColor = Color.White;
                if (ep is ItemPrefab itemPrefab)
                {
                    if (itemPrefab.InventoryIcon != null)
                    {
                        icon = itemPrefab.InventoryIcon;
                        iconColor = itemPrefab.InventoryIconColor;
                    }
                    else
                    {
                        iconColor = itemPrefab.SpriteColor;
                    }
                }
                GUIImage img = null;
                if (ep.sprite != null)
                {
                    img = new GUIImage(new RectTransform(new Vector2(1.0f, 0.8f),
                        paddedFrame.RectTransform, Anchor.TopCenter), icon)
                    {
                        CanBeFocused = false,                        
                        Color = legacy ? iconColor * 0.6f : iconColor
                    };
                }

                if (ep is ItemAssemblyPrefab itemAssemblyPrefab)
                {
                    new GUICustomComponent(new RectTransform(new Vector2(1.0f, 0.75f),
                        paddedFrame.RectTransform, Anchor.TopCenter), onDraw: itemAssemblyPrefab.DrawIcon, onUpdate: null)
                    {
                        HideElementsOutsideFrame = true,
                        ToolTip = frame.RawToolTip
                    };
                }

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform, Anchor.BottomCenter),
                    text: ep.Name, textAlignment: Alignment.Center, font: GUI.SmallFont)
                {
                    CanBeFocused = false
                };
                if (legacy) textBlock.TextColor *= 0.6f;
                textBlock.Text = ToolBox.LimitString(textBlock.Text, textBlock.Font, textBlock.Rect.Width);

                if (ep.Category == MapEntityCategory.ItemAssembly)
                {
                    var deleteButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform, Anchor.BottomCenter) { MinSize = new Point(0, 20) },
                        TextManager.Get("Delete"), style: "GUIButtonSmall")
                    {
                        UserData = ep,
                        OnClicked = (btn, userData) =>
                        {
                            ItemAssemblyPrefab assemblyPrefab = userData as ItemAssemblyPrefab;
                            var msgBox = new GUIMessageBox(
                                TextManager.Get("DeleteDialogLabel"),
                                TextManager.GetWithVariable("DeleteDialogQuestion", "[file]", assemblyPrefab.Name),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
                            msgBox.Buttons[0].OnClicked += (deleteBtn, userData2) =>
                            {
                                try
                                {
                                    assemblyPrefab.Delete();
                                    UpdateEntityList();
                                    OpenEntityMenu(MapEntityCategory.ItemAssembly);
                                }
                                catch (Exception e)
                                {
                                    DebugConsole.ThrowError(TextManager.GetWithVariable("DeleteFileError", "[file]", assemblyPrefab.Name), e);
                                }
                                return true;
                            };
                            msgBox.Buttons[0].OnClicked += msgBox.Close;
                            msgBox.Buttons[1].OnClicked += msgBox.Close;
                            return true;
                        }
                    };
                }
                paddedFrame.Recalculate();
                if (img != null)
                {
                    img.Scale = Math.Min(Math.Min(img.Rect.Width / img.Sprite.size.X, img.Rect.Height / img.Sprite.size.Y), 1.5f);
                    img.RectTransform.NonScaledSize = new Point((int)(img.Sprite.size.X * img.Scale), img.Rect.Height);
                }
            }

            entityList.Content.RectTransform.SortChildren((i1, i2) =>
                (i1.GUIComponent.UserData as MapEntityPrefab).Name.CompareTo((i2.GUIComponent.UserData as MapEntityPrefab).Name));
        }

        public override void Select()
        {
            base.Select();

            UpdateEntityList();

            foreach (MapEntityPrefab prefab in MapEntityPrefab.List)
            {
                prefab.sprite?.EnsureLazyLoaded();
                if (prefab is ItemPrefab itemPrefab)
                {
                    itemPrefab.InventoryIcon?.EnsureLazyLoaded();
                }
            }

            editorSelectedTime = DateTime.Now;

            GUI.ForceMouseOn(null);
            SetMode(Mode.Default);

            if (Submarine.MainSub != null)
            {
                Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
                Submarine.MainSub.UpdateTransform();
                cam.Position = Submarine.MainSub.Position + Submarine.MainSub.HiddenSubPosition;
            }
            else
            {
                Submarine.MainSub = new Submarine(Path.Combine(Submarine.SavePath, TextManager.Get("UnspecifiedSubFileName") + ".sub"), "", false);
                cam.Position = Submarine.MainSub.Position;
            }

            GameMain.SoundManager.SetCategoryGainMultiplier("default", 0.0f, 0);
            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", 0.0f, 0);

            linkedSubBox.ClearChildren();
            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                linkedSubBox.AddItem(sub.Name, sub);
            }

            cam.UpdateTransform();

            GameAnalyticsManager.SetCustomDimension01("editor");
            if (!GameMain.Config.EditorDisclaimerShown)
            {
                GameMain.Instance.ShowEditorDisclaimer();
            }
        }

        public override void Deselect()
        {
            base.Deselect();

            TimeSpan timeInEditor = DateTime.Now - editorSelectedTime;
#if USE_STEAM
            Steam.SteamManager.IncrementStat("hoursineditor", (float)timeInEditor.TotalHours);
#endif

            GUI.ForceMouseOn(null);

            MapEntityPrefab.Selected = null;

            saveFrame = null;
            loadFrame = null;

            MapEntity.DeselectAll();
            MapEntity.SelectionGroups.Clear();

            SetMode(Mode.Default);

            SoundPlayer.OverrideMusicType = null;
            GameMain.SoundManager.SetCategoryGainMultiplier("default", GameMain.Config.SoundVolume, 0);
            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", GameMain.Config.SoundVolume, 0);

            if (dummyCharacter != null)
            {
                dummyCharacter.Remove();
                dummyCharacter = null;
                GameMain.World.ProcessChanges();
            }

            if (GUIMessageBox.MessageBoxes.Any(mbox => (mbox as GUIMessageBox).Tag == containerDeleteTag))
            {
                for (int i = 0; i < GUIMessageBox.MessageBoxes.Count; i++)
                {
                    GUIMessageBox box = GUIMessageBox.MessageBoxes[i] as GUIMessageBox;
                    if (box.Tag != containerDeleteTag) continue;
                    box.Close();
                    i--; // Take into account the message boxes removing themselves from the list when closed
                }
            }
            ClearFilter();
        }

        public void HandleContainerContentsDeletion(Item itemToDelete, Inventory itemInventory)
        {
            string itemNames = string.Empty;

            foreach (Item item in itemInventory.Items)
            {
                if (item == null) continue;
                itemNames += item.Name + "\n";
            }

            if (itemNames.Length > 0)
            {
                // Multiple prompts open
                if (GUIMessageBox.MessageBoxes.Any(mbox => (mbox as GUIMessageBox).Tag == containerDeleteTag))
                {
                    var msgBox = new GUIMessageBox(itemToDelete.Name, TextManager.Get("DeletingContainerWithItems") + itemNames, new string[] { TextManager.Get("Yes"), TextManager.Get("No"), TextManager.Get("YesToAll"), TextManager.Get("NoToAll") }, tag: containerDeleteTag);

                    // Yes
                    msgBox.Buttons[0].OnClicked = (btn, userdata) =>
                    {
                        itemInventory.DeleteAllItems();
                        msgBox.Close();
                        return true;
                    };

                    // No
                    msgBox.Buttons[1].OnClicked = (btn, userdata) =>
                    {
                        if (Selected == GameMain.SubEditorScreen)
                        {
                            foreach (Item item in itemInventory.Items)
                            {
                                item?.Drop(null);
                            }
                        }
                        else // If current screen is not subeditor, delete anyway to avoid lingering objects
                        {
                            itemInventory.DeleteAllItems();
                        }

                        msgBox.Close();
                        return true;
                    };

                    // Yes to All
                    msgBox.Buttons[2].OnClicked = (btn, userdata) =>
                    {
                        for (int i = 0; i < GUIMessageBox.MessageBoxes.Count; i++)
                        {
                            GUIMessageBox box = GUIMessageBox.MessageBoxes[i] as GUIMessageBox;
                            if (box.Tag != msgBox.Tag || box == msgBox) continue;
                            GUIButton button = box.Buttons[0];
                            button.OnClicked(button, button.UserData);
                            i--; // Take into account the message boxes removing themselves from the list when closed
                        }

                        itemInventory.DeleteAllItems();
                        msgBox.Close();
                        return true;
                    };

                    // No to all
                    msgBox.Buttons[3].OnClicked = (btn, userdata) =>
                    {
                        for (int i = 0; i < GUIMessageBox.MessageBoxes.Count; i++)
                        {
                            GUIMessageBox box = GUIMessageBox.MessageBoxes[i] as GUIMessageBox;
                            if (box.Tag != msgBox.Tag || box == msgBox) continue;
                            GUIButton button = box.Buttons[1];
                            button.OnClicked(button, button.UserData);
                            i--; // Take into account the message boxes removing themselves from the list when closed
                        }

                        if (Selected == GameMain.SubEditorScreen)
                        {
                            foreach (Item item in itemInventory.Items)
                            {
                                item?.Drop(null);
                            }
                        }
                        else // If current screen is not subeditor, delete anyway to avoid lingering objects
                        {
                            itemInventory.DeleteAllItems();
                        }

                        msgBox.Close();
                        return true;
                    };
                }
                else // Single prompt
                {
                    var msgBox = new GUIMessageBox(itemToDelete.Name, TextManager.Get("DeletingContainerWithItems") + itemNames, new string[] { TextManager.Get("Yes"), TextManager.Get("No") }, tag: containerDeleteTag);

                    // Yes
                    msgBox.Buttons[0].OnClicked = (btn, userdata) =>
                    {
                        itemInventory.DeleteAllItems();
                        msgBox.Close();
                        return true;
                    };

                    // No
                    msgBox.Buttons[1].OnClicked = (btn, userdata) =>
                    {
                        if (Selected == GameMain.SubEditorScreen)
                        {
                            foreach (Item item in itemInventory.Items)
                            {
                                item?.Drop(null);
                            }
                        }
                        else // If current screen is not subeditor, delete anyway to avoid lingering objects
                        {
                            itemInventory.DeleteAllItems();
                        }

                        msgBox.Close();
                        return true;
                    };
                }
            }
        }

        private void CreateDummyCharacter()
        {
            if (dummyCharacter != null) RemoveDummyCharacter();

            dummyCharacter = Character.Create(CharacterPrefab.HumanSpeciesName, Vector2.Zero, "", hasAi: false);

            //make space for the entity menu
            for (int i = 0; i < dummyCharacter.Inventory.SlotPositions.Length; i++)
            {
                if (CharacterInventory.PersonalSlots.HasFlag(dummyCharacter.Inventory.SlotTypes[i])) { continue; }
                if (dummyCharacter.Inventory.SlotPositions[i].Y > GameMain.GraphicsHeight / 2)
                {
                    dummyCharacter.Inventory.SlotPositions[i].Y -= 50 * GUI.Scale;
                }
            }
            dummyCharacter.Inventory.CreateSlots();

            Character.Controlled = dummyCharacter;
            GameMain.World.ProcessChanges();
        }

        private bool SaveSub(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                GUI.AddMessage(TextManager.Get("SubNameMissingWarning"), GUI.Style.Red);

                nameBox.Flash();
                return false;
            }
            
            foreach (char illegalChar in Path.GetInvalidFileNameChars())
            {
                if (nameBox.Text.Contains(illegalChar))
                {
                    GUI.AddMessage(TextManager.GetWithVariable("SubNameIllegalCharsWarning", "[illegalchar]", illegalChar.ToString()), GUI.Style.Red);
                    nameBox.Flash();
                    return false;
                }
            }
            
            string savePath = nameBox.Text + ".sub";
            string prevSavePath = null;
            if (Submarine.MainSub != null)
            {
                prevSavePath = Submarine.MainSub.FilePath;
                savePath = Path.Combine(Path.GetDirectoryName(Submarine.MainSub.FilePath), savePath);
            }
            else
            {
                savePath = Path.Combine(Submarine.SavePath, savePath);
            }

#if !DEBUG
            var vanilla = GameMain.VanillaContent;
            if (vanilla != null)
            {
                var vanillaSubs = vanilla.GetFilesOfType(ContentType.Submarine);
                string pathToCompare = savePath.Replace(@"\", @"/").ToLowerInvariant();
                if (vanillaSubs.Any(sub => sub.Replace(@"\", @"/").ToLowerInvariant() == pathToCompare))
                {
                    GUI.AddMessage(TextManager.Get("CannotEditVanillaSubs"), GUI.Style.Red, font: GUI.LargeFont);
                    return false;
                }
            }
#endif

            if (previewImage.Sprite?.Texture != null)
            {
                using (MemoryStream imgStream = new MemoryStream())
                {
                    previewImage.Sprite.Texture.SaveAsPng(imgStream, previewImage.Sprite.Texture.Width, previewImage.Sprite.Texture.Height);
                    Submarine.SaveCurrent(savePath, imgStream);
                }
            }
            else
            {
                Submarine.SaveCurrent(savePath);
            }
            Submarine.MainSub.CheckForErrors();
            
            GUI.AddMessage(TextManager.GetWithVariable("SubSavedNotification", "[filepath]", Submarine.MainSub.FilePath), GUI.Style.Green);

            Submarine.RefreshSavedSub(savePath);
            if (prevSavePath != null && prevSavePath != savePath)
            {
                Submarine.RefreshSavedSub(prevSavePath);
            }

            linkedSubBox.ClearChildren();
            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                linkedSubBox.AddItem(sub.Name, sub);
            }

            saveFrame = null;
            
            return false;
        }

        private void CreateSaveScreen()
        {
            SetMode(Mode.Default);

            saveFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker")
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) saveFrame = null; return true; }
            };

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.5f), saveFrame.RectTransform, Anchor.Center) { MinSize = new Point(750, 400) });
            var paddedSaveFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center)) { Stretch = true, RelativeSpacing = 0.02f };

            //var header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedSaveFrame.RectTransform), TextManager.Get("SaveSubDialogHeader"), font: GUI.LargeFont);

            var columnArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), paddedSaveFrame.RectTransform), isHorizontal: true) { RelativeSpacing = 0.02f, Stretch = true };
            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.55f, 1.0f), columnArea.RectTransform)) { RelativeSpacing = 0.01f, Stretch = true };
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.42f, 1.0f), columnArea.RectTransform)) { RelativeSpacing = 0.02f, Stretch = true };

            // left column ----------------------------------------------------------------------- 

            var nameHeaderGroup = new GUILayoutGroup(new RectTransform(new Vector2(.975f, 0.03f), leftColumn.RectTransform), true);
            var saveSubLabel = new GUITextBlock(new RectTransform(new Vector2(.5f, 1f), nameHeaderGroup.RectTransform),
                TextManager.Get("SaveSubDialogName"), font: GUI.SubHeadingFont);

            submarineNameCharacterCount = new GUITextBlock(new RectTransform(new Vector2(.5f, 1f), nameHeaderGroup.RectTransform), string.Empty, textAlignment: Alignment.TopRight);

            nameBox = new GUITextBox(new RectTransform(new Vector2(.95f, 0.05f), leftColumn.RectTransform))
            {
                OnEnterPressed = ChangeSubName,
                Text = GetSubName()
            };
            nameBox.OnTextChanged += (textBox, text) =>
            {
                if (text.Length > submarineNameLimit)
                {
                    nameBox.Text = text.Substring(0, submarineNameLimit);
                    nameBox.Flash(GUI.Style.Red);
                    return true;
                }

                submarineNameCharacterCount.Text = text.Length + " / " + submarineNameLimit;
                return true;
            };

            submarineNameCharacterCount.Text = nameBox.Text.Length + " / " + submarineNameLimit;

            var descriptionHeaderGroup = new GUILayoutGroup(new RectTransform(new Vector2(.975f, 0.03f), leftColumn.RectTransform), true);

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), descriptionHeaderGroup.RectTransform), TextManager.Get("SaveSubDialogDescription"), font: GUI.SubHeadingFont);
            submarineDescriptionCharacterCount = new GUITextBlock(new RectTransform(new Vector2(.5f, 1f), descriptionHeaderGroup.RectTransform), string.Empty, textAlignment: Alignment.TopRight);

            var descriptionContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.25f), leftColumn.RectTransform));
            descriptionBox = new GUITextBox(new RectTransform(Vector2.One, descriptionContainer.Content.RectTransform, Anchor.Center), 
                font: GUI.SmallFont, style: "GUITextBoxNoBorder", wrap: true, textAlignment: Alignment.TopLeft)
            {
                Padding = new Vector4(10 * GUI.Scale)
            };

            descriptionBox.OnTextChanged += (textBox, text) =>
            {
                if (text.Length > submarineDescriptionLimit)
                {
                    descriptionBox.Text = text.Substring(0, submarineDescriptionLimit);
                    descriptionBox.Flash(GUI.Style.Red);
                    return true;
                }

                Vector2 textSize = textBox.Font.MeasureString(descriptionBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(descriptionContainer.Content.Rect.Height, (int)textSize.Y + 10));
                descriptionContainer.UpdateScrollBarSize();
                descriptionContainer.BarScroll = 1.0f;
                ChangeSubDescription(textBox, text);
                return true;
            };

            descriptionBox.Text = GetSubDescription();

            var crewSizeArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), leftColumn.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = 5
            };

            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), crewSizeArea.RectTransform),
                TextManager.Get("RecommendedCrewSize"), textAlignment: Alignment.CenterLeft, wrap: true, font: GUI.SmallFont);
            var crewSizeMin = new GUINumberInput(new RectTransform(new Vector2(0.1f, 1.0f), crewSizeArea.RectTransform), GUINumberInput.NumberType.Int)
            {
                MinValueInt = 1,
                MaxValueInt = 128
            };
            new GUITextBlock(new RectTransform(new Vector2(0.1f, 1.0f), crewSizeArea.RectTransform), "-", textAlignment: Alignment.Center);
            var crewSizeMax = new GUINumberInput(new RectTransform(new Vector2(0.1f, 1.0f), crewSizeArea.RectTransform), GUINumberInput.NumberType.Int)
            {
                MinValueInt = 1,
                MaxValueInt = 128
            };

            crewSizeMin.OnValueChanged += (numberInput) =>
            {
                crewSizeMax.IntValue = Math.Max(crewSizeMax.IntValue, numberInput.IntValue);
                Submarine.MainSub.RecommendedCrewSizeMin = crewSizeMin.IntValue;
                Submarine.MainSub.RecommendedCrewSizeMax = crewSizeMax.IntValue;
            };

            crewSizeMax.OnValueChanged += (numberInput) =>
            {
                crewSizeMin.IntValue = Math.Min(crewSizeMin.IntValue, numberInput.IntValue);
                Submarine.MainSub.RecommendedCrewSizeMin = crewSizeMin.IntValue;
                Submarine.MainSub.RecommendedCrewSizeMax = crewSizeMax.IntValue;
            };

            var crewExpArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), leftColumn.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = 5
            };

            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), crewExpArea.RectTransform),
                TextManager.Get("RecommendedCrewExperience"), textAlignment: Alignment.CenterLeft, wrap: true, font: GUI.SmallFont);

            var toggleExpLeft = new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), crewExpArea.RectTransform), style: "GUIButtonToggleLeft");
            var experienceText = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), crewExpArea.RectTransform), crewExperienceLevels[0], textAlignment: Alignment.Center);
            var toggleExpRight = new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), crewExpArea.RectTransform), style: "GUIButtonToggleRight");

            toggleExpLeft.OnClicked += (btn, userData) =>
            {
                int currentIndex = Array.IndexOf(crewExperienceLevels, (string)experienceText.UserData);
                currentIndex--;
                if (currentIndex < 0) currentIndex = crewExperienceLevels.Length - 1;
                experienceText.UserData = crewExperienceLevels[currentIndex];
                experienceText.Text = TextManager.Get(crewExperienceLevels[currentIndex]);
                Submarine.MainSub.RecommendedCrewExperience = (string)experienceText.UserData;
                return true;
            };

            toggleExpRight.OnClicked += (btn, userData) =>
            {
                int currentIndex = Array.IndexOf(crewExperienceLevels, (string)experienceText.UserData);
                currentIndex++;
                if (currentIndex >= crewExperienceLevels.Length) currentIndex = 0;
                experienceText.UserData = crewExperienceLevels[currentIndex];
                experienceText.Text = TextManager.Get(crewExperienceLevels[currentIndex]);
                Submarine.MainSub.RecommendedCrewExperience = (string)experienceText.UserData;
                return true;
            };

            if (Submarine.MainSub != null)
            {
                int min =  Submarine.MainSub.RecommendedCrewSizeMin;
                int max = Submarine.MainSub.RecommendedCrewSizeMax;
                crewSizeMin.IntValue = min;
                crewSizeMax.IntValue = max;
                experienceText.UserData =  string.IsNullOrEmpty(Submarine.MainSub.RecommendedCrewExperience) ?
                    crewExperienceLevels[0] : Submarine.MainSub.RecommendedCrewExperience;
                experienceText.Text = TextManager.Get((string)experienceText.UserData);
            }
            
            // right column ---------------------------------------------------
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), rightColumn.RectTransform), TextManager.Get("SubPreviewImage"), font: GUI.SubHeadingFont);
            
            var previewImageHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), rightColumn.RectTransform), style: null) { Color = Color.Black, CanBeFocused = false };
            previewImage = new GUIImage(new RectTransform(Vector2.One, previewImageHolder.RectTransform), Submarine.MainSub?.PreviewImage, scaleToFit: true);

            var previewImageButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };

            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), previewImageButtonHolder.RectTransform), TextManager.Get("SubPreviewImageCreate"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    using (MemoryStream imgStream = new MemoryStream())
                    {
                        CreateImage(defaultPreviewImageSize.X, defaultPreviewImageSize.Y, imgStream);
                        previewImage.Sprite = new Sprite(TextureLoader.FromStream(imgStream), null, null);
                        if (Submarine.MainSub != null)
                        {
                            Submarine.MainSub.PreviewImage = previewImage.Sprite;
                        }
                    }
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), previewImageButtonHolder.RectTransform), TextManager.Get("SubPreviewImageBrowse"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    FileSelection.OnFileSelected = (file) =>
                    {
                        if (new FileInfo(file).Length > 2048 * 2048)
                        {
                            new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("WorkshopItemPreviewImageTooLarge"));
                            return;
                        }

                        previewImage.Sprite = new Sprite(file, sourceRectangle: null);
                        if (Submarine.MainSub != null)
                        {
                            Submarine.MainSub.PreviewImage = previewImage.Sprite;
                        }
                    };
                    FileSelection.ClearFileTypeFilters();
                    FileSelection.AddFileTypeFilter("PNG", "*.png");
                    FileSelection.AddFileTypeFilter("JPEG", "*.jpg, *.jpeg");
                    FileSelection.AddFileTypeFilter("All files", "*.*");
                    FileSelection.SelectFileTypeFilter("*.png");
                    FileSelection.Open = true;
                    return false;
                }
            };

            previewImageButtonHolder.RectTransform.MinSize = new Point(0, previewImageButtonHolder.RectTransform.Children.Max(c => c.MinSize.Y));

            var horizontalArea = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.35f), rightColumn.RectTransform), style: null);

            var settingsLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), horizontalArea.RectTransform),
                TextManager.Get("SaveSubDialogSettings"), wrap: true, font: GUI.SmallFont);

            var tagContainer = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f - settingsLabel.RectTransform.RelativeSize.Y), 
                horizontalArea.RectTransform, Anchor.BottomLeft),
                style: "InnerFrame");

            foreach (SubmarineTag tag in Enum.GetValues(typeof(SubmarineTag)))
            {
                string tagStr = TextManager.Get(tag.ToString());
                var tagTickBox = new GUITickBox(new RectTransform(new Vector2(0.2f, 0.2f), tagContainer.Content.RectTransform),
                    tagStr, font: GUI.SmallFont)
                {
                    Selected = Submarine.MainSub == null ? false : Submarine.MainSub.HasTag(tag),
                    UserData = tag,

                    OnSelected = (GUITickBox tickBox) =>
                    {
                        if (Submarine.MainSub == null) return false;
                        if (tickBox.Selected)
                        {
                            Submarine.MainSub.AddTag((SubmarineTag)tickBox.UserData);
                        }
                        else
                        {
                            Submarine.MainSub.RemoveTag((SubmarineTag)tickBox.UserData);
                        }
                        return true;
                    }
                };
            }

            var contentPackagesLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), horizontalArea.RectTransform, Anchor.TopRight),
                TextManager.Get("RequiredContentPackages"), wrap: true, font: GUI.SmallFont);

            var contentPackList = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f - contentPackagesLabel.RectTransform.RelativeSize.Y),
                horizontalArea.RectTransform, Anchor.BottomRight));

            List<string> contentPacks = Submarine.MainSub.RequiredContentPackages.ToList();
            foreach (ContentPackage contentPack in ContentPackage.List)
            {                
                //don't show content packages that only define submarine files
                //(it doesn't make sense to require another sub to be installed to install this one)
                if (contentPack.Files.All(cp => cp.Type == ContentType.Submarine)) { continue; }
                if (!contentPacks.Contains(contentPack.Name)) { contentPacks.Add(contentPack.Name); }
            }

            foreach (string contentPackageName in contentPacks)
            {
                var cpTickBox = new GUITickBox(new RectTransform(new Vector2(0.2f, 0.2f), contentPackList.Content.RectTransform), contentPackageName, font: GUI.SmallFont)
                {
                    Selected = Submarine.MainSub.RequiredContentPackages.Contains(contentPackageName),
                    UserData = contentPackageName
                };
                cpTickBox.OnSelected += (GUITickBox tickBox) =>
                {
                    if (tickBox.Selected)
                    {
                        Submarine.MainSub.RequiredContentPackages.Add((string)tickBox.UserData);
                    }
                    else
                    {
                        Submarine.MainSub.RequiredContentPackages.Remove((string)tickBox.UserData);
                    }
                    return true;
                };
            }


            var buttonArea = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), paddedSaveFrame.RectTransform, Anchor.BottomCenter, minSize: new Point(0, 30)), style: null);

            var cancelButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Cancel"))
            {
                OnClicked = (GUIButton btn, object userdata) =>
                {
                    saveFrame = null;
                    return true;
                }
            };

            var saveButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomRight),
                TextManager.Get("SaveSubButton"))
            {
                OnClicked = SaveSub
            };
            paddedSaveFrame.Recalculate();
            leftColumn.Recalculate();
            descriptionBox.Text = Submarine.MainSub == null ? "" : Submarine.MainSub.Description;
            submarineDescriptionCharacterCount.Text = descriptionBox.Text.Length + " / " + submarineDescriptionLimit;
        }


        private void CreateSaveAssemblyScreen()
        {
            SetMode(Mode.Default);

            saveFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker")
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) saveFrame = null; return true; }
            };

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.25f, 0.3f), saveFrame.RectTransform, Anchor.Center) { MinSize = new Point(400, 300) });
            GUILayoutGroup paddedSaveFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), innerFrame.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 5,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedSaveFrame.RectTransform),                 
                TextManager.Get("SaveItemAssemblyDialogHeader"), font: GUI.LargeFont);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedSaveFrame.RectTransform), 
                TextManager.Get("SaveItemAssemblyDialogName"));
            nameBox = new GUITextBox(new RectTransform(new Vector2(0.6f, 0.1f), paddedSaveFrame.RectTransform));

#if DEBUG
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedSaveFrame.RectTransform), TextManager.Get("SaveItemAssemblyHideInMenus"))
            {
                UserData = "hideinmenus"
            };
#endif

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedSaveFrame.RectTransform), 
                TextManager.Get("SaveItemAssemblyDialogDescription"));
            descriptionBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.3f), paddedSaveFrame.RectTransform))
            {
                UserData = "description",
                Wrap = true,
                Text = ""
            };
            
            var buttonArea = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), paddedSaveFrame.RectTransform), style: null);
            new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonArea.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Cancel"))
            {
                OnClicked = (GUIButton btn, object userdata) =>
                {
                    saveFrame = null;
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonArea.RectTransform, Anchor.BottomRight),
                TextManager.Get("SaveSubButton"))
            {
                OnClicked = SaveAssembly
            };
        }

        private bool SaveAssembly(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                GUI.AddMessage(TextManager.Get("ItemAssemblyNameMissingWarning"), GUI.Style.Red);

                nameBox.Flash();
                return false;
            }

            foreach (char illegalChar in Path.GetInvalidFileNameChars())
            {
                if (nameBox.Text.Contains(illegalChar))
                {
                    GUI.AddMessage(TextManager.GetWithVariable("ItemAssemblyNameIllegalCharsWarning", "[illegalchar]", illegalChar.ToString()), GUI.Style.Red);
                    nameBox.Flash();
                    return false;
                }
            }

            var hideInMenusTickBox = nameBox.Parent.GetChildByUserData("hideinmenus") as GUITickBox;
            bool hideInMenus = hideInMenusTickBox == null ? false : hideInMenusTickBox.Selected;
            
            string saveFolder = Path.Combine("Content", "Items", "Assemblies");
            string filePath = Path.Combine(saveFolder, nameBox.Text + ".xml");

            if (File.Exists(filePath))
            {
                var msgBox = new GUIMessageBox(TextManager.Get("Warning"), TextManager.Get("ItemAssemblyFileExistsWarning"), new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                msgBox.Buttons[0].OnClicked = (btn, userdata) =>
                {
                    msgBox.Close();
                    ItemAssemblyPrefab.Remove(filePath);
                    Save();
                    return true;
                };
                msgBox.Buttons[1].OnClicked = msgBox.Close;
            }
            else
            {
                Save();
            }

            void Save()
            {
                XDocument doc = new XDocument(ItemAssemblyPrefab.Save(MapEntity.SelectedList, nameBox.Text, descriptionBox.Text, hideInMenus));
                doc.Save(filePath);

                new ItemAssemblyPrefab(filePath);
                UpdateEntityList();
            }

            saveFrame = null;
            return false;
        }

        private bool CreateLoadScreen()
        {
            SetMode(Mode.Default);

            loadFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker")
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) loadFrame = null; return true; },
            };

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.2f, 0.36f), loadFrame.RectTransform, Anchor.Center) { MinSize = new Point(350, 500) });

            var paddedLoadFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), innerFrame.RectTransform, Anchor.Center)) { Stretch = true, RelativeSpacing = 0.02f };

            var deleteButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedLoadFrame.RectTransform, Anchor.Center));
            
            var searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedLoadFrame.RectTransform), font: GUI.Font, createClearButton: true);
            var searchTitle = new GUITextBlock(new RectTransform(Vector2.One, searchBox.RectTransform), TextManager.Get("serverlog.filter"),
                textAlignment: Alignment.CenterLeft, font: GUI.Font)
            {
                CanBeFocused = false,
                IgnoreLayoutGroups = true
            };
            searchTitle.TextColor *= 0.5f;

            var subList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedLoadFrame.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (GUIComponent selected, object userData) =>
                {
                    if (deleteButtonHolder.FindChild("delete") is GUIButton deleteBtn)
                    {
#if DEBUG
                        deleteBtn.Enabled = true;
#else
                        deleteBtn.Enabled = userData is Submarine sub && !sub.IsVanillaSubmarine();
#endif
                    }
                    return true;
                }
            };

            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (textBox, text) => { FilterSubs(subList, text); return true; };

            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), subList.Content.RectTransform) { MinSize = new Point(0, 30) },
                    ToolBox.LimitString(sub.Name, GUI.Font, subList.Rect.Width - 80))
                {
                    UserData = sub,
                    ToolTip = sub.FilePath
                };

                if (sub.HasTag(SubmarineTag.Shuttle))
                {
                    var shuttleText = new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), textBlock.RectTransform, Anchor.CenterRight),
                        TextManager.Get("Shuttle", fallBackTag: "RespawnShuttle"), textAlignment: Alignment.CenterRight, font: GUI.SmallFont)
                        {
                            TextColor = textBlock.TextColor * 0.8f,
                            ToolTip = textBlock.RawToolTip
                        };
                }
            }

            var deleteButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), deleteButtonHolder.RectTransform, Anchor.TopCenter),
                TextManager.Get("Delete"))
            {
                Enabled = false,
                UserData = "delete"
            };
            deleteButton.OnClicked = (btn, userdata) =>
            {
                if (subList.SelectedComponent != null)
                {
                    TryDeleteSub(subList.SelectedComponent.UserData as Submarine);
                }
                deleteButton.Enabled = false;
                return true;
            };

            var controlBtnHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedLoadFrame.RectTransform), isHorizontal: true) { RelativeSpacing = 0.2f, Stretch = true };

            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), controlBtnHolder.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Cancel"))
            {
                OnClicked = (GUIButton btn, object userdata) =>
                {
                    loadFrame = null;
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), controlBtnHolder.RectTransform, Anchor.BottomRight),
                TextManager.Get("Load"))
            {
                OnClicked = LoadSub
            };

            return true;
        }

        private void FilterSubs(GUIListBox subList, string filter)
        {
            foreach (GUIComponent child in subList.Content.Children)
            {
                if (!(child.UserData is Submarine sub)) { return; }
                child.Visible = string.IsNullOrEmpty(filter) ? true : sub.Name.ToLower().Contains(filter.ToLower());
            }
        }

        private bool LoadSub(GUIButton button, object obj)
        {
            if (loadFrame == null)
            {
                DebugConsole.NewMessage("load frame null", Color.Red);
                return false;
            }

            GUIListBox subList = loadFrame.GetAnyChild<GUIListBox>();
            if (subList == null)
            {
                DebugConsole.NewMessage("Sublist null", Color.Red);
                return false;
            }

            if (subList.SelectedComponent == null) { return false; }
            if (!(subList.SelectedComponent.UserData is Submarine selectedSub)) { return false; }

            selectedSub.Load(true);
            Submarine.MainSub = selectedSub;
            Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
            Submarine.MainSub.UpdateTransform();

            cam.Position = Submarine.MainSub.Position + Submarine.MainSub.HiddenSubPosition;

            loadFrame = null;
            
            //turn off lights that are inside an inventory (cabinet for example)
            foreach (Item item in Item.ItemList)
            {
                var lightComponent = item.GetComponent<LightComponent>();
                if (lightComponent != null) lightComponent.Light.Enabled = item.ParentInventory == null;
            }

            if (selectedSub.GameVersion < new Version("0.8.9.0"))
            {
                var adjustLightsPrompt = new GUIMessageBox(TextManager.Get("Warning"), TextManager.Get("AdjustLightsPrompt"), 
                    new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                adjustLightsPrompt.Buttons[0].OnClicked += adjustLightsPrompt.Close;
                adjustLightsPrompt.Buttons[0].OnClicked += (btn, userdata) =>
                {
                    foreach (Item item in Item.ItemList)
                    {
                        if (item.ParentInventory != null || item.body != null) continue;
                        var lightComponent = item.GetComponent<LightComponent>();
                        if (lightComponent != null) lightComponent.LightColor = new Color(lightComponent.LightColor, lightComponent.LightColor.A / 255.0f * 0.5f);
                    }
                    new GUIMessageBox("", TextManager.Get("AdjustedLightsNotification"));
                    return true;
                };
                adjustLightsPrompt.Buttons[1].OnClicked += adjustLightsPrompt.Close;
            }

            return true;
        }

        private void TryDeleteSub(Submarine sub)
        {
            if (sub == null) { return; }

            //if the sub is included in a content package that only defines that one sub,
            //delete the content package as well
            ContentPackage subPackage = null;
            foreach (ContentPackage cp in ContentPackage.List)
            {
                if (!cp.CorePackage && cp.Files.Count == 1 && Path.GetFullPath(cp.Files[0].Path) == Path.GetFullPath(sub.FilePath))
                {
                    subPackage = cp;
                    break;
                }
            }
            subPackage?.Delete();

            var msgBox = new GUIMessageBox(
                TextManager.Get("DeleteDialogLabel"),
                TextManager.GetWithVariable("DeleteDialogQuestion", "[file]", sub.Name), 
                new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
            msgBox.Buttons[0].OnClicked += (btn, userData) => 
            {
                try
                {
                    sub.Remove();
                    File.Delete(sub.FilePath);
                    Submarine.RefreshSavedSubs();
                    CreateLoadScreen();
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError(TextManager.GetWithVariable("DeleteFileError", "[file]", sub.FilePath), e);
                }
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked += msgBox.Close;            
        }

        private bool OpenEntityMenu(MapEntityCategory selectedCategory)
        {
            entityFilterBox.Text = "";
            SetMode(Mode.Default);

            saveFrame = null;
            loadFrame = null;

            ClearFilter();
            foreach (GUIButton button in entityCategoryButtons)
            {
                button.Selected = 
                    button.UserData != null &&
                    (MapEntityCategory)button.UserData == selectedCategory;
            }
            
            foreach (GUIComponent child in toggleEntityMenuButton.Children)
            {
                child.SpriteEffects = entityMenuOpen ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }

            foreach (GUIComponent child in entityList.Content.Children)
            {
                child.Visible = ((MapEntityPrefab)child.UserData).Category == selectedCategory;
            }
            entityList.UpdateScrollBarSize();
            entityList.BarScroll = 0.0f;

            return true;
        }

        private bool FilterEntities(string filter)
        {
            foreach (GUIButton button in entityCategoryButtons)
            {
                button.Selected = false;
            }

            if (string.IsNullOrWhiteSpace(filter))
            {
                entityList.Content.Children.ForEach(c => c.Visible = true);
                return true;
            }

            filter = filter.ToLower();
            foreach (GUIComponent child in entityList.Content.Children)
            {
                var textBlock = child.GetChild<GUITextBlock>();
                child.Visible = ((MapEntityPrefab)child.UserData).Name.ToLower().Contains(filter);
            }
            entityList.UpdateScrollBarSize();
            entityList.BarScroll = 0.0f;

            return true;
        }

        public bool ClearFilter()
        {
            FilterEntities("");
            entityList.UpdateScrollBarSize();
            entityList.BarScroll = 0.0f;
            entityFilterBox.Text = "";
            return true;
        }

        public void SetMode(Mode mode)
        {
            if (mode == this.mode) { return; }

            this.mode = mode;
            modeButtonGroup.Selected = (int)mode;

            switch (mode)
            {
                case Mode.Character:
                    CreateDummyCharacter();
                    break;
                case Mode.Wiring:
                    CreateDummyCharacter();
                    var item = new Item(MapEntityPrefab.Find(null, "screwdriver") as ItemPrefab, Vector2.Zero, null);
                    dummyCharacter.Inventory.TryPutItem(item, null, new List<InvSlotType>() { InvSlotType.RightHand });
                    wiringToolPanel = CreateWiringPanel();
                    break;
                default:
                    if (dummyCharacter != null)
                    {
                        RemoveDummyCharacter();
                    }
                    break;
            }

            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                me.IsHighlighted = false;
            }

            MapEntity.DeselectAll();
        }

        private void RemoveDummyCharacter()
        {
            if (dummyCharacter == null) { return; }

            foreach (Item item in dummyCharacter.Inventory.Items)
            {
                if (item == null) { continue; }

                item.Remove();
            }

            dummyCharacter.Remove();
            dummyCharacter = null;            
        }

        private void CreateContextMenu()
        {
            List<MapEntity> targets = MapEntity.mapEntityList.Any(me => me.IsHighlighted && !MapEntity.SelectedList.Contains(me)) ? 
                MapEntity.mapEntityList.Where(me => me.IsHighlighted).ToList() :
                new List<MapEntity>(MapEntity.SelectedList);
                        
            contextMenu = new GUIListBox(new RectTransform(new Vector2(0.1f, 0.1f), GUI.Canvas)
            {
                MinSize = new Point(180,0),
                ScreenSpaceOffset = PlayerInput.MousePosition.ToPoint()
            }, style: "GUIToolTip");

            new GUITextBlock(new RectTransform(new Point(contextMenu.Rect.Width, (int)(18 * GUI.Scale)), contextMenu.Content.RectTransform),
                TextManager.Get("editor.cut"), font: GUI.SmallFont)
            {
                UserData = "cut",
                Enabled = targets.Count > 0
            };
            new GUITextBlock(new RectTransform(new Point(contextMenu.Rect.Width, (int)(18 * GUI.Scale)), contextMenu.Content.RectTransform),
                TextManager.Get("editor.copytoclipboard"), font: GUI.SmallFont)
            {
                UserData = "copy",
                Enabled = targets.Count > 0
            };
            new GUITextBlock(new RectTransform(new Point(contextMenu.Rect.Width, (int)(18 * GUI.Scale)), contextMenu.Content.RectTransform),
                TextManager.Get("editor.paste"), font: GUI.SmallFont)
            {
                UserData = "paste",
                Enabled = MapEntity.CopiedList.Any()
            };            
            new GUITextBlock(new RectTransform(new Point(contextMenu.Rect.Width, (int)(18 * GUI.Scale)), contextMenu.Content.RectTransform),
                TextManager.Get("delete"), font: GUI.SmallFont)
            {
                UserData = "delete",
                Enabled = targets.Count > 0
            };

            foreach (GUITextBlock child in contextMenu.Content.Children)
            {
                if (!child.Enabled) { child.TextColor *= 0.5f; }
            }
            
            contextMenu.RectTransform.NonScaledSize = new Point(
                contextMenu.Rect.Width, 
                (int)((contextMenu.Content.CountChildren * 18) * GUI.Scale));
            
            contextMenu.OnSelected = (GUIComponent component, object obj) =>
            {
                if (!component.Enabled) { return false; }
                switch (obj as string)
                {
                    case "copy":
                        MapEntity.Copy(targets);
                        break;
                    case "cut":
                        MapEntity.Cut(targets);
                        break;
                    case "paste":
                        MapEntity.Paste(cam.ScreenToWorld(contextMenu.Rect.Location.ToVector2()));
                        break;
                    case "delete":
                        targets.ForEach(me => me.Remove());
                        break;
                }
                contextMenu = null;
                return true;
            };
        }

        private GUIFrame CreateWiringPanel()
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(0.03f, 0.35f), GUI.Canvas)
                { MinSize = new Point(120, 300), AbsoluteOffset = new Point((int)(10 * GUI.Scale), TopPanel.Rect.Height + entityCountPanel.Rect.Height + (int)(10 * GUI.Scale)) });

            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center))
            {
                OnSelected = SelectWire
            };

            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                if (string.IsNullOrEmpty(itemPrefab.Name)) { continue; }
                if (!itemPrefab.Tags.Contains("wire")) { continue; }

                GUIFrame imgFrame = new GUIFrame(new RectTransform(new Point(listBox.Content.Rect.Width, listBox.Rect.Width / 2), listBox.Content.RectTransform), style: "ListBoxElement")
                {
                    UserData = itemPrefab
                };

                var img = new GUIImage(new RectTransform(new Vector2(0.9f), imgFrame.RectTransform, Anchor.Center), itemPrefab.sprite, scaleToFit: true)
                {
                    UserData = itemPrefab,
                    Color = itemPrefab.SpriteColor
                };
            }

            return frame;
        }

        private bool SelectLinkedSub(GUIComponent selected, object userData)
        {
            if (!(selected.UserData is Submarine submarine)) return false;
            var prefab = new LinkedSubmarinePrefab(submarine);
            MapEntityPrefab.SelectPrefab(prefab);
            return true;
        }

        private bool SelectWire(GUIComponent component, object userData)
        {
            if (dummyCharacter == null) return false;

            //if the same type of wire has already been selected, deselect it and return
            Item existingWire = dummyCharacter.SelectedItems.FirstOrDefault(i => i != null && i.Prefab == userData as ItemPrefab);
            if (existingWire != null)
            {
                existingWire.Drop(null);
                existingWire.Remove();
                return false;
            }

            var wire = new Item(userData as ItemPrefab, Vector2.Zero, null);

            int slotIndex = dummyCharacter.Inventory.FindLimbSlot(InvSlotType.LeftHand);

            //if there's some other type of wire in the inventory, remove it
            existingWire = dummyCharacter.Inventory.Items[slotIndex];
            if (existingWire != null && existingWire.Prefab != userData as ItemPrefab)
            {
                existingWire.Drop(null);
                existingWire.Remove();
            }

            dummyCharacter.Inventory.TryPutItem(wire, slotIndex, false, false, dummyCharacter);

            return true;
           
        }

        private bool ChangeSubName(GUITextBox textBox, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                textBox.Flash(GUI.Style.Red);
                return false;
            }

            if (Submarine.MainSub != null) Submarine.MainSub.Name = text;
            textBox.Deselect();

            textBox.Text = text;

            textBox.Flash(GUI.Style.Green);

            return true;
        }

        private bool ChangeSubDescription(GUITextBox textBox, string text)
        {
            if (Submarine.MainSub != null)
            {
                Submarine.MainSub.Description = text;
            }
            else
            {
                textBox.UserData = text;
            }

            submarineDescriptionCharacterCount.Text = text.Length + " / " + submarineDescriptionLimit;

            return true;
        }
        
        private bool SelectPrefab(GUIComponent component, object obj)
        {
            if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }

            AddPreviouslyUsed(obj as MapEntityPrefab);

            //if selecting a gap/hull/waypoint/spawnpoint, make sure the visibility is toggled on
            if (obj is CoreEntityPrefab prefab)
            {
                var matchingTickBox = showEntitiesTickBoxes.Find(tb => tb.UserData as string == prefab.Identifier);
                if (matchingTickBox != null && !matchingTickBox.Selected)
                {
                    previouslyUsedPanel.Visible = false;
                    showEntitiesPanel.Visible = true;
                    matchingTickBox.Selected = true;
                    matchingTickBox.Flash(GUI.Style.Green);
                }
            }

            MapEntityPrefab.SelectPrefab(obj);
            GUI.ForceMouseOn(null);
            return false;
        }

        private bool GenerateWaypoints(GUIButton button, object obj)
        {
            if (Submarine.MainSub == null) return false;

            WayPoint.GenerateSubWaypoints(Submarine.MainSub);
            return true;
        }

        private void AddPreviouslyUsed(MapEntityPrefab mapEntityPrefab)
        {
            if (previouslyUsedList == null || mapEntityPrefab == null) { return; }

            previouslyUsedList.Deselect();

            if (previouslyUsedList.CountChildren == PreviouslyUsedCount)
            {
                previouslyUsedList.RemoveChild(previouslyUsedList.Content.Children.Last());
            }

            var existing = previouslyUsedList.Content.FindChild(mapEntityPrefab);
            if (existing != null) { previouslyUsedList.Content.RemoveChild(existing); }

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), previouslyUsedList.Content.RectTransform) { MinSize = new Point(0, 15) },
                ToolBox.LimitString(mapEntityPrefab.Name, GUI.SmallFont, previouslyUsedList.Content.Rect.Width), font: GUI.SmallFont)
            {
                UserData = mapEntityPrefab
            };
            textBlock.RectTransform.SetAsFirstChild();
        }

        public void AutoHull()
        {
            for (int i = 0; i < MapEntity.mapEntityList.Count; i++)
            {
                MapEntity h = MapEntity.mapEntityList[i];
                if (h is Hull || h is Gap)
                {
                    h.Remove();
                    i--;
                }
            }

            List<Vector2> wallPoints = new List<Vector2>();
            Vector2 min = Vector2.Zero;
            Vector2 max = Vector2.Zero;

            List<MapEntity> mapEntityList = new List<MapEntity>();

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (e is Item)
                {
                    Item it = e as Item;
                    Door door = it.GetComponent<Door>();
                    if (door != null)
                    {
                        int halfW = e.WorldRect.Width / 2;
                        wallPoints.Add(new Vector2(e.WorldRect.X + halfW, -e.WorldRect.Y + e.WorldRect.Height));
                        mapEntityList.Add(it);
                    }
                    continue;
                }

                if (!(e is Structure)) continue;
                Structure s = e as Structure;
                if (!s.HasBody) continue;
                mapEntityList.Add(e);

                if (e.Rect.Width > e.Rect.Height)
                {
                    int halfH = e.WorldRect.Height / 2;
                    wallPoints.Add(new Vector2(e.WorldRect.X, -e.WorldRect.Y + halfH));
                    wallPoints.Add(new Vector2(e.WorldRect.X + e.WorldRect.Width, -e.WorldRect.Y + halfH));
                }
                else
                {
                    int halfW = e.WorldRect.Width / 2;
                    wallPoints.Add(new Vector2(e.WorldRect.X + halfW, -e.WorldRect.Y));
                    wallPoints.Add(new Vector2(e.WorldRect.X + halfW, -e.WorldRect.Y + e.WorldRect.Height));
                }
            }

            if (wallPoints.Count < 4)
            {
                DebugConsole.ThrowError("Generating hulls for the submarine failed. Not enough wall structures to generate hulls.");
                return;
            }

            min = wallPoints[0];
            max = wallPoints[0];
            for (int i = 0; i < wallPoints.Count; i++)
            {
                min.X = Math.Min(min.X, wallPoints[i].X);
                min.Y = Math.Min(min.Y, wallPoints[i].Y);
                max.X = Math.Max(max.X, wallPoints[i].X);
                max.Y = Math.Max(max.Y, wallPoints[i].Y);
            }

            List<Rectangle> hullRects = new List<Rectangle>
            {
                new Rectangle((int)min.X, (int)min.Y, (int)(max.X - min.X), (int)(max.Y - min.Y))
            };
            foreach (Vector2 point in wallPoints)
            {
                MathUtils.SplitRectanglesHorizontal(hullRects, point);
                MathUtils.SplitRectanglesVertical(hullRects, point);
            }

            hullRects.Sort((a, b) =>
            {
                if (a.Y < b.Y) return -1;
                if (a.Y > b.Y) return 1;
                if (a.X < b.X) return -1;
                if (a.X > b.X) return 1;
                return 0;
            });

            for (int i = 0; i < hullRects.Count - 1; i++)
            {
                Rectangle rect = hullRects[i];
                if (hullRects[i + 1].Y > rect.Y) continue;

                Vector2 hullRPoint = new Vector2(rect.X + rect.Width - 8, rect.Y + rect.Height / 2);
                Vector2 hullLPoint = new Vector2(rect.X, rect.Y + rect.Height / 2);

                MapEntity container = null;
                foreach (MapEntity e in mapEntityList)
                {
                    Rectangle entRect = e.WorldRect;
                    entRect.Y = -entRect.Y;
                    if (entRect.Contains(hullRPoint))
                    {
                        if (!entRect.Contains(hullLPoint)) container = e;
                        break;
                    }
                }
                if (container == null)
                {
                    rect.Width += hullRects[i + 1].Width;
                    hullRects[i] = rect;
                    hullRects.RemoveAt(i + 1);
                    i--;
                }
            }
            
            foreach (MapEntity e in mapEntityList)
            {
                Rectangle entRect = e.WorldRect;
                if (entRect.Width < entRect.Height) continue;
                entRect.Y = -entRect.Y - 16;
                for (int i = 0; i < hullRects.Count; i++)
                {
                    Rectangle hullRect = hullRects[i];
                    if (entRect.Intersects(hullRect))
                    {
                        if (hullRect.Y < entRect.Y)
                        {
                            hullRect.Height = Math.Max((entRect.Y + 16 + entRect.Height / 2) - hullRect.Y, hullRect.Height);
                            hullRects[i] = hullRect;
                        }
                        else if (hullRect.Y + hullRect.Height <= entRect.Y + 16 + entRect.Height)
                        {
                            hullRects.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }

            foreach (MapEntity e in mapEntityList)
            {
                Rectangle entRect = e.WorldRect;
                if (entRect.Width < entRect.Height) continue;
                entRect.Y = -entRect.Y;
                for (int i = 0; i < hullRects.Count; i++)
                {
                    Rectangle hullRect = hullRects[i];
                    if (entRect.Intersects(hullRect))
                    {
                        if (hullRect.Y >= entRect.Y - 8 && hullRect.Y + hullRect.Height <= entRect.Y + entRect.Height + 8)
                        {
                            hullRects.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }
            
            for (int i = 0; i < hullRects.Count;)
            {
                Rectangle hullRect = hullRects[i];
                Vector2 point = new Vector2(hullRect.X+2, hullRect.Y+hullRect.Height/2);
                MapEntity container = null;
                foreach (MapEntity e in mapEntityList)
                {
                    Rectangle entRect = e.WorldRect;
                    entRect.Y = -entRect.Y;
                    if (entRect.Contains(point))
                    {
                        container = e;
                        break;
                    }
                }
                if (container == null)
                {
                    hullRects.RemoveAt(i);
                    continue;
                }

                while (hullRects[i].Y <= hullRect.Y)
                {
                    i++;
                    if (i >= hullRects.Count) break;
                }
            }
            
            for (int i = hullRects.Count-1; i >= 0;)
            {
                Rectangle hullRect = hullRects[i];
                Vector2 point = new Vector2(hullRect.X+hullRect.Width-2, hullRect.Y+hullRect.Height/2);
                MapEntity container = null;
                foreach (MapEntity e in mapEntityList)
                {
                    Rectangle entRect = e.WorldRect;
                    entRect.Y = -entRect.Y;
                    if (entRect.Contains(point))
                    {
                        container = e;
                        break;
                    }
                }
                if (container == null)
                {
                    hullRects.RemoveAt(i); i--;
                    continue;
                }

                while (hullRects[i].Y >= hullRect.Y)
                {
                    i--;
                    if (i < 0) break;
                }
            }
            
            hullRects.Sort((a, b) =>
            {
                if (a.X < b.X) return -1;
                if (a.X > b.X) return 1;
                if (a.Y < b.Y) return -1;
                if (a.Y > b.Y) return 1;
                return 0;
            });
            
            for (int i = 0; i < hullRects.Count - 1; i++)
            {
                Rectangle rect = hullRects[i];
                if (hullRects[i + 1].Width != rect.Width) continue;
                if (hullRects[i + 1].X > rect.X) continue;

                Vector2 hullBPoint = new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height - 8);
                Vector2 hullUPoint = new Vector2(rect.X + rect.Width / 2, rect.Y);

                MapEntity container = null;
                foreach (MapEntity e in mapEntityList)
                {
                    Rectangle entRect = e.WorldRect;
                    entRect.Y = -entRect.Y;
                    if (entRect.Contains(hullBPoint))
                    {
                        if (!entRect.Contains(hullUPoint)) container = e;
                        break;
                    }
                }
                if (container == null)
                {
                    rect.Height += hullRects[i + 1].Height;
                    hullRects[i] = rect;
                    hullRects.RemoveAt(i + 1);
                    i--;
                }
            }
            
            for (int i = 0; i < hullRects.Count;i++)
            {
                Rectangle rect = hullRects[i];
                rect.Y -= 16;
                rect.Height += 32;
                hullRects[i] = rect;
            }
            
            hullRects.Sort((a, b) =>
            {
                if (a.Y < b.Y) return -1;
                if (a.Y > b.Y) return 1;
                if (a.X < b.X) return -1;
                if (a.X > b.X) return 1;
                return 0;
            });
            
            for (int i = 0; i < hullRects.Count; i++)
            {
                for (int j = i+1; j < hullRects.Count; j++)
                {
                    if (hullRects[j].Y <= hullRects[i].Y) continue;
                    if (hullRects[j].Intersects(hullRects[i]))
                    {
                        Rectangle rect = hullRects[i];
                        rect.Height = hullRects[j].Y - rect.Y;
                        hullRects[i] = rect;
                        break;
                    }
                }
            }

            foreach (Rectangle rect in hullRects)
            {
                Rectangle hullRect = rect;
                hullRect.Y = -hullRect.Y;
                Hull newHull = new Hull(MapEntityPrefab.Find(null, "hull"),
                                        hullRect,
                                        Submarine.MainSub);
            }

            foreach (MapEntity e in mapEntityList)
            {
                if (!(e is Structure)) continue;
                if (!(e as Structure).IsPlatform) continue;

                Rectangle gapRect = e.WorldRect;
                gapRect.Y -= 8;
                gapRect.Height = 16;
                Gap newGap = new Gap(MapEntityPrefab.Find(null, "gap"),
                                        gapRect);
            }
        }
        
        public override void AddToGUIUpdateList()
        {
            MapEntity.FilteredSelectedList.FirstOrDefault()?.AddToGUIUpdateList();
            EntityMenu.AddToGUIUpdateList();
            showEntitiesPanel.AddToGUIUpdateList();
            previouslyUsedPanel.AddToGUIUpdateList();
            entityCountPanel.AddToGUIUpdateList();
            TopPanel.AddToGUIUpdateList();

            if (WiringMode)
            {
                wiringToolPanel.AddToGUIUpdateList();
            }

            if (contextMenu != null)
            {
                contextMenu.AddToGUIUpdateList();
            }
            else if (MapEntity.HighlightedListBox != null)
            {
                MapEntity.HighlightedListBox.AddToGUIUpdateList();
            }

            if ((CharacterMode || WiringMode) && dummyCharacter != null)
            {
                CharacterHUD.AddToGUIUpdateList(dummyCharacter);
                if (dummyCharacter.SelectedConstruction != null)
                {
                    dummyCharacter.SelectedConstruction.AddToGUIUpdateList();
                }
                else if (WiringMode && MapEntity.SelectedList.Count == 1 && MapEntity.SelectedList[0] is Item item && item.GetComponent<Wire>() != null)
                {
                    MapEntity.SelectedList[0].AddToGUIUpdateList();
                }
            }
            else
            {
                if (loadFrame != null)
                {
                    loadFrame.AddToGUIUpdateList();
                }
                else if (saveFrame != null)
                {
                    saveFrame.AddToGUIUpdateList();
                }
            }
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        public override void Update(double deltaTime)
        {
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y)
            {
                saveFrame = null;
                loadFrame = null;
                saveAssemblyFrame = null;
                CreateUI();
                UpdateEntityList();
            }

            hullVolumeFrame.Visible = MapEntity.SelectedList.Any(s => s is Hull);
            saveAssemblyFrame.Visible = MapEntity.SelectedList.Count > 0;
            
            cam.MoveCamera((float)deltaTime, true);       
            if (PlayerInput.MidButtonHeld())
            {
                Vector2 moveSpeed = PlayerInput.MouseSpeed * (float)deltaTime * 100.0f / cam.Zoom;
                moveSpeed.X = -moveSpeed.X;
                cam.Position += moveSpeed;
            }

            if (contextMenu != null)
            {
                Rectangle expandedRect = contextMenu.Rect;
                expandedRect.Inflate(20, 20);
                if (!expandedRect.Contains(PlayerInput.MousePosition))
                {
                    contextMenu = null;
                }                
            }

            if (CharacterMode || WiringMode)
            {
                if (dummyCharacter == null || Entity.FindEntityByID(dummyCharacter.ID) != dummyCharacter)
                {
                    SetMode(Mode.Default);
                }
                else
                {
                    foreach (MapEntity me in MapEntity.mapEntityList)
                    {
                        me.IsHighlighted = false;
                    }

                    if (WiringMode && dummyCharacter.SelectedConstruction == null)
                    {
                        List<Wire> wires = new List<Wire>();
                        foreach (Item item in Item.ItemList)
                        {
                            var wire = item.GetComponent<Wire>();
                            if (wire != null) wires.Add(wire);
                        }
                        Wire.UpdateEditing(wires);
                    }

                    if (dummyCharacter.SelectedConstruction == null || 
                        dummyCharacter.SelectedConstruction.GetComponent<Pickable>() != null)
                    {
                        if (WiringMode && (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) || PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.Right)))
                        {
                            Wire equippedWire =
                                Character.Controlled?.SelectedItems[0]?.GetComponent<Wire>() ??
                                Character.Controlled?.SelectedItems[1]?.GetComponent<Wire>();
                            if (equippedWire != null && equippedWire.GetNodes().Count > 0)
                            {
                                Vector2 lastNode = equippedWire.GetNodes().Last();
                                if (equippedWire.Item.Submarine != null)
                                {
                                    lastNode += equippedWire.Item.Submarine.HiddenSubPosition + equippedWire.Item.Submarine.Position;
                                }

                                dummyCharacter.CursorPosition =
                                    Math.Abs(dummyCharacter.CursorPosition.X - lastNode.X) < Math.Abs(dummyCharacter.CursorPosition.Y - lastNode.Y) ?
                                        new Vector2(lastNode.X, dummyCharacter.CursorPosition.Y) :
                                        dummyCharacter.CursorPosition = new Vector2(dummyCharacter.CursorPosition.X, lastNode.Y);
                            }
                        }

                        Vector2 mouseSimPos = FarseerPhysics.ConvertUnits.ToSimUnits(dummyCharacter.CursorPosition);
                        foreach (Limb limb in dummyCharacter.AnimController.Limbs)
                        {
                            limb.body.SetTransform(mouseSimPos, 0.0f);
                        }
                        dummyCharacter.AnimController.Collider.SetTransform(mouseSimPos, 0.0f);
                    }

                    dummyCharacter.ControlLocalPlayer((float)deltaTime, cam, false);
                    dummyCharacter.Control((float)deltaTime, cam);

                    dummyCharacter.Submarine = Submarine.MainSub;

                    cam.TargetPos = Vector2.Zero;
                }
            }
            else if (!saveAssemblyFrame.Rect.Contains(PlayerInput.MousePosition))
            {
                MapEntity.UpdateSelecting(cam);
            }

            if (!CharacterMode && !WiringMode)
            {
                if (MapEntityPrefab.Selected != null && GUI.MouseOn == null)
                {
                    MapEntityPrefab.Selected.UpdatePlacing(cam);
                }
                else
                {
                    if (PlayerInput.RightButtonClicked())
                    {
                        CreateContextMenu();                    
                    }
                }                
                MapEntity.UpdateEditor(cam);
            }

            entityMenuOpenState = entityMenuOpen && !CharacterMode & !WiringMode ? 
                (float)Math.Min(entityMenuOpenState + deltaTime * 5.0f, 1.0f) :
                (float)Math.Max(entityMenuOpenState - deltaTime * 5.0f, 0.0f);

            EntityMenu.RectTransform.ScreenSpaceOffset = Vector2.Lerp(new Vector2(0.0f, EntityMenu.Rect.Height - 10), Vector2.Zero, entityMenuOpenState).ToPoint();

            if (WiringMode && dummyCharacter != null)
            {
                if (!dummyCharacter.SelectedItems.Any(it => it != null && it.HasTag("wire")))
                {
                    wiringToolPanel.GetChild<GUIListBox>().Deselect();
                }
            }

            if (PlayerInput.PrimaryMouseButtonClicked() && !GUI.IsMouseOn(entityFilterBox))
            {
                entityFilterBox.Deselect();
            }

            if (loadFrame != null)
            {
                if (PlayerInput.SecondaryMouseButtonClicked())
                {
                    loadFrame = null;
                }
            }
            else if (saveFrame != null)
            {
                if (PlayerInput.SecondaryMouseButtonClicked())
                {
                    saveFrame = null;
                }
            }            

            if ((CharacterMode || WiringMode) && dummyCharacter != null)
            {
                dummyCharacter.AnimController.FindHull(dummyCharacter.CursorWorldPosition, false);

                foreach (Item item in dummyCharacter.Inventory.Items)
                {
                    if (item == null) continue;

                    item.SetTransform(dummyCharacter.SimPosition, 0.0f);
                    item.UpdateTransform();
                    item.SetTransform(item.body.SimPosition, 0.0f);

                    //wires need to be updated for the last node to follow the player during rewiring
                    Wire wire = item.GetComponent<Wire>();
                    if (wire != null) wire.Update((float)deltaTime, cam);
                }

                if (dummyCharacter.SelectedConstruction != null)
                {
                    if (dummyCharacter.SelectedConstruction != null)
                    {
                        dummyCharacter.SelectedConstruction.UpdateHUD(cam, dummyCharacter, (float)deltaTime);
                    }

                    //if (PlayerInput.KeyHit(InputType.Select) && dummyCharacter.FocusedItem != dummyCharacter.SelectedConstruction && GUI.KeyboardDispatcher.Subscriber == null)
                    //{
                    //    dummyCharacter.SelectedConstruction = null;
                    //}
                    /*if (PlayerInput.KeyHit(InputType.Deselect))
                    {
                        dummyCharacter.SelectedConstruction = null;
                    }*/
                }
                else if (MapEntity.SelectedList.Count == 1)
                {
                    (MapEntity.SelectedList[0] as Item)?.UpdateHUD(cam, dummyCharacter, (float)deltaTime);
                }

                CharacterHUD.Update((float)deltaTime, dummyCharacter, cam);
            }

            //GUI.Update((float)deltaTime);
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform();
            if (lightingEnabled)
            {
                GameMain.LightManager.UpdateLightMap(graphics, spriteBatch, cam);
            }

            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.UpdateTransform();
            }

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: cam.Transform);
            graphics.Clear(new Color(0.051f, 0.149f, 0.271f, 1.0f));
            if (GameMain.DebugDraw)
            {
                GUI.DrawLine(spriteBatch, new Vector2(Submarine.MainSub.HiddenSubPosition.X, -cam.WorldView.Y), new Vector2(Submarine.MainSub.HiddenSubPosition.X, -(cam.WorldView.Y - cam.WorldView.Height)), Color.White * 0.5f, 1.0f, (int)(2.0f / cam.Zoom));
                GUI.DrawLine(spriteBatch, new Vector2(cam.WorldView.X, -Submarine.MainSub.HiddenSubPosition.Y), new Vector2(cam.WorldView.Right, -Submarine.MainSub.HiddenSubPosition.Y), Color.White * 0.5f, 1.0f, (int)(2.0f / cam.Zoom));
            }
           
            Submarine.DrawBack(spriteBatch, editing: true);

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: cam.Transform);
            Submarine.DrawDamageable(spriteBatch, null, editing: true);
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: cam.Transform);
            Submarine.DrawFront(spriteBatch, editing: true);
            if (!CharacterMode && !WiringMode && GUI.MouseOn == null)
            {
                MapEntityPrefab.Selected?.DrawPlacing(spriteBatch, cam);                
                MapEntity.DrawSelecting(spriteBatch, cam);
            }
            spriteBatch.End();

            if (GameMain.LightManager.LightingEnabled && lightingEnabled)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, Lights.CustomBlendStates.Multiplicative, null, DepthStencilState.None, null, null, null);
                spriteBatch.Draw(GameMain.LightManager.LightMap, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                spriteBatch.End();
            }

            //-------------------- HUD -----------------------------
            
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState);

            if (Submarine.MainSub != null)
            {
                GUI.DrawIndicator(
                    spriteBatch, Submarine.MainSub.WorldPosition, cam,
                    cam.WorldView.Width,
                    GUI.SubmarineIcon, Color.LightBlue * 0.5f);
            }
            
            if ((CharacterMode || WiringMode) && dummyCharacter != null)
            {
                dummyCharacter.DrawHUD(spriteBatch, cam, false);
                if (WiringMode) wiringToolPanel.DrawManually(spriteBatch);
            }
            else
            {
                MapEntity.DrawEditor(spriteBatch, cam);
            }

            GUI.Draw(Cam, spriteBatch);

            if (!PlayerInput.PrimaryMouseButtonHeld()) Inventory.draggingItem = null;
                                              
            spriteBatch.End();
        }

        private void CreateImage(int width, int height, Stream stream)
        {
            MapEntity.SelectedList.Clear();

            var prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;

            Rectangle subDimensions = Submarine.MainSub.CalculateDimensions(false);
            Vector2 viewPos = subDimensions.Center.ToVector2();
            float scale = Math.Min(width / (float)subDimensions.Width, height / (float)subDimensions.Height);

            var viewMatrix = Matrix.CreateTranslation(new Vector3(width / 2.0f, height / 2.0f, 0));
            var transform = Matrix.CreateTranslation(
                new Vector3(-viewPos.X, viewPos.Y, 0)) *
                Matrix.CreateScale(new Vector3(scale, scale, 1)) *
                viewMatrix;

            /*Sprite backgroundSprite = LevelGenerationParams.LevelParams.Find(l => l.BackgroundTopSprite != null).BackgroundTopSprite;*/

            using (RenderTarget2D rt = new RenderTarget2D(
                 GameMain.Instance.GraphicsDevice,
                 width, height, false, SurfaceFormat.Color, DepthFormat.None))
            using (SpriteBatch spriteBatch = new SpriteBatch(GameMain.Instance.GraphicsDevice))
            {
                GameMain.Instance.GraphicsDevice.SetRenderTarget(rt);

                GameMain.Instance.GraphicsDevice.Clear(new Color(8, 13, 19));

                /*if (backgroundSprite != null)
                {
                    spriteBatch.Begin();
                    backgroundSprite.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(width, height), color: new Color(0.025f, 0.075f, 0.131f, 1.0f));
                    spriteBatch.End();
                }*/

                spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, null, null, null, null, transform);
                Submarine.Draw(spriteBatch, false);
                Submarine.DrawFront(spriteBatch);
                Submarine.DrawDamageable(spriteBatch, null);
                spriteBatch.End();
                

                GameMain.Instance.GraphicsDevice.SetRenderTarget(null);
                rt.SaveAsPng(stream, width, height);
            }

            //for some reason setting the rendertarget changes the size of the viewport 
            //but it doesn't change back to default when setting it back to null
            GameMain.Instance.ResetViewPort();
        }

        public void SaveScreenShot(int width, int height, string filePath)
        {
            Stream stream = File.OpenWrite(filePath);
            CreateImage(width, height, stream);
            stream.Dispose();
        }


    }
}
