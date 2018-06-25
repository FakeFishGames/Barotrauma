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
        private bool showWidgets = true;
        private bool showRagdollEditor;
        private bool showParamsEditor;

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

        private IEnumerable<Structure> AllWalls => clones.Concat(_originalWalls);

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
                    allFiles = GameMain.Instance.GetFilesOfType(ContentType.Character).Where(f => !f.Contains("husk")).ToList();
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
            if (characterIndex > AllFiles.Count - 1)
            {
                characterIndex = 0;
            }
        }

        private void ReduceIndex()
        {
            characterIndex--;
            if (characterIndex < 0)
            {
                characterIndex = AllFiles.Count - 1;
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
            SetWallCollisions(character.AnimController.forceStanding);
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
            Vector2 buttonSize = new Vector2(1, 0.05f);
            Vector2 toggleSize = new Vector2(0.03f, 0.03f);
            Point margin = new Point(40, 60);
            panel = new GUIFrame(new RectTransform(new Vector2(0.15f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.CenterRight) { RelativeOffset = new Vector2(0.01f, 0) });
            var layoutGroup = new GUILayoutGroup(new RectTransform(new Point(panel.Rect.Width - margin.X, panel.Rect.Height - margin.Y), panel.RectTransform, Anchor.Center));
            var charButtons = new GUIFrame(new RectTransform(buttonSize, parent: layoutGroup.RectTransform), style: null);
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
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Widgets")
            {
                Selected = true,
                OnSelected = (GUITickBox box) =>
                {
                    showWidgets = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Parameters")
            {
                OnSelected = (GUITickBox box) =>
                {
                    showParamsEditor = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Ragdoll")
            {
                OnSelected = (GUITickBox box) =>
                {
                    showRagdollEditor = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Swim")
            {
                Enabled = character.AnimController.CanWalk,
                Selected = character.AnimController is FishAnimController,
                OnSelected = (GUITickBox box) =>
                {
                    character.AnimController.forceStanding = !box.Selected;
                    SetWallCollisions(character.AnimController.forceStanding);
                    if (character.AnimController.forceStanding)
                    {
                        // Teleport
                        character.AnimController.SetPosition(ConvertUnits.ToSimUnits(spawnPosition), false);
                    }
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Auto Move")
            {
                OnSelected = (GUITickBox box) =>
                {
                    character.OverrideMovement = box.Selected ? new Vector2(-1, 0) as Vector2? : null;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Force Fast Speed")
            {
                OnSelected = (GUITickBox box) =>
                {
                    character.ForceRun = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Follow Cursor")
            {
                OnSelected = (GUITickBox box) =>
                {
                    character.dontFollowCursor = !box.Selected;
                    return true;
                }
            };
            var saveButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Save");
            saveButton.OnClicked += (b, obj) =>
            {
                AnimParams.ForEach(p => p.Save());
                return true;
            };
            var resetButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Reset");
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
            if (showParamsEditor)
            {
                AnimationParams.Editor.AddToGUIUpdateList();
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
            Submarine.MainSub.Update((float)deltaTime);

            PhysicsBody.List.ForEach(pb => pb.SetPrevTransform(pb.SimPosition, pb.Rotation));

            character.ControlLocalPlayer((float)deltaTime, Cam, false);
            character.Control((float)deltaTime, Cam);
            character.AnimController.UpdateAnim((float)deltaTime);
            character.AnimController.Update((float)deltaTime, Cam);

            if (character.Position.X < min)
            {
                CloneWalls(false);
            }
            else if (character.Position.X > max)
            {
                CloneWalls(true);
            }

            //Cam.TargetPos = Vector2.Zero;
            Cam.MoveCamera((float)deltaTime, allowMove: false, allowZoom: GUI.MouseOn == null);
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
            //character.AnimController.Collider.DebugDraw(spriteBatch, Color.LightGreen);
            spriteBatch.End();

            // GUI
            spriteBatch.Begin(SpriteSortMode.Immediate, rasterizerState: GameMain.ScissorTestEnable);
            Structure wall = clones.FirstOrDefault();
            Vector2 indicatorPos = wall == null ? OriginalWalls.First().DrawPosition : wall.DrawPosition;
            GUI.DrawIndicator(spriteBatch, indicatorPos, Cam, 700, GUI.SubmarineIcon, Color.White);
            GUI.Draw((float)deltaTime, spriteBatch);

            if (showWidgets)
            {
                DrawWidgetEditor(spriteBatch);
            }
            if (showRagdollEditor)
            {
                DrawJointEditor(spriteBatch);
            }

            // Debug
            if (GameMain.DebugDraw)
            {
                // Limb positions
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    Vector2 limbDrawPos = Cam.WorldToScreen(limb.WorldPosition);
                    GUI.DrawLine(spriteBatch, limbDrawPos + Vector2.UnitY * 5.0f, limbDrawPos - Vector2.UnitY * 5.0f, Color.White);
                    GUI.DrawLine(spriteBatch, limbDrawPos + Vector2.UnitX * 5.0f, limbDrawPos - Vector2.UnitX * 5.0f, Color.White);
                }

                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 0), $"Cursor World Pos: {character.CursorWorldPosition}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 20), $"Cursor Pos: {character.CursorPosition}", Color.White, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 40), $"Cursor Screen Pos: {PlayerInput.MousePosition}", Color.White, font: GUI.SmallFont);


                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 80), $"Character World Pos: {character.WorldPosition}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 100), $"Character Pos: {character.Position}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 120), $"Character Sim Pos: {character.SimPosition}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 140), $"Character Draw Pos: {character.DrawPosition}", Color.White, font: GUI.SmallFont);


                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 180), $"Submarine World Pos: {Submarine.MainSub.WorldPosition}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 200), $"Submarine Pos: {Submarine.MainSub.Position}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 220), $"Submarine Sim Pos: {Submarine.MainSub.Position}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 240), $"Submarine Draw Pos: {Submarine.MainSub.DrawPosition}", Color.White, font: GUI.SmallFont);

                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 280), $"Movement Limits: MIN: {min} MAX: {max}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 300), $"Clones: {clones.Count}", Color.White, font: GUI.SmallFont);
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 320), $"Total amount of walls: {Structure.WallList.Count}", Color.White, font: GUI.SmallFont);

                // Collider
                var collider = character.AnimController.Collider;
                var colliderDrawPos = SimToScreen(collider.SimPosition);
                Vector2 forward = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation));
                var endPos = SimToScreen(collider.SimPosition + forward * collider.radius);
                GUI.DrawLine(spriteBatch, colliderDrawPos, endPos, Color.LightGreen);
                GUI.DrawLine(spriteBatch, colliderDrawPos, SimToScreen(collider.SimPosition + forward * 0.25f), Color.Blue);
                Vector2 left = Vector2.Transform(-Vector2.UnitX, Matrix.CreateRotationZ(collider.Rotation));
                GUI.DrawLine(spriteBatch, colliderDrawPos, SimToScreen(collider.SimPosition + left * 0.25f), Color.Red);
                ShapeExtensions.DrawCircle(spriteBatch, colliderDrawPos, (endPos - colliderDrawPos).Length(), 40, Color.LightGreen);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 300, 0), $"Collider rotation: {MathHelper.ToDegrees(MathUtils.WrapAngleTwoPi(collider.Rotation))}", Color.White, font: GUI.SmallFont);
            }
            spriteBatch.End();
        }

        #region Helpers
        private Vector2 ScreenToSim(float x, float y) => ScreenToSim(new Vector2(x, y));
        private Vector2 ScreenToSim(Vector2 p) => ConvertUnits.ToSimUnits(Cam.ScreenToWorld(p));
        private Vector2 SimToScreen(float x, float y) => SimToScreen(new Vector2(x, y));
        private Vector2 SimToScreen(Vector2 p) => Cam.WorldToScreen(ConvertUnits.ToDisplayUnits(p));

        private void SetWallCollisions(bool enabled)
        {
            var collisionCategory = enabled ? FarseerPhysics.Dynamics.Category.Cat1 : FarseerPhysics.Dynamics.Category.None;
            AllWalls.ForEach(w => w.SetCollisionCategory(collisionCategory));
            GameMain.World.ProcessChanges();
        }
        #endregion

        #region Widgets
        private void DrawWidgetEditor(SpriteBatch spriteBatch)
        {
            var collider = character.AnimController.Collider;
            var charDrawPos = SimToScreen(collider.SimPosition);
            var animParams = character.AnimController.CurrentAnimationParams;
            var groundedParams = animParams as GroundedMovementParams;
            var humanGroundedParams = animParams as HumanGroundedParams;
            var fishGroundedParams = animParams as FishGroundedParams;
            var fishSwimParams = animParams as FishSwimParams;
            var humanSwimParams = animParams as HumanSwimParams;
            var head = character.AnimController.GetLimb(LimbType.Head);
            var torso = character.AnimController.GetLimb(LimbType.Torso);
            var tail = character.AnimController.GetLimb(LimbType.Tail);
            var legs = character.AnimController.GetLimb(LimbType.Legs);
            var thigh = character.AnimController.GetLimb(LimbType.RightThigh) ?? character.AnimController.GetLimb(LimbType.LeftThigh);
            var foot = character.AnimController.GetLimb(LimbType.RightFoot) ?? character.AnimController.GetLimb(LimbType.LeftFoot);
            var hand = character.AnimController.GetLimb(LimbType.RightHand) ?? character.AnimController.GetLimb(LimbType.LeftHand);
            var arm = character.AnimController.GetLimb(LimbType.RightArm) ?? character.AnimController.GetLimb(LimbType.LeftArm);
            int widgetDefaultSize = 10;
            // collider does not rotate when the sprite is flipped -> rotates only when swimming
            float dir = character.AnimController.Dir;
            Vector2 colliderBottom = character.AnimController.GetColliderBottom();
            Vector2 centerOfMass = character.AnimController.GetCenterOfMass();
            Vector2 simSpaceForward = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation));
            Vector2 simSpaceLeft = Vector2.Transform(-Vector2.UnitX, Matrix.CreateRotationZ(collider.Rotation));
            Vector2 screenSpaceForward = -VectorExtensions.Forward(collider.Rotation, 1);
            Vector2 screenSpaceLeft = screenSpaceForward.Right();
            // The forward vector is left or right in screen space when the unit is not swimming. Cannot rely on the collider here, because the rotation may vary on ground.
            Vector2 forward = animParams.IsSwimAnimation ? screenSpaceForward : Vector2.UnitX * dir;

            if (GameMain.DebugDraw)
            {
                //GUI.DrawLine(spriteBatch, charDrawPos, charDrawPos + screenSpaceForward * 40, Color.Blue);
                //GUI.DrawLine(spriteBatch, charDrawPos, charDrawPos + screenSpaceLeft * 40, Color.Red);
            }

            // Widgets for all anims -->
            // Speed
            float multiplier = 0.02f;
            Vector2 referencePoint = SimToScreen(collider.SimPosition);
            Vector2 drawPos = referencePoint;
            drawPos += forward * animParams.Speed / multiplier;
            DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 20, Color.Turquoise, "Speed", () =>
            {
                TryUpdateValue("speed", MathHelper.Clamp(animParams.Speed + Vector2.Multiply(PlayerInput.MouseSpeed, forward).Combine() * multiplier, 0.1f, Ragdoll.MAX_SPEED));
                GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.Turquoise);
            });
            GUI.DrawLine(spriteBatch, drawPos + forward * 10, drawPos + forward * 15, Color.Turquoise);
            if (head != null)
            {
                // Head angle
                DrawCircularWidget(spriteBatch, SimToScreen(head.SimPosition), animParams.HeadAngle, "Head Angle", Color.White, 
                    angle => TryUpdateValue("headangle", angle), circleRadius: 25, rotationOffset: collider.Rotation, clockWise: dir < 0);
                // Head position and leaning
                if (animParams.IsGroundedAnimation)
                {
                    if (humanGroundedParams != null)
                    {
                        drawPos = SimToScreen(head.SimPosition.X + humanGroundedParams.HeadLeanAmount * dir, head.pullJoint.WorldAnchorB.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Head", () =>
                        {
                            TryUpdateValue("headleanamount", humanGroundedParams.HeadLeanAmount + 0.01f * PlayerInput.MouseSpeed.X * dir);
                            TryUpdateValue("headposition", humanGroundedParams.HeadPosition + 0.015f * - PlayerInput.MouseSpeed.Y);
                            GUI.DrawLine(spriteBatch, drawPos, SimToScreen(head.SimPosition), Color.Red);
                        });
                        var origin = drawPos + new Vector2(widgetDefaultSize / 2, 0) * dir;
                        GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Red);
                    }
                    else
                    {
                        drawPos = SimToScreen(head.SimPosition.X, head.pullJoint.WorldAnchorB.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Head Position", () =>
                        {
                            TryUpdateValue("headposition", groundedParams.HeadPosition + 0.015f * -PlayerInput.MouseSpeed.Y);
                            GUI.DrawLine(spriteBatch, new Vector2(drawPos.X, 0), new Vector2(drawPos.X, GameMain.GraphicsHeight), Color.Red);
                        });
                    }
                }
            }
            if (torso != null)
            {
                // Torso angle
                DrawCircularWidget(spriteBatch, SimToScreen(torso.SimPosition), animParams.TorsoAngle, "Torso Angle", Color.White, 
                    angle => TryUpdateValue("torsoangle", angle), rotationOffset: collider.Rotation, clockWise: dir < 0);

                if (animParams.IsGroundedAnimation)
                {
                    // Torso position and leaning
                    if (humanGroundedParams != null)
                    {
                        drawPos = SimToScreen(torso.SimPosition.X + humanGroundedParams.TorsoLeanAmount * dir, torso.SimPosition.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Torso", () =>
                        {
                            TryUpdateValue("torsoleanamount", humanGroundedParams.TorsoLeanAmount + 0.01f * + PlayerInput.MouseSpeed.X * dir);
                            TryUpdateValue("torsoposition", humanGroundedParams.TorsoPosition + 0.015f * - PlayerInput.MouseSpeed.Y);
                            GUI.DrawLine(spriteBatch, drawPos, SimToScreen(torso.SimPosition), Color.Red);
                        });
                        var origin = drawPos + new Vector2(widgetDefaultSize / 2, 0) * dir;
                        GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Red);
                    }
                    else
                    {
                        drawPos = SimToScreen(torso.SimPosition.X, torso.pullJoint.WorldAnchorB.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Torso Position", () =>
                        {
                            TryUpdateValue("torsoposition", groundedParams.TorsoPosition + 0.015f * -PlayerInput.MouseSpeed.Y);
                            GUI.DrawLine(spriteBatch, new Vector2(drawPos.X, 0), new Vector2(drawPos.X, GameMain.GraphicsHeight), Color.Red);
                        });
                    }
                }
            }
            if (foot != null)
            {
                // Fish grounded only
                if (fishGroundedParams != null)
                {
                    DrawCircularWidget(spriteBatch, SimToScreen(colliderBottom), fishGroundedParams.FootRotation, "Foot Rotation", Color.White,
                        angle => TryUpdateValue("footrotation", angle), circleRadius: 25, rotationOffset: collider.Rotation, clockWise: dir < 0);
                }
                // Both
                if (groundedParams != null)
                {
                    multiplier = 0.01f;
                    referencePoint = SimToScreen(colliderBottom);
                    var v = groundedParams.StepSize / multiplier;
                    drawPos = referencePoint + new Vector2(v.X * dir, -v.Y);
                    var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                    DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Blue, "Step Size", () =>
                    {
                        var input = new Vector2(PlayerInput.MouseSpeed.X, PlayerInput.MouseSpeed.Y);
                        var transformedInput = input * dir;
                        if (dir > 0)
                        {
                            transformedInput.Y = -transformedInput.Y;
                        }
                        GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsHeight / 2, 20), "transformed input: " + transformedInput.ToString(), Color.White);
                        TryUpdateValue("stepsize", groundedParams.StepSize + transformedInput * multiplier);
                        GUI.DrawLine(spriteBatch, origin, referencePoint, Color.Blue);
                    });
                    GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Blue);
                }
            }
            // Human grounded only -->
            if (humanGroundedParams != null)
            {
                if (legs != null || foot != null)
                {
                    multiplier = 10;
                    drawPos = SimToScreen(colliderBottom + simSpaceForward * 0.3f);
                    DrawCircularWidget(spriteBatch, drawPos, humanGroundedParams.LegCorrectionTorque * multiplier, "Leg Correction Torque", Color.Chartreuse, angle =>
                    {
                        TryUpdateValue("legcorrectiontorque", angle / multiplier);
                        GUI.DrawString(spriteBatch, drawPos, humanGroundedParams.LegCorrectionTorque.FormatAsSingleDecimal(), Color.Black, Color.Chartreuse, font: GUI.SmallFont);
                    },circleRadius: 25, rotationOffset: collider.Rotation, clockWise: dir < 0, displayAngle: false);
                }
                if (hand != null || arm != null)
                {
                    multiplier = 0.02f;
                    referencePoint = charDrawPos;
                    var v = humanGroundedParams.HandMoveAmount / multiplier;
                    drawPos = referencePoint + new Vector2(v.X * dir, v.Y);
                    var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                    DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Blue, "Hand Move Amount", () =>
                    {
                        var transformedInput = new Vector2(PlayerInput.MouseSpeed.X * dir, PlayerInput.MouseSpeed.Y) * multiplier;
                        TryUpdateValue("handmoveamount", humanGroundedParams.HandMoveAmount + transformedInput);
                        GUI.DrawLine(spriteBatch, origin, referencePoint, Color.Blue);
                    });
                    GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Blue);
                }
            }
            // Fish swim only -->
            else if (tail != null && fishSwimParams != null)
            {
                float lengthMultiplier = 5;
                float amplitudeMultiplier = 200;
                referencePoint = charDrawPos - screenSpaceForward * ConvertUnits.ToDisplayUnits(collider.radius) * 2;
                drawPos = referencePoint;
                drawPos -= screenSpaceForward * fishSwimParams.WaveLength * lengthMultiplier;
                Vector2 toRefPoint = referencePoint - drawPos;
                var start = drawPos + toRefPoint / 2;
                var control = start + (screenSpaceLeft * dir * fishSwimParams.WaveAmplitude * amplitudeMultiplier);
                int points = 1000;
                // Length
                DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 15, Color.Purple, "Wave Length", () =>
                {
                    var input = Vector2.Multiply(PlayerInput.MouseSpeed, screenSpaceForward).Combine() / lengthMultiplier;
                    TryUpdateValue("wavelength", MathHelper.Clamp(fishSwimParams.WaveLength - input, 0, 100));
                    //GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, fishSwimParams.WaveAmplitude * amplitudeMultiplier, fishSwimParams.WaveLength * lengthMultiplier, 5000, points, Color.Purple);

                });
                // Amplitude
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.Purple, "Wave Amplitude", () =>
                {
                    var input = Vector2.Multiply(PlayerInput.MouseSpeed, screenSpaceLeft).Combine() / amplitudeMultiplier * dir;
                    TryUpdateValue("waveamplitude", MathHelper.Clamp(fishSwimParams.WaveAmplitude + input, -2, 2));
                    //GUI.DrawLine(spriteBatch, start, control, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, fishSwimParams.WaveAmplitude * amplitudeMultiplier, fishSwimParams.WaveLength * lengthMultiplier, 5000, points, Color.Purple);

                });
            }
            // Human swim only -->
            else if (humanSwimParams != null)
            {
                // Legs
                var amplitudeMultiplier = 20;
                var lengthMultiplier = 20;
                referencePoint = SimToScreen(character.SimPosition - simSpaceForward / 2);
                drawPos = referencePoint;
                drawPos -= screenSpaceForward * (humanSwimParams.LegCycleLength * lengthMultiplier);
                Vector2 toRefPoint = referencePoint - drawPos;
                var start = drawPos + toRefPoint / 2;
                var control = start + (screenSpaceLeft * dir * humanSwimParams.LegMoveAmount * amplitudeMultiplier);
                int points = 1000;
                // Cycle length
                DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 15, Color.Purple, "Leg Movement Speed", () =>
                {
                    float input = Vector2.Multiply(PlayerInput.MouseSpeed, screenSpaceForward).Combine() / lengthMultiplier;
                    TryUpdateValue("legcyclelength", MathHelper.Clamp(humanSwimParams.LegCycleLength - input, 0, 20));
                    //GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, humanSwimParams.LegMoveAmount * amplitudeMultiplier, humanSwimParams.LegCycleLength * lengthMultiplier, 5000, points, Color.Purple);
                });
                // Movement amount
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.Purple, "Leg Movement Amount", () =>
                {
                    float input = Vector2.Multiply(PlayerInput.MouseSpeed, screenSpaceLeft).Combine() / amplitudeMultiplier * dir;
                    TryUpdateValue("legmoveamount", MathHelper.Clamp(humanSwimParams.LegMoveAmount + input, -2, 2));
                    //GUI.DrawLine(spriteBatch, start, control, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, humanSwimParams.LegMoveAmount * amplitudeMultiplier, humanSwimParams.LegCycleLength * lengthMultiplier, 5000, points, Color.Purple);
                });
                // Arms
                multiplier = 0.01f;
                referencePoint = charDrawPos + screenSpaceForward * 10;
                Vector2 v = humanSwimParams.HandMoveAmount / multiplier;
                drawPos = referencePoint + new Vector2(v.X * dir, v.Y);
                var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Blue, "Hand Move Amount", () =>
                {
                    var transformedInput = new Vector2(PlayerInput.MouseSpeed.X * dir, PlayerInput.MouseSpeed.Y) * multiplier;
                    var handMovement = humanSwimParams.HandMoveAmount + transformedInput;
                    TryUpdateValue("handmoveamount", handMovement);
                    TryUpdateValue("handcyclespeed", handMovement.X * 4);
                    GUI.DrawLine(spriteBatch, origin, referencePoint, Color.Blue);
                });
                GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Blue);
            }
        }

        private void TryUpdateValue(string name, object value)
        {
            var animParams = character.AnimController.CurrentAnimationParams;
            if (animParams.SerializableProperties.TryGetValue(name, out SerializableProperty p))
            {
                animParams.SerializableEntityEditor.UpdateValue(p, value);
            }
        }

        private void DrawCircularWidget(SpriteBatch spriteBatch, Vector2 drawPos, float value, string toolTip, Color color, Action<float> onClick, 
            float circleRadius = 30, int widgetSize = 10, float rotationOffset = 0, bool clockWise = true, bool displayAngle = true)
        {
            var angle = value;
            if (!MathUtils.IsValid(angle))
            {
                angle = 0;
            }
            var up = -VectorExtensions.Forward(rotationOffset, circleRadius);
            var widgetDrawPos = drawPos + up;
            widgetDrawPos = MathUtils.RotatePointAroundTarget(widgetDrawPos, drawPos, angle, clockWise);
            GUI.DrawLine(spriteBatch, drawPos, widgetDrawPos, color);
            DrawWidget(spriteBatch, widgetDrawPos, WidgetType.Rectangle, 10, color, toolTip, () =>
            {
                GUI.DrawLine(spriteBatch, drawPos, drawPos + up, Color.Red);
                ShapeExtensions.DrawCircle(spriteBatch, drawPos, circleRadius, 40, color, thickness: 1);
                var rotationOffsetInDegrees = MathHelper.ToDegrees(MathUtils.WrapAnglePi(rotationOffset));
                // Collider rotation is counter-clockwise
                var transformedRot = clockWise ? angle - rotationOffsetInDegrees : angle + rotationOffsetInDegrees;
                if (transformedRot > 360)
                {
                    transformedRot -= 360;
                }
                else if (transformedRot < -360)
                {
                    transformedRot += 360;
                }
                //GUI.DrawString(spriteBatch, drawPos + Vector2.UnitX * 30, rotationOffsetInDegrees.FormatAsInt(), Color.Red);
                //GUI.DrawString(spriteBatch, drawPos + Vector2.UnitX * 30, transformedRot.FormatAsInt(), Color.Red);
                float x = PlayerInput.MouseSpeed.X * 1.5f;
                float y = PlayerInput.MouseSpeed.Y * 1.5f;
                if (clockWise)
                {
                    if ((transformedRot > 90 && transformedRot < 270) || (transformedRot < -90 && transformedRot > -270))
                    {
                        x = -x;
                    }
                    if (transformedRot > 180 || (transformedRot < 0 && transformedRot > -180))
                    {
                        y = -y;
                    }
                }
                else
                {
                    if (transformedRot < 90 && transformedRot > -90)
                    {
                        x = -x;
                    }
                    if (transformedRot < 0 && transformedRot > -180)
                    {
                        y = -y;
                    }
                }
                angle += x + y;
                if (angle > 360 || angle < -360)
                {
                    angle = 0;
                }
                if (displayAngle)
                {
                    GUI.DrawString(spriteBatch, drawPos, angle.FormatAsInt(), Color.Black, backgroundColor: color, font: GUI.SmallFont);
                }
                onClick(angle);
            });
        }

        public enum WidgetType { Rectangle, Circle }
        private string selectedWidget;
        private void DrawWidget(SpriteBatch spriteBatch, Vector2 drawPos, WidgetType widgetType, int size, Color color, string name, Action onPressed)
        {
            var drawRect = new Rectangle((int)drawPos.X - size / 2, (int)drawPos.Y - size / 2, size, size);
            var inputRect = drawRect;
            inputRect.Inflate(size, size);
            bool isMouseOn = inputRect.Contains(PlayerInput.MousePosition);
            // Unselect
            if (!isMouseOn && selectedWidget == name)
            {
                selectedWidget = null;
            }
            bool isSelected = isMouseOn && (selectedWidget == null || selectedWidget == name);
            switch (widgetType)
            {
                case WidgetType.Rectangle:
                    GUI.DrawRectangle(spriteBatch, drawRect, color, false, thickness: isSelected ? 3 : 1);
                    break;
                case WidgetType.Circle:
                    ShapeExtensions.DrawCircle(spriteBatch, drawPos, size / 2, 40, color, thickness: isSelected ? 3 : 1);
                    break;
                default: throw new NotImplementedException(widgetType.ToString());
            }
            if (isSelected)
            {
                selectedWidget = name;
                // Label/tooltip
                GUI.DrawString(spriteBatch, new Vector2(drawRect.Right + 5, drawRect.Y - drawRect.Height / 2), name, Color.White, Color.Black * 0.5f);
                if (PlayerInput.LeftButtonHeld())
                {
                    onPressed();
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
