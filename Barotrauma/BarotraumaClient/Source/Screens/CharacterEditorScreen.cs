using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;

namespace Barotrauma
{
    class CharacterEditorScreen : Screen
    {
        private static CharacterEditorScreen instance;

        private Camera cam;
        public override Camera Cam
        {
            get
            {
                if (cam == null)
                {
                    cam = new Camera()
                    {
                        MinZoom = 0.1f,
                        MaxZoom = 5.0f
                    };
                }
                return cam;
            }
        }

        private Character character;
        private Vector2 spawnPosition;
        private bool editAnimations;
        private bool editLimbs;
        private bool editJoints;
        private bool editIK;
        private bool showRagdoll;
        private bool showParamsEditor;
        private bool showSpritesheet;
        private bool isFreezed;
        private bool autoFreeze = true;
        private bool limbPairEditing = true;
        private bool uniformScaling = true;
        private bool lockSpriteOrigin;
        private bool lockSpritePosition;
        private bool lockSpriteSize;
        private bool copyJointSettings;
        private bool displayColliders;
        private bool displayBackgroundColor;
        private bool ragdollResetRequiresForceLoading;

        private bool isExtrudingJoint;
        private bool isDrawingJoint;
        private Limb targetLimb;
        private Vector2? anchor1Pos;

        private float spriteSheetZoom = 1;
        private float textureScale = 1;
        private float textureMinScale = 0.1f;
        private float textureMaxScale = 2;
        private float spriteSheetMinZoom = 0.25f;
        private float spriteSheetMaxZoom = 1;
        private int spriteSheetOffsetY = 100;
        private int spriteSheetOffsetX = (int)(GameMain.GraphicsWidth * 0.6f);
        private Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1.0f);

        private List<LimbJoint> selectedJoints = new List<LimbJoint>();
        private List<Limb> selectedLimbs = new List<Limb>();

        private float spriteSheetOrientation;

        private bool isEndlessRunner;

        private Rectangle GetSpritesheetRectangle => 
            new Rectangle(
                spriteSheetOffsetX, 
                spriteSheetOffsetY, 
                (int)(Textures.OrderByDescending(t => t.Width).First().Width * spriteSheetZoom), 
                (int)(Textures.Sum(t => t.Height) * spriteSheetZoom));

        public override void Select()
        {
            base.Select();
            GUI.ForceMouseOn(null);
            if (Submarine.MainSub == null)
            {
                Submarine.MainSub = new Submarine("Content/AnimEditor.sub");
                Submarine.MainSub.Load(unloadPrevious: false, showWarningMessages: false);
                originalWall = new WallGroup(new List<Structure>(Structure.WallList));
                CloneWalls();
                CalculateMovementLimits();
                isEndlessRunner = true;
                GameMain.LightManager.LightingEnabled = false;
            }
            Submarine.MainSub.GodMode = true;
            if (Character.Controlled == null)
            {
                SpawnCharacter(Character.HumanConfigFile);
                //SpawnCharacter(AllFiles.First());
            }
            else
            {
                OnPreSpawn();
                character = Character.Controlled;
                TeleportTo(spawnPosition);
                OnPostSpawn();
            }
            GameMain.Instance.OnResolutionChanged += OnResolutionChanged;
            instance = this;
        }

        public override void Deselect()
        {
            base.Deselect();
            GUI.ForceMouseOn(null);
            if (isEndlessRunner)
            {
                Submarine.MainSub.Remove();
                isEndlessRunner = false;
                if (character != null)
                {
                    character?.Remove();
                    character = null;
                }
                GameMain.World.ProcessChanges();
            }
            GameMain.Instance.OnResolutionChanged -= OnResolutionChanged;
            GameMain.LightManager.LightingEnabled = true;
        }

        private void OnResolutionChanged()
        {
            CreateGUI();
        }

        #region Main methods
        public override void AddToGUIUpdateList()
        {
            //base.AddToGUIUpdateList();
            rightPanel.AddToGUIUpdateList();
            Wizard.Instance.AddToGUIUpdateList();
            if (displayBackgroundColor)
            {
                backgroundColorPanel.AddToGUIUpdateList();
            }
            if (editAnimations)
            {
                animationControls.AddToGUIUpdateList();
            }
            if (showSpritesheet)
            {
                spriteSheetControls.AddToGUIUpdateList();
            }
            if (editJoints)
            {
                ragdollControls.AddToGUIUpdateList();
            }
            if (editLimbs)
            {
                limbControls.AddToGUIUpdateList();
            }
            if (showParamsEditor)
            {
                ParamsEditor.Instance.EditorBox.AddToGUIUpdateList();
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            // Handle shortcut keys
            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (PlayerInput.KeyDown(Keys.LeftControl))
                {
                    Character.DisableControls = true;
                    Widget.EnableMultiSelect = !editAnimations;
                }
                else
                {
                    Widget.EnableMultiSelect = false;
                }
                // Conflicts with ctrl + c
                //if (PlayerInput.KeyHit(Keys.C))
                //{
                //    copyJointsToggle.Selected = !copyJointsToggle.Selected;
                //}
                if (PlayerInput.KeyHit(Keys.T) || PlayerInput.KeyHit(Keys.X))
                {
                    animTestPoseToggle.Selected = !animTestPoseToggle.Selected;
                }
                if (PlayerInput.KeyHit(InputType.Run))
                {
                    // TODO: refactor this horrible hacky index manipulation mess
                    int index = 0;
                    bool isSwimming = character.AnimController.ForceSelectAnimationType == AnimationType.SwimFast || character.AnimController.ForceSelectAnimationType == AnimationType.SwimSlow;
                    bool isMovingFast = character.AnimController.ForceSelectAnimationType == AnimationType.Run || character.AnimController.ForceSelectAnimationType == AnimationType.SwimFast;
                    if (isMovingFast)
                    {
                        if (isSwimming || !character.AnimController.CanWalk)
                        {
                            index = !character.AnimController.CanWalk ? 0 : (int)AnimationType.SwimSlow - 1;
                        }
                        else
                        {
                            index = (int)AnimationType.Walk - 1;
                        }
                    }
                    else
                    {
                        if (isSwimming || !character.AnimController.CanWalk)
                        {
                            index = !character.AnimController.CanWalk ? 1 : (int)AnimationType.SwimFast - 1;
                        }
                        else
                        {
                            index = (int)AnimationType.Run - 1;
                        }
                    }
                    if (animSelection.SelectedIndex != index)
                    {
                        animSelection.Select(index);
                    }
                }
                if (PlayerInput.KeyHit(Keys.E))
                {
                    bool isSwimming = character.AnimController.ForceSelectAnimationType == AnimationType.SwimFast || character.AnimController.ForceSelectAnimationType == AnimationType.SwimSlow;
                    if (isSwimming)
                    {
                        animSelection.Select((int)AnimationType.Walk - 1);
                    }
                    else
                    {
                        animSelection.Select((int)AnimationType.SwimSlow - 1);
                    }
                }
                if (PlayerInput.KeyHit(Keys.F))
                {
                    freezeToggle.Selected = !freezeToggle.Selected;
                }
                if (PlayerInput.RightButtonClicked())
                {
                    bool reset = false;
                    if (selectedLimbs.Any())
                    {
                        selectedLimbs.Clear();
                        reset = true;
                    }
                    if (selectedJoints.Any())
                    {
                        selectedJoints.Clear();
                        foreach (var w in jointSelectionWidgets.Values)
                        {
                            w.refresh();
                            w.linkedWidget?.refresh();
                        }
                        reset = true;
                    }
                    if (reset)
                    {
                        ResetParamsEditor();
                    }
                }
                if (PlayerInput.KeyHit(Keys.Delete))
                {
                    DeleteSelected();
                }
                if (editLimbs && PlayerInput.KeyDown(Keys.LeftControl))
                {
                    if (PlayerInput.KeyHit(Keys.C))
                    {
                        var selectedLimb = selectedLimbs.FirstOrDefault();
                        if (selectedLimb != null)
                        {
                            CopyLimb(selectedLimb);
                        }
                    }
                }
                UpdateJointCreation();
            }
            if (!isFreezed)
            {
                Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
                Submarine.MainSub.Update((float)deltaTime);
                PhysicsBody.List.ForEach(pb => pb.SetPrevTransform(pb.SimPosition, pb.Rotation));
                // Handle ragdolling here, because we are not calling the Character.Update() method.
                if (!Character.DisableControls)
                {
                    character.IsRagdolled = PlayerInput.KeyDown(InputType.Ragdoll);
                }
                if (character.IsRagdolled)
                {
                    character.AnimController.ResetPullJoints();
                }
                character.ControlLocalPlayer((float)deltaTime, Cam, false);
                character.Control((float)deltaTime, Cam);
                character.AnimController.UpdateAnim((float)deltaTime);
                character.AnimController.Update((float)deltaTime, Cam);
                character.CurrentHull = character.AnimController.CurrentHull;
                if (isEndlessRunner)
                {
                    if (character.Position.X < min)
                    {
                        UpdateWalls(false);
                    }
                    else if (character.Position.X > max)
                    {
                        UpdateWalls(true);
                    }
                }
                GameMain.World.Step((float)deltaTime);
            }
            Cam.MoveCamera((float)deltaTime, allowMove: false, allowZoom: GUI.MouseOn == null);
            Cam.Position = character.WorldPosition;
            MapEntity.mapEntityList.ForEach(e => e.IsHighlighted = false);
            // Update widgets
            jointSelectionWidgets.Values.ForEach(w => w.Update((float)deltaTime));
            animationWidgets.Values.ForEach(w => w.Update((float)deltaTime));
        }

        /// <summary>
        /// Fps independent mouse input. The draw method is called multiple times per frame.
        /// </summary>
        private Vector2 scaledMouseSpeed;
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            if (isFreezed)
            {
                Timing.Alpha = 0.0f;
            }
            scaledMouseSpeed = PlayerInput.MouseSpeedPerSecond * (float)deltaTime;
            Cam.UpdateTransform(true);
            Submarine.CullEntities(Cam);

            // TODO: use the same drawing method as in game?
            //GameMain.GameScreen.DrawMap(graphics, spriteBatch, deltaTime, Cam);

            Submarine.MainSub.UpdateTransform();

            // Lightmaps
            if (GameMain.LightManager.LightingEnabled)
            {
                GameMain.LightManager.ObstructVision = Character.Controlled.ObstructVision;
                GameMain.LightManager.UpdateLightMap(graphics, spriteBatch, cam);
                GameMain.LightManager.UpdateObstructVision(graphics, spriteBatch, cam, Character.Controlled.CursorWorldPosition);
            }
            base.Draw(deltaTime, graphics, spriteBatch);

            graphics.Clear(backgroundColor);

            // Submarine
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            Submarine.Draw(spriteBatch, isEndlessRunner);
            spriteBatch.End();

            // Character(s)
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            Character.CharacterList.ForEach(c => c.Draw(spriteBatch, Cam));
            if (GameMain.DebugDraw)
            {
                character.AnimController.DebugDraw(spriteBatch);
            }
            else if (displayColliders)
            {
                character.AnimController.Collider.DebugDraw(spriteBatch, Color.White);
                character.AnimController.Limbs.ForEach(l => l.body.DebugDraw(spriteBatch, Color.LightGreen));
            }
            spriteBatch.End();

