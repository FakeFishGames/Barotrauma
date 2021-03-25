using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Xna.Framework.Input;
using System.Threading.Tasks;
#if DEBUG
using System.IO;
#else
using Barotrauma.IO;
#endif

// ReSharper disable AccessToModifiedClosure, PossibleLossOfFraction, RedundantLambdaParameterType, UnusedVariable

namespace Barotrauma
{
    class SubEditorScreen : Screen
    {
        private static readonly string[] crewExperienceLevels = 
        {
            "CrewExperienceLow",
            "CrewExperienceMid",
            "CrewExperienceHigh"
        };

        public enum Mode
        {
            Default,
            Wiring
        }

        public enum WarningType
        {
            NoWaypoints,
            NoHulls,
            DisconnectedVents,
            NoHumanSpawnpoints,
            NoCargoSpawnpoints,
            NoBallastTag,
            NonLinkedGaps,
            TooManyLights
        }
        
        public static Vector2 MouseDragStart = Vector2.Zero;

        private readonly Point defaultPreviewImageSize = new Point(640, 368);

        private readonly Camera cam;
        private Vector2 camTargetFocus = Vector2.Zero;

        private SubmarineInfo backedUpSubInfo;

        private Point screenResolution;

        private bool lightingEnabled;

        private bool wasSelectedBefore;

        public GUIComponent TopPanel;
        private GUIComponent showEntitiesPanel, entityCountPanel;
        private readonly List<GUITickBox> showEntitiesTickBoxes = new List<GUITickBox>();
        private readonly Dictionary<string, bool> hiddenSubCategories = new Dictionary<string, bool>();

        private GUITextBlock subNameLabel;

        public bool ShowThalamus { get; private set; } = true;

        private bool entityMenuOpen = true;
        private float entityMenuOpenState = 1.0f;
        private string lastFilter;
        public GUIComponent EntityMenu;
        private GUITextBox entityFilterBox;
        private GUIListBox categorizedEntityList, allEntityList;
        private GUIButton toggleEntityMenuButton;

        public GUIButton ToggleEntityMenuButton => toggleEntityMenuButton;

        private GUITickBox defaultModeTickBox, wiringModeTickBox;

        private GUIComponent loadFrame, saveFrame;

        private GUITextBox nameBox, descriptionBox;

        private GUIButton selectedCategoryButton;
        private GUITextBlock selectedCategoryText;
        private readonly List<GUIButton> entityCategoryButtons = new List<GUIButton>();
        private MapEntityCategory? selectedCategory;

        private GUIFrame hullVolumeFrame;

        private GUIFrame saveAssemblyFrame;

        const int PreviouslyUsedCount = 10;
        private GUIFrame previouslyUsedPanel;
        private GUIListBox previouslyUsedList;

        private GUIFrame undoBufferPanel;
        private GUIListBox undoBufferList;

        private GUIDropDown linkedSubBox;

        private static GUIComponent autoSaveLabel;
        private static int maxAutoSaves = GameSettings.MaximumAutoSaves;

        public static readonly object ItemAddMutex = new object(), ItemRemoveMutex = new object();

        public static bool TransparentWiringMode = true;

        private static object bulkItemBufferinUse;

        public static object BulkItemBufferInUse
        {
            get => bulkItemBufferinUse;
            set
            {
                if (value != bulkItemBufferinUse && bulkItemBufferinUse != null)
                {
                    CommitBulkItemBuffer();
                }

                bulkItemBufferinUse = value;
            }
        }
        public static List<AddOrDeleteCommand> BulkItemBuffer = new List<AddOrDeleteCommand>();

        public static List<WarningType> SuppressedWarnings = new List<WarningType>();

        public static readonly EditorImageManager ImageManager = new EditorImageManager();

        public static bool ShouldDrawGrid = false;

        //a Character used for picking up and manipulating items
        private Character dummyCharacter;
        
        /// <summary>
        /// Prefab used for dragging from the item catalog into inventories
        /// <see cref="GUI.Draw"/>
        /// </summary>
        public static MapEntityPrefab DraggedItemPrefab;
        
        /// <summary>
        /// Currently opened hand-held item container like crates
        /// </summary>
        private Item OpenedItem;

        /// <summary>
        /// When opening an item we save the location of it so we can teleport the dummy character there
        /// </summary>
        private Vector2 oldItemPosition;

        /// <summary>
        /// Global undo/redo state for the sub editor and a selector index for it
        /// <see cref="Command"/>
        /// </summary>
        public static readonly List<Command> Commands = new List<Command>();
        private static int commandIndex;

        private GUIFrame wiringToolPanel;

        private DateTime editorSelectedTime;

        private GUIImage previewImage;
        private GUILayoutGroup previewImageButtonHolder;

        private const int submarineNameLimit = 30;
        private GUITextBlock submarineNameCharacterCount;

        private const int submarineDescriptionLimit = 500;
        private GUITextBlock submarineDescriptionCharacterCount;

        private Mode mode;

        private Color backgroundColor = GameSettings.SubEditorBackgroundColor;

        private Vector2 MeasurePositionStart = Vector2.Zero;

        // Prevent the mode from changing
        private bool lockMode;

        private static bool isAutoSaving;

        public override Camera Cam => cam;

        public static XDocument AutoSaveInfo;
        private static readonly string autoSavePath = Path.Combine(SubmarineInfo.SavePath, ".AutoSaves");
        private static readonly string autoSaveInfoPath = Path.Combine(autoSavePath, "autosaves.xml");

        private static string GetSubDescription()
        {
            string localizedDescription = TextManager.Get("submarine.description." + (Submarine.MainSub?.Info.Name ?? ""), true);
            if (localizedDescription != null) { return localizedDescription; }
            return (Submarine.MainSub == null) ? "" : Submarine.MainSub.Info.Description;
        }

        private static string GetTotalHullVolume()
        {
            return TextManager.Get("TotalHullVolume") + ":\n" + Hull.hullList.Sum(h => h.Volume);
        }

