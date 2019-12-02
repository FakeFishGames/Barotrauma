using Barotrauma.Lights;
using Barotrauma.RuinGeneration;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    class LevelEditorScreen : Screen
    {
        private readonly Camera cam;
        public override Camera Cam
        {
            get { return cam; }
        }

        private GUIFrame leftPanel, rightPanel, bottomPanel, topPanel;
        
        private LevelGenerationParams selectedParams;
        private LevelObjectPrefab selectedLevelObject;

        private GUIListBox paramsList, ruinParamsList, levelObjectList;
        private GUIListBox editorContainer;

        private GUIButton spriteEditDoneButton;

        private GUITextBox seedBox;

        private GUITickBox lightingEnabled, cursorLightEnabled;

        private Sprite editingSprite;

        private LightSource pointerLightSource;

        public LevelEditorScreen()
        {
            cam = new Camera()
            {
                MinZoom = 0.01f,
                MaxZoom = 1.0f
            };

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.125f, 0.8f), Frame.RectTransform) { MinSize = new Point(150, 0) },
                style: "GUIFrameLeft");
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

            ruinParamsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.2f), paddedLeftPanel.RectTransform));
            ruinParamsList.OnSelected += (GUIComponent component, object obj) =>
            {
                var ruinGenerationParams = obj as RuinGenerationParams;
                editorContainer.ClearChildren();
                new SerializableEntityEditor(editorContainer.Content.RectTransform, ruinGenerationParams, false, true, elementHeight: 20);
                return true;
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedLeftPanel.RectTransform),
                TextManager.Get("leveleditor.createlevelobj"))
            {
                OnClicked = (btn, obj) =>
                {
                    Wizard.Instance.Create();
                    return true;
                }
            };

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

            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), Frame.RectTransform, Anchor.TopRight) { MinSize = new Point(450, 0) },
                style: "GUIFrameRight");
            var paddedRightPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), rightPanel.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            editorContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedRightPanel.RectTransform));

            var seedContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), paddedRightPanel.RectTransform), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), seedContainer.RectTransform), TextManager.Get("leveleditor.levelseed"));
            seedBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), seedContainer.RectTransform), ToolBox.RandomSeed(8));

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedRightPanel.RectTransform),
                TextManager.Get("leveleditor.generate"))
            {
                OnClicked = (btn, obj) =>
                {
                    Submarine.Unload();
                    GameMain.LightManager.ClearLights();
                    Level.CreateRandom(seedBox.Text, generationParams: selectedParams).Generate(mirror: false);
                    GameMain.LightManager.AddLight(pointerLightSource);
                    cam.Position = new Vector2(Level.Loaded.Size.X / 2, Level.Loaded.Size.Y / 2);
                    foreach (GUITextBlock param in paramsList.Content.Children)
                    {
                        param.TextColor = param.UserData == selectedParams ? Color.LightGreen : param.Style.textColor;
                    }
                    seedBox.Deselect();
                    return true;
                }
            };

            bottomPanel = new GUIFrame(new RectTransform(new Vector2(0.75f, 0.2f), Frame.RectTransform, Anchor.BottomLeft)
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

            foreach (LevelObjectPrefab levelObjPrefab in LevelObjectPrefab.List)
            {
                foreach (Sprite sprite in levelObjPrefab.Sprites)
                {
                    sprite?.EnsureLazyLoaded();
                }
            }

            pointerLightSource = new LightSource(Vector2.Zero, 1000.0f, Color.White, submarine: null);
            GameMain.LightManager.AddLight(pointerLightSource);
            topPanel.ClearChildren();
            new SerializableEntityEditor(topPanel.RectTransform, pointerLightSource.LightSourceParams, false, true);

            editingSprite = null;
            UpdateParamsList();
            UpdateRuinParamsList();
            UpdateLevelObjectsList();
        }

        public override void Deselect()
        {
            base.Deselect();
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

            foreach (RuinGenerationParams genParams in RuinGenerationParams.List)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), ruinParamsList.Content.RectTransform) { MinSize = new Point(0, 20) },
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

            int objectsPerRow = (int)Math.Ceiling(levelObjectList.Content.Rect.Width / Math.Max(150 * GUI.Scale, 100));
            float relWidth = 1.0f / objectsPerRow;

            foreach (LevelObjectPrefab levelObjPrefab in LevelObjectPrefab.List)
            {
                var frame = new GUIFrame(new RectTransform(
                    new Vector2(relWidth, relWidth * ((float)levelObjectList.Content.Rect.Width / levelObjectList.Content.Rect.Height)), 
                    levelObjectList.Content.RectTransform) { MinSize = new Point(0, 60) }, style: "GUITextBox")
                {
                    UserData = levelObjPrefab
                };
                var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), style: null);

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform, Anchor.BottomCenter),
                    text: ToolBox.LimitString(levelObjPrefab.Name, GUI.SmallFont, paddedFrame.Rect.Width), textAlignment: Alignment.Center, font: GUI.SmallFont)
                {
                    CanBeFocused = false,
                };

                Sprite sprite = levelObjPrefab.Sprites.FirstOrDefault() ?? levelObjPrefab.DeformableSprite?.Sprite;
                GUIImage img = new GUIImage(new RectTransform(new Point(paddedFrame.Rect.Height, paddedFrame.Rect.Height - textBlock.Rect.Height),
                    paddedFrame.RectTransform, Anchor.TopCenter), sprite, scaleToFit: true)
                {
                    CanBeFocused = false
                };
            }
        }

        private void CreateLevelObjectEditor(LevelObjectPrefab levelObjectPrefab)
        {
            editorContainer.ClearChildren();

            var editor = new SerializableEntityEditor(editorContainer.Content.RectTransform, levelObjectPrefab, false, true, elementHeight: 20);

            if (selectedParams != null)
            {
                var commonnessContainer = new GUILayoutGroup(new RectTransform(new Point(editor.Rect.Width, 70)), isHorizontal: false, childAnchor: Anchor.TopCenter)
                {
                    AbsoluteSpacing = 5,
                    Stretch = true
                };
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), commonnessContainer.RectTransform),
                    TextManager.GetWithVariable("leveleditor.levelobjcommonness", "[leveltype]", selectedParams.Name), textAlignment: Alignment.Center);
                new GUINumberInput(new RectTransform(new Vector2(0.5f, 0.4f), commonnessContainer.RectTransform), GUINumberInput.NumberType.Float)
                {
                    MinValueFloat = 0,
                    MaxValueFloat = 100,
                    FloatValue = levelObjectPrefab.GetCommonness(selectedParams.Name),
                    OnValueChanged = (numberInput) =>
                    {
                        levelObjectPrefab.OverrideCommonness[selectedParams.Name] = numberInput.FloatValue;
                    }
                };
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), commonnessContainer.RectTransform), style: null);
                editor.AddCustomContent(commonnessContainer, 1);
            }

            Sprite sprite = levelObjectPrefab.Sprites.FirstOrDefault() ?? levelObjectPrefab.DeformableSprite?.Sprite;
            if (sprite != null)
            {
                editor.AddCustomContent(new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, 20)), 
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
                TextManager.Get("leveleditor.childobjects"), textAlignment: Alignment.BottomCenter);
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
                foreach (LevelObjectPrefab objPrefab in LevelObjectPrefab.List)
                {
                    dropdown.AddItem(objPrefab.Name, objPrefab);
                    if (childObj.AllowedNames.Contains(objPrefab.Name)) dropdown.SelectItem(objPrefab);
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

                new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), paddedFrame.RectTransform), "X")
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

            new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, 20), editorContainer.Content.RectTransform),
                TextManager.Get("leveleditor.addchildobject"))
            {
                OnClicked = (btn, userdata) =>
                {
                    selectedLevelObject.ChildObjects.Add(new LevelObjectPrefab.ChildObject());
                    CreateLevelObjectEditor(selectedLevelObject);
                    return true;
                }
            };

            //light editing
            new GUITextBlock(new RectTransform(new Point(editor.Rect.Width, 40), editorContainer.Content.RectTransform),
                TextManager.Get("leveleditor.lightsources"), textAlignment: Alignment.BottomCenter);
            foreach (LightSourceParams lightSourceParams in selectedLevelObject.LightSourceParams)
            {
                new SerializableEntityEditor(editorContainer.Content.RectTransform, lightSourceParams, inGame: false, showName: true);
            }
            new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, 20), editorContainer.Content.RectTransform), 
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
        }

        private void SortLevelObjectsList(LevelGenerationParams selectedParams)
        {
            //fade out levelobjects that don't spawn in this type of level
            foreach (GUIComponent levelObjFrame in levelObjectList.Content.Children)
            {
                var levelObj = levelObjFrame.UserData as LevelObjectPrefab;
                Color color = levelObj.GetCommonness(selectedParams.Name) > 0.0f ? Color.White : Color.White * 0.3f;
                levelObjFrame.Color = color;
                levelObjFrame.GetAnyChild<GUIImage>().Color = color;
            }

            //sort the levelobjects according to commonness in this level
            levelObjectList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                var levelObj1 = c1.GUIComponent.UserData as LevelObjectPrefab;
                var levelObj2 = c2.GUIComponent.UserData as LevelObjectPrefab;
                return Math.Sign(levelObj2.GetCommonness(selectedParams.Name) - levelObj1.GetCommonness(selectedParams.Name));
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
                GameMain.LightManager.UpdateLightMap(graphics, spriteBatch, cam);
            }
            graphics.Clear(Color.Black);

            if (Level.Loaded != null)
            {
                Level.Loaded.DrawBack(graphics, spriteBatch, cam);
                spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, SamplerState.LinearWrap, DepthStencilState.DepthRead, transformMatrix: cam.Transform);
                Level.Loaded.DrawFront(spriteBatch, cam);
                Submarine.Draw(spriteBatch, false);
                Submarine.DrawFront(spriteBatch);
                Submarine.DrawDamageable(spriteBatch, null);
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
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }

        public override void Update(double deltaTime)
        {
            pointerLightSource.Position = cam.ScreenToWorld(PlayerInput.MousePosition);
            pointerLightSource.Enabled = cursorLightEnabled.Selected;
            pointerLightSource.IsBackground = true;
            cam.MoveCamera((float)deltaTime);
            cam.UpdateTransform();
            Level.Loaded?.Update((float)deltaTime, cam);

            if (editingSprite != null)
            {
                GameMain.SpriteEditorScreen.Update(deltaTime);
            }
        }

        private void SerializeAll()
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };
            foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.LevelGenerationParameters))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                foreach (LevelGenerationParams genParams in LevelGenerationParams.LevelParams)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.Name.ToString().ToLowerInvariant() != genParams.Name.ToLowerInvariant()) continue;
                        SerializableProperty.SerializeProperties(genParams, element, true);
                        break;
                    }
                }
                using (var writer = XmlWriter.Create(configFile.Path, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }

            settings.NewLineOnAttributes = false;
            foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.LevelObjectPrefabs))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                foreach (LevelObjectPrefab levelObjPrefab in LevelObjectPrefab.List)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.Name.ToString().ToLowerInvariant() != levelObjPrefab.Name.ToLowerInvariant()) continue;
                        levelObjPrefab.Save(element);
                        break;
                    }
                }
                using (var writer = XmlWriter.Create(configFile.Path, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }

            RuinGenerationParams.SaveAll();
        }

        private void Serialize(LevelGenerationParams genParams)
        {
            foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.LevelGenerationParameters))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                bool elementFound = false;
                foreach (XElement element in doc.Root.Elements())
                {
                    if (element.Name.ToString().ToLowerInvariant() != genParams.Name.ToLowerInvariant()) continue;
                    SerializableProperty.SerializeProperties(genParams, element, true);
                    elementFound = true;
                }                

                if (elementFound)
                {
                    XmlWriterSettings settings = new XmlWriterSettings
                    {
                        Indent = true,
                        NewLineOnAttributes = true
                    };

                    using (var writer = XmlWriter.Create(configFile.Path, settings))
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
                    new string[] { TextManager.Get("cancel"), TextManager.Get("done") }, new Vector2(0.5f, 0.8f));

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
                foreach (LevelObjectPrefab prefab in LevelObjectPrefab.List)
                {
                    if (prefab.Sprites.FirstOrDefault() == null) continue;
                    texturePathBox.Text = Path.GetDirectoryName(prefab.Sprites.FirstOrDefault().FilePath);
                    break;
                }

                newPrefab = new LevelObjectPrefab(null);

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
                        nameBox.Flash(Color.Red);
                        GUI.AddMessage(TextManager.Get("leveleditor.levelobjnameempty"), Color.Red);
                        return false;
                    }
                    
                    if (LevelObjectPrefab.List.Any(obj => obj.Name.ToLower() == nameBox.Text.ToLower()))
                    {
                        nameBox.Flash(Color.Red);
                        GUI.AddMessage(TextManager.Get("leveleditor.levelobjnametaken"), Color.Red);
                        return false;
                    }

                    if (!File.Exists(texturePathBox.Text))
                    {
                        texturePathBox.Flash(Color.Red);
                        GUI.AddMessage(TextManager.Get("leveleditor.levelobjtexturenotfound"), Color.Red);
                        return false;
                    }

                    newPrefab.Name = nameBox.Text;
                    
                    XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                    foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.LevelObjectPrefabs))
                    {
                        XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                        if (doc == null) { continue; }
                        var newElement = new XElement(newPrefab.Name);
                        newPrefab.Save(newElement);
                        newElement.Add(new XElement("Sprite", 
                            new XAttribute("texture", texturePathBox.Text), 
                            new XAttribute("sourcerect", "0,0,100,100"),
                            new XAttribute("origin", "0.5,0.5")));

                        doc.Root.Add(newElement);
                        using (var writer = XmlWriter.Create(configFile.Path, settings))
                        {
                            doc.WriteTo(writer);
                            writer.Flush();
                        }
                        // Recreate the prefab so that the sprite loads correctly: TODO: consider a better way to do this
                        newPrefab = new LevelObjectPrefab(newElement);
                        break;
                    }

                    LevelObjectPrefab.List.Add(newPrefab);
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
