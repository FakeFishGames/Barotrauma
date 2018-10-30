using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Barotrauma.Lights;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class LevelEditorScreen : Screen
    {
        private readonly Camera cam;
        public override Camera Cam
        {
            get { return cam; }
        }

        private GUIFrame leftPanel, rightPanel;
        
        private LevelGenerationParams selectedParams;
        private LevelObjectPrefab selectedLevelObject;

        private GUIListBox paramsList, levelObjectList;
        private GUIListBox editorContainer;

        private GUITextBox seedBox;

        private GUITickBox lightingEnabled;

        public LevelEditorScreen()
        {
            cam = new Camera()
            {
                MinZoom = 0.01f,
                MaxZoom = 1.0f
            };

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.07f, 0.8f), Frame.RectTransform) { MinSize = new Point(150, 0) },
                style: "GUIFrameLeft");
            var paddedLeftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), leftPanel.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            paramsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), paddedLeftPanel.RectTransform));
            paramsList.OnSelected += (GUIComponent component, object obj) =>
            {
                selectedParams = obj as LevelGenerationParams;
                editorContainer.ClearChildren();
                SortLevelObjectsList(selectedParams);
                new SerializableEntityEditor(editorContainer.Content.RectTransform, selectedParams, false, true, elementHeight: 20);
                return true;
            };

            lightingEnabled = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.025f), paddedLeftPanel.RectTransform),
                TextManager.Get("LevelEditorLightingEnabled"));

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedLeftPanel.RectTransform),
                TextManager.Get("LevelEditorReloadTextures"))
            {
                OnClicked = (btn, obj) =>
                {
                    Level.Loaded?.ReloadTextures();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedLeftPanel.RectTransform),
                TextManager.Get("LevelEditorSaveAll"))
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
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), seedContainer.RectTransform), TextManager.Get("LevelEditorLevelSeed"));
            seedBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), seedContainer.RectTransform), ToolBox.RandomSeed(8));

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedRightPanel.RectTransform),
                TextManager.Get("LevelEditorGenerate"))
            {
                OnClicked = (btn, obj) =>
                {
                    GameMain.LightManager.ClearLights();
                    Level.CreateRandom(seedBox.Text, generationParams: selectedParams).Generate(mirror: false);
                    cam.Position = new Vector2(Level.Loaded.Size.X / 2, Level.Loaded.Size.Y / 2);
                    return true;
                }
            };

            var levelObjectContainer = new GUIFrame(new RectTransform(new Vector2(0.75f, 0.2f), Frame.RectTransform, Anchor.BottomLeft)
            { MaxSize = new Point(GameMain.GraphicsWidth - rightPanel.Rect.Width, 1000) }, style: "GUIFrameBottom");

            levelObjectList = new GUIListBox(new RectTransform(new Vector2(0.99f, 0.85f), levelObjectContainer.RectTransform, Anchor.Center))
            {
                UseGridLayout = true
            };
            levelObjectList.OnSelected += (GUIComponent component, object obj) =>
            {
                selectedLevelObject = obj as LevelObjectPrefab;
                CreateLevelObjectEditor(selectedLevelObject);
                return true;
            };

        }

        public override void Select()
        {
            base.Select();
            UpdateParamsList();
            UpdateLevelObjectsList();
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

                Sprite sprite = levelObjPrefab.Sprite ?? levelObjPrefab.DeformableSprite?.Sprite;
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
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), commonnessContainer.RectTransform), "Commonness in " + selectedParams.Name, textAlignment: Alignment.Center);
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
            
            //child object editing
            new GUITextBlock(new RectTransform(new Point(editor.Rect.Width, 40), editorContainer.Content.RectTransform), "Child objects:", textAlignment: Alignment.BottomCenter);
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

            new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, 20), editorContainer.Content.RectTransform), "Add new child object")
            {
                OnClicked = (btn, userdata) =>
                {
                    selectedLevelObject.ChildObjects.Add(new LevelObjectPrefab.ChildObject());
                    CreateLevelObjectEditor(selectedLevelObject);
                    return true;
                }
            };

            //light editing
            new GUITextBlock(new RectTransform(new Point(editor.Rect.Width, 40), editorContainer.Content.RectTransform), "Light sources:", textAlignment: Alignment.BottomCenter);
            foreach (LightSourceParams lightSourceParams in selectedLevelObject.LightSourceParams)
            {
                new SerializableEntityEditor(editorContainer.Content.RectTransform, lightSourceParams, inGame: false, showName: true);
            }
            new GUIButton(new RectTransform(new Point(editor.Rect.Width / 2, 20), editorContainer.Content.RectTransform), "Add new light source")
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
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap, DepthStencilState.Default, transformMatrix: cam.Transform);
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

            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);

            GUI.Draw(Cam, spriteBatch);

            spriteBatch.End();
        }

        public override void Update(double deltaTime)
        {
            cam.MoveCamera((float)deltaTime);
            cam.UpdateTransform();
            Level.Loaded?.Update((float)deltaTime, cam);
        }

        private void SerializeAll()
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.LevelGenerationParameters))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc == null || doc.Root == null) continue;

                foreach (LevelGenerationParams genParams in LevelGenerationParams.LevelParams)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.Name.ToString().ToLowerInvariant() != genParams.Name.ToLowerInvariant()) continue;
                        SerializableProperty.SerializeProperties(genParams, element, true);
                        break;
                    }
                }
                using (var writer = XmlWriter.Create(configFile, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }

            settings.NewLineOnAttributes = false;
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.LevelObjectPrefabs))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc == null || doc.Root == null) continue;

                foreach (LevelObjectPrefab levelObjPrefab in LevelObjectPrefab.List)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.Name.ToString().ToLowerInvariant() != levelObjPrefab.Name.ToLowerInvariant()) continue;
                        levelObjPrefab.Save(element);
                        break;
                    }
                }
                using (var writer = XmlWriter.Create(configFile, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }
        }

        private void Serialize(LevelGenerationParams genParams)
        {
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.LevelGenerationParameters))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc == null || doc.Root == null) continue;

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

                    using (var writer = XmlWriter.Create(configFile, settings))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                    }
                    return;
                }
            }
        }
    }
}
