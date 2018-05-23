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
        public override Camera Cam
        {
            get
            {
                if (cam == null)
                {
                    cam = new Camera();
                }
                return cam;
            }
        }
        private Character _character;

        public override void Select()
        {
            base.Select();
            //Submarine.RefreshSavedSubs();
            //Submarine.MainSub = Submarine.SavedSubmarines.First();
            //Submarine.MainSub.Load(true);

            //var spawnPos = WayPoint.GetRandom(sub: Submarine.MainSub).WorldPosition;

            //character.Submarine = Submarine.MainSub;
            //GameMain.World.ProcessChanges();
            _character = SpawnCharacter(Character.HumanConfigFile);
            Cam.UpdateTransform(true);

            var frame = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.9f), parent: Frame.RectTransform, anchor: Anchor.CenterRight) { RelativeOffset = new Vector2(0.01f, 0) });
            var layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform));

            var switchCharacterButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Switch Character");
            switchCharacterButton.OnClicked += (b, obj) =>
            {
                var configFile = GetConfigFile(_character.SpeciesName.Contains("human") ? "mantis" : "human");
                _character = SpawnCharacter(configFile);
                return true;
            };
            // TODO: use tick boxes?
            var swimButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Swim");
            swimButton.OnClicked += (b, obj) =>
            {
                _character.AnimController.forceStanding = !_character.AnimController.forceStanding;
                swimButton.Text = _character.AnimController.forceStanding ? "Swim" : "Stand";
                return true;
            };
            var moveButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Move");
            moveButton.OnClicked += (b, obj) =>
            {
                _character.OverrideMovement = _character.OverrideMovement.HasValue ? null : new Vector2(-1, 0) as Vector2?;
                moveButton.Text = _character.OverrideMovement.HasValue ? "Stop" : "Move";
                return true;
            };
            var runButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Walk");
            runButton.OnClicked += (b, obj) =>
            {
                _character.ForceRun = !_character.ForceRun;
                runButton.Text = _character.ForceRun ? "Walk" : "Run";
                return true;
            };
        }

        private string GetConfigFile(string speciesName)
        {
            var characterFiles = GameMain.SelectedPackage.GetFilesOfType(ContentType.Character);
            return characterFiles.Find(c => c.EndsWith(speciesName + ".xml"));
        }

        private Character SpawnCharacter(string configFile)
        {
            var spawnPos = Vector2.Zero;
            var character = Character.Create(configFile, spawnPos, ToolBox.RandomSeed(8), hasAi: false);
            // TODO: change
            character.AnimController.forceStanding = character.IsHumanoid;
            Character.Controlled = character;
            Cam.Position = character.WorldPosition;
            Cam.TargetPos = character.WorldPosition;
            return character;
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            HumanoidAnimParams.Editor.AddToGUIUpdateList();
        }

        public static void UpdateEditor(float deltaTime)
        {
            if (Character.Controlled == null) { return; }
            if (PlayerInput.KeyDown(Keys.LeftAlt) && PlayerInput.KeyHit(Keys.S))
            {
                HumanoidAnimParams.RunInstance.Save();
                HumanoidAnimParams.WalkInstance.Save();
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            UpdateEditor((float)deltaTime);

            //Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
            //Submarine.MainSub.Update((float)deltaTime);

            //Vector2 mouseSimPos = ConvertUnits.ToSimUnits(character.CursorPosition);
            //foreach (Limb limb in character.AnimController.Limbs)
            //{
            //    limb.body.SetTransform(mouseSimPos, 0.0f);
            //}
            //character.AnimController.Collider.SetTransform(mouseSimPos, 0.0f);

            PhysicsBody.List.ForEach(pb => pb.SetPrevTransform(pb.SimPosition, pb.Rotation));

            _character.ControlLocalPlayer((float)deltaTime, cam, false);
            _character.Control((float)deltaTime, cam);
            _character.AnimController.UpdateAnim((float)deltaTime);
            _character.AnimController.Update((float)deltaTime, cam);

            cam.MoveCamera((float)deltaTime, allowMove: false, allowZoom: false);
            cam.Position = _character.Position;

            GameMain.World.Step((float)deltaTime);
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
            _character.Draw(spriteBatch);
            spriteBatch.End();

            // GUI
            spriteBatch.Begin(SpriteSortMode.Immediate, rasterizerState: GameMain.ScissorTestEnable);
            GUI.Draw((float)deltaTime, spriteBatch);
            spriteBatch.End();
        }
    }
}
