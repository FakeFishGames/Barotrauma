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
        private bool showControls = true;
        private bool editOffsets;
        private bool editJointPositions;
        private bool editJointLimits;
        private bool showParamsEditor;
        private bool showSpritesheet;
        private bool freeze;

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
            CreateTextures(character);
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
            var controlsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Controls") { Selected = showControls };
            var paramsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Parameters") { Selected = showParamsEditor };
            var offsetsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Offsets") { Selected = editOffsets };
            var jointPositionsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Joint Positions") { Selected = editJointPositions };
            var jointLimitsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Joints Limits") { Selected = editJointLimits };
            var spritesheetToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Spritesheet") { Selected = showSpritesheet };
            var physicsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Freeze") { Selected = freeze };
            controlsToggle.OnSelected = box =>
            {
                showControls = box.Selected;
                if (showControls)
                {
                    spritesheetToggle.Selected = false;
                    offsetsToggle.Selected = false;
                    jointPositionsToggle.Selected = false;
                    jointLimitsToggle.Selected = false;
                }
                return true;
            };
            paramsToggle.OnSelected = box =>
            {
                showParamsEditor = box.Selected;
                if (showParamsEditor)
                {
                    spritesheetToggle.Selected = false;
                    offsetsToggle.Selected = false;
                    jointPositionsToggle.Selected = false;
                    jointLimitsToggle.Selected = false;
                }
                return true;
            };
            offsetsToggle.OnSelected = box =>
            {
                editOffsets = box.Selected;
                if (editOffsets)
                {
                    jointPositionsToggle.Selected = false;
                    jointLimitsToggle.Selected = false;
                    spritesheetToggle.Selected = true;
                }
                return true;
            };
            jointPositionsToggle.OnSelected = box =>
            {
                editJointPositions = box.Selected;
                if (editJointPositions)
                {
                    offsetsToggle.Selected = false;
                    spritesheetToggle.Selected = true;
                }
                return true;
            };
            jointLimitsToggle.OnSelected = box =>
            {
                editJointLimits = box.Selected;
                if (editJointLimits)
                {
                    offsetsToggle.Selected = false;
                    spritesheetToggle.Selected = true;
                }
                return true;
            };
            spritesheetToggle.OnSelected = box =>
            {
                showSpritesheet = box.Selected;
                if (showSpritesheet)
                {
                    controlsToggle.Selected = false;
                    paramsToggle.Selected = false;
                }
                return true;
            };
            physicsToggle.OnSelected = box =>
            {
                freeze = box.Selected;
                return true;
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
            if (!freeze)
            {
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
                GameMain.World.Step((float)deltaTime);
            }
            //Cam.TargetPos = Vector2.Zero;
            Cam.MoveCamera((float)deltaTime, allowMove: false, allowZoom: GUI.MouseOn == null);
            Cam.Position = character.Position;
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
            if (showControls)
            {
                DrawAnimationControls(spriteBatch);
            }
            if (editOffsets)
            {
                DrawOffsetEditor(spriteBatch);
            }
            if (editJointPositions || editJointLimits)
            {
                DrawJointEditor(spriteBatch);
            }
            if (showSpritesheet)
            {
                DrawSpritesheetEditor(spriteBatch);
            }
            GUI.Draw((float)deltaTime, spriteBatch);

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
                //Vector2 left = Vector2.Transform(-Vector2.UnitX, Matrix.CreateRotationZ(collider.Rotation));
                //Vector2 left = -Vector2.UnitX.TransformVector(forward);
                Vector2 left = -forward.Right();
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

        #region Animation Controls
        private void DrawAnimationControls(SpriteBatch spriteBatch)
        {
            var collider = character.AnimController.Collider;
            var colliderDrawPos = SimToScreen(collider.SimPosition);
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
            Vector2 referencePoint = SimToScreen(head != null ? head.SimPosition : collider.SimPosition);
            Vector2 drawPos = referencePoint;
            float multiplier = 0.015f;
            drawPos += forward * animParams.Speed / multiplier * Cam.Zoom;
            DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 20, Color.Turquoise, "Movement Speed", () =>
            {
                float speed = animParams.Speed + Vector2.Multiply(PlayerInput.MouseSpeed, forward).Combine() * multiplier / Cam.Zoom;
                TryUpdateValue("speed", MathHelper.Clamp(speed, 0.1f, Ragdoll.MAX_SPEED));
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
                            float input = 0.006f * PlayerInput.MouseSpeed.X / Cam.Zoom * dir;
                            TryUpdateValue("headleanamount", humanGroundedParams.HeadLeanAmount + input);
                            input = 0.015f * PlayerInput.MouseSpeed.Y / Cam.Zoom;
                            TryUpdateValue("headposition", humanGroundedParams.HeadPosition - input);
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
                            float v = groundedParams.HeadPosition - ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed.Y / Cam.Zoom);
                            TryUpdateValue("headposition", v);
                            GUI.DrawLine(spriteBatch, new Vector2(drawPos.X, 0), new Vector2(drawPos.X, GameMain.GraphicsHeight), Color.Red);
                        });
                    }
                }
            }
            if (torso != null)
            {
                referencePoint = torso.SimPosition;
                if (animParams is HumanGroundedParams || animParams is HumanSwimParams)
                {
                    referencePoint -= simSpaceForward * 0.25f;
                }
                // Torso angle
                DrawCircularWidget(spriteBatch, SimToScreen(referencePoint), animParams.TorsoAngle, "Torso Angle", Color.White,
                    angle => TryUpdateValue("torsoangle", angle), rotationOffset: collider.Rotation, clockWise: dir < 0);

                if (animParams.IsGroundedAnimation)
                {
                    // Torso position and leaning
                    if (humanGroundedParams != null)
                    {
                        drawPos = SimToScreen(torso.SimPosition.X + humanGroundedParams.TorsoLeanAmount * dir, torso.SimPosition.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Torso", () =>
                        {
                            float input = 0.005f * PlayerInput.MouseSpeed.X / Cam.Zoom * dir;
                            TryUpdateValue("torsoleanamount", humanGroundedParams.TorsoLeanAmount + input);
                            input = 0.02f * PlayerInput.MouseSpeed.Y / Cam.Zoom;
                            TryUpdateValue("torsoposition", humanGroundedParams.TorsoPosition - input);
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
                            float v = groundedParams.TorsoPosition - ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed.Y / Cam.Zoom);
                            TryUpdateValue("torsoposition", v);
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
                    drawPos = referencePoint + new Vector2(v.X * dir, -v.Y) * Cam.Zoom;
                    var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                    DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Blue, "Step Size", () =>
                    {
                        var input = new Vector2(PlayerInput.MouseSpeed.X, PlayerInput.MouseSpeed.Y);
                        var transformedInput = input * dir;
                        if (dir > 0)
                        {
                            transformedInput.Y = -transformedInput.Y;
                        }
                        TryUpdateValue("stepsize", groundedParams.StepSize + transformedInput * multiplier / Cam.Zoom);
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
                    DrawCircularWidget(spriteBatch, drawPos, humanGroundedParams.LegCorrectionTorque * multiplier, "Leg Angle", Color.Chartreuse, angle =>
                    {
                        TryUpdateValue("legcorrectiontorque", angle / multiplier);
                        GUI.DrawString(spriteBatch, drawPos, humanGroundedParams.LegCorrectionTorque.FormatAsSingleDecimal(), Color.Black, Color.Chartreuse, font: GUI.SmallFont);
                    }, circleRadius: 25, rotationOffset: collider.Rotation, clockWise: dir < 0, displayAngle: false);
                }
                if (hand != null || arm != null)
                {
                    multiplier = 0.02f;
                    referencePoint = SimToScreen(collider.SimPosition + simSpaceForward * 0.2f);
                    var v = humanGroundedParams.HandMoveAmount / multiplier;
                    drawPos = referencePoint + new Vector2(v.X * dir, v.Y) * Cam.Zoom;
                    var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                    DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Blue, "Hand Move Amount", () =>
                    {
                        var transformedInput = new Vector2(PlayerInput.MouseSpeed.X * dir, PlayerInput.MouseSpeed.Y) * multiplier / Cam.Zoom;
                        TryUpdateValue("handmoveamount", humanGroundedParams.HandMoveAmount + transformedInput);
                        GUI.DrawLine(spriteBatch, origin, referencePoint, Color.Blue);
                    });
                    GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Blue);
                }
            }
            // Fish swim only -->
            else if (tail != null && fishSwimParams != null)
            {
                float lengthMultiplier = 5 * Cam.Zoom;
                float amplitudeMultiplier = 200 * Cam.Zoom;
                referencePoint = colliderDrawPos - screenSpaceForward * ConvertUnits.ToDisplayUnits(collider.radius) * 2 * Cam.Zoom;
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
                    TryUpdateValue("wavelength", MathHelper.Clamp(fishSwimParams.WaveLength - input, 0, 150));
                    //GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, fishSwimParams.WaveAmplitude * amplitudeMultiplier, fishSwimParams.WaveLength * lengthMultiplier, 5000 * Cam.Zoom, points, Color.Purple);

                });
                // Amplitude
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.Purple, "Wave Amplitude", () =>
                {
                    var input = Vector2.Multiply(PlayerInput.MouseSpeed, screenSpaceLeft).Combine() / amplitudeMultiplier * dir;
                    TryUpdateValue("waveamplitude", MathHelper.Clamp(fishSwimParams.WaveAmplitude + input, -4, 4));
                    //GUI.DrawLine(spriteBatch, start, control, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, fishSwimParams.WaveAmplitude * amplitudeMultiplier, fishSwimParams.WaveLength * lengthMultiplier, 5000 * Cam.Zoom, points, Color.Purple);

                });
            }
            // Human swim only -->
            else if (humanSwimParams != null)
            {
                // Legs
                var amplitudeMultiplier = 20 * Cam.Zoom;
                var lengthMultiplier = 20 * Cam.Zoom;
                referencePoint = SimToScreen(character.SimPosition - simSpaceForward / 2);
                drawPos = referencePoint;
                drawPos -= screenSpaceForward * (humanSwimParams.LegCycleLength * lengthMultiplier) * Cam.Zoom;
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
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, humanSwimParams.LegMoveAmount * amplitudeMultiplier, humanSwimParams.LegCycleLength * lengthMultiplier, 5000 * Cam.Zoom, points, Color.Purple);
                });
                // Movement amount
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.Purple, "Leg Movement Amount", () =>
                {
                    float input = Vector2.Multiply(PlayerInput.MouseSpeed, screenSpaceLeft).Combine() / amplitudeMultiplier * dir;
                    TryUpdateValue("legmoveamount", MathHelper.Clamp(humanSwimParams.LegMoveAmount + input, -2, 2));
                    //GUI.DrawLine(spriteBatch, start, control, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, humanSwimParams.LegMoveAmount * amplitudeMultiplier, humanSwimParams.LegCycleLength * lengthMultiplier, 5000 * Cam.Zoom, points, Color.Purple);
                });
                // Arms
                referencePoint = colliderDrawPos + screenSpaceForward * 10;
                Vector2 v = ConvertUnits.ToDisplayUnits(humanSwimParams.HandMoveAmount) * Cam.Zoom;
                drawPos = referencePoint + new Vector2(v.X * dir, v.Y);
                var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Blue, "Hand Move Amount", () =>
                {
                    var transformedInput = ConvertUnits.ToSimUnits(new Vector2(PlayerInput.MouseSpeed.X * dir, PlayerInput.MouseSpeed.Y)) / Cam.Zoom;
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
                // Collider rotation is counter-clockwise, todo: this should be handled before passing the arguments
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

        #region Ragdoll
        private Vector2[] corners = new Vector2[4];
        private void DrawOffsetEditor(SpriteBatch spriteBatch)
        {
            float inputMultiplier = 0.5f;
            Limb selectedLimb = null;
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb == null || limb.sprite == null) { continue; }
                Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
                //GUI.DrawRectangle(spriteBatch, new Rectangle(limbBodyPos.ToPoint(), new Point(5, 5)), Color.Red);
                Vector2 size = limb.sprite.SourceRect.Size.ToVector2() * Cam.Zoom * limb.Scale;
                Vector2 up = -VectorExtensions.Forward(limb.Rotation);
                corners = MathUtils.GetImaginaryRect(corners, up, limbScreenPos, size);
                //var rect = new Rectangle(limbBodyPos.ToPoint() - size.Divide(2), size);
                //GUI.DrawRectangle(spriteBatch, rect, Color.Blue);

                GUI.DrawRectangle(spriteBatch, corners, Color.Red);
                GUI.DrawLine(spriteBatch, limbScreenPos, limbScreenPos + up * 20, Color.White, width: 3);
                GUI.DrawLine(spriteBatch, limbScreenPos, limbScreenPos + up * 20, Color.Red);

                // Limb positions
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.White, width: 3);
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.White, width: 3);
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.Red);
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.Red);

                if (PlayerInput.LeftButtonHeld() && MathUtils.RectangleContainsPoint(corners, PlayerInput.MousePosition))
                {
                    if (selectedLimb == null)
                    {
                        selectedLimb = limb;
                    }
                }
                else if (selectedLimb == limb)
                {
                    selectedLimb = null;
                }
            }
            if (selectedLimb != null)
            {
                Vector2 up = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(selectedLimb.Rotation));
                var input = -PlayerInput.MouseSpeed * inputMultiplier * Cam.Zoom / selectedLimb.Scale;
                selectedLimb.sprite.Origin += input.TransformVector(up);
                var max = new Vector2(selectedLimb.sprite.SourceRect.Width, selectedLimb.sprite.SourceRect.Height);
                selectedLimb.sprite.Origin = selectedLimb.sprite.Origin.Clamp(Vector2.Zero, max);
            }
        }

        private void DrawJointEditor(SpriteBatch spriteBatch)
        {
            foreach (Limb limb in character.AnimController.Limbs)
            {
                Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
                foreach (var joint in character.AnimController.LimbJoints)
                {
                    Vector2 jointPos = Vector2.Zero;
                    Vector2 otherPos = Vector2.Zero;
                    Vector2 jointDir = Vector2.Zero;
                    Vector2 anchorPosA = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA);
                    Vector2 anchorPosB = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB);
                    Vector2 up = -VectorExtensions.Forward(limb.Rotation);
                    if (joint.BodyA == limb.body.FarseerBody)
                    {
                        jointPos = anchorPosA;
                        otherPos = anchorPosB;
                    }
                    else if (joint.BodyB == limb.body.FarseerBody)
                    {
                        jointPos = anchorPosB;
                        otherPos = anchorPosA;
                    }
                    else
                    {
                        continue;
                    }
                    var f = Vector2.Transform(jointPos, Matrix.CreateRotationZ(limb.Rotation));
                    f.Y = -f.Y;
                    Vector2 tformedJointPos = limbScreenPos + f * Cam.Zoom;
                    ShapeExtensions.DrawPoint(spriteBatch, limbScreenPos, Color.Black, size: 5);
                    ShapeExtensions.DrawPoint(spriteBatch, limbScreenPos, Color.White, size: 1);
                    GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.Black, width: 3);
                    GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.White, width: 1);

                    if (editJointLimits)
                    {
                        DrawJointLimitWidgets(spriteBatch, joint, tformedJointPos, character.AnimController.Collider.Rotation);
                    }
                    if (editJointPositions)
                    {
                        // TODO: add a toggle to switch between body a and body b, because when the positions are the same, which makes it impossible to edit joint ends separatedly
                        Color color = joint.BodyA == limb.body.FarseerBody ? Color.Red : Color.Blue;
                        var widgetSize = new Vector2(5, 5);
                        var rect = new Rectangle((tformedJointPos - widgetSize / 2).ToPoint(), widgetSize.ToPoint());
                        GUI.DrawRectangle(spriteBatch, tformedJointPos - widgetSize / 2, widgetSize, color, true);
                        var inputRect = rect;
                        inputRect.Inflate(widgetSize.X, widgetSize.Y);
                        //GUI.DrawLine(spriteBatch, tformedJointPos, limbScreenPos + otherPos, color, width: 1);
                        GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + up * 20, Color.White);
                        if (inputRect.Contains(PlayerInput.MousePosition))
                        {
                            GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + up * 20, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.Yellow, width: 3);
                            GUI.DrawString(spriteBatch, tformedJointPos + Vector2.One * 10.0f, jointPos.FormatAsZeroDecimal(), Color.White, Color.Black * 0.5f);
                            GUI.DrawRectangle(spriteBatch, inputRect, Color.Red);
                            if (PlayerInput.LeftButtonHeld())
                            {
                                Vector2 input = ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed) / Cam.Zoom;
                                input.Y = -input.Y;
                                input = input.TransformVector(-up);
                                if (joint.BodyA == limb.body.FarseerBody)
                                {
                                    joint.LocalAnchorA += input;
                                }
                                else
                                {
                                    joint.LocalAnchorB += input;
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Spritesheet
        private List<Texture2D> textures;
        private List<Texture2D> Textures
        {
            get
            {
                if (textures == null)
                {
                    CreateTextures(character);
                }
                return textures;
            }
        }
        private List<string> texturePaths;
        private void CreateTextures(Character character)
        {
            textures = new List<Texture2D>();
            texturePaths = new List<string>();
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb.sprite == null || texturePaths.Contains(limb.sprite.FilePath)) { continue; }
                textures.Add(limb.sprite.Texture);
                texturePaths.Add(limb.sprite.FilePath);
            }
        }

        private void DrawSpritesheetEditor(SpriteBatch spriteBatch)
        {
            //TODO: allow to zoom the sprite sheet
            //TODO: separate or combine the controls for the limbs that share a texture?
            int y = 30;
            int x = 30;
            for (int i = 0; i < Textures.Count; i++)
            {
                spriteBatch.Draw(Textures[i], new Vector2(x, y), Color.White);
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (limb.sprite == null || limb.sprite.FilePath != texturePaths[i]) continue;
                    Rectangle rect = limb.sprite.SourceRect;
                    rect.X += x;
                    rect.Y += y;
                    GUI.DrawRectangle(spriteBatch, rect, Color.Red);
                    Vector2 origin = limb.sprite.Origin;
                    Vector2 limbBodyPos = new Vector2(rect.X + origin.X, rect.Y + origin.Y);
                    // The origin is manipulated when the character is flipped. We have to undo it here.
                    if (character.AnimController.Dir < 0)
                    {
                        limbBodyPos.X = rect.X + rect.Width - origin.X;
                    }
                    if (editJointLimits || editJointPositions)
                    {
                        DrawJointEditor(spriteBatch, limb, limbBodyPos);
                    }
                    if (editOffsets)
                    {
                        // Sprite origin
                        GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.White, width: 3);
                        GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.White, width: 3);
                        GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.Red);
                        GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.Red);
                        if (PlayerInput.LeftButtonHeld() && rect.Contains(PlayerInput.MousePosition))
                        {
                            var input = PlayerInput.MouseSpeed;
                            input.X *= character.AnimController.Dir;
                            limb.sprite.Origin += input;
                            GUI.DrawString(spriteBatch, limbBodyPos + new Vector2(10, -10), limb.sprite.Origin.FormatAsZeroDecimal(), Color.White, Color.Black * 0.5f);
                        }
                    }
                }
                y += Textures[i].Height;
            }
        }

        private void DrawJointEditor(SpriteBatch spriteBatch, Limb limb, Vector2 limbScreenPos, float spriteRotation = 0)
        {
            foreach (var joint in character.AnimController.LimbJoints)
            {
                Vector2 jointPos = Vector2.Zero;
                Vector2 anchorPosA = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA);
                Vector2 anchorPosB = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB);
                if (joint.BodyA == limb.body.FarseerBody)
                {
                    jointPos = anchorPosA;

                }
                else if (joint.BodyB == limb.body.FarseerBody)
                {
                    jointPos = anchorPosB;
                }
                else
                {
                    continue;
                }
                Vector2 tformedJointPos = jointPos /= limb.Scale;
                tformedJointPos.Y = -tformedJointPos.Y;
                tformedJointPos.X *= character.AnimController.Dir;
                tformedJointPos += limbScreenPos;
                if (editJointLimits)
                {
                    //if (joint.BodyA == limb.body.FarseerBody)
                    //{
                    //    float a1 = joint.UpperLimit - MathHelper.PiOver2;
                    //    float a2 = joint.LowerLimit - MathHelper.PiOver2;
                    //    float a3 = (a1 + a2) / 2.0f;
                    //    GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + new Vector2((float)Math.Cos(a1), -(float)Math.Sin(a1)) * 30.0f, Color.Yellow);
                    //    GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + new Vector2((float)Math.Cos(a2), -(float)Math.Sin(a2)) * 30.0f, Color.Cyan);
                    //    GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + new Vector2((float)Math.Cos(a3), -(float)Math.Sin(a3)) * 30.0f, Color.Black);
                    //}
                    if (joint.BodyA == limb.body.FarseerBody)
                    {
                        DrawJointLimitWidgets(spriteBatch, joint, tformedJointPos);
                    }
                }
                if (editJointPositions)
                {
                    Color color = joint.BodyA == limb.body.FarseerBody ? Color.Red : Color.Blue;
                    Vector2 widgetSize = new Vector2(5.0f, 5.0f); ;
                    var rect = new Rectangle((tformedJointPos - widgetSize / 2).ToPoint(), widgetSize.ToPoint());
                    var inputRect = rect;
                    inputRect.Inflate(widgetSize.X * 0.75f, widgetSize.Y * 0.75f);
                    GUI.DrawRectangle(spriteBatch, rect, color, isFilled: true);
                    if (inputRect.Contains(PlayerInput.MousePosition))
                    {          
                        GUI.DrawString(spriteBatch, tformedJointPos + Vector2.One * 10.0f, jointPos.FormatAsZeroDecimal(), Color.White, Color.Black * 0.5f);
                        GUI.DrawRectangle(spriteBatch, inputRect, color);
                        if (PlayerInput.LeftButtonHeld())
                        {
                            Vector2 input = ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed);
                            input.Y = -input.Y;
                            input.X *= character.AnimController.Dir;
                            if (joint.BodyA == limb.body.FarseerBody)
                            {
                                joint.LocalAnchorA += input * limb.Scale;
                            }
                            else
                            {
                                joint.LocalAnchorB += input * limb.Scale;
                            }
                        }
                    }
                }
            }
        }

        private void DrawJointLimitWidgets(SpriteBatch spriteBatch, LimbJoint joint, Vector2 drawPos, float rotationOffset = 0)
        {
            // The joint limits are flipped and inversed when the character is flipped, so we have to handle it here, because we don't want it to affect the user interface.
            if (character.AnimController.Dir < 0)
            {
                DrawCircularWidget(spriteBatch, drawPos, MathHelper.ToDegrees(-joint.LowerLimit), "Upper Limit", Color.Yellow, angle =>
                {
                    joint.LowerLimit = MathHelper.ToRadians(-angle);
                }, rotationOffset: rotationOffset);
                DrawCircularWidget(spriteBatch, drawPos, MathHelper.ToDegrees(-joint.UpperLimit), "Lower Limit", Color.Cyan, angle =>
                {
                    joint.UpperLimit = MathHelper.ToRadians(-angle);
                }, rotationOffset: rotationOffset);
            }
            else
            {
                DrawCircularWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.UpperLimit), "Upper Limit", Color.Yellow, angle =>
                {
                    joint.UpperLimit = MathHelper.ToRadians(angle);
                }, rotationOffset: rotationOffset);
                DrawCircularWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.LowerLimit), "Lower Limit", Color.Cyan, angle =>
                {
                    joint.LowerLimit = MathHelper.ToRadians(angle);
                }, rotationOffset: rotationOffset);
            }
        }
        #endregion
    }
}
