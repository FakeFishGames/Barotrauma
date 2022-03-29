using Barotrauma.Extensions;
using Barotrauma.Lights;
using Barotrauma.RuinGeneration;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
#if DEBUG
using System.IO;
using System.Xml;
#else
using Barotrauma.IO;
#endif

namespace Barotrauma
{
    class LevelEditorScreen : EditorScreen
    {
        public override Camera Cam { get; }

        private readonly GUIFrame leftPanel, rightPanel, bottomPanel, topPanel;
        
        private LevelGenerationParams selectedParams;
        private LevelObjectPrefab selectedLevelObject;

        private readonly GUIListBox paramsList, ruinParamsList, caveParamsList, outpostParamsList, levelObjectList;
        private readonly GUIListBox editorContainer;

        private readonly GUIButton spriteEditDoneButton;

        private readonly GUITextBox seedBox;

        private readonly GUITickBox lightingEnabled, cursorLightEnabled, mirrorLevel;

        private Sprite editingSprite;

        private LightSource pointerLightSource;

        private readonly Color[] tunnelDebugColors = new Color[] { Color.White, Color.Cyan, Color.LightGreen, Color.Red, Color.LightYellow, Color.LightSeaGreen };

        public LevelEditorScreen()
        {
            Cam = new Camera()
            {
                MinZoom = 0.01f,
                MaxZoom = 1.0f
            };

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.125f, 0.8f), Frame.RectTransform) { MinSize = new Point(150, 0) });
            var paddedLeftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), leftPanel.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            paramsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), paddedLeftPanel.RectTransform));
            paramsList.OnSelected += (GUIComponent component, object obj) =>
            {
                selectedParams = obj as LevelGenerationParams;
                editorContainer.ClearChildren();
                SortLevelObjectsList(selectedParams);
                new SerializableEntityEditor(editorContainer.Content.RectTransform, selectedParams, false, true, elementHeight: 20);
                return true;
            };

            var ruinTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedLeftPanel.RectTransform), TextManager.Get("leveleditor.ruinparams"), font: GUIStyle.SubHeadingFont);

            ruinParamsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedLeftPanel.RectTransform));
            ruinParamsList.OnSelected += (GUIComponent component, object obj) =>
            {
                CreateOutpostGenerationParamsEditor(obj as OutpostGenerationParams);
                return true;
            };

            var caveTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedLeftPanel.RectTransform), TextManager.Get("leveleditor.caveparams"), font: GUIStyle.SubHeadingFont);

            caveParamsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedLeftPanel.RectTransform));
            caveParamsList.OnSelected += (GUIComponent component, object obj) =>
            {
                CreateCaveParamsEditor(obj as CaveGenerationParams);
                return true;
            };

            var outpostTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedLeftPanel.RectTransform), TextManager.Get("leveleditor.outpostparams"), font: GUIStyle.SubHeadingFont);
            GUITextBlock.AutoScaleAndNormalize(ruinTitle, caveTitle, outpostTitle);

            outpostParamsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.2f), paddedLeftPanel.RectTransform));
            outpostParamsList.OnSelected += (GUIComponent component, object obj) =>
            {
                CreateOutpostGenerationParamsEditor(obj as OutpostGenerationParams);
                return true;
            };

            var createLevelObjButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedLeftPanel.RectTransform),
                TextManager.Get("leveleditor.createlevelobj"))
            {
                OnClicked = (btn, obj) =>
                {
                    Wizard.Instance.Create();
                    return true;
                }
            };
            GUITextBlock.AutoScaleAndNormalize(createLevelObjButton.TextBlock);            

            lightingEnabled = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.025f), paddedLeftPanel.RectTransform),
                TextManager.Get("leveleditor.lightingenabled"));

            cursorLightEnabled = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.025f), paddedLeftPanel.RectTransform),
                TextManager.Get("leveleditor.cursorlightenabled"));

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedLeftPanel.RectTransform),
                TextManager.Get("leveleditor.reloadtextures"))
            {
                OnClicked = (btn, obj) =>
                {
                    Level.Loaded?.ReloadTextures();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedLeftPanel.RectTransform),
                TextManager.Get("editor.saveall"))
            {
                OnClicked = (btn, obj) =>
                {
                    SerializeAll();
                    return true;
                }
            };

            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), Frame.RectTransform, Anchor.TopRight) { MinSize = new Point(450, 0) });
            var paddedRightPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), rightPanel.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            editorContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedRightPanel.RectTransform));

            var seedContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), paddedRightPanel.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            Vector2 randomizeButtonRelativeSize = GetRandomizeButtonRelativeSize();
            Vector2 elementRelativeSize = GetSeedElementRelativeSize();
            var seedLabel = new GUITextBlock(new RectTransform(elementRelativeSize, seedContainer.RectTransform), TextManager.Get("leveleditor.levelseed"));
            seedBox = new GUITextBox(new RectTransform(elementRelativeSize, seedContainer.RectTransform), GetLevelSeed());
            var seedButton = new GUIButton(new RectTransform(randomizeButtonRelativeSize, seedContainer.RectTransform), style: "RandomizeButton")
            {
                OnClicked = (button, userData) =>
                {
                    if(seedBox == null) { return false; }
                    seedBox.Text = GetLevelSeed();
                    return true;
                }
            };
            seedContainer.RectTransform.SizeChanged += () =>
            {
                Vector2 randomizeButtonRelativeSize = GetRandomizeButtonRelativeSize();
                Vector2 elementRelativeSize = GetSeedElementRelativeSize();
                seedLabel.RectTransform.RelativeSize = elementRelativeSize;
                seedBox.RectTransform.RelativeSize = elementRelativeSize;
                seedButton.RectTransform.RelativeSize = randomizeButtonRelativeSize;
            };
            Vector2 GetRandomizeButtonRelativeSize() => 0.2f * seedContainer.Rect.Width > seedContainer.Rect.Height ?
                new Vector2(Math.Min((float)seedContainer.Rect.Height / seedContainer.Rect.Width, 0.2f), 1.0f) :
                new Vector2(0.15f, Math.Min((0.2f * seedContainer.Rect.Width) / seedContainer.Rect.Height, 1.0f));
            Vector2 GetSeedElementRelativeSize() => new Vector2(0.5f * (1.0f - randomizeButtonRelativeSize.X), 1.0f);
            static string GetLevelSeed() => ToolBox.RandomSeed(8);

            mirrorLevel = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.02f), paddedRightPanel.RectTransform), TextManager.Get("mirrorentityx"));

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedRightPanel.RectTransform),
                TextManager.Get("leveleditor.generate"))
            {
                OnClicked = (btn, obj) =>
                {
                    bool wasLevelLoaded = Level.Loaded != null;
                    Submarine.Unload();
                    GameMain.LightManager.ClearLights();
                    LevelData levelData = LevelData.CreateRandom(seedBox.Text, generationParams: selectedParams);
                    levelData.ForceOutpostGenerationParams = outpostParamsList.SelectedData as OutpostGenerationParams;
                    Level.Generate(levelData, mirror: mirrorLevel.Selected);
                    GameMain.LightManager.AddLight(pointerLightSource);
                    if (!wasLevelLoaded || Cam.Position.X < 0 || Cam.Position.Y < 0 || Cam.Position.Y > Level.Loaded.Size.X || Cam.Position.Y > Level.Loaded.Size.Y)
                    {
                        Cam.Position = new Vector2(Level.Loaded.Size.X / 2, Level.Loaded.Size.Y / 2);
                    }
                    foreach (GUITextBlock param in paramsList.Content.Children)
                    {
                        param.TextColor = param.UserData == selectedParams ? GUIStyle.Green : param.Style.TextColor;
                    }
                    seedBox.Deselect();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedRightPanel.RectTransform),
                TextManager.Get("leveleditor.test"))
            {
                OnClicked = (btn, obj) =>
                {
                    if (Level.Loaded?.LevelData == null) { return false; }

                    GameMain.GameScreen.Select();

                    var currEntities = Entity.GetEntities().ToList();
                    if (Submarine.MainSub != null)
                    {
                        var toRemove = Entity.GetEntities().Where(e => e.Submarine == Submarine.MainSub).ToList();
                        foreach (Entity ent in toRemove)
                        {
                            ent.Remove();
                        }
                        Submarine.MainSub.Remove();
                    }

                    var nonPlayerFiles = ContentPackageManager.EnabledPackages.All.SelectMany(p => p
                        .GetFiles<BaseSubFile>()
                        .Where(f => !(f is SubmarineFile))).ToArray();
                    SubmarineInfo subInfo = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == GameSettings.CurrentConfig.QuickStartSub);
                    subInfo ??= SubmarineInfo.SavedSubmarines.GetRandomUnsynced(s =>
                        s.IsPlayer && !s.HasTag(SubmarineTag.Shuttle) &&
                        !nonPlayerFiles.Any(f => f.Path == s.FilePath));
                    GameSession gameSession = new GameSession(subInfo, "", GameModePreset.TestMode, CampaignSettings.Empty, null);
                    gameSession.StartRound(Level.Loaded.LevelData);
                    (gameSession.GameMode as TestGameMode).OnRoundEnd = () =>
                    {
                        GameMain.LevelEditorScreen.Select();
                        Submarine.MainSub.Remove();

                        var toRemove = Entity.GetEntities().Where(e => !currEntities.Contains(e)).ToList();
                        foreach (Entity ent in toRemove)
                        {
                            ent.Remove();
                        }

                        Submarine.MainSub = null;
                    };

                    GameMain.GameSession = gameSession;

                    return true;
                }
            };

            bottomPanel = new GUIFrame(new RectTransform(new Vector2(0.75f, 0.22f), Frame.RectTransform, Anchor.BottomLeft)
            { MaxSize = new Point(GameMain.GraphicsWidth - rightPanel.Rect.Width, 1000) }, style: "GUIFrameBottom");

            levelObjectList = new GUIListBox(new RectTransform(new Vector2(0.99f, 0.85f), bottomPanel.RectTransform, Anchor.Center))
            {
                UseGridLayout = true
            };
            levelObjectList.OnSelected += (GUIComponent component, object obj) =>
            {
                selectedLevelObject = obj as LevelObjectPrefab;
                CreateLevelObjectEditor(selectedLevelObject);
                return true;
            };

            spriteEditDoneButton = new GUIButton(new RectTransform(new Point(200, 30), anchor: Anchor.BottomRight) { AbsoluteOffset = new Point(20, 20) },
                TextManager.Get("leveleditor.spriteeditdone"))
            {
                OnClicked = (btn, userdata) =>
                {
                    editingSprite = null;
                    return true;
                }
            };

            topPanel = new GUIFrame(new RectTransform(new Point(400, 100), GUI.Canvas)
            { RelativeOffset = new Vector2(leftPanel.RectTransform.RelativeSize.X * 2, 0.0f) }, style: "GUIFrameTop");
        }

        public override void Select()
        {
            base.Select();

            GUI.PreventPauseMenuToggle = false;
            pointerLightSource = new LightSource(Vector2.Zero, 1000.0f, Color.White, submarine: null);
            GameMain.LightManager.AddLight(pointerLightSource);
            topPanel.ClearChildren();
            new SerializableEntityEditor(topPanel.RectTransform, pointerLightSource.LightSourceParams, false, true);

            editingSprite = null;
            UpdateParamsList();
            UpdateRuinParamsList();
            UpdateCaveParamsList();
            UpdateOutpostParamsList();
            UpdateLevelObjectsList();
        }

        protected override void DeselectEditorSpecific()
        {
            pointerLightSource?.Remove();
            pointerLightSource = null;
        }

        private void UpdateParamsList()
        {
            editorContainer.ClearChildren();
            paramsList.Content.ClearChildren();

            foreach (LevelGenerationParams genParams in LevelGenerationParams.LevelParams)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paramsList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    genParams.Identifier.Value)
                {
                    Padding = Vector4.Zero,
                    UserData = genParams
                };
            }
        }

        private void UpdateCaveParamsList()
        {
            editorContainer.ClearChildren();
            caveParamsList.Content.ClearChildren();

            foreach (CaveGenerationParams genParams in CaveGenerationParams.CaveParams)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), caveParamsList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    genParams.Name)
                {
                    Padding = Vector4.Zero,
                    UserData = genParams
                };
            }
        }

        private void UpdateRuinParamsList()
        {
            editorContainer.ClearChildren();
            ruinParamsList.Content.ClearChildren();

            foreach (RuinGenerationParams genParams in RuinGenerationParams.RuinParams)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), ruinParamsList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    genParams.Name)
                {
                    Padding = Vector4.Zero,
                    UserData = genParams
                };
            }
        }

        private void UpdateOutpostParamsList()
        {
            editorContainer.ClearChildren();
            outpostParamsList.Content.ClearChildren();

            foreach (OutpostGenerationParams genParams in OutpostGenerationParams.OutpostParams)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), outpostParamsList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    genParams.Name)
                {
                    Padding = Vector4.Zero,
                    UserData = genParams
                };
            }
        }

        private void UpdateLevelObjectsList()
        {
            editorContainer.ClearChildren();
            levelObjectList.Content.ClearChildren();

            int objectsPerRow = (int)Math.Ceiling(levelObjectList.Content.Rect.Width / Math.Max(100 * GUI.Scale, 100));
            float relWidth = 1.0f / objectsPerRow;

            foreach (LevelObjectPrefab levelObjPrefab in LevelObjectPrefab.Prefabs)
            {
                var frame = new GUIFrame(new RectTransform(
                    new Vector2(relWidth, relWidth * ((float)levelObjectList.Content.Rect.Width / levelObjectList.Content.Rect.Height)), 
                    levelObjectList.Content.RectTransform) { MinSize = new Point(0, 60) }, style: "ListBoxElementSquare")
                {
                    UserData = levelObjPrefab
                };
                var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), style: null);

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform, Anchor.BottomCenter),
                    text: ToolBox.LimitString(levelObjPrefab.Name, GUIStyle.SmallFont, paddedFrame.Rect.Width), textAlignment: Alignment.Center, font: GUIStyle.SmallFont)
                {
                    CanBeFocused = false,
                    ToolTip = levelObjPrefab.Name
                };

                Sprite sprite = levelObjPrefab.Sprites.FirstOrDefault() ?? levelObjPrefab.DeformableSprite?.Sprite;
                new GUIImage(new RectTransform(new Point(paddedFrame.Rect.Height, paddedFrame.Rect.Height - textBlock.Rect.Height),
                    paddedFrame.RectTransform, Anchor.TopCenter), sprite, scaleToFit: true)
                {
                    LoadAsynchronously = true,
                    CanBeFocused = false
                };
            }
        }

        private void CreateCaveParamsEditor(CaveGenerationParams caveGenerationParams)
        {
            editorContainer.ClearChildren();
            var editor = new SerializableEntityEditor(editorContainer.Content.RectTransform, caveGenerationParams, false, true, elementHeight: 20);

            if (selectedParams != null)
            {
                var commonnessContainer = new GUILayoutGroup(new RectTransform(new Point(editor.Rect.Width, 70)) { IsFixedSize = true },
                    isHorizontal: false, childAnchor: Anchor.TopCenter)
                {
                    AbsoluteSpacing = 5,
                    Stretch = true
                };
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), commonnessContainer.RectTransform),
                    TextManager.GetWithVariable("leveleditor.levelobjcommonness", "[leveltype]", selectedParams.Identifier.Value), textAlignment: Alignment.Center);
                new GUINumberInput(new RectTransform(new Vector2(0.5f, 0.4f), commonnessContainer.RectTransform), GUINumberInput.NumberType.Float)
                {
                    MinValueFloat = 0,
                    MaxValueFloat = 100,
                    FloatValue = caveGenerationParams.GetCommonness(selectedParams, abyss: false),
                    OnValueChanged = (numberInput) =>
                    {
                        caveGenerationParams.OverrideCommonness[selectedParams.Identifier] = numberInput.FloatValue;
                    }
                };
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), commonnessContainer.RectTransform), style: null);
                editor.AddCustomContent(commonnessContainer, 1);
            }
        }

        private void CreateOutpostGenerationParamsEditor(OutpostGenerationParams outpostGenerationParams)
        {
            editorContainer.ClearChildren();
            var outpostParamsEditor = new SerializableEntityEditor(editorContainer.Content.RectTransform, outpostGenerationParams, false, true, elementHeight: 20);

            // location type -------------------------

            var locationTypeGroup = new GUILayoutGroup(new RectTransform(new Point(editorContainer.Content.Rect.Width, 20)), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), locationTypeGroup.RectTransform), TextManager.Get("outpostmoduleallowedlocationtypes"), textAlignment: Alignment.CenterLeft);
            HashSet<Identifier> availableLocationTypes = new HashSet<Identifier> { "any".ToIdentifier() };
            foreach (LocationType locationType in LocationType.Prefabs) { availableLocationTypes.Add(locationType.Identifier); }

            var locationTypeDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), locationTypeGroup.RectTransform),
                text: LocalizedString.Join(", ", outpostGenerationParams.AllowedLocationTypes.Select(lt => TextManager.Capitalize(lt.Value)) ?? ((LocalizedString)"any").ToEnumerable()), selectMultiple: true);
            foreach (Identifier locationType in availableLocationTypes)
            {
                locationTypeDropDown.AddItem(TextManager.Capitalize(locationType.Value), locationType);
                if (outpostGenerationParams.AllowedLocationTypes.Contains(locationType))
                {
                    locationTypeDropDown.SelectItem(locationType);
                }
            }
            if (!outpostGenerationParams.AllowedLocationTypes.Any())
            {
                locationTypeDropDown.SelectItem("any");
            }

            locationTypeDropDown.OnSelected += (_, __) =>
            {
                outpostGenerationParams.SetAllowedLocationTypes(locationTypeDropDown.SelectedDataMultiple.Cast<Identifier>());
                locationTypeDropDown.Text = ToolBox.LimitString(locationTypeDropDown.Text, locationTypeDropDown.Font, locationTypeDropDown.Rect.Width);
                return true;
            };
            locationTypeGroup.RectTransform.MinSize = new Point(locationTypeGroup.Rect.Width, locationTypeGroup.RectTransform.Children.Max(c => c.MinSize.Y));

            outpostParamsEditor.AddCustomContent(locationTypeGroup, 100);

            // module count -------------------------

            var moduleLabel = new GUITextBlock(new RectTransform(new Point(editorContainer.Content.Rect.Width, (int)(70 * GUI.Scale))), TextManager.Get("submarinetype.outpostmodules"), font: GUIStyle.SubHeadingFont);
            outpostParamsEditor.AddCustomContent(moduleLabel, 100);

            foreach (KeyValuePair<Identifier, int> moduleCount in outpostGenerationParams.ModuleCounts)
            {
                var moduleCountGroup = new GUILayoutGroup(new RectTransform(new Point(editorContainer.Content.Rect.Width, (int)(25 * GUI.Scale))), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), moduleCountGroup.RectTransform), TextManager.Capitalize(moduleCount.Key.Value), textAlignment: Alignment.CenterLeft);
                new GUINumberInput(new RectTransform(new Vector2(0.5f, 1f), moduleCountGroup.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = 100,
                    IntValue = moduleCount.Value,
                    OnValueChanged = (numInput) =>
                    {
                        outpostGenerationParams.SetModuleCount(moduleCount.Key, numInput.IntValue);
                        if (numInput.IntValue == 0)
                        {
                            outpostParamsList.Select(outpostParamsList.SelectedData);
                        }
                    }
                };
                moduleCountGroup.RectTransform.MinSize = new Point(moduleCountGroup.Rect.Width, moduleCountGroup.RectTransform.Children.Max(c => c.MinSize.Y));
                outpostParamsEditor.AddCustomContent(moduleCountGroup, 100);
            }

            // add module count -------------------------

            var addModuleCountGroup = new GUILayoutGroup(new RectTransform(new Point(editorContainer.Content.Rect.Width, (int)(40 * GUI.Scale))), isHorizontal: true, childAnchor: Anchor.Center);

            HashSet<Identifier> availableFlags = new HashSet<Identifier>();
            foreach (Identifier flag in OutpostGenerationParams.OutpostParams.SelectMany(p => p.ModuleCounts.Select(m => m.Key))) { availableFlags.Add(flag); }
            foreach (var sub in SubmarineInfo.SavedSubmarines)
            {
                if (sub.OutpostModuleInfo == null) { continue; }
                foreach (Identifier flag in sub.OutpostModuleInfo.ModuleFlags) { availableFlags.Add(flag); }
            }

            var moduleTypeDropDown = new GUIDropDown(new RectTransform(new Vector2(0.8f, 0.8f), addModuleCountGroup.RectTransform),
                text: TextManager.Get("leveleditor.addmoduletype"));
            foreach (Identifier flag in availableFlags)
            {
                if (outpostGenerationParams.ModuleCounts.Any(mc => mc.Key == flag)) { continue; }
                moduleTypeDropDown.AddItem(TextManager.Capitalize(flag.Value), flag);
            }
            moduleTypeDropDown.OnSelected += (_, userdata) =>
            {
                outpostGenerationParams.SetModuleCount((Identifier)userdata, 1);
                outpostParamsList.Select(outpostParamsList.SelectedData);
                return true;
            };
            addModuleCountGroup.RectTransform.MinSize = new Point(addModuleCountGroup.Rect.Width, addModuleCountGroup.RectTransform.Children.Max(c => c.MinSize.Y));
            outpostParamsEditor.AddCustomContent(addModuleCountGroup, 100);

        }

        private void CreateLevelObjectEditor(LevelObjectPrefab levelObjectPrefab)
        {
            editorContainer.ClearChildren();

            var editor = new SerializableEntityEditor(editorContainer.Content.RectTransform, levelObjectPrefab, false, true, elementHeight: 20, titleFont: GUIStyle.LargeFont);

            if (selectedParams != null)
            {
                List<Identifier> availableIdentifiers = new List<Identifier>();
                if (selectedParams != null) { availableIdentifiers.Add(selectedParams.Identifier); }
                foreach (var caveParam in CaveGenerationParams.CaveParams)
                {
                    if (selectedParams != null && caveParam.GetCommonness(selectedParams, abyss: false) <= 0.0f) { continue; }
                    availableIdentifiers.Add(caveParam.Identifier);
                }
                availableIdentifiers.Reverse();

                foreach (Identifier paramsId in availableIdentifiers)
                {
                    var commonnessContainer = new GUILayoutGroup(new RectTransform(new Point(editor.Rect.Width, 70)) { IsFixedSize = true }, 
                        isHorizontal: false, childAnchor: Anchor.TopCenter)
                    {
                        AbsoluteSpacing = 5,
                        Stretch = true
                    };
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), commonnessContainer.RectTransform),
                        TextManager.GetWithVariable("leveleditor.levelobjcommonness", "[leveltype]", paramsId.Value), textAlignment: Alignment.Center);
                    new GUINumberInput(new RectTransform(new Vector2(0.5f, 0.4f), commonnessContainer.RectTransform), GUINumberInput.NumberType.Float)
                    {
                        MinValueFloat = 0,
                        MaxValueFloat = 100,
                        FloatValue = selectedParams.Identifier == paramsId ? levelObjectPrefab.GetCommonness(selectedParams) : levelObjectPrefab.GetCommonness(CaveGenerationParams.CaveParams.Find(p => p.Identifier == paramsId)),
                        OnValueChanged = (numberInput) =>
                        {
                            levelObjectPrefab.OverrideCommonness[paramsId] = numberInput.FloatValue;
                        }
                    };
                    new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), commonnessContainer.RectTransform), style: null);
                    editor.AddCustomContent(commonnessContainer, 1);
                }
            }

            Sprite sprite = levelObjectPrefab.Sprites.FirstOrDefault() ?? levelObjectPrefab.DeformableSprite?.Sprite;
            if (sprite != null)
            {
                editor.AddCustomContent(new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, (int)(25 * GUI.Scale))) { IsFixedSize = true }, 
                    TextManager.Get("leveleditor.editsprite"))
                {
                    OnClicked = (btn, userdata) =>
                    {
                        editingSprite = sprite;
                        GameMain.SpriteEditorScreen.SelectSprite(editingSprite);
                        return true;
                    }
                }, 1);
            }

            if (levelObjectPrefab.DeformableSprite != null)
            {
                var deformEditor = levelObjectPrefab.DeformableSprite.CreateEditor(editor, levelObjectPrefab.SpriteDeformations, levelObjectPrefab.Name);
                deformEditor.GetChild<GUIDropDown>().OnSelected += (selected, userdata) =>
                {
                    CreateLevelObjectEditor(selectedLevelObject);
                    return true;
                };
                editor.AddCustomContent(deformEditor, editor.ContentCount);
            }
            //child object editing
            new GUITextBlock(new RectTransform(new Point(editor.Rect.Width, 40), editorContainer.Content.RectTransform),
                TextManager.Get("leveleditor.childobjects"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.BottomCenter);
            foreach (LevelObjectPrefab.ChildObject childObj in levelObjectPrefab.ChildObjects)
            {
                var childObjFrame = new GUIFrame(new RectTransform(new Point(editor.Rect.Width, 30)));
                var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), childObjFrame.RectTransform, Anchor.Center), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };
                var selectedChildObj = childObj;
                var dropdown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1.0f), paddedFrame.RectTransform), elementCount: 10, selectMultiple: true);
                foreach (LevelObjectPrefab objPrefab in LevelObjectPrefab.Prefabs)
                {
                    dropdown.AddItem(objPrefab.Name, objPrefab);
                    if (childObj.AllowedNames.Contains(objPrefab.Name)) { dropdown.SelectItem(objPrefab); }
                }
                dropdown.OnSelected = (selected, obj) =>
                {
                    childObj.AllowedNames = dropdown.SelectedDataMultiple.Select(d => ((LevelObjectPrefab)d).Name).ToList();
                    return true;
                };
                new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), paddedFrame.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = 10,
                    OnValueChanged = (numberInput) =>
                    {
                        selectedChildObj.MinCount = numberInput.IntValue;
                        selectedChildObj.MaxCount = Math.Max(selectedChildObj.MaxCount, selectedChildObj.MinCount);
                    }
                }.IntValue = childObj.MinCount;
                new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), paddedFrame.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = 10,
                    OnValueChanged = (numberInput) =>
                    {
                        selectedChildObj.MaxCount = numberInput.IntValue;
                        selectedChildObj.MinCount = Math.Min(selectedChildObj.MaxCount, selectedChildObj.MinCount);
                    }
                }.IntValue = childObj.MaxCount;

                new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), paddedFrame.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUICancelButton")
                {
                    OnClicked = (btn, userdata) =>
                    {
                        selectedLevelObject.ChildObjects.Remove(selectedChildObj);
                        CreateLevelObjectEditor(selectedLevelObject);
                        return true;
                    }
                };

                childObjFrame.RectTransform.Parent = editorContainer.Content.RectTransform;
            }

            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), editorContainer.Content.RectTransform), style: null);
            new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, 20), buttonContainer.RectTransform, Anchor.Center),
                TextManager.Get("leveleditor.addchildobject"))
            {
                OnClicked = (btn, userdata) =>
                {
                    selectedLevelObject.ChildObjects.Add(new LevelObjectPrefab.ChildObject());
                    CreateLevelObjectEditor(selectedLevelObject);
                    return true;
                }
            };
            buttonContainer.RectTransform.MinSize = buttonContainer.RectTransform.Children.First().MinSize;

            //light editing
            new GUITextBlock(new RectTransform(new Point(editor.Rect.Width, 40), editorContainer.Content.RectTransform),
                TextManager.Get("leveleditor.lightsources"), textAlignment: Alignment.BottomCenter, font: GUIStyle.SubHeadingFont);
            foreach (LightSourceParams lightSourceParams in selectedLevelObject.LightSourceParams)
            {
                new SerializableEntityEditor(editorContainer.Content.RectTransform, lightSourceParams, inGame: false, showName: true);
            }
            buttonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), editorContainer.Content.RectTransform), style: null);
            new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, 20), buttonContainer.RectTransform, Anchor.Center), 
                TextManager.Get("leveleditor.addlightsource"))
            {
                OnClicked = (btn, userdata) =>
                {
                    selectedLevelObject.LightSourceTriggerIndex.Add(-1);
                    selectedLevelObject.LightSourceParams.Add(new LightSourceParams(100.0f, Color.White));
                    CreateLevelObjectEditor(selectedLevelObject);
                    return true;
                }
            };
            buttonContainer.RectTransform.MinSize = buttonContainer.RectTransform.Children.First().MinSize;
        }

        private void SortLevelObjectsList(LevelGenerationParams selectedParams)
        {
            //fade out levelobjects that don't spawn in this type of level
            foreach (GUIComponent levelObjFrame in levelObjectList.Content.Children)
            {
                var levelObj = levelObjFrame.UserData as LevelObjectPrefab;
                float commonness = levelObj.GetCommonness(selectedParams);
                levelObjFrame.Color = commonness > 0.0f ? GUIStyle.Green * 0.4f : Color.Transparent;
                levelObjFrame.SelectedColor = commonness > 0.0f ? GUIStyle.Green * 0.6f : Color.White * 0.5f;
                levelObjFrame.HoverColor = commonness > 0.0f ? GUIStyle.Green * 0.7f : Color.White * 0.6f;

                levelObjFrame.GetAnyChild<GUIImage>().Color = commonness > 0.0f ? Color.White : Color.DarkGray;
                if (commonness <= 0.0f)
                {
                    levelObjFrame.GetAnyChild<GUITextBlock>().TextColor = Color.DarkGray;
                }
            }

            //sort the levelobjects according to commonness in this level
            levelObjectList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                var levelObj1 = c1.GUIComponent.UserData as LevelObjectPrefab;
                var levelObj2 = c2.GUIComponent.UserData as LevelObjectPrefab;
                return Math.Sign(levelObj2.GetCommonness(selectedParams) - levelObj1.GetCommonness(selectedParams));
            });
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            rightPanel.Visible = leftPanel.Visible = bottomPanel.Visible = editingSprite == null;
            if (editingSprite != null)
            {
                GameMain.SpriteEditorScreen.TopPanel.AddToGUIUpdateList();
                spriteEditDoneButton.AddToGUIUpdateList();
            }
            else if (lightingEnabled.Selected && cursorLightEnabled.Selected)
            {
                topPanel.AddToGUIUpdateList();
            }
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            if (lightingEnabled.Selected)
            {
                GameMain.LightManager.RenderLightMap(graphics, spriteBatch, Cam);
            }
            graphics.Clear(Color.Black);

            if (Level.Loaded != null)
            {
                Level.Loaded.DrawBack(graphics, spriteBatch, Cam);
                Level.Loaded.DrawFront(spriteBatch, Cam);
                spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, SamplerState.LinearWrap, DepthStencilState.DepthRead, transformMatrix: Cam.Transform);
                Level.Loaded.DrawDebugOverlay(spriteBatch, Cam);
                Submarine.Draw(spriteBatch, false);
                Submarine.DrawFront(spriteBatch);
                Submarine.DrawDamageable(spriteBatch, null);
                GUI.DrawRectangle(spriteBatch, new Rectangle(new Point(0, -Level.Loaded.Size.Y), Level.Loaded.Size), Color.Gray, thickness: (int)(1.0f / Cam.Zoom));

                for (int i = 0; i < Level.Loaded.Tunnels.Count; i++)
                {
                    var tunnel = Level.Loaded.Tunnels[i];
                    Color tunnelColor = tunnelDebugColors[i % tunnelDebugColors.Length] * 0.2f;
                    for (int j = 1; j < tunnel.Nodes.Count; j++)
                    {
                        Vector2 start = new Vector2(tunnel.Nodes[j - 1].X, -tunnel.Nodes[j - 1].Y);
                        Vector2 end = new Vector2(tunnel.Nodes[j].X, -tunnel.Nodes[j].Y);
                        GUI.DrawLine(spriteBatch, start, end, tunnelColor, width: (int)(2.0f / Cam.Zoom));
                    }
                }

                foreach (Level.InterestingPosition interestingPos in Level.Loaded.PositionsOfInterest)
                {
                    if (interestingPos.Position.X < Cam.WorldView.X || interestingPos.Position.X > Cam.WorldView.Right ||
                        interestingPos.Position.Y > Cam.WorldView.Y || interestingPos.Position.Y < Cam.WorldView.Y - Cam.WorldView.Height)
                    {
                        continue;
                    }

                    Vector2 pos = new Vector2(interestingPos.Position.X, -interestingPos.Position.Y);
                    spriteBatch.DrawCircle(pos, 500, 6, Color.White * 0.5f, thickness: (int)(2 / Cam.Zoom));
                    GUI.DrawString(spriteBatch, pos, interestingPos.PositionType.ToString(), Color.White, font: GUIStyle.LargeFont);
                }

                // TODO: Improve this temporary level editor debug solution (or remove it)
                foreach (var pathPoint in Level.Loaded.PathPoints)
                {
                    Vector2 pathPointPos = new Vector2(pathPoint.Position.X, -pathPoint.Position.Y);
                    foreach (var location in pathPoint.ClusterLocations)
                    {
                        if (location.Resources == null) { continue; }
                        foreach (var resource in location.Resources)
                        {
                            Vector2 resourcePos = new Vector2(resource.Position.X, -resource.Position.Y);
                            spriteBatch.DrawCircle(resourcePos, 100, 6, Color.DarkGreen * 0.5f, thickness: (int)(2 / Cam.Zoom));
                            GUI.DrawString(spriteBatch, resourcePos, resource.Name, Color.DarkGreen, font: GUIStyle.LargeFont);
                            var dist = Vector2.Distance(resourcePos, pathPointPos);
                            var lineStartPos = Vector2.Lerp(resourcePos, pathPointPos, 110 / dist);
                            var lineEndPos = Vector2.Lerp(pathPointPos, resourcePos, 310 / dist);
                            GUI.DrawLine(spriteBatch, lineStartPos, lineEndPos, Color.DarkGreen * 0.5f, width: (int)(2 / Cam.Zoom));
                        }
                    }
                    var color = pathPoint.ShouldContainResources ? Color.DarkGreen : Color.DarkRed;
                    spriteBatch.DrawCircle(pathPointPos, 300, 6, color * 0.5f, thickness: (int)(2 / Cam.Zoom));
                    GUI.DrawString(spriteBatch, pathPointPos, "Path Point\n" + pathPoint.Id, color, font: GUIStyle.LargeFont);
                }

                /*for (int i = 0; i < Level.Loaded.distanceField.Count; i++)
                {
                    GUI.DrawRectangle(spriteBatch, 
                        new Vector2(Level.Loaded.distanceField[i].First.X, -Level.Loaded.distanceField[i].First.Y), 
                        Vector2.One * 5 / cam.Zoom, 
                        ToolBox.GradientLerp((float)(Level.Loaded.distanceField[i].Second / 20000.0), Color.Red, Color.Orange, Color.Yellow, Color.LightGreen), 
                        isFilled: true);
                }*/
                /*for (int i = 0; i < Level.Loaded.siteCoordsX.Count; i++)
                {
                    GUI.DrawRectangle(spriteBatch,
                        new Vector2((float)Level.Loaded.siteCoordsX[i], -(float)Level.Loaded.siteCoordsY[i]),
                        Vector2.One * 5 / cam.Zoom,
                        Color.Red,
                        isFilled: true);
                }*/

                spriteBatch.End();

                if (lightingEnabled.Selected)
                {
                    spriteBatch.Begin(SpriteSortMode.Immediate, Lights.CustomBlendStates.Multiplicative, null, DepthStencilState.None, null, null, null);
                    spriteBatch.Draw(GameMain.LightManager.LightMap, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                    spriteBatch.End();
                }
            }            

            if (editingSprite != null)
            {
                GameMain.SpriteEditorScreen.Draw(deltaTime, graphics, spriteBatch);
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            if (Level.Loaded != null)
            {
                float crushDepthScreen = Cam.WorldToScreen(new Vector2(0.0f, -Level.Loaded.CrushDepth)).Y;
                if (crushDepthScreen > 0.0f && crushDepthScreen < GameMain.GraphicsHeight)
                {
                    GUI.DrawLine(spriteBatch, new Vector2(0, crushDepthScreen), new Vector2(GameMain.GraphicsWidth, crushDepthScreen), GUIStyle.Red * 0.25f, width: 5);
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, crushDepthScreen), "Crush depth", GUIStyle.Red, backgroundColor: Color.Black);
                }

                float abyssStartScreen = Cam.WorldToScreen(new Vector2(0.0f, Level.Loaded.AbyssArea.Bottom)).Y;
                if (abyssStartScreen > 0.0f && abyssStartScreen < GameMain.GraphicsHeight)
                {
                    GUI.DrawLine(spriteBatch, new Vector2(0, abyssStartScreen), new Vector2(GameMain.GraphicsWidth, abyssStartScreen), GUIStyle.Blue * 0.25f, width: 5);
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, abyssStartScreen), "Abyss start", GUIStyle.Blue, backgroundColor: Color.Black);
                }
                float abyssEndScreen = Cam.WorldToScreen(new Vector2(0.0f, Level.Loaded.AbyssArea.Y)).Y;
                if (abyssEndScreen > 0.0f && abyssEndScreen < GameMain.GraphicsHeight)
                {
                    GUI.DrawLine(spriteBatch, new Vector2(0, abyssEndScreen), new Vector2(GameMain.GraphicsWidth, abyssEndScreen), GUIStyle.Blue * 0.25f, width: 5);
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, abyssEndScreen), "Abyss end", GUIStyle.Blue, backgroundColor: Color.Black);
                }
            }
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }

        public override void Update(double deltaTime)
        {
            if (lightingEnabled.Selected)
            {
                foreach (Item item in Item.ItemList)
                {
                    item?.GetComponent<Items.Components.LightComponent>()?.Update((float)deltaTime, Cam);
                }
            }
            GameMain.LightManager?.Update((float)deltaTime);

            pointerLightSource.Position = Cam.ScreenToWorld(PlayerInput.MousePosition);
            pointerLightSource.Enabled = cursorLightEnabled.Selected;
            pointerLightSource.IsBackground = true;
            Cam.MoveCamera((float)deltaTime, allowZoom: GUI.MouseOn == null);
            Cam.UpdateTransform();
            Level.Loaded?.Update((float)deltaTime, Cam);

            if (editingSprite != null)
            {
                GameMain.SpriteEditorScreen.Update(deltaTime);
            }
        }

        private void SerializeAll()
        {
            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };
            foreach (var configFile in ContentPackageManager.AllPackages.SelectMany(p => p.GetFiles<LevelGenerationParametersFile>()))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                foreach (LevelGenerationParams genParams in LevelGenerationParams.LevelParams)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.IsOverride())
                        {
                            foreach (var subElement in element.Elements())
                            {
                                string id = element.GetAttributeString("identifier", null) ?? element.Name.ToString();
                                if (!id.Equals(genParams.Name, StringComparison.OrdinalIgnoreCase)) { continue; }
                                SerializableProperty.SerializeProperties(genParams, element, true);
                            }
                        }
                        else
                        {
                            string id = element.GetAttributeString("identifier", null) ?? element.Name.ToString();
                            if (!id.Equals(genParams.Name, StringComparison.OrdinalIgnoreCase)) { continue; }
                            SerializableProperty.SerializeProperties(genParams, element, true);
                        }
                        break;
                    }
                }
                using (var writer = XmlWriter.Create(configFile.Path.Value, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }

            foreach (var configFile in ContentPackageManager.AllPackages.SelectMany(p => p.GetFiles<CaveGenerationParametersFile>()))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                foreach (CaveGenerationParams genParams in CaveGenerationParams.CaveParams)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.IsOverride())
                        {
                            foreach (var subElement in element.Elements())
                            {
                                string id = subElement.GetAttributeString("identifier", null) ?? subElement.Name.ToString();
                                if (!id.Equals(genParams.Name, StringComparison.OrdinalIgnoreCase)) { continue; }
                                genParams.Save(subElement);
                            }
                        }
                        else
                        {
                            string id = element.GetAttributeString("identifier", null) ?? element.Name.ToString();
                            if (!id.Equals(genParams.Name, StringComparison.OrdinalIgnoreCase)) { continue; }
                            genParams.Save(element);
                        }
                        break;
                    }
                }
                using (var writer = XmlWriter.Create(configFile.Path.Value, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }

            settings.NewLineOnAttributes = false;
            foreach (var configFile in ContentPackageManager.AllPackages.SelectMany(p => p.GetFiles<LevelObjectPrefabsFile>()))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                foreach (LevelObjectPrefab levelObjPrefab in LevelObjectPrefab.Prefabs)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        Identifier identifier = element.GetAttributeIdentifier("identifier", "");
                        if (identifier != levelObjPrefab.Identifier) { continue; }
                        levelObjPrefab.Save(element);
                        break;
                    }
                }
                using (var writer = XmlWriter.Create(configFile.Path.Value, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }

            RuinGenerationParams.SaveAll();
        }

        private void Serialize(LevelGenerationParams genParams)
        {
            foreach (var configFile in ContentPackageManager.AllPackages.SelectMany(p => p.GetFiles<LevelGenerationParametersFile>()))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                bool elementFound = false;
                foreach (XElement element in doc.Root.Elements())
                {
                    string id = element.GetAttributeString("identifier", null) ?? element.Name.ToString();
                    if (!id.Equals(genParams.Name, StringComparison.OrdinalIgnoreCase)) { continue; }
                    SerializableProperty.SerializeProperties(genParams, element, true);
                    elementFound = true;
                }                

                if (elementFound)
                {
                    System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
                    {
                        Indent = true,
                        NewLineOnAttributes = true
                    };

                    using (var writer = XmlWriter.Create(configFile.Path.Value, settings))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                    }
                    return;
                }
            }
        }

        