            // Lights
            if (GameMain.LightManager.LightingEnabled)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, Lights.CustomBlendStates.Multiplicative, null, DepthStencilState.None, null, null, null);
                spriteBatch.Draw(GameMain.LightManager.LightMap, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                spriteBatch.End();
            }

            // GUI
            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
            if (editAnimations)
            {
                DrawAnimationControls(spriteBatch, (float)deltaTime);
            }
            if (editLimbs)
            {
                DrawLimbEditor(spriteBatch);
            }
            if (showRagdoll)
            {
                DrawRagdoll(spriteBatch, (float)deltaTime);
            }
            if (showSpritesheet)
            {
                DrawSpritesheetEditor(spriteBatch, (float)deltaTime);
            }
            if (isExtrudingJoint)
            {
                var selectedJoint = selectedJoints.FirstOrDefault();
                if (selectedJoint != null)
                {
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 120, GameMain.GraphicsHeight / 5), "Creating a new joint", Color.White, font: GUI.LargeFont);
                    if (GetSpritesheetRectangle.Contains(PlayerInput.MousePosition))
                    {
                        var startPos = GetLimbSpritesheetRect(selectedJoint.LimbB).Center.ToVector2();
                        var offset = ConvertUnits.ToDisplayUnits(selectedJoint.LocalAnchorB) * spriteSheetZoom;
                        offset.Y = -offset.Y;
                        DrawJointCreationOnSpritesheet(spriteBatch, startPos + offset);
                    }
                    else
                    {
                        DrawJointCreationOnRagdoll(spriteBatch, SimToScreen(selectedJoint.WorldAnchorB));
                    }
                }
            }
            else if (isDrawingJoint)
            {
                var selectedLimb = selectedLimbs.FirstOrDefault();
                if (selectedLimb != null)
                {
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 120, GameMain.GraphicsHeight / 5), "Creating a new joint", Color.White, font: GUI.LargeFont);
                    if (GetSpritesheetRectangle.Contains(PlayerInput.MousePosition))
                    {
                        var startPos = GetLimbSpritesheetRect(selectedLimb).Center.ToVector2();
                        if (anchor1Pos.HasValue)
                        {
                            var offset = anchor1Pos.Value;
                            offset = -offset;
                            startPos += offset;
                        }
                        DrawJointCreationOnSpritesheet(spriteBatch, startPos);
                    }
                    else
                    {
                        var startPos = anchor1Pos.HasValue
                            ? SimToScreen(selectedLimb.SimPosition + Vector2.Transform(ConvertUnits.ToSimUnits(anchor1Pos.Value), Matrix.CreateRotationZ(selectedLimb.Rotation)))
                            : SimToScreen(selectedLimb.SimPosition);
                        DrawJointCreationOnRagdoll(spriteBatch, startPos);
                    }
                }
            }
            if (isEndlessRunner)
            {
                Structure wall = CurrentWall.walls.FirstOrDefault();
                Vector2 indicatorPos = wall == null ? originalWall.walls.First().DrawPosition : wall.DrawPosition;
                GUI.DrawIndicator(spriteBatch, indicatorPos, Cam, 700, GUI.SubmarineIcon, Color.White);
            }
            GUI.Draw(Cam, spriteBatch);
            if (isFreezed)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 35, 100), "FREEZED", Color.Blue, Color.White * 0.5f, 10, GUI.Font);
            }
            if (animTestPoseToggle.Selected)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 100, 150), "Animation Test Pose Enabled", Color.Blue, Color.White * 0.5f, 10, GUI.Font);
            }
            if (showSpritesheet)
            {
                var topLeft = spriteSheetControls.RectTransform.TopLeft;
                GUI.DrawString(spriteBatch, new Vector2(topLeft.X + 300, 50), "Spritesheet Orientation:", Color.White, Color.Gray * 0.5f, 10, GUI.Font);
                DrawRadialWidget(spriteBatch, new Vector2(topLeft.X + 510, 60), spriteSheetOrientation, string.Empty, Color.White,
                    angle => spriteSheetOrientation = angle, circleRadius: 40, widgetSize: 20, rotationOffset: MathHelper.Pi, autoFreeze: false);
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
                //GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 300), $"Clones: {clones.Length}", Color.White, font: GUI.SmallFont);
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
                Vector2 left = forward.Left();
                GUI.DrawLine(spriteBatch, colliderDrawPos, SimToScreen(collider.SimPosition + left * 0.25f), Color.Red);
                ShapeExtensions.DrawCircle(spriteBatch, colliderDrawPos, (endPos - colliderDrawPos).Length(), 40, Color.LightGreen);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 300, 0), $"Collider rotation: {MathHelper.ToDegrees(MathUtils.WrapAngleTwoPi(collider.Rotation))}", Color.White, font: GUI.SmallFont);
            }
            spriteBatch.End();
        }
        #endregion

        #region Ragdoll Manipulation
        private void UpdateJointCreation()
        {
            bool ctrlAlt = PlayerInput.KeyDown(Keys.LeftControl) && PlayerInput.KeyDown(Keys.LeftAlt);
            isExtrudingJoint = !editLimbs && editJoints && ctrlAlt;
            isDrawingJoint = !editJoints && editLimbs && ctrlAlt;
            if (isExtrudingJoint)
            {
                var selectedJoint = selectedJoints.FirstOrDefault();
                if (selectedJoint != null)
                {
                    if (GetSpritesheetRectangle.Contains(PlayerInput.MousePosition))
                    {
                        targetLimb = GetClosestLimbOnSpritesheet(PlayerInput.MousePosition, l => l != null && l != selectedJoint.LimbB && l.ActiveSprite != null);
                        if (targetLimb != null && PlayerInput.LeftButtonClicked())
                        {
                            Vector2 anchor1 = ConvertUnits.ToDisplayUnits(selectedJoint.LocalAnchorB);
                            Vector2 anchor2 = (GetLimbSpritesheetRect(targetLimb).Center.ToVector2() - PlayerInput.MousePosition) / spriteSheetZoom;
                            anchor2.X = -anchor2.X;
                            ExtrudeJoint(selectedJoint, targetLimb.limbParams.ID, anchor1, anchor2);
                        }
                    }
                    else
                    {
                        targetLimb = GetClosestLimbOnRagdoll(PlayerInput.MousePosition, l => l != null && l != selectedJoint.LimbB && l.ActiveSprite != null);
                        if (targetLimb != null && PlayerInput.LeftButtonClicked())
                        {
                            Vector2 anchor1 = ConvertUnits.ToDisplayUnits(selectedJoint.LocalAnchorB);
                            Vector2 anchor2 = ConvertUnits.ToDisplayUnits(targetLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                            ExtrudeJoint(selectedJoint, targetLimb.limbParams.ID, anchor1, anchor2);
                        }
                    }
                }
                else
                {
                    targetLimb = null;
                }
            }
            else if (isDrawingJoint)
            {
                var selectedLimb = selectedLimbs.FirstOrDefault();
                if (selectedLimb != null)
                {
                    if (GetSpritesheetRectangle.Contains(PlayerInput.MousePosition))
                    {
                        if (anchor1Pos == null)
                        {
                            anchor1Pos = GetLimbSpritesheetRect(selectedLimb).Center.ToVector2() - PlayerInput.MousePosition;
                        }
                        targetLimb = GetClosestLimbOnSpritesheet(PlayerInput.MousePosition, l => l != null && l != selectedLimb && l.ActiveSprite != null);
                        if (targetLimb != null && PlayerInput.LeftButtonClicked())
                        {
                            Vector2 anchor1 = anchor1Pos.HasValue ? anchor1Pos.Value / spriteSheetZoom : Vector2.Zero;
                            anchor1.X = -anchor1.X;
                            Vector2 anchor2 = (GetLimbSpritesheetRect(targetLimb).Center.ToVector2() - PlayerInput.MousePosition) / spriteSheetZoom;
                            anchor2.X = -anchor2.X;
                            CreateJoint(selectedLimb.limbParams.ID, targetLimb.limbParams.ID, anchor1, anchor2);
                        }
                    }
                    else
                    {
                        if (anchor1Pos == null)
                        {
                            anchor1Pos = ConvertUnits.ToDisplayUnits(selectedLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                        }
                        targetLimb = GetClosestLimbOnRagdoll(PlayerInput.MousePosition, l => l != null && l != selectedLimb && l.ActiveSprite != null);
                        if (targetLimb != null && PlayerInput.LeftButtonClicked())
                        {
                            Vector2 anchor1 = anchor1Pos ?? Vector2.Zero;
                            Vector2 anchor2 = ConvertUnits.ToDisplayUnits(targetLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                            CreateJoint(selectedLimb.limbParams.ID, targetLimb.limbParams.ID, anchor1, anchor2);
                        }
                    }
                }
                else
                {
                    targetLimb = null;
                    anchor1Pos = null;
                }
            }
            else
            {
                targetLimb = null;
                anchor1Pos = null;
            }
        }

        private void CopyLimb(Limb limb)
        {
            // TODO: copy all params and sub params -> use a generic method?
            var rect = limb.ActiveSprite.SourceRect;
            var spriteParams = limb.limbParams.normalSpriteParams;
            if (spriteParams == null)
            {
                spriteParams = limb.limbParams.deformSpriteParams;
            }
            var newLimbElement = new XElement("limb",
                new XAttribute("id", RagdollParams.Limbs.Last().ID + 1),
                new XAttribute("radius", limb.limbParams.Radius),
                new XAttribute("width", limb.limbParams.Width),
                new XAttribute("height", limb.limbParams.Height),
                new XAttribute("mass", limb.limbParams.Mass),
                new XElement("sprite",
                    new XAttribute("texture", spriteParams.Texture),
                    new XAttribute("sourcerect", $"{rect.X}, {rect.Y}, {rect.Size.X}, {rect.Size.Y}"))
                );
            var lastLimbElement = RagdollParams.MainElement.Elements("limb").Last();
            lastLimbElement.AddAfterSelf(newLimbElement);
            var newLimbParams = new LimbParams(newLimbElement, RagdollParams);
            RagdollParams.Limbs.Add(newLimbParams);
            character.AnimController.Recreate();
            TeleportTo(spawnPosition);
            ClearWidgets();
            selectedLimbs.Add(character.AnimController.Limbs.Single(l => l.limbParams == newLimbParams));
            ResetParamsEditor();
            ragdollResetRequiresForceLoading = true;
        }

        /// <summary>
        /// Creates a new joint between the last limb of the given joint and the target limb.
        /// </summary>
        private void ExtrudeJoint(LimbJoint joint, int targetLimb, Vector2? anchor1 = null, Vector2? anchor2 = null)
        {
            CreateJoint(joint.jointParams.Limb2, targetLimb, anchor1, anchor2);
        }

        /// <summary>
        /// Creates a new joint using the limb IDs.
        /// </summary>
        private void CreateJoint(int fromLimb, int toLimb, Vector2? anchor1 = null, Vector2? anchor2 = null)
        {
            Vector2 a1 = anchor1 ?? Vector2.Zero;
            Vector2 a2 = anchor2 ?? Vector2.Zero;
            var newJointElement = new XElement("joint",
                new XAttribute("limb1", fromLimb),
                new XAttribute("limb2", toLimb),
                new XAttribute("limb1anchor", $"{a1.X.Format(2)}, {a1.Y.Format(2)}"),
                new XAttribute("limb2anchor", $"{a2.X.Format(2)}, {a2.Y.Format(2)}")
                );
            var lastJointElement = RagdollParams.MainElement.Elements("joint").Last();
            lastJointElement.AddAfterSelf(newJointElement);
            var newJointParams = new JointParams(newJointElement, RagdollParams);
            RagdollParams.Joints.Add(newJointParams);
            character.AnimController.Recreate();
            TeleportTo(spawnPosition);
            ClearWidgets();
            selectedJoints.Add(character.AnimController.LimbJoints.Single(j => j.jointParams == newJointParams));
            jointsToggle.Selected = true;
            ResetParamsEditor();
            ragdollResetRequiresForceLoading = true;
        }

        /// <summary>
        /// Removes all selected joints and limbs in the params level (-> serializable). The method also recreates the ids and names, when required.
        /// </summary>
        private void DeleteSelected()
        {
            for (int i = 0; i < selectedJoints.Count; i++)
            {
                var joint = selectedJoints[i];
                RagdollParams.Joints.Remove(joint.jointParams);
            }
            var removedIDs = new List<int>();
            for (int i = 0; i < selectedLimbs.Count; i++)
            {
                if (character.IsHumanoid)
                {
                    DebugConsole.ThrowError("Deleting limbs from a humanoid is currently disabled, because it will most likely crash the game.");
                    break;
                }
                var limb = selectedLimbs[i];
                if (limb == character.AnimController.MainLimb)
                {
                    DebugConsole.ThrowError("Can't remove the main limb, because it will crash the game.");
                    continue;
                }
                removedIDs.Add(limb.limbParams.ID);
                RagdollParams.Limbs.Remove(limb.limbParams);
            }
            // Recreate ids
            var renamedIDs = new Dictionary<int, int>();
            for (int i = 0; i < RagdollParams.Limbs.Count; i++)
            {
                int oldID = RagdollParams.Limbs[i].ID;
                int newID = i;
                if (oldID != newID)
                {
                    var limbParams = RagdollParams.Limbs[i];
                    limbParams.ID = newID;
                    limbParams.Name = limbParams.GenerateName();
                    renamedIDs.Add(oldID, newID);
                }
            }
            // Refresh/recreate joints
            var jointsToRemove = new List<JointParams>();
            for (int i = 0; i < RagdollParams.Joints.Count; i++)
            {
                var joint = RagdollParams.Joints[i];
                if (removedIDs.Contains(joint.Limb1) || removedIDs.Contains(joint.Limb2))
                {
                    // At least one of the limbs has been removed -> remove the joint
                    jointsToRemove.Add(joint);
                }
                else
                {
                    // Both limbs still remains -> update
                    bool rename = false;
                    if (renamedIDs.TryGetValue(joint.Limb1, out int newID1))
                    {
                        joint.Limb1 = newID1;
                        rename = true;
                    }
                    if (renamedIDs.TryGetValue(joint.Limb2, out int newID2))
                    {
                        joint.Limb2 = newID2;
                        rename = true;
                    }
                    if (rename)
                    {
                        joint.Name = joint.GenerateName();
                    }
                }
            }
            jointsToRemove.ForEach(j => RagdollParams.Joints.Remove(j));
            RecreateRagdoll();
            ragdollResetRequiresForceLoading = true;
        }
        #endregion

        #region Endless runner
        private int min;
        private int max;
        private void CalculateMovementLimits()
        {
            min = CurrentWall.walls.Select(w => w.Rect.Left).OrderBy(p => p).First();
            max = CurrentWall.walls.Select(w => w.Rect.Right).OrderBy(p => p).Last();
        }

        private WallGroup originalWall;
        private WallGroup[] clones = new WallGroup[3];
        private IEnumerable<Structure> AllWalls => originalWall.walls.Concat(clones.SelectMany(c => c.walls));

        private WallGroup _currentWall;
        private WallGroup CurrentWall
        {
            get
            {
                if (_currentWall == null)
                {
                    _currentWall = originalWall;
                }
                return _currentWall;
            }
            set
            {
                _currentWall = value;
            }
        }

        private class WallGroup
        {
            public readonly List<Structure> walls;
            
            public WallGroup(List<Structure> walls)
            {
                this.walls = walls;
            }

            public WallGroup Clone()
            {
                var clones = new List<Structure>();
                walls.ForEachMod(w => clones.Add(w.Clone() as Structure));
                return new WallGroup(clones);
            }     
        }

        private void CloneWalls()
        {
            for (int i = 0; i < 3; i++)
            {
                clones[i] = originalWall.Clone();
                for (int j = 0; j < originalWall.walls.Count; j++)
                {
                    if (i == 1)
                    {
                        clones[i].walls[j].Move(new Vector2(originalWall.walls[j].Rect.Width, 0));
                    }
                    else if (i == 2)
                    {
                        clones[i].walls[j].Move(new Vector2(-originalWall.walls[j].Rect.Width, 0));
                    }      
                }
            }
        }

        private WallGroup SelectClosestWallGroup(Vector2 pos)
        {
            var closestWall = clones.SelectMany(c => c.walls).OrderBy(w => Vector2.Distance(pos, w.Position)).First();
            return clones.Where(c => c.walls.Contains(closestWall)).FirstOrDefault();
        }

        private WallGroup SelectLastClone(bool right)
        {
            var lastWall = right 
                ? clones.SelectMany(c => c.walls).OrderBy(w => w.Rect.Right).Last() 
                : clones.SelectMany(c => c.walls).OrderBy(w => w.Rect.Left).First();
            return clones.Where(c => c.walls.Contains(lastWall)).FirstOrDefault();
        }

        private void UpdateWalls(bool right)
        {
            CurrentWall = SelectClosestWallGroup(character.Position);
            CalculateMovementLimits();
            var lastClone = SelectLastClone(!right);
            for (int i = 0; i < lastClone.walls.Count; i++)
            {
                var amount = right ? lastClone.walls[i].Rect.Width : -lastClone.walls[i].Rect.Width;
                var distance = CurrentWall.walls[i].Position.X - lastClone.walls[i].Position.X;
                lastClone.walls[i].Move(new Vector2(amount + distance, 0));
            }
            GameMain.World.ProcessChanges();
        }

        private bool wallCollisionsEnabled;
        private void SetWallCollisions(bool enabled)
        {
            if (!isEndlessRunner) { return; }
            wallCollisionsEnabled = enabled;
            var collisionCategory = enabled ? FarseerPhysics.Dynamics.Category.Cat1 : FarseerPhysics.Dynamics.Category.None;
            AllWalls.ForEach(w => w.SetCollisionCategory(collisionCategory));
            GameMain.World.ProcessChanges();
        }
        #endregion

        #region Character spawning
        private int characterIndex = -1;
        private string currentCharacterConfig;
        private string selectedJob = null;

        private List<string> allFiles;
        private List<string> AllFiles
        {
            get
            {
                if (allFiles == null)
                {
                    allFiles = GameMain.Instance.GetFilesOfType(ContentType.Character).OrderBy(f => f).ToList();
                    allFiles.ForEach(f => DebugConsole.NewMessage(f, Color.White));
                }
                return allFiles;
            }
        }

        private string GetNextConfigFile()
        {
            GetCurrentCharacterIndex();
            IncreaseIndex();
            currentCharacterConfig = AllFiles[characterIndex];
            return currentCharacterConfig;
        }

        private string GetPreviousConfigFile()
        {
            GetCurrentCharacterIndex();
            ReduceIndex();
            currentCharacterConfig = AllFiles[characterIndex];
            return currentCharacterConfig;
        }

        private void GetCurrentCharacterIndex()
        {
            characterIndex = AllFiles.IndexOf(GetConfigFile(character.SpeciesName));
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

        private Character SpawnCharacter(string configFile, RagdollParams ragdoll = null)
        {
            DebugConsole.NewMessage($"Trying to spawn {configFile}", Color.HotPink);
            OnPreSpawn();
            bool dontFollowCursor = true;
            if (character != null)
            {
                dontFollowCursor = character.dontFollowCursor;
                character.Remove();
                character = null;
            }
            if (configFile == Character.HumanConfigFile && selectedJob != null)
            {
                var characterInfo = new CharacterInfo(configFile, jobPrefab: JobPrefab.List.First(job => job.Identifier == selectedJob));
                character = Character.Create(configFile, spawnPosition, ToolBox.RandomSeed(8), characterInfo, hasAi: false, ragdoll: ragdoll);
                character.GiveJobItems();
                selectedJob = characterInfo.Job.Prefab.Identifier;
            }
            else
            {
                character = Character.Create(configFile, spawnPosition, ToolBox.RandomSeed(8), hasAi: false, ragdoll: ragdoll);
                selectedJob = null;
            }
            character.dontFollowCursor = dontFollowCursor;
            OnPostSpawn();
            return character;
        }

        private void OnPreSpawn()
        {
            WayPoint wayPoint = null;
            if (!isEndlessRunner)
            {
                wayPoint = WayPoint.GetRandom(spawnType: SpawnType.Human, sub: Submarine.MainSub);
            }
            if (wayPoint == null)
            {
                wayPoint = WayPoint.GetRandom(sub: Submarine.MainSub);
            }
            spawnPosition = wayPoint.WorldPosition;
            ragdollResetRequiresForceLoading = false;
        }

        private void OnPostSpawn()
        {
            currentCharacterConfig = character.ConfigPath;
            GetCurrentCharacterIndex();
            character.Submarine = Submarine.MainSub;
            character.AnimController.forceStanding = character.AnimController.CanWalk;
            character.AnimController.ForceSelectAnimationType = character.AnimController.CanWalk ? AnimationType.Walk : AnimationType.SwimSlow;
            Character.Controlled = character;
            SetWallCollisions(character.AnimController.forceStanding);
            CreateTextures();
            CreateGUI();
            ClearWidgets();
            ResetParamsEditor();
        }

        private void ClearWidgets()
        {
            Widget.selectedWidgets.Clear();
            animationWidgets.Clear();
            jointSelectionWidgets.Clear();
            selectedLimbs.Clear();
            selectedJoints.Clear();
        }

        private void RecreateRagdoll(RagdollParams ragdoll = null)
        {
            character.AnimController.Recreate(ragdoll);
            TeleportTo(spawnPosition);
            ResetParamsEditor();
            ClearWidgets();
        }

        private void TeleportTo(Vector2 position)
        {
            if (isEndlessRunner)
            {
                character.AnimController.SetPosition(ConvertUnits.ToSimUnits(position), false);
            }
            else
            {
                character.TeleportTo(position);
            }
        }

        private void CreateCharacter(string name, bool isHumanoid, params object[] ragdollConfig)
        {
            string speciesName = name;
            string mainFolder = $"Content/Characters/{speciesName}";
            // Config file
            string configFilePath = $"{mainFolder}/{speciesName}.xml";
            if (ContentPackage.GetFilesOfType(GameMain.SelectedPackages, ContentType.Character).None(path => path.Contains(speciesName)))
            {
                // Create the config file
                XElement mainElement = new XElement("Character",
                    new XAttribute("name", speciesName),
                    new XAttribute("humanoid", isHumanoid),
                    new XElement("ragdolls"),
                    new XElement("animations"),
                    new XElement("health"),
                    new XElement("ai"));
                XDocument doc = new XDocument(mainElement);
                if (!Directory.Exists(mainFolder))
                {
                    Directory.CreateDirectory(mainFolder);
                }
                doc.Save(configFilePath);
                // Add to the content package
                var contentPackage = GameMain.Config.SelectedContentPackages.Last();
                contentPackage.AddFile(configFilePath, ContentType.Character);
                contentPackage.Save(contentPackage.Path);
            }
            // Ragdoll
            string ragdollFolder = RagdollParams.GetDefaultFolder(speciesName);
            string ragdollPath = RagdollParams.GetDefaultFile(speciesName);
            RagdollParams ragdollParams = isHumanoid
                ? RagdollParams.CreateDefault<HumanRagdollParams>(ragdollPath, speciesName, ragdollConfig)
                : RagdollParams.CreateDefault<FishRagdollParams>(ragdollPath, speciesName, ragdollConfig) as RagdollParams;
            // Animations
            string animFolder = AnimationParams.GetDefaultFolder(speciesName);
            foreach (AnimationType animType in Enum.GetValues(typeof(AnimationType)))
            {
                if (animType != AnimationType.NotDefined)
                {
                    Type type = AnimationParams.GetParamTypeFromAnimType(animType, isHumanoid);
                    string fullPath = AnimationParams.GetDefaultFile(speciesName, animType);
                    AnimationParams.Create(fullPath, speciesName, animType, type);
                }
            }
            if (!AllFiles.Contains(configFilePath))
            {
                AllFiles.Add(configFilePath);
            }
            SpawnCharacter(configFilePath, ragdollParams);
        }
        #endregion

        #region GUI
        private GUIFrame rightPanel;
        private GUIFrame centerPanel;
        private GUIFrame ragdollControls;
        private GUIFrame animationControls;
        private GUIFrame limbControls;
        private GUIFrame spriteSheetControls;
        private GUIFrame backgroundColorPanel;
        private GUIDropDown animSelection;
        private GUITickBox freezeToggle;
        private GUITickBox animTestPoseToggle;
        private GUIScrollBar jointScaleBar;
        private GUIScrollBar limbScaleBar;
        private GUIScrollBar textureScaleBar;
        private GUIScrollBar spriteSheetZoomBar;
        private GUITickBox copyJointsToggle;
        private GUITickBox jointsToggle;

        private void CreateGUI()
        {
            CreateRightPanel();
            CreateCenterPanel();
        }

        private void CreateCenterPanel()
        {
            // Release the old panel
            if (centerPanel != null)
            {
                centerPanel.RectTransform.Parent = null;
            }
            Point elementSize = new Point(120, 20);
            int textAreaHeight = 20;
            centerPanel = new GUIFrame(new RectTransform(new Vector2(0.45f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.Center), style: null) { CanBeFocused = false };
            // General controls
            backgroundColorPanel = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerPanel.RectTransform, Anchor.BottomRight), style: null) { CanBeFocused = false };
            var layoutGroupGeneral = new GUILayoutGroup(new RectTransform(Vector2.One, backgroundColorPanel.RectTransform), childAnchor: Anchor.BottomRight)
            {
                AbsoluteSpacing = 5, CanBeFocused = false
            };
            // Background color
            var frame = new GUIFrame(new RectTransform(new Point(500, 80), layoutGroupGeneral.RectTransform), style: null, color: Color.Black * 0.4f);
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), frame.RectTransform) { MinSize = new Point(80, 26) }, "Background Color:", textColor: Color.WhiteSmoke);
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1), frame.RectTransform, Anchor.TopRight)
            {
                AbsoluteOffset = new Point(20, 0)
            }, isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            var fields = new GUIComponent[4];
            string[] colorComponentLabels = { "R", "G", "B" };
            for (int i = 2; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.2f, 1), inputArea.RectTransform)
                {
                    MinSize = new Point(40, 0),
                    MaxSize = new Point(100, 50)
                }, style: null, color: Color.Black * 0.6f);
                var colorLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), colorComponentLabels[i], 
                    font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = GUI.SmallFont
                };
                numberInput.MinValueInt = 0;
                numberInput.MaxValueInt = 255;
                numberInput.Font = GUI.SmallFont;
                switch (i)
                {
                    case 0:
                        colorLabel.TextColor = Color.Red;
                        numberInput.IntValue = backgroundColor.R;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.R = (byte)(numInput.IntValue);
                        break;
                    case 1:
                        colorLabel.TextColor = Color.LightGreen;
                        numberInput.IntValue = backgroundColor.G;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.G = (byte)(numInput.IntValue);
                        break;
                    case 2:
                        colorLabel.TextColor = Color.DeepSkyBlue;
                        numberInput.IntValue = backgroundColor.B;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.B = (byte)(numInput.IntValue);
                        break;
                }
            }
            // Spritesheet controls
            spriteSheetControls = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerPanel.RectTransform, Anchor.TopRight)
            {
                AbsoluteOffset = new Point(100, 0)
            }, style: null)
            {
                CanBeFocused = false
            };
            var layoutGroupSpriteSheet = new GUILayoutGroup(new RectTransform(Vector2.One, spriteSheetControls.RectTransform)) { AbsoluteSpacing = 5, CanBeFocused = false };
            new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), "Spritesheet zoom:", Color.White);
            var spriteSheetControlElement = new GUIFrame(new RectTransform(new Point(elementSize.X * 2, textAreaHeight), layoutGroupSpriteSheet.RectTransform), style: null);
            CalculateSpritesheetZoom();
            spriteSheetZoomBar = new GUIScrollBar(new RectTransform(new Vector2(0.75f, 1), spriteSheetControlElement.RectTransform), barSize: 0.2f)
            {
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(spriteSheetMinZoom, spriteSheetMaxZoom, spriteSheetZoom)),
                Step = 0.01f,
                OnMoved = (scrollBar, value) =>
                {
                    spriteSheetZoom = MathHelper.Lerp(spriteSheetMinZoom, spriteSheetMaxZoom, value);
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.2f, 1), spriteSheetControlElement.RectTransform, Anchor.TopRight), "Reset")
            {
                OnClicked = (box, data) =>
                {
                    spriteSheetZoom = Math.Min(1, spriteSheetMaxZoom);
                    spriteSheetZoomBar.BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(spriteSheetMinZoom, spriteSheetMaxZoom, spriteSheetZoom));
                    return true;
                }
            };
            new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), "Texture scale:", Color.White);
            textureScaleBar = new GUIScrollBar(new RectTransform(new Point((int)(elementSize.X * 1.75f), textAreaHeight), layoutGroupSpriteSheet.RectTransform), barSize: 0.2f)
            {
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(textureMinScale, textureMaxScale, RagdollParams.TextureScale)),
                Step = 0.01f,
                OnMoved = (scrollBar, value) =>
                {
                    RagdollParams.TextureScale = MathHelper.Lerp(textureMinScale, textureMaxScale, value);
                    return true;
                }
            };
            // Limb controls
            limbControls = new GUIFrame(new RectTransform(Vector2.One, centerPanel.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupLimbControls = new GUILayoutGroup(new RectTransform(Vector2.One, limbControls.RectTransform)) { CanBeFocused = false };
            // Spacing
            new GUIFrame(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), style: null) { CanBeFocused = false };

            var lockSpriteOriginToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), "Lock Sprite Origin")
            {
                Selected = lockSpriteOrigin,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpriteOrigin = box.Selected;
                    return true;
                }
            };
            lockSpriteOriginToggle.TextColor = Color.White;
            var lockSpritePositionToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), "Lock Sprite Position")
            {
                Selected = lockSpritePosition,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpritePosition = box.Selected;
                    return true;
                }
            };
            lockSpritePositionToggle.TextColor = Color.White;
            var lockSpriteSizeToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), "Lock Sprite Size")
            {
                Selected = lockSpriteSize,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpriteSize = box.Selected;
                    return true;
                }
            };
            lockSpriteSizeToggle.TextColor = Color.White;
            // Ragdoll
            Point sliderSize = new Point(300, 20);
            ragdollControls = new GUIFrame(new RectTransform(Vector2.One, centerPanel.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupRagdoll = new GUILayoutGroup(new RectTransform(Vector2.One, ragdollControls.RectTransform)) { CanBeFocused = false };
            var jointScaleElement = new GUIFrame(new RectTransform(sliderSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var jointScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), jointScaleElement.RectTransform), $"Joint Scale: {RagdollParams.JointScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
            var limbScaleElement = new GUIFrame(new RectTransform(sliderSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var limbScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), limbScaleElement.RectTransform), $"Limb Scale: {RagdollParams.LimbScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
            jointScaleBar = new GUIScrollBar(new RectTransform(sliderSize, jointScaleElement.RectTransform, Anchor.BottomLeft), barSize: 0.1f)
            {
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE, RagdollParams.JointScale)),
                Step = 0.001f,
                OnMoved = (scrollBar, value) =>
                {
                    float v = MathHelper.Lerp(RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE, value);
                    UpdateJointScale(v);
                    if (uniformScaling)
                    {
                        UpdateLimbScale(v);
                        limbScaleBar.BarScroll = value;
                    }
                    return true;
                }
            };
            limbScaleBar = new GUIScrollBar(new RectTransform(sliderSize, limbScaleElement.RectTransform, Anchor.BottomLeft), barSize: 0.1f)
            {
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE, RagdollParams.LimbScale)),
                Step = 0.001f,
                OnMoved = (scrollBar, value) =>
                {
                    float v = MathHelper.Lerp(RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE, value);
                    UpdateLimbScale(v);
                    if (uniformScaling)
                    {
                        UpdateJointScale(v);
                        jointScaleBar.BarScroll = value;
                    }
                    return true;
                }
            };
            void UpdateJointScale(float value)
            {
                TryUpdateRagdollParam("jointscale", value);
                jointScaleText.Text = $"Joint Scale: {RagdollParams.JointScale.FormatDoubleDecimal()}";
                character.AnimController.ResetJoints();
            }
            void UpdateLimbScale(float value)
            {
                TryUpdateRagdollParam("limbscale", value);
                limbScaleText.Text = $"Limb Scale: {RagdollParams.LimbScale.FormatDoubleDecimal()}";
            }
            // TODO: doesn't trigger if the mouse is released while the cursor is outside the button rect
            limbScaleBar.Bar.OnClicked += (button, data) =>
            {
                RecreateRagdoll();
                return true;
            };
            jointScaleBar.Bar.OnClicked += (button, data) =>
            {
                if (uniformScaling)
                {
                    RecreateRagdoll();
                }
                return true;
            };
            var uniformScalingToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), ragdollControls.RectTransform)
            {
                AbsoluteOffset = new Point(0, textAreaHeight * 4 + 10)
            }, "Uniform Scale")
            {
                Selected = uniformScaling,
                OnSelected = (GUITickBox box) =>
                {
                    uniformScaling = box.Selected;
                    return true;
                }
            };
            uniformScalingToggle.TextColor = Color.White;
            copyJointsToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), ragdollControls.RectTransform)
            {
                AbsoluteOffset = new Point(0, textAreaHeight * 5 + 10)
            }, "Copy Joint Settings")
            {
                ToolTip = "Copies the currently tweaked settings to all selected joints.",
                Selected = copyJointSettings,
                TextColor = copyJointSettings ? Color.Red : Color.White,
                OnSelected = (GUITickBox box) =>
                {
                    copyJointSettings = box.Selected;
                    box.TextColor = copyJointSettings ? Color.Red : Color.White;
                    return true;
                }
            };
            // Animation
            animationControls = new GUIFrame(new RectTransform(Vector2.One, centerPanel.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupAnimation = new GUILayoutGroup(new RectTransform(Vector2.One, animationControls.RectTransform)) { CanBeFocused = false };
            var animationSelectionElement = new GUIFrame(new RectTransform(new Point(elementSize.X * 2 - 5, elementSize.Y), layoutGroupAnimation.RectTransform), style: null);
            var animationSelectionText = new GUITextBlock(new RectTransform(new Point(elementSize.X, elementSize.Y), animationSelectionElement.RectTransform), "Selected Animation:", Color.WhiteSmoke, textAlignment: Alignment.Center);
            animSelection = new GUIDropDown(new RectTransform(new Point(100, elementSize.Y), animationSelectionElement.RectTransform, Anchor.TopRight), elementCount: 4);
            if (character.AnimController.CanWalk)
            {
                animSelection.AddItem(AnimationType.Walk.ToString(), AnimationType.Walk);
                animSelection.AddItem(AnimationType.Run.ToString(), AnimationType.Run);
            }
            animSelection.AddItem(AnimationType.SwimSlow.ToString(), AnimationType.SwimSlow);
            animSelection.AddItem(AnimationType.SwimFast.ToString(), AnimationType.SwimFast);
            animSelection.SelectItem(character.AnimController.CanWalk ? AnimationType.Walk : AnimationType.SwimSlow);
            animSelection.OnSelected += (element, data) =>
            {
                AnimationType previousAnim = character.AnimController.ForceSelectAnimationType;
                character.AnimController.ForceSelectAnimationType = (AnimationType)data;               
                switch (character.AnimController.ForceSelectAnimationType)
                {
                    case AnimationType.Walk:
                        character.AnimController.forceStanding = true;
                        character.ForceRun = false;
                        if (!wallCollisionsEnabled)
                        {
                            SetWallCollisions(true);
                        }
                        if (previousAnim != AnimationType.Walk && previousAnim != AnimationType.Run)
                        {
                            TeleportTo(spawnPosition);
                        }
                        break;
                    case AnimationType.Run:
                        character.AnimController.forceStanding = true;
                        character.ForceRun = true;
                        if (!wallCollisionsEnabled)
                        {
                            SetWallCollisions(true);
                        }
                        if (previousAnim != AnimationType.Walk && previousAnim != AnimationType.Run)
                        {
                            TeleportTo(spawnPosition);
                        }
                        break;
                    case AnimationType.SwimSlow:
                        character.AnimController.forceStanding = false;
                        character.ForceRun = false;
                        if (wallCollisionsEnabled)
                        {
                            SetWallCollisions(false);
                        }
                        break;
                    case AnimationType.SwimFast:
                        character.AnimController.forceStanding = false;
                        character.ForceRun = true;
                        if (wallCollisionsEnabled)
                        {
                            SetWallCollisions(false);
                        }
                        break;
                    default:
                        throw new NotImplementedException();
                }
                return true;
            };
        }

        private void CreateRightPanel()
        {
            // Release the old panel
            if (rightPanel != null)
            {
                rightPanel.RectTransform.Parent = null;
            }
            Vector2 buttonSize = new Vector2(1, 0.04f);
            Vector2 toggleSize = new Vector2(0.03f, 0.03f);
            Point margin = new Point(40, 60);
            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.15f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.CenterRight) { RelativeOffset = new Vector2(0.01f, 0) });
            var layoutGroup = new GUILayoutGroup(new RectTransform(new Point(rightPanel.Rect.Width - margin.X, rightPanel.Rect.Height - margin.Y), rightPanel.RectTransform, Anchor.Center));
            var characterDropDown = new GUIDropDown(new RectTransform(new Vector2(1, 0.04f), layoutGroup.RectTransform), elementCount: 10, style: null);
            characterDropDown.ListBox.Color = new Color(characterDropDown.ListBox.Color.R, characterDropDown.ListBox.Color.G, characterDropDown.ListBox.Color.B, byte.MaxValue);
            foreach (var file in AllFiles)
            {
                characterDropDown.AddItem(Path.GetFileNameWithoutExtension(file).CapitaliseFirstInvariant(), file);
            }
            characterDropDown.SelectItem(currentCharacterConfig);
            characterDropDown.OnSelected = (component, data) =>
            {
                SpawnCharacter((string)data);
                return true;
            };
            if (currentCharacterConfig == Character.HumanConfigFile)
            {
                var jobDropDown = new GUIDropDown(new RectTransform(new Vector2(1, 0.04f), layoutGroup.RectTransform), elementCount: 6, style: null);
                jobDropDown.ListBox.Color = new Color(jobDropDown.ListBox.Color.R, jobDropDown.ListBox.Color.G, jobDropDown.ListBox.Color.B, byte.MaxValue);
                jobDropDown.AddItem("None");
                JobPrefab.List.ForEach(j => jobDropDown.AddItem(j.Name, j.Identifier));
                jobDropDown.SelectItem(selectedJob);
                jobDropDown.OnSelected = (component, data) =>
                {
                    string newJob = data is string jobIdentifier ? jobIdentifier : null;
                    if (newJob != selectedJob)
                    {
                        selectedJob = newJob;
                        SpawnCharacter(currentCharacterConfig);
                    }
                    return true;
                };
            }
            var charButtons = new GUIFrame(new RectTransform(buttonSize, parent: layoutGroup.RectTransform), style: null);
            var prevCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1), charButtons.RectTransform, Anchor.TopLeft), "Previous \nCharacter");
            prevCharacterButton.OnClicked += (b, obj) =>
            {
                SpawnCharacter(GetPreviousConfigFile());
                return true;
            };
            var nextCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1), charButtons.RectTransform, Anchor.TopRight), "Next \nCharacter");
            nextCharacterButton.OnClicked += (b, obj) =>
            {
                SpawnCharacter(GetNextConfigFile());
                return true;
            };
            var paramsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Parameters") { Selected = showParamsEditor };
            var spritesheetToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Spritesheet") { Selected = showSpritesheet };
            var ragdollToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Show Ragdoll") { Selected = showRagdoll };
            var editAnimsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Animations") { Selected = editAnimations };       
            var editLimbsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Limbs") { Selected = editLimbs };
            jointsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Joints") { Selected = editJoints };
            var ikToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit IK Targets") { Selected = editIK };
            freezeToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Freeze") { Selected = isFreezed };
            var autoFreezeToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Auto Freeze") { Selected = autoFreeze };
            var limbPairEditToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Limb Pair Editing") { Selected = limbPairEditing };
            animTestPoseToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Animation Test Pose") { Selected = character.AnimController.AnimationTestPose };
            editAnimsToggle.OnSelected = box =>
            {
                editAnimations = box.Selected;
                if (editAnimations)
                {
                    spritesheetToggle.Selected = false;
                    editLimbsToggle.Selected = false;
                    jointsToggle.Selected = false;
                    ResetParamsEditor();
                }
                return true;
            };
            paramsToggle.OnSelected = box =>
            {
                showParamsEditor = box.Selected;
                return true;
            };
            editLimbsToggle.OnSelected = box =>
            {
                editLimbs = box.Selected;
                if (editLimbs)
                {
                    editAnimsToggle.Selected = false;
                    jointsToggle.Selected = false;
                    spritesheetToggle.Selected = true;
                    ResetParamsEditor();
                }
                return true;
            };
            ragdollToggle.OnSelected = box => showRagdoll = box.Selected;
            jointsToggle.OnSelected = box =>
            {
                editJoints = box.Selected;
                if (editJoints)
                {
                    editLimbsToggle.Selected = false;
                    editAnimsToggle.Selected = false;
                    ikToggle.Selected = false;
                    spritesheetToggle.Selected = true;
                    ragdollToggle.Selected = true;
                    ResetParamsEditor();
                }
                return true;
            };
            ikToggle.OnSelected = box =>
            {
                editIK = box.Selected;
                if (editIK)
                {
                    ragdollToggle.Selected = true;
                }
                return true;
            };
            spritesheetToggle.OnSelected = box =>
            {
                showSpritesheet = box.Selected;
                return true;
            };
            freezeToggle.OnSelected = box =>
            {
                isFreezed = box.Selected;
                return true;
            };
            autoFreezeToggle.OnSelected = box =>
            {
                autoFreeze = box.Selected;
                return true;
            };
            limbPairEditToggle.OnSelected = box =>
            {
                limbPairEditing = box.Selected;
                return true;
            };
            animTestPoseToggle.OnSelected = box =>
            {
                character.AnimController.AnimationTestPose = box.Selected;
                return true;
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Auto Move")
            {
                OnSelected = box =>
                {
                    character.OverrideMovement = box.Selected ? new Vector2(1, 0) as Vector2? : null;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Follow Cursor")
            {
                Selected = !character.dontFollowCursor,
                OnSelected = box =>
                {
                    character.dontFollowCursor = !box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Display Colliders")
            {
                Selected = displayColliders,
                OnSelected = box =>
                {
                    displayColliders = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Background Color")
            {
                Selected = displayBackgroundColor,
                OnSelected = box =>
                {
                    displayBackgroundColor = box.Selected;
                    return true;
                }
            };

            var quickSaveAnimButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Quick Save Animations");
            quickSaveAnimButton.OnClicked += (button, userData) =>
            {
                AnimParams.ForEach(p => p.Save());
                GUI.AddMessage($"All animations saved", Color.Green, font: GUI.Font);
                return true;
            };
            var quickSaveRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Quick Save Ragdoll");
            quickSaveRagdollButton.OnClicked += (button, userData) =>
            {
                character.AnimController.SaveRagdoll();
                GUI.AddMessage($"Ragdoll saved to {RagdollParams.FullPath}", Color.Green, font: GUI.Font);
                return true;
            };
            var resetAnimButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Reset Animations");
            resetAnimButton.OnClicked += (button, userData) =>
            {
                AnimParams.ForEach(p => p.Reset());
                ResetParamsEditor();
                GUI.AddMessage($"All animations reset", Color.WhiteSmoke, font: GUI.Font);
                return true;
            };
            var resetRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Reset Ragdoll");
            resetRagdollButton.OnClicked += (button, userData) =>
            {
                if (ragdollResetRequiresForceLoading)
                {
                    character.AnimController.ResetRagdoll(forceReload: true);
                    RecreateRagdoll();
                    ragdollResetRequiresForceLoading = false;
                }
                else
                {
                    character.AnimController.ResetRagdoll(forceReload: false);
                    ResetParamsEditor();
                    ClearWidgets();
                }
                ResetParamsEditor();
                ClearWidgets();
                CreateCenterPanel();
                GUI.AddMessage($"Ragdoll reset", Color.WhiteSmoke, font: GUI.Font);
                return true;
            };
            int messageBoxWidth = GameMain.GraphicsWidth / 2;
            int messageBoxHeight = GameMain.GraphicsHeight / 2;
            var saveRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Save Ragdoll");
            saveRagdollButton.OnClicked += (button, userData) =>
            {
                var box = new GUIMessageBox("Save Ragdoll", "Please provide a name for the file:", new string[] { "Cancel", "Save" }, messageBoxWidth, messageBoxHeight);
                var inputField = new GUITextBox(new RectTransform(new Point(box.Content.Rect.Width, 30), box.Content.RectTransform, Anchor.Center), RagdollParams.Name);
                box.Buttons[0].OnClicked += (b, d) =>
                {
                    box.Close();
                    return true;
                };
                box.Buttons[1].OnClicked += (b, d) =>
                {
                    character.AnimController.SaveRagdoll(inputField.Text);
                    ResetParamsEditor();
                    GUI.AddMessage($"Ragdoll saved to {RagdollParams.FullPath}", Color.Green, font: GUI.Font);
                    box.Close();
                    return true;
                };
                return true;
            };
            var loadRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Load Ragdoll");
            loadRagdollButton.OnClicked += (button, userData) =>
            {
                var loadBox = new GUIMessageBox("Load Ragdoll", "", new string[] { "Cancel", "Load", "Delete" }, messageBoxWidth, messageBoxHeight);
                loadBox.Buttons[0].OnClicked += loadBox.Close;
                var listBox = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.6f), loadBox.Content.RectTransform, Anchor.TopCenter));
                var deleteButton = loadBox.Buttons[2];
                deleteButton.Enabled = false;
                void PopulateListBox()
                {
                    try
                    {
                        var filePaths = Directory.GetFiles(RagdollParams.Folder);
                        foreach (var path in filePaths)
                        {
                            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform) { MinSize = new Point(0, 30) },
                                ToolBox.LimitString(Path.GetFileNameWithoutExtension(path), GUI.Font, listBox.Rect.Width - 80))
                            {
                                UserData = path,
                                ToolTip = path
                            };
                        }
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Couldn't open directory \"" + RagdollParams.Folder + "\"!", e);
                    }
                }
                PopulateListBox();
                // Handle file selection
                string selectedFile = null;
                listBox.OnSelected += (component, data) =>
                {
                    selectedFile = data as string;
                    // Don't allow to delete the ragdoll that is currently in use, nor the default file.
                    var fileName = Path.GetFileNameWithoutExtension(selectedFile);
                    deleteButton.Enabled = fileName != RagdollParams.Name && fileName != RagdollParams.GetDefaultFileName(character.SpeciesName);
                    return true;
                };
                deleteButton.OnClicked += (btn, data) =>
                {
                    if (selectedFile == null)
                    {
                        loadBox.Close();
                        return false;
                    }
                    var msgBox = new GUIMessageBox(
                        TextManager.Get("DeleteDialogLabel"),
                        TextManager.Get("DeleteDialogQuestion").Replace("[file]", selectedFile),
                        new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") }, messageBoxWidth - 100, messageBoxHeight - 100);
                    msgBox.Buttons[0].OnClicked += (b, d) =>
                    {
                        try
                        {
                            File.Delete(selectedFile);
                            GUI.AddMessage($"Ragdoll deleted from {selectedFile}", Color.Red, font: GUI.Font);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError(TextManager.Get("DeleteFileError").Replace("[file]", selectedFile), e);
                        }
                        msgBox.Close();
                        listBox.ClearChildren();
                        PopulateListBox();
                        selectedFile = null;
                        return true;
                    };
                    msgBox.Buttons[1].OnClicked += (b, d) =>
                    {
                        msgBox.Close();
                        return true;
                    };
                    return true;
                };
                loadBox.Buttons[1].OnClicked += (btn, data) =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(selectedFile);
                    var ragdoll = character.IsHumanoid ? HumanRagdollParams.GetRagdollParams(character.SpeciesName, fileName) as RagdollParams : RagdollParams.GetRagdollParams<FishRagdollParams>(character.SpeciesName, fileName);
                    GUI.AddMessage($"Ragdoll loaded from {selectedFile}", Color.WhiteSmoke, font: GUI.Font);
                    RecreateRagdoll(ragdoll);
                    CreateTextures();
                    CreateCenterPanel();
                    loadBox.Close();
                    return true;
                };
                return true;
            };
            var saveAnimationButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Save Animation");
            saveAnimationButton.OnClicked += (button, userData) =>
            {
                var box = new GUIMessageBox("Save Animation", string.Empty, new string[] { "Cancel", "Save" }, messageBoxWidth, messageBoxHeight);
                var textArea = new GUIFrame(new RectTransform(new Vector2(1, 0.1f), box.Content.RectTransform) { MinSize = new Point(350, 30) }, style: null);
                var inputLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), textArea.RectTransform) { MinSize = new Point(250, 30) }, "Please provide a name for the file:");
                var inputField = new GUITextBox(new RectTransform(new Vector2(0.5f, 1), textArea.RectTransform, Anchor.TopRight) { MinSize = new Point(100, 30) }, CurrentAnimation.Name);
                // Type filtering
                var typeSelectionArea = new GUIFrame(new RectTransform(new Vector2(1f, 0.1f), box.Content.RectTransform) { MinSize = new Point(0, 30) }, style: null);
                var typeLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), typeSelectionArea.RectTransform, Anchor.TopCenter, Pivot.TopRight), "Select Animation Type:");
                var typeDropdown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1), typeSelectionArea.RectTransform, Anchor.TopCenter, Pivot.TopLeft), elementCount: 4);
                foreach (object enumValue in Enum.GetValues(typeof(AnimationType)))
                {
                    typeDropdown.AddItem(enumValue.ToString(), enumValue);
                }
                AnimationType selectedType = character.AnimController.ForceSelectAnimationType;
                typeDropdown.OnSelected = (component, data) =>
                {
                    selectedType = (AnimationType)data;
                    inputField.Text = character.AnimController.GetAnimationParamsFromType(selectedType).Name;
                    return true;
                };
                typeDropdown.SelectItem(selectedType);
                box.Buttons[0].OnClicked += (b, d) =>
                {
                    box.Close();
                    return true;
                };
                box.Buttons[1].OnClicked += (b, d) =>
                {
                    var animParams = character.AnimController.GetAnimationParamsFromType(selectedType);
                    animParams.Save(inputField.Text);
                    GUI.AddMessage($"Animation of type {animParams.AnimationType} saved to {animParams.FullPath}", Color.Green, font: GUI.Font);
                    ResetParamsEditor();
                    box.Close();
                    return true;
                };
                return true;
            };
            var loadAnimationButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Load Animation");
            loadAnimationButton.OnClicked += (button, userData) =>
            {
                var loadBox = new GUIMessageBox("Load Animation", "", new string[] { "Cancel", "Load", "Delete" }, messageBoxWidth, messageBoxHeight);
                loadBox.Buttons[0].OnClicked += loadBox.Close;
                var listBox = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.6f), loadBox.Content.RectTransform));
                var deleteButton = loadBox.Buttons[2];
                deleteButton.Enabled = false;
                // Type filtering
                var typeSelectionArea = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.1f), loadBox.Content.RectTransform) { MinSize = new Point(0, 30) }, style: null);
                var typeLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), typeSelectionArea.RectTransform, Anchor.TopCenter, Pivot.TopRight), "Select Animation Type:");
                var typeDropdown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1), typeSelectionArea.RectTransform, Anchor.TopCenter, Pivot.TopLeft), elementCount: 4);
                foreach (object enumValue in Enum.GetValues(typeof(AnimationType)))
                {
                    typeDropdown.AddItem(enumValue.ToString(), enumValue);
                }
                AnimationType selectedType = character.AnimController.ForceSelectAnimationType;
                typeDropdown.OnSelected = (component, data) =>
                {
                    selectedType = (AnimationType)data;
                    PopulateListBox();
                    return true;
                };
                typeDropdown.SelectItem(selectedType);
                void PopulateListBox()
                {
                    try
                    {
                        listBox.ClearChildren();
                        var filePaths = Directory.GetFiles(CurrentAnimation.Folder);
                        foreach (var path in AnimationParams.FilterFilesByType(filePaths, selectedType))
                        {
                            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform) { MinSize = new Point(0, 30) }, ToolBox.LimitString(Path.GetFileNameWithoutExtension(path), GUI.Font, listBox.Rect.Width - 80))
                            {
                                UserData = path,
                                ToolTip = path
                            };
                        }
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Couldn't open directory \"" + CurrentAnimation.Folder + "\"!", e);
                    }
                }
                PopulateListBox();
                // Handle file selection
                string selectedFile = null;
                listBox.OnSelected += (component, data) =>
                {
                    selectedFile = data as string;
                    // Don't allow to delete the animation that is currently in use, nor the default file.
                    var fileName = Path.GetFileNameWithoutExtension(selectedFile);
                    deleteButton.Enabled = fileName != CurrentAnimation.Name && fileName != AnimationParams.GetDefaultFileName(character.SpeciesName, CurrentAnimation.AnimationType);
                    return true;
                };
                deleteButton.OnClicked += (btn, data) =>
                {
                    if (selectedFile == null)
                    {
                        loadBox.Close();
                        return false;
                    }
                    var msgBox = new GUIMessageBox(
                        TextManager.Get("DeleteDialogLabel"),
                        TextManager.Get("DeleteDialogQuestion").Replace("[file]", selectedFile),
                        new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") }, messageBoxWidth - 100, messageBoxHeight - 100);
                    msgBox.Buttons[0].OnClicked += (b, d) =>
                    {
                        try
                        {
                            File.Delete(selectedFile);
                            GUI.AddMessage($"Animation of type {selectedType} at {selectedFile} deleted", Color.Red, font: GUI.Font);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError(TextManager.Get("DeleteFileError").Replace("[file]", selectedFile), e);
                        }
                        msgBox.Close();
                        PopulateListBox();
                        selectedFile = null;
                        return true;
                    };
                    msgBox.Buttons[1].OnClicked += (b, d) =>
                    {
                        msgBox.Close();
                        return true;
                    };
                    return true;
                };
                loadBox.Buttons[1].OnClicked += (btn, data) =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(selectedFile);
                    if (character.IsHumanoid)
                    {
                        switch (selectedType)
                        {
                            case AnimationType.Walk:
                                character.AnimController.WalkParams = HumanWalkParams.GetAnimParams(character, fileName);
                            break;
                            case AnimationType.Run:
                                character.AnimController.RunParams = HumanRunParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.SwimSlow:
                                character.AnimController.SwimSlowParams = HumanSwimSlowParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.SwimFast:
                                character.AnimController.SwimFastParams = HumanSwimFastParams.GetAnimParams(character, fileName);
                                break;
                            default:
                                DebugConsole.ThrowError($"Animation type {selectedType.ToString()} not implemented!");
                                break;
                        }
                    }
                    else
                    {
                        switch (selectedType)
                        {
                            case AnimationType.Walk:
                                character.AnimController.WalkParams = FishWalkParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.Run:
                                character.AnimController.RunParams = FishRunParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.SwimSlow:
                                character.AnimController.SwimSlowParams = FishSwimSlowParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.SwimFast:
                                character.AnimController.SwimFastParams = FishSwimFastParams.GetAnimParams(character, fileName);
                                break;
                            default:
                                DebugConsole.ThrowError($"Animation type {selectedType.ToString()} not implemented!");
                                break;
                        }
                    }
                    GUI.AddMessage($"Animation of type {selectedType} loaded from {selectedFile}", Color.WhiteSmoke, font: GUI.Font);
                    ResetParamsEditor();
                    loadBox.Close();
                    return true;
                };
                return true;
            };
            var reloadTexturesButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Reload Textures");
            reloadTexturesButton.OnClicked += (button, userData) =>
            {
                foreach (var limb in character.AnimController.Limbs)
                {
                    limb.ActiveSprite.ReloadTexture();
                }
                return true;
            };
            new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Recreate Ragdoll")
            {
                ToolTip = "Many of the parameters requires recreation of the ragdoll. If adjusting the parameter doesn't seem to have effect, click this button.",
                OnClicked = (button, data) =>
                {
                    RecreateRagdoll();
                    character.AnimController.ResetLimbs();
                    return true;
                }
            };
            new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), "Create New Character")
            {
                OnClicked = (button, data) =>
                {
                    Wizard.Instance.SelectTab(Wizard.Tab.Character);
                    return true;
                }
            };
        }
        #endregion

        #region Params
        private List<AnimationParams> AnimParams => character.AnimController.AllAnimParams;
        private AnimationParams CurrentAnimation => character.AnimController.CurrentAnimationParams;
        private RagdollParams RagdollParams => character.AnimController.RagdollParams;
        
        private void ResetParamsEditor()
        {
            ParamsEditor.Instance.Clear();
            if (editAnimations)
            {
                AnimParams.ForEach(p => p.AddToEditor(ParamsEditor.Instance));
            }
            else if (editJoints || editLimbs || editIK)
            {
                if (selectedJoints.Any())
                {
                    foreach (var jointParams in RagdollParams.Joints)
                    {
                        if (selectedJoints.Any(j => j.jointParams == jointParams))
                        {
                            jointParams?.AddToEditor(ParamsEditor.Instance);
                        }
                    }
                }
                if (selectedLimbs.Any())
                {
                    foreach (var limbParams in RagdollParams.Limbs)
                    {
                        if (selectedLimbs.Any(l => l.limbParams == limbParams))
                        {
                            limbParams?.AddToEditor(ParamsEditor.Instance);
                        }
                    }
                }
                if (selectedJoints.None() && selectedLimbs.None())
                {
                    RagdollParams.AddToEditor(ParamsEditor.Instance);
                }
            }
        }

        private void TryUpdateAnimParam(string name, object value) => TryUpdateParam(character.AnimController.CurrentAnimationParams, name, value);
        private void TryUpdateRagdollParam(string name, object value) => TryUpdateParam(RagdollParams, name, value);

        private void TryUpdateParam(EditableParams editableParams, string name, object value)
        {
            if (editableParams.SerializableProperties.TryGetValue(name, out SerializableProperty p))
            {
                editableParams.SerializableEntityEditor?.UpdateValue(p, value);
            }
        }

        private void TryUpdateJointParam(LimbJoint joint, string name, object value) => TryUpdateSubParam(joint.jointParams, name, value);
        private void TryUpdateLimbParam(Limb limb, string name, object value) => TryUpdateSubParam(limb.limbParams, name, value);

        private void TryUpdateSubParam(RagdollSubParams ragdollSubParams, string name, object value)
        {
            if (ragdollSubParams.SerializableProperties.TryGetValue(name, out SerializableProperty p))
            {
                ragdollSubParams.SerializableEntityEditor?.UpdateValue(p, value);
            }
            else
            {
                var subParams = ragdollSubParams.SubParams.Where(sp => sp.SerializableProperties.ContainsKey(name)).FirstOrDefault();
                if (subParams != null)
                {
                    if (subParams.SerializableProperties.TryGetValue(name, out p))
                    {
                        subParams.SerializableEntityEditor?.UpdateValue(p, value);
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"No field for {name} found!");
                    //ragdollParams.SubParams.ForEach(sp => sp.SerializableProperties.ForEach(prop => DebugConsole.ThrowError($"{sp.Name}: sub param field: {prop.Key}")));
                }
            }
        }
        #endregion

        #region Helpers
        private Vector2 ScreenToSim(float x, float y) => ScreenToSim(new Vector2(x, y));
        private Vector2 ScreenToSim(Vector2 p) => ConvertUnits.ToSimUnits(Cam.ScreenToWorld(p)) + Submarine.MainSub.SimPosition;
        private Vector2 SimToScreen(float x, float y) => SimToScreen(new Vector2(x, y));
        private Vector2 SimToScreen(Vector2 p) => Cam.WorldToScreen(ConvertUnits.ToDisplayUnits(p + Submarine.MainSub.SimPosition));

        private void ValidateJoint(LimbJoint limbJoint)
        {
            if (limbJoint.UpperLimit < limbJoint.LowerLimit)
            {
                if (limbJoint.LowerLimit > 0.0f) limbJoint.LowerLimit -= MathHelper.TwoPi;
                if (limbJoint.UpperLimit < 0.0f) limbJoint.UpperLimit += MathHelper.TwoPi;
            }

            if (limbJoint.UpperLimit - limbJoint.LowerLimit > MathHelper.TwoPi)
            {
                // Wrapping the limits between PI seems to cause the joint angles being flipped by 180 degrees.
                //limbJoint.LowerLimit = MathUtils.WrapAnglePi(limbJoint.LowerLimit);
                //limbJoint.UpperLimit = MathUtils.WrapAnglePi(limbJoint.UpperLimit);
                limbJoint.LowerLimit = MathUtils.WrapAngleTwoPi(limbJoint.LowerLimit);
                limbJoint.UpperLimit = MathUtils.WrapAngleTwoPi(limbJoint.UpperLimit);
            }
        }

        private Limb GetClosestLimbOnRagdoll(Vector2 targetPos, Func<Limb, bool> filter = null)
        {
            // TODO: optimize?
            return character.AnimController.Limbs
                .Where(l => filter == null ? true : filter(l))
                .OrderBy(l => Vector2.Distance(SimToScreen(l.SimPosition), targetPos))
                .FirstOrDefault();
        }

        private Limb GetClosestLimbOnSpritesheet(Vector2 targetPos, Func<Limb, bool> filter = null)
        {
            // TODO: optimize?
            return character.AnimController.Limbs
                .Where(l => filter == null ? true : filter(l))
                .OrderBy(l => Vector2.Distance(GetLimbSpritesheetRect(l).Center.ToVector2(), targetPos))
                .FirstOrDefault();
        }

        private Rectangle GetLimbSpritesheetRect(Limb limb)
        {
            int offsetX = spriteSheetOffsetX;
            int offsetY = spriteSheetOffsetY;
            Rectangle rect = Rectangle.Empty;
            for (int i = 0; i < Textures.Count; i++)
            {
                if (limb.ActiveSprite.FilePath != texturePaths[i])
                {
                    offsetY += (int)(Textures[i].Height * spriteSheetZoom);
                }
                else
                {
                    rect = limb.ActiveSprite.SourceRect;
                    rect.Size = rect.MultiplySize(spriteSheetZoom);
                    rect.Location = rect.Location.Multiply(spriteSheetZoom);
                    rect.X += offsetX;
                    rect.Y += offsetY;
                    Vector2 origin = limb.ActiveSprite.Origin;
                    break;
                }
            }
            return rect;
        }

        private void DrawJointCreationOnSpritesheet(SpriteBatch spriteBatch, Vector2 startPos)
        {
            // Spritesheet
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 200, GameMain.GraphicsHeight - 150), "Left click to select the target limb for the other end of the joint.", Color.White, Color.Black * 0.5f, 10, GUI.Font);
            GUI.DrawLine(spriteBatch, startPos, PlayerInput.MousePosition, Color.LightGreen, width: 3);
            if (targetLimb != null)
            {
                GUI.DrawRectangle(spriteBatch, GetLimbSpritesheetRect(targetLimb), Color.LightGreen, thickness: 3);
            }
        }

        private void DrawJointCreationOnRagdoll(SpriteBatch spriteBatch, Vector2 startPos)
        {
            // Ragdoll
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 200, GameMain.GraphicsHeight - 150), "Left click to select the target limb for the other end of the joint.", Color.White, Color.Black * 0.5f, 10, GUI.Font);
            GUI.DrawLine(spriteBatch, startPos, PlayerInput.MousePosition, Color.LightGreen, width: 3);
            if (targetLimb != null)
            {
                var sourceRect = targetLimb.ActiveSprite.SourceRect;
                Vector2 size = sourceRect.Size.ToVector2() * Cam.Zoom * targetLimb.Scale * targetLimb.TextureScale;
                Vector2 up = VectorExtensions.Backward(targetLimb.Rotation);
                Vector2 left = up.Right();
                Vector2 limbScreenPos = SimToScreen(targetLimb.SimPosition);
                var offset = targetLimb.ActiveSprite.RelativeOrigin.X * left + targetLimb.ActiveSprite.RelativeOrigin.Y * up;
                Vector2 center = limbScreenPos + offset;
                corners = MathUtils.GetImaginaryRect(corners, up, center, size);
                GUI.DrawRectangle(spriteBatch, corners, Color.LightGreen, thickness: 3);
            }
        }

        private void CalculateSpritesheetZoom()
        {
            float width = textures.OrderByDescending(t => t.Width).First().Width;
            float height = textures.Sum(t => t.Height);
            if (textures == null || textures.None())
            {
                spriteSheetMaxZoom = 1;
            }
            else if (height > width)
            {
                spriteSheetMaxZoom = (rightPanel.Rect.Bottom - spriteSheetOffsetY) / height;
            }
            else
            {
                spriteSheetMaxZoom = (rightPanel.Rect.Left - spriteSheetOffsetX) / width;
            }
            spriteSheetZoom = MathHelper.Clamp(1, spriteSheetMinZoom, spriteSheetMaxZoom);
        }
        #endregion

        #region Animation Controls
        private void DrawAnimationControls(SpriteBatch spriteBatch, float deltaTime)
        {
            var collider = character.AnimController.Collider;
            var colliderDrawPos = SimToScreen(collider.SimPosition);
            var animParams = character.AnimController.CurrentAnimationParams;
            var groundedParams = animParams as GroundedMovementParams;
            var humanGroundedParams = animParams as HumanGroundedParams;
            var humanSwimParams = animParams as HumanSwimParams;
            var fishGroundedParams = animParams as FishGroundedParams;
            var fishSwimParams = animParams as FishSwimParams;
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
            //Vector2 centerOfMass = character.AnimController.GetCenterOfMass();
            Vector2 simSpaceForward = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation));
            //Vector2 simSpaceLeft = Vector2.Transform(-Vector2.UnitX, Matrix.CreateRotationZ(collider.Rotation));
            Vector2 screenSpaceForward = VectorExtensions.Backward(collider.Rotation, 1);
            Vector2 screenSpaceLeft = screenSpaceForward.Right();
            // The forward vector is left or right in screen space when the unit is not swimming. Cannot rely on the collider here, because the rotation may vary on ground.
            Vector2 forward = animParams.IsSwimAnimation ? screenSpaceForward : Vector2.UnitX * dir;
            Vector2 GetSimSpaceForward() => animParams.IsSwimAnimation ? Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation)) : Vector2.UnitX * dir;
            Vector2 GetScreenSpaceForward() => animParams.IsSwimAnimation ? VectorExtensions.Backward(collider.Rotation, 1) : Vector2.UnitX * dir;
            bool ShowCycleWidget() => PlayerInput.KeyDown(Keys.LeftAlt) && (CurrentAnimation is IHumanAnimation || CurrentAnimation is GroundedMovementParams);

            bool altDown = PlayerInput.KeyDown(Keys.LeftAlt);
            if (!altDown && (animParams is IHumanAnimation || animParams is GroundedMovementParams))
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 120, GameMain.GraphicsHeight - 150), "HOLD \"Left Alt\" TO ADJUST THE CYCLE SPEED", Color.White, Color.Black * 0.5f, 10, GUI.Font);
            }
            // Widgets for all anims -->
            Vector2 referencePoint = SimToScreen(head != null ? head.SimPosition: collider.SimPosition);
            Vector2 drawPos = referencePoint;
            if (ShowCycleWidget())
            {
                GetAnimationWidget($"{character.SpeciesName}_{CurrentAnimation.AnimationType.ToString()}_CycleSpeed", Color.MediumPurple, size: 20, sizeMultiplier: 1.5f, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    float multiplier = 0.25f;
                    w.tooltip = "Cycle Speed";
                    w.refresh = () =>
                    {
                        referencePoint = SimToScreen(head != null ? head.SimPosition : collider.SimPosition);
                        w.DrawPos = referencePoint + GetScreenSpaceForward() * ConvertUnits.ToDisplayUnits(CurrentAnimation.CycleSpeed * multiplier) * Cam.Zoom;
                        // Update tooltip, because the cycle speed might be automatically adjusted by the movement speed widget.
                        w.tooltip = $"Cycle Speed: {CurrentAnimation.CycleSpeed.FormatSingleDecimal()}";
                    };
                    w.MouseHeld += dTime =>
                    {
                        // TODO: clamp so that cannot manipulate the local y axis -> remove the additional refresh callback in below
                        //Vector2 newPos = PlayerInput.MousePosition;
                        //w.DrawPos = newPos;
                        float speed = CurrentAnimation.CycleSpeed + ConvertUnits.ToSimUnits(Vector2.Multiply(PlayerInput.MouseSpeed / multiplier, GetScreenSpaceForward()).Combine()) / Cam.Zoom;
                        TryUpdateAnimParam("cyclespeed", speed);
                        w.tooltip = $"Cycle Speed: {CurrentAnimation.CycleSpeed.FormatSingleDecimal()}";
                    };
                    // Additional check, which overrides the previous value (because evaluated last)
                    w.PreUpdate += dTime =>
                    {
                        if (!ShowCycleWidget())
                        {
                            w.Enabled = false;
                        }
                    };
                    // Additional (remove if the position is update when the mouse is held)
                    w.PreDraw += (sp, dTime) =>
                    {
                        if (w.IsControlled)
                        {
                            w.refresh();
                        }
                    };
                    w.PostDraw += (sp, dTime) =>
                    {
                        if (w.IsSelected)
                        {
                            GUI.DrawLine(spriteBatch, w.DrawPos, referencePoint, Color.MediumPurple);
                        }
                    };
                }).Draw(spriteBatch, deltaTime);
            }
            else
            {
                GetAnimationWidget($"{character.SpeciesName}_{CurrentAnimation.AnimationType.ToString()}_MovementSpeed", Color.Turquoise, size: 20, sizeMultiplier: 1.5f, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    float multiplier = 0.5f;
                    w.tooltip = "Movement Speed";
                    w.refresh = () =>
                    {
                        referencePoint = SimToScreen(head != null ? head.SimPosition : collider.SimPosition);
                        w.DrawPos = referencePoint + GetScreenSpaceForward() * ConvertUnits.ToDisplayUnits(CurrentAnimation.MovementSpeed * multiplier) * Cam.Zoom;
                    };
                    w.MouseHeld += dTime =>
                    {
                        // TODO: clamp so that cannot manipulate the local y axis -> remove the additional refresh callback in below
                        //Vector2 newPos = PlayerInput.MousePosition;
                        //w.DrawPos = newPos;
                        float speed = CurrentAnimation.MovementSpeed + ConvertUnits.ToSimUnits(Vector2.Multiply(PlayerInput.MouseSpeed / multiplier, GetScreenSpaceForward()).Combine()) / Cam.Zoom;
                        TryUpdateAnimParam("movementspeed", MathHelper.Clamp(speed, 0.1f, Ragdoll.MAX_SPEED));
                        // Sync
                        if (humanSwimParams != null)
                        {
                            TryUpdateAnimParam("cyclespeed", character.AnimController.CurrentAnimationParams.MovementSpeed);
                        }
                        w.tooltip = $"Movement Speed: {CurrentAnimation.MovementSpeed.FormatSingleDecimal()}";
                    };
                    // Additional check, which overrides the previous value (because evaluated last)
                    w.PreUpdate += dTime =>
                    {
                        if (ShowCycleWidget())
                        {
                            w.Enabled = false;
                        }
                    };
                    // Additional (remove if the position is update when the mouse is held)
                    w.PreDraw += (sp, dTime) =>
                    {
                        if (w.IsControlled)
                        {
                            w.refresh();
                        }
                    };
                    w.PostDraw += (sp, dTime) =>
                    {
                        if (w.IsSelected)
                        {
                            GUI.DrawLine(spriteBatch, w.DrawPos, referencePoint, Color.Turquoise);
                        }
                    };
                }).Draw(spriteBatch, deltaTime);
            }

            if (head != null)
            {
                // Head angle
                DrawRadialWidget(spriteBatch, SimToScreen(head.SimPosition), animParams.HeadAngle, "Head Angle", Color.White,
                    angle => TryUpdateAnimParam("headangle", angle), circleRadius: 25, rotationOffset: collider.Rotation + MathHelper.Pi, clockWise: dir < 0);
                // Head position and leaning
                if (animParams.IsGroundedAnimation)
                {
                    if (humanGroundedParams != null)
                    {
                        drawPos = SimToScreen(head.SimPosition.X + humanGroundedParams.HeadLeanAmount * dir, head.PullJointWorldAnchorB.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Head", () =>
                        {
                            var scaledInput = ConvertUnits.ToSimUnits(scaledMouseSpeed) / Cam.Zoom;
                            TryUpdateAnimParam("headleanamount", humanGroundedParams.HeadLeanAmount + scaledInput.X * dir);
                            TryUpdateAnimParam("headposition", humanGroundedParams.HeadPosition - scaledInput.Y * 1.5f / RagdollParams.JointScale);
                            GUI.DrawLine(spriteBatch, drawPos, SimToScreen(head.SimPosition), Color.Red);
                        }, autoFreeze: false);
                        var origin = drawPos + new Vector2(widgetDefaultSize / 2, 0) * dir;
                        GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Red);
                    }
                    else
                    {
                        drawPos = SimToScreen(head.SimPosition.X, head.PullJointWorldAnchorB.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Head Position", () =>
                        {
                            float v = groundedParams.HeadPosition - ConvertUnits.ToSimUnits(scaledMouseSpeed.Y) / Cam.Zoom / RagdollParams.JointScale;
                            TryUpdateAnimParam("headposition", v);
                            GUI.DrawLine(spriteBatch, new Vector2(drawPos.X, 0), new Vector2(drawPos.X, GameMain.GraphicsHeight), Color.Red);
                        }, autoFreeze: false);
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
                DrawRadialWidget(spriteBatch, SimToScreen(referencePoint), animParams.TorsoAngle, "Torso Angle", Color.White,
                    angle => TryUpdateAnimParam("torsoangle", angle), rotationOffset: collider.Rotation + MathHelper.Pi, clockWise: dir < 0);

                if (animParams.IsGroundedAnimation)
                {
                    // Torso position and leaning
                    if (humanGroundedParams != null)
                    {
                        drawPos = SimToScreen(torso.SimPosition.X + humanGroundedParams.TorsoLeanAmount * dir, torso.PullJointWorldAnchorB.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Torso", () =>
                        {
                            var scaledInput = ConvertUnits.ToSimUnits(scaledMouseSpeed) / Cam.Zoom;
                            TryUpdateAnimParam("torsoleanamount", humanGroundedParams.TorsoLeanAmount + scaledInput.X * dir);
                            TryUpdateAnimParam("torsoposition", humanGroundedParams.TorsoPosition - scaledInput.Y * 1.5f / RagdollParams.JointScale);
                            GUI.DrawLine(spriteBatch, drawPos, SimToScreen(torso.SimPosition), Color.Red);
                        }, autoFreeze: false);
                        var origin = drawPos + new Vector2(widgetDefaultSize / 2, 0) * dir;
                        GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.Red);
                    }
                    else
                    {
                        drawPos = SimToScreen(torso.SimPosition.X, torso.PullJointWorldAnchorB.Y);
                        DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.Red, "Torso Position", () =>
                        {
                            float v = groundedParams.TorsoPosition - ConvertUnits.ToSimUnits(scaledMouseSpeed.Y) / Cam.Zoom / RagdollParams.JointScale;
                            TryUpdateAnimParam("torsoposition", v);
                            GUI.DrawLine(spriteBatch, new Vector2(drawPos.X, 0), new Vector2(drawPos.X, GameMain.GraphicsHeight), Color.Red);
                        }, autoFreeze: false);
                    }
                }
            }
            // Foot angle
            if (foot != null)
            {
                if (animParams is IFishAnimation fishParams)
                {
                    foreach (Limb limb in character.AnimController.Limbs)
                    {
                        if (limb.type != LimbType.LeftFoot && limb.type != LimbType.RightFoot) continue;
                        
                        if (!fishParams.FootAnglesInRadians.ContainsKey(limb.limbParams.ID))
                        {
                            fishParams.FootAnglesInRadians[limb.limbParams.ID] = 0.0f;
                        }

                        DrawRadialWidget(spriteBatch, 
                            SimToScreen(new Vector2(limb.SimPosition.X, colliderBottom.Y)), 
                            MathHelper.ToDegrees(fishParams.FootAnglesInRadians[limb.limbParams.ID]), 
                            "Foot Angle", Color.White,
                            angle =>
                            {
                                fishParams.FootAnglesInRadians[limb.limbParams.ID] = MathHelper.ToRadians(angle);
                                TryUpdateAnimParam("footangles", fishParams.FootAngles);
                            },
                            circleRadius: 25, rotationOffset: collider.Rotation, clockWise: dir < 0);
                    }
                }
                else if (animParams is IHumanAnimation humanParams)
                {
                    DrawRadialWidget(spriteBatch, SimToScreen(foot.SimPosition), humanParams.FootAngle, "Foot Angle", Color.White,
                        angle => TryUpdateAnimParam("footangle", angle), circleRadius: 25, rotationOffset: collider.Rotation + MathHelper.Pi, clockWise: dir < 0);
                }
                // Grounded only
                if (groundedParams != null)
                {
                    referencePoint = SimToScreen(colliderBottom);
                    var v = ConvertUnits.ToDisplayUnits(groundedParams.StepSize);
                    drawPos = referencePoint + new Vector2(v.X * dir, -v.Y) * Cam.Zoom;
                    var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                    DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.LightGreen, "Step Size", () =>
                    {
                        var transformedInput = ConvertUnits.ToSimUnits(scaledMouseSpeed) * dir / Cam.Zoom;
                        if (dir > 0)
                        {
                            transformedInput.Y = -transformedInput.Y;
                        }
                        TryUpdateAnimParam("stepsize", groundedParams.StepSize + transformedInput);
                        GUI.DrawLine(spriteBatch, origin, referencePoint, Color.LightGreen);
                    });
                    GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.LightGreen);
                }
            }
            // Human grounded only -->
            if (humanGroundedParams != null)
            {
                if (hand != null || arm != null)
                {
                    var widget = GetAnimationWidget($"{character.SpeciesName}_{character.AnimController.CurrentAnimationParams.AnimationType.ToString()}_HandMoveAmount", Color.LightGreen, initMethod: w =>
                    {
                        w.tooltip = "Hand Move Amount";
                        w.refresh = () =>
                        {
                            referencePoint = SimToScreen(collider.SimPosition + simSpaceForward * 0.2f);
                            var handMovement = ConvertUnits.ToDisplayUnits(humanGroundedParams.HandMoveAmount);
                            w.DrawPos = referencePoint + new Vector2(handMovement.X * dir, handMovement.Y) * Cam.Zoom;
                        };
                        w.MouseHeld += dTime =>
                        {
                            w.DrawPos = PlayerInput.MousePosition;
                            var transformedInput = ConvertUnits.ToSimUnits(new Vector2(PlayerInput.MouseSpeed.X * dir, PlayerInput.MouseSpeed.Y) / Cam.Zoom);
                            TryUpdateAnimParam("handmoveamount", humanGroundedParams.HandMoveAmount + transformedInput);
                            w.tooltip = $"Hand Move Amount: {humanGroundedParams.HandMoveAmount.FormatDoubleDecimal()}";
                        };
                        w.PostDraw += (sp, dTime) =>
                        {
                            if (w.IsSelected)
                            {
                                GUI.DrawLine(sp, w.DrawPos, referencePoint, Color.LightGreen);
                            }
                        };
                    });
                    widget.Draw(spriteBatch, deltaTime);
                }
            }
            // Fish swim only -->
            else if (tail != null && fishSwimParams != null)
            {
                float amplitudeMultiplier = 0.5f;
                float lengthMultiplier = 20;
                float amplitude = ConvertUnits.ToDisplayUnits(fishSwimParams.WaveAmplitude) * Cam.Zoom / amplitudeMultiplier;
                float length = ConvertUnits.ToDisplayUnits(fishSwimParams.WaveLength) * Cam.Zoom / lengthMultiplier;
                referencePoint = colliderDrawPos - screenSpaceForward * ConvertUnits.ToDisplayUnits(collider.radius) * 3 * Cam.Zoom;
                drawPos = referencePoint;
                drawPos -= screenSpaceForward * length;
                Vector2 toRefPoint = referencePoint - drawPos;
                var start = drawPos + toRefPoint / 2;
                var control = start + (screenSpaceLeft * dir * amplitude);
                int points = 1000;
                // Length
                DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 15, Color.NavajoWhite, "Wave Length", () =>
                {
                    var input = Vector2.Multiply(ConvertUnits.ToSimUnits(scaledMouseSpeed), screenSpaceForward).Combine() / Cam.Zoom * lengthMultiplier;
                    TryUpdateAnimParam("wavelength", MathHelper.Clamp(fishSwimParams.WaveLength - input, 0, 150));
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, amplitude, length, 5000, points, Color.NavajoWhite);

                });
                // Amplitude
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.NavajoWhite, "Wave Amplitude", () =>
                {
                    var input = Vector2.Multiply(ConvertUnits.ToSimUnits(scaledMouseSpeed), screenSpaceLeft).Combine() * dir / Cam.Zoom * amplitudeMultiplier;
                    TryUpdateAnimParam("waveamplitude", MathHelper.Clamp(fishSwimParams.WaveAmplitude + input, -4, 4));
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, amplitude, length, 5000, points, Color.NavajoWhite);

                });
            }
            // Human swim only -->
            else if (humanSwimParams != null)
            {
                // Legs
                float amplitudeMultiplier = 5;
                float lengthMultiplier = 5;
                float legMoveAmount = ConvertUnits.ToDisplayUnits(humanSwimParams.LegMoveAmount) * Cam.Zoom / amplitudeMultiplier;
                float legCycleLength = ConvertUnits.ToDisplayUnits(humanSwimParams.LegCycleLength) * Cam.Zoom / lengthMultiplier;
                referencePoint = SimToScreen(character.SimPosition - simSpaceForward / 2);
                drawPos = referencePoint;
                drawPos -= screenSpaceForward * legCycleLength;
                Vector2 toRefPoint = referencePoint - drawPos;
                Vector2 start = drawPos + toRefPoint / 2;
                Vector2 control = start + (screenSpaceLeft * dir * legMoveAmount);
                int points = 1000;
                // Cycle length
                DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 15, Color.NavajoWhite, "Leg Movement Speed", () =>
                {
                    float input = Vector2.Multiply(ConvertUnits.ToSimUnits(scaledMouseSpeed), screenSpaceForward).Combine() / Cam.Zoom * amplitudeMultiplier;
                    TryUpdateAnimParam("legcyclelength", MathHelper.Clamp(humanSwimParams.LegCycleLength - input, 0, 20));
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, legMoveAmount, legCycleLength, 5000, points, Color.NavajoWhite);
                });
                // Movement amount
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.NavajoWhite, "Leg Movement Amount", () =>
                {
                    float input = Vector2.Multiply(ConvertUnits.ToSimUnits(scaledMouseSpeed), screenSpaceLeft).Combine() * dir / Cam.Zoom * lengthMultiplier;
                    TryUpdateAnimParam("legmoveamount", MathHelper.Clamp(humanSwimParams.LegMoveAmount + input, -2, 2));
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, legMoveAmount, legCycleLength, 5000, points, Color.NavajoWhite);
                });
                // Arms
                referencePoint = colliderDrawPos + screenSpaceForward * 10;
                Vector2 handMoveAmount = ConvertUnits.ToDisplayUnits(humanSwimParams.HandMoveAmount) * Cam.Zoom;
                drawPos = referencePoint + new Vector2(handMoveAmount.X * dir, handMoveAmount.Y);
                Vector2 origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.LightGreen, "Hand Move Amount", () =>
                {
                    Vector2 transformedInput = ConvertUnits.ToSimUnits(new Vector2(scaledMouseSpeed.X * dir, scaledMouseSpeed.Y)) / Cam.Zoom;
                    Vector2 handMovement = humanSwimParams.HandMoveAmount + transformedInput;
                    TryUpdateAnimParam("handmoveamount", handMovement);
                    TryUpdateAnimParam("handcyclespeed", handMovement.X * 4);
                    GUI.DrawLine(spriteBatch, origin, referencePoint, Color.LightGreen);
                });
                GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.LightGreen);
            }
        }
        #endregion

        #region Ragdoll
        private Vector2[] corners = new Vector2[4];
        private void DrawLimbEditor(SpriteBatch spriteBatch)
        {
            float inputMultiplier = 0.5f;
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb == null || limb.ActiveSprite == null) { continue; }
                var origin = limb.ActiveSprite.Origin;
                var relativeOrigin = limb.ActiveSprite.RelativeOrigin;
                var sourceRect = limb.ActiveSprite.SourceRect;
                Vector2 size = sourceRect.Size.ToVector2() * Cam.Zoom * limb.Scale * limb.TextureScale;
                Vector2 up = VectorExtensions.Backward(limb.Rotation);
                Vector2 left = up.Right();
                Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
                Vector2 offset = relativeOrigin.X * left + relativeOrigin.Y * up;
                Vector2 center = limbScreenPos + offset;
                corners = MathUtils.GetImaginaryRect(corners, up, center, size);
                if (selectedLimbs.Contains(limb))
                {
                    GUI.DrawRectangle(spriteBatch, corners, Color.Yellow, thickness: 3);
                }
                else
                {

                    GUI.DrawRectangle(spriteBatch, corners, Color.Red);
                }
                // Draw origins
                if (selectedLimbs.Contains(limb))
                {
                    GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.White, width: 3);
                    GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.White, width: 3);
                    GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.Yellow);
                    GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.Yellow);
                }
                if (MathUtils.RectangleContainsPoint(corners, PlayerInput.MousePosition) && GUI.MouseOn == null)
                {
                    // Select limbs
                    if (PlayerInput.LeftButtonDown())
                    {
                        if (!selectedLimbs.Contains(limb))
                        {
                            if (!Widget.EnableMultiSelect)
                            {
                                selectedLimbs.Clear();
                            }
                            selectedLimbs.Add(limb);
                            ResetParamsEditor();
                        }
                        else if (Widget.EnableMultiSelect)
                        {
                            selectedLimbs.Remove(limb);
                            ResetParamsEditor();
                        }
                    }
                    // Origin
                    if (!lockSpriteOrigin && PlayerInput.LeftButtonHeld() && selectedLimbs.Contains(limb))
                    {
                        Vector2 forward = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(limb.Rotation));
                        var input = -scaledMouseSpeed * inputMultiplier / Cam.Zoom / limb.Scale / limb.TextureScale;
                        var sprite = limb.ActiveSprite;
                        origin += input.TransformVector(forward);
                        var max = new Vector2(sourceRect.Width, sourceRect.Height);
                        sprite.Origin = origin.Clamp(Vector2.Zero, max);
                        if (limb.DamagedSprite != null)
                        {
                            limb.DamagedSprite.Origin = sprite.Origin;
                        }
                        if (character.AnimController.IsFlipped)
                        {
                            origin.X = Math.Abs(origin.X - sourceRect.Width);
                        }
                        TryUpdateLimbParam(limb, "origin", limb.ActiveSprite.RelativeOrigin);
                        if (limbPairEditing)
                        {
                            UpdateOtherLimbs(limb, otherLimb =>
                            {
                                otherLimb.ActiveSprite.Origin = sprite.Origin;
                                if (otherLimb.DamagedSprite != null)
                                {
                                    otherLimb.DamagedSprite.Origin = sprite.Origin;
                                }
                                TryUpdateLimbParam(otherLimb, "origin", otherLimb.ActiveSprite.RelativeOrigin);
                            });
                        }
                        GUI.DrawString(spriteBatch, limbScreenPos + new Vector2(10, -10), limb.ActiveSprite.RelativeOrigin.FormatDoubleDecimal(), Color.Yellow, Color.Black * 0.5f);
                    }
                    else
                    {
                        GUI.DrawString(spriteBatch, limbScreenPos + new Vector2(10, -10), limb.Name, Color.White, Color.Black * 0.5f);
                    }
                }
            }
        }

        private void DrawRagdoll(SpriteBatch spriteBatch, float deltaTime)
        {
            bool altDown = PlayerInput.KeyDown(Keys.LeftAlt);
            if (!altDown && editJoints && selectedJoints.Any())
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 200, GameMain.GraphicsHeight - 150), "HOLD \"Left Alt\" TO MANIPULATE THE OTHER END OF THE JOINT", Color.White, Color.Black * 0.5f, 10, GUI.Font);
            }
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (editIK)
                {
                    if (limb.type == LimbType.LeftFoot || limb.type == LimbType.RightFoot || limb.type == LimbType.LeftHand || limb.type == LimbType.RightHand)
                    {
                        var pullJointWidgetSize = new Vector2(5, 5);
                        Vector2 tformedPullPos = SimToScreen(limb.PullJointWorldAnchorA);
                        GUI.DrawRectangle(spriteBatch, tformedPullPos - pullJointWidgetSize / 2, pullJointWidgetSize, Color.Red, true);
                        DrawWidget(spriteBatch, tformedPullPos, WidgetType.Rectangle, 8, Color.Cyan, $"IK ({limb.Name})", () =>
                        {
                            if (!selectedLimbs.Contains(limb))
                            {
                                selectedLimbs.Add(limb);
                                ResetParamsEditor();
                            }
                            limb.PullJointWorldAnchorA = ScreenToSim(PlayerInput.MousePosition);
                            TryUpdateLimbParam(limb, "pullpos", ConvertUnits.ToDisplayUnits(limb.PullJointLocalAnchorA / limb.limbParams.Ragdoll.LimbScale));
                            GUI.DrawLine(spriteBatch, SimToScreen(limb.SimPosition), tformedPullPos, Color.MediumPurple);
                        });
                    }
                }
                foreach (var joint in character.AnimController.LimbJoints)
                {
                    Vector2 jointPos = Vector2.Zero;
                    Vector2 otherPos = Vector2.Zero;
                    Vector2 anchorPosA = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA);
                    Vector2 anchorPosB = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB);
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
                    Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
                    var f = Vector2.Transform(jointPos, Matrix.CreateRotationZ(limb.Rotation));
                    f.Y = -f.Y;
                    Vector2 tformedJointPos = limbScreenPos + f * Cam.Zoom;
                    if (showRagdoll)
                    {
                        ShapeExtensions.DrawPoint(spriteBatch, limbScreenPos, Color.Black, size: 5);
                        ShapeExtensions.DrawPoint(spriteBatch, limbScreenPos, Color.White, size: 1);
                        GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.Black, width: 3);
                        GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.White, width: 1);
                    }
                    if (editJoints)
                    {
                        if (altDown && joint.BodyA == limb.body.FarseerBody)
                        {
                            continue;
                        }
                        if (!altDown && joint.BodyB == limb.body.FarseerBody)
                        {
                            continue;
                        }
                        var selectionWidget = GetJointSelectionWidget($"{joint.jointParams.Name} selection widget ragdoll", joint);
                        selectionWidget.DrawPos = tformedJointPos;
                        selectionWidget.Draw(spriteBatch, deltaTime);
                        if (selectedJoints.Contains(joint))
                        {
                            if (joint.LimitEnabled)
                            {
                                DrawJointLimitWidgets(spriteBatch, limb, joint, tformedJointPos, autoFreeze: true, allowPairEditing: true, rotationOffset: limb.Rotation);
                            }
                            Vector2 to = tformedJointPos + VectorExtensions.Forward(joint.LimbB.Rotation + MathHelper.ToRadians(-spriteSheetOrientation), 20);
                            GUI.DrawLine(spriteBatch, tformedJointPos, to, Color.Magenta, width: 2);
                            var dotSize = new Vector2(5, 5);
                            var rect = new Rectangle((tformedJointPos - dotSize / 2).ToPoint(), dotSize.ToPoint());
                            //GUI.DrawRectangle(spriteBatch, tformedJointPos - dotSize / 2, dotSize, color, true);
                            //GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + up * 20, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.Yellow, width: 3);
                            //GUI.DrawRectangle(spriteBatch, inputRect, Color.Red);
                            GUI.DrawString(spriteBatch, tformedJointPos + new Vector2(dotSize.X, -dotSize.Y) * 2, $"{joint.jointParams.Name} {jointPos.FormatZeroDecimal()}", Color.White, Color.Black * 0.5f);
                            if (PlayerInput.LeftButtonHeld())
                            {
                                if (!selectionWidget.IsControlled) { continue; }
                                if (autoFreeze)
                                {
                                    isFreezed = true;
                                }
                                Vector2 input = ConvertUnits.ToSimUnits(scaledMouseSpeed) / Cam.Zoom;
                                input.Y = -input.Y;
                                input = input.TransformVector(VectorExtensions.Forward(limb.Rotation));
                                if (joint.BodyA == limb.body.FarseerBody)
                                {
                                    joint.LocalAnchorA += input;
                                    TryUpdateJointParam(joint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA));
                                    // Snap all selected joints to the first selected
                                    if (copyJointSettings)
                                    {
                                        foreach (var j in selectedJoints)
                                        {
                                            j.LocalAnchorA = joint.LocalAnchorA;
                                            TryUpdateJointParam(j, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA));
                                        }
                                    }
                                }
                                else if (joint.BodyB == limb.body.FarseerBody)
                                {
                                    joint.LocalAnchorB += input;
                                    TryUpdateJointParam(joint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
                                    // Snap all selected joints to the first selected
                                    if (copyJointSettings)
                                    {
                                        foreach (var j in selectedJoints)
                                        {
                                            j.LocalAnchorA = joint.LocalAnchorA;
                                            TryUpdateJointParam(j, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
                                        }
                                    }
                                }
                                // Edit the other joints
                                if (limbPairEditing)
                                {
                                    UpdateOtherJoints(limb, (otherLimb, otherJoint) =>
                                    {
                                        if (joint.BodyA == limb.body.FarseerBody && otherJoint.BodyA == otherLimb.body.FarseerBody)
                                        {
                                            otherJoint.LocalAnchorA = joint.LocalAnchorA;
                                            TryUpdateJointParam(otherJoint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA));
                                        }
                                        else if (joint.BodyB == limb.body.FarseerBody && otherJoint.BodyB == otherLimb.body.FarseerBody)
                                        {
                                            otherJoint.LocalAnchorB = joint.LocalAnchorB;
                                            TryUpdateJointParam(otherJoint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
                                        }
                                    });
                                }
                            }
                            else
                            {
                                isFreezed = freezeToggle.Selected;
                            }
                        }
                    }
                }
            }
        }

        private void UpdateOtherLimbs(Limb limb, Action<Limb> updateAction)
        {
            // Edit the other limbs
            if (limbPairEditing)
            {
                string limbType = limb.type.ToString();
                bool isLeft = limbType.Contains("Left");
                bool isRight = limbType.Contains("Right");
                if (isLeft || isRight)
                {
                    if (character.AnimController.HasMultipleLimbsOfSameType)
                    {
                        GetOtherLimbs(limb)?.ForEach(l => UpdateOtherLimbs(l));
                    }
                    else
                    {
                        Limb otherLimb = GetOtherLimb(limbType, isLeft);
                        if (otherLimb != null)
                        {
                            UpdateOtherLimbs(otherLimb);
                        }
                    }
                    void UpdateOtherLimbs(Limb otherLimb)
                    {
                        updateAction(otherLimb);
                    }
                }
            }
        }

        private void UpdateOtherJoints(Limb limb, Action<Limb, LimbJoint> updateAction)
        {
            // Edit the other joints
            if (limbPairEditing)
            {
                string limbType = limb.type.ToString();
                bool isLeft = limbType.Contains("Left");
                bool isRight = limbType.Contains("Right");
                if (isLeft || isRight)
                {
                    if (character.AnimController.HasMultipleLimbsOfSameType)
                    {
                        GetOtherLimbs(limb)?.ForEach(l => UpdateOtherJoints(l));
                    }
                    else
                    {
                        Limb otherLimb = GetOtherLimb(limbType, isLeft);
                        if (otherLimb != null)
                        {
                            UpdateOtherJoints(otherLimb);
                        }
                    }
                    void UpdateOtherJoints(Limb otherLimb)
                    {
                        foreach (var otherJoint in character.AnimController.LimbJoints)
                        {
                            updateAction(otherLimb, otherJoint);
                        }
                    }
                }
            }
        }

        private Limb GetOtherLimb(string limbType, bool isLeft)
        {
            string otherLimbType = isLeft ? limbType.Replace("Left", "Right") : limbType.Replace("Right", "Left");
            if (Enum.TryParse(otherLimbType, out LimbType type))
            {
                return character.AnimController.GetLimb(type);
            }
            return null;
        }

        // TODO: optimize?, this method creates carbage (not much, but it's used frequently)
        private IEnumerable<Limb> GetOtherLimbs(Limb limb)
        {
            var otherLimbs = character.AnimController.Limbs.Where(l => l.type == limb.type && l != limb);
            string limbType = limb.type.ToString();
            string otherLimbType = limbType.Contains("Left") ? limbType.Replace("Left", "Right") : limbType.Replace("Right", "Left");
            if (Enum.TryParse(otherLimbType, out LimbType type))
            {
                otherLimbs = otherLimbs.Union(character.AnimController.Limbs.Where(l => l.type == type));
            }
            return otherLimbs;
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
                    CreateTextures();
                }
                return textures;
            }
        }
        private List<string> texturePaths;
        private void CreateTextures()
        {
            textures = new List<Texture2D>();
            texturePaths = new List<string>();
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb.ActiveSprite == null || texturePaths.Contains(limb.ActiveSprite.FilePath)) { continue; }
                if (limb.ActiveSprite.Texture == null) { continue; }
                textures.Add(limb.ActiveSprite.Texture);
                texturePaths.Add(limb.ActiveSprite.FilePath);
            }
        }

        private void DrawSpritesheetEditor(SpriteBatch spriteBatch, float deltaTime)
        {
            int offsetX = spriteSheetOffsetX;
            int offsetY = spriteSheetOffsetY;
            for (int i = 0; i < Textures.Count; i++)
            {
                var texture = Textures[i];
                spriteBatch.Draw(texture, 
                    position: new Vector2(offsetX, offsetY), 
                    rotation: 0, 
                    origin: Vector2.Zero,
                    sourceRectangle: null,
                    scale: spriteSheetZoom,
                    effects: SpriteEffects.None,
                    color: Color.White,
                    layerDepth: 0);
                GUI.DrawRectangle(spriteBatch, new Vector2(offsetX, offsetY), texture.Bounds.Size.ToVector2() * spriteSheetZoom, Color.White);
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (limb.ActiveSprite == null || limb.ActiveSprite.FilePath != texturePaths[i]) continue;
                    Rectangle rect = limb.ActiveSprite.SourceRect;
                    rect.Size = rect.MultiplySize(spriteSheetZoom);
                    rect.Location = rect.Location.Multiply(spriteSheetZoom);
                    rect.X += offsetX;
                    rect.Y += offsetY;
                    GUI.DrawRectangle(spriteBatch, rect, selectedLimbs.Contains(limb) ? Color.Yellow : Color.Red);
                    Vector2 origin = limb.ActiveSprite.Origin;
                    Vector2 limbScreenPos = new Vector2(rect.X + origin.X * spriteSheetZoom, rect.Y + origin.Y * spriteSheetZoom);
                    // The origin is manipulated when the character is flipped. We have to undo it here.
                    if (character.AnimController.Dir < 0)
                    {
                        limbScreenPos.X = rect.X + rect.Width - (float)Math.Round(origin.X * spriteSheetZoom);
                    }
                    if (editJoints)
                    {
                        DrawSpritesheetJointEditor(spriteBatch, deltaTime, limb, limbScreenPos);
                    }
                    if (editLimbs)
                    {
                        // Draw the source rect widgets
                        int widgetSize = 8;
                        int halfSize = widgetSize / 2;
                        Vector2 stringOffset = new Vector2(5, 14);
                        var topLeft = rect.Location.ToVector2();
                        var topRight = new Vector2(topLeft.X + rect.Width, topLeft.Y);
                        var bottomRight = new Vector2(topRight.X, topRight.Y + rect.Height);
                        //var bottomLeft = new Vector2(topLeft.X, bottomRight.Y);
                        // Select limbs
                        bool isMouseOnRect = rect.Contains(PlayerInput.MousePosition);
                        if (isMouseOnRect && GUI.MouseOn == null)
                        {
                            if (PlayerInput.LeftButtonDown() && selectedWidget == null)
                            {
                                if (!selectedLimbs.Contains(limb))
                                {
                                    if (!Widget.EnableMultiSelect)
                                    {
                                        selectedLimbs.Clear();
                                    }
                                    selectedLimbs.Add(limb);
                                }
                                else if (Widget.EnableMultiSelect)
                                {
                                    selectedLimbs.Remove(limb);
                                }
                            }
                            if (PlayerInput.LeftButtonClicked())
                            {
                                ResetParamsEditor();
                            }
                        }
                        if (selectedLimbs.Contains(limb))
                        {
                            // Draw the sprite origins
                            GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.Yellow);
                            GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.Yellow);

                            if (!lockSpriteOrigin && PlayerInput.LeftButtonHeld() && isMouseOnRect && selectedWidget == null && GUI.MouseOn == null)
                            {
                                var input = scaledMouseSpeed / spriteSheetZoom;
                                input.X *= character.AnimController.Dir;
                                // Adjust the sprite origin
                                origin += input;
                                var sprite = limb.ActiveSprite;
                                var sourceRect = sprite.SourceRect;
                                var max = new Vector2(sourceRect.Width, sourceRect.Height);
                                sprite.Origin = origin.Clamp(Vector2.Zero, max);
                                if (limb.DamagedSprite != null)
                                {
                                    limb.DamagedSprite.Origin = limb.ActiveSprite.Origin;
                                }
                                if (character.AnimController.IsFlipped)
                                {
                                    origin.X = Math.Abs(origin.X - sourceRect.Width);
                                }
                                var relativeOrigin = new Vector2(origin.X / sourceRect.Width, origin.Y / sourceRect.Height);
                                TryUpdateLimbParam(limb, "origin", relativeOrigin);
                                GUI.DrawString(spriteBatch, limbScreenPos + new Vector2(10, -10), relativeOrigin.FormatDoubleDecimal(), Color.Yellow, Color.Black * 0.5f);
                                if (limbPairEditing)
                                {
                                    UpdateOtherLimbs(limb, otherLimb =>
                                    {
                                        otherLimb.ActiveSprite.Origin = sprite.Origin;
                                        if (otherLimb.DamagedSprite != null)
                                        {
                                            otherLimb.DamagedSprite.Origin = sprite.Origin;
                                        }
                                        TryUpdateLimbParam(otherLimb, "origin", relativeOrigin);
                                    });
                                }
                            }
                            if (!lockSpritePosition)
                            {
                                DrawWidget(spriteBatch, topLeft - new Vector2(halfSize), WidgetType.Rectangle, widgetSize, Color.Yellow, "Position", () =>
                                {
                                    // Adjust the source rect location
                                    var newRect = limb.ActiveSprite.SourceRect;
                                    newRect.Location = new Point(
                                        (int)((PlayerInput.MousePosition.X + halfSize - offsetX) / spriteSheetZoom),
                                        (int)((PlayerInput.MousePosition.Y + halfSize - offsetY) / spriteSheetZoom));
                                    limb.ActiveSprite.SourceRect = newRect;
                                    if (limb.DamagedSprite != null)
                                    {
                                        limb.DamagedSprite.SourceRect = limb.ActiveSprite.SourceRect;
                                    }
                                    TryUpdateLimbParam(limb, "sourcerect", newRect);
                                    GUI.DrawString(spriteBatch, topLeft + new Vector2(stringOffset.X, -stringOffset.Y * 1.5f), limb.ActiveSprite.SourceRect.Location.ToString(), Color.Yellow, Color.Black * 0.5f);
                                    if (limbPairEditing)
                                    {
                                        UpdateOtherLimbs(limb, otherLimb =>
                                        {
                                            otherLimb.ActiveSprite.SourceRect = newRect;
                                            if (otherLimb.DamagedSprite != null)
                                            {
                                                otherLimb.DamagedSprite.SourceRect = newRect;
                                            }
                                            TryUpdateLimbParam(otherLimb, "sourcerect", newRect);
                                        });
                                    }
                                }, autoFreeze: false);
                            }
                            if (!lockSpriteSize)
                            {
                                DrawWidget(spriteBatch, bottomRight + new Vector2(halfSize), WidgetType.Rectangle, widgetSize, Color.Yellow, "Size", () =>
                                {
                                    // Adjust the source rect width and height, and the sprite size.
                                    var newRect = limb.ActiveSprite.SourceRect;
                                    int width = (int)((PlayerInput.MousePosition.X - rect.X) / spriteSheetZoom);
                                    int height = (int)((PlayerInput.MousePosition.Y - rect.Y) / spriteSheetZoom);
                                    int dx = newRect.Width - width;
                                    newRect.Width = width;
                                    newRect.Height = height;
                                    limb.ActiveSprite.SourceRect = newRect;
                                    limb.ActiveSprite.size = new Vector2(width, height);
                                    // Refresh the relative origin, so that the origin in pixels will be recalculated
                                    limb.ActiveSprite.RelativeOrigin = limb.ActiveSprite.RelativeOrigin;
                                    if (limb.DamagedSprite != null)
                                    {
                                        limb.DamagedSprite.RelativeOrigin = limb.DamagedSprite.RelativeOrigin;
                                    }
                                    TryUpdateLimbParam(limb, "sourcerect", newRect);
                                    GUI.DrawString(spriteBatch, bottomRight + stringOffset, limb.ActiveSprite.size.FormatZeroDecimal(), Color.Yellow, Color.Black * 0.5f);
                                    if (limbPairEditing)
                                    {
                                        UpdateOtherLimbs(limb, otherLimb =>
                                        {
                                            otherLimb.ActiveSprite.SourceRect = newRect;
                                            // Refresh the relative origin, so that the origin in pixels will be recalculated
                                            otherLimb.ActiveSprite.RelativeOrigin = limb.ActiveSprite.RelativeOrigin;
                                            if (otherLimb.DamagedSprite != null)
                                            {
                                                otherLimb.DamagedSprite.RelativeOrigin = limb.DamagedSprite.RelativeOrigin;
                                            }
                                            TryUpdateLimbParam(otherLimb, "sourcerect", newRect);
                                        });
                                    }
                                }, autoFreeze: false);
                            }
                        }
                        else if (isMouseOnRect)
                        {
                            GUI.DrawString(spriteBatch, limbScreenPos + new Vector2(10, -10), limb.Name, Color.White, Color.Black * 0.5f);
                        }
                    }
                }
                offsetY += (int)(texture.Height * spriteSheetZoom);
            }
        }

        private void DrawSpritesheetJointEditor(SpriteBatch spriteBatch, float deltaTime, Limb limb, Vector2 limbScreenPos, float spriteRotation = 0)
        {
            foreach (var joint in character.AnimController.LimbJoints)
            {
                Vector2 jointPos = Vector2.Zero;
                Vector2 anchorPosA = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA);
                Vector2 anchorPosB = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB);
                string anchorID;
                string otherID;
                if (joint.BodyA == limb.body.FarseerBody)
                {
                    jointPos = anchorPosA;
                    anchorID = "1";
                    otherID = "2";
                }
                else if (joint.BodyB == limb.body.FarseerBody)
                {
                    jointPos = anchorPosB;
                    anchorID = "2";
                    otherID = "1";
                }
                else
                {
                    continue;
                }
                Vector2 tformedJointPos = jointPos = jointPos / RagdollParams.JointScale / limb.TextureScale * spriteSheetZoom;
                tformedJointPos.Y = -tformedJointPos.Y;
                tformedJointPos.X *= character.AnimController.Dir;
                tformedJointPos += limbScreenPos;
                var jointSelectionWidget = GetJointSelectionWidget($"{joint.jointParams.Name} selection widget {anchorID}", joint, $"{joint.jointParams.Name} selection widget {otherID}");
                jointSelectionWidget.DrawPos = tformedJointPos;
                jointSelectionWidget.Draw(spriteBatch, deltaTime);
                var otherWidget = GetJointSelectionWidget($"{joint.jointParams.Name} selection widget {otherID}", joint, $"{joint.jointParams.Name} selection widget {anchorID}");
                if (anchorID == "2")
                {
                    bool isSelected = selectedJoints.Contains(joint);
                    bool isHovered = jointSelectionWidget.IsSelected || otherWidget.IsSelected;
                    if (isSelected || isHovered)
                    {
                        GUI.DrawLine(spriteBatch, jointSelectionWidget.DrawPos, otherWidget.DrawPos, jointSelectionWidget.color, width: 2);
                    }
                }
                if (selectedJoints.Contains(joint))
                {
                    if (joint.LimitEnabled)
                    {
                        DrawJointLimitWidgets(spriteBatch, limb, joint, tformedJointPos, autoFreeze: false, allowPairEditing: true);
                    }
                    if (jointSelectionWidget.IsControlled)
                    {
                        Vector2 input = ConvertUnits.ToSimUnits(scaledMouseSpeed);
                        input.Y = -input.Y;
                        input.X *= character.AnimController.Dir;
                        input *= RagdollParams.JointScale * limb.TextureScale / spriteSheetZoom;
                        if (joint.BodyA == limb.body.FarseerBody)
                        {
                            joint.LocalAnchorA += input;
                            TryUpdateJointParam(joint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA));
                            // Snap all selected joints to the first selected
                            if (copyJointSettings)
                            {
                                foreach (var j in selectedJoints)
                                {
                                    j.LocalAnchorA = joint.LocalAnchorA;
                                    TryUpdateJointParam(j, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA));
                                }
                            }
                        }
                        else if (joint.BodyB == limb.body.FarseerBody)
                        {
                            joint.LocalAnchorB += input;
                            TryUpdateJointParam(joint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
                            // Snap all selected joints to the first selected
                            if (copyJointSettings)
                            {
                                foreach (var j in selectedJoints)
                                {
                                    j.LocalAnchorB = joint.LocalAnchorB;
                                    TryUpdateJointParam(j, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
                                }
                            }
                        }
                        if (limbPairEditing)
                        {
                            UpdateOtherJoints(limb, (otherLimb, otherJoint) =>
                            {
                                if (joint.BodyA == limb.body.FarseerBody && otherJoint.BodyA == otherLimb.body.FarseerBody)
                                {
                                    otherJoint.LocalAnchorA = joint.LocalAnchorA;
                                    TryUpdateJointParam(otherJoint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA));
                                }
                                else if (joint.BodyB == limb.body.FarseerBody && otherJoint.BodyB == otherLimb.body.FarseerBody)
                                {
                                    otherJoint.LocalAnchorB = joint.LocalAnchorB;
                                    TryUpdateJointParam(otherJoint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
                                }
                            });
                        }
                    }
                }
            }
        }

        private void DrawJointLimitWidgets(SpriteBatch spriteBatch, Limb limb, LimbJoint joint, Vector2 drawPos, bool autoFreeze, bool allowPairEditing, float rotationOffset = 0)
        {
            rotationOffset -= MathHelper.ToRadians(spriteSheetOrientation);
            Color angleColor = joint.UpperLimit - joint.LowerLimit > 0 ? Color.LightGreen * 0.5f : Color.Red;
            DrawRadialWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.UpperLimit), $"{joint.jointParams.Name} Upper Limit", Color.Cyan, angle =>
            {
                joint.UpperLimit = MathHelper.ToRadians(angle);
                ValidateJoint(joint);
                TryUpdateJointParam(joint, "upperlimit", angle);
                if (copyJointSettings)
                {
                    foreach (var j in selectedJoints)
                    {
                        if (j.LimitEnabled != joint.LimitEnabled)
                        {
                            j.LimitEnabled = joint.LimitEnabled;
                            TryUpdateJointParam(j, "limitenabled", j.LimitEnabled);
                        }
                        j.UpperLimit = joint.UpperLimit;
                        TryUpdateJointParam(j, "upperlimit", angle);
                    }
                }
                if (allowPairEditing && limbPairEditing)
                {
                    UpdateOtherJoints(limb, (otherLimb, otherJoint) =>
                    {
                        if (IsMatchingLimb(limb, otherLimb, joint, otherJoint))
                        {
                            if (otherJoint.LimitEnabled != joint.LimitEnabled)
                            {
                                otherJoint.LimitEnabled = otherJoint.LimitEnabled;
                                TryUpdateJointParam(otherJoint, "limitenabled", otherJoint.LimitEnabled);
                            }
                            otherJoint.UpperLimit = joint.UpperLimit;
                            TryUpdateJointParam(otherJoint, "upperlimit", angle);
                        }
                    });
                }
                DrawAngle(20, angleColor, 4);
                DrawAngle(40, Color.Cyan);
                GUI.DrawString(spriteBatch, drawPos, angle.FormatZeroDecimal(), Color.Black, backgroundColor: Color.Cyan, font: GUI.SmallFont);
            }, circleRadius: 40, rotationOffset: rotationOffset, displayAngle: false, clockWise: false);
            DrawRadialWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.LowerLimit), $"{joint.jointParams.Name} Lower Limit", Color.Yellow, angle =>
            {
                joint.LowerLimit = MathHelper.ToRadians(angle);
                ValidateJoint(joint);
                TryUpdateJointParam(joint, "lowerlimit", angle);
                if (copyJointSettings)
                {
                    foreach (var j in selectedJoints)
                    {
                        if (j.LimitEnabled != joint.LimitEnabled)
                        {
                            j.LimitEnabled = joint.LimitEnabled;
                            TryUpdateJointParam(j, "limitenabled", j.LimitEnabled);
                        }
                        j.LowerLimit = joint.LowerLimit;
                        TryUpdateJointParam(j, "lowerlimit", angle);
                    }
                }
                if (allowPairEditing && limbPairEditing)
                {
                    UpdateOtherJoints(limb, (otherLimb, otherJoint) =>
                    {
                        if (IsMatchingLimb(limb, otherLimb, joint, otherJoint))
                        {
                            if (otherJoint.LimitEnabled != joint.LimitEnabled)
                            {
                                otherJoint.LimitEnabled = otherJoint.LimitEnabled;
                                TryUpdateJointParam(otherJoint, "limitenabled", otherJoint.LimitEnabled);
                            }
                            otherJoint.LowerLimit = joint.LowerLimit;
                            TryUpdateJointParam(otherJoint, "lowerlimit", angle);
                        }
                    });
                }
                DrawAngle(20, angleColor, 4);
                DrawAngle(25, Color.Yellow);
                GUI.DrawString(spriteBatch, drawPos, angle.FormatZeroDecimal(), Color.Black, backgroundColor: Color.Yellow, font: GUI.SmallFont);
            }, circleRadius: 25, rotationOffset: rotationOffset, displayAngle: false, clockWise: false);
            void DrawAngle(float radius, Color color, float thickness = 5)
            {
                float angle = joint.UpperLimit - joint.LowerLimit;
                ShapeExtensions.DrawSector(spriteBatch, drawPos, radius, angle, 40, color, 
                    offset: -rotationOffset - joint.UpperLimit + MathHelper.PiOver2, thickness: thickness);
            }
        }

        private bool IsMatchingLimb(Limb limb1, Limb limb2, LimbJoint joint1, LimbJoint joint2) =>
            joint1.BodyA == limb1.body.FarseerBody && joint2.BodyA == limb2.body.FarseerBody ||
            joint1.BodyB == limb1.body.FarseerBody && joint2.BodyB == limb2.body.FarseerBody;
        #endregion

        #region Widgets as methods
        private void DrawRadialWidget(SpriteBatch spriteBatch, Vector2 drawPos, float value, string toolTip, Color color, Action<float> onClick,
            float circleRadius = 30, int widgetSize = 10, float rotationOffset = 0, bool clockWise = true, bool displayAngle = true, bool? autoFreeze = null)
        {
            var angle = value;
            if (!MathUtils.IsValid(angle))
            {
                angle = 0;
            }
            float drawAngle = clockWise ? -angle : angle;
            var widgetDrawPos = drawPos + VectorExtensions.Forward(MathHelper.ToRadians(drawAngle) + rotationOffset, circleRadius);
            GUI.DrawLine(spriteBatch, drawPos, widgetDrawPos, color);
            DrawWidget(spriteBatch, widgetDrawPos, WidgetType.Rectangle, 10, color, toolTip, () =>
            {
                GUI.DrawLine(spriteBatch, drawPos, widgetDrawPos, color, width: 3);
                ShapeExtensions.DrawCircle(spriteBatch, drawPos, circleRadius, 40, color, thickness: 1);
                Vector2 d = PlayerInput.MousePosition - drawPos;
                float newAngle = MathUtils.VectorToAngle(d) - MathHelper.PiOver2 + rotationOffset;
                angle = MathHelper.ToDegrees(newAngle);
                if (!clockWise)
                {
                    angle = -angle;
                }
                if (displayAngle)
                {
                    GUI.DrawString(spriteBatch, drawPos, angle.FormatZeroDecimal(), Color.Black, backgroundColor: color, font: GUI.SmallFont);
                }
                onClick(angle);
                var zeroPos = drawPos + VectorExtensions.Forward(rotationOffset, circleRadius);
                GUI.DrawLine(spriteBatch, drawPos, zeroPos, Color.Red, width: 3);
            }, autoFreeze, onHovered: () =>
            {
                if (!PlayerInput.LeftButtonHeld())
                {
                    GUI.DrawString(spriteBatch, new Vector2(drawPos.X + 5, drawPos.Y - widgetSize / 2),
                        $"{toolTip} ({angle.FormatZeroDecimal()})", color, Color.Black * 0.5f);
                }    
            });
        }

        public enum WidgetType { Rectangle, Circle }
        private string selectedWidget;
        private void DrawWidget(SpriteBatch spriteBatch, Vector2 drawPos, WidgetType widgetType, int size, Color color, string name, Action onPressed, bool ? autoFreeze = null, Action onHovered = null)
        {
            var drawRect = new Rectangle((int)drawPos.X - size / 2, (int)drawPos.Y - size / 2, size, size);
            var inputRect = drawRect;
            inputRect.Inflate(size * 0.75f, size * 0.75f);
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
                    GUI.DrawRectangle(spriteBatch, drawRect, color, thickness: isSelected ? 3 : 1, isFilled: isSelected && PlayerInput.LeftButtonHeld());
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
                if (onHovered == null)
                {
                    GUI.DrawString(spriteBatch, new Vector2(drawRect.Right + 5, drawRect.Y - drawRect.Height / 2), name, color, Color.Black);
                }
                else
                {
                    onHovered();
                }
                if (PlayerInput.LeftButtonHeld())
                {
                    if (autoFreeze ?? this.autoFreeze)
                    {
                        isFreezed = true;
                    }
                    onPressed();
                }
                else
                {
                    isFreezed = freezeToggle.Selected;
                }
            }
        }
        #endregion

        #region Widgets as classes
        private Dictionary<string, Widget> animationWidgets = new Dictionary<string, Widget>();

        private Widget GetAnimationWidget(string id, Color color, int size = 10, float sizeMultiplier = 2, Widget.Shape shape = Widget.Shape.Rectangle, Action<Widget> initMethod = null)
        {
            if (!animationWidgets.TryGetValue(id, out Widget widget))
            {
                int selectedSize = (int)Math.Round(size * sizeMultiplier);
                widget = new Widget(id, size, shape)
                {
                    tooltipOffset = new Vector2(selectedSize / 2 + 5, -10)
                };
                widget.color = color;
                widget.PreUpdate += dTime => widget.Enabled = editAnimations;
                widget.PostUpdate += dTime =>
                {
                    if (widget.IsSelected)
                    {
                        widget.size = selectedSize;
                        widget.inputAreaMargin = 20;
                    }
                    else
                    {
                        widget.size = size;
                        widget.inputAreaMargin = 5;
                    }
                    widget.isFilled = widget.IsControlled;
                };
                widget.PreDraw += (sp, dTime) =>
                {
                    if (!widget.IsControlled)
                    {
                        widget.refresh();
                    }
                };
                animationWidgets.Add(id, widget);
                initMethod?.Invoke(widget);
            }
            return widget;
        }

        private Dictionary<string, Widget> jointSelectionWidgets = new Dictionary<string, Widget>();

        private Widget GetJointSelectionWidget(string id, LimbJoint joint, string linkedId = null)
        {
            // Handle widget linking and create the widgets
            if (!jointSelectionWidgets.TryGetValue(id, out Widget jointWidget))
            {
                jointWidget = CreateJointSelectionWidget(id, joint);
                if (linkedId != null)
                {
                    if (!jointSelectionWidgets.TryGetValue(linkedId, out Widget linkedWidget))
                    {
                        linkedWidget = CreateJointSelectionWidget(linkedId, joint);
                    }
                    jointWidget.linkedWidget = linkedWidget;
                    linkedWidget.linkedWidget = jointWidget;
                }
            }
            return jointWidget;

            // Widget creation method
            Widget CreateJointSelectionWidget(string ID, LimbJoint j)
            {
                int normalSize = 10;
                int selectedSize = 20;
                var widget = new Widget(ID, normalSize, Widget.Shape.Circle)
                {
                    tooltipOffset = new Vector2(selectedSize / 2 + 5, -10)
                };
                widget.refresh = () =>
                {
                    widget.showTooltip = !selectedJoints.Contains(joint);
                    widget.color = selectedJoints.Contains(joint) ? Color.Yellow : Color.Red;
                };
                widget.refresh();
                widget.PreUpdate += dTime => widget.Enabled = editJoints;
                widget.PostUpdate += dTime =>
                {
                    if (widget.IsSelected)
                    {
                        widget.size = selectedSize;
                        widget.inputAreaMargin = 10;
                    }
                    else
                    {
                        widget.size = normalSize;
                        widget.inputAreaMargin = 0;
                    }
                };
                widget.MouseDown += () =>
                {
                    if (!selectedJoints.Contains(joint))
                    {
                        if (!Widget.EnableMultiSelect)
                        {
                            selectedJoints.Clear();
                        }
                        selectedJoints.Add(joint);
                    }
                    else if (Widget.EnableMultiSelect)
                    {
                        selectedJoints.Remove(joint);
                    }
                    foreach (var w in jointSelectionWidgets.Values)
                    {
                        w.refresh();
                        w.linkedWidget?.refresh();
                    }
                    ResetParamsEditor();
                };
                widget.tooltip = joint.jointParams.Name;
                jointSelectionWidgets.Add(ID, widget);
                return widget;
            }
        }
        #endregion

        #region Character Wizard
        private class Wizard
        {
            // Ragdoll data
            private string name = string.Empty;
            private bool isHumanoid = false;
            private string texturePath;
            private string xmlPath;
            private Dictionary<string, XElement> limbXElements = new Dictionary<string, XElement>();
            private List<GUIComponent> limbGUIElements = new List<GUIComponent>();
            private List<XElement> jointXElements = new List<XElement>();
            private List<GUIComponent> jointGUIElements = new List<GUIComponent>();

            private static Wizard instance;
            public static Wizard Instance
            {
                get
                {
                    if (instance == null)
                    {
                        instance = new Wizard();
                    }
                    return instance;
                }
            }

            public enum Tab { None, Character, Ragdoll }
            private View activeView;
            private Tab currentTab;

            public void SelectTab(Tab tab)
            {
                currentTab = tab;
                activeView?.Box.Close();
                switch (currentTab)
                {
                    case Tab.Character:
                        activeView = CharacterView.Get();
                        break;
                    case Tab.Ragdoll:
                        activeView = RagdollView.Get();
                        break;
                    case Tab.None:
                    default:
                        //activeView = null;
                        instance = null;
                        break;
                }
            }

            public void AddToGUIUpdateList()
            {
                activeView?.Box.AddToGUIUpdateList();
            }

            private class CharacterView : View
            {
                private static CharacterView instance;
                public static CharacterView Get() => Get(instance);

                protected override GUIMessageBox Create()
                {
                    var box = new GUIMessageBox("Create New Character", string.Empty, new string[] { "Cancel", "Next" }, GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight);
                    box.Content.ChildAnchor = Anchor.TopCenter;
                    box.Content.AbsoluteSpacing = 20;
                    int elementSize = 30;
                    var listBox = new GUIListBox(new RectTransform(new Vector2(1, 0.9f), box.Content.RectTransform));
                    var topGroup = new GUILayoutGroup(new RectTransform(new Point(listBox.Content.Rect.Width, elementSize * 6 + 20), listBox.Content.RectTransform)) { AbsoluteSpacing = 2 };
                    var fields = new List<GUIComponent>();
                    GUITextBox texturePathElement = null;
                    GUITextBox xmlPathElement = null;
                    void UpdatePaths()
                    {
                        string pathBase = $"Content/Characters/{Name}/{Name}";
                        XMLPath = $"{pathBase}.xml";
                        TexturePath = $"{pathBase}.png";
                        texturePathElement.Text = TexturePath;
                        xmlPathElement.Text = XMLPath;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        var mainElement = new GUIFrame(new RectTransform(new Point(topGroup.RectTransform.Rect.Width, elementSize), topGroup.RectTransform), style: null, color: Color.Gray * 0.25f);
                        fields.Add(mainElement);
                        RectTransform leftElement = new RectTransform(new Vector2(0.5f, 1), mainElement.RectTransform, Anchor.TopLeft);
                        RectTransform rightElement = new RectTransform(new Vector2(0.5f, 1), mainElement.RectTransform, Anchor.TopRight);
                        switch (i)
                        {
                            case 0:
                                new GUITextBlock(leftElement, "Name");
                                var nameField = new GUITextBox(rightElement, "Worm X") { CaretColor = Color.White };
                                string ProcessText(string text) => text.RemoveWhitespace().CapitaliseFirstInvariant();
                                Name = ProcessText(nameField.Text);
                                nameField.OnTextChanged += (tb, text) =>
                                {
                                    Name = ProcessText(text);
                                    UpdatePaths();
                                    return true;
                                };
                                break;
                            case 1:
                                new GUITextBlock(leftElement, "Is Humanoid?");
                                new GUITickBox(rightElement, string.Empty)
                                {
                                    Selected = IsHumanoid,
                                    OnSelected = (tB) => IsHumanoid = tB.Selected
                                };
                                break;
                            case 2:
                                new GUITextBlock(leftElement, "Config File Output");
                                xmlPathElement = new GUITextBox(rightElement, string.Empty)
                                {
                                    CaretColor = Color.White
                                };
                                xmlPathElement.OnTextChanged += (tb, text) =>
                                {
                                    XMLPath = text;
                                    return true;
                                };
                                break;
                            case 3:
                                new GUITextBlock(leftElement, "Texture Path");
                                texturePathElement = new GUITextBox(rightElement, string.Empty)
                                {
                                    CaretColor = Color.White,
                                };
                                texturePathElement.OnTextChanged += (tb, text) =>
                                {
                                    TexturePath = text;
                                    return true;
                                };
                                break;
                        }
                    }
                    UpdatePaths();
                    //var codeArea = new GUIFrame(new RectTransform(new Vector2(1, 0.5f), listBox.Content.RectTransform), style: null) { CanBeFocused = false };
                    //new GUITextBlock(new RectTransform(new Vector2(1, 0.05f), codeArea.RectTransform), "Custom code:");
                    //var inputBox = new GUITextBox(new RectTransform(new Vector2(1, 1 - 0.05f), codeArea.RectTransform, Anchor.BottomLeft), string.Empty, textAlignment: Alignment.TopLeft);
                    // Cancel
                    box.Buttons[0].OnClicked += (b, d) =>
                    {
                        Instance.SelectTab(Tab.None);
                        return true;
                    };
                    // Next
                    box.Buttons[1].OnClicked += (b, d) =>
                    {
                        Instance.SelectTab(Tab.Ragdoll);
                        return true;
                    };
                    return box;
                }
            }

            private class RagdollView : View
            {
                private static RagdollView instance;
                public static RagdollView Get() => Get(instance);

                protected override GUIMessageBox Create()
                {
                    var box = new GUIMessageBox("Define Ragdoll", string.Empty, new string[] { "Previous", "Create" }, GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight);
                    box.Content.ChildAnchor = Anchor.TopCenter;
                    box.Content.AbsoluteSpacing = 20;
                    int elementSize = 30;
                    var topGroup = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.05f), box.Content.RectTransform)) { AbsoluteSpacing = 2 };
                    var bottomGroup = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.75f), box.Content.RectTransform)) { AbsoluteSpacing = 10 };
                    // HTML
                    GUIMessageBox htmlBox = null;
                    var loadHtmlButton = new GUIButton(new RectTransform(new Point(topGroup.RectTransform.Rect.Width, elementSize), topGroup.RectTransform), "Load from HTML");
                    // Limbs
                    var limbsElement = new GUIFrame(new RectTransform(new Vector2(1, 0.05f), bottomGroup.RectTransform), style: null) { CanBeFocused = false };
                    new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), limbsElement.RectTransform), "Limbs:");
                    var limbButtonElement = new GUIFrame(new RectTransform(new Vector2(0.5f, 1f), limbsElement.RectTransform)
                        { RelativeOffset = new Vector2(0.25f, 0) }, style: null) { CanBeFocused = false };
                    var limbsList = new GUIListBox(new RectTransform(new Vector2(1, 0.45f), bottomGroup.RectTransform));
                    var removeLimbButton = new GUIButton(new RectTransform(new Point(limbButtonElement.Rect.Height, limbButtonElement.Rect.Height), limbButtonElement.RectTransform), "-")
                    {
                        OnClicked = (b, d) =>
                        {
                            var element = LimbGUIElements.LastOrDefault();
                            if (element == null) { return false; }
                            element.RectTransform.Parent = null;
                            LimbGUIElements.Remove(element);
                            return true;
                        }
                    };
                    var addLimbButton = new GUIButton(new RectTransform(new Point(limbButtonElement.Rect.Height, limbButtonElement.Rect.Height), limbButtonElement.RectTransform)
                    {
                        AbsoluteOffset = new Point(removeLimbButton.Rect.Width + 10, 0)
                    }, "+")
                    {
                        OnClicked = (b, d) =>
                        {
                            LimbType limbType = LimbType.None;
                            switch (LimbGUIElements.Count)
                            {
                                case 0:
                                    limbType = LimbType.Head;
                                    break;
                                case 1:
                                    limbType = LimbType.Torso;
                                    break;
                            }
                            CreateLimbGUIElement(limbsList.Content.RectTransform, elementSize, id: LimbGUIElements.Count, limbType: limbType);
                            return true;
                        }
                    };
                    // Joints
                    new GUIFrame(new RectTransform(new Vector2(1, 0.05f), bottomGroup.RectTransform), style: null) { CanBeFocused = false };
                    var jointsElement = new GUIFrame(new RectTransform(new Vector2(1, 0.05f), bottomGroup.RectTransform), style: null) { CanBeFocused = false };
                    new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), jointsElement.RectTransform), "Joints:");
                    var jointButtonElement = new GUIFrame(new RectTransform(new Vector2(0.5f, 1f), jointsElement.RectTransform)
                        { RelativeOffset = new Vector2(0.25f, 0) }, style: null) { CanBeFocused = false };
                    var jointsList = new GUIListBox(new RectTransform(new Vector2(1, 0.45f), bottomGroup.RectTransform));
                    var removeJointButton = new GUIButton(new RectTransform(new Point(jointButtonElement.Rect.Height, jointButtonElement.Rect.Height), jointButtonElement.RectTransform), "-")
                    {
                        OnClicked = (b, d) =>
                        {
                            var element = JointGUIElements.LastOrDefault();
                            if (element == null) { return false; }
                            element.RectTransform.Parent = null;
                            JointGUIElements.Remove(element);
                            return true;
                        }
                    };
                    var addJointButton = new GUIButton(new RectTransform(new Point(jointButtonElement.Rect.Height, jointButtonElement.Rect.Height), jointButtonElement.RectTransform)
                    {
                        AbsoluteOffset = new Point(removeJointButton.Rect.Width + 10, 0)
                    }, "+")
                    {
                        OnClicked = (b, d) =>
                        {
                            CreateJointGUIElement(jointsList.Content.RectTransform, elementSize);
                            return true;
                        }
                    };
                    loadHtmlButton.OnClicked = (b, d) =>
                    {
                        if (htmlBox == null)
                        {
                            htmlBox = new GUIMessageBox("Load HTML", string.Empty, new string[] { "Close", "Load" }, GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight);
                            var element = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.05f), htmlBox.Content.RectTransform), style: null, color: Color.Gray * 0.25f);
                            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), element.RectTransform), "HTML Path");
                            var htmlPathElement = new GUITextBox(new RectTransform(new Vector2(0.5f, 1), element.RectTransform, Anchor.TopRight), $"Content/Characters/{Name}/{Name}.html");
                            var list = new GUIListBox(new RectTransform(new Vector2(1, 0.8f), htmlBox.Content.RectTransform));
                            var htmlOutput = new GUITextBlock(new RectTransform(Vector2.One, list.Content.RectTransform), string.Empty) { CanBeFocused = false };
                            htmlBox.Buttons[0].OnClicked += (_b, _d) =>
                            {
                                htmlBox.Close();
                                return true;
                            };
                            htmlBox.Buttons[1].OnClicked += (_b, _d) =>
                            {
                                LimbGUIElements.ForEach(l => l.RectTransform.Parent = null);
                                LimbGUIElements.Clear();
                                JointGUIElements.ForEach(j => j.RectTransform.Parent = null);
                                JointGUIElements.Clear();
                                LimbXElements.Clear();
                                JointXElements.Clear();
                                ParseRagdollFromHTML(htmlPathElement.Text, (id, limbName, limbType, rect) =>
                                {
                                    CreateLimbGUIElement(limbsList.Content.RectTransform, elementSize, id, limbName, limbType, rect);
                                }, (id1, id2, anchor1, anchor2, jointName) =>
                                {
                                    CreateJointGUIElement(jointsList.Content.RectTransform, elementSize, id1, id2, anchor1, anchor2, jointName);
                                });
                                htmlOutput.Text = new XDocument(new XElement("Ragdoll", new object[]
                                {
                                        new XAttribute("type", Name),
                                            LimbXElements.Values,
                                            JointXElements
                                })).ToString();
                                htmlOutput.CalculateHeightFromText();
                                list.UpdateScrollBarSize();
                                return true;
                            };
                        }
                        else
                        {
                            GUIMessageBox.MessageBoxes.Add(htmlBox);
                        }
                        return true;
                    };
                    //var codeArea = new GUIFrame(new RectTransform(new Vector2(1, 0.5f), listBox.Content.RectTransform), style: null) { CanBeFocused = false };
                    //new GUITextBlock(new RectTransform(new Vector2(1, 0.05f), codeArea.RectTransform), "Custom code:");
                    //new GUITextBox(new RectTransform(new Vector2(1, 1 - 0.05f), codeArea.RectTransform, Anchor.BottomLeft), string.Empty, textAlignment: Alignment.TopLeft);
                    // Previous
                    box.Buttons[0].OnClicked += (b, d) =>
                    {
                        Instance.SelectTab(Tab.Character);
                        return true;
                    };
                    // Parse and create
                    box.Buttons[1].OnClicked += (b, d) =>
                    {
                        ParseLimbsFromGUIElements();
                        ParseJointsFromGUIElements();
                        var torsoAttributes = LimbXElements.Values.Select(x => x.Attribute("type")).Where(a => a.Value.ToLowerInvariant() == "torso");
                        if (torsoAttributes.Count() != 1)
                        {
                            GUI.AddMessage("You need to define one and only one limb as \"Torso\"!", Color.Red);
                            return false;
                        }
                        XElement torso = torsoAttributes.Single().Parent;
                        int radius = torso.GetAttributeInt("radius", -1);
                        int height = torso.GetAttributeInt("height", -1);
                        int width = torso.GetAttributeInt("width", -1);
                        int colliderHeight = - 1;
                        if (radius == -1)
                        {
                            // the collider is a box -> calculate the capsule
                            if (width == height)
                            {
                                radius = width / 2;
                            }
                            else
                            {
                                if (height > width)
                                {
                                    radius = width / 2;
                                    colliderHeight = height;
                                }
                                else
                                {
                                    radius = height / 2;
                                    colliderHeight = width;
                                }
                            }
                        }
                        else if (height > -1 || width > -1)
                        {
                            // the collider is a capsule -> use the capsule as it is
                            colliderHeight = width > height ? width : height;
                        }
                        var colliderAttributes = new List<XAttribute>() { new XAttribute("radius", radius) };
                        if (colliderHeight > -1)
                        {
                            colliderAttributes.Add(new XAttribute("height", colliderHeight));
                        }
                        var colliderElements = new List<XElement>() { new XElement("collider", colliderAttributes) };
                        if (IsHumanoid)
                        {
                            // For humanoids, we need a secondary, shorter collider for crouching
                            var secondaryCollider = new XElement("collider", new XAttribute("radius", radius));
                            if (colliderHeight > -1)
                            {
                                secondaryCollider.Add(new XAttribute("height", colliderHeight * 0.75f));
                            }
                            colliderElements.Add(secondaryCollider);
                        }
                        var ragdollParams = new object[]
                        {
                            new XAttribute("type", Name),
                                colliderElements,
                                LimbXElements.Values,
                                JointXElements
                        };
                        CharacterEditorScreen.instance.CreateCharacter(Name, IsHumanoid, ragdollParams);
                        GUI.AddMessage($"Character {Name} Created", Color.Green, font: GUI.Font);
                        Instance.SelectTab(Tab.None);
                        return true;
                    };
                    return box;
                }

                private void CreateLimbGUIElement(RectTransform parent, int elementSize, int id, string name = "", LimbType limbType = LimbType.None, Rectangle? sourceRect = null)
                {
                    var limbElement = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, elementSize * 5 + 40), parent), style: null, color: Color.Gray * 0.25f)
                    {
                        CanBeFocused = false
                    };
                    var group = new GUILayoutGroup(new RectTransform(Vector2.One, limbElement.RectTransform)) { AbsoluteSpacing = 2 };
                    var label = new GUITextBlock(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), name);
                    var idField = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                    var nameField = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                    var limbTypeField = GUI.CreateEnumField(limbType, elementSize, "Limb Type", group.RectTransform, font: GUI.Font);
                    var sourceRectField = GUI.CreateRectangleField(sourceRect ?? new Rectangle(0, 0, 1, 1), elementSize, "Source Rect", group.RectTransform, font: GUI.Font);
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), idField.RectTransform, Anchor.TopLeft), "ID");
                    new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), idField.RectTransform, Anchor.TopRight), GUINumberInput.NumberType.Int)
                    {
                        MinValueInt = 0,
                        MaxValueInt = byte.MaxValue,
                        IntValue = id,
                        OnValueChanged = numInput =>
                        {
                            id = numInput.IntValue;
                            string text = nameField.GetChild<GUITextBox>().Text;
                            string t = string.IsNullOrWhiteSpace(text) ? id.ToString() : text;
                            label.Text = t;
                        }
                    };
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopLeft), "Name");
                    var nameInput = new GUITextBox(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopRight), name)
                    {
                        CaretColor = Color.White,
                    };
                    nameInput.OnTextChanged += (tb, text) =>
                    {
                        string t = string.IsNullOrWhiteSpace(text) ? id.ToString() : text;
                        label.Text = t;
                        return true;
                    };
                    LimbGUIElements.Add(limbElement);
                }

                private void CreateJointGUIElement(RectTransform parent, int elementSize, int id1 = 0, int id2 = 1, Vector2? anchor1 = null, Vector2?  anchor2 = null, string jointName = "")
                {
                    var jointElement = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, elementSize * 6 + 40), parent), style: null, color: Color.Gray * 0.25f)
                    {
                        CanBeFocused = false
                    };
                    var group = new GUILayoutGroup(new RectTransform(Vector2.One, jointElement.RectTransform)) { AbsoluteSpacing = 2 };
                    var label = new GUITextBlock(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), jointName);
                    var nameField = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopLeft), "Name");
                    var nameInput = new GUITextBox(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopRight), jointName)
                    {
                        CaretColor = Color.White,
                    };
                    nameInput.OnTextChanged += (textB, text) =>
                    {
                        jointName = text;
                        label.Text = jointName;
                        return true;
                    };
                    var limb1Field = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), limb1Field.RectTransform, Anchor.TopLeft), "Limb 1");
                    var limb1InputField = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), limb1Field.RectTransform, Anchor.TopRight), GUINumberInput.NumberType.Int)
                    {
                        MinValueInt = 0,
                        MaxValueInt = byte.MaxValue,
                        IntValue = id1
                    };
                    var limb2Field = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), limb2Field.RectTransform, Anchor.TopLeft), "Limb 2");
                    var limb2InputField = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), limb2Field.RectTransform, Anchor.TopRight), GUINumberInput.NumberType.Int)
                    {
                        MinValueInt = 0,
                        MaxValueInt = byte.MaxValue,
                        IntValue = id2
                    };
                    GUI.CreateVector2Field(anchor1 ?? Vector2.Zero, elementSize, "Limb 1 Anchor", group.RectTransform, font: GUI.Font, decimalsToDisplay: 2);
                    GUI.CreateVector2Field(anchor2 ?? Vector2.Zero, elementSize, "Limb 2 Anchor", group.RectTransform, font: GUI.Font, decimalsToDisplay: 2);
                    label.Text = GetJointName(jointName);
                    limb1InputField.OnValueChanged += nInput => label.Text = GetJointName(jointName);
                    limb2InputField.OnValueChanged += nInput => label.Text = GetJointName(jointName);
                    JointGUIElements.Add(jointElement);
                    string GetJointName(string n) => string.IsNullOrWhiteSpace(n) ? $"Joint {limb1InputField.IntValue} - {limb2InputField.IntValue}" : n;
                }
            }

            private abstract class View
            {
                // Easy accessors to the common data.
                public string Name
                {
                    get => Instance.name;
                    set => Instance.name = value;
                }
                public bool IsHumanoid
                {
                    get => Instance.isHumanoid;
                    set => Instance.isHumanoid = value;
                }
                public string TexturePath
                {
                    get => Instance.texturePath;
                    set => Instance.texturePath = value;
                }
                public string XMLPath
                {
                    get => Instance.xmlPath;
                    set => Instance.xmlPath = value;
                }
                public Dictionary<string, XElement> LimbXElements
                {
                    get => Instance.limbXElements;
                    set => Instance.limbXElements = value;
                }
                public List<GUIComponent> LimbGUIElements
                {
                    get => Instance.limbGUIElements;
                    set => Instance.limbGUIElements = value;
                }
                public List<XElement> JointXElements
                {
                    get => Instance.jointXElements;
                    set => Instance.jointXElements = value;
                }
                public List<GUIComponent> JointGUIElements
                {
                    get => Instance.jointGUIElements;
                    set => Instance.jointGUIElements = value;
                }

                private GUIMessageBox box;
                public GUIMessageBox Box
                {
                    get
                    {
                        if (box == null)
                        {
                            box = Create();
                        }
                        return box;
                    }
                }

                protected abstract GUIMessageBox Create();
                protected static T Get<T>(T instance) where T : View, new()
                {
                    if (instance == null)
                    {
                        instance = new T();
                    }
                    return instance;
                }

                protected void ParseLimbsFromGUIElements()
                {
                    LimbXElements.Clear();
                    for (int i = 0; i < LimbGUIElements.Count; i++)
                    {
                        var limbGUIElement = LimbGUIElements[i];
                        var allChildren = limbGUIElement.GetAllChildren();
                        GUITextBlock GetField(string n) => allChildren.First(c => c is GUITextBlock textBlock && textBlock.Text == n) as GUITextBlock;
                        int id = GetField("ID").Parent.GetChild<GUINumberInput>().IntValue;
                        string limbName = GetField("Name").Parent.GetChild<GUITextBox>().Text;
                        LimbType limbType = (LimbType)GetField("Limb Type").Parent.GetChild<GUIDropDown>().SelectedData;
                        // Reverse, because the elements are created from right to left
                        var rectInputs = GetField("Source Rect").Parent.GetAllChildren().Where(c => c is GUINumberInput).Select(c => c as GUINumberInput).Reverse().ToArray();
                        int width = rectInputs[2].IntValue;
                        int height = rectInputs[3].IntValue;
                        var colliderAttributes = new List<XAttribute>();
                        if (width == height)
                        {
                            colliderAttributes.Add(new XAttribute("radius", width / 2));
                        }
                        else
                        {
                            if (height > width)
                            {
                                colliderAttributes.Add(new XAttribute("radius", width / 2));
                                colliderAttributes.Add(new XAttribute("height", (int)Math.Round(height * 0.75f)));
                            }
                            else
                            {
                                colliderAttributes.Add(new XAttribute("radius", height / 2));
                                colliderAttributes.Add(new XAttribute("width", (int)Math.Round(width * 0.75f)));
                            }
                        }
                        string notes = string.Empty;
                        idToCodeName.TryGetValue(id, out notes);
                        LimbXElements.Add(id.ToString(), new XElement("limb",
                            new XAttribute("id", id),
                            new XAttribute("name", limbName),
                            new XAttribute("type", limbType.ToString()),
                            colliderAttributes,
                            new XElement("sprite",
                                new XAttribute("texture", TexturePath),
                                new XAttribute("sourcerect", $"{rectInputs[0].IntValue}, {rectInputs[1].IntValue}, {width}, {height}")),
                            new XAttribute("notes", notes)
                            ));
                    }
                }

                protected void ParseJointsFromGUIElements()
                {
                    JointXElements.Clear();
                    for (int i = 0; i < JointGUIElements.Count; i++)
                    {
                        var jointGUIElement = JointGUIElements[i];
                        var allChildren = jointGUIElement.GetAllChildren();
                        GUITextBlock GetField(string n) => allChildren.First(c => c is GUITextBlock textBlock && textBlock.Text == n) as GUITextBlock;
                        string jointName = GetField("Name").Parent.GetChild<GUITextBox>().Text;
                        int limb1ID = GetField("Limb 1").Parent.GetChild<GUINumberInput>().IntValue;
                        int limb2ID = GetField("Limb 2").Parent.GetChild<GUINumberInput>().IntValue;
                        // Reverse, because the elements are created from right to left
                        var anchor1Inputs = GetField("Limb 1 Anchor").Parent.GetAllChildren().Where(c => c is GUINumberInput).Select(c => c as GUINumberInput).Reverse().ToArray();
                        var anchor2Inputs = GetField("Limb 2 Anchor").Parent.GetAllChildren().Where(c => c is GUINumberInput).Select(c => c as GUINumberInput).Reverse().ToArray();
                        JointXElements.Add(new XElement("joint",
                            new XAttribute("name", jointName),
                            new XAttribute("limb1", limb1ID),
                            new XAttribute("limb2", limb2ID),
                            new XAttribute("limb1anchor", $"{anchor1Inputs[0].FloatValue.Format(2)}, {anchor1Inputs[1].FloatValue.Format(2)}"),
                            new XAttribute("limb2anchor", $"{anchor2Inputs[0].FloatValue.Format(2)}, {anchor2Inputs[1].FloatValue.Format(2)}")));
                    }
                }

                Dictionary<int, string> idToCodeName = new Dictionary<int, string>();
                protected void ParseRagdollFromHTML(string path, Action<int, string, LimbType, Rectangle> limbCallback = null, Action<int, int, Vector2, Vector2, string> jointCallback = null)
                {
                    // TODO: parse as xml?
                    //XDocument doc = XMLExtensions.TryLoadXml(path);
                    //var xElements = doc.Elements().ToArray();
                    string html = string.Empty;
                    try
                    {
                        html = File.ReadAllText(path);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError($"[CharacterEditorScreen] Failed to read html at {path}", e);
                        return;
                    }

                    var lines = html.Split(new string[] { "<div", "</div>", Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                    .Where(s => s.Contains("left") && s.Contains("top") && s.Contains("width") && s.Contains("height"));
                    int id = 0;
                    Dictionary<string, int> hierarchyToID = new Dictionary<string, int>();
                    Dictionary<int, string> idToHierarchy = new Dictionary<int, string>();
                    Dictionary<int, string> idToPositionCode = new Dictionary<int, string>();
                    Dictionary<int, string> idToName = new Dictionary<int, string>();
                    idToCodeName.Clear();
                    foreach (var line in lines)
                    {
                        var codeNames = new string(line.SkipWhile(c => c != '>').Skip(1).ToArray()).Split(',');
                        for (int i = 0; i < codeNames.Length; i++)
                        {
                            string codeName = codeNames[i].Trim();
                            if (string.IsNullOrWhiteSpace(codeName)) { continue; }
                            idToCodeName.Add(id, codeName);
                            string limbName = new string(codeName.SkipWhile(c => c != '_').Skip(1).ToArray());
                            if (string.IsNullOrWhiteSpace(limbName)) { continue; }
                            idToName.Add(id, limbName);
                            var parts = line.Split(' ');
                            int ParseToInt(string selector)
                            {
                                string part = parts.First(p => p.Contains(selector));
                                string s = new string(part.SkipWhile(c => c != ':').Skip(1).TakeWhile(c => char.IsNumber(c)).ToArray());
                                int.TryParse(s, out int v);
                                return v;
                            };
                            // example: 111311cr -> 111311
                            string hierarchy = new string(codeName.TakeWhile(c => char.IsNumber(c)).ToArray());
                            if (hierarchyToID.ContainsKey(hierarchy))
                            {
                                DebugConsole.ThrowError($"Multiple items with the same hierarchy \"{hierarchy}\" found ({codeName}). Cannot continue.");
                                return;
                            }
                            hierarchyToID.Add(hierarchy, id);
                            idToHierarchy.Add(id, hierarchy);
                            string positionCode = new string(codeName.SkipWhile(c => char.IsNumber(c)).TakeWhile(c => c != '_').ToArray());
                            idToPositionCode.Add(id, positionCode.ToLowerInvariant());
                            int x = ParseToInt("left");
                            int y = ParseToInt("top");
                            int width = ParseToInt("width");
                            int height = ParseToInt("height");
                            // This is overridden when the data is loaded from the gui fields.
                            LimbXElements.Add(hierarchy, new XElement("limb",
                                new XAttribute("id", id),
                                new XAttribute("name", limbName),
                                new XAttribute("type", ParseLimbType(limbName).ToString()),
                                new XElement("sprite",
                                    new XAttribute("texture", TexturePath),
                                    new XAttribute("sourcerect", $"{x}, {y}, {width}, {height}"))
                                ));
                            limbCallback?.Invoke(id, limbName, ParseLimbType(limbName), new Rectangle(x, y, width, height));
                            id++;
                        }
                    }
                    for (int i = 0; i < id; i++)
                    {
                        if (idToHierarchy.TryGetValue(i, out string hierarchy))
                        {
                            if (hierarchy != "0")
                            {
                                // OLD LOGIC: If the bone is at the root hierarchy, parent the bone to the last sibling (1 is parented to 0, 2 to 1 etc)
                                // NEW LOGIC: if hierarchy length == 1, parent to 0
                                // Else parent to the last bone in the current hierarchy (11 is parented to 1, 212 is parented to 21 etc)
                                //string parent = hierarchy.Length > 1 ? hierarchy.Remove(hierarchy.Length - 1, 1) : (int.Parse(hierarchy) - 1).ToString();
                                string parent = hierarchy.Length > 1 ? hierarchy.Remove(hierarchy.Length - 1, 1) : "0";
                                if (hierarchyToID.TryGetValue(parent, out int parentID))
                                {
                                    Vector2 anchor1 = Vector2.Zero;
                                    Vector2 anchor2 = Vector2.Zero;
                                    idToName.TryGetValue(parentID, out string parentName);
                                    idToName.TryGetValue(i, out string limbName);
                                    string jointName = $"Joint {parentName} - {limbName}";
                                    if (idToPositionCode.TryGetValue(i, out string positionCode))
                                    {
                                        float scalar = 0.8f;
                                        if (LimbXElements.TryGetValue(parent, out XElement parentElement))
                                        {
                                            Rectangle parentSourceRect = parentElement.Element("sprite").GetAttributeRect("sourcerect", Rectangle.Empty);
                                            float parentWidth = parentSourceRect.Width / 2 * scalar;
                                            float parentHeight = parentSourceRect.Height / 2 * scalar;
                                            switch (positionCode)
                                            {
                                                case "tl":  // -1, 1
                                                    anchor1 = new Vector2(-parentWidth, parentHeight);
                                                    break;
                                                case "tc":  // 0, 1
                                                    anchor1 = new Vector2(0, parentHeight);
                                                    break;
                                                case "tr":  // -1, 1
                                                    anchor1 = new Vector2(-parentWidth, parentHeight);
                                                    break;
                                                case "cl":  // -1, 0
                                                    anchor1 = new Vector2(-parentWidth, 0);
                                                    break;
                                                case "cr":  // 1, 0
                                                    anchor1 = new Vector2(parentWidth, 0);
                                                    break;
                                                case "bl":  // -1, -1
                                                    anchor1 = new Vector2(-parentWidth, -parentHeight);
                                                    break;
                                                case "bc":  // 0, -1
                                                    anchor1 = new Vector2(0, -parentHeight);
                                                    break;
                                                case "br":  // 1, -1
                                                    anchor1 = new Vector2(parentWidth, -parentHeight);
                                                    break;
                                            }
                                            if (LimbXElements.TryGetValue(hierarchy, out XElement element))
                                            {
                                                Rectangle sourceRect = element.Element("sprite").GetAttributeRect("sourcerect", Rectangle.Empty);
                                                float width = sourceRect.Width / 2 * scalar;
                                                float height = sourceRect.Height / 2 * scalar;
                                                switch (positionCode)
                                                {
                                                    // Inverse
                                                    case "tl":
                                                        // br
                                                        anchor2 = new Vector2(-width, -height);
                                                        break;
                                                    case "tc":
                                                        // bc
                                                        anchor2 = new Vector2(0, -height);
                                                        break;
                                                    case "tr":
                                                        // bl
                                                        anchor2 = new Vector2(-width, -height);
                                                        break;
                                                    case "cl":
                                                        // cr
                                                        anchor2 = new Vector2(width, 0);
                                                        break;
                                                    case "cr":
                                                        // cl
                                                        anchor2 = new Vector2(-width, 0);
                                                        break;
                                                    case "bl":
                                                        // tr
                                                        anchor2 = new Vector2(-width, height);
                                                        break;
                                                    case "bc":
                                                        // tc
                                                        anchor2 = new Vector2(0, height);
                                                        break;
                                                    case "br":
                                                        // tl
                                                        anchor2 = new Vector2(-width, height);
                                                        break;
                                                }
                                            }
                                        }
                                    }
                                    // This is overridden when the data is loaded from the gui fields.
                                    JointXElements.Add(new XElement("joint",
                                        new XAttribute("name", jointName),
                                        new XAttribute("limb1", parentID),
                                        new XAttribute("limb2", i),
                                        new XAttribute("limb1anchor", $"{anchor1.X.Format(2)}, {anchor1.Y.Format(2)}"),
                                        new XAttribute("limb2anchor", $"{anchor2.X.Format(2)}, {anchor2.Y.Format(2)}")
                                        ));
                                    jointCallback?.Invoke(parentID, i, anchor1, anchor2, jointName);
                                }
                            }
                        }
                    }
                }

                protected LimbType ParseLimbType(string limbName)
                {
                    var limbType = LimbType.None;
                    string n = limbName.ToLowerInvariant();
                    switch (n)
                    {
                        case "head":
                            limbType = LimbType.Head;
                            break;
                        case "torso":
                            limbType = LimbType.Torso;
                            break;
                        case "waist":
                        case "pelvis":
                            limbType = LimbType.Waist;
                            break;
                        case "tail":
                            limbType = LimbType.Tail;
                            break;
                    }
                    if (limbType == LimbType.None)
                    {
                        if (n.Contains("tail"))
                        {
                            limbType = LimbType.Tail;
                        }
                        else if (n.Contains("arm") && !n.Contains("lower"))
                        {
                            if (n.Contains("right"))
                            {
                                limbType = LimbType.RightArm;
                            }
                            else if (n.Contains("left"))
                            {
                                limbType = LimbType.LeftArm;
                            }
                        }
                        else if (n.Contains("hand") || n.Contains("palm"))
                        {
                            if (n.Contains("right"))
                            {
                                limbType = LimbType.RightHand;
                            }
                            else if (n.Contains("left"))
                            {
                                limbType = LimbType.LeftHand;
                            }
                        }
                        else if (n.Contains("thigh") || n.Contains("upperleg"))
                        {
                            if (n.Contains("right"))
                            {
                                limbType = LimbType.RightThigh;
                            }
                            else if (n.Contains("left"))
                            {
                                limbType = LimbType.LeftThigh;
                            }
                        }
                        else if (n.Contains("shin") || n.Contains("lowerleg"))
                        {
                            if (n.Contains("right"))
                            {
                                limbType = LimbType.RightLeg;
                            }
                            else if (n.Contains("left"))
                            {
                                limbType = LimbType.LeftLeg;
                            }
                        }
                        else if (n.Contains("foot"))
                        {
                            if (n.Contains("right"))
                            {
                                limbType = LimbType.RightFoot;
                            }
                            else if (n.Contains("left"))
                            {
                                limbType = LimbType.LeftFoot;
                            }
                        }
                    }
                    return limbType;
                }
            }
        }
        #endregion
    }
}
