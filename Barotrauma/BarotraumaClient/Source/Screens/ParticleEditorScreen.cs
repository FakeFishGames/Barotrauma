using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Barotrauma.Particles;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Windows;
using System.Xml;
using System.Text;
using System.Windows.Forms;

namespace Barotrauma
{
    class ParticleEditorScreen : Screen
    {
        class Emitter : ISerializableEntity
        {
            public float EmitTimer;
            
            [Editable(), Serialize("0.0,0.0", false)]
            public Vector2 AngleRange { get; private set; }
            
            [Editable(), Serialize("0.0,0.0", false)]
            public Vector2 VelocityRange { get; private set; }

            [Editable(), Serialize("1.0,1.0", false)]
            public Vector2 ScaleRange { get; private set; }

            [Editable(), Serialize(0, false)]
            public int ParticleAmount { get; private set; }
            [Editable(), Serialize(0.0f, false)]
            public float ParticlesPerSecond { get; private set; }

            public string Name
            {
                get
                {
                    return "Emitter";
                }
            }

            public Dictionary<string, SerializableProperty> SerializableProperties
            {
                get;
                private set;
            }

            public Emitter()
            {
                SerializableProperties = SerializableProperty.GetProperties(this);
            }
        }

        private GUIComponent guiRoot;
        private GUIComponent rightPanel, leftPanel;

        private GUIListBox prefabList;

        private ParticlePrefab selectedPrefab;

        private SerializableEntityEditor particlePrefabEditor;

        private Emitter emitter;

        private Camera cam;

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

            guiRoot = new GUIFrame(Rectangle.Empty, null, null);
            
            leftPanel = new GUIFrame(new Rectangle(0, 0, 150, GameMain.GraphicsHeight), "GUIFrameLeft", guiRoot);
            leftPanel.Padding = new Vector4(10.0f, 20.0f, 10.0f, 20.0f);

            rightPanel = new GUIFrame(new Rectangle(0, 0, 450, GameMain.GraphicsHeight), null, Alignment.Right, "GUIFrameRight", guiRoot);
            rightPanel.Padding = new Vector4(10.0f, 20.0f, 0.0f, 20.0f);

            var saveAllButton = new GUIButton(new Rectangle(leftPanel.Rect.Right + 20, 10, 150, 20), "Save all", "", guiRoot);
            saveAllButton.OnClicked += (btn, obj) =>
            {
                SerializeAll();
                return true;
            };

            var serializeToClipBoardButton = new GUIButton(new Rectangle(leftPanel.Rect.Right + 20, 10, 150, 20), "Copy to clipboard", "", guiRoot);
            serializeToClipBoardButton.OnClicked += (btn, obj) =>
            {
                SerializeToClipboard(selectedPrefab);
                return true;
            };

            emitter = new Emitter();
            var emitterEditor = new SerializableEntityEditor(emitter, false, rightPanel, true);

            var listBox = new GUIListBox(new Rectangle(0, emitterEditor.Rect.Height + 20, 0, 0), "", rightPanel);

            prefabList = new GUIListBox(new Rectangle(0, 50, 0, 0), "", leftPanel);
            prefabList.OnSelected += (GUIComponent component, object obj) =>
            {
                selectedPrefab = obj as ParticlePrefab;
                listBox.ClearChildren();
                particlePrefabEditor = new SerializableEntityEditor(selectedPrefab, false, listBox, true);
                return true;
            };
        }

        public override void Select()
        {
            base.Select();
            RefreshPrefabList();
        }

        private void RefreshPrefabList()
        {
            prefabList.ClearChildren();

            var particlePrefabs = GameMain.ParticleManager.GetPrefabList();
            foreach (ParticlePrefab particlePrefab in particlePrefabs)
            {
                var prefabText = new GUITextBlock(new Rectangle(0, 0, 0, 20), particlePrefab.Name, "", prefabList);
                prefabText.Padding = Vector4.Zero;
                prefabText.UserData = particlePrefab;
            }
        }

        public override void AddToGUIUpdateList()
        {
            guiRoot.AddToGUIUpdateList();
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

        private void SerializeAll()
        {            
            XDocument doc = XMLExtensions.TryLoadXml(GameMain.ParticleManager.ConfigFile);
            if (doc == null || doc.Root == null) return;

            var prefabList = GameMain.ParticleManager.GetPrefabList();
            foreach (ParticlePrefab prefab in prefabList)
            {
                foreach (XElement element in doc.Root.Elements())
                {
                    if (element.Name.ToString().ToLowerInvariant() != prefab.Name.ToLowerInvariant()) continue;

                    SerializableProperty.SerializeProperties(prefab, element, true);
                }
            }

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.OmitXmlDeclaration = true;
            settings.NewLineOnAttributes = true;

            using (var writer = XmlWriter.Create(GameMain.ParticleManager.ConfigFile, settings))
            {
                doc.WriteTo(writer);
                writer.Flush();
            }
        }

        private void SerializeToClipboard(ParticlePrefab prefab)
        {
            if (prefab == null) return;

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.OmitXmlDeclaration = true;
            settings.NewLineOnAttributes = true;

            XElement element = new XElement(prefab.Name);
            SerializableProperty.SerializeProperties(prefab, element, true);

            StringBuilder sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, settings))
            {
                element.WriteTo(writer);
                writer.Flush();
            }

            Clipboard.SetText(sb.ToString());
        }

        public override void Update(double deltaTime)
        {
            cam.MoveCamera((float)deltaTime, true, GUIComponent.MouseOn == null);

            guiRoot.Update((float)deltaTime);

            if (selectedPrefab != null)
            {
                emitter.EmitTimer += (float)deltaTime;

                if (emitter.ParticlesPerSecond > 0)
                {
                    float emitInterval = 1.0f / emitter.ParticlesPerSecond;
                    while (emitter.EmitTimer > emitInterval)
                    {
                        Emit(Vector2.Zero);
                        emitter.EmitTimer -= emitInterval;
                    }
                }

                for (int i = 0; i < emitter.ParticleAmount; i++)
                {
                    Emit(Vector2.Zero);
                }
            }

            GameMain.ParticleManager.Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform();
            GameMain.ParticleManager.UpdateTransforms();

            //-------------------------------------------------------

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);

            graphics.Clear(new Color(0.051f, 0.149f, 0.271f, 1.0f));

            GameMain.ParticleManager.Draw(spriteBatch, false, false, ParticleBlendState.AlphaBlend);
            GameMain.ParticleManager.Draw(spriteBatch, true, false, ParticleBlendState.AlphaBlend);

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.Additive,
                null, null, null, null,
                cam.Transform);

            GameMain.ParticleManager.Draw(spriteBatch, false, false, ParticleBlendState.Additive);
            GameMain.ParticleManager.Draw(spriteBatch, true, false, ParticleBlendState.Additive);

            spriteBatch.End();

            //-------------------------------------------------------

            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);

            guiRoot.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, cam);

            spriteBatch.End();
        }
    }
}
