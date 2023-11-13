using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Barotrauma.Particles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using Barotrauma.Extensions;
using FarseerPhysics;
#if DEBUG
using System.IO;
using System.Xml;
#else
using Barotrauma.IO;
#endif

namespace Barotrauma
{
    class ParticleEditorScreen : EditorScreen
    {
        private GUIComponent rightPanel, leftPanel;
        private GUIListBox prefabList;
        private GUITextBox filterBox;
        private GUITextBlock filterLabel;

        private ParticlePrefab selectedPrefab;

        private readonly ParticleEmitterProperties emitterProperties = new ParticleEmitterProperties(null)
        {
            ScaleMax = 1f,
            ScaleMin = 1f,
            AngleMax = 360f,
            AngleMin = 0,
            ParticlesPerSecond = 1f
        };

        private ParticleEmitterPrefab emitterPrefab;
        private ParticleEmitter emitter;

        private readonly Camera cam;

        public override Camera Cam => cam;

        private const string sizeRefFilePath = "Content/size_reference.png";
        private readonly Texture2D sizeReference;
        private Vector2 sizeRefPosition = Vector2.Zero;
        private readonly Vector2 sizeRefOrigin;
        private bool sizeRefEnabled;

        public ParticleEditorScreen()
        {
            cam = new Camera();
            GameMain.Instance.ResolutionChanged += CreateUI;
            CreateUI();
            if (File.Exists(sizeRefFilePath))
            {
                sizeReference = TextureLoader.FromFile(sizeRefFilePath, compress: false);
                sizeRefOrigin = new Vector2(sizeReference.Width / 2f, sizeReference.Height / 2f);
            }
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

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.03f), paddedRightPanel.RectTransform),
                TextManager.Get("ParticleEditor.CopyPrefabToClipboard"))
            {
                OnClicked = (btn, obj) =>
                {
                    SerializeToClipboard(selectedPrefab);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.03f), paddedRightPanel.RectTransform),
                TextManager.Get("ParticleEditor.CopyEmitterToClipboard"))
            {
                OnClicked = (btn, obj) =>
                {
                    SerializeEmitterToClipboard();
                    return true;
                }
            };

            var emitterListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.25f), paddedRightPanel.RectTransform));
            new SerializableEntityEditor(emitterListBox.Content.RectTransform, emitterProperties, false, true, elementHeight: 20, titleFont: GUIStyle.SubHeadingFont);

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.6f), paddedRightPanel.RectTransform));

            var filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), paddedLeftPanel.RectTransform) { MinSize = new Point(0, 20) }, isHorizontal: true)
            {
                Stretch = true,
                UserData = "filterarea"
            };

            filterLabel = new GUITextBlock(new RectTransform(Vector2.One, filterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.Font) { IgnoreLayoutGroups = true };
            filterBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), font: GUIStyle.Font);
            filterBox.OnTextChanged += (textBox, text) => { FilterEmitters(text); return true; };
            new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), filterArea.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUICancelButton")
            {
                OnClicked = (btn, userdata) => { FilterEmitters(""); filterBox.Text = ""; filterBox.Flash(Color.White); return true; }
            };

            prefabList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.8f), paddedLeftPanel.RectTransform))
            {
                PlaySoundOnSelect = true,
            };
            prefabList.OnSelected += (GUIComponent component, object obj) =>
            {
                cam.Position = Vector2.Zero;
                selectedPrefab = obj as ParticlePrefab;
                emitterPrefab = new ParticleEmitterPrefab(selectedPrefab, emitterProperties);
                emitter = new ParticleEmitter(emitterPrefab);
                listBox.ClearChildren();
                new SerializableEntityEditor(listBox.Content.RectTransform, selectedPrefab, false, true, elementHeight: 20, titleFont: GUIStyle.SubHeadingFont);
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

        protected override void DeselectEditorSpecific()
        {
            GameMain.ParticleManager.Camera = GameMain.GameScreen.Cam;
            filterBox.Text = "";
        }

        private void RefreshPrefabList()
        {
            prefabList.ClearChildren();

            var particlePrefabs = ParticleManager.GetPrefabList();
            foreach (ParticlePrefab particlePrefab in particlePrefabs)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), prefabList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    particlePrefab.Name)
                {
                    Padding = Vector4.Zero,
                    UserData = particlePrefab
                };
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
            Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
            foreach (var configFile in ContentPackageManager.AllPackages.SelectMany(p => p.GetFiles<ParticlesFile>()))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                var prefabList = ParticleManager.GetPrefabList();
                foreach (ParticlePrefab prefab in prefabList)
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (!element.Name.ToString().Equals(prefab.Name, StringComparison.OrdinalIgnoreCase)) { continue; }
                        SerializableProperty.SerializeProperties(prefab, element, true);
                    }
                }

                System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = true,
                    NewLineOnAttributes = true
                };

                using (var writer = XmlWriter.Create(configFile.Path.Value, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }
            Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
        }

        private void SerializeEmitterToClipboard()
        {
            XElement element = new XElement(nameof(ParticleEmitter));
            if (selectedPrefab is { } prefab)
            {
                element.Add(new XAttribute("particle", prefab.Identifier));
            }

            SerializableProperty.SerializeProperties(emitterProperties, element, saveIfDefault: false, ignoreEditable: true);

            StringBuilder sb = new StringBuilder();

            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                OmitXmlDeclaration = true
            };

            using (var writer = System.Xml.XmlWriter.Create(sb, settings))
            {
                element.WriteTo(writer);
                writer.Flush();
            }

            Clipboard.SetText(sb.ToString());
        }

        private void SerializeToClipboard(ParticlePrefab prefab)
        {
            if (prefab == null) { return; }

            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };

            XElement originalElement = null;
            foreach (var configFile in ContentPackageManager.AllPackages.SelectMany(p => p.GetFiles<ParticlesFile>()))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                var prefabList = ParticleManager.GetPrefabList();
                foreach (ParticlePrefab otherPrefab in prefabList)
                {
                    foreach (var subElement in doc.Root.Elements())
                    {
                        if (!subElement.Name.ToString().Equals(prefab.Name, StringComparison.OrdinalIgnoreCase)) { continue; }
                        SerializableProperty.SerializeProperties(prefab, subElement, true);
                        originalElement = subElement;
                        break;
                    }
                }
            }

            if (originalElement == null)
            {
                originalElement = new XElement(prefab.Name);
                SerializableProperty.SerializeProperties(prefab, originalElement, true);
            }

            StringBuilder sb = new StringBuilder();
            using (var writer = System.Xml.XmlWriter.Create(sb, settings))
            {
                originalElement.WriteTo(writer);
                writer.Flush();
            }

            Clipboard.SetText(sb.ToString());
        }

        public override void Update(double deltaTime)
        {
            cam.MoveCamera((float)deltaTime, allowMove: true, allowZoom: GUI.MouseOn == null);

            if (GUI.MouseOn is null && PlayerInput.PrimaryMouseButtonHeld())
            {
                sizeRefPosition = cam.ScreenToWorld(PlayerInput.MousePosition);
            }

            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                CreateContextMenu();
            }

            if (selectedPrefab != null && emitter != null)
            {
                emitter.Emit((float) deltaTime, Vector2.Zero);
            }

            GameMain.ParticleManager.Update((float)deltaTime);
        }

        private void CreateContextMenu()
        {
            GUIContextMenu.CreateContextMenu
            (
                new ContextMenuOption("subeditor.editbackgroundcolor", true, CreateBackgroundColorPicker),
                new ContextMenuOption("editor.togglereferencecharacter", true, delegate { sizeRefEnabled = !sizeRefEnabled; })
            );
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

            graphics.Clear(BackgroundColor);

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

            if (sizeRefEnabled && !(sizeReference is null))
            {
                spriteBatch.Begin(SpriteSortMode.Deferred,
                    BlendState.NonPremultiplied,
                    null, null, null, null,
                    cam.Transform);

                Vector2 pos = sizeRefPosition;
                pos.Y = -pos.Y;
                spriteBatch.Draw(sizeReference, pos, null, Color.White, 0f, sizeRefOrigin, new Vector2(0.4f), SpriteEffects.None, 0f);

                spriteBatch.End();
            }

            //-------------------------------------------------------

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            
            GUI.Draw(Cam, spriteBatch);

            spriteBatch.End();
        }
    }
}
