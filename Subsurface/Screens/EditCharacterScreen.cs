using System;
using System.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Subsurface
{
    class EditCharacterScreen : Screen
    {
        Camera cam;

        GUIComponent GUIpanel;
        GUIButton physicsButton;

        GUIListBox limbList, jointList;

        GUIFrame limbPanel;
        
        Character editingCharacter;
        Limb editingLimb;
        //RevoluteJoint editingJoint;



        List<Texture2D> textures;
        List<string> texturePaths;

        private bool physicsEnabled;

        public Camera Cam
        {
            get { return cam; }
        }
        
        public override void Select()
        {
            base.Select();

            Ragdoll.DebugDraw = true;

            cam = new Camera();

            GUIpanel = new GUIFrame(new Rectangle(0, 0, 300, Game1.GraphicsHeight), Color.DarkGray * 0.8f);
            GUIpanel.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            physicsButton = new GUIButton(new Rectangle(0, 50, 200, 25), "Physics", Color.White, GUIpanel);
            physicsButton.OnClicked += TogglePhysics;

            new GUITextBlock(new Rectangle(0, 80, 0, 25), "Limbs:", Color.Transparent, Color.Black, Alignment.Left, GUIpanel);
            limbList = new GUIListBox(new Rectangle(0, 110, 0, 250), Color.White * 0.7f, GUIpanel);
            limbList.OnSelected = SelectLimb;

            new GUITextBlock(new Rectangle(0, 360, 0, 25), "Joints:", Color.Transparent, Color.Black, Alignment.Left, GUIpanel);
            jointList = new GUIListBox(new Rectangle(0, 390, 0, 250), Color.White * 0.7f, GUIpanel);


            while (Character.characterList.Count > 1)
            {
                Character.characterList.First().Remove();
            }

            if (Character.characterList.Count == 1)
            {
                if (editingCharacter != Character.characterList[0]) UpdateLimbLists(Character.characterList[0]);
                editingCharacter = Character.characterList[0];

                Vector2 camPos = editingCharacter.animController.limbs[0].body.Position;
                camPos = ConvertUnits.ToDisplayUnits(camPos);
                camPos.Y = -camPos.Y;
                cam.TargetPos = camPos;

                if (physicsEnabled)
                {
                    editingCharacter.Control(cam);
                }
                else
                {
                    cam.TargetPos = Vector2.Zero;
                }
            }

            textures = new List<Texture2D>();
            texturePaths = new List<string>();
            foreach (Limb limb in editingCharacter.animController.limbs)
            {
                if (texturePaths.Contains(limb.sprite.FilePath)) continue;
                textures.Add(limb.sprite.Texture);
                texturePaths.Add(limb.sprite.FilePath);
            }
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Update(double deltaTime)
        {
            Physics.accumulator += deltaTime;
            //cam.Zoom += Math.Sign(PlayerInput.GetMouseState.ScrollWheelValue - PlayerInput.GetOldMouseState.ScrollWheelValue)*0.1f;
                       
            cam.MoveCamera((float)deltaTime);

            if (physicsEnabled)
            {
                Physics.accumulator = Math.Min(Physics.accumulator, Physics.step * 4);
                while (Physics.accumulator >= Physics.step)
                {
                    Character.UpdateAnimAll((float)Physics.step * 1000.0f);

                    Ragdoll.UpdateAll((float)Physics.step);

                    Game1.world.Step((float)Physics.step);

                    Physics.accumulator -= Physics.step;
                }

                Physics.Alpha = Physics.accumulator / Physics.step;
            }


                                                
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            //cam.UpdateTransform();

            graphics.Clear(Color.CornflowerBlue);

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);            
            
            Map.Draw(spriteBatch, true);

            spriteBatch.End();
            
            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform); 
            
            //if (EntityPrefab.Selected != null) EntityPrefab.Selected.UpdatePlacing(spriteBatch, cam);

            //Entity.DrawSelecting(spriteBatch, cam);

            if (editingCharacter!=null)
                editingCharacter.Draw(spriteBatch);

            spriteBatch.End();

            //-------------------- HUD -----------------------------

            spriteBatch.Begin();

            GUIpanel.Draw(spriteBatch);

            EditLimb(spriteBatch);


            int x = 0, y = 0;
            for (int i = 0; i < textures.Count; i++ )
            {
                x = Game1.GraphicsWidth - textures[i].Width;
                spriteBatch.Draw(textures[i], new Vector2(x, y), Color.White);

                foreach (Limb limb in editingCharacter.animController.limbs)
                {
                    if (limb.sprite.FilePath != texturePaths[i]) continue;
                    Rectangle rect = limb.sprite.SourceRect;
                    rect.X += x;
                    rect.Y += y;

                    GUI.DrawRectangle(spriteBatch, rect, Color.Red);

                    Vector2 limbBodyPos = new Vector2(
                        rect.X + limb.sprite.Origin.X,
                        rect.Y + limb.sprite.Origin.Y);
                    
                    DrawJoints(spriteBatch, limb, limbBodyPos);

                    if (limb.BodyShapeTexture == null) continue;

                    spriteBatch.Draw(limb.BodyShapeTexture, limbBodyPos,
                        null, Color.White, 0.0f,
                        new Vector2(limb.BodyShapeTexture.Width, limb.BodyShapeTexture.Height) / 2,
                        1.0f, SpriteEffects.None, 0.0f);

                    GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.White);
                    GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.White);

                    if (Vector2.Distance(PlayerInput.MousePosition, limbBodyPos)<5.0f && PlayerInput.LeftButtonDown())
                    {
                        limb.sprite.Origin -= PlayerInput.MouseSpeed;
                    }
                }

                y += textures[i].Height;
            }

            
            GUI.Draw((float)deltaTime, spriteBatch, cam);

            //EntityPrefab.DrawList(spriteBatch, new Vector2(20,50));

            //Entity.Edit(spriteBatch, cam);
                      
            spriteBatch.End();
        }

        private void UpdateLimbLists(Character character)
        {
            limbList.ClearChildren();
            foreach (Limb limb in character.animController.limbs)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0,0,0,25),
                    limb.type.ToString(),
                    Color.Transparent,
                    Color.Black,
                    Alignment.Left,
                    limbList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = limb;
            }

            jointList.ClearChildren();
            foreach (RevoluteJoint joint in character.animController.limbJoints)
            {
                Limb limb1 = (Limb)(joint.BodyA.UserData);
                Limb limb2 = (Limb)(joint.BodyB.UserData);

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    limb1.type.ToString() + " - " + limb2.type.ToString(),
                    Color.Transparent,
                    Color.Black,
                    Alignment.Left,
                    jointList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = joint;
            }
        }

        private void DrawJoints(SpriteBatch spriteBatch, Limb limb, Vector2 limbBodyPos)
        {
            foreach (var joint in editingCharacter.animController.limbJoints)
            {
                Vector2 jointPos = Vector2.Zero;

                if (joint.BodyA == limb.body.FarseerBody)
                {
                    jointPos = limbBodyPos + ConvertUnits.ToDisplayUnits(joint.LocalAnchorA);

                }
                else if (joint.BodyB == limb.body.FarseerBody)
                {
                    jointPos = limbBodyPos + ConvertUnits.ToDisplayUnits(joint.LocalAnchorB);
                }
                else
                {
                    continue;
                }

                GUI.DrawRectangle(spriteBatch, jointPos, new Vector2(5.0f, 5.0f), Color.Red, true);
                if (Vector2.Distance(PlayerInput.MousePosition, jointPos) < 6.0f)
                {
                    GUI.DrawRectangle(spriteBatch, jointPos - new Vector2(3.0f, 3.0f), new Vector2(11.0f, 11.0f), Color.Red, false);
                    if (PlayerInput.LeftButtonDown())
                    {
                        if (joint.BodyA == limb.body.FarseerBody)
                        {
                            joint.LocalAnchorA += ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed);
                        }
                        else
                        {
                            joint.LocalAnchorB += ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed);
                        }
                    }
                }
            }
        }

        private bool SelectLimb(object selection)
        {
            try
            {
                editingLimb = (Limb)selection;
                limbPanel = new GUIFrame(new Rectangle(300, 0, 500, 100), Color.Gray*0.8f);
                limbPanel.Padding = new Vector4(10.0f,10.0f,10.0f,10.0f);
                new GUITextBlock(new Rectangle(0, 0, 200, 25), editingLimb.type.ToString(), Color.Transparent, Color.Black, Alignment.Left, limbPanel);

                //spriteOrigin = new GUITextBlock(new Rectangle(0, 25, 200, 25), "Sprite origin: ", Color.White, Color.Black, Alignment.Left, limbPanel);
                
            }
            catch
            {
                return false;
            }
            return true;
        }

        private void EditLimb(SpriteBatch spriteBatch)
        {
            if (editingLimb == null) return;

            limbPanel.Draw(spriteBatch);
        }

        private bool TogglePhysics(GUIButton button, object selection)
        {
            physicsEnabled = !physicsEnabled;

            physicsButton.Text = (physicsEnabled) ? "Disable physics" : "Enable physics";

            return false;
        }
    }
}
