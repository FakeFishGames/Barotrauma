using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Barotrauma.Particles;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml;
using System.Text;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class ParticleEditorScreen : Screen
    {
        class Emitter : ISerializableEntity
        {
            public float EmitTimer;

            public float BurstTimer;

            [Editable(), Serialize("0.0,360.0", false)]
            public Vector2 AngleRange { get; private set; }

            [Editable(), Serialize("0.0,0.0", false)]
            public Vector2 VelocityRange { get; private set; }

            [Editable(), Serialize("1.0,1.0", false)]
            public Vector2 ScaleRange { get; private set; }

            [Editable(), Serialize(0, false)]
            public int ParticleBurstAmount { get; private set; }

            [Editable(), Serialize(1.0f, false)]
            public float ParticleBurstInterval { get; private set; }

            [Editable(), Serialize(1.0f, false)]
            public float ParticlesPerSecond { get; private set; }

            public string Name
            {
                get
                {
                    return TextManager.Get("particleeditor.emitter");
                }
            }

            public Dictionary<string, SerializableProperty> SerializableProperties
            {
                get;
                private set;
            }

            public Emitter()
            {
                ScaleRange = Vector2.One;
                AngleRange = new Vector2(0.0f, 360.0f);
                ParticleBurstAmount = 1;
                ParticleBurstInterval = 1.0f;

                SerializableProperties = SerializableProperty.GetProperties(this);
            }
        }

        private GUIComponent rightPanel, leftPanel;
        private GUIListBox prefabList;
        private GUITextBox filterBox;
        private GUITextBlock filterLabel;

        private ParticlePrefab selectedPrefab;

        private Emitter emitter;

        private readonly Camera cam;

        public override Camera Cam
        {
            get
            {
                return cam;
            }
        }

        public ParticleEditorScreen()
        {
            cam = new Camera();
            GameMain.Instance.OnResolutionChanged += CreateUI;
            CreateUI();

        }

        private void CreateUI()
        {
            Frame.ClearChildren();

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.125f, 1.0f), Frame.RectTransform) { MinSize = new Point(150, 0) },
                style: "GUIFrameLeft");
            var paddedLeftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), leftPanel.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), Frame.RectTransform, Anchor.TopRight) { MinSize = new Point(350, 0) },
                style: "GUIFrameRight");
            var paddedRightPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), rightPanel.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };
            
            var saveAllButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.03f), paddedRightPanel.RectTransform),
                TextManager.Get("editor.saveall"))
            {
                OnClicked = (btn, obj) =>
                {
                    SerializeAll();
                    return true;
                }
            };

            var serializeToClipBoardButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.03f), paddedRightPanel.RectTransform),
                TextManager.Get("editor.copytoclipboard"))
            {
                OnClicked = (btn, obj) =>
                {
                    SerializeToClipboard(selectedPrefab);
                    return true;
                }
            };

            emitter = new Emitter();
            var emitterEditorContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), paddedRightPanel.RectTransform), style: null);
            var emitterEditor = new SerializableEntityEditor(emitterEditorContainer.RectTransform, emitter, false, true, elementHeight: 20, titleFont: GUI.SubHeadingFont);
            emitterEditor.RectTransform.RelativeSize = Vector2.One;
            emitterEditorContainer.RectTransform.Resize(new Point(emitterEditorContainer.RectTransform.NonScaledSize.X, emitterEditor.ContentHeight), false);

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.6f), paddedRightPanel.RectTransform));

            var filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), paddedLeftPanel.RectTransform) { MinSize = new Point(0, 20) }, isHorizontal: true)
            {
                Stretch = true,
                UserData = "filterarea"
            };

            filterLabel = new GUITextBlock(new RectTransform(Vector2.One, filterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUI.Font) { IgnoreLayoutGroups = true };
            filterBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), font: GUI.Font);
            filterBox.OnTextChanged += (textBox, text) => { FilterEmitters(text); return true; };
            new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), filterArea.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUICancelButton")
            {
                OnClicked = (btn, userdata) => { FilterEmitters(""); filterBox.Text = ""; filterBox.Flash(Color.White); return true; }
            };

            prefabList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.8f), paddedLeftPanel.RectTransform));
            prefabList.OnSelected += (GUIComponent component, object obj) =>
            {
                selectedPrefab = obj as ParticlePrefab;
                listBox.ClearChildren();
                new SerializableEntityEditor(listBox.Content.RectTransform, selectedPrefab, false, true, elementHeight: 20, titleFont: GUI.SubHeadingFont);
                //listBox.Content.RectTransform.NonScaledSize = particlePrefabEditor.RectTransform.NonScaledSize;
                //listBox.UpdateScrollBarSize();
                return true;
            };

            if (GameMain.ParticleManager != null) { RefreshPrefabList(); }
        }

        public override void Select()
        {
            base.Select();
            GameMain.ParticleManager.Camera = cam;
            RefreshPrefabList();
        }

        public override void Deselect()
        {
            base.Deselect();
            GameMain.ParticleManager.Camera = GameMain.GameScreen.Cam;
            filterBox.Text = "";
        }

        private void RefreshPrefabList()
        {
            prefabList.ClearChildren();

            var particlePrefabs = GameMain.ParticleManager.GetPrefabList();
            foreach (ParticlePrefab particlePrefab in particlePrefabs)
            {
                var prefabText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), prefabList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    particlePrefab.DisplayName)
                {
                    Padding = Vector4.Zero,
                    UserData = particlePrefab
                };
            }
        }

        private void Emit(Vector2 position)
        {
            float angle = MathHelper.ToRadians(Rand.Range(emitter.AngleRange.X, emitter.AngleRange.Y));
            Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Rand.Range(emitter.VelocityRange.X, emitter.VelocityRange.Y);

            var particle = GameMain.ParticleManager.CreateParticle(selectedPrefab, position, velocity, 0.0f);

            if (particle != null)
            {
                particle.Size *= Rand.Range(emitter.ScaleRange.X, emitter.ScaleRange.Y);
            }
        }

        private void FilterEmitters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                filterLabel.Visible = true;
                prefabList.Content.Children.ForEach(c => c.Visible = true);
                return;
            }

            text = text.ToLower();
            filterLabel.Visible = false;
            foreach (GUIComponent child in prefabList.Content.Children)
            {
                if (!(child is GUITextBlock textBlock)) { continue; }
                textBlock.Visible = textBlock.Text.ToLower().Contains(text);
            }
        }
        
        private void SerializeAll()
        {
            foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.Particles))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                var prefabList = GameMain.ParticleManager.GetPrefabList();
                foreach (ParticlePrefab prefab in prefabList)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.Name.ToString().ToLowerInvariant() != prefab.Name.ToLowerInvariant()) continue;
                        SerializableProperty.SerializeProperties(prefab, element, true);
                    }
                }

                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = true,
                    NewLineOnAttributes = true
                };

                using (var writer = XmlWriter.Create(configFile.Path, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }
        }

        private void SerializeToClipboard(ParticlePrefab prefab)
        {
#if WINDOWS
            if (prefab == null) return;

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };

            XElement element = new XElement(prefab.Name);
            SerializableProperty.SerializeProperties(prefab, element, true);

            StringBuilder sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, settings))
            {
                element.WriteTo(writer);
                writer.Flush();
            }

            Clipboard.SetText(sb.ToString());
