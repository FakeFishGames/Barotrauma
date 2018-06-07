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
            Submarine.MainSub.GodMode = true;
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
            CheckAndGetIndex();
            IncreaseIndex();
            return AllFiles[characterIndex];
        }

        private string GetPreviousConfigFile()
        {
            CheckAndGetIndex();
            ReduceIndex();
            return AllFiles[characterIndex];
        }

        // Check if the index is not set, in which case we'll get the index from the current species name.
        private void CheckAndGetIndex()
        {
            if (characterIndex == -1)
            {
                characterIndex = AllFiles.IndexOf(GetConfigFile(character.SpeciesName));
            }
        }

        private void IncreaseIndex()
        {
            characterIndex++;
            if (characterIndex == AllFiles.Count - 1)
            {
                characterIndex = 0;
            }
        }

        private void ReduceIndex()
        {
            characterIndex--;
            if (characterIndex == AllFiles.Count - 1)
            {
                characterIndex = 0;
            }
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
            character.dontFollowCursor = true;
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
        private GUIFrame panel;
        private void CreateButtons()
        {
            if (panel != null)
            {
                panel.RectTransform.Parent = null;
            }
            panel = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.9f), parent: Frame.RectTransform, anchor: Anchor.CenterRight) { RelativeOffset = new Vector2(0.01f, 0) });
            var layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, panel.RectTransform));
            var charButtons = new GUIFrame(new RectTransform(new Vector2(1, 0.1f), parent: layoutGroup.RectTransform), style: null);
            var prevCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1), charButtons.RectTransform, Anchor.TopLeft), "Previous \nCharacter");
            prevCharacterButton.OnClicked += (b, obj) =>
            {
                character = SpawnCharacter(GetPreviousConfigFile());
                ResetEditor();
                CreateButtons();
                return true;
            };
            var nextCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1), charButtons.RectTransform, Anchor.TopRight), "Next \nCharacter");
            nextCharacterButton.OnClicked += (b, obj) =>
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

        #region Widgets
        private void DrawWidgetEditor(SpriteBatch spriteBatch)
        {
            foreach (Limb limb in character.AnimController.Limbs)
            {
                Vector2 limbBodyPos = Cam.WorldToScreen(limb.WorldPosition);
                // Limb positions
                GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.White);
                GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.White);
                var animParams = character.AnimController.CurrentAnimationParams;
                var groundedParams = animParams as GroundedMovementParams;
                var humanGroundedParams = animParams as HumanGroundedParams;
                // TODO: could we get the value directly from the mouse position?
                //var newPos = ConvertUnits.ToSimUnits(Cam.ScreenToWorld(PlayerInput.MousePosition)) - character.AnimController.GetColliderBottom();
                //grounded.HeadPosition = newPos.Y;
                Point widgetSize = new Point(10, 10);
                switch (limb.type)
                {
                    case LimbType.Head:
                        if (animParams.IsGroundedAnimation)
                        {
                            if (humanGroundedParams != null)
                            {
                                Vector2 drawPoint = SimToScreenPoint(limb.SimPosition.X - humanGroundedParams.HeadLeanAmount, limb.pullJoint.WorldAnchorB.Y);
                                DrawWidget(drawPoint.ToPoint(), spriteBatch, groundedParams, limb, widgetSize, Color.Red, "Head", () =>
                                {
                                    humanGroundedParams.HeadLeanAmount += 0.01f * -PlayerInput.MouseSpeed.X;
                                    humanGroundedParams.HeadPosition += 0.015f * -PlayerInput.MouseSpeed.Y;
                                    SerializableProperty p;
                                    if (humanGroundedParams.SerializableProperties.TryGetValue("headleanamount", out p))
                                    {
                                        RefreshField(p, animParams, humanGroundedParams.HeadLeanAmount);
                                    }
                                    if (humanGroundedParams.SerializableProperties.TryGetValue("headposition", out p))
                                    {
                                        RefreshField(p, animParams, humanGroundedParams.HeadPosition);
                                    }
                                });
                                var origin = drawPoint - new Vector2(widgetSize.X / 2, 0);
                                GUI.DrawLine(spriteBatch, origin, origin - Vector2.UnitX * 5, Color.Red);
                            }
                            else
                            {
                                // TODO: implement head leaning on fishes?
                                Vector2 drawPoint = SimToScreenPoint(limb.SimPosition.X, limb.pullJoint.WorldAnchorB.Y);
                                DrawWidget(drawPoint.ToPoint(), spriteBatch, groundedParams, limb, widgetSize, Color.Red, "Head Position",
                                    () => groundedParams.HeadPosition += 0.015f * -PlayerInput.MouseSpeed.Y);
                            }
                        }
                        else if (animParams.IsSwimAnimation)
                        {
                        }
                        break;
                    case LimbType.Torso:
                        if (animParams.IsGroundedAnimation)
                        {
                            if (humanGroundedParams != null)
                            {
                                Vector2 drawPoint = SimToScreenPoint(limb.SimPosition.X - humanGroundedParams.TorsoLeanAmount, limb.SimPosition.Y);
                                DrawWidget(drawPoint.ToPoint(), spriteBatch, groundedParams, limb, widgetSize, Color.Red, "Torso", () =>
                                {
                                    humanGroundedParams.TorsoLeanAmount += 0.01f * -PlayerInput.MouseSpeed.X;
                                    humanGroundedParams.TorsoPosition += 0.015f * -PlayerInput.MouseSpeed.Y;
                                    SerializableProperty p;
                                    if (humanGroundedParams.SerializableProperties.TryGetValue("torsoleanamount", out p))
                                    {
                                        RefreshField(p, animParams, humanGroundedParams.TorsoLeanAmount);
                                    }
                                    if (humanGroundedParams.SerializableProperties.TryGetValue("torsoposition", out p))
                                    {
                                        RefreshField(p, animParams, humanGroundedParams.TorsoPosition);
                                    }
                                });
                                var origin = drawPoint - new Vector2(widgetSize.X / 2, 0);
                                GUI.DrawLine(spriteBatch, origin, origin - Vector2.UnitX * 5, Color.Red);
                            }
                            else
                            {
                                // TODO: implement torso leaning on fishes?
                                Vector2 drawPoint = SimToScreenPoint(limb.SimPosition.X, limb.pullJoint.WorldAnchorB.Y);
                                DrawWidget(drawPoint.ToPoint(), spriteBatch, groundedParams, limb, widgetSize, Color.Red, "Torso Position",
                                    () => groundedParams.TorsoPosition += 0.015f * -PlayerInput.MouseSpeed.Y);
                            }
                        }
                        else if (animParams.IsSwimAnimation)
                        {
                        }
                        break;
                }
            }
        }

        // Note: currently only handles floats.
        private void RefreshField(SerializableProperty property, AnimationParams animationParams, object newValue)
        {
            if (!animationParams.SerializableEntityEditor.Fields.TryGetValue(property, out IEnumerable<GUIComponent> fields))
            {
                return;
            }
            if (newValue is float f)
            {
                foreach (var field in fields)
                {
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Float)
                        {
                            numInput.FloatValue = f;
                        }
                    }
                }
            }
        }

        private Vector2 SimToScreenPoint(float x, float y) => Cam.WorldToScreen(ConvertUnits.ToDisplayUnits(new Vector2(x, y)));
        private Vector2 SimToScreenPoint(Vector2 p) => Cam.WorldToScreen(ConvertUnits.ToDisplayUnits(p));

        private void DrawWidget(Point drawPoint, SpriteBatch spriteBatch, GroundedMovementParams animParams, Limb limb, Point size, Color color, string text, Action onPressed, int thickness = 3)
        {
            var drawRect = new Rectangle(new Point(drawPoint.X - size.X / 2, drawPoint.Y - size.Y / 2), size);
            var inputRect = drawRect;
            inputRect.Inflate(size.X, size.Y);
            // TODO: allow to select only one widget at a time
            bool isSelected = inputRect.Contains(PlayerInput.MousePosition);
            if (isSelected)
            {
                // Label/tooltip
                GUI.DrawString(spriteBatch, new Vector2(drawRect.Right + 5, drawRect.Y - drawRect.Height / 2), text, Color.White, Color.Black * 0.5f);
                GUI.DrawRectangle(spriteBatch, drawRect, color, false, thickness: thickness);
                if (PlayerInput.LeftButtonHeld())
                {
                    onPressed();
                }
            }
            else
            {
                GUI.DrawRectangle(spriteBatch, drawRect, color);
                //ShapeExtensions.DrawCircle(spriteBatch, drawPoint.ToVector2(), 10, 10, Color.White, thickness: 1);
            }
            // Bezier test
            //if (PlayerInput.LeftButtonHeld())
            //{
            //    Vector2 start = drawPoint.ToVector2();
            //    Vector2 end = start + new Vector2(50, 0);
            //    Vector2 dir = end - start;
            //    Vector2 control = start + dir / 2 + new Vector2(0, -20);
            //    var points = new Vector2[10];
            //    for (int i = 0; i < points.Length; i++)
            //    {
            //        float t = (float)i / (points.Length - 1);
            //        Vector2 pos = MathUtils.Bezier(start, control, end, t);
            //        points[i] = pos;
            //        //DebugConsole.NewMessage(i.ToString(), Color.White);
            //        //DebugConsole.NewMessage(t.ToString(), Color.Blue);
            //        //DebugConsole.NewMessage(pos.ToString(), Color.Red);
            //        ShapeExtensions.DrawPoint(spriteBatch, pos, Color.White, size: 2);
            //    }
            //}
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