        private static string GetSelectedHullVolume()
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
                    retVal += " (" + TextManager.GetWithVariable("OptimalBallastLevel", "[value]", (buoyancyVol / selectedVol).ToString("0.0000")) + ")";
                }
                else
                {
                    retVal += " (" + TextManager.Get("InsufficientBallast") + ")";
                }
            }
            return retVal;
        }

        public bool WiringMode => mode == Mode.Wiring;

        public SubEditorScreen()
        {
            cam = new Camera
            {
                MaxZoom = 10f
            };
            WayPoint.ShowWayPoints = false;
            WayPoint.ShowSpawnPoints = false;
            Hull.ShowHulls = false;
            Gap.ShowGaps = false;
            CreateUI();
        }

        private void CreateUI()
        {
            TopPanel = new GUIFrame(new RectTransform(new Vector2(GUI.Canvas.RelativeSize.X, 0.01f), GUI.Canvas) { MinSize = new Point(0, 35) }, "GUIFrameTop");

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
                    var msgBox = new GUIMessageBox("", TextManager.Get("PauseMenuQuitVerificationEditor"), new[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
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
                OnClicked = (btn, data) =>
                {
                    saveFrame = null;
                    CreateLoadScreen();

                    return true;
                }
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");
            
            new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "SaveButton")
            {
                ToolTip = TextManager.Get("SaveSubButton") + "‖color:125,125,125‖\nCtrl + S‖color:end‖",
                OnClicked = (btn, data) =>
                {
                    loadFrame = null;
                    CreateSaveScreen();

                    return true;
                }
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "TestButton")
            {
                ToolTip = TextManager.Get("TestSubButton"),
                OnClicked = TestSubmarine
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            var visibilityButton = new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "SetupVisibilityButton")
            {
                ToolTip = TextManager.Get("SubEditorVisibilityButton") + '\n' + TextManager.Get("SubEditorVisibilityToolTip"),
                OnClicked = (btn, userData) =>
                {
                    previouslyUsedPanel.Visible = false;
                    undoBufferPanel.Visible = false;
                    showEntitiesPanel.Visible = !showEntitiesPanel.Visible;
                    showEntitiesPanel.RectTransform.AbsoluteOffset = new Point(Math.Max(Math.Max(btn.Rect.X, entityCountPanel.Rect.Right), saveAssemblyFrame.Rect.Right), TopPanel.Rect.Height);
                    return true;
                }
            };

            var previouslyUsedButton = new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "RecentlyUsedButton")
            {
                ToolTip = TextManager.Get("PreviouslyUsedLabel"),
                OnClicked = (btn, userData) =>
                {
                    showEntitiesPanel.Visible = false;
                    undoBufferPanel.Visible = false;
                    previouslyUsedPanel.Visible = !previouslyUsedPanel.Visible;
                    previouslyUsedPanel.RectTransform.AbsoluteOffset = new Point(Math.Max(Math.Max(btn.Rect.X, entityCountPanel.Rect.Right), saveAssemblyFrame.Rect.Right), TopPanel.Rect.Height);
                    return true;
                }
            };

            var undoBufferButton = new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "UndoHistoryButton")
            {
                ToolTip = TextManager.Get("Editor.UndoHistoryButton"),
                OnClicked = (btn, userData) =>
                {
                    showEntitiesPanel.Visible = false;
                    previouslyUsedPanel.Visible = false;
                    undoBufferPanel.Visible = !undoBufferPanel.Visible;
                    undoBufferPanel.RectTransform.AbsoluteOffset = new Point(Math.Max(Math.Max(btn.Rect.X, entityCountPanel.Rect.Right), saveAssemblyFrame.Rect.Right), TopPanel.Rect.Height);
                    return true;
                }
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), paddedTopPanel.RectTransform), style: "VerticalLine");

            subNameLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.9f), paddedTopPanel.RectTransform, Anchor.CenterLeft),
                TextManager.Get("unspecifiedsubfilename"), font: GUI.LargeFont, textAlignment: Alignment.CenterLeft);

            linkedSubBox = new GUIDropDown(new RectTransform(new Vector2(0.15f, 0.9f), paddedTopPanel.RectTransform),
                TextManager.Get("AddSubButton"), elementCount: 20)
            {
                ToolTip = TextManager.Get("AddSubToolTip")
            };
            foreach (SubmarineInfo sub in SubmarineInfo.SavedSubmarines)
            {
                if (sub.Type != SubmarineType.Player) { continue; }
                linkedSubBox.AddItem(sub.Name, sub);
            }
            linkedSubBox.OnSelected += SelectLinkedSub;
            linkedSubBox.OnDropped += (component, obj) =>
            {
                MapEntity.SelectedList.Clear();
                return true;
            };

            var spacing = new GUIFrame(new RectTransform(new Vector2(0.02f, 1.0f), paddedTopPanel.RectTransform), style: null);
            new GUIFrame(new RectTransform(new Vector2(0.1f, 0.9f), spacing.RectTransform, Anchor.Center), style: "VerticalLine");

            defaultModeTickBox = new GUITickBox(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "EditSubButton")
            {
                ToolTip = TextManager.Get("SubEditorEditingMode")　+ "‖color:125,125,125‖\nCtrl + 1‖color:end‖",
                OnSelected = tBox =>
                {
                    if (!lockMode)
                    {
                        if (tBox.Selected) { SetMode(Mode.Default); }

                        return true;
                    }

                    return false;
                }
            };

            wiringModeTickBox = new GUITickBox(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "WiringModeButton")
            {
                ToolTip = TextManager.Get("WiringModeButton") + '\n' + TextManager.Get("WiringModeToolTip") + "‖color:125,125,125‖\nCtrl + 2‖color:end‖",
                OnSelected = tBox =>
                {
                    if (!lockMode)
                    {
                        SetMode(tBox.Selected ? Mode.Wiring : Mode.Default);
                        return true;
                    }

                    return false;
                }
            };

            spacing = new GUIFrame(new RectTransform(new Vector2(0.02f, 1.0f), paddedTopPanel.RectTransform), style: null);
            new GUIFrame(new RectTransform(new Vector2(0.1f, 0.9f), spacing.RectTransform, Anchor.Center), style: "VerticalLine");

            new GUIButton(new RectTransform(new Vector2(0.9f, 0.9f), paddedTopPanel.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "GenerateWaypointsButton")
            {
                ToolTip = TextManager.Get("GenerateWaypointsButton") + '\n' + TextManager.Get("GenerateWaypointsToolTip"),
                OnClicked = (btn, userdata) =>
                {
                    if (WayPoint.WayPointList.Any())
                    {
                        var generateWaypointsVerification = new GUIMessageBox("", TextManager.Get("generatewaypointsverification"), new[] { TextManager.Get("ok"), TextManager.Get("cancel") });
                        generateWaypointsVerification.Buttons[0].OnClicked = delegate
                        {
                            if (GenerateWaypoints())
                            {
                                GUI.AddMessage(TextManager.Get("waypointsgeneratedsuccesfully"), GUI.Style.Green);
                            }
                            WayPoint.ShowWayPoints = true;
                            generateWaypointsVerification.Close();
                            return true;
                        };
                        generateWaypointsVerification.Buttons[1].OnClicked = generateWaypointsVerification.Close;
                    }
                    else
                    {
                        if (GenerateWaypoints())
                        {
                            GUI.AddMessage(TextManager.Get("waypointsgeneratedsuccesfully"), GUI.Style.Green);
                        }
                        WayPoint.ShowWayPoints = true;

                    }
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

            previouslyUsedPanel = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.2f), GUI.Canvas) { MinSize = new Point(200, 200) })
            {
                Visible = false
            };
            previouslyUsedList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.9f), previouslyUsedPanel.RectTransform, Anchor.Center))
            {
                ScrollBarVisible = true,
                OnSelected = SelectPrefab
            };

            //-----------------------------------------------

            undoBufferPanel = new GUIFrame(new RectTransform(new Vector2(0.15f, 0.2f), GUI.Canvas) { MinSize = new Point(200, 200) })
            {
                Visible = false
            };
            undoBufferList = new GUIListBox(new RectTransform(new Vector2(0.925f, 0.9f), undoBufferPanel.RectTransform, Anchor.Center))
            {
                ScrollBarVisible = true,
                OnSelected = (_, userData) =>
                {
                    int index;
                    if (userData is Command command)
                    {
                        index = Commands.IndexOf(command);
                    }
                    else
                    {
                        index = -1;
                    }

                    int diff = index- commandIndex;
                    int amount = Math.Abs(diff);

                    if (diff >= 0)
                    {
                        Redo(amount + 1);
                    }
                    else
                    {
                        Undo(amount - 1);
                    }
                    
                    return true;
                }
            };
            
            UpdateUndoHistoryPanel();

            //-----------------------------------------------

            showEntitiesPanel = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.5f), GUI.Canvas)
            {
                MinSize = new Point(190, 0)
            }) 
            { 
                Visible = false 
            };

            GUILayoutGroup paddedShowEntitiesPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.98f), showEntitiesPanel.RectTransform, Anchor.Center))
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
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("ShowWires"))
            {
                UserData = "wire",
                Selected = Item.ShowWires,
                OnSelected = (GUITickBox obj) => { Item.ShowWires = obj.Selected; return true; }
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

            var subcategoryHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedShowEntitiesPanel.RectTransform), TextManager.Get("subcategories"), font: GUI.SubHeadingFont);
            subcategoryHeader.RectTransform.MinSize = new Point(0, (int)(subcategoryHeader.Rect.Height * 1.5f));

            var subcategoryList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedShowEntitiesPanel.RectTransform) { MinSize = new Point(0, showEntitiesPanel.Rect.Height / 3) });
            List<string> availableSubcategories = new List<string>();
            foreach (var prefab in MapEntityPrefab.List)
            {
                if (!string.IsNullOrEmpty(prefab.Subcategory) && !availableSubcategories.Contains(prefab.Subcategory)) 
                { 
                    availableSubcategories.Add(prefab.Subcategory); 
                }
            }
            foreach (string subcategory in availableSubcategories)
            {
                var tb = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), subcategoryList.Content.RectTransform), 
                    TextManager.Get("subcategory." + subcategory, returnNull: true) ?? subcategory, font: GUI.SmallFont)
                {
                    UserData = subcategory,
                    Selected = !IsSubcategoryHidden(subcategory),
                    OnSelected = (GUITickBox obj) => { hiddenSubCategories[(string)obj.UserData] = !obj.Selected; return true; },
                };
                if (tb.TextBlock.TextSize.X > tb.TextBlock.Rect.Width * 1.25f)
                {
                    tb.ToolTip = tb.Text;
                    tb.Text = ToolBox.LimitString(tb.Text, tb.Font, (int)(tb.TextBlock.Rect.Width * 1.25f));
                }
            }

            GUITextBlock.AutoScaleAndNormalize(subcategoryList.Content.Children.Where(c => c is GUITickBox).Select(c => ((GUITickBox)c).TextBlock));

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
            
            var lightCountLabel = new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.0f), paddedEntityCountPanel.RectTransform), TextManager.Get("SubEditorLights"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SmallFont);
            var lightCountText = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1.0f), lightCountLabel.RectTransform, Anchor.TopRight, Pivot.TopLeft), "", textAlignment: Alignment.CenterRight);
            lightCountText.TextGetter = () =>
            {
                int lightCount = 0;
                foreach (Item item in Item.ItemList)
                {
                    if (item.ParentInventory != null) { continue; }
                    lightCount += item.GetComponents<LightComponent>().Count();
                }
                lightCountText.TextColor = ToolBox.GradientLerp(lightCount / 250.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
                return lightCount.ToString();
            };
            var shadowCastingLightCountLabel = new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.0f), paddedEntityCountPanel.RectTransform), TextManager.Get("SubEditorShadowCastingLights"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SmallFont, wrap: true);
            var shadowCastingLightCountText = new GUITextBlock(new RectTransform(new Vector2(0.33f, 1.0f), shadowCastingLightCountLabel.RectTransform, Anchor.TopRight, Pivot.TopLeft), "", textAlignment: Alignment.CenterRight);
            shadowCastingLightCountText.TextGetter = () =>
            {
                int lightCount = 0;
                foreach (Item item in Item.ItemList)
                {
                    if (item.ParentInventory != null) { continue; }
                    lightCount += item.GetComponents<LightComponent>().Count(l => l.CastShadows);
                }
                shadowCastingLightCountText.TextColor = ToolBox.GradientLerp(lightCount / 60.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
                return lightCount.ToString();
            };
            entityCountPanel.RectTransform.NonScaledSize =
                new Point(
                    (int)(paddedEntityCountPanel.RectTransform.Children.Max(c => (int)((GUITextBlock) c.GUIComponent).TextSize.X / 0.75f) / paddedEntityCountPanel.RectTransform.RelativeSize.X),
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
            saveAssemblyButton.TextBlock.AutoScaleHorizontal = true;
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
                ToolTip = TextManager.Get("EntityMenuToggleTooltip") + "‖color:125,125,125‖\nQ‖color:end‖",
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
                RelativeSpacing = 0.04f,
                Stretch = true
            };

            var entityMenuTop = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.13f), paddedTab.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            selectedCategoryButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), entityMenuTop.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "CategoryButton.All")
            {
                CanBeFocused = false
            };
            selectedCategoryText = new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), entityMenuTop.RectTransform), TextManager.Get("MapEntityCategory.All"), font: GUI.LargeFont);

            var filterText = new GUITextBlock(new RectTransform(new Vector2(0.1f, 1.0f), entityMenuTop.RectTransform), TextManager.Get("serverlog.filter"), font: GUI.SubHeadingFont);
            filterText.RectTransform.MaxSize = new Point((int)(filterText.TextSize.X * 1.5f), int.MaxValue);
            entityFilterBox = new GUITextBox(new RectTransform(new Vector2(0.17f, 1.0f), entityMenuTop.RectTransform), font: GUI.Font, createClearButton: true);
            entityFilterBox.OnTextChanged += (textBox, text) =>
            {
                if (text == lastFilter) { return true; }
                lastFilter = text;
                FilterEntities(text); 
                return true;
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(0.075f, 1.0f), entityMenuTop.RectTransform), style: null);

            entityCategoryButtons.Clear();
            entityCategoryButtons.Add(
                new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), entityMenuTop.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "CategoryButton.All")
                {
                    OnClicked = (btn, userdata) =>
                    {
                        OpenEntityMenu(null);
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
                        MapEntityCategory newCategory = (MapEntityCategory)userdata;
                        OpenEntityMenu(newCategory);
                        return true;
                    }
                });
            }
            entityCategoryButtons.ForEach(b => b.RectTransform.MaxSize = new Point(b.Rect.Height));

            new GUIFrame(new RectTransform(new Vector2(0.8f, 0.01f), paddedTab.RectTransform), style: "HorizontalLine");

            var entityListContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), paddedTab.RectTransform), style: null);
            categorizedEntityList = new GUIListBox(new RectTransform(Vector2.One, entityListContainer.RectTransform), useMouseDownToSelect: true);
            allEntityList = new GUIListBox(new RectTransform(Vector2.One, entityListContainer.RectTransform), useMouseDownToSelect: true)
            {
                OnSelected = SelectPrefab,
                UseGridLayout = true,
                CheckSelected = MapEntityPrefab.GetSelected,
                Visible = false
            };

            paddedTab.Recalculate();

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private bool TestSubmarine(GUIButton button, object obj)
        {
            List<string> errorMsgs = new List<string>();

            if (!Hull.hullList.Any())
            {
                errorMsgs.Add(TextManager.Get("NoHullsWarning"));
            }

            if (!WayPoint.WayPointList.Any(wp => wp.ShouldBeSaved && wp.SpawnType == SpawnType.Human))
            {
                errorMsgs.Add(TextManager.Get("NoHumanSpawnpointWarning"));
            }

            if (errorMsgs.Any())
            {
                new GUIMessageBox(TextManager.Get("Error"), string.Join("\n\n", errorMsgs), new Vector2(0.25f, 0.0f), new Point(400, 200));
                return true;
            }

            backedUpSubInfo = new SubmarineInfo(Submarine.MainSub);

            GameMain.GameScreen.Select();

            GameSession gameSession = new GameSession(backedUpSubInfo, "", GameModePreset.TestMode, CampaignSettings.Empty, null);
            gameSession.StartRound(null, false);
            (gameSession.GameMode as TestGameMode).OnRoundEnd = () =>
            {
                Submarine.Unload();
                GameMain.SubEditorScreen.Select();
            };

            return true;
        }

        public void ClearBackedUpSubInfo()
        {
            backedUpSubInfo = null;
        }

        private void UpdateEntityList()
        {
            categorizedEntityList.Content.ClearChildren();
            allEntityList.Content.ClearChildren();

            int maxTextWidth = (int)(GUI.SubHeadingFont.MeasureString(TextManager.Get("mapentitycategory.misc")).X + GUI.IntScale(50));
            Dictionary<string, List<MapEntityPrefab>> entityLists = new Dictionary<string, List<MapEntityPrefab>>();
            Dictionary<string, MapEntityCategory> categoryKeys = new Dictionary<string, MapEntityCategory>();

            foreach (MapEntityCategory category in Enum.GetValues(typeof(MapEntityCategory)))
            {
                string categoryName = TextManager.Get("MapEntityCategory." + category);
                maxTextWidth = (int)Math.Max(maxTextWidth, GUI.SubHeadingFont.MeasureString(categoryName.Replace(' ', '\n')).X + GUI.IntScale(50));
                foreach (MapEntityPrefab ep in MapEntityPrefab.List)
                {
                    if (!ep.Category.HasFlag(category)) { continue; }

                    if (!entityLists.ContainsKey(category + ep.Subcategory))
                    {
                        entityLists[category + ep.Subcategory] = new List<MapEntityPrefab>();
                    }
                    entityLists[category + ep.Subcategory].Add(ep);
                    categoryKeys[category + ep.Subcategory] = category;
                    string subcategoryName = TextManager.Get("subcategory." + ep.Subcategory, returnNull: true) ?? ep.Subcategory;
                    if (subcategoryName != null)
                    {
                        maxTextWidth = (int)Math.Max(maxTextWidth, GUI.SubHeadingFont.MeasureString(subcategoryName.Replace(' ', '\n')).X + GUI.IntScale(50));
                    }
                }
            }

            categorizedEntityList.Content.ClampMouseRectToParent = true;
            int entitiesPerRow = (int)Math.Ceiling(categorizedEntityList.Content.Rect.Width / Math.Max(125 * GUI.Scale, 60));
            foreach (string categoryKey in entityLists.Keys)
            {
                var categoryFrame = new GUIFrame(new RectTransform(Vector2.One, categorizedEntityList.Content.RectTransform), style: null)
                {
                    ClampMouseRectToParent = true,
                    UserData = categoryKeys[categoryKey]
                };

                new GUIFrame(new RectTransform(Vector2.One, categoryFrame.RectTransform), style: "HorizontalLine");

                string categoryName = TextManager.Get("MapEntityCategory." + entityLists[categoryKey].First().Category);
                string subCategoryName = entityLists[categoryKey].First().Subcategory;
                if (string.IsNullOrEmpty(subCategoryName))
                {
                    new GUITextBlock(new RectTransform(new Point(maxTextWidth, categoryFrame.Rect.Height), categoryFrame.RectTransform, Anchor.TopLeft),
                        categoryName, textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont, wrap: true)
                    {
                        Padding = new Vector4(GUI.IntScale(10))
                    };

                }
                else
                {
                    subCategoryName = string.IsNullOrEmpty(subCategoryName) ?
                        TextManager.Get("mapentitycategory.misc") :
                        (TextManager.Get("subcategory." + subCategoryName, returnNull: true) ?? subCategoryName);
                    var categoryTitle = new GUITextBlock(new RectTransform(new Point(maxTextWidth, categoryFrame.Rect.Height), categoryFrame.RectTransform, Anchor.TopLeft),
                        categoryName, textAlignment: Alignment.TopLeft, font: GUI.Font, wrap: true)
                    {
                        Padding = new Vector4(GUI.IntScale(10))
                    };
                    new GUITextBlock(new RectTransform(new Point(maxTextWidth, categoryFrame.Rect.Height), categoryFrame.RectTransform, Anchor.TopLeft) { AbsoluteOffset = new Point(0, (int)(categoryTitle.TextSize.Y + GUI.IntScale(10))) },
                        subCategoryName, textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont, wrap: true)
                    {
                        Padding = new Vector4(GUI.IntScale(10))
                    };
                }

                var entityListInner = new GUIListBox(new RectTransform(new Point(categoryFrame.Rect.Width - maxTextWidth, categoryFrame.Rect.Height), categoryFrame.RectTransform, Anchor.CenterRight),
                    style: null,
                    useMouseDownToSelect: true)
                {
                    ScrollBarVisible = false,
                    AutoHideScrollBar = false,
                    OnSelected = SelectPrefab,
                    UseGridLayout = true,
                    CheckSelected = MapEntityPrefab.GetSelected,
                    ClampMouseRectToParent = true
                };
                entityListInner.ContentBackground.ClampMouseRectToParent = true;
                entityListInner.Content.ClampMouseRectToParent = true;

                foreach (MapEntityPrefab ep in entityLists[categoryKey])
                {
#if !DEBUG
                    if (ep.HideInMenus) { continue; }
#endif
                    CreateEntityElement(ep, entitiesPerRow, entityListInner.Content);                   
                }

                entityListInner.UpdateScrollBarSize();
                int contentHeight = (int)(entityListInner.TotalSize + entityListInner.Padding.Y + entityListInner.Padding.W);
                categoryFrame.RectTransform.NonScaledSize = new Point(categoryFrame.Rect.Width, contentHeight);
                categoryFrame.RectTransform.MinSize = new Point(0, contentHeight);
                entityListInner.RectTransform.NonScaledSize = new Point(entityListInner.Rect.Width, contentHeight);
                entityListInner.RectTransform.MinSize = new Point(0, contentHeight);

                entityListInner.Content.RectTransform.SortChildren((i1, i2) => 
                    string.Compare(((MapEntityPrefab)i1.GUIComponent.UserData). Name, (i2.GUIComponent.UserData as MapEntityPrefab)?.Name, StringComparison.Ordinal));
            }

            foreach (MapEntityPrefab ep in MapEntityPrefab.List)
            {
#if !DEBUG
                if (ep.HideInMenus) { continue; }
#endif
                CreateEntityElement(ep, entitiesPerRow, allEntityList.Content);
            }
        }

        private void CreateEntityElement(MapEntityPrefab ep, int entitiesPerRow, GUIComponent parent)
        {
            bool legacy = ep.Category.HasFlag(MapEntityCategory.Legacy);

            float relWidth = 1.0f / entitiesPerRow;
            GUIFrame frame = new GUIFrame(new RectTransform(
                new Vector2(relWidth, relWidth * ((float)parent.Rect.Width / parent.Rect.Height)),
                parent.RectTransform)
                { MinSize = new Point(0, 50) },
                style: "GUITextBox")
            {
                UserData = ep,
                ClampMouseRectToParent = true
            };
            frame.RectTransform.MinSize = new Point(0, frame.Rect.Width);
            frame.RectTransform.MaxSize = new Point(int.MaxValue, frame.Rect.Width);

            string name = legacy ? TextManager.GetWithVariable("legacyitemformat", "[name]", ep.Name) : ep.Name;
            frame.ToolTip = string.IsNullOrEmpty(ep.Description) ? name : name + '\n' + ep.Description;

            if (ep.HideInMenus)
            {
                frame.Color = Color.Red;
                name = "[HIDDEN] " + name;
            }

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
                    LoadAsynchronously = true,
                    Color = legacy ? iconColor * 0.6f : iconColor
                };
            }

            if (ep is ItemAssemblyPrefab itemAssemblyPrefab)
            {
                new GUICustomComponent(new RectTransform(new Vector2(1.0f, 0.75f),
                    paddedFrame.RectTransform, Anchor.TopCenter), onDraw: (sb, customComponent) =>
                    {
                        if (GUIImage.LoadingTextures) { return; }
                        itemAssemblyPrefab.DrawIcon(sb, customComponent);
                    })
                {
                    HideElementsOutsideFrame = true,
                    ToolTip = frame.RawToolTip
                };
            }

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform, Anchor.BottomCenter),
                text: name, textAlignment: Alignment.Center, font: GUI.SmallFont)
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
                        ItemAssemblyPrefab assemblyPrefab = (ItemAssemblyPrefab)userData;
                        if (assemblyPrefab != null)
                        {
                            var msgBox = new GUIMessageBox(
                                TextManager.Get("DeleteDialogLabel"),
                                TextManager.GetWithVariable("DeleteDialogQuestion", "[file]", assemblyPrefab.Name),
                                new[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
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
                        }

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

        public override void Select()
        {
            Select(enableAutoSave: true);
        }

        public void Select(bool enableAutoSave = true)
        {
            base.Select();

            GUI.PreventPauseMenuToggle = false;
            if (!Directory.Exists(autoSavePath))
            {
                System.IO.DirectoryInfo e = Directory.CreateDirectory(autoSavePath);
                e.Attributes = System.IO.FileAttributes.Directory | System.IO.FileAttributes.Hidden;
                if (!e.Exists)
                {
                    DebugConsole.ThrowError("Failed to create auto save directory!");
                }
            }

            if (!File.Exists(autoSaveInfoPath))
            {
                try
                {
                    AutoSaveInfo = new XDocument(new XElement("AutoSaves"));
                    IO.SafeXML.SaveSafe(AutoSaveInfo, autoSaveInfoPath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Saving auto save info to \"" + autoSaveInfoPath + "\" failed!", e);
                }
            }
            else
            {
                AutoSaveInfo = XMLExtensions.TryLoadXml(autoSaveInfoPath);
            }
            
            GameMain.LightManager.AmbientLight = 
                Level.Loaded?.GenerationParams?.AmbientLightColor ??
                new Color(3, 3, 3, 3);

            UpdateEntityList();

            isAutoSaving = false;
            if (!wasSelectedBefore)
            {
                OpenEntityMenu(null);
                wasSelectedBefore = true;
            }

            if (backedUpSubInfo != null)
            {
                Submarine.Unload();
            }

            string name = (Submarine.MainSub == null) ? TextManager.Get("unspecifiedsubfilename") : Submarine.MainSub.Info.Name;
            if (backedUpSubInfo != null) { name = backedUpSubInfo.Name; }
            subNameLabel.Text = ToolBox.LimitString(name, subNameLabel.Font, subNameLabel.Rect.Width);

            editorSelectedTime = DateTime.Now;

            GUI.ForceMouseOn(null);
            SetMode(Mode.Default);

            if (backedUpSubInfo != null)
            {
                Submarine.MainSub = new Submarine(backedUpSubInfo);
                if (previewImage != null && backedUpSubInfo.PreviewImage?.Texture != null && !backedUpSubInfo.PreviewImage.Texture.IsDisposed)
                {
                    previewImage.Sprite = backedUpSubInfo.PreviewImage;
                }
                backedUpSubInfo = null;
            }
            else if (Submarine.MainSub == null)
            {
                var subInfo = new SubmarineInfo();
                Submarine.MainSub = new Submarine(subInfo);
            }

            Submarine.MainSub.UpdateTransform(interpolate: false);
            cam.Position = Submarine.MainSub.Position + Submarine.MainSub.HiddenSubPosition;

            GameMain.SoundManager.SetCategoryGainMultiplier("default", 0.0f);
            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", 0.0f);

            string downloadFolder = Path.GetFullPath(SaveUtil.SubmarineDownloadFolder);
            linkedSubBox.ClearChildren();
            foreach (SubmarineInfo sub in SubmarineInfo.SavedSubmarines)
            {
                if (sub.Type != SubmarineType.Player) { continue; }
                if (Path.GetDirectoryName(Path.GetFullPath(sub.FilePath)) == downloadFolder) { continue; }
                linkedSubBox.AddItem(sub.Name, sub);
            }

            cam.UpdateTransform();

            CreateDummyCharacter();

            if (GameSettings.EnableSubmarineAutoSave && enableAutoSave)
            {
                CoroutineManager.StartCoroutine(AutoSaveCoroutine(), "SubEditorAutoSave");
            }
            
            ImageManager.OnEditorSelected();
            
            GameAnalyticsManager.SetCustomDimension01("editor");
            if (!GameMain.Config.EditorDisclaimerShown)
            {
                GameMain.Instance.ShowEditorDisclaimer();
            }
        }

        /// <summary>
        /// Coroutine that waits 5 minutes and then runs itself recursively again to save the submarine into a temporary file
        /// </summary>
        /// <see cref="AutoSave"/>
        /// <returns></returns>
        private static IEnumerable<object> AutoSaveCoroutine()
        {
            DateTime target = DateTime.Now.AddMinutes(GameSettings.AutoSaveIntervalSeconds);
            DateTime tempTarget = DateTime.Now;

            bool wasPaused = false;

            while (DateTime.Now < target && Selected is SubEditorScreen || GameMain.Instance.Paused || wasPaused)
            {
                if (GameMain.Instance.Paused && !wasPaused)
                {
                    AutoSave();
                    tempTarget = DateTime.Now;
                    wasPaused = true;
                }
                
                if (!GameMain.Instance.Paused && wasPaused)
                {
                    wasPaused = false; 
                    target = target.AddSeconds((DateTime.Now - tempTarget).TotalSeconds);
                }
                yield return CoroutineStatus.Running;
            }

            if (Selected is SubEditorScreen)
            {
                AutoSave();
                CoroutineManager.StartCoroutine(AutoSaveCoroutine(), "SubEditorAutoSave");
            }
            yield return CoroutineStatus.Success;
        }
        
        public override void Deselect()
        {
            base.Deselect();

            autoSaveLabel?.Parent?.RemoveChild(autoSaveLabel);
            autoSaveLabel = null;

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
            ClearUndoBuffer();

            SetMode(Mode.Default);

            SoundPlayer.OverrideMusicType = null;
            GameMain.SoundManager.SetCategoryGainMultiplier("default", GameMain.Config.SoundVolume);
            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", GameMain.Config.SoundVolume);

            if (CoroutineManager.IsCoroutineRunning("SubEditorAutoSave"))
            {
                CoroutineManager.StopCoroutines("SubEditorAutoSave");
            }

            if (dummyCharacter != null)
            {
                dummyCharacter.Remove();
                dummyCharacter = null;
                GameMain.World.ProcessChanges();
            }
            
            GUIMessageBox.MessageBoxes.ForEachMod(component =>
            {
                if (component is GUIMessageBox { Closed: false, UserData: "colorpicker" } msgBox)
                {
                    foreach (GUIColorPicker colorPicker in msgBox.GetAllChildren<GUIColorPicker>())
                    {
                        colorPicker.DisposeTextures();
                    }

                    msgBox.Close();
                }
            });
            
            ClearFilter();
        }

        private void CreateDummyCharacter()
        {
            if (dummyCharacter != null) RemoveDummyCharacter();

            dummyCharacter = Character.Create(CharacterPrefab.HumanSpeciesName, Vector2.Zero, "", id: Entity.DummyID, hasAi: false);
            dummyCharacter.Info.Name = "Galldren";

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

        /// <summary>
        /// Saves the current main sub into a temporary file outside of the Submarines/ folder
        /// </summary>
        /// <see cref="LoadAutoSave"/>
        /// <remarks>The saving is ran in another thread to avoid lag spikes</remarks>
        private static void AutoSave()
        {
            if (MapEntity.mapEntityList.Any() && GameSettings.EnableSubmarineAutoSave && !isAutoSaving)
            {
                if (Submarine.MainSub != null)
                {
                    isAutoSaving = true;
                    if (!Directory.Exists(autoSavePath)) { return; }

                    XDocument doc = new XDocument(new XElement("Submarine"));
                    Submarine.MainSub.SaveToXElement(doc.Root);
                    Thread saveThread = new Thread(start =>
                    {
                        try
                        {
                            Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                            TimeSpan time = DateTime.UtcNow - DateTime.MinValue;
                            string filePath = Path.Combine(autoSavePath, $"AutoSave_{(ulong)time.TotalMilliseconds}.sub");
                            SaveUtil.CompressStringToFile(filePath, doc.ToString());

                            CrossThread.RequestExecutionOnMainThread(() =>
                            {
                                if (AutoSaveInfo?.Root == null || Submarine.MainSub?.Info == null) { return; }

                                int saveCount = AutoSaveInfo.Root.Elements().Count();
                                while (AutoSaveInfo.Root.Elements().Count() > maxAutoSaves)
                                {
                                    XElement min = AutoSaveInfo.Root.Elements().OrderBy(element => element.GetAttributeUInt64("time", 0)).FirstOrDefault();
                                    string path = min.GetAttributeString("file", "");
                                    if (string.IsNullOrWhiteSpace(path)) { continue; }

                                    if (IO.File.Exists(path)) { IO.File.Delete(path); }
                                    min?.Remove();
                                }

                                XElement newElement = new XElement("AutoSave", 
                                    new XAttribute("file", filePath), 
                                    new XAttribute("name", Submarine.MainSub.Info.Name),
                                    new XAttribute("time", (ulong)time.TotalSeconds));
                                AutoSaveInfo.Root.Add(newElement);

                                try
                                {
                                    IO.SafeXML.SaveSafe(AutoSaveInfo, autoSaveInfoPath);
                                }
                                catch (Exception e)
                                {
                                    DebugConsole.ThrowError("Saving auto save info to \"" + autoSaveInfoPath + "\" failed!", e);
                                }
                            });
                            
                            Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
                            CrossThread.RequestExecutionOnMainThread(DisplayAutoSavePrompt);
                        }
                        catch (Exception e)
                        {
                            CrossThread.RequestExecutionOnMainThread(() => DebugConsole.ThrowError("Auto saving submarine failed!", e));
                        }
                        isAutoSaving = false;
                    }) { Name = "Auto Save Thread" };
                    saveThread.Start();
                }
            }
        }

        private static void DisplayAutoSavePrompt()
        {
            if (Selected != GameMain.SubEditorScreen) { return; }
            autoSaveLabel?.Parent?.RemoveChild(autoSaveLabel);

            string label = TextManager.Get("AutoSaved");
            autoSaveLabel = new GUILayoutGroup(new RectTransform(new Point(GUI.IntScale(150), GUI.IntScale(32)), GameMain.SubEditorScreen.EntityMenu.RectTransform, Anchor.TopRight)
            {
                ScreenSpaceOffset = new Point(-GUI.IntScale(16), -GUI.IntScale(48))
            }, isHorizontal: true)
            {
                CanBeFocused = false
            };

            GUIImage checkmark = new GUIImage(new RectTransform(new Vector2(0.25f, 1f), autoSaveLabel.RectTransform), style: "MissionCompletedIcon", scaleToFit: true);
            GUITextBlock labelComponent = new GUITextBlock(new RectTransform(new Vector2(0.75f, 1f), autoSaveLabel.RectTransform), label, font: GUI.SubHeadingFont, color: GUI.Style.Green)
            {
                Padding = Vector4.Zero,
                AutoScaleHorizontal = true,
                AutoScaleVertical = true
            };

            labelComponent.FadeOut(0.5f, true, 1f);
            checkmark.FadeOut(0.5f, true, 1f);
            autoSaveLabel?.FadeOut(0.5f, true, 1f);
        }

        private bool SaveSub(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                GUI.AddMessage(TextManager.Get("SubNameMissingWarning"), GUI.Style.Red);
                nameBox.Flash();
                return false;
            }

            string specialSavePath = "";
            if (Submarine.MainSub.Info.Type != SubmarineType.Player)
            {
                ContentType contentType = ContentType.Submarine;
                switch (Submarine.MainSub.Info.Type)
                {
                    case SubmarineType.OutpostModule:
                        if (Submarine.MainSub.Info?.OutpostModuleInfo != null)
                        {
                            contentType = ContentType.OutpostModule;
                            Submarine.MainSub.Info.PreviewImage = null;
                        }
                        break;
                    case SubmarineType.Outpost:
                        contentType = ContentType.Outpost;
                        break;
                    case SubmarineType.Wreck:
                        contentType = ContentType.Wreck;
                        break;
                }
                if (contentType != ContentType.Submarine)
                {
#if DEBUG
                    var existingFiles = ContentPackage.GetFilesOfType(GameMain.VanillaContent.ToEnumerable(), contentType);
#else
                    var existingFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages.Where(c => c != GameMain.VanillaContent), contentType);
#endif
                    specialSavePath = existingFiles.FirstOrDefault(f => 
                        Path.GetFullPath(f.Path) != Path.GetFullPath(SubmarineInfo.SavePath) && ContentPackage.IsModFilePathAllowed(f.Path))?.Path;
                    if (!string.IsNullOrEmpty(specialSavePath))
                    {
                        specialSavePath = Path.GetDirectoryName(specialSavePath);
                    }
                }
            }
            else if (Submarine.MainSub.Info.SubmarineClass == SubmarineClass.Undefined && !Submarine.MainSub.Info.HasTag(SubmarineTag.Shuttle))
            {
                var msgBox = new GUIMessageBox(TextManager.Get("warning"), TextManager.Get("undefinedsubmarineclasswarning"), new string[] { TextManager.Get("yes"), TextManager.Get("no") });

                msgBox.Buttons[0].OnClicked = (bt, userdata) =>
                {
                    SaveSubToFile(nameBox.Text);
                    saveFrame = null;
                    msgBox.Close();
                    return true;
                };
                msgBox.Buttons[1].OnClicked = (bt, userdata) =>
                {
                    msgBox.Close();
                    return true;
                };
                return true;
            }

            if (!string.IsNullOrEmpty(specialSavePath) &&
                (string.IsNullOrEmpty(Submarine.MainSub?.Info.FilePath) || Path.GetFileNameWithoutExtension(Submarine.MainSub.Info.Name) != nameBox.Text || Path.GetDirectoryName(Submarine.MainSub?.Info.FilePath) != specialSavePath))
            {
                var msgBox = new GUIMessageBox("", TextManager.GetWithVariables("savesubtospecialfolderprompt",
                    new string[] { "[type]", "[outpostpath]" }, new string[] { TextManager.Get("submarinetype." + Submarine.MainSub.Info.Type), specialSavePath }),
                    new string[] { TextManager.Get("yes"), TextManager.Get("no") });
                msgBox.Buttons[0].OnClicked = (bt, userdata) =>
                {
                    SaveSubToFile(nameBox.Text, specialSavePath);
                    saveFrame = null;
                    msgBox.Close();
                    return true;
                }; 
                msgBox.Buttons[1].OnClicked = (bt, userdata) =>
                {
                    SaveSubToFile(nameBox.Text);
                    saveFrame = null;
                    msgBox.Close();
                    return true;
                };
                return true;
            }

            var result = SaveSubToFile(nameBox.Text, specialSavePath);
            saveFrame = null;
            return result;
        }

        private bool SaveSubToFile(string name, string specialSavePath = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                GUI.AddMessage(TextManager.Get("SubNameMissingWarning"), GUI.Style.Red);
                return false;
            }
            
            foreach (var illegalChar in Path.GetInvalidFileNameChars())
            {
                if (!name.Contains(illegalChar)) continue;
                GUI.AddMessage(TextManager.GetWithVariable("SubNameIllegalCharsWarning", "[illegalchar]", illegalChar.ToString()), GUI.Style.Red);
                return false;
            }

            string savePath = name + ".sub";
            string prevSavePath = null;
            string directoryName = Submarine.MainSub?.Info?.FilePath == null ? 
                SubmarineInfo.SavePath : Path.GetDirectoryName(Submarine.MainSub.Info.FilePath);
            if (!string.IsNullOrEmpty(specialSavePath))
            {
                directoryName = specialSavePath;
                savePath = Path.Combine(directoryName, savePath);
                ContentPackage contentPackage = GameMain.Config.AllEnabledPackages.FirstOrDefault(cp => cp.Files.Any(f => Path.GetDirectoryName(f.Path) == directoryName));

                bool allowSavingToVanilla = false;
#if DEBUG
                allowSavingToVanilla = true;
#endif
                if (!contentPackage.Files.Any(f => Path.GetFullPath(f.Path) == Path.GetFullPath(savePath)) && (allowSavingToVanilla || contentPackage != GameMain.VanillaContent))
                {
                    var msgBox = new GUIMessageBox("", TextManager.GetWithVariable("addtocontentpackageprompt", "[packagename]", contentPackage.Name),
                        new string[] { TextManager.Get("yes"), TextManager.Get("no") });
                    msgBox.Buttons[0].OnClicked = (bt, userdata) =>
                    {
                        contentPackage.AddFile(savePath, ContentType.OutpostModule);
                        Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                        contentPackage.Save(contentPackage.Path, reload: false);
                        Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
                        msgBox.Close();
                        return true;
                    };
                    msgBox.Buttons[1].OnClicked = (bt, userdata) =>
                    {
                        msgBox.Close();
                        return true;
                    };
                }
            }
            else if (!string.IsNullOrEmpty(Submarine.MainSub?.Info.FilePath) &&
                Submarine.MainSub.Info.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
            {
                prevSavePath = Submarine.MainSub.Info.FilePath.CleanUpPath();
                string prevDir = Path.GetDirectoryName(Submarine.MainSub.Info.FilePath).CleanUpPath();
                string[] subDirs = prevDir.Split('/');
                bool forceToSubFolder = Steam.SteamManager.IsInitialized;
                bool isInSubFolder = subDirs.Length > 0 && subDirs[0].Equals("Submarines", StringComparison.InvariantCultureIgnoreCase);
                if (forceToSubFolder && subDirs.Length > 1 && subDirs[0].Equals("Mods", StringComparison.InvariantCultureIgnoreCase))
                {
                    string modName = subDirs[1];
                    ContentPackage contentPackage = ContentPackage.AllPackages.FirstOrDefault(p => p.Name.Equals(modName, StringComparison.InvariantCultureIgnoreCase));
                    if (contentPackage != null)
                    {
                        Steamworks.Data.PublishedFileId packageId = contentPackage.SteamWorkshopId;

                        Task<Steamworks.Ugc.Item?> itemInfoTask = Steamworks.Ugc.Item.GetAsync(packageId);
                        Task<Steamworks.Ugc.Item?> itemUpdateTask = Task.Run(async () =>
                        {
                            while (!itemInfoTask.IsCompleted)
                            {
                                Steamworks.SteamClient.RunCallbacks();
                                await Task.Delay(16);
                            }
                            return itemInfoTask.Result;
                        });

                        Steamworks.Ugc.Item? item = itemUpdateTask.Result;
                        if (item?.Owner.Id == Steam.SteamManager.GetSteamID())
                        {
                            forceToSubFolder = false;
                            string targetPath = Path.Combine(prevDir, savePath).CleanUpPath();
                            if (!contentPackage.Files.Any(f => f.Type == ContentType.Submarine &&
                                f.Path.CleanUpPath().Equals(targetPath, StringComparison.InvariantCultureIgnoreCase)))
                            {
                                contentPackage.AddFile(new ContentFile(targetPath, ContentType.Submarine));
                            }
                            contentPackage.Save(contentPackage.Path, reload: false);
                        }
                    }
                }
                savePath = Path.Combine(forceToSubFolder && !isInSubFolder ? SubmarineInfo.SavePath : prevDir, savePath).CleanUpPath();
            }
            else
            {
                savePath = Path.Combine(SubmarineInfo.SavePath, savePath);
            }

#if !DEBUG
            var vanilla = GameMain.VanillaContent;
            if (vanilla != null)
            {
                var vanillaSubs = vanilla.GetFilesOfType(ContentType.Submarine);
                string pathToCompare = savePath.Replace(@"\", @"/");
                if (vanillaSubs.Any(sub => sub.Replace(@"\", @"/").Equals(pathToCompare, StringComparison.OrdinalIgnoreCase)))
                {
                    GUI.AddMessage(TextManager.Get("CannotEditVanillaSubs"), GUI.Style.Red, font: GUI.LargeFont);
                    return false;
                }
            }
#endif

            if (Submarine.MainSub != null)
            {
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                if (previewImage?.Sprite?.Texture != null && !previewImage.Sprite.Texture.IsDisposed && Submarine.MainSub.Info.Type != SubmarineType.OutpostModule)
                {
                    bool savePreviewImage = true;
                    using System.IO.MemoryStream imgStream = new System.IO.MemoryStream();
                    try
                    {
                        previewImage.Sprite.Texture.SaveAsPng(imgStream, previewImage.Sprite.Texture.Width, previewImage.Sprite.Texture.Height);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError($"Saving the preview image of the submarine \"{Submarine.MainSub.Info.Name}\" failed.", e);
                        savePreviewImage = false;
                    }
                    Submarine.MainSub.SaveAs(savePath, savePreviewImage ? imgStream : null);
                }
                else
                {
                    Submarine.MainSub.SaveAs(savePath);
                }
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;

                Submarine.MainSub.CheckForErrors();
                
                GUI.AddMessage(TextManager.GetWithVariable("SubSavedNotification", "[filepath]", savePath), GUI.Style.Green);

                SubmarineInfo.RefreshSavedSub(savePath);
                if (prevSavePath != null && prevSavePath != savePath) { SubmarineInfo.RefreshSavedSub(prevSavePath); }

                string downloadFolder = Path.GetFullPath(SaveUtil.SubmarineDownloadFolder);
                linkedSubBox.ClearChildren();
                foreach (SubmarineInfo sub in SubmarineInfo.SavedSubmarines) 
                { 
                    if (sub.Type != SubmarineType.Player) { continue; }
                    if (Path.GetDirectoryName(Path.GetFullPath(sub.FilePath)) == downloadFolder) { continue; }
                    linkedSubBox.AddItem(sub.Name, sub); 
                }
                subNameLabel.Text = ToolBox.LimitString(Submarine.MainSub.Info.Name, subNameLabel.Font, subNameLabel.Rect.Width);
            }

            return false;
        }

        private void CreateSaveScreen(bool quickSave = false)
        {
            if (saveFrame != null) { return; }
            
            if (!quickSave)
            {
                CloseItem();
                SetMode(Mode.Default);
            }

            saveFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null)
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) saveFrame = null; return true; }
            };

            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, saveFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");
            
            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.55f, 0.6f), saveFrame.RectTransform, Anchor.Center) { MinSize = new Point(750, 500) });
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
                OnEnterPressed = ChangeSubName
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

            nameBox.Text = subNameLabel?.Text ?? "";

            submarineNameCharacterCount.Text = nameBox.Text.Length + " / " + submarineNameLimit;

            var descriptionHeaderGroup = new GUILayoutGroup(new RectTransform(new Vector2(.975f, 0.03f), leftColumn.RectTransform), isHorizontal: true);

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

            var subTypeContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.01f), leftColumn.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1f), subTypeContainer.RectTransform), TextManager.Get("submarinetype"));
            var subTypeDropdown = new GUIDropDown(new RectTransform(new Vector2(0.6f, 1f), subTypeContainer.RectTransform));
            subTypeContainer.RectTransform.MinSize = new Point(0, subTypeContainer.RectTransform.Children.Max(c => c.MinSize.Y));
            foreach (SubmarineType subType in Enum.GetValues(typeof(SubmarineType)))
            {
                subTypeDropdown.AddItem(TextManager.Get("submarinetype."+subType.ToString().ToLowerInvariant()), subType);
            }

            //---------------------------------------

            var outpostSettingsContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), leftColumn.RectTransform))
            {
                IgnoreLayoutGroups = true,
                CanBeFocused = true,
                Visible = false,
                Stretch = true
            };
            new GUIFrame(new RectTransform(Vector2.One, outpostSettingsContainer.RectTransform), "InnerFrame")
            {
                IgnoreLayoutGroups = true
            };

            // module flags ---------------------

            var outpostModuleGroup = new GUILayoutGroup(new RectTransform(new Vector2(.975f, 0.1f), outpostSettingsContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), outpostModuleGroup.RectTransform), TextManager.Get("outpostmoduletype"), textAlignment: Alignment.CenterLeft);
            HashSet<string> availableFlags = new HashSet<string>();
            foreach (string flag in OutpostGenerationParams.Params.SelectMany(p => p.ModuleCounts.Select(m => m.Key))) { availableFlags.Add(flag); }
            foreach (var sub in SubmarineInfo.SavedSubmarines)
            {
                if (sub.OutpostModuleInfo == null) { continue; }
                foreach (string flag in sub.OutpostModuleInfo.ModuleFlags) 
                {
                    if (flag == "none") { continue; }
                    availableFlags.Add(flag); 
                }
            }

            var moduleTypeDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), outpostModuleGroup.RectTransform),
                text: string.Join(", ", Submarine.MainSub?.Info?.OutpostModuleInfo?.ModuleFlags.Select(s => TextManager.Capitalize(s)) ?? "None".ToEnumerable()), selectMultiple: true);
            foreach (string flag in availableFlags)
            {
                moduleTypeDropDown.AddItem(TextManager.Capitalize(flag), flag);
                if (Submarine.MainSub?.Info?.OutpostModuleInfo == null) { continue; } 
                if (Submarine.MainSub.Info.OutpostModuleInfo.ModuleFlags.Contains(flag))
                {
                    moduleTypeDropDown.SelectItem(flag);
                }
            }
            moduleTypeDropDown.OnSelected += (_, __) =>
            {
                if (Submarine.MainSub?.Info?.OutpostModuleInfo == null) { return false; }
                Submarine.MainSub.Info.OutpostModuleInfo.SetFlags(moduleTypeDropDown.SelectedDataMultiple.Cast<string>());                
                moduleTypeDropDown.Text = ToolBox.LimitString(
                    Submarine.MainSub.Info.OutpostModuleInfo.ModuleFlags.Any(f => f != "none") ? moduleTypeDropDown.Text : "None", 
                    moduleTypeDropDown.Font, moduleTypeDropDown.Rect.Width);
                return true;
            };
            outpostModuleGroup.RectTransform.MinSize = new Point(0, outpostModuleGroup.RectTransform.Children.Max(c => c.MinSize.Y));

            // module flags ---------------------

            var allowAttachGroup = new GUILayoutGroup(new RectTransform(new Vector2(.975f, 0.1f), outpostSettingsContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), allowAttachGroup.RectTransform), TextManager.Get("outpostmoduleallowattachto"), textAlignment: Alignment.CenterLeft);
            
            var allowAttachDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), allowAttachGroup.RectTransform),
                text: string.Join(", ", Submarine.MainSub?.Info?.OutpostModuleInfo?.AllowAttachToModules.Select(s => TextManager.Capitalize(s)) ?? "Any".ToEnumerable()), selectMultiple: true);
            allowAttachDropDown.AddItem(TextManager.Capitalize("any"), "any");
            if (Submarine.MainSub.Info.OutpostModuleInfo == null || 
                !Submarine.MainSub.Info.OutpostModuleInfo.AllowAttachToModules.Any() ||
                Submarine.MainSub.Info.OutpostModuleInfo.AllowAttachToModules.All(s => s.Equals("any", StringComparison.OrdinalIgnoreCase)))
            {
                allowAttachDropDown.SelectItem("any");
            }
            foreach (string flag in availableFlags)
            {
                if (flag.Equals("any", StringComparison.OrdinalIgnoreCase) || flag.Equals("none", StringComparison.OrdinalIgnoreCase)) { continue; }
                allowAttachDropDown.AddItem(TextManager.Capitalize(flag), flag);
                if (Submarine.MainSub?.Info?.OutpostModuleInfo == null) { continue; }
                if (Submarine.MainSub.Info.OutpostModuleInfo.AllowAttachToModules.Contains(flag))
                {
                    allowAttachDropDown.SelectItem(flag);
                }
            }
            allowAttachDropDown.OnSelected += (_, __) =>
            {
                if (Submarine.MainSub?.Info?.OutpostModuleInfo == null) { return false; }
                Submarine.MainSub.Info.OutpostModuleInfo.SetAllowAttachTo(allowAttachDropDown.SelectedDataMultiple.Cast<string>());
                allowAttachDropDown.Text = ToolBox.LimitString(
                    Submarine.MainSub.Info.OutpostModuleInfo.ModuleFlags.Any(f => f != "none") ? allowAttachDropDown.Text : "None",
                    allowAttachDropDown.Font, allowAttachDropDown.Rect.Width);
                return true;
            };
            allowAttachGroup.RectTransform.MinSize = new Point(0, allowAttachGroup.RectTransform.Children.Max(c => c.MinSize.Y));

            // location types ---------------------

            var locationTypeGroup = new GUILayoutGroup(new RectTransform(new Vector2(.975f, 0.1f), outpostSettingsContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), locationTypeGroup.RectTransform), TextManager.Get("outpostmoduleallowedlocationtypes"), textAlignment: Alignment.CenterLeft);
            HashSet<string> availableLocationTypes = new HashSet<string> { "any" };
            foreach (LocationType locationType in LocationType.List) { availableLocationTypes.Add(locationType.Identifier.ToLowerInvariant()); }

            var locationTypeDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), locationTypeGroup.RectTransform),
                text: string.Join(", ", Submarine.MainSub?.Info?.OutpostModuleInfo?.AllowedLocationTypes.Select(lt => TextManager.Capitalize(lt)) ?? "any".ToEnumerable()), selectMultiple: true);
            foreach (string locationType in availableLocationTypes)
            {
                locationTypeDropDown.AddItem(TextManager.Capitalize(locationType), locationType);
                if (Submarine.MainSub?.Info?.OutpostModuleInfo == null) { continue; }
                if (Submarine.MainSub.Info.OutpostModuleInfo.AllowedLocationTypes.Contains(locationType))
                {
                    locationTypeDropDown.SelectItem(locationType);
                }
            }
            if (!Submarine.MainSub.Info?.OutpostModuleInfo?.AllowedLocationTypes?.Any() ?? true) { locationTypeDropDown.SelectItem("any"); }

            locationTypeDropDown.OnSelected += (_, __) =>
            {
                Submarine.MainSub?.Info?.OutpostModuleInfo?.SetAllowedLocationTypes(locationTypeDropDown.SelectedDataMultiple.Cast<string>());
                locationTypeDropDown.Text = ToolBox.LimitString(locationTypeDropDown.Text, locationTypeDropDown.Font, locationTypeDropDown.Rect.Width);
                return true;
            };
            locationTypeGroup.RectTransform.MinSize = new Point(0, locationTypeGroup.RectTransform.Children.Max(c => c.MinSize.Y));


            // gap positions ---------------------

            var gapPositionGroup = new GUILayoutGroup(new RectTransform(new Vector2(.975f, 0.1f), outpostSettingsContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), gapPositionGroup.RectTransform), TextManager.Get("outpostmodulegappositions"), textAlignment: Alignment.CenterLeft);

            var gapPositionDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), gapPositionGroup.RectTransform),
                text: "", selectMultiple: true);

            Submarine.MainSub.Info?.OutpostModuleInfo?.DetermineGapPositions(Submarine.MainSub);
            foreach (var gapPos in Enum.GetValues(typeof(OutpostModuleInfo.GapPosition)))
            {
                if ((OutpostModuleInfo.GapPosition)gapPos == OutpostModuleInfo.GapPosition.None) { continue; }
                gapPositionDropDown.AddItem(TextManager.Capitalize(gapPos.ToString()), gapPos);
                if (Submarine.MainSub.Info?.OutpostModuleInfo?.GapPositions.HasFlag((OutpostModuleInfo.GapPosition)gapPos) ?? false)
                {
                    gapPositionDropDown.SelectItem(gapPos);
                }
            }

            gapPositionDropDown.OnSelected += (_, __) =>
            {
                if (Submarine.MainSub.Info?.OutpostModuleInfo == null) { return false; }
                Submarine.MainSub.Info.OutpostModuleInfo.GapPositions = OutpostModuleInfo.GapPosition.None;
                if (gapPositionDropDown.SelectedDataMultiple.Any())
                {
                    List<string> gapPosTexts = new List<string>();
                    foreach (OutpostModuleInfo.GapPosition gapPos in gapPositionDropDown.SelectedDataMultiple)
                    {
                        Submarine.MainSub.Info.OutpostModuleInfo.GapPositions |= gapPos;
                        gapPosTexts.Add(TextManager.Capitalize(gapPos.ToString()));
                    }
                    gapPositionDropDown.Text = ToolBox.LimitString(string.Join(", ", gapPosTexts), gapPositionDropDown.Font, gapPositionDropDown.Rect.Width);
                }
                else
                {
                    gapPositionDropDown.Text = ToolBox.LimitString("None", gapPositionDropDown.Font, gapPositionDropDown.Rect.Width);
                }
                return true;
            };
            gapPositionGroup.RectTransform.MinSize = new Point(0, gapPositionGroup.RectTransform.Children.Max(c => c.MinSize.Y));

            // -------------------

            var maxModuleCountGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), outpostSettingsContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), maxModuleCountGroup.RectTransform),
                TextManager.Get("OutPostModuleMaxCount"), textAlignment: Alignment.CenterLeft, wrap: true)
            {
                ToolTip = TextManager.Get("OutPostModuleMaxCountToolTip")
            };
            new GUINumberInput(new RectTransform(new Vector2(0.4f, 1.0f), maxModuleCountGroup.RectTransform), GUINumberInput.NumberType.Int)
            {
                ToolTip = TextManager.Get("OutPostModuleMaxCountToolTip"),
                IntValue = Submarine.MainSub?.Info?.OutpostModuleInfo?.MaxCount ?? 1000,
                MinValueInt = 0,
                MaxValueInt = 1000,
                OnValueChanged = (numberInput) =>
                {
                    Submarine.MainSub.Info.OutpostModuleInfo.MaxCount = numberInput.IntValue;
                }
            };

            var commonnessGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), outpostSettingsContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), commonnessGroup.RectTransform),
                TextManager.Get("subeditor.outpostcommonness"), textAlignment: Alignment.CenterLeft, wrap: true);
            new GUINumberInput(new RectTransform(new Vector2(0.4f, 1.0f), commonnessGroup.RectTransform), GUINumberInput.NumberType.Float)
            {
                FloatValue = Submarine.MainSub?.Info?.OutpostModuleInfo?.Commonness ?? 10,
                MinValueFloat = 0,
                MaxValueFloat = 100,
                OnValueChanged = (numberInput) =>
                {
                    Submarine.MainSub.Info.OutpostModuleInfo.Commonness = numberInput.FloatValue;
                }
            };
            outpostSettingsContainer.RectTransform.MinSize = new Point(0, outpostSettingsContainer.RectTransform.Children.Sum(c => c.Children.Any() ? c.Children.Max(c2 => c2.MinSize.Y) : 0));

            //------------------------------------------------------------------

            var subSettingsContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), leftColumn.RectTransform))
            {
                Stretch = true
            };
            new GUIFrame(new RectTransform(Vector2.One, subSettingsContainer.RectTransform), "InnerFrame")
            {
                IgnoreLayoutGroups = true
            };

            var priceGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), subSettingsContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), priceGroup.RectTransform),
                TextManager.Get("subeditor.price"), textAlignment: Alignment.CenterLeft, wrap: true);

            int basePrice = GameMain.DebugDraw ? 0 : Submarine.MainSub?.CalculateBasePrice() ?? 1000;
            new GUINumberInput(new RectTransform(new Vector2(0.4f, 1.0f), priceGroup.RectTransform), GUINumberInput.NumberType.Int, hidePlusMinusButtons: true)
            {
                IntValue = Math.Max(Submarine.MainSub?.Info?.Price ?? basePrice, basePrice),
                MinValueInt = basePrice,                
                MaxValueInt = 999999,
                OnValueChanged = (numberInput) =>
                {
                    Submarine.MainSub.Info.Price = numberInput.IntValue;
                }
            };
            if (Submarine.MainSub?.Info != null)
            {
                Submarine.MainSub.Info.Price = Math.Max(Submarine.MainSub.Info.Price, basePrice);
            }

            if (!Submarine.MainSub.Info.HasTag(SubmarineTag.Shuttle))
            {
                var classGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), subSettingsContainer.RectTransform), isHorizontal: true)
                {
                    Stretch = true
                };
                new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), classGroup.RectTransform),
                    TextManager.Get("submarineclass"), textAlignment: Alignment.CenterLeft, wrap: true);
                GUIDropDown classDropDown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1.0f), classGroup.RectTransform));
                classDropDown.RectTransform.MinSize = new Point(0, subTypeContainer.RectTransform.Children.Max(c => c.MinSize.Y));
                classDropDown.AddItem(TextManager.Get("submarineclass.undefined"), SubmarineClass.Undefined);
                classDropDown.AddItem(TextManager.Get("submarineclass.scout"), SubmarineClass.Scout);
                classDropDown.AddItem(TextManager.Get("submarineclass.attack"), SubmarineClass.Attack);
                classDropDown.AddItem(TextManager.Get("submarineclass.transport"), SubmarineClass.Transport);
                classDropDown.AddItem(TextManager.Get("submarineclass.deepdiver"), SubmarineClass.DeepDiver);
                classDropDown.OnSelected += (selected, userdata) =>
                {
                    SubmarineClass submarineClass = (SubmarineClass)userdata;
                    Submarine.MainSub.Info.SubmarineClass = submarineClass;
                    return true;
                };

                classDropDown.SelectItem(Submarine.MainSub.Info.SubmarineClass);
            }

            var crewSizeArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), subSettingsContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = 5
            };

            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), crewSizeArea.RectTransform),
                TextManager.Get("RecommendedCrewSize"), textAlignment: Alignment.CenterLeft, wrap: true, font: GUI.SmallFont);
            var crewSizeMin = new GUINumberInput(new RectTransform(new Vector2(0.17f, 1.0f), crewSizeArea.RectTransform), GUINumberInput.NumberType.Int, relativeButtonAreaWidth: 0.25f)
            {
                MinValueInt = 1,
                MaxValueInt = 128
            };
            new GUITextBlock(new RectTransform(new Vector2(0.06f, 1.0f), crewSizeArea.RectTransform), "-", textAlignment: Alignment.Center);
            var crewSizeMax = new GUINumberInput(new RectTransform(new Vector2(0.17f, 1.0f), crewSizeArea.RectTransform), GUINumberInput.NumberType.Int, relativeButtonAreaWidth: 0.25f)
            {
                MinValueInt = 1,
                MaxValueInt = 128
            };

            crewSizeMin.OnValueChanged += (numberInput) =>
            {
                crewSizeMax.IntValue = Math.Max(crewSizeMax.IntValue, numberInput.IntValue);
                Submarine.MainSub.Info.RecommendedCrewSizeMin = crewSizeMin.IntValue;
                Submarine.MainSub.Info.RecommendedCrewSizeMax = crewSizeMax.IntValue;
            };

            crewSizeMax.OnValueChanged += (numberInput) =>
            {
                crewSizeMin.IntValue = Math.Min(crewSizeMin.IntValue, numberInput.IntValue);
                Submarine.MainSub.Info.RecommendedCrewSizeMin = crewSizeMin.IntValue;
                Submarine.MainSub.Info.RecommendedCrewSizeMax = crewSizeMax.IntValue;
            };

            var crewExpArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), subSettingsContainer.RectTransform), isHorizontal: true)
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
                Submarine.MainSub.Info.RecommendedCrewExperience = (string)experienceText.UserData;
                return true;
            };

            toggleExpRight.OnClicked += (btn, userData) =>
            {
                int currentIndex = Array.IndexOf(crewExperienceLevels, (string)experienceText.UserData);
                currentIndex++;
                if (currentIndex >= crewExperienceLevels.Length) currentIndex = 0;
                experienceText.UserData = crewExperienceLevels[currentIndex];
                experienceText.Text = TextManager.Get(crewExperienceLevels[currentIndex]);
                Submarine.MainSub.Info.RecommendedCrewExperience = (string)experienceText.UserData;
                return true;
            };

            if (Submarine.MainSub != null)
            {
                int min =  Submarine.MainSub.Info.RecommendedCrewSizeMin;
                int max = Submarine.MainSub.Info.RecommendedCrewSizeMax;
                crewSizeMin.IntValue = min;
                crewSizeMax.IntValue = max;
                experienceText.UserData =  string.IsNullOrEmpty(Submarine.MainSub.Info.RecommendedCrewExperience) ?
                    crewExperienceLevels[0] : Submarine.MainSub.Info.RecommendedCrewExperience;
                experienceText.Text = TextManager.Get((string)experienceText.UserData);
            }

            subTypeDropdown.OnSelected += (selected, userdata) =>
            {
                SubmarineType type = (SubmarineType)userdata;
                Submarine.MainSub.Info.Type = type;
                if (type == SubmarineType.OutpostModule)
                {
                    Submarine.MainSub.Info.OutpostModuleInfo ??= new OutpostModuleInfo(Submarine.MainSub.Info);
                }
                previewImageButtonHolder.Children.ForEach(c => c.Enabled = type != SubmarineType.OutpostModule);
                outpostSettingsContainer.Visible = type == SubmarineType.OutpostModule;
                outpostSettingsContainer.IgnoreLayoutGroups = !outpostSettingsContainer.Visible;

                subSettingsContainer.Visible = type == SubmarineType.Player;
                subSettingsContainer.IgnoreLayoutGroups = !subSettingsContainer.Visible;
                return true;
            };
            subSettingsContainer.RectTransform.MinSize = new Point(0, subSettingsContainer.RectTransform.Children.Sum(c => c.Children.Any() ? c.Children.Max(c2 => c2.MinSize.Y) : 0));

            // right column ---------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), rightColumn.RectTransform), TextManager.Get("SubPreviewImage"), font: GUI.SubHeadingFont);
            
            var previewImageHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), rightColumn.RectTransform), style: null) { Color = Color.Black, CanBeFocused = false };
            previewImage = new GUIImage(new RectTransform(Vector2.One, previewImageHolder.RectTransform), Submarine.MainSub?.Info.PreviewImage, scaleToFit: true);

            previewImageButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };

            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), previewImageButtonHolder.RectTransform), TextManager.Get("SubPreviewImageCreate"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    using (System.IO.MemoryStream imgStream = new System.IO.MemoryStream())
                    {
                        CreateImage(defaultPreviewImageSize.X, defaultPreviewImageSize.Y, imgStream);
                        previewImage.Sprite = new Sprite(TextureLoader.FromStream(imgStream, compress: false), null, null);
                        if (Submarine.MainSub != null)
                        {
                            Submarine.MainSub.Info.PreviewImage = previewImage.Sprite;
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
                            Submarine.MainSub.Info.PreviewImage = previewImage.Sprite;
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
                    Selected = Submarine.MainSub != null && Submarine.MainSub.Info.HasTag(tag),
                    UserData = tag,

                    OnSelected = (GUITickBox tickBox) =>
                    {
                        if (Submarine.MainSub == null) return false;
                        if (tickBox.Selected)
                        {
                            Submarine.MainSub.Info.AddTag((SubmarineTag)tickBox.UserData);
                        }
                        else
                        {
                            Submarine.MainSub.Info.RemoveTag((SubmarineTag)tickBox.UserData);
                        }
                        return true;
                    }
                };
            }

            var contentPackagesLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), horizontalArea.RectTransform, Anchor.TopRight),
                TextManager.Get("RequiredContentPackages"), wrap: true, font: GUI.SmallFont);

            var contentPackList = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f - contentPackagesLabel.RectTransform.RelativeSize.Y),
                horizontalArea.RectTransform, Anchor.BottomRight));

            if (Submarine.MainSub != null) {
                List<string> contentPacks = Submarine.MainSub.Info.RequiredContentPackages.ToList();
                foreach (ContentPackage contentPack in ContentPackage.AllPackages)
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
                        Selected = Submarine.MainSub.Info.RequiredContentPackages.Contains(contentPackageName),
                        UserData = contentPackageName
                    };
                    cpTickBox.OnSelected += tickBox =>
                    {
                        if (tickBox.Selected)
                        {
                            Submarine.MainSub.Info.RequiredContentPackages.Add((string)tickBox.UserData);
                        }
                        else
                        {
                            Submarine.MainSub.Info.RequiredContentPackages.Remove((string)tickBox.UserData);
                        }
                        return true;
                    };
                }
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

            subSettingsContainer.RectTransform.MinSize = outpostSettingsContainer.RectTransform.MinSize =
                new Point(0, Math.Max(subSettingsContainer.Rect.Height, outpostSettingsContainer.Rect.Height));
            subSettingsContainer.Recalculate();
            outpostSettingsContainer.Recalculate();

            descriptionBox.Text = Submarine.MainSub == null ? "" : Submarine.MainSub.Info.Description;
            submarineDescriptionCharacterCount.Text = descriptionBox.Text.Length + " / " + submarineDescriptionLimit;

            subTypeDropdown.SelectItem(Submarine.MainSub.Info.Type);

            if (quickSave) { SaveSub(saveButton, saveButton.UserData); }
        }


        private void CreateSaveAssemblyScreen()
        {
            SetMode(Mode.Default);

            saveFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null)
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) saveFrame = null; return true; }
            };

            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, saveFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

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

        /// <summary>
        /// Loads an item assembly and only returns items which are not inside other inventories.
        /// This is to prevent us from trying to place for example Oxygen Tanks inside an inventory
        /// when it's already inside a diving suit.
        /// </summary>
        /// <param name="assemblyPrefab"></param>
        /// <returns></returns>
        private List<Item> LoadItemAssemblyInventorySafe(ItemAssemblyPrefab assemblyPrefab)
        {
            var realItems = assemblyPrefab.CreateInstance(Vector2.Zero, Submarine.MainSub);
            var itemInstance = new List<Item>();
            realItems.ForEach(entity =>
            {
                if (entity is Item it && it.ParentInventory == null)
                {
                    itemInstance.Add(it);
                }
            });
            return itemInstance;
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

            bool hideInMenus = !(nameBox.Parent.GetChildByUserData("hideinmenus") is GUITickBox hideInMenusTickBox) ? false : hideInMenusTickBox.Selected;
#if DEBUG
            string saveFolder = ItemAssemblyPrefab.VanillaSaveFolder;
#else
            string saveFolder = ItemAssemblyPrefab.SaveFolder;
            if (!Directory.Exists(saveFolder))
            {
                try
                {
                    Directory.CreateDirectory(saveFolder);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create a directory for the item assmebly.", e);
                    return false;
                }
            }
#endif
            string filePath = Path.Combine(saveFolder, nameBox.Text + ".xml");

            if (File.Exists(filePath))
            {
                var msgBox = new GUIMessageBox(TextManager.Get("Warning"), TextManager.Get("ItemAssemblyFileExistsWarning"), new[] { TextManager.Get("Yes"), TextManager.Get("No") });
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
#if DEBUG
                doc.Save(filePath);
#else
                doc.SaveSafe(filePath);
#endif
                new ItemAssemblyPrefab(filePath);
                UpdateEntityList();
            }

            saveFrame = null;
            return false;
        }

        private void CreateLoadScreen()
        {
            CloseItem();
            SetMode(Mode.Default);

            loadFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null)
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) loadFrame = null; return true; },
            };

            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, loadFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.75f), loadFrame.RectTransform, Anchor.Center) { MinSize = new Point(350, 500) });

            var paddedLoadFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), innerFrame.RectTransform, Anchor.Center)) { Stretch = true, RelativeSpacing = 0.01f };

            var deleteButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedLoadFrame.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.1f,
                Stretch = true
            };
            
            var searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedLoadFrame.RectTransform), font: GUI.Font, createClearButton: true);
            var searchTitle = new GUITextBlock(new RectTransform(Vector2.One, searchBox.RectTransform), TextManager.Get("serverlog.filter"),
                textAlignment: Alignment.CenterLeft, font: GUI.Font)
            {
                CanBeFocused = false,
                IgnoreLayoutGroups = true
            };
            searchTitle.TextColor *= 0.5f;

            var subList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), paddedLoadFrame.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (GUIComponent selected, object userData) =>
                {
                    if (deleteButtonHolder.FindChild("delete") is GUIButton deleteBtn)
                    {
#if DEBUG
                        deleteBtn.Enabled = true;
#else
                        deleteBtn.Enabled = userData is SubmarineInfo subInfo && !subInfo.IsVanillaSubmarine();
#endif
                    }
                    return true;
                }
            };

            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (textBox, text) => { FilterSubs(subList, text); return true; };

            string downloadFolder = Path.GetFullPath(SaveUtil.SubmarineDownloadFolder);
            List<SubmarineInfo> sortedSubs = new List<SubmarineInfo>(SubmarineInfo.SavedSubmarines.Where(s => Path.GetDirectoryName(Path.GetFullPath(s.FilePath)) != downloadFolder));
            sortedSubs.Sort((s1, s2) => { return s1.Type.CompareTo(s2.Type) * 100 + s1.Name.CompareTo(s2.Name); });

            SubmarineInfo prevSub = null;

            foreach (SubmarineInfo sub in sortedSubs)
            {
                if (prevSub == null || prevSub.Type != sub.Type)
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), subList.Content.RectTransform) { MinSize = new Point(0, 35) },
                        TextManager.Get("SubmarineType." + sub.Type), font: GUI.LargeFont, textAlignment: Alignment.Center, style: "ListBoxElement")
                    {
                        CanBeFocused = false
                    };
                    prevSub = sub;
                }

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), subList.Content.RectTransform) { MinSize = new Point(0, 30) },
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
                else if (sub.IsPlayer)
                {
                    var classText = new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), textBlock.RectTransform, Anchor.CenterRight),
                    TextManager.Get($"submarineclass.{sub.SubmarineClass}"), textAlignment: Alignment.CenterRight, font: GUI.SmallFont)
                    {
                        TextColor = textBlock.TextColor * 0.8f,
                        ToolTip = textBlock.RawToolTip
                    };
                }
            }

            var deleteButton = new GUIButton(new RectTransform(Vector2.One, deleteButtonHolder.RectTransform, Anchor.TopCenter),
                TextManager.Get("Delete"))
            {
                Enabled = false,
                UserData = "delete"
            };
            deleteButton.OnClicked = (btn, userdata) =>
            {
                if (subList.SelectedComponent != null)
                {
                    TryDeleteSub(subList.SelectedComponent.UserData as SubmarineInfo);
                }
                deleteButton.Enabled = false;
                return true;
            };
            

            if (AutoSaveInfo?.Root != null)
            {
                int min = Math.Min(6, AutoSaveInfo.Root.Elements().Count());
                var loadAutoSave = new GUIDropDown(new RectTransform(Vector2.One,  deleteButtonHolder.RectTransform, Anchor.BottomCenter), TextManager.Get("LoadAutoSave"), elementCount: min)
                {
                    ToolTip = TextManager.Get("LoadAutoSaveTooltip"),
                    UserData = "loadautosave",
                    OnSelected = (button, o) =>
                    {
                        LoadAutoSave(o);
                        return true;
                    }
                };
                foreach (XElement saveElement in AutoSaveInfo.Root.Elements().Reverse())
                {
                    DateTime time = DateTime.MinValue.AddSeconds(saveElement.GetAttributeUInt64("time", 0));
                    TimeSpan difference = DateTime.UtcNow - time;

                    string tooltip = TextManager.GetWithVariables("subeditor.autosaveage", 
                        new[]
                        {
                            "[hours]", 
                            "[minutes]", 
                            "[seconds]"
                        },
                        new[]
                        {
                            ((int)Math.Floor(difference.TotalHours)).ToString(),
                            difference.Minutes.ToString(),
                            difference.Seconds.ToString()
                        });

                    string submarineName = saveElement.GetAttributeString("name", TextManager.Get("UnspecifiedSubFileName"));
                    string timeFormat;

                    double totalMinutes = difference.TotalMinutes;

                    if (totalMinutes < 1)
                    {
                        timeFormat = TextManager.Get("subeditor.savedjustnow");
                    } 
                    else if (totalMinutes > 60)
                    {
                        timeFormat = TextManager.Get("subeditor.savedmorethanhour");
                    }
                    else
                    {
                        timeFormat = TextManager.GetWithVariable("subeditor.saveageminutes", "[minutes]", difference.Minutes.ToString());
                    }
                    
                    string entryName = TextManager.GetWithVariables("subeditor.autosaveentry", new []{ "[submarine]", "[saveage]" }, new []{ submarineName, timeFormat });

                    loadAutoSave.AddItem(entryName, saveElement, tooltip);
                }
            }

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

            controlBtnHolder.RectTransform.MaxSize = new Point(int.MaxValue, controlBtnHolder.Children.First().Rect.Height);
        }

        private void FilterSubs(GUIListBox subList, string filter)
        {
            foreach (GUIComponent child in subList.Content.Children)
            {
                if (!(child.UserData is SubmarineInfo sub)) { continue; }
                child.Visible = string.IsNullOrEmpty(filter) || sub.Name.ToLower().Contains(filter.ToLower());
            }
        }

        /// <summary>
        /// Recovers the auto saved submarine
        /// <see cref="AutoSave"/>
        /// </summary>
        private void LoadAutoSave(object UserData)
        {
            if (!(UserData is XElement element)) { return; }

            string filePath = element.GetAttributeString("file", "");
            if (string.IsNullOrWhiteSpace(filePath)) { return; }

            var loadedSub = Submarine.Load(new SubmarineInfo(filePath), true);
        
            // set the submarine file path to the "default" value
            loadedSub.Info.FilePath = Path.Combine(SubmarineInfo.SavePath, $"{TextManager.Get("UnspecifiedSubFileName")}.sub");
            loadedSub.Info.Name = TextManager.Get("UnspecifiedSubFileName");
            try 
            {
                loadedSub.Info.Name = loadedSub.Info.SubmarineElement.GetAttributeString("name",  loadedSub.Info.Name); 
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to find a name for the submarine.", e);
            }
            Submarine.MainSub = loadedSub;
            Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
            Submarine.MainSub.UpdateTransform();
            Submarine.MainSub.Info.Name = loadedSub.Info.Name;
            subNameLabel.Text = ToolBox.LimitString(loadedSub.Info.Name, subNameLabel.Font, subNameLabel.Rect.Width);
        
            CreateDummyCharacter();
        
            cam.Position = Submarine.MainSub.Position + Submarine.MainSub.HiddenSubPosition;

            loadFrame = null;
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
            if (!(subList.SelectedComponent.UserData is SubmarineInfo selectedSubInfo)) { return false; }

            Submarine.Unload();
            var selectedSub = new Submarine(selectedSubInfo);
            Submarine.MainSub = selectedSub;
            Submarine.MainSub.UpdateTransform(interpolate: false);
            ClearUndoBuffer();
            CreateDummyCharacter();

            string name = Submarine.MainSub.Info.Name;
            subNameLabel.Text = ToolBox.LimitString(name, subNameLabel.Font, subNameLabel.Rect.Width);

            cam.Position = Submarine.MainSub.Position + Submarine.MainSub.HiddenSubPosition;

            loadFrame = null;
            
            if (selectedSub.Info.GameVersion < new Version("0.8.9.0"))
            {
                var adjustLightsPrompt = new GUIMessageBox(TextManager.Get("Warning"), TextManager.Get("AdjustLightsPrompt"), 
                    new[] { TextManager.Get("Yes"), TextManager.Get("No") });
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

        private void TryDeleteSub(SubmarineInfo sub)
        {
            if (sub == null) { return; }

            //if the sub is included in a content package that only defines that one sub,
            //delete the content package as well
            ContentPackage subPackage = null;
            foreach (ContentPackage cp in ContentPackage.RegularPackages)
            {
                if (cp.Files.Count == 1 && Path.GetFullPath(cp.Files[0].Path) == Path.GetFullPath(sub.FilePath))
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
                    sub.Dispose();
                    File.Delete(sub.FilePath);
                    SubmarineInfo.RefreshSavedSubs();
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

        private void OpenEntityMenu(MapEntityCategory? entityCategory)
        {
            foreach (GUIButton categoryButton in entityCategoryButtons)
            {
                categoryButton.Selected = entityCategory.HasValue ?
                    categoryButton.UserData is MapEntityCategory category && entityCategory.Value == category :
                    categoryButton.UserData == null;
                string categoryName = entityCategory.HasValue ? entityCategory.Value.ToString() : "All";
                selectedCategoryText.Text = TextManager.Get("MapEntityCategory." + categoryName);
                selectedCategoryButton.ApplyStyle(GUI.Style.GetComponentStyle("CategoryButton." + categoryName));
            }

            selectedCategory = entityCategory;
            
            SetMode(Mode.Default);

            saveFrame = null;
            loadFrame = null;
                        
            foreach (GUIComponent child in toggleEntityMenuButton.Children)
            {
                child.SpriteEffects = entityMenuOpen ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }

            foreach (GUIComponent child in categorizedEntityList.Content.Children)
            {
                child.Visible = !entityCategory.HasValue || (MapEntityCategory)child.UserData == entityCategory;
                if (child.Visible && dummyCharacter?.SelectedConstruction?.OwnInventory != null)
                {
                    child.Visible = child.UserData is MapEntityPrefab item && IsItemPrefab(item);
                }
            }
            
            if (!string.IsNullOrEmpty(entityFilterBox.Text)) { FilterEntities(entityFilterBox.Text); }
            
            categorizedEntityList.UpdateScrollBarSize();
            categorizedEntityList.BarScroll = 0.0f;
            // categorizedEntityList.Visible = true;
            // allEntityList.Visible = false;
        }

        private void FilterEntities(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                allEntityList.Visible = false;
                categorizedEntityList.Visible = true;

                foreach (GUIComponent child in categorizedEntityList.Content.Children)
                {
                    child.Visible = !selectedCategory.HasValue || selectedCategory == (MapEntityCategory)child.UserData;
                    if (!child.Visible) { return; }
                    var innerList = child.GetChild<GUIListBox>();
                    foreach (GUIComponent grandChild in innerList.Content.Children)
                    {
                        grandChild.Visible = ((MapEntityPrefab)grandChild.UserData).Name.ToLower().Contains(filter);
                        if (grandChild.Visible && dummyCharacter?.SelectedConstruction?.OwnInventory != null)
                        {
                            grandChild.Visible = grandChild.UserData is MapEntityPrefab item && IsItemPrefab(item);
                        }
                    }
                };
                categorizedEntityList.UpdateScrollBarSize();
                categorizedEntityList.BarScroll = 0.0f;                
                return;
            }

            allEntityList.Visible = true;
            categorizedEntityList.Visible = false;
            filter = filter.ToLower();
            foreach (GUIComponent child in allEntityList.Content.Children)
            {
                child.Visible = 
                    (!selectedCategory.HasValue || ((MapEntityPrefab)child.UserData).Category.HasFlag(selectedCategory)) &&
                    ((MapEntityPrefab)child.UserData).Name.ToLower().Contains(filter); ;
                if (child.Visible && dummyCharacter?.SelectedConstruction?.OwnInventory != null)
                {
                    child.Visible = child.UserData is MapEntityPrefab item && IsItemPrefab(item);
                }
            }
            allEntityList.UpdateScrollBarSize();
            allEntityList.BarScroll = 0.0f;
        }

        private void ClearFilter()
        {
            FilterEntities("");
            categorizedEntityList.UpdateScrollBarSize();
            categorizedEntityList.BarScroll = 0.0f;
            entityFilterBox.Text = "";
        }

        public void SetMode(Mode newMode)
        {
            if (newMode == mode) { return; }
            mode = newMode;

            lockMode = true;
            defaultModeTickBox.Selected = newMode == Mode.Default;
            wiringModeTickBox.Selected = newMode == Mode.Wiring;
            lockMode = false;

            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                me.IsHighlighted = false;
            }

            MapEntity.DeselectAll();
            MapEntity.FilteredSelectedList.Clear();
            ClearUndoBuffer();
            
            CreateDummyCharacter();
            if (newMode == Mode.Wiring)
            {
                var item = new Item(MapEntityPrefab.Find(null, "screwdriver") as ItemPrefab, Vector2.Zero, null);
                dummyCharacter.Inventory.TryPutItem(item, null, new List<InvSlotType>() { InvSlotType.RightHand });
                wiringToolPanel = CreateWiringPanel();
            }
        }

        private void RemoveDummyCharacter()
        {
            if (dummyCharacter == null || dummyCharacter.Removed) { return; }

            dummyCharacter.Inventory.AllItems.ForEachMod(it => it.Remove());
            dummyCharacter.Remove();
            dummyCharacter = null;            
        }

        private void CreateContextMenu()
        {
            if (GUIContextMenu.CurrentContextMenu != null) { return; }

            List<MapEntity> targets = MapEntity.mapEntityList.Any(me => me.IsHighlighted && !MapEntity.SelectedList.Contains(me)) ? 
                MapEntity.mapEntityList.Where(me => me.IsHighlighted).ToList() :
                new List<MapEntity>(MapEntity.SelectedList);

            Item target = null;

            var single = targets.Count == 1 ? targets.Single() : null;
            if (single is Item item && item.Components.Any(ic => !(ic is ConnectionPanel) && !(ic is Repairable) && ic.GuiFrame != null))
            {
                // Do not offer the ability to open the inventory if the inventory should never be drawn
                var container = item.GetComponent<ItemContainer>();
                if (container == null || container.DrawInventory) { target = item; }
            }

            // Holding shift brings up special context menu options
            if (PlayerInput.IsShiftDown())
            {
                GUIContextMenu.CreateContextMenu(
                    new ContextMenuOption("SubEditor.EditBackgroundColor", isEnabled: true,  onSelected: CreateBackgroundColorPicker),
                    new ContextMenuOption("SubEditor.ToggleTransparency",  isEnabled: true,  onSelected: () => TransparentWiringMode = !TransparentWiringMode),
                    new ContextMenuOption("SubEditor.ToggleGrid",  isEnabled: true,  onSelected: () => ShouldDrawGrid = !ShouldDrawGrid),
                    new ContextMenuOption("SubEditor.PasteAssembly",  isEnabled: true,  PasteAssembly),
                    new ContextMenuOption("Editor.SelectSame", isEnabled: targets.Count > 0, onSelected: delegate
                    {
                        IEnumerable<MapEntity> matching = MapEntity.mapEntityList.Where(e => e.prefab != null && targets.Any(t => t.prefab?.Identifier == e.prefab.Identifier) && !MapEntity.SelectedList.Contains(e));
                        MapEntity.SelectedList.AddRange(matching);
                    }),
                    new ContextMenuOption("SubEditor.AddImage",            isEnabled: true, onSelected: ImageManager.CreateImageWizard),
                    new ContextMenuOption("SubEditor.ToggleImageEditing",  isEnabled: true, onSelected: delegate
                    {
                        ImageManager.EditorMode = !ImageManager.EditorMode;
                        if (!ImageManager.EditorMode) { GameMain.Config.SaveNewPlayerConfig(); }
                    }));
            }
            else
            {
                GUIContextMenu.CreateContextMenu(
                    new ContextMenuOption("label.openlabel",        isEnabled: target != null,             onSelected: () => OpenItem(target)),
                    new ContextMenuOption("editor.cut",             isEnabled: targets.Count > 0,          onSelected: () => MapEntity.Cut(targets)),
                    new ContextMenuOption("editor.copytoclipboard", isEnabled: targets.Count > 0,          onSelected: () => MapEntity.Copy(targets)),
                    new ContextMenuOption("editor.paste",           isEnabled: MapEntity.CopiedList.Any(), onSelected: () => MapEntity.Paste(cam.ScreenToWorld(PlayerInput.MousePosition))),
                    new ContextMenuOption("delete",                 isEnabled: targets.Count > 0,          onSelected: delegate
                    {
                        StoreCommand(new AddOrDeleteCommand(targets, true));
                        foreach (var me in targets)
                        {
                            if (!me.Removed) { me.Remove(); }
                        }
                    }));
            }
        }

        private void PasteAssembly()
        {
            string clipboard = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clipboard))
            {
                DebugConsole.ThrowError("Unable to paste assembly: Clipboard content is empty.");
                return;
            }

            XElement element = null;

            try
            {
                element = XDocument.Parse(clipboard).Root;
            }
            catch (Exception) { /* ignored */ }

            if (element == null)
            {
                DebugConsole.ThrowError("Unable to paste assembly: Clipboard content is not valid XML.");
                return;
            }

            Vector2 pos = cam.ScreenToWorld(PlayerInput.MousePosition);
            Submarine sub = Submarine.MainSub;
            List<MapEntity> entities;
            try
            {
                entities = ItemAssemblyPrefab.PasteEntities(pos, sub, element, selectInstance: true);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Unable to paste assembly: Failed to load items.", e);
                return;
            }

            if (!entities.Any()) { return; }
            StoreCommand(new AddOrDeleteCommand(entities, false, handleInventoryBehavior: false));
        }

        public static GUIMessageBox CreatePropertyColorPicker(Color originalColor, SerializableProperty property, ISerializableEntity entity)
        {
            var entities = new List<(ISerializableEntity Entity, Color OriginalColor, SerializableProperty Property)> { (entity, originalColor, property) };

            foreach (ISerializableEntity selectedEntity in MapEntity.SelectedList.Where(selectedEntity => selectedEntity is ISerializableEntity && entity != selectedEntity).Cast<ISerializableEntity>())
            {
                switch (entity)
                {
                    case ItemComponent _ when selectedEntity is Item item:
                        foreach (var component in item.Components)
                        {
                            if (component.GetType() == entity.GetType() && component != entity)
                            {
                                entities.Add((component, (Color) property.GetValue(component), property));
                            }
                        }
                        break;
                    default:
                        if (selectedEntity.GetType() == entity.GetType())
                        {
                            entities.Add((selectedEntity, (Color) property.GetValue(selectedEntity), property));
                        }
                        else if (selectedEntity is { SerializableProperties: { } props} )
                        {
                            if (props.TryGetValue(property.NameToLowerInvariant, out SerializableProperty foundProp))
                            {
                                entities.Add((selectedEntity, (Color) foundProp.GetValue(selectedEntity), foundProp));
                            }
                        }
                        break;
                }
            }
            
            bool setValues = true;
            object sliderMutex     = new object(),
                   sliderTextMutex = new object(),
                   pickerMutex     = new object(),
                   hexMutex        = new object();

            Vector2 relativeSize = new Vector2(GUI.IsFourByThree() ? 0.4f : 0.3f, 0.3f);

            GUIMessageBox msgBox = new GUIMessageBox(string.Empty, string.Empty, Array.Empty<string>(), relativeSize, type: GUIMessageBox.Type.Vote)
            {
                UserData = "colorpicker",
                Draggable = true
            };

            GUILayoutGroup contentLayout = new GUILayoutGroup(new RectTransform(Vector2.One, msgBox.Content.RectTransform));
            GUITextBlock headerText = new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), contentLayout.RectTransform), property.Name, font: GUI.SubHeadingFont, textAlignment: Alignment.TopCenter)
            {
                AutoScaleVertical = true
            };

            GUILayoutGroup colorLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.7f), contentLayout.RectTransform), isHorizontal: true);

            GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), contentLayout.RectTransform), childAnchor: Anchor.BottomLeft, isHorizontal: true)
            {
                RelativeSpacing = 0.1f,
                Stretch = true
            };

            GUIButton closeButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1f), buttonLayout.RectTransform), TextManager.Get("OK"), textAlignment: Alignment.Center);
            GUIButton cancelButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1f), buttonLayout.RectTransform), TextManager.Get("Cancel"), textAlignment: Alignment.Center);

            contentLayout.Recalculate();
            colorLayout.Recalculate();

            GUIColorPicker colorPicker = new GUIColorPicker(new RectTransform(new Point(colorLayout.Rect.Height), colorLayout.RectTransform));
            var (h, s, v) = ToolBox.RGBToHSV(originalColor);
            colorPicker.SelectedHue = float.IsNaN(h) ? 0f : h;
            colorPicker.SelectedSaturation = s;
            colorPicker.SelectedValue = v;

            colorLayout.Recalculate();

            GUILayoutGroup sliderLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - colorPicker.RectTransform.RelativeSize.X, 1f), colorLayout.RectTransform), childAnchor: Anchor.TopRight);

            float currentHue = colorPicker.SelectedHue / 360f;
            GUILayoutGroup hueSliderLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.25f), sliderLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(0.1f, 0.2f), hueSliderLayout.RectTransform), text: "H:", font: GUI.SubHeadingFont) { Padding = Vector4.Zero, ToolTip = "Hue" };
            GUIScrollBar hueScrollBar = new GUIScrollBar(new RectTransform(new Vector2(0.7f, 1f), hueSliderLayout.RectTransform), style: "GUISlider", barSize: 0.05f) { BarScroll = currentHue };
            GUINumberInput hueTextBox = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1f), hueSliderLayout.RectTransform), inputType: GUINumberInput.NumberType.Float) { FloatValue = currentHue, MaxValueFloat = 1f, MinValueFloat = 0f, DecimalsToDisplay = 2 };

            GUILayoutGroup satSliderLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.2f), sliderLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(0.1f, 0.2f), satSliderLayout.RectTransform), text: "S:", font: GUI.SubHeadingFont) { Padding = Vector4.Zero, ToolTip = "Saturation"};
            GUIScrollBar satScrollBar = new GUIScrollBar(new RectTransform(new Vector2(0.7f, 1f), satSliderLayout.RectTransform), style: "GUISlider", barSize: 0.05f) { BarScroll = colorPicker.SelectedSaturation };
            GUINumberInput satTextBox = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1f), satSliderLayout.RectTransform), inputType: GUINumberInput.NumberType.Float) { FloatValue = colorPicker.SelectedSaturation, MaxValueFloat = 1f, MinValueFloat = 0f, DecimalsToDisplay = 2 };

            GUILayoutGroup valueSliderLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.2f), sliderLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(0.1f, 0.2f), valueSliderLayout.RectTransform), text: "V:", font: GUI.SubHeadingFont) { Padding = Vector4.Zero, ToolTip = "Value"};
            GUIScrollBar valueScrollBar = new GUIScrollBar(new RectTransform(new Vector2(0.7f, 1f), valueSliderLayout.RectTransform), style: "GUISlider", barSize: 0.05f) { BarScroll = colorPicker.SelectedValue };
            GUINumberInput valueTextBox = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1f), valueSliderLayout.RectTransform), inputType: GUINumberInput.NumberType.Float) { FloatValue = colorPicker.SelectedValue, MaxValueFloat = 1f, MinValueFloat = 0f, DecimalsToDisplay = 2 };

            GUILayoutGroup colorInfoLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.3f), sliderLayout.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true) { RelativeSpacing = 0.15f };

            new GUICustomComponent(new RectTransform(new Vector2(0.4f, 0.8f), colorInfoLayout.RectTransform), (batch, component) =>
            {
                Rectangle rect = component.Rect;
                Point areaSize = new Point(rect.Width, rect.Height / 2);
                Rectangle newColorRect = new Rectangle(rect.Location, areaSize);
                Rectangle oldColorRect = new Rectangle(new Point(newColorRect.Left, newColorRect.Bottom), areaSize);
            
                GUI.DrawRectangle(batch, newColorRect, ToolBox.HSVToRGB(colorPicker.SelectedHue, colorPicker.SelectedSaturation, colorPicker.SelectedValue), isFilled: true);
                GUI.DrawRectangle(batch, oldColorRect, originalColor, isFilled: true);
                GUI.DrawRectangle(batch, rect, Color.Black, isFilled: false);
            });

            GUITextBox hexValueBox = new GUITextBox(new RectTransform(new Vector2(0.3f, 1f), colorInfoLayout.RectTransform), text: ColorToHex(originalColor), createPenIcon: false) { OverflowClip = true };

            hueScrollBar.OnMoved = (bar, scroll) => { SetColor(sliderMutex); return true; };
            hueTextBox.OnValueChanged = input => { SetColor(sliderTextMutex); };
            
            satScrollBar.OnMoved = (bar, scroll) => { SetColor(sliderMutex); return true; };
            satTextBox.OnValueChanged = input => { SetColor(sliderTextMutex); };
            
            valueScrollBar.OnMoved = (bar, scroll) => { SetColor(sliderMutex); return true; };
            valueTextBox.OnValueChanged = input => { SetColor(sliderTextMutex); };

            colorPicker.OnColorSelected = (component, color) => { SetColor(pickerMutex); return true; };

            hexValueBox.OnEnterPressed = (box, text) => { SetColor(hexMutex); return true; };
            hexValueBox.OnDeselected += (sender, key) => { SetColor(hexMutex); };

            closeButton.OnClicked = (button, o) =>
            {
                colorPicker.DisposeTextures();
                msgBox.Close();

                Color newColor = SetColor(null);

                Dictionary<object, List<ISerializableEntity>> oldProperties = new Dictionary<object, List<ISerializableEntity>>();

                foreach (var (sEntity, color, _) in entities)
                {
                    if (sEntity is MapEntity { Removed: true }) { continue; }
                    if (!oldProperties.ContainsKey(color))
                    {
                        oldProperties.Add(color, new List<ISerializableEntity>());
                    }
                    oldProperties[color].Add(sEntity);
                }

                List<ISerializableEntity> affected = entities.Select(t => t.Entity).Where(se => se is MapEntity { Removed: false }).ToList();
                StoreCommand(new PropertyCommand(affected, property.Name, newColor, oldProperties));

                if (MapEntity.EditingHUD != null && (MapEntity.EditingHUD.UserData == entity || (!(entity is ItemComponent ic) || MapEntity.EditingHUD.UserData == ic.Item)))
                {
                    GUIListBox list = MapEntity.EditingHUD.GetChild<GUIListBox>();
                    if (list != null)
                    {
                        IEnumerable<SerializableEntityEditor> editors = list.Content.FindChildren(comp => comp is SerializableEntityEditor).Cast<SerializableEntityEditor>();
                        SerializableEntityEditor.LockEditing = true;
                        foreach (SerializableEntityEditor editor in editors)
                        {
                            if (editor.UserData == entity && editor.Fields.TryGetValue(property.Name, out GUIComponent[] _))
                            {
                                editor.UpdateValue(property, newColor, flash: false);
                            }
                        }
                        SerializableEntityEditor.LockEditing = false;
                    }
                }
                return true;
            };
            
            cancelButton.OnClicked = (button, o) =>
            {
                colorPicker.DisposeTextures();
                msgBox.Close();

                foreach (var (e, color, prop) in entities)
                {
                    if (e is MapEntity { Removed: true }) { continue; }
                    prop.TrySetValue(e, color);
                }
                return true;
            };

            return msgBox;

            Color SetColor(object source)
            {
                if (setValues)
                {
                    setValues = false;

                    if (source == sliderMutex)
                    {
                        Vector3 hsv = new Vector3(hueScrollBar.BarScroll * 360f, satScrollBar.BarScroll, valueScrollBar.BarScroll);
                        SetSliderTexts(hsv);
                        SetColorPicker(hsv);
                        SetHex(hsv);
                    } 
                    else if (source == sliderTextMutex)
                    {
                        Vector3 hsv = new Vector3(hueTextBox.FloatValue * 360f, satTextBox.FloatValue, valueTextBox.FloatValue);
                        SetSliders(hsv);
                        SetColorPicker(hsv);
                        SetHex(hsv);
                    }
                    else if (source == pickerMutex)
                    {
                        Vector3 hsv = new Vector3(colorPicker.SelectedHue, colorPicker.SelectedSaturation, colorPicker.SelectedValue);
                        SetSliders(hsv);
                        SetSliderTexts(hsv);
                        SetHex(hsv);
                    } 
                    else if (source == hexMutex)
                    {
                        Vector3 hsv = ToolBox.RGBToHSV(XMLExtensions.ParseColor(hexValueBox.Text, errorMessages: false));
                        if (float.IsNaN(hsv.X)) { hsv.X = 0f; }
                        SetSliders(hsv);
                        SetSliderTexts(hsv);
                        SetColorPicker(hsv);
                        SetHex(hsv);
                    }

                    setValues = true;
                }

                Color color = ToolBox.HSVToRGB(colorPicker.SelectedHue, colorPicker.SelectedSaturation, colorPicker.SelectedValue);
                foreach (var (e, origColor, prop) in entities)
                {
                    if (e is MapEntity { Removed: true }) { continue; }
                    color.A = origColor.A;
                    prop.TrySetValue(e, color);
                }
                return color;

                void SetSliders(Vector3 hsv)
                {
                    hueScrollBar.BarScroll = hsv.X / 360f;
                    satScrollBar.BarScroll = hsv.Y;
                    valueScrollBar.BarScroll = hsv.Z;
                }

                void SetSliderTexts(Vector3 hsv)
                {
                    hueTextBox.FloatValue = hsv.X / 360f;
                    satTextBox.FloatValue = hsv.Y;
                    valueTextBox.FloatValue = hsv.Z;
                }

                void SetColorPicker(Vector3 hsv)
                {
                    bool hueChanged = !MathUtils.NearlyEqual(colorPicker.SelectedHue, hsv.X);
                    colorPicker.SelectedHue = hsv.X;
                    colorPicker.SelectedSaturation = hsv.Y;
                    colorPicker.SelectedValue = hsv.Z;
                    if (hueChanged) { colorPicker.RefreshHue(); }
                }

                void SetHex(Vector3 hsv)
                {
                    Color hexColor = ToolBox.HSVToRGB(hsv.X, hsv.Y, hsv.Z);
                    hexValueBox!.Text = ColorToHex(hexColor);
                }
            }

            static string ColorToHex(Color color) => $"#{(color.R << 16 | color.G << 8 | color.B):X6}";
        }

        /// <summary>
        /// Creates a color picker that can be used to change the submarine editor's background color
        /// </summary>
        private void CreateBackgroundColorPicker()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("CharacterEditor.EditBackgroundColor"), "", new[] { TextManager.Get("Reset"), TextManager.Get("OK")}, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));

            var rgbLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), msgBox.Content.RectTransform), isHorizontal: true);

            // Generate R,G,B labels and parent elements
            var layoutParents = new GUILayoutGroup[3];
            for (int i = 0; i < 3; i++)
            {
                var colorContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1), rgbLayout.RectTransform), isHorizontal: true) { Stretch = true };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), colorContainer.RectTransform, Anchor.CenterLeft) { MinSize = new Point(15, 0) }, GUI.colorComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.Center);
                layoutParents[i] = colorContainer;
            }

            // attach number inputs to our generated parent elements
            var rInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[0].RectTransform), GUINumberInput.NumberType.Int) { IntValue = backgroundColor.R };
            var gInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[1].RectTransform), GUINumberInput.NumberType.Int) { IntValue = backgroundColor.G };
            var bInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[2].RectTransform), GUINumberInput.NumberType.Int) { IntValue = backgroundColor.B };

            rInput.MinValueInt = gInput.MinValueInt = bInput.MinValueInt = 0;
            rInput.MaxValueInt = gInput.MaxValueInt = bInput.MaxValueInt = 255;
            
            rInput.OnValueChanged = gInput.OnValueChanged = bInput.OnValueChanged = delegate
            {
                var color = new Color(rInput.IntValue, gInput.IntValue, bInput.IntValue);
                backgroundColor = color;
                GameSettings.SubEditorBackgroundColor = color;
            };
            
            // Reset button
            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                rInput.IntValue = 13;
                gInput.IntValue = 37;
                bInput.IntValue = 69;
                return true;
            };
            
            // Ok button
            msgBox.Buttons[1].OnClicked = (button, o) => 
            { 
                msgBox.Close();
                GameMain.Config.SaveNewPlayerConfig();
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
            if (!(selected.UserData is SubmarineInfo submarine)) return false;
            var prefab = new LinkedSubmarinePrefab(submarine);
            MapEntityPrefab.SelectPrefab(prefab);
            return true;
        }

        private bool SelectWire(GUIComponent component, object userData)
        {
            if (dummyCharacter == null) return false;

            //if the same type of wire has already been selected, deselect it and return
            Item existingWire = dummyCharacter.HeldItems.FirstOrDefault(i => i.Prefab == userData as ItemPrefab);
            if (existingWire != null)
            {
                existingWire.Drop(null);
                existingWire.Remove();
                return false;
            }

            var wire = new Item(userData as ItemPrefab, Vector2.Zero, null);

            int slotIndex = dummyCharacter.Inventory.FindLimbSlot(InvSlotType.LeftHand);

            //if there's some other type of wire in the inventory, remove it
            existingWire = dummyCharacter.Inventory.GetItemAt(slotIndex);
            if (existingWire != null && existingWire.Prefab != userData as ItemPrefab)
            {
                existingWire.Drop(null);
                existingWire.Remove();
            }

            dummyCharacter.Inventory.TryPutItem(wire, slotIndex, false, false, dummyCharacter);

            return true;
           
        }

        /// <summary>
        /// Tries to open an item container in the submarine editor using the dummy character
        /// </summary>
        /// <param name="itemContainer">The item we want to open</param>
        private void OpenItem(Item itemContainer)
        {
            if (dummyCharacter == null || itemContainer == null) { return; }

            if (((itemContainer.GetComponent<Holdable>() is { } holdable && !holdable.Attached) || itemContainer.GetComponent<Wearable>() != null) && itemContainer.GetComponent<ItemContainer>() != null)
            {
                // We teleport our dummy character to the item so it appears as the entity stays still when in reality the dummy is holding it
                oldItemPosition = itemContainer.SimPosition;
                TeleportDummyCharacter(oldItemPosition);
                
                // Override this so we can be sure the container opens
                var container = itemContainer.GetComponent<ItemContainer>();
                if (container != null) { container.KeepOpenWhenEquipped = true; }
                
                // We accept any slots except "Any" since that would take priority
                List<InvSlotType> allowedSlots = new List<InvSlotType>();
                itemContainer.AllowedSlots.ForEach(type =>
                {
                    if (type != InvSlotType.Any) { allowedSlots.Add(type); }
                });
                
                // Try to place the item in the dummy character's inventory
                bool success = dummyCharacter.Inventory.TryPutItem(itemContainer, dummyCharacter, allowedSlots);
                if (success) { OpenedItem = itemContainer; }
                else { return; }
            }
            MapEntity.SelectedList.Clear();
            MapEntity.FilteredSelectedList.Clear();
            MapEntity.SelectEntity(itemContainer);
            dummyCharacter.SelectedConstruction = itemContainer;
            FilterEntities(entityFilterBox.Text);
        }

        /// <summary>
        /// Close the currently opened item
        /// </summary>
        private void CloseItem()
        {
            if (dummyCharacter == null) { return; }
            //nothing to close -> return
            if (DraggedItemPrefab == null && dummyCharacter?.SelectedConstruction == null && OpenedItem == null) { return; }
            DraggedItemPrefab = null;
            dummyCharacter.SelectedConstruction = null;
            OpenedItem?.Drop(dummyCharacter);
            OpenedItem?.SetTransform(oldItemPosition, 0f);
            OpenedItem = null;
            FilterEntities(entityFilterBox.Text);
        }

        /// <summary>
        /// Teleports the dummy character to the specified position
        /// </summary>
        /// <param name="pos">The desired position</param>
        private void TeleportDummyCharacter(Vector2 pos)
        {
            if (dummyCharacter != null)
            {
                foreach (Limb limb in dummyCharacter.AnimController.Limbs)
                {
                    limb.body.SetTransform(pos, 0.0f);
                }
                dummyCharacter.AnimController.Collider.SetTransform(pos, 0);
            }
        }

        private bool ChangeSubName(GUITextBox textBox, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                textBox.Flash(GUI.Style.Red);
                return false;
            }

            if (Submarine.MainSub != null) Submarine.MainSub.Info.Name = text;
            textBox.Deselect();

            textBox.Text = text;

            textBox.Flash(GUI.Style.Green);

            return true;
        }

        private void ChangeSubDescription(GUITextBox textBox, string text)
        {
            if (Submarine.MainSub != null)
            {
                Submarine.MainSub.Info.Description = text;
            }
            else
            {
                textBox.UserData = text;
            }

            submarineDescriptionCharacterCount.Text = text.Length + " / " + submarineDescriptionLimit;
        }

        /// <summary>
        /// Checks if the prefab is an item or if it only consists of items
        /// </summary>
        /// <param name="mapPrefab">The prefab to check</param>
        /// <returns>True if the the prefab is an item or it contains only items</returns>
        private bool IsItemPrefab(MapEntityPrefab mapPrefab)
        {
            if (dummyCharacter?.SelectedConstruction == null)
            {
                return false;
            }

            return mapPrefab switch
            {
                ItemPrefab iPrefab => true,
                ItemAssemblyPrefab aPrefab => aPrefab.DisplayEntities.All(pair => pair.First is ItemPrefab),
                _ => false
            };
        }
        
        private bool SelectPrefab(GUIComponent component, object obj)
        {
            allEntityList.Deselect();
            categorizedEntityList.Deselect();
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
                    showEntitiesPanel.RectTransform.AbsoluteOffset = new Point(Math.Max(entityCountPanel.Rect.Right, saveAssemblyFrame.Rect.Right), TopPanel.Rect.Height);
                    matchingTickBox.Selected = true;
                    matchingTickBox.Flash(GUI.Style.Green);
                }
            }

            if (dummyCharacter?.SelectedConstruction != null)
            {
                var inv = dummyCharacter?.SelectedConstruction?.OwnInventory;
                if (inv != null)
                {
                    switch (obj)
                    {
                        case ItemAssemblyPrefab assemblyPrefab when PlayerInput.IsShiftDown():
                        {
                            var itemInstance = LoadItemAssemblyInventorySafe(assemblyPrefab);
                            var spawnedItem = false;
                            
                            itemInstance.ForEach(newItem =>
                            {
                                if (newItem != null)
                                {
                                    var placedItem = inv.TryPutItem(newItem, dummyCharacter);
                                    spawnedItem |= placedItem;
                                    
                                    if (!placedItem)
                                    {
                                        // Remove everything inside of the item so we don't get the popup asking if we want to keep the contained items
                                        newItem.OwnInventory?.DeleteAllItems();
                                        newItem.Remove();
                                    }
                                }
                            });

                            List<MapEntity> placedEntities = itemInstance.Where(it => !it.Removed).Cast<MapEntity>().ToList();
                            if (placedEntities.Any())
                            {
                                StoreCommand(new AddOrDeleteCommand(placedEntities, false));
                            }
                            SoundPlayer.PlayUISound(spawnedItem ? GUISoundType.PickItem : GUISoundType.PickItemFail);
                            break;
                        }
                        case ItemPrefab itemPrefab when PlayerInput.IsShiftDown():
                        {
                            var item = new Item(itemPrefab, Vector2.Zero, Submarine.MainSub);
                            if (!inv.TryPutItem(item, dummyCharacter))
                            {
                                // We failed, remove the item so it doesn't stay at x:0,y:0
                                SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                                item.Remove();
                            }
                            else
                            {
                                SoundPlayer.PlayUISound(GUISoundType.PickItem);
                            }

                            if (!item.Removed)
                            {
                                StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { item }, false));
                            }
                            break;
                        }
                        case ItemAssemblyPrefab _:
                        case ItemPrefab _:
                        {
                            // Place the item into our hands
                            DraggedItemPrefab = (MapEntityPrefab)obj;
                            SoundPlayer.PlayUISound(GUISoundType.PickItem);
                            break;
                        }
                    }
                }
            }
            else
            {
                SoundPlayer.PlayUISound(GUISoundType.PickItem);
                MapEntityPrefab.SelectPrefab(obj);
            }

            return false;
        }

        private bool GenerateWaypoints()
        {
            if (Submarine.MainSub == null) { return false; }
            return WayPoint.GenerateSubWaypoints(Submarine.MainSub);
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
            Vector2 max;

            List<MapEntity> mapEntityList = new List<MapEntity>();

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (e is Item it)
                {
                    Door door = it.GetComponent<Door>();
                    if (door != null)
                    {
                        int halfW = it.WorldRect.Width / 2;
                        wallPoints.Add(new Vector2(it.WorldRect.X + halfW, -it.WorldRect.Y + it.WorldRect.Height));
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

            var min = wallPoints[0];
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
                Gap newGap = new Gap(MapEntityPrefab.Find(null, "gap"), gapRect);
            }
        }
        
        public override void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) { return; }

            MapEntity.FilteredSelectedList.FirstOrDefault()?.AddToGUIUpdateList();
            EntityMenu.AddToGUIUpdateList();
            showEntitiesPanel.AddToGUIUpdateList();
            previouslyUsedPanel.AddToGUIUpdateList();
            undoBufferPanel.AddToGUIUpdateList();
            entityCountPanel.AddToGUIUpdateList();
            TopPanel.AddToGUIUpdateList();

            if (WiringMode)
            {
                wiringToolPanel.AddToGUIUpdateList();
            }

            if (MapEntity.HighlightedListBox != null)
            {
                MapEntity.HighlightedListBox.AddToGUIUpdateList();
            }

            if (dummyCharacter != null)
            {
                CharacterHUD.AddToGUIUpdateList(dummyCharacter);
                if (dummyCharacter.SelectedConstruction != null)
                {
                    dummyCharacter.SelectedConstruction.AddToGUIUpdateList();
                }
                else if (WiringMode && MapEntity.SelectedList.FirstOrDefault() is Item item && item.GetComponent<Wire>() != null)
                {
                    MapEntity.SelectedList.FirstOrDefault()?.AddToGUIUpdateList();
                }
            }
            if (loadFrame != null)
            {
                loadFrame.AddToGUIUpdateList();
            }
            else if (saveFrame != null)
            {
                saveFrame.AddToGUIUpdateList();
            }
        }
        
        /// <summary>
        /// GUI.MouseOn doesn't get updated while holding primary mouse and we need it to
        /// </summary>
        private bool IsMouseOnEditorGUI()
        {
            if (GUI.MouseOn == null) { return false; }

            return (EntityMenu?.MouseRect.Contains(PlayerInput.MousePosition) ?? false)
                   || (entityCountPanel?.MouseRect.Contains(PlayerInput.MousePosition) ?? false)
                   || (MapEntity.EditingHUD?.MouseRect.Contains(PlayerInput.MousePosition) ?? false) 
                   || (TopPanel?.MouseRect.Contains(PlayerInput.MousePosition) ?? false);
        }

        private static void Redo(int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                if (commandIndex < Commands.Count)
                {
                    Command command = Commands[commandIndex++];
                    command.Execute();
                }
            }
            GameMain.SubEditorScreen.UpdateUndoHistoryPanel();
        }

        private static void Undo(int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                if (commandIndex > 0)
                {
                    Command command = Commands[--commandIndex];
                    command.UnExecute();
                }
            }
            GameMain.SubEditorScreen.UpdateUndoHistoryPanel();
        }

        private static void ClearUndoBuffer()
        {
            SerializableEntityEditor.PropertyChangesActive = false;
            SerializableEntityEditor.CommandBuffer = null;
            Commands.ForEach(cmd => cmd.Cleanup());
            Commands.Clear();
            commandIndex = 0;
            GameMain.SubEditorScreen.UpdateUndoHistoryPanel();
        }

        public static void StoreCommand(Command command)
        {
            if (commandIndex != Commands.Count)
            {
                Commands.RemoveRange(commandIndex, Commands.Count - commandIndex);
            }
            Commands.Add(command);
            commandIndex++;

            // Start removing old commands
            if (Commands.Count > Math.Clamp(GameSettings.SubEditorMaxUndoBuffer, 1, 10240))
            {
                Commands.First()?.Cleanup();
                Commands.RemoveRange(0, 1);
                commandIndex = Commands.Count;
            }

            GameMain.SubEditorScreen.UpdateUndoHistoryPanel();
        }

        public void UpdateUndoHistoryPanel()
        {
            if (undoBufferPanel == null) { return; }

            undoBufferList.Content.Children.ForEachMod(component =>
            {
                undoBufferList.Content.RemoveChild(component);
            });

            for (int i = 0; i < Commands.Count; i++)
            {
                Command command = Commands[i];
                string description = command.GetDescription();
                CreateTextBlock(description, description, i + 1, command).RectTransform.SetAsFirstChild();
            }

            CreateTextBlock(TextManager.Get("undo.beginning"), TextManager.Get("undo.beginningtooltip"), 0, null);

            GUITextBlock CreateTextBlock(string name, string description, int index, Command command)
            {
                return new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), undoBufferList.Content.RectTransform) { MinSize = new Point(0, 15) },
                    ToolBox.LimitString(name, GUI.SmallFont, undoBufferList.Content.Rect.Width), font: GUI.SmallFont, textColor: index == commandIndex ? GUI.Style.Green : (Color?) null)
                {
                    UserData = command,
                    ToolTip = description
                };
            }
        }

        private static void CommitBulkItemBuffer()
        {
            if (BulkItemBuffer.Any())
            {
                AddOrDeleteCommand master = BulkItemBuffer[0];
                for (int i = 1; i < BulkItemBuffer.Count; i++)
                {
                    AddOrDeleteCommand command = BulkItemBuffer[i];
                    command.MergeInto(master);
                }

                StoreCommand(master);
                BulkItemBuffer.Clear();
            }

            bulkItemBufferinUse = null;
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        public override void Update(double deltaTime)
        {
            ImageManager.Update((float) deltaTime);

            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y)
            {
                saveFrame = null;
                loadFrame = null;
                saveAssemblyFrame = null;
                CreateUI();
                UpdateEntityList();
            }

            if (WiringMode && dummyCharacter != null)
            {
                Wire equippedWire = 
                    Character.Controlled?.HeldItems.FirstOrDefault(it => it.GetComponent<Wire>() != null)?.GetComponent<Wire>() ??
                    Wire.DraggingWire;

                if (equippedWire == null)
                {
                    // Highlight wires when hovering over the entity selection box
                    if (MapEntity.HighlightedListBox != null)
                    {
                        var lBox = MapEntity.HighlightedListBox;
                        foreach (var child in lBox.Content.Children)
                        {
                            if (child.UserData is Item item)
                            {
                                item.ExternalHighlight = GUI.IsMouseOn(child);
                            }
                        }
                    }
                
                    var highlightedEntities = new List<MapEntity>();
                
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach (Item item in MapEntity.mapEntityList.Where(entity => entity is Item).Cast<Item>())
                    {
                        var wire = item.GetComponent<Wire>();
                        if (wire == null || !wire.IsMouseOn()) { continue; }
                        highlightedEntities.Add(item);
                    }
                
                    MapEntity.UpdateHighlighting(highlightedEntities, true);
                }
            }
            
            hullVolumeFrame.Visible = MapEntity.SelectedList.Any(s => s is Hull);
            hullVolumeFrame.RectTransform.AbsoluteOffset = new Point(Math.Max(showEntitiesPanel.Rect.Right, previouslyUsedPanel.Rect.Right), 0);
            saveAssemblyFrame.Visible = MapEntity.SelectedList.Count > 0;

            var offset = cam.WorldView.Top - cam.ScreenToWorld(new Vector2(0, GameMain.GraphicsHeight - EntityMenu.Rect.Top)).Y;

            // Move the camera towards to the focus point
            if (camTargetFocus != Vector2.Zero)
            {
                if (GameMain.Config.KeyBind(InputType.Up).IsDown() || GameMain.Config.KeyBind(InputType.Down).IsDown() ||
                    GameMain.Config.KeyBind(InputType.Left).IsDown() || GameMain.Config.KeyBind(InputType.Right).IsDown())
                {
                    camTargetFocus = Vector2.Zero;
                }
                else
                {
                    var targetWithOffset = new Vector2(camTargetFocus.X, camTargetFocus.Y - offset / 2);
                    if (Math.Abs(cam.Position.X - targetWithOffset.X) < 1.0f && 
                        Math.Abs(cam.Position.Y - targetWithOffset.Y) < 1.0f)
                    {
                        camTargetFocus = Vector2.Zero;
                    } 
                    else
                    {
                        cam.Position += (targetWithOffset - cam.Position) / cam.MoveSmoothness;
                    }
                }
            }

            if (undoBufferPanel.Visible)
            {
                undoBufferList.Deselect();
            }

            if (GUI.KeyboardDispatcher.Subscriber == null 
                || MapEntity.EditingHUD != null 
                && GUI.KeyboardDispatcher.Subscriber is GUIComponent sub 
                && MapEntity.EditingHUD.Children.Contains(sub))
            {
                if (PlayerInput.IsCtrlDown() && !WiringMode)
                {
                    if (PlayerInput.KeyHit(Keys.Z))
                    {
                        // Ctrl+Shift+Z redos while Ctrl+Z undos
                        if (PlayerInput.IsShiftDown()) { Redo(1); } else { Undo(1); }
                    }
                    
                    // ctrl+Y redo
                    if (PlayerInput.KeyHit(Keys.Y))
                    {
                        Redo(1);
                    }
                }
            }

            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (WiringMode && dummyCharacter != null)
                {
                    if (wiringToolPanel.GetChild<GUIListBox>() is { } listBox)
                    {
                        if (!dummyCharacter.HeldItems.Any(it => it.HasTag("wire")))
                        {
                            listBox.Deselect();
                        }

                        List<Keys> numberKeys = PlayerInput.NumberKeys;
                        if (numberKeys.Find(PlayerInput.KeyHit) is { } key)
                        {
                            // treat 0 as the last key instead of first
                            int index = key == Keys.D0 ? numberKeys.Count : numberKeys.IndexOf(key) - 1;
                            listBox.Select(index, force: false, autoScroll: true, takeKeyBoardFocus: false);
                        }
                    }
                }

                if (PlayerInput.KeyHit(Keys.E) && mode == Mode.Default)
                {
                    if (dummyCharacter != null)
                    {
                        if (dummyCharacter.SelectedConstruction == null)
                        {
                            foreach (var entity in MapEntity.mapEntityList)
                            {
                                if (entity is Item item && entity.IsHighlighted && item.Components.Any(ic => !(ic is ConnectionPanel) && !(ic is Repairable) && ic.GuiFrame != null))
                                {
                                    var container = item.GetComponents<ItemContainer>().ToList();
                                    if (!container.Any() || container.Any(ic => ic?.DrawInventory ?? false))
                                    {
                                        OpenItem(item);
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            CloseItem();
                        }
                    }
                }
                
                // Focus to selection
                if (PlayerInput.KeyHit(Keys.F) && mode == Mode.Default)
                {
                    // content warning: contains coordinate system workarounds
                    var selected = MapEntity.SelectedList;
                    if (selected.Count > 0)
                    {
                        var dRect = selected.First().Rect;
                        var rect = new Rectangle(dRect.Left, dRect.Top, dRect.Width, dRect.Height * -1);
                        if (selected.Count > 1)
                        {
                            // Create one big rect out of our selection
                            selected.Skip(1).ForEach(me =>
                            {
                                var wRect = me.Rect;
                                rect = Rectangle.Union(rect, new Rectangle(wRect.Left, wRect.Top, wRect.Width, wRect.Height * -1));
                            });
                        }
                        camTargetFocus = rect.Center.ToVector2();
                    }
                }
                
                if (GameMain.Config.KeyBind(InputType.ToggleInventory).IsHit() && mode == Mode.Default)
                {
                    toggleEntityMenuButton.OnClicked?.Invoke(toggleEntityMenuButton, toggleEntityMenuButton.UserData);
                }

                if (PlayerInput.KeyHit(Keys.Tab))
                {
                    entityFilterBox.Select();
                }

                if (PlayerInput.IsCtrlDown() && MapEntity.StartMovingPos == Vector2.Zero)
                {
                    cam.MoveCamera((float) deltaTime, allowMove: false);
                    // Save menu
                    if (PlayerInput.KeyHit(Keys.S))
                    {
                        if (PlayerInput.IsShiftDown())
                        {
                            // Quick-save, but only when we've set a custom name for our sub
                            CreateSaveScreen(subNameLabel != null && subNameLabel.Text != TextManager.Get("unspecifiedsubfilename"));
                        }
                        else
                        {
                            // Save menu
                            CreateSaveScreen();
                        }
                    }

                    // Select or deselect everything
                    if (PlayerInput.KeyHit(Keys.A) && mode == Mode.Default)
                    {
                        if (MapEntity.SelectedList.Any())
                        {
                            MapEntity.DeselectAll();
                        }
                        else
                        {
                            var selectables = MapEntity.mapEntityList.Where(entity => entity.SelectableInEditor).ToList();
                            lock (selectables)
                            {
                                selectables.ForEach(MapEntity.AddSelection);
                            }
                        }
                    }

                    // 1-2 keys on the keyboard for switching modes
                    if (PlayerInput.KeyHit(Keys.D1)) { SetMode(Mode.Default); }
                    if (PlayerInput.KeyHit(Keys.D2)) { SetMode(Mode.Wiring); }
                }
                else
                {
                    cam.MoveCamera((float) deltaTime, allowMove: true);
                }
            }
            else
            {
                cam.MoveCamera((float) deltaTime, allowMove: false);
            }

            if (PlayerInput.MidButtonHeld())
            {
                Vector2 moveSpeed = PlayerInput.MouseSpeed * (float)deltaTime * 60.0f / cam.Zoom;
                moveSpeed.X = -moveSpeed.X;
                cam.Position += moveSpeed;
                // break out of trying to focus
                camTargetFocus = Vector2.Zero;
            }

            if (PlayerInput.KeyHit(Keys.Escape) && dummyCharacter != null)
            {
                CloseItem();
            }

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
                        lightComponent.Light.LightSpriteEffect = lightComponent.Item.SpriteEffects;
                    }
                }                
                GameMain.LightManager?.Update((float)deltaTime);
            }

            if (dummyCharacter != null && Entity.FindEntityByID(dummyCharacter.ID) == dummyCharacter)
            {
                if (WiringMode)
                {
                    foreach (MapEntity me in MapEntity.mapEntityList)
                    {
                        me.IsHighlighted = false;
                    }

                    if (dummyCharacter.SelectedConstruction == null)
                    {
                        List<Wire> wires = new List<Wire>();
                        foreach (Item item in Item.ItemList)
                        {
                            var wire = item.GetComponent<Wire>();
                            if (wire != null) wires.Add(wire);
                        }
                        Wire.UpdateEditing(wires);
                    }
                }

                if (!WiringMode)
                {
                    // Move all of our slots on top center of the entity list
                    // We use the slots to open item inventories and we want the position of them to be consisent
                    dummyCharacter.Inventory.visualSlots.ForEach(slot =>
                    {
                        slot.Rect.Y = EntityMenu.Rect.Top;
                        slot.Rect.X = EntityMenu.Rect.X + (EntityMenu.Rect.Width / 2) - (slot.Rect.Width /2);
                    });
                }

                if (dummyCharacter.SelectedConstruction == null || 
                    dummyCharacter.SelectedConstruction.GetComponent<Pickable>() != null)
                {
                    if (WiringMode && PlayerInput.IsShiftDown())
                    {
                        Wire equippedWire = Character.Controlled?.HeldItems.FirstOrDefault(i => i.GetComponent<Wire>() != null)?.GetComponent<Wire>();
                        if (equippedWire != null && equippedWire.GetNodes().Count > 0)
                        {
                            Vector2 lastNode = equippedWire.GetNodes().Last();
                            if (equippedWire.Item.Submarine != null)
                            {
                                lastNode += equippedWire.Item.Submarine.HiddenSubPosition + equippedWire.Item.Submarine.Position;
                            }

                            var (cursorX, cursorY) = dummyCharacter.CursorPosition;

                            bool isHorizontal = Math.Abs(cursorX - lastNode.X) < Math.Abs(cursorY - lastNode.Y);
                            
                            float roundedY = MathUtils.Round(cursorY, Submarine.GridSize.Y / 2.0f);
                            float roundedX = MathUtils.Round(cursorX, Submarine.GridSize.X / 2.0f);

                            dummyCharacter.CursorPosition = isHorizontal 
                                ? new Vector2(lastNode.X, roundedY) 
                                : new Vector2(roundedX, lastNode.Y);
                        }
                    }

                    // Keep teleporting the dummy character to the opened item to make it look like the container didn't go anywhere
                    if (OpenedItem != null)
                    {
                        TeleportDummyCharacter(oldItemPosition);
                    }
                    
                    if (WiringMode && dummyCharacter?.SelectedConstruction == null)
                    {
                        TeleportDummyCharacter(FarseerPhysics.ConvertUnits.ToSimUnits(dummyCharacter.CursorPosition));
                    }
                }

                if (WiringMode)
                {
                    dummyCharacter.ControlLocalPlayer((float)deltaTime, cam, false);
                    dummyCharacter.Control((float)deltaTime, cam);
                }

                cam.TargetPos = Vector2.Zero;
                dummyCharacter.Submarine = Submarine.MainSub;
            }

            // Deposit item from our "infinite stack" into inventory slots
            var inv = dummyCharacter?.SelectedConstruction?.OwnInventory;
            if (inv?.visualSlots != null && !PlayerInput.IsCtrlDown())
            {
                var dragginMouse = MouseDragStart != Vector2.Zero && Vector2.Distance(PlayerInput.MousePosition, MouseDragStart) >= GUI.Scale * 20;
                
                // So we don't accidentally drag inventory items while doing this
                if (DraggedItemPrefab != null) { Inventory.DraggingItems.Clear(); }
                
                switch (DraggedItemPrefab) 
                {
                    // regular item prefabs
                    case ItemPrefab itemPrefab when PlayerInput.PrimaryMouseButtonClicked() || dragginMouse: 
                    {
                        bool spawnedItem = false;
                        for (var i = 0; i < inv.Capacity; i++)
                        {
                            var slot = inv.visualSlots[i];
                            var itemContainer = inv.GetItemAt(i)?.GetComponent<ItemContainer>();
                            
                            // check if the slot is empty or if we can place the item into a container, for example an oxygen tank into a diving suit
                            if (Inventory.IsMouseOnSlot(slot))
                            {
                                var newItem = new Item(itemPrefab, Vector2.Zero, Submarine.MainSub);
                                
                                if (inv.CanBePut(itemPrefab, i))
                                {
                                    bool placedItem = inv.TryPutItem(newItem, i, false, true, dummyCharacter);
                                    spawnedItem |= placedItem;
                                    
                                    if (!placedItem)
                                    {
                                        newItem.Remove();
                                    }
                                }
                                else if (itemContainer != null && itemContainer.Inventory.CanBePut(itemPrefab))
                                {
                                    bool placedItem = itemContainer.Inventory.TryPutItem(newItem, dummyCharacter);
                                    spawnedItem |= placedItem;
                                    
                                    // try to place the item into the inventory of the item we are hovering over
                                    if (!placedItem)
                                    {
                                        newItem.Remove();
                                    }
                                    else
                                    {
                                        slot.ShowBorderHighlight(GUI.Style.Green, 0.1f, 0.4f);
                                    }
                                }
                                else
                                {
                                    newItem.Remove();
                                    slot.ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.4f);
                                }

                                if (!newItem.Removed)
                                {
                                    BulkItemBufferInUse = ItemAddMutex;
                                    BulkItemBuffer.Add(new AddOrDeleteCommand(new List<MapEntity> { newItem }, false));
                                }

                                if (!dragginMouse)
                                {
                                    SoundPlayer.PlayUISound(spawnedItem ? GUISoundType.PickItem : GUISoundType.PickItemFail);
                                }
                            }
                        }
                        break;
                    }
                    // item assemblies
                    case ItemAssemblyPrefab assemblyPrefab when PlayerInput.PrimaryMouseButtonClicked():
                    {
                        bool spawnedItems = false;
                        for (var i = 0; i < inv.visualSlots.Length; i++)
                        {
                            var slot = inv.visualSlots[i];
                            var item = inv?.GetItemAt(i);
                            var itemContainer = item?.GetComponent<ItemContainer>();
                            if (item == null && Inventory.IsMouseOnSlot(slot))
                            {
                                // load the items
                                var itemInstance = LoadItemAssemblyInventorySafe(assemblyPrefab);
                                
                                // counter for items that failed so we so we known that slot remained empty
                                var failedCount = 0;
                                
                                for (var j = 0; j < itemInstance.Count(); j++)
                                {
                                    var newItem = itemInstance[j];
                                    var newSpot = i + j - failedCount;
                                    
                                    // try to find a valid slot to put the items
                                    while (inv.visualSlots.Length > newSpot) 
                                    {
                                        if (inv.GetItemAt(newSpot) == null) { break; }
                                        newSpot++;
                                    }
                                    
                                    // valid slot found
                                    if (inv.visualSlots.Length > newSpot)
                                    {
                                        var placedItem = inv.TryPutItem(newItem, newSpot, false, true, dummyCharacter);
                                        spawnedItems |= placedItem;
                                        
                                        if (!placedItem)
                                        {
                                            failedCount++;
                                            // delete the included items too so we don't get a popup asking if we want to keep them
                                            newItem?.OwnInventory?.DeleteAllItems();
                                            newItem.Remove();
                                        }
                                    }
                                    else
                                    {
                                        var placedItem = inv.TryPutItem(newItem, dummyCharacter);
                                        spawnedItems |= placedItem;
                                        
                                        // if our while loop didn't find a valid slot then let the inventory decide where to put it as a last resort
                                        if (!placedItem)
                                        {
                                            // delete the included items too so we don't get a popup asking if we want to keep them
                                            newItem?.OwnInventory?.DeleteAllItems();
                                            newItem.Remove();
                                        }
                                    }
                                }

                                List<MapEntity> placedEntities = itemInstance.Where(it => !it.Removed).Cast<MapEntity>().ToList();
                                if (placedEntities.Any())
                                {
                                    BulkItemBufferInUse = ItemAddMutex;
                                    BulkItemBuffer.Add(new AddOrDeleteCommand(placedEntities, false));
                                }
                            }
                        }

                        SoundPlayer.PlayUISound(spawnedItems ? GUISoundType.PickItem : GUISoundType.PickItemFail);
                        break;
                    }
                }
            }

            if (PlayerInput.PrimaryMouseButtonReleased() && BulkItemBufferInUse != null)
            {
                CommitBulkItemBuffer();
            }

            if (SerializableEntityEditor.PropertyChangesActive && (SerializableEntityEditor.NextCommandPush < DateTime.Now || MapEntity.EditingHUD == null))
            {
                SerializableEntityEditor.CommitCommandBuffer();
            }

            // Update our mouse dragging state so we can easily slide thru slots while holding the mouse button down to place lots of items
            if (PlayerInput.PrimaryMouseButtonHeld())
            {
                if (MouseDragStart == Vector2.Zero)
                {
                    MouseDragStart = PlayerInput.MousePosition;
                }
            }
            else
            {
                MouseDragStart = Vector2.Zero;
            }

            if (!saveAssemblyFrame.Rect.Contains(PlayerInput.MousePosition) && dummyCharacter?.SelectedConstruction == null && !WiringMode && GUI.MouseOn == null)
            {
                MapEntity.UpdateSelecting(cam);
            }

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                MeasurePositionStart = Vector2.Zero;
            }

            if (PlayerInput.KeyDown(Keys.LeftAlt) || PlayerInput.KeyDown(Keys.RightAlt))
            {
                if (PlayerInput.PrimaryMouseButtonDown())
                {
                    MeasurePositionStart = cam.ScreenToWorld(PlayerInput.MousePosition);
                }
            }
            
            if (!WiringMode)
            {
                bool shouldCloseHud = dummyCharacter?.SelectedConstruction != null && HUD.CloseHUD(dummyCharacter.SelectedConstruction.Rect) && DraggedItemPrefab == null;
                
                if (MapEntityPrefab.Selected != null && GUI.MouseOn == null)
                {
                    MapEntityPrefab.Selected.UpdatePlacing(cam);
                }
                else
                {
                    if (PlayerInput.SecondaryMouseButtonClicked() && !shouldCloseHud)
                    {
                        if (GUI.IsMouseOn(entityFilterBox))
                        {
                            ClearFilter();
                        }
                        else
                        {
                            if (dummyCharacter?.SelectedConstruction == null)
                            {
                                CreateContextMenu();                    
                            }
                            DraggedItemPrefab = null;
                        }
                    }

                    if (shouldCloseHud)
                    {
                        CloseItem();
                    }
                }                
                MapEntity.UpdateEditor(cam);
            }

            entityMenuOpenState = entityMenuOpen && !WiringMode ? 
                (float)Math.Min(entityMenuOpenState + deltaTime * 5.0f, 1.0f) :
                (float)Math.Max(entityMenuOpenState - deltaTime * 5.0f, 0.0f);

            EntityMenu.RectTransform.ScreenSpaceOffset = Vector2.Lerp(new Vector2(0.0f, EntityMenu.Rect.Height - 10), Vector2.Zero, entityMenuOpenState).ToPoint();

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

            if (dummyCharacter != null)
            {
                dummyCharacter.AnimController.FindHull(dummyCharacter.CursorWorldPosition, false);

                foreach (Item item in dummyCharacter.Inventory.AllItems)
                {
                    item.SetTransform(dummyCharacter.SimPosition, 0.0f);
                    item.UpdateTransform();
                    item.SetTransform(item.body.SimPosition, 0.0f);

                    //wires need to be updated for the last node to follow the player during rewiring
                    Wire wire = item.GetComponent<Wire>();
                    wire?.Update((float)deltaTime, cam);
                }

                if (dummyCharacter.SelectedConstruction != null)
                {
                    if (MapEntity.SelectedList.Contains(dummyCharacter.SelectedConstruction) || WiringMode)
                    {
                        dummyCharacter.SelectedConstruction?.UpdateHUD(cam, dummyCharacter, (float)deltaTime);
                    }
                    else
                    {
                        // We somehow managed to unfocus the item, close it so our framerate doesn't go to 5 because the
                        // UpdateHUD() method keeps re-creating the editing HUD
                        CloseItem();
                    }
                }
                else if (MapEntity.SelectedList.Count == 1 && WiringMode)
                {
                    (MapEntity.SelectedList[0] as Item)?.UpdateHUD(cam, dummyCharacter, (float)deltaTime);
                }

                CharacterHUD.Update((float)deltaTime, dummyCharacter, cam);
            }
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform();
            if (lightingEnabled)
            {
                GameMain.LightManager.RenderLightMap(graphics, spriteBatch, cam);
            }

            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.UpdateTransform();
            }

            graphics.Clear(backgroundColor);

            ImageManager.Draw(spriteBatch, cam);

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: cam.Transform);

            if (GameMain.DebugDraw)
            {
                GUI.DrawLine(spriteBatch, new Vector2(Submarine.MainSub.HiddenSubPosition.X, -cam.WorldView.Y), new Vector2(Submarine.MainSub.HiddenSubPosition.X, -(cam.WorldView.Y - cam.WorldView.Height)), Color.White * 0.5f, 1.0f, (int)(2.0f / cam.Zoom));
                GUI.DrawLine(spriteBatch, new Vector2(cam.WorldView.X, -Submarine.MainSub.HiddenSubPosition.Y), new Vector2(cam.WorldView.Right, -Submarine.MainSub.HiddenSubPosition.Y), Color.White * 0.5f, 1.0f, (int)(2.0f / cam.Zoom));
            }
            Submarine.DrawBack(spriteBatch, true, e => 
                e is Structure s && 
                !IsSubcategoryHidden(e.prefab?.Subcategory) && 
                (e.SpriteDepth >= 0.9f || s.Prefab.BackgroundSprite != null));
            Submarine.DrawPaintedColors(spriteBatch, true);
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: cam.Transform);
            
            // When we "open" a wearable item with inventory it won't get rendered because the dummy character is invisible
            // So we are drawing a clone of it on the same position
            if (OpenedItem?.GetComponent<Wearable>() != null)
            {
                OpenedItem.Sprite.Draw(spriteBatch, new Vector2(OpenedItem.DrawPosition.X, -(OpenedItem.DrawPosition.Y)), 
                                       scale: OpenedItem.Scale, color: OpenedItem.SpriteColor, depth: OpenedItem.SpriteDepth);
                GUI.DrawRectangle(spriteBatch,
                                  new Vector2(OpenedItem.WorldRect.X, -OpenedItem.WorldRect.Y),
                                  new Vector2(OpenedItem.Rect.Width, OpenedItem.Rect.Height),
                                  Color.White, false, 0, (int)Math.Max(2.0f / cam.Zoom, 1.0f));
            }
            
            Submarine.DrawBack(spriteBatch, true, e => 
                (!(e is Structure) || e.SpriteDepth < 0.9f) &&
                !IsSubcategoryHidden(e.prefab?.Subcategory));
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: cam.Transform);
            Submarine.DrawDamageable(spriteBatch, null, editing: true, e => !IsSubcategoryHidden(e.prefab?.Subcategory));
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: cam.Transform);
            Submarine.DrawFront(spriteBatch, editing: true, e => !IsSubcategoryHidden(e.prefab?.Subcategory));
            if (!WiringMode && !IsMouseOnEditorGUI())
            {
                MapEntityPrefab.Selected?.DrawPlacing(spriteBatch, cam);
                MapEntity.DrawSelecting(spriteBatch, cam);
            }
            if (dummyCharacter != null && WiringMode)
            {
                foreach (Item heldItem in dummyCharacter.HeldItems)
                {
                    heldItem.Draw(spriteBatch, editing: false, back: true);
                }
            }

            DrawGrid(spriteBatch);
            spriteBatch.End();

            ImageManager.DrawEditing(spriteBatch, cam);

            if (GameMain.LightManager.LightingEnabled && lightingEnabled)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, Lights.CustomBlendStates.Multiplicative, null, DepthStencilState.None);
                spriteBatch.Draw(GameMain.LightManager.LightMap, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                spriteBatch.End();
            }

            //-------------------- HUD -----------------------------

            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState);

            if (Submarine.MainSub != null && cam.Zoom < 5f)
            {
                Vector2 position = Submarine.MainSub.SubBody != null ? Submarine.MainSub.WorldPosition : Submarine.MainSub.HiddenSubPosition;

                GUI.DrawIndicator(
                    spriteBatch, position, cam,
                    cam.WorldView.Width,
                    GUI.SubmarineIcon, Color.LightBlue * 0.5f);
            }

            var notificationIcon = GUI.Style.GetComponentStyle("GUINotificationButton");
            var tooltipStyle = GUI.Style.GetComponentStyle("GUIToolTip");
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.linkedTo.Count == 2 && gap.linkedTo[0] == gap.linkedTo[1])
                {
                    Vector2 screenPos = Cam.WorldToScreen(gap.WorldPosition);
                    Rectangle rect = new Rectangle(screenPos.ToPoint() - new Point(20), new Point(40));
                    tooltipStyle.Sprites[GUIComponent.ComponentState.None][0].Draw(spriteBatch, rect, Color.White);
                    notificationIcon.Sprites[GUIComponent.ComponentState.None][0].Draw(spriteBatch, rect, GUI.Style.Orange);
                    if (Vector2.Distance(PlayerInput.MousePosition, screenPos) < 30 * Cam.Zoom)
                    {
                        GUIComponent.DrawToolTip(spriteBatch, TextManager.Get("gapinsidehullwarning"), new Rectangle(screenPos.ToPoint(), new Point(10)));
                    }
                }
            }
            
            if (dummyCharacter != null)
            {
                if (WiringMode)
                {
                    dummyCharacter.DrawHUD(spriteBatch, cam, false);
                    wiringToolPanel.DrawManually(spriteBatch);
                }
            }
            MapEntity.DrawEditor(spriteBatch, cam);

            GUI.Draw(Cam, spriteBatch);

            if (MeasurePositionStart != Vector2.Zero)
            {
                Vector2 startPos = MeasurePositionStart;
                Vector2 mouseWorldPos = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (PlayerInput.IsShiftDown())
                {
                    startPos = RoundToGrid(startPos);
                    mouseWorldPos = RoundToGrid(mouseWorldPos);

                    static Vector2 RoundToGrid(Vector2 position)
                    {
                        position.X = (float) Math.Round(position.X / Submarine.GridSize.X) * Submarine.GridSize.X;
                        position.Y = (float) Math.Round(position.Y / Submarine.GridSize.Y) * Submarine.GridSize.Y;
                        return position;
                    }
                }

                GUI.DrawLine(spriteBatch, cam.WorldToScreen(startPos), cam.WorldToScreen(mouseWorldPos), GUI.Style.Green, width: 4);

                decimal realWorldDistance = decimal.Round((decimal) (Vector2.Distance(startPos, mouseWorldPos) * Physics.DisplayToRealWorldRatio), 2);

                Vector2 offset = new Vector2(GUI.IntScale(24));
                GUI.DrawString(spriteBatch, PlayerInput.MousePosition + offset, $"{realWorldDistance}m", GUI.Style.TextColor, font: GUI.SubHeadingFont, backgroundColor: Color.Black, backgroundPadding: 4);
            }
                                              
            spriteBatch.End();
        }

        private void CreateImage(int width, int height, System.IO.Stream stream)
        {
            MapEntity.SelectedList.Clear();
            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                me.IsHighlighted = false;
            }

            var prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;

            Rectangle subDimensions = Submarine.MainSub.CalculateDimensions(false);
            Vector2 viewPos = subDimensions.Center.ToVector2();
            float scale = Math.Min(width / (float)subDimensions.Width, height / (float)subDimensions.Height);

            var viewMatrix = Matrix.CreateTranslation(new Vector3(width / 2.0f, height / 2.0f, 0));
            var transform = Matrix.CreateTranslation(
                new Vector3(-viewPos.X, viewPos.Y, 0)) *
                Matrix.CreateScale(new Vector3(scale, scale, 1)) *
                viewMatrix;

            using (RenderTarget2D rt = new RenderTarget2D(
                 GameMain.Instance.GraphicsDevice,
                 width, height, false, SurfaceFormat.Color, DepthFormat.None))
            using (SpriteBatch spriteBatch = new SpriteBatch(GameMain.Instance.GraphicsDevice))
            {
                GameMain.Instance.GraphicsDevice.SetRenderTarget(rt);
                GameMain.Instance.GraphicsDevice.Clear(new Color(8, 13, 19));

                spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, null, null, null, null, transform);
                Submarine.Draw(spriteBatch);
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

        private static readonly Color gridBaseColor = Color.White * 0.1f;

        private void DrawGrid(SpriteBatch spriteBatch)
        {
            // don't render at high zoom levels because it would just turn the screen white
            if (cam.Zoom < 0.5f || !ShouldDrawGrid) { return; }

            var (gridX, gridY) = Submarine.GridSize;

            int scale = Math.Max(1, GUI.IntScale(1));
            float zoom = cam.Zoom / 2f; // Don't ask
            float lineThickness = Math.Max(1, scale / zoom);

            Color gridColor = gridBaseColor;
            if (cam.Zoom < 1.0f)
            {
                // fade the grid when zooming out
                gridColor *= Math.Max(0, (cam.Zoom - 0.5f) * 2f);
            }

            Rectangle camRect = cam.WorldView;

            for (float x = snapX(camRect.X); x < snapX(camRect.X + camRect.Width) + gridX; x += gridX)
            {
                spriteBatch.DrawLine(new Vector2(x, -camRect.Y), new Vector2(x, -(camRect.Y - camRect.Height)), gridColor, thickness: lineThickness);
            }

            for (float y = snapY(camRect.Y); y >= snapY(camRect.Y - camRect.Height) - gridY; y -= Submarine.GridSize.Y)
            {
                spriteBatch.DrawLine(new Vector2(camRect.X, -y), new Vector2(camRect.Right, -y), gridColor, thickness: lineThickness);
            }

            float snapX(int x) => (float) Math.Floor(x / gridX) * gridX;
            float snapY(int y) => (float) Math.Ceiling(y / gridY) * gridY;
        }

        public void SaveScreenShot(int width, int height, string filePath)
        {
            System.IO.Stream stream = File.OpenWrite(filePath);
            CreateImage(width, height, stream);
            stream.Dispose();
        }

        public bool IsSubcategoryHidden(string subcategory)
        {
            if (string.IsNullOrEmpty(subcategory) || !hiddenSubCategories.ContainsKey(subcategory))
            {
                return false;
            }
            return hiddenSubCategories[subcategory];
        }

        public static bool IsSubEditor() => Screen.Selected is SubEditorScreen && !Submarine.Unloading;
        public static bool IsWiringMode() => Screen.Selected == GameMain.SubEditorScreen && GameMain.SubEditorScreen.WiringMode && !Submarine.Unloading;

    }
}