#region LevelObject Wizard
        private class Wizard
        {
            private LevelObjectPrefab newPrefab;

            private static Wizard instance;
            public static Wizard Instance
            {
                get
                {
                    if (instance == null)
                    {
                        instance = new Wizard();
                    }
                    return instance;
                }
            }
            
            public void AddToGUIUpdateList()
            {
                //activeView?.Box.AddToGUIUpdateList();
            }

            public GUIMessageBox Create()
            {
                var box = new GUIMessageBox(TextManager.Get("leveleditor.createlevelobj"), string.Empty, 
                    new LocalizedString[] { TextManager.Get("cancel"), TextManager.Get("done") }, new Vector2(0.5f, 0.8f));

                box.Content.ChildAnchor = Anchor.TopCenter;
                box.Content.AbsoluteSpacing = 20;
                int elementSize = 30;
                var listBox = new GUIListBox(new RectTransform(new Vector2(1, 0.9f), box.Content.RectTransform));

                new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width, elementSize), listBox.Content.RectTransform), 
                    TextManager.Get("leveleditor.levelobjname")) { CanBeFocused = false };
                var nameBox = new GUITextBox(new RectTransform(new Point(listBox.Content.Rect.Width, elementSize), listBox.Content.RectTransform));

                new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width, elementSize), listBox.Content.RectTransform), 
                    TextManager.Get("leveleditor.levelobjtexturepath")) { CanBeFocused = false };
                var texturePathBox = new GUITextBox(new RectTransform(new Point(listBox.Content.Rect.Width, elementSize), listBox.Content.RectTransform));
                foreach (LevelObjectPrefab prefab in LevelObjectPrefab.Prefabs)
                {
                    if (prefab.Sprites.FirstOrDefault() == null) continue;
                    texturePathBox.Text = Path.GetDirectoryName(prefab.Sprites.FirstOrDefault().FilePath.Value);
                    break;
                }

                //this is nasty :(
                newPrefab = new LevelObjectPrefab(null, null, Identifier.Empty);

                new SerializableEntityEditor(listBox.Content.RectTransform, newPrefab, false, false);
                
                box.Buttons[0].OnClicked += (b, d) =>
                {
                    box.Close();
                    return true;
                };
                // Next
                box.Buttons[1].OnClicked += (b, d) =>
                {
                    if (string.IsNullOrEmpty(nameBox.Text))
                    {
                        nameBox.Flash(GUIStyle.Red);
                        GUI.AddMessage(TextManager.Get("leveleditor.levelobjnameempty"), GUIStyle.Red);
                        return false;
                    }
                    
                    if (LevelObjectPrefab.Prefabs.Any(obj => obj.Identifier == nameBox.Text))
                    {
                        nameBox.Flash(GUIStyle.Red);
                        GUI.AddMessage(TextManager.Get("leveleditor.levelobjnametaken"), GUIStyle.Red);
                        return false;
                    }

                    if (!File.Exists(texturePathBox.Text))
                    {
                        texturePathBox.Flash(GUIStyle.Red);
                        GUI.AddMessage(TextManager.Get("leveleditor.levelobjtexturenotfound"), GUIStyle.Red);
                        return false;
                    }

                    System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings { Indent = true };

                    var newElement = new XElement(nameBox.Text);
                    newPrefab.Save(newElement);
                    newElement.Add(new XElement("Sprite", 
                        new XAttribute("texture", texturePathBox.Text), 
                        new XAttribute("sourcerect", "0,0,100,100"),
                        new XAttribute("origin", "0.5,0.5")));

                    // Create a new mod for the purpose of providing this new prefab
                    #warning TODO: add a clear way to tack it into an existing content package?
                    string modDir = Path.Combine(ContentPackage.LocalModsDir, nameBox.Text);
                    Directory.CreateDirectory(modDir);

                    string fileListPath = Path.Combine(modDir, ContentPackage.FileListFileName);
                    string prefabFilePath = Path.Combine(modDir, $"{nameBox.Text}.xml");

                    var newMod = new ModProject { Name = nameBox.Text };
                    var newFile = ModProject.File.FromPath<LevelObjectPrefabsFile>(prefabFilePath);
                    newMod.AddFile(newFile);

                    XDocument fileListDoc = newMod.ToXDocument();
                    Directory.CreateDirectory(Path.GetDirectoryName(fileListPath));
                    using (XmlWriter writer = XmlWriter.Create(fileListPath, settings)) { fileListDoc.Save(writer); }

                    XDocument prefabDoc = new XDocument();
                    var prefabFileRoot = new XElement("LevelObjects");
                    prefabFileRoot.Add(newElement);
                    prefabDoc.Add(prefabFileRoot);
                    using (XmlWriter writer = XmlWriter.Create(prefabFilePath, settings)) { prefabDoc.Save(writer); }

                    ContentPackageManager.UpdateContentPackageList();

                    var newRegularList = ContentPackageManager.EnabledPackages.Regular.ToList();
                    newRegularList.Add(ContentPackageManager.RegularPackages.First(p => p.Name == nameBox.Text));
                    ContentPackageManager.EnabledPackages.SetRegular(newRegularList);
                    
                    GameMain.LevelEditorScreen.UpdateLevelObjectsList();

                    box.Close();
                    return true;
                };
                return box;
            }

        }
#endregion
    }
}