#endif
        }

        public override void Update(double deltaTime)
        {
            cam.MoveCamera((float)deltaTime, true);
            
            if (selectedPrefab != null)
            {
                emitter.EmitTimer += (float)deltaTime;
                emitter.BurstTimer += (float)deltaTime;


                if (emitter.ParticlesPerSecond > 0)
                {
                    float emitInterval = 1.0f / emitter.ParticlesPerSecond;
                    while (emitter.EmitTimer > emitInterval)
                    {
                        Emit(Vector2.Zero);
                        emitter.EmitTimer -= emitInterval;
                    }
                }

                if (emitter.BurstTimer > emitter.ParticleBurstInterval)
                {
                    for (int i = 0; i < emitter.ParticleBurstAmount; i++)
                    {
                        Emit(Vector2.Zero);
                    }
                    emitter.BurstTimer = 0.0f;
                }

            }

            GameMain.ParticleManager.Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform();
            GameMain.ParticleManager.UpdateTransforms();

            //-------------------------------------------------------

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                null, null, null, null,
                cam.Transform);

            graphics.Clear(new Color(0.051f, 0.149f, 0.271f, 1.0f));

            GameMain.ParticleManager.Draw(spriteBatch, false, false, ParticleBlendState.AlphaBlend);
            GameMain.ParticleManager.Draw(spriteBatch, true, false, ParticleBlendState.AlphaBlend);

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.Additive,
                null, null, null, null,
                cam.Transform);

            GameMain.ParticleManager.Draw(spriteBatch, false, false, ParticleBlendState.Additive);
            GameMain.ParticleManager.Draw(spriteBatch, true, false, ParticleBlendState.Additive);

            spriteBatch.End();

            //-------------------------------------------------------

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            
            GUI.Draw(Cam, spriteBatch);

            spriteBatch.End();
        }
    }
}
