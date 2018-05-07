using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;

namespace Barotrauma
{
    class AnimationEditorScreen : Screen
    {
        private Camera cam;
        public override Camera Cam => cam;
        private GUIFrame gui;
        private Character character;

        public override void Select()
        {
            base.Select();
            //Submarine.RefreshSavedSubs();
            //Submarine.MainSub = Submarine.SavedSubmarines.First();
            //Submarine.MainSub.Load(true);

            //var spawnPos = WayPoint.GetRandom(sub: Submarine.MainSub).WorldPosition;
            var spawnPos = Vector2.Zero;
            character = Character.Create(Character.HumanConfigFile, spawnPos, hasAi: false);
            character.AnimController.forceStanding = true;
            Character.Controlled = character;
            //character.Submarine = Submarine.MainSub;
            //GameMain.World.ProcessChanges();
            cam = new Camera()
            {
                Position = character.WorldPosition,
                TargetPos = character.WorldPosition
            };
            cam.UpdateTransform(true);

            gui = new GUIFrame(new RectTransform(new Vector2(0.2f, 0.9f), parent: null, anchor: Anchor.CenterLeft) { RelativeOffset = new Vector2(0.01f, 0) });

            //new SerializableEntityEditor(HumanoidAnimParams.WalkInstance, false, editor, true);
            //new SerializableEntityEditor(HumanoidAnimParams.RunInstance, false, editor, true);

            var buttons = GUI.CreateButtons(1, new Vector2(0.9f, 0.1f), gui.RectTransform, anchor: Anchor.TopCenter, relativeSpacing: 0, startOffsetAbsolute: 30);
            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                switch (i)
                {
                    case 0:
                        button.Text = "Swim";
                        button.OnClicked += (b, obj) =>
                        {
                            character.AnimController.forceStanding = !character.AnimController.forceStanding;
                            button.Text = character.AnimController.forceStanding ? "Swim" : "Stand";
                            return true;
                        };
                        break;
                }

            }
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            gui.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            //Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
            //Submarine.MainSub.Update((float)deltaTime);

            //Vector2 mouseSimPos = ConvertUnits.ToSimUnits(character.CursorPosition);
            //foreach (Limb limb in character.AnimController.Limbs)
            //{
            //    limb.body.SetTransform(mouseSimPos, 0.0f);
            //}
            //character.AnimController.Collider.SetTransform(mouseSimPos, 0.0f);

            PhysicsBody.List.ForEach(pb => pb.SetPrevTransform(pb.SimPosition, pb.Rotation));

            character.ControlLocalPlayer((float)deltaTime, cam, false);
            character.Control((float)deltaTime, cam);
            character.AnimController.UpdateAnim((float)deltaTime);
            character.AnimController.Update((float)deltaTime, cam);

            cam.MoveCamera((float)deltaTime, allowMove: false, allowZoom: false);
            cam.Position = character.Position;

            GameMain.World.Step((float)deltaTime);

            gui.Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            base.Draw(deltaTime, graphics, spriteBatch);
            graphics.Clear(Color.CornflowerBlue);
            cam.UpdateTransform(true);

            // Submarine
            //spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: cam.Transform);
            //Submarine.Draw(spriteBatch, true);
            //spriteBatch.End();

            // Character
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: cam.Transform);
            character.Draw(spriteBatch);
            spriteBatch.End();

            // GUI
            spriteBatch.Begin(SpriteSortMode.Immediate, rasterizerState: GameMain.ScissorTestEnable);
            gui.Draw(spriteBatch);
            GUI.Draw((float)deltaTime, spriteBatch, cam);
            spriteBatch.End();
        }
    }
}
