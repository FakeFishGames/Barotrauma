using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
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

        private GUIListBox paramsList;
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

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.07f, 1.0f), Frame.RectTransform) { MinSize = new Point(150, 0) },
                style: "GUIFrameLeft");
            var paddedLeftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), leftPanel.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            paramsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedLeftPanel.RectTransform));
            paramsList.OnSelected += (GUIComponent component, object obj) =>
            {
                selectedParams = obj as LevelGenerationParams;
                editorContainer.ClearChildren();
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
        }

        public override void Select()
        {
            base.Select();
            UpdateParamsList();
        }

        private void UpdateParamsList()
        {
            editorContainer.ClearChildren();
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
                    }
                }

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
