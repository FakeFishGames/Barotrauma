using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Barotrauma.Particles;

namespace Barotrauma
{
    class ParticleEditorScreen : Screen
    {
        private GUIComponent rightPanel, leftPanel;

        private GUIListBox prefabList;

        private ParticlePrefab selectedPrefab;

        private SerializableEntityEditor particlePrefabEditor;

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

            leftPanel = new GUIFrame(new Rectangle(0, 0, 150, GameMain.GraphicsHeight), "GUIFrameLeft");
            leftPanel.Padding = new Vector4(10.0f, 20.0f, 10.0f, 20.0f);

            rightPanel = new GUIFrame(new Rectangle(0, 0, 450, GameMain.GraphicsHeight), null, Alignment.Right, "GUIFrameRight");
            rightPanel.Padding = new Vector4(10.0f, 20.0f, 0.0f, 20.0f);

            var listBox = new GUIListBox(new Rectangle(0,0,0,0), "", rightPanel);

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
                prefabText.UserData = particlePrefab;
            }
        }

        public override void AddToGUIUpdateList()
        {
            leftPanel.AddToGUIUpdateList();
            rightPanel.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            cam.MoveCamera((float)deltaTime);

            leftPanel.Update((float)deltaTime);
            rightPanel.Update((float)deltaTime);

            if (selectedPrefab != null)
            {
                GameMain.ParticleManager.CreateParticle(selectedPrefab, cam.WorldViewCenter, Vector2.UnitY * 100.0f);
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

            leftPanel.Draw(spriteBatch);
            rightPanel.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, cam);

            spriteBatch.End();
        }
    }
}
