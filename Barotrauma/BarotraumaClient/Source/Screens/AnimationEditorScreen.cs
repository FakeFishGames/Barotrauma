using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;

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

        private Character character;
        private Vector2 spawnPosition;

        public override void Select()
        {
            base.Select();
            Submarine.RefreshSavedSubs();
            Submarine.MainSub = Submarine.SavedSubmarines.First(s => s.Name.Contains("AnimEditor"));
            Submarine.MainSub.Load(true);
            CalculateMovementLimits();
            character = SpawnCharacter(Character.HumanConfigFile);
            AnimParams.ForEach(p => p.AddToEditor());
            CreateButtons();
        }

        #region Inifinite runner
        private int min;
        private int max;
        private void CalculateMovementLimits()
        {
            min = CurrentWalls.Select(w => w.Rect.Left).OrderBy(p => p).First();
            max = CurrentWalls.Select(w => w.Rect.Right).OrderBy(p => p).Last();
        }

        private List<Structure> _originalWalls;
        private List<Structure> OriginalWalls
        {
            get
            {
                if (_originalWalls == null)
                {
                    _originalWalls = Structure.WallList;
                }
                return _originalWalls;
            }
        }

        private List<Structure> clones = new List<Structure>();
        private List<Structure> previousWalls;

        private List<Structure> _currentWalls;
        private List<Structure> CurrentWalls
        {
            get
            {
                if (_currentWalls == null)
                {
                    _currentWalls = OriginalWalls;
                }
                return _currentWalls;
            }
            set
            {
                _currentWalls = value;
            }
        }

        private void CloneWalls(bool right)
        {
            previousWalls = CurrentWalls;
            if (previousWalls == null)
            {
                previousWalls = OriginalWalls;
            }
            if (clones.None())
            {
                OriginalWalls.ForEachMod(w => clones.Add(w.Clone() as Structure));
                CurrentWalls = clones;
            }
            else
            {
                // Select by position
                var lastWall = right ?
                    previousWalls.OrderBy(w => w.Rect.Right).Last() :
                    previousWalls.OrderBy(w => w.Rect.Left).First();

                CurrentWalls = clones.Contains(lastWall) ? clones : OriginalWalls;
            }
            if (CurrentWalls != OriginalWalls)
            {
                // Move the clones
                for (int i = 0; i < CurrentWalls.Count; i++)
                {
                    int amount = right ? previousWalls[i].Rect.Width : -previousWalls[i].Rect.Width;
                    CurrentWalls[i].Move(new Vector2(amount, 0));
                }
            }
            GameMain.World.ProcessChanges();
            CalculateMovementLimits();
        }
        #endregion

        #region Character spawning
        private int characterIndex = -1;
        private List<string> allFiles;
        private List<string> AllFiles
        {
            get
            {
                if (allFiles == null)
                {
                    allFiles = GameMain.SelectedPackage.GetFilesOfType(ContentType.Character).FindAll(f => !f.Contains("husk"));
                    allFiles.ForEach(f => DebugConsole.NewMessage(f, Color.White));
                }
                return allFiles;
            }
        }

        private string GetNextConfigFile()
        {
            if (characterIndex == -1)
            {
                characterIndex = AllFiles.IndexOf(GetConfigFile(character.SpeciesName));
            }
            characterIndex++;
            if (characterIndex == AllFiles.Count - 1)
            {
                characterIndex = 0;
            }
            return AllFiles[characterIndex];
        }

        private string GetConfigFile(string speciesName)
        {
            return AllFiles.Find(c => c.EndsWith(speciesName + ".xml"));
        }

        private Character SpawnCharacter(string configFile)
        {
            DebugConsole.NewMessage($"Trying to spawn {configFile}", Color.HotPink);
            spawnPosition = WayPoint.GetRandom(sub: Submarine.MainSub).WorldPosition;
            var character = Character.Create(configFile, spawnPosition, ToolBox.RandomSeed(8), hasAi: false);
            character.Submarine = Submarine.MainSub;
            character.AnimController.forceStanding = character.IsHumanoid;
            Character.Controlled = character;
            float size = ConvertUnits.ToDisplayUnits(character.AnimController.Collider.radius * 2);
            float margin = 100;
            float distance = Vector2.Distance(spawnPosition, new Vector2(spawnPosition.X, OriginalWalls.First().WorldPosition.Y)) - margin;
            if (size > distance)
            {
                character.AnimController.Teleport(ConvertUnits.ToSimUnits(new Vector2(0, size * 1.5f)), Vector2.Zero);
            }
            Cam.Position = character.WorldPosition;
            Cam.TargetPos = character.WorldPosition;
            Cam.UpdateTransform(true);
            GameMain.World.ProcessChanges();
            return character;
        }
        #endregion

        #region GUI
        private GUIFrame frame;
        private void CreateButtons()
        {
            if (frame != null)
            {
                frame.RectTransform.Parent = null;
            }
            frame = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.9f), parent: Frame.RectTransform, anchor: Anchor.CenterRight) { RelativeOffset = new Vector2(0.01f, 0) });
            var layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform));
            var switchCharacterButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Switch Character");
            switchCharacterButton.OnClicked += (b, obj) =>
            {
                character = SpawnCharacter(GetNextConfigFile());
                ResetEditor();
                CreateButtons();
                return true;
            };
            // TODO: use tick boxes?
            var swimButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), character.AnimController.forceStanding ? "Swim" : "Grounded");
            swimButton.OnClicked += (b, obj) =>
            {
                character.AnimController.forceStanding = !character.AnimController.forceStanding;
                swimButton.Text = character.AnimController.forceStanding ? "Swim" : "Grounded";
                return true;
            };
            swimButton.Enabled = character.AnimController.CanWalk;
            var moveButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), character.OverrideMovement.HasValue ? "Stop" : "Move");
            moveButton.OnClicked += (b, obj) =>
            {
                character.OverrideMovement = character.OverrideMovement.HasValue ? null : new Vector2(-1, 0) as Vector2?;
                moveButton.Text = character.OverrideMovement.HasValue ? "Stop" : "Move";
                return true;
            };
            var speedButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), character.ForceRun ? "Slow" : "Fast");
            speedButton.OnClicked += (b, obj) =>
            {
                character.ForceRun = !character.ForceRun;
                speedButton.Text = character.ForceRun ? "Slow" : "Fast";
                return true;
            };
            var turnButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), character.dontFollowCursor ? "Enable Turning" : "Disable Turning");
            turnButton.OnClicked += (b, obj) =>
            {
                character.dontFollowCursor = !character.dontFollowCursor;
                turnButton.Text = character.dontFollowCursor ? "Enable Turning" : "Disable Turning";
                return true;
            };
            var saveButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Save");
            saveButton.OnClicked += (b, obj) =>
            {
                AnimParams.ForEach(p => p.Save());
                return true;
            };
            var resetButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Reset");
            resetButton.OnClicked += (b, obj) =>
            {
                AnimParams.ForEach(p => p.Reset());
                ResetEditor();
                return true;
            };
        }
        #endregion

        #region AnimParams
        private List<AnimationParams> AnimParams => character.AnimController.AllAnimParams;

        private void ResetEditor()
        {
            AnimationParams.CreateEditor();
            AnimParams.ForEach(p => p.AddToEditor());
        }
        #endregion

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            AnimationParams.Editor.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
            Submarine.MainSub.Update((float)deltaTime);

            //Vector2 mouseSimPos = ConvertUnits.ToSimUnits(character.CursorPosition);
            //foreach (Limb limb in character.AnimController.Limbs)
            //{
            //    limb.body.SetTransform(mouseSimPos, 0.0f);
            //}
            //character.AnimController.Collider.SetTransform(mouseSimPos, 0.0f);

            PhysicsBody.List.ForEach(pb => pb.SetPrevTransform(pb.SimPosition, pb.Rotation));

            character.ControlLocalPlayer((float)deltaTime, Cam, false);
            character.Control((float)deltaTime, Cam);
            character.AnimController.UpdateAnim((float)deltaTime);
            character.AnimController.Update((float)deltaTime, Cam);

            // Teleports the character -> not very smooth
            //if (_character.Position.X < min || _character.Position.X > max)
            //{
            //    _character.AnimController.SetPosition(ConvertUnits.ToSimUnits(new Vector2(spawnPosition.X, _character.Position.Y)), false);
            //}

            if (character.Position.X < min)
            {
                CloneWalls(false);
            }
            else if (character.Position.X > max)
            {
                CloneWalls(true);
            }

            Cam.MoveCamera((float)deltaTime);
            Cam.Position = character.Position;

            GameMain.World.Step((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            base.Draw(deltaTime, graphics, spriteBatch);
            graphics.Clear(Color.CornflowerBlue);
            Cam.UpdateTransform(true);

            // Submarine
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            Submarine.Draw(spriteBatch, true);
            spriteBatch.End();

            // Character
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            character.Draw(spriteBatch);
            spriteBatch.End();

            // GUI
            spriteBatch.Begin(SpriteSortMode.Immediate, rasterizerState: GameMain.ScissorTestEnable);
            Structure clone = clones.FirstOrDefault();
            Vector2 pos = clone == null ? OriginalWalls.First().DrawPosition : clone.DrawPosition;
            GUI.DrawIndicator(spriteBatch, pos, Cam, 700, GUI.SubmarineIcon, Color.White);
            GUI.Draw((float)deltaTime, spriteBatch);

            DrawWidgetEditor(spriteBatch);
            //DrawJointEditor(spriteBatch);

            // Debug
            if (GameMain.DebugDraw)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 00), $"Cursor World Pos: {character.CursorWorldPosition}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 20), $"Cursor Pos: {character.CursorPosition}", Color.White, font: GUI.SmallFont);

                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 60), $"Character World Pos: {character.WorldPosition}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 80), $"Character Pos: {character.Position}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 100), $"Character Sim Pos: {character.SimPosition}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 120), $"Character Draw Pos: {character.DrawPosition}", Color.White, font: GUI.SmallFont);


                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 160), $"Submarine World Pos: {Submarine.MainSub.WorldPosition}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 180), $"Submarine Pos: {Submarine.MainSub.Position}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 200), $"Submarine Sim Pos: {Submarine.MainSub.Position}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 220), $"Submarine Draw Pos: {Submarine.MainSub.DrawPosition}", Color.White, font: GUI.SmallFont);

                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 260), $"Movement Limits: MIN: {min} MAX: {max}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 280), $"Clones: {clones.Count}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 300), $"Total amount of walls: {Structure.WallList.Count}", Color.White, font: GUI.SmallFont);
            }

            spriteBatch.End();
        }

        #region Widgets (test)
        private void DrawWidgetEditor(SpriteBatch spriteBatch)
        {
            foreach (Limb limb in character.AnimController.Limbs)
            {
                Vector2 limbBodyPos = Cam.WorldToScreen(limb.WorldPosition);
                GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.White);
                GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.White);

                if (limb.type == LimbType.Head)
                {
                    // raycast?
                    if (PlayerInput.LeftButtonHeld())
                    {
                        var currentAnimParams = character.AnimController.CurrentAnimationParams;
                        if (currentAnimParams is GroundedMovementParams grounded)
                        {
                            grounded.HeadPosition += 0.01f * PlayerInput.MouseSpeed.Y;
                            var point = Cam.WorldToScreen(ConvertUnits.ToDisplayUnits(limb.pullJoint.WorldAnchorB)).ToPoint();
                            GUI.DrawRectangle(spriteBatch, new Rectangle(point, new Point(5, 5)), Color.Red);
                            // TODO: update the value in the editor
                        }
                    }
                }
            }
        }
        #endregion

        #region Joint edit (test)
        private void DrawJointEditor(SpriteBatch spriteBatch)
        {
            foreach (Limb limb in character.AnimController.Limbs)
            {
                Vector2 limbBodyPos = Cam.WorldToScreen(limb.WorldPosition);
                GUI.DrawRectangle(spriteBatch, new Rectangle(limbBodyPos.ToPoint(), new Point(5, 5)), Color.Red);

                DrawJoints(spriteBatch, limb, limbBodyPos);

                GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.White);
                GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.White);

                if (Vector2.Distance(PlayerInput.MousePosition, limbBodyPos) < 5.0f && PlayerInput.LeftButtonHeld())
                {
                    limb.sprite.Origin += PlayerInput.MouseSpeed;
                }
            }
        }

        private void DrawJoints(SpriteBatch spriteBatch, Limb limb, Vector2 limbBodyPos)
        {
            foreach (var joint in character.AnimController.LimbJoints)
            {
                Vector2 jointPos = Vector2.Zero;

                if (joint.BodyA == limb.body.FarseerBody)
                {
                    jointPos = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA);

                }
                else if (joint.BodyB == limb.body.FarseerBody)
                {
                    jointPos = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB);
                }
                else
                {
                    continue;
                }

                Vector2 tformedJointPos = jointPos /= limb.Scale;
                tformedJointPos.Y = -tformedJointPos.Y;
                tformedJointPos += limbBodyPos;

                if (joint.BodyA == limb.body.FarseerBody)
                {
                    float a1 = joint.UpperLimit - MathHelper.PiOver2;
                    float a2 = joint.LowerLimit - MathHelper.PiOver2;
                    float a3 = (a1 + a2) / 2.0f;
                    GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + new Vector2((float)Math.Cos(a1), -(float)Math.Sin(a1)) * 30.0f, Color.Green);
                    GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + new Vector2((float)Math.Cos(a2), -(float)Math.Sin(a2)) * 30.0f, Color.DarkGreen);

                    GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + new Vector2((float)Math.Cos(a3), -(float)Math.Sin(a3)) * 30.0f, Color.LightGray);
                }

                GUI.DrawRectangle(spriteBatch, tformedJointPos, new Vector2(5.0f, 5.0f), Color.Red, true);
                if (Vector2.Distance(PlayerInput.MousePosition, tformedJointPos) < 10.0f)
                {
                    GUI.DrawString(spriteBatch, tformedJointPos + Vector2.One * 10.0f, jointPos.ToString(), Color.White, Color.Black * 0.5f);
                    GUI.DrawRectangle(spriteBatch, tformedJointPos - new Vector2(3.0f, 3.0f), new Vector2(11.0f, 11.0f), Color.Red, false);
                    if (PlayerInput.LeftButtonHeld())
                    {
                        Vector2 speed = ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed);
                        speed.Y = -speed.Y;
                        if (joint.BodyA == limb.body.FarseerBody)
                        {
                            joint.LocalAnchorA += speed;
                        }
                        else
                        {
                            joint.LocalAnchorB += speed;
                        }
                    }
                }
            }
        }
        #endregion
    }
}
