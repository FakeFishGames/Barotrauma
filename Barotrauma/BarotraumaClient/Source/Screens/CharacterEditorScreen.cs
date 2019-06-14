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

        private bool ShowExtraRagdollControls => selectedLimbs.Any() && editLimbs || selectedJoints.Any() && editJoints;

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
        private bool autoFreeze;
        private bool limbPairEditing;
        private bool uniformScaling;
        private bool lockSpriteOrigin;
        private bool lockSpritePosition;
        private bool lockSpriteSize;
        private bool recalculateCollider;
        private bool copyJointSettings;
        private bool showColliders;
        private bool displayWearables;
        private bool displayBackgroundColor;
        private bool ragdollResetRequiresForceLoading;
        private bool animationResetRequiresForceLoading;

        private bool jointCreationMode;
        private bool useMouseOffset;
        private bool isExtrudingJoint;
        private bool isDrawingJoint;
        private Limb closestSelectedLimb;
        private Limb targetLimb;
        private Vector2? anchor1Pos;

        private float spriteSheetZoom = 1;
        private float spriteSheetMinZoom = 0.25f;
        private float spriteSheetMaxZoom = 1;
        private int spriteSheetOffsetY = 20;
        private int spriteSheetOffsetX;
        private bool hideBodySheet;
        private Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1.0f);
        private Vector2 cameraOffset;

        private List<LimbJoint> selectedJoints = new List<LimbJoint>();
        private List<Limb> selectedLimbs = new List<Limb>();

        private bool isEndlessRunner;

        private Rectangle spriteSheetRect;

        private Rectangle CalculateSpritesheetRectangle() => 
            new Rectangle(
                spriteSheetOffsetX, 
                spriteSheetOffsetY, 
                (int)(Textures.OrderByDescending(t => t.Width).First().Width * spriteSheetZoom), 
                (int)(Textures.Sum(t => t.Height) * spriteSheetZoom));

        private const string screenTextTag = "CharacterEditor.";

        public override void Select()
        {
            base.Select();

            SoundPlayer.OverrideMusicType = "none";
            SoundPlayer.OverrideMusicDuration = null;
            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", 0.0f);

            GUI.ForceMouseOn(null);
            CalculateSpritesheetPosition();
            if (Submarine.MainSub == null)
            {
                ResetVariables();
                Submarine.MainSub = new Submarine("Content/AnimEditor.sub");
                Submarine.MainSub.Load(unloadPrevious: false, showWarningMessages: false);
                originalWall = new WallGroup(new List<Structure>(Structure.WallList));
                CloneWalls();
                CalculateMovementLimits();
                isEndlessRunner = true;
                GameMain.LightManager.LightingEnabled = false;
            }
            else if (instance == null)
            {
                ResetVariables();
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
                OnPostSpawn();
            }
            OpenDoors();
            GameMain.Instance.OnResolutionChanged += OnResolutionChanged;
            instance = this;

            if (!GameMain.Config.EditorDisclaimerShown)
            {
                GameMain.Instance.ShowEditorDisclaimer();
            }
        }

        private void ResetVariables()
        {
            editAnimations = false;
            editLimbs = false;
            editJoints = false;
            editIK = false;
            showRagdoll = false;
            showParamsEditor = false;
            showSpritesheet = true;
            isFreezed = false;
            autoFreeze = true;
            limbPairEditing = true;
            uniformScaling = true;
            lockSpriteOrigin = false;
            lockSpritePosition = false;
            lockSpriteSize = false;
            recalculateCollider = false;
            copyJointSettings = false;
            showColliders = false;
            displayWearables = true;
            displayBackgroundColor = false;
            ragdollResetRequiresForceLoading = false;
            animationResetRequiresForceLoading = false;
            jointCreationMode = false;
            isExtrudingJoint = false;
            isDrawingJoint = false;
            cameraOffset = Vector2.Zero;
            targetLimb = null;
            anchor1Pos = null;
            useMouseOffset = false;
            closestSelectedLimb = null;
            Wizard.instance = null;
        }

        private void Reset()
        {
            ResetVariables();
            if (character != null)
            {
                AnimParams.ForEach(a => a.Reset(true));
                RagdollParams.Reset(true);
                RagdollParams.ClearHistory();
                CurrentAnimation.ClearHistory();
                if (!character.Removed)
                {
                    character.Remove();
                }
                character = null;
            }
        }

        public override void Deselect()
        {
            base.Deselect();

            SoundPlayer.OverrideMusicType = null;
            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", GameMain.Config.SoundVolume);

            GUI.ForceMouseOn(null);
            if (isEndlessRunner)
            {
                Submarine.MainSub.Remove();
                isEndlessRunner = false;
                Reset();
                GameMain.World.ProcessChanges();
            }
            else
            {
                if (character != null)
                {
                    character.ForceRun = false;
                    character.AnimController.ForceSelectAnimationType = AnimationType.NotDefined;
                }
            }
            GameMain.Instance.OnResolutionChanged -= OnResolutionChanged;
            GameMain.LightManager.LightingEnabled = true;
            ClearWidgets();
            ClearSelection();
        }

        private void OnResolutionChanged()
        {
            CreateGUI();
            CalculateSpritesheetPosition();
        }

        private static string GetCharacterEditorTranslation(string tag)
        {
            return TextManager.Get(screenTextTag + tag);
        }

        #region Main methods
        public override void AddToGUIUpdateList()
        {
            //base.AddToGUIUpdateList();

            characterSelectionPanel.AddToGUIUpdateList();
            fileEditPanel.AddToGUIUpdateList();
            modesPanel.AddToGUIUpdateList();
            miscToolsPanel.AddToGUIUpdateList();

            Wizard.instance?.AddToGUIUpdateList();
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
                ParamsEditor.Instance.EditorBox.Parent.AddToGUIUpdateList();
            }
            if (ShowExtraRagdollControls)
            {
                extraRagdollControls.AddToGUIUpdateList();
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            spriteSheetRect = CalculateSpritesheetRectangle();
            // Handle shortcut keys
            if (GUI.KeyboardDispatcher.Subscriber == null && Wizard.instance == null)
            {
                if (PlayerInput.KeyDown(Keys.LeftControl))
                {
                    Character.DisableControls = true;
                    Widget.EnableMultiSelect = !editAnimations;
                    // Undo/Redo
                    if (PlayerInput.KeyHit(Keys.Z))
                    {
                        if (editJoints || editLimbs || editIK)
                        {
                            RagdollParams.Undo();
                            character.AnimController.ResetJoints();
                            character.AnimController.ResetLimbs();
                            ClearWidgets();
                            CreateGUI();
                            //ragdollResetRequiresForceLoading = true;
                            ResetParamsEditor();
                        }
                        if (editAnimations)
                        {
                            CurrentAnimation.Undo();
                            ClearWidgets();
                            ResetParamsEditor();
                            //CreateGUI();
                            animationResetRequiresForceLoading = true;
                        }
                    }
                    else if (PlayerInput.KeyHit(Keys.R))
                    {
                        if (editJoints || editLimbs || editIK)
                        {
                            RagdollParams.Redo();
                            character.AnimController.ResetJoints();
                            character.AnimController.ResetLimbs();
                            ClearWidgets();
                            CreateGUI();
                            //ragdollResetRequiresForceLoading = true;
                            ResetParamsEditor();
                        }
                        if (editAnimations)
                        {
                            CurrentAnimation.Redo();
                            ClearWidgets();
                            ResetParamsEditor();
                            //CreateGUI();
                            animationResetRequiresForceLoading = true;
                        }
                    }
                }
                else
                {
                    Widget.EnableMultiSelect = false;
                }
                if (PlayerInput.KeyHit(Keys.C) && !PlayerInput.KeyDown(Keys.LeftControl))
                {
                    copyJointsToggle.Selected = !copyJointsToggle.Selected;
                }
                if (character.IsHumanoid)
                {
                    if (PlayerInput.KeyHit(Keys.T) || PlayerInput.KeyHit(Keys.X))
                    {
                        animTestPoseToggle.Selected = !animTestPoseToggle.Selected;
                    }
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
                        CurrentAnimation.ClearHistory();
                        animSelection.Select(index);
                        CurrentAnimation.CreateSnapshot();
                    }
                }
                if (!PlayerInput.KeyDown(Keys.LeftControl) && PlayerInput.KeyHit(Keys.E))
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
                if (PlayerInput.RightButtonClicked() || PlayerInput.KeyHit(Keys.Escape))
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
                    jointCreationMode = false;
                    closestSelectedLimb = null;
                }
                if (PlayerInput.KeyHit(Keys.Delete))
                {
                    DeleteSelected();
                }
                if (editLimbs && PlayerInput.KeyDown(Keys.LeftControl))
                {
                    var selectedLimb = selectedLimbs.FirstOrDefault();
                    if (selectedLimb != null)
                    {
                        if (PlayerInput.KeyHit(Keys.C))
                        {
                            CopyLimb(selectedLimb);
                        }
                    }
                }
                if (ShowExtraRagdollControls && PlayerInput.KeyDown(Keys.LeftControl))
                {
                    if (PlayerInput.KeyHit(Keys.E))
                    {
                        jointCreationMode = !jointCreationMode;
                        useMouseOffset = true;
                    }
                }
                if (jointCreationMode)
                {
                    createJointButton.HoverColor = Color.LightGreen;
                    createJointButton.Color = Color.LightGreen;
                }
                else
                {
                    createJointButton.HoverColor = Color.White;
                    createJointButton.Color = Color.White;
                }
                UpdateJointCreation();
                if (PlayerInput.KeyHit(Keys.Left))
                {
                    foreach (var limb in selectedLimbs)
                    {
                        var newRect = limb.ActiveSprite.SourceRect;
                        newRect.X--;
                        UpdateSourceRect(limb, newRect);
                    }
                }
                if (PlayerInput.KeyHit(Keys.Right))
                {
                    foreach (var limb in selectedLimbs)
                    {
                        var newRect = limb.ActiveSprite.SourceRect;
                        newRect.X++;
                        UpdateSourceRect(limb, newRect);
                    }
                }
                if (PlayerInput.KeyHit(Keys.Down))
                {
                    foreach (var limb in selectedLimbs)
                    {
                        var newRect = limb.ActiveSprite.SourceRect;
                        newRect.Y++;
                        UpdateSourceRect(limb, newRect);
                    }
                }
                if (PlayerInput.KeyHit(Keys.Up))
                {
                    foreach (var limb in selectedLimbs)
                    {
                        var newRect = limb.ActiveSprite.SourceRect;
                        newRect.Y--;
                        UpdateSourceRect(limb, newRect);
                    }
                }
            }
            if (!isFreezed && Wizard.instance == null)
            {
                if (character.AnimController.Invalid)
                {
                    Reset();
                    SpawnCharacter(currentCharacterConfig);
                }

                Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
                Submarine.MainSub.Update((float)deltaTime);

                foreach (PhysicsBody body in PhysicsBody.List)
                {
                    body.SetPrevTransform(body.SimPosition, body.Rotation);
                    body.Update((float)deltaTime);
                }
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
            // Camera
            Cam.MoveCamera((float)deltaTime, allowMove: false);
            Vector2 targetPos = character.WorldPosition;
            if (PlayerInput.MidButtonHeld())
            {
                // Pan
                Vector2 moveSpeed = PlayerInput.MouseSpeed * (float)deltaTime * 100.0f / Cam.Zoom;
                moveSpeed.X = -moveSpeed.X;
                cameraOffset += moveSpeed;
                Vector2 max = new Vector2(GameMain.GraphicsWidth * 0.3f, GameMain.GraphicsHeight * 0.38f) / Cam.Zoom;
                Vector2 min = -max;
                cameraOffset = Vector2.Clamp(cameraOffset, min, max);
            }
            Cam.Position = targetPos + cameraOffset;
            MapEntity.mapEntityList.ForEach(e => e.IsHighlighted = false);
            // Update widgets
            jointSelectionWidgets.Values.ForEach(w => w.Update((float)deltaTime));
            limbEditWidgets.Values.ForEach(w => w.Update((float)deltaTime));
            animationWidgets.Values.ForEach(w => w.Update((float)deltaTime));
            // Handle limb selection
            if (editLimbs && PlayerInput.LeftButtonDown() && GUI.MouseOn == null && Widget.selectedWidgets.None())
            {
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (limb == null || limb.ActiveSprite == null) { continue; }
                    // Select limbs on ragdoll
                    if (!spriteSheetRect.Contains(PlayerInput.MousePosition) && MathUtils.RectangleContainsPoint(GetLimbPhysicRect(limb), PlayerInput.MousePosition))
                    {
                        HandleLimbSelection(limb);
                    }
                    // Select limbs on sprite sheet
                    if (GetLimbSpritesheetRect(limb).Contains(PlayerInput.MousePosition))
                    {
                        HandleLimbSelection(limb);
                    }
                }
            }
            miscToolsToggle?.UpdateOpenState((float)deltaTime, new Vector2(miscToolsPanel.Rect.Width + rightArea.RectTransform.AbsoluteOffset.X, 0), miscToolsPanel.RectTransform);
            fileEditToggle?.UpdateOpenState((float)deltaTime, new Vector2(-fileEditPanel.Rect.Width - rightArea.RectTransform.AbsoluteOffset.X, 0), fileEditPanel.RectTransform);
            characterPanelToggle?.UpdateOpenState((float)deltaTime, new Vector2(-characterSelectionPanel.Rect.Width - rightArea.RectTransform.AbsoluteOffset.X, 0), characterSelectionPanel.RectTransform);
            modesToggle?.UpdateOpenState((float)deltaTime, new Vector2(-modesPanel.Rect.Width - leftArea.RectTransform.AbsoluteOffset.X, 0), modesPanel.RectTransform);
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
            else if (showColliders)
            {
                character.AnimController.Collider.DebugDraw(spriteBatch, Color.White, forceColor: true);
                character.AnimController.Limbs.ForEach(l => l.body.DebugDraw(spriteBatch, Color.LightGreen, forceColor: true));
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
            if (jointCreationMode)
            {
                var textPos = new Vector2(GameMain.GraphicsWidth / 2 - 120, GameMain.GraphicsHeight / 4);
                if (isExtrudingJoint)
                {
                    var selectedJoint = selectedJoints.LastOrDefault();
                    if (selectedJoint != null)
                    {
                        GUI.DrawString(spriteBatch, textPos, GetCharacterEditorTranslation("CreatingNewJoint"), Color.White, font: GUI.LargeFont);
                        if (spriteSheetRect.Contains(PlayerInput.MousePosition))
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
                    if (closestSelectedLimb != null)
                    {
                        GUI.DrawString(spriteBatch, textPos, GetCharacterEditorTranslation("CreatingNewJoint"), Color.White, font: GUI.LargeFont);
                        if (spriteSheetRect.Contains(PlayerInput.MousePosition))
                        {
                            var startPos = GetLimbSpritesheetRect(closestSelectedLimb).Center.ToVector2();
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
                                ? SimToScreen(closestSelectedLimb.SimPosition + Vector2.Transform(ConvertUnits.ToSimUnits(anchor1Pos.Value), Matrix.CreateRotationZ(closestSelectedLimb.Rotation)))
                                : SimToScreen(closestSelectedLimb.SimPosition);
                            DrawJointCreationOnRagdoll(spriteBatch, startPos);
                        }
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
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 35, 100), GetCharacterEditorTranslation("Frozen"), Color.Blue, Color.White * 0.5f, 10, GUI.Font);
            }
            if (animTestPoseToggle.Selected)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 100, 150), GetCharacterEditorTranslation("AnimationTestPoseEnabled"), Color.Blue, Color.White * 0.5f, 10, GUI.Font);
            }
            if (showSpritesheet)
            {
                var topLeft = spriteSheetControls.RectTransform.TopLeft;
                GUI.DrawString(spriteBatch, new Vector2(topLeft.X + 300, GameMain.GraphicsHeight - 80), GetCharacterEditorTranslation("SpriteSheetOrientation") + ":", Color.White, Color.Gray * 0.5f, 10, GUI.Font);
                DrawRadialWidget(spriteBatch, new Vector2(topLeft.X + 510, GameMain.GraphicsHeight - 60), RagdollParams.SpritesheetOrientation, string.Empty, Color.White,
                    angle => TryUpdateRagdollParam("spritesheetorientation", angle), circleRadius: 40, widgetSize: 15, rotationOffset: MathHelper.Pi, autoFreeze: false);
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

                // Collider
                var collider = character.AnimController.Collider;
                var colliderDrawPos = SimToScreen(collider.SimPosition);
                Vector2 forward = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation));
                var endPos = SimToScreen(collider.SimPosition + forward * collider.radius);
                GUI.DrawLine(spriteBatch, colliderDrawPos, endPos, Color.LightGreen);
                GUI.DrawLine(spriteBatch, colliderDrawPos, SimToScreen(collider.SimPosition + forward * 0.25f), Color.Blue);
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
            isExtrudingJoint = !editLimbs && editJoints && jointCreationMode;
            isDrawingJoint = !editJoints && editLimbs && jointCreationMode;
            if (isExtrudingJoint)
            {
                var selectedJoint = selectedJoints.LastOrDefault();
                if (selectedJoint != null)
                {
                    if (spriteSheetRect.Contains(PlayerInput.MousePosition))
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
                if (selectedLimbs.Any())
                {
                    if (spriteSheetRect.Contains(PlayerInput.MousePosition))
                    {
                        if (closestSelectedLimb == null)
                        {
                            closestSelectedLimb = GetClosestLimbOnSpritesheet(PlayerInput.MousePosition, l => selectedLimbs.Contains(l));
                        }
                        if (anchor1Pos == null && useMouseOffset)
                        {
                            anchor1Pos = GetLimbSpritesheetRect(closestSelectedLimb).Center.ToVector2() - PlayerInput.MousePosition;
                        }
                        targetLimb = GetClosestLimbOnSpritesheet(PlayerInput.MousePosition, l => l != null && l != closestSelectedLimb && l.ActiveSprite != null);
                        if (targetLimb != null && PlayerInput.LeftButtonClicked())
                        {
                            Vector2 anchor1 = anchor1Pos.HasValue ? anchor1Pos.Value / spriteSheetZoom : Vector2.Zero;
                            anchor1.X = -anchor1.X;
                            Vector2 anchor2 = (GetLimbSpritesheetRect(targetLimb).Center.ToVector2() - PlayerInput.MousePosition) / spriteSheetZoom;
                            anchor2.X = -anchor2.X;
                            CreateJoint(closestSelectedLimb.limbParams.ID, targetLimb.limbParams.ID, anchor1, anchor2);
                            jointCreationMode = false;
                            closestSelectedLimb = null;
                        }
                    }
                    else
                    {
                        if (closestSelectedLimb == null)
                        {
                            closestSelectedLimb = GetClosestLimbOnRagdoll(PlayerInput.MousePosition, l => selectedLimbs.Contains(l));
                        }
                        if (anchor1Pos == null && useMouseOffset)
                        {
                            anchor1Pos = ConvertUnits.ToDisplayUnits(closestSelectedLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                        }
                        targetLimb = GetClosestLimbOnRagdoll(PlayerInput.MousePosition, l => l != null && l != closestSelectedLimb && l.ActiveSprite != null);
                        if (targetLimb != null && PlayerInput.LeftButtonClicked())
                        {
                            Vector2 anchor1 = anchor1Pos ?? Vector2.Zero;
                            Vector2 anchor2 = ConvertUnits.ToDisplayUnits(targetLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                            CreateJoint(closestSelectedLimb.limbParams.ID, targetLimb.limbParams.ID, anchor1, anchor2);
                            jointCreationMode = false;
                            closestSelectedLimb = null;
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
            if (limb == null) { return; }
            //RagdollParams.StoreState();
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
            CreateTextures();
            TeleportTo(spawnPosition);
            ClearWidgets();
            ClearSelection();
            selectedLimbs.Add(character.AnimController.Limbs.Single(l => l.limbParams == newLimbParams));
            ResetParamsEditor();
            ragdollResetRequiresForceLoading = true;
        }

        /// <summary>
        /// Creates a new joint between the last limb of the given joint and the target limb.
        /// </summary>
        private void ExtrudeJoint(LimbJoint joint, int targetLimb, Vector2? anchor1 = null, Vector2? anchor2 = null)
        {
            if (joint == null) { return; }
            CreateJoint(joint.jointParams.Limb2, targetLimb, anchor1, anchor2);
        }

        /// <summary>
        /// Creates a new joint using the limb IDs.
        /// </summary>
        private void CreateJoint(int fromLimb, int toLimb, Vector2? anchor1 = null, Vector2? anchor2 = null)
        {
            //RagdollParams.StoreState();
            Vector2 a1 = anchor1 ?? Vector2.Zero;
            Vector2 a2 = anchor2 ?? Vector2.Zero;
            var newJointElement = new XElement("joint",
                new XAttribute("limb1", fromLimb),
                new XAttribute("limb2", toLimb),
                new XAttribute("limb1anchor", $"{a1.X.Format(2)}, {a1.Y.Format(2)}"),
                new XAttribute("limb2anchor", $"{a2.X.Format(2)}, {a2.Y.Format(2)}")
                );
            var lastJointElement = RagdollParams.MainElement.Elements("joint").LastOrDefault();
            if (lastJointElement == null)
            {
                // If no joints exist, use the last limb element.
                lastJointElement = RagdollParams.MainElement.Elements("limb").LastOrDefault();
            }
            if (lastJointElement == null)
            {
                DebugConsole.ThrowError(GetCharacterEditorTranslation("CantAddJointsNoLimbElements"));
                return;
            }
            lastJointElement.AddAfterSelf(newJointElement);
            var newJointParams = new JointParams(newJointElement, RagdollParams);
            RagdollParams.Joints.Add(newJointParams);
            character.AnimController.Recreate();
            CreateTextures();
            TeleportTo(spawnPosition);
            ClearWidgets();
            ClearSelection();
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
            //RagdollParams.StoreState();
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
                    DebugConsole.ThrowError(GetCharacterEditorTranslation("HumanoidLimbDeletionDisabled"));
                    break;
                }
                var limb = selectedLimbs[i];
                //if (limb == character.AnimController.MainLimb)
                //{
                //    // TODO: this should be possible now -> test
                //    DebugConsole.ThrowError("Can't remove the main limb, because it will crash the game.");
                //    continue;
                //}
                removedIDs.Add(limb.limbParams.ID);
                limb.limbParams.Element.Remove();
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
            foreach (var jointParam in jointsToRemove)
            {
                jointParam.Element.Remove();
                RagdollParams.Joints.Remove(jointParam);
            }
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

        private List<string> vanillaCharacters;
        private List<string> VanillaCharacters
        {
            get
            {
                if (vanillaCharacters == null)
                {
                    vanillaCharacters = GameMain.VanillaContent?.GetFilesOfType(ContentType.Character).ToList();
                }
                return vanillaCharacters;
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
            characterIndex = AllFiles.IndexOf(Character.GetConfigFile(character.SpeciesName));
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

        private Character SpawnCharacter(string configFile, RagdollParams ragdoll = null)
        {
            DebugConsole.NewMessage(GetCharacterEditorTranslation("TryingToSpawnCharacter").Replace("[config]", configFile.ToString()), Color.HotPink);
            OnPreSpawn();
            bool dontFollowCursor = true;
            if (character != null)
            {
                dontFollowCursor = character.dontFollowCursor;
                RagdollParams.ClearHistory();
                CurrentAnimation.ClearHistory();
                if (!character.Removed)
                {
                    character.Remove();
                }
                character = null;
            }
            if (configFile == Character.HumanConfigFile && selectedJob != null)
            {
                var characterInfo = new CharacterInfo(configFile, jobPrefab: JobPrefab.List.First(job => job.Identifier == selectedJob));
                character = Character.Create(configFile, spawnPosition, ToolBox.RandomSeed(8), characterInfo, hasAi: false, ragdoll: ragdoll);
                character.GiveJobItems();
                HideWearables();
                if (displayWearables)
                {
                    ShowWearables();
                }
                selectedJob = characterInfo.Job.Prefab.Identifier;
            }
            else
            {
                character = Character.Create(configFile, spawnPosition, ToolBox.RandomSeed(8), hasAi: false, ragdoll: ragdoll);
                selectedJob = null;
            }
            if (character != null)
            {
                character.dontFollowCursor = dontFollowCursor;
            }
            if (character == null)
            {
                if (currentCharacterConfig == configFile)
                {
                    return null;
                }
                else
                {
                    // Respawn the current character;
                    SpawnCharacter(currentCharacterConfig);
                }
            }
            OnPostSpawn();
            return character;
        }

        private void OnPreSpawn()
        {
            cameraOffset = Vector2.Zero;
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
            animationResetRequiresForceLoading = false;
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
            limbPairEditing = character.IsHumanoid;
            CreateGUI();
            ClearWidgets();
            ClearSelection();
            ResetParamsEditor();
            CurrentAnimation.CreateSnapshot();
            RagdollParams.CreateSnapshot();
            Cam.Position = character.WorldPosition;
        }

        private void ClearWidgets()
        {
            Widget.selectedWidgets.Clear();
            animationWidgets.Clear();
            jointSelectionWidgets.Clear();
            limbEditWidgets.Clear();
        }

        private void ClearSelection()
        {
            selectedLimbs.Clear();
            selectedJoints.Clear();
        }

        private void RecreateRagdoll(RagdollParams ragdoll = null)
        {
            character.AnimController.Recreate(ragdoll);
            TeleportTo(spawnPosition);
            // For some reason Enumerable.Contains() method does not find the match, threfore the conversion to a list.
            var selectedJointParams = selectedJoints.Select(j => j.jointParams).ToList();
            var selectedLimbParams = selectedLimbs.Select(l => l.limbParams).ToList();
            CreateTextures();
            ClearWidgets();
            ClearSelection();
            foreach (var joint in character.AnimController.LimbJoints)
            {
                if (selectedJointParams.Contains(joint.jointParams))
                {
                    selectedJoints.Add(joint);
                }
            }
            foreach (var limb in character.AnimController.Limbs)
            {
                if (selectedLimbParams.Contains(limb.limbParams))
                {
                    selectedLimbs.Add(limb);
                }
            }
            ResetParamsEditor();
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
            Cam.Position = character.WorldPosition;
        }

        private bool CreateCharacter(string name, string mainFolder, bool isHumanoid, string contentPackageName = null, params object[] ragdollConfig)
        {
            var vanilla = GameMain.VanillaContent;
            ContentPackage contentPackage = null;
            if (string.IsNullOrWhiteSpace(contentPackageName))
            {
                contentPackageName = null;
            }
            if (contentPackageName == null)
            {
#if DEBUG
                contentPackage = GameMain.Config.SelectedContentPackages.LastOrDefault();
#else
                contentPackage = GameMain.Config.SelectedContentPackages.LastOrDefault(cp => cp != vanilla);
#endif
            }
            else
            {
#if DEBUG
                contentPackage = ContentPackage.List.LastOrDefault(cp => cp.Name == contentPackageName);
#else
                contentPackage = ContentPackage.List.LastOrDefault(cp => cp != vanilla && cp.Name == contentPackageName);
#endif
            }
            if (contentPackage == null)
            {
                string modName = contentPackageName ?? "NewCharacterMod";
                if (ContentPackage.List.Any(cp => cp.Name == modName))
                {
                    string tempName = modName;
                    for (int i = 0; i < 100; i++)
                    {
                        tempName = modName + i.ToString();
                        if (ContentPackage.List.None(cp => cp.Name == tempName))
                        {
                            modName = tempName;
                            break;
                        }
                    }
                }
                contentPackage = ContentPackage.CreatePackage(modName, Path.Combine(ContentPackage.Folder, $"{modName}.xml"), false);
                ContentPackage.List.Add(contentPackage);
            }
            if (contentPackage == null)
            {
                // This should not be possible.
                DebugConsole.ThrowError(GetCharacterEditorTranslation("NoContentPackageSelected"));
                return false;
            }
            if (!GameMain.Config.SelectedContentPackages.Contains(contentPackage))
            {
                GameMain.Config.SelectedContentPackages.Add(contentPackage);
                GameMain.Config.SaveNewPlayerConfig();
            }
#if !DEBUG
            if (vanilla != null && contentPackage == vanilla)
            {
                GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), Color.Red, font: GUI.LargeFont);
                return false;
            }
#endif
            string speciesName = name;
            // Config file
            string configFilePath = Path.Combine(mainFolder, $"{speciesName}.xml").Replace(@"\", @"/");
            if (ContentPackage.GetFilesOfType(GameMain.SelectedPackages, ContentType.Character).Any(path => path.Contains(speciesName)))
            {
                GUI.AddMessage(GetCharacterEditorTranslation("ExistingCharacterFound"), Color.Red, font: GUI.LargeFont);
                // TODO: add a prompt: "Do you want to replace it?" + functionality
                return false;
            }

            // Create the config file
            XElement mainElement = new XElement("Character",
                new XAttribute("name", speciesName),
                new XAttribute("humanoid", isHumanoid),
                new XElement("ragdolls", new XAttribute("folder", Path.Combine(mainFolder, $"Ragdolls/").Replace(@"\", @"/"))),
                new XElement("animations", new XAttribute("folder", Path.Combine(mainFolder, $"Animations/").Replace(@"\", @"/"))),
                new XElement("health"),
                new XElement("ai"));

            XDocument doc = new XDocument(mainElement);
            if (!Directory.Exists(mainFolder))
            {
                Directory.CreateDirectory(mainFolder);
            }
            doc.Save(configFilePath);
            // Add to the selected content package
            contentPackage.AddFile(configFilePath, ContentType.Character);
            contentPackage.Save(contentPackage.Path);
            DebugConsole.NewMessage(GetCharacterEditorTranslation("ContentPackageSaved").Replace("[path]", contentPackage.Path));

            // Ragdoll
            string ragdollFolder = RagdollParams.GetFolder(speciesName);
            string ragdollPath = RagdollParams.GetDefaultFile(speciesName);
            RagdollParams ragdollParams = isHumanoid
                ? RagdollParams.CreateDefault<HumanRagdollParams>(ragdollPath, speciesName, ragdollConfig)
                : RagdollParams.CreateDefault<FishRagdollParams>(ragdollPath, speciesName, ragdollConfig) as RagdollParams;
            // Animations
            string animFolder = AnimationParams.GetFolder(speciesName);
            foreach (AnimationType animType in Enum.GetValues(typeof(AnimationType)))
            {
                switch (animType)
                {
                    case AnimationType.Walk:
                    case AnimationType.Run:
                        if (!ragdollParams.CanEnterSubmarine) { continue; }
                        break;
                    case AnimationType.SwimSlow:
                    case AnimationType.SwimFast:
                        break;
                    default: continue;
                }
                Type type = AnimationParams.GetParamTypeFromAnimType(animType, isHumanoid);
                string fullPath = AnimationParams.GetDefaultFile(speciesName, animType);
                AnimationParams.Create(fullPath, speciesName, animType, type);
            }
            if (!AllFiles.Contains(configFilePath))
            {
                AllFiles.Add(configFilePath);
            }
            SpawnCharacter(configFilePath, ragdollParams);
            return true;
        }

        private void ShowWearables()
        {
            foreach (var item in character.Inventory.Items)
            {
                if (item == null) { continue; }
                // Temp condition, todo: remove
                if (item.AllowedSlots.Contains(InvSlotType.Head) || item.AllowedSlots.Contains(InvSlotType.Headset)) { continue; }
                item.Equip(character);
            }
        }

        private void HideWearables()
        {
            character.Inventory.Items.ForEachMod(i => i?.Unequip(character));
        }
        #endregion

        #region GUI
        private static Point outerMargin = new Point(0, 0);
        private static Point innerMargin = new Point(40, 40);
        private static Color panelColor = new Color(20, 20, 20, 255);
        private static Color toggleButtonColor = new Color(0.4f, 0.4f, 0.4f, 1);

        private GUIFrame rightArea;
        private GUIFrame leftArea;
        private GUIFrame centerArea;

        private GUIFrame characterSelectionPanel;
        private GUIFrame fileEditPanel;
        private GUIFrame modesPanel;
        private GUIFrame miscToolsPanel;

        private GUIFrame ragdollControls;
        private GUIFrame animationControls;
        private GUIFrame limbControls;
        private GUIFrame spriteSheetControls;
        private GUIFrame backgroundColorPanel;

        private GUIDropDown animSelection;
        private GUITickBox freezeToggle;
        private GUITickBox animTestPoseToggle;
        private GUITickBox showCollidersToggle;
        private GUIScrollBar jointScaleBar;
        private GUIScrollBar limbScaleBar;
        private GUIScrollBar spriteSheetZoomBar;
        private GUITickBox copyJointsToggle;
        private GUITickBox jointsToggle;
        private GUITickBox editAnimsToggle;
        private GUITickBox editLimbsToggle;
        private GUITickBox paramsToggle;
        private GUITickBox spritesheetToggle;
        private GUITickBox ragdollToggle;
        private GUITickBox ikToggle;
        private GUIFrame extraRagdollControls;
        private GUIButton duplicateLimbButton;
        private GUIButton deleteSelectedButton;
        private GUIButton createJointButton;

        private ToggleButton modesToggle;
        private ToggleButton miscToolsToggle;
        private ToggleButton characterPanelToggle;
        private ToggleButton fileEditToggle;

        private void CreateGUI()
        {
            // Release the old areas
            if (rightArea != null)
            {
                rightArea.RectTransform.Parent = null;
            }
            if (centerArea != null)
            {
                centerArea.RectTransform.Parent = null;
            }
            if (leftArea != null)
            {
                leftArea.RectTransform.Parent = null;
            }

            // Create the areas
            rightArea = new GUIFrame(new RectTransform(new Vector2(0.15f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.CenterRight)
            {
                AbsoluteOffset = new Point(outerMargin.X, 0)
            }, style: null) { CanBeFocused = false };
            centerArea = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.TopRight)
            {
                AbsoluteOffset = new Point((int)(rightArea.RectTransform.ScaledSize.X + rightArea.RectTransform.RelativeOffset.X * rightArea.RectTransform.Parent.ScaledSize.X + 20), outerMargin.Y + 20)

            }, style: null)
            { CanBeFocused = false };
            leftArea = new GUIFrame(new RectTransform(new Vector2(0.2f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.CenterLeft)
            {
                AbsoluteOffset = new Point(outerMargin.X, 0)
            }, style: null)
            {
                CanBeFocused = false
            };

            Vector2 buttonSize = new Vector2(1, 0.04f);
            Vector2 toggleSize = new Vector2(0.03f, 0.03f);

            CreateCharacterSelectionPanel();
            CreateModesPanel(toggleSize);
            CreateFileEditPanel();
            CreateMiscToolsPanel(toggleSize);
            CreateContextualControls();
        }

        private void CreateModesPanel(Vector2 toggleSize)
        {
            modesPanel = new GUIFrame(new RectTransform(new Vector2(0.6f, 0.25f), leftArea.RectTransform, Anchor.BottomLeft), style: null, color: panelColor);
            var layoutGroup = new GUILayoutGroup(new RectTransform(new Point(modesPanel.Rect.Width - innerMargin.X, modesPanel.Rect.Height - innerMargin.Y),
                modesPanel.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 2,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(0.03f, 0.06f), layoutGroup.RectTransform), GetCharacterEditorTranslation("ModesPanel"), font: GUI.LargeFont);
            paramsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ShowParameters")) { Selected = showParamsEditor };
            spritesheetToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ShowSpriteSheet")) { Selected = showSpritesheet };
            ragdollToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ShowRagdoll")) { Selected = showRagdoll };
            showCollidersToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ShowColliders"))
            {
                Selected = showColliders,
                OnSelected = box =>
                {
                    showColliders = box.Selected;
                    return true;
                }
            };
            editAnimsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditAnimations")) { Selected = editAnimations };
            editLimbsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditLimbs")) { Selected = editLimbs };
            jointsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditJoints")) { Selected = editJoints };
            ikToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditIKTargets")) { Selected = editIK };
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
                    ClearSelection();
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
                    ClearSelection();
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
            modesToggle = new ToggleButton(new RectTransform(new Vector2(0.125f, 1), modesPanel.RectTransform, Anchor.CenterRight, Pivot.CenterLeft), Direction.Left);
        }

        private void CreateMiscToolsPanel(Vector2 toggleSize)
        {
            miscToolsPanel = new GUIFrame(new RectTransform(new Vector2(1, 0.25f), rightArea.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, -0.075f)
            }, style: null, color: panelColor);
            var layoutGroup = new GUILayoutGroup(new RectTransform(new Point(miscToolsPanel.Rect.Width - innerMargin.X, miscToolsPanel.Rect.Height - innerMargin.Y),
                miscToolsPanel.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 2,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(0.03f, 0.06f), layoutGroup.RectTransform), GetCharacterEditorTranslation("MiscToolsPanel"), font: GUI.LargeFont);
            freezeToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("Freeze")) { Selected = isFreezed };
            var autoFreezeToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("AutoFreeze")) { Selected = autoFreeze };
            var limbPairEditToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("LimbPairEditing"))
            {
                Selected = limbPairEditing,
                Enabled = character.IsHumanoid  // TODO: remove when limb pair editing works for non-humanoids
            };
            animTestPoseToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("AnimationTestPose"))
            {
                Selected = character.AnimController.AnimationTestPose,
                Enabled = character.IsHumanoid
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("AutoMove"))
            {
                Selected = character.OverrideMovement != null,
                OnSelected = box =>
                {
                    character.OverrideMovement = box.Selected ? new Vector2(1, 0) as Vector2? : null;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("FollowCursor"))
            {
                Selected = !character.dontFollowCursor,
                OnSelected = box =>
                {
                    character.dontFollowCursor = !box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditBackgroundColor"))
            {
                Selected = displayBackgroundColor,
                OnSelected = box =>
                {
                    displayBackgroundColor = box.Selected;
                    return true;
                }
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
            miscToolsToggle = new ToggleButton(new RectTransform(new Vector2(0.1f, 1), miscToolsPanel.RectTransform, Anchor.CenterLeft, Pivot.CenterRight), Direction.Right);
        }

        private void CreateContextualControls()
        {
            Point elementSize = new Point(120, 20);
            int textAreaHeight = 20;
            // General controls
            backgroundColorPanel = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerArea.RectTransform, Anchor.TopRight)
            {
                AbsoluteOffset = new Point(10, 0)
            }, style: null)
            {
                CanBeFocused = false
            };
            // Background color
            var frame = new GUIFrame(new RectTransform(new Point(500, 80), backgroundColorPanel.RectTransform, Anchor.TopRight), style: null, color: Color.Black * 0.4f);
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), frame.RectTransform) { MinSize = new Point(80, 26) }, GetCharacterEditorTranslation("BackgroundColor") + ":", textColor: Color.WhiteSmoke);
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
                var element = new GUIFrame(new RectTransform(new Vector2(0.3f, 1), inputArea.RectTransform)
                {
                    MinSize = new Point(40, 0),
                    MaxSize = new Point(100, 50)
                }, style: null, color: Color.Black * 0.6f);
                var colorLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), colorComponentLabels[i],
                    font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int, relativeButtonAreaWidth: 0.25f)
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
                        numberInput.OnValueChanged += (numInput) => backgroundColor.R = (byte)numInput.IntValue;
                        break;
                    case 1:
                        colorLabel.TextColor = Color.LightGreen;
                        numberInput.IntValue = backgroundColor.G;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.G = (byte)numInput.IntValue;
                        break;
                    case 2:
                        colorLabel.TextColor = Color.DeepSkyBlue;
                        numberInput.IntValue = backgroundColor.B;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.B = (byte)numInput.IntValue;
                        break;
                }
            }
            // Spritesheet controls
            spriteSheetControls = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerArea.RectTransform, Anchor.BottomLeft), style: null)
            {
                CanBeFocused = false
            };
            var layoutGroupSpriteSheet = new GUILayoutGroup(new RectTransform(Vector2.One, spriteSheetControls.RectTransform)) { AbsoluteSpacing = 5, CanBeFocused = false };
            new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), GetCharacterEditorTranslation("SpriteSheetZoom") + ":", Color.White);
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
            new GUIButton(new RectTransform(new Vector2(0.2f, 1), spriteSheetControlElement.RectTransform, Anchor.TopRight), GetCharacterEditorTranslation("Reset"))
            {
                OnClicked = (box, data) =>
                {
                    spriteSheetZoom = Math.Min(1, spriteSheetMaxZoom);
                    spriteSheetZoomBar.BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(spriteSheetMinZoom, spriteSheetMaxZoom, spriteSheetZoom));
                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), GetCharacterEditorTranslation("HideBodySprites"))
            {
                TextColor = Color.White,
                Selected = hideBodySheet,
                OnSelected = (GUITickBox box) =>
                {
                    hideBodySheet = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), GetCharacterEditorTranslation("ShowWearables"))
            {
                TextColor = Color.White,
                Selected = displayWearables,
                OnSelected = (GUITickBox box) =>
                {
                    displayWearables = box.Selected;
                    if (displayWearables)
                    {
                        ShowWearables();
                    }
                    else
                    {
                        HideWearables();
                    }
                    return true;
                }
            };
            //new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), "Texture scale:", Color.White);
            //new GUIScrollBar(new RectTransform(new Point((int)(elementSize.X * 1.75f), textAreaHeight), layoutGroupSpriteSheet.RectTransform), barSize: 0.2f)
            //{
            //    BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(textureMinScale, textureMaxScale, RagdollParams.TextureScale)),
            //    Step = 0.01f,
            //    OnMoved = (scrollBar, value) =>
            //    {
            //        RagdollParams.TextureScale = MathHelper.Lerp(textureMinScale, textureMaxScale, value);
            //        return true;
            //    }
            //};
            // Limb controls
            limbControls = new GUIFrame(new RectTransform(Vector2.One, centerArea.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupLimbControls = new GUILayoutGroup(new RectTransform(Vector2.One, limbControls.RectTransform), childAnchor: Anchor.TopLeft) { CanBeFocused = false };
            var lockSpriteOriginToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("LockSpriteOrigin"))
            {
                Selected = lockSpriteOrigin,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpriteOrigin = box.Selected;
                    return true;
                }
            };
            lockSpriteOriginToggle.TextColor = Color.White;
            var lockSpritePositionToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("LockSpritePosition"))
            {
                Selected = lockSpritePosition,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpritePosition = box.Selected;
                    return true;
                }
            };
            lockSpritePositionToggle.TextColor = Color.White;
            var lockSpriteSizeToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("LockSpriteSize"))
            {
                Selected = lockSpriteSize,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpriteSize = box.Selected;
                    return true;
                }
            };
            lockSpriteSizeToggle.TextColor = Color.White;
            var recalculateColliderToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("AdjustCollider"))
            {
                Selected = recalculateCollider,
                OnSelected = (GUITickBox box) =>
                {
                    recalculateCollider = box.Selected;
                    showCollidersToggle.Selected = recalculateCollider;
                    return true;
                }
            };
            recalculateColliderToggle.TextColor = Color.White;
            // Ragdoll
            Point sliderSize = new Point(300, 20);
            ragdollControls = new GUIFrame(new RectTransform(Vector2.One, centerArea.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupRagdoll = new GUILayoutGroup(new RectTransform(Vector2.One, ragdollControls.RectTransform), childAnchor: Anchor.TopLeft) { CanBeFocused = false };
            var uniformScalingToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupRagdoll.RectTransform), GetCharacterEditorTranslation("UniformScale"))
            {
                Selected = uniformScaling,
                OnSelected = (GUITickBox box) =>
                {
                    uniformScaling = box.Selected;
                    return true;
                }
            };
            uniformScalingToggle.TextColor = Color.White;
            copyJointsToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupRagdoll.RectTransform), GetCharacterEditorTranslation("CopyJointSettings"))
            {
                ToolTip = GetCharacterEditorTranslation("CopyJointSettingsTooltip"),
                Selected = copyJointSettings,
                TextColor = copyJointSettings ? Color.Red : Color.White,
                OnSelected = (GUITickBox box) =>
                {
                    copyJointSettings = box.Selected;
                    box.TextColor = copyJointSettings ? Color.Red : Color.White;
                    return true;
                }
            };
            // Spacing
            new GUIFrame(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null) { CanBeFocused = false };
            var jointScaleElement = new GUIFrame(new RectTransform(sliderSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var jointScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), jointScaleElement.RectTransform), $"{GetCharacterEditorTranslation("JointScale")}: {RagdollParams.JointScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
            var limbScaleElement = new GUIFrame(new RectTransform(sliderSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var limbScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), limbScaleElement.RectTransform), $"{GetCharacterEditorTranslation("LimbScale")}: {RagdollParams.LimbScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
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
                freezeToggle.Selected = false;
                TryUpdateRagdollParam("jointscale", value);
                jointScaleText.Text = $"{GetCharacterEditorTranslation("JointScale")}: {RagdollParams.JointScale.FormatDoubleDecimal()}";
                character.AnimController.ResetJoints();
            }
            void UpdateLimbScale(float value)
            {
                TryUpdateRagdollParam("limbscale", value);
                limbScaleText.Text = $"{GetCharacterEditorTranslation("LimbScale")}: {RagdollParams.LimbScale.FormatDoubleDecimal()}";
            }
            // TODO: doesn't trigger if the mouse is released while the cursor is outside the button rect
            limbScaleBar.Bar.OnClicked += (button, data) =>
            {
                RecreateRagdoll();
                RagdollParams.CreateSnapshot();
                ragdollResetRequiresForceLoading = true;
                return true;
            };
            jointScaleBar.Bar.OnClicked += (button, data) =>
            {
                if (uniformScaling)
                {
                    RecreateRagdoll();
                }
                RagdollParams.CreateSnapshot();
                ragdollResetRequiresForceLoading = true;
                return true;
            };

            // Ragdoll manipulation
            extraRagdollControls = new GUIFrame(new RectTransform(new Point(140, 30), centerArea.RectTransform, Anchor.BottomRight)
            {
                RelativeOffset = new Vector2(0.2f, 0.15f)
            }, style: null)
            {
                CanBeFocused = false
            };
            var extraRagdollLayout = new GUILayoutGroup(new RectTransform(Vector2.One, extraRagdollControls.RectTransform));
            duplicateLimbButton = new GUIButton(new RectTransform(new Point(140, 30), extraRagdollLayout.RectTransform), "Duplicate Limb")
            {
                OnClicked = (button, data) =>
                {
                    CopyLimb(selectedLimbs.FirstOrDefault());
                    return true;
                }
            };
            deleteSelectedButton = new GUIButton(new RectTransform(new Point(140, 30), extraRagdollLayout.RectTransform), "Delete Selected")
            {
                OnClicked = (button, data) =>
                {
                    DeleteSelected();
                    return true;
                }
            };
            createJointButton = new GUIButton(new RectTransform(new Point(140, 30), extraRagdollLayout.RectTransform), "Create Joint")
            {
                OnClicked = (button, data) =>
                {
                    jointCreationMode = !jointCreationMode;
                    useMouseOffset = false;
                    return true;
                }
            };

            // Animation
            animationControls = new GUIFrame(new RectTransform(Vector2.One, centerArea.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupAnimation = new GUILayoutGroup(new RectTransform(Vector2.One, animationControls.RectTransform), childAnchor: Anchor.TopLeft) { CanBeFocused = false };
            var animationSelectionElement = new GUIFrame(new RectTransform(new Point(elementSize.X * 2 - 5, elementSize.Y), layoutGroupAnimation.RectTransform), style: null);
            var animationSelectionText = new GUITextBlock(new RectTransform(new Point(elementSize.X, elementSize.Y), animationSelectionElement.RectTransform), GetCharacterEditorTranslation("SelectedAnimation") + ": ", Color.WhiteSmoke, textAlignment: Alignment.Center);
            animSelection = new GUIDropDown(new RectTransform(new Point(100, elementSize.Y), animationSelectionElement.RectTransform, Anchor.TopRight), elementCount: 4);
            if (character.AnimController.CanWalk)
            {
                animSelection.AddItem(AnimationType.Walk.ToString(), AnimationType.Walk);
                animSelection.AddItem(AnimationType.Run.ToString(), AnimationType.Run);
            }
            animSelection.AddItem(AnimationType.SwimSlow.ToString(), AnimationType.SwimSlow);
            animSelection.AddItem(AnimationType.SwimFast.ToString(), AnimationType.SwimFast);
            if (character.AnimController.ForceSelectAnimationType == AnimationType.NotDefined)
            {
                animSelection.SelectItem(character.AnimController.CanWalk ? AnimationType.Walk : AnimationType.SwimSlow);
            }
            else
            {
                animSelection.SelectItem(character.AnimController.ForceSelectAnimationType);
            }
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

        private void CreateCharacterSelectionPanel()
        {
            characterSelectionPanel = new GUIFrame(new RectTransform(new Vector2(1, 0.25f), rightArea.RectTransform, Anchor.TopRight), style: null, color: panelColor);
            var padding = new GUIFrame(new RectTransform(new Point(characterSelectionPanel.Rect.Width - innerMargin.X, characterSelectionPanel.Rect.Height - innerMargin.Y),
                characterSelectionPanel.RectTransform, Anchor.Center), style: null)
            {
                CanBeFocused = false
            };

            // Disclaimer
            var disclaimerBtnHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), padding.RectTransform), style: null);
            var disclaimerBtn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.8f), disclaimerBtnHolder.RectTransform, Anchor.TopRight), style: "GUINotificationButton")
            {
                OnClicked = (btn, userdata) => { GameMain.Instance.ShowEditorDisclaimer(); return true; }
            };
            disclaimerBtn.RectTransform.MaxSize = new Point(disclaimerBtn.Rect.Height);

            // Character selection
            new GUITextBlock(new RectTransform(new Vector2(1, 0.2f), padding.RectTransform), GetCharacterEditorTranslation("CharacterPanel"), font: GUI.LargeFont);

            var characterDropDown = new GUIDropDown(new RectTransform(new Vector2(1, 0.2f), padding.RectTransform)
            {
                RelativeOffset = new Vector2(0, 0.2f)
            }, elementCount: 10, style: null);
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
                var jobDropDown = new GUIDropDown(new RectTransform(new Vector2(1, 0.15f), padding.RectTransform)
                {
                    RelativeOffset = new Vector2(0, 0.45f)
                }, elementCount: 7, style: null);
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
            var charButtons = new GUIFrame(new RectTransform(new Vector2(1, 0.25f), parent: padding.RectTransform, anchor: Anchor.BottomLeft), style: null);
            var prevCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), charButtons.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("PreviousCharacter"));
            prevCharacterButton.TextBlock.AutoScale = true;
            prevCharacterButton.OnClicked += (b, obj) =>
            {
                SpawnCharacter(GetPreviousConfigFile());
                return true;
            };
            var nextCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), charButtons.RectTransform, Anchor.TopRight), GetCharacterEditorTranslation("NextCharacter"));
            prevCharacterButton.TextBlock.AutoScale = true;
            nextCharacterButton.OnClicked += (b, obj) =>
            {
                SpawnCharacter(GetNextConfigFile());
                return true;
            };
            characterPanelToggle = new ToggleButton(new RectTransform(new Vector2(0.1f, 1), characterSelectionPanel.RectTransform, Anchor.CenterLeft, Pivot.CenterRight), Direction.Right);
        }

        private void CreateFileEditPanel()
        {
            Vector2 buttonSize = new Vector2(1, 0.04f);

            fileEditPanel = new GUIFrame(new RectTransform(new Vector2(1, 0.4f), rightArea.RectTransform, Anchor.BottomRight), style: null, color: panelColor);
            var layoutGroup = new GUILayoutGroup(new RectTransform(new Point(fileEditPanel.Rect.Width - innerMargin.X, fileEditPanel.Rect.Height - innerMargin.Y),
                fileEditPanel.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 1,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(0.03f, 0.06f), layoutGroup.RectTransform), GetCharacterEditorTranslation("FileEditPanel"), font: GUI.LargeFont);
            var quickSaveAnimButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("QuickSaveAnimations"));
            quickSaveAnimButton.OnClicked += (button, userData) =>
            {
#if !DEBUG
                if (VanillaCharacters != null && VanillaCharacters.Contains(currentCharacterConfig))
                {
                    GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), Color.Red, font: GUI.LargeFont);
                    return false;
                }
#endif
                AnimParams.ForEach(p => p.Save());
                animationResetRequiresForceLoading = true;
                GUI.AddMessage(GetCharacterEditorTranslation("AllAnimationsSaved"), Color.Green, font: GUI.Font);
                return true;
            };
            var quickSaveRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("QuickSaveRagdoll"));
            quickSaveRagdollButton.OnClicked += (button, userData) =>
            {
#if !DEBUG
                if (VanillaCharacters != null && VanillaCharacters.Contains(currentCharacterConfig))
                {
                    GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), Color.Red, font: GUI.LargeFont);
                    return false;
                }
#endif
                character.AnimController.SaveRagdoll();
                ragdollResetRequiresForceLoading = true;
                GUI.AddMessage(GetCharacterEditorTranslation("RagdollSavedTo").Replace("[path]", RagdollParams.FullPath), Color.Green, font: GUI.Font);
                return true;
            };
            var resetAnimButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ResetAnimations"));
            resetAnimButton.OnClicked += (button, userData) =>
            {
                AnimParams.ForEach(p => p.Reset(true));
                ResetParamsEditor();
                GUI.AddMessage(GetCharacterEditorTranslation("AllAnimationsReset"), Color.WhiteSmoke, font: GUI.Font);
                animationResetRequiresForceLoading = false;
                return true;
            };
            var resetRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ResetRagdoll"));
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
                    // For some reason Enumerable.Contains() method does not find the match, threfore the conversion to a list.
                    var selectedJointParams = selectedJoints.Select(j => j.jointParams).ToList();
                    var selectedLimbParams = selectedLimbs.Select(l => l.limbParams).ToList();
                    ClearWidgets();
                    ClearSelection();
                    foreach (var joint in character.AnimController.LimbJoints)
                    {
                        if (selectedJointParams.Contains(joint.jointParams))
                        {
                            selectedJoints.Add(joint);
                        }
                    }
                    foreach (var limb in character.AnimController.Limbs)
                    {
                        if (selectedLimbParams.Contains(limb.limbParams))
                        {
                            selectedLimbs.Add(limb);
                        }
                    }
                    ResetParamsEditor();
                }
                jointCreationMode = false;
                closestSelectedLimb = null;
                CreateGUI();
                GUI.AddMessage(GetCharacterEditorTranslation("RagdollReset"), Color.WhiteSmoke, font: GUI.Font);
                return true;
            };
            Vector2 messageBoxRelSize = new Vector2(0.5f, 0.5f);
            int messageBoxWidth = GameMain.GraphicsWidth / 2;
            int messageBoxHeight = GameMain.GraphicsHeight / 2;
            var saveRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("SaveRagdoll"));
            saveRagdollButton.OnClicked += (button, userData) =>
            {
                var box = new GUIMessageBox(GetCharacterEditorTranslation("SaveRagdoll"), $"{GetCharacterEditorTranslation("ProvideFileName")}: ", new string[] { TextManager.Get("Cancel"), TextManager.Get("Save") }, messageBoxRelSize);
                var inputField = new GUITextBox(new RectTransform(new Point(box.Content.Rect.Width, 30), box.Content.RectTransform, Anchor.Center), RagdollParams.Name);
                box.Buttons[0].OnClicked += (b, d) =>
                {
                    box.Close();
                    return true;
                };
                box.Buttons[1].OnClicked += (b, d) =>
                {
#if !DEBUG
                    if (VanillaCharacters != null && VanillaCharacters.Contains(currentCharacterConfig))
                    {
                        GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), Color.Red, font: GUI.LargeFont);
                        box.Close();
                        return false;
                    }
#endif
                    character.AnimController.SaveRagdoll(inputField.Text);
                    ragdollResetRequiresForceLoading = true;
                    GUI.AddMessage(GetCharacterEditorTranslation("RagdollSavedTo").Replace("[path]", RagdollParams.FullPath), Color.Green, font: GUI.Font);
                    box.Close();
                    return true;
                };
                return true;
            };
            var loadRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("LoadRagdoll"));
            loadRagdollButton.OnClicked += (button, userData) =>
            {
                var loadBox = new GUIMessageBox(GetCharacterEditorTranslation("LoadRagdoll"), "", new string[] { TextManager.Get("Cancel"), TextManager.Get("Load"), TextManager.Get("Delete") }, messageBoxRelSize);
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
                        DebugConsole.ThrowError(GetCharacterEditorTranslation("CouldntOpenDirectory").Replace("[folder]", RagdollParams.Folder), e);
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
                        new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
                    msgBox.Buttons[0].OnClicked += (b, d) =>
                    {
                        try
                        {
                            File.Delete(selectedFile);
                            GUI.AddMessage(GetCharacterEditorTranslation("RagdollDeletedFrom").Replace("[file]", selectedFile), Color.Red, font: GUI.Font);
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
                    ragdoll.Reset(true);
                    GUI.AddMessage(GetCharacterEditorTranslation("RagdollLoadedFrom").Replace("[file]", selectedFile), Color.WhiteSmoke, font: GUI.Font);
                    RecreateRagdoll(ragdoll);
                    CreateContextualControls();
                    loadBox.Close();
                    return true;
                };
                return true;
            };
            var saveAnimationButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("SaveAnimation"));
            saveAnimationButton.OnClicked += (button, userData) =>
            {
                var box = new GUIMessageBox(GetCharacterEditorTranslation("SaveAnimation"), string.Empty, new string[] { TextManager.Get("Cancel"), TextManager.Get("Save") }, messageBoxRelSize);
                var textArea = new GUIFrame(new RectTransform(new Vector2(1, 0.1f), box.Content.RectTransform) { MinSize = new Point(350, 30) }, style: null);
                var inputLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), textArea.RectTransform) { MinSize = new Point(250, 30) }, $"{GetCharacterEditorTranslation("ProvideFileName")}: ");
                var inputField = new GUITextBox(new RectTransform(new Vector2(0.5f, 1), textArea.RectTransform, Anchor.TopRight) { MinSize = new Point(100, 30) }, CurrentAnimation.Name);
                // Type filtering
                var typeSelectionArea = new GUIFrame(new RectTransform(new Vector2(1f, 0.1f), box.Content.RectTransform) { MinSize = new Point(0, 30) }, style: null);
                var typeLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), typeSelectionArea.RectTransform, Anchor.TopCenter, Pivot.TopRight), $"{GetCharacterEditorTranslation("SelectAnimationType")}: ");
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
#if !DEBUG
                    if (VanillaCharacters != null && VanillaCharacters.Contains(currentCharacterConfig))
                    {
                        GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), Color.Red, font: GUI.LargeFont);
                        box.Close();
                        return false;
                    }
#endif
                    var animParams = character.AnimController.GetAnimationParamsFromType(selectedType);
                    animParams.Save(inputField.Text);
                    animationResetRequiresForceLoading = true;
                    GUI.AddMessage(GetCharacterEditorTranslation("AnimationOfTypeSavedTo").Replace("[type]", animParams.AnimationType.ToString()).Replace("[path]", animParams.FullPath), Color.Green, font: GUI.Font);
                    ResetParamsEditor();
                    box.Close();
                    return true;
                };
                return true;
            };
            var loadAnimationButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("LoadAnimation"));
            loadAnimationButton.OnClicked += (button, userData) =>
            {
                var loadBox = new GUIMessageBox(GetCharacterEditorTranslation("LoadAnimation"), "", new string[] { TextManager.Get("Cancel"), TextManager.Get("Load"), TextManager.Get("Delete") }, messageBoxRelSize);
                loadBox.Buttons[0].OnClicked += loadBox.Close;
                var listBox = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.6f), loadBox.Content.RectTransform));
                var deleteButton = loadBox.Buttons[2];
                deleteButton.Enabled = false;
                // Type filtering
                var typeSelectionArea = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.1f), loadBox.Content.RectTransform) { MinSize = new Point(0, 30) }, style: null);
                var typeLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), typeSelectionArea.RectTransform, Anchor.TopCenter, Pivot.TopRight), $"{GetCharacterEditorTranslation("SelectAnimationType")}: ");
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
                        DebugConsole.ThrowError(GetCharacterEditorTranslation("CouldntOpenDirectory").Replace("[folder]", CurrentAnimation.Folder), e);
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
                        new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
                    msgBox.Buttons[0].OnClicked += (b, d) =>
                    {
                        try
                        {
                            File.Delete(selectedFile);
                            GUI.AddMessage(GetCharacterEditorTranslation("AnimationOfTypeDeleted").Replace("[type]", selectedType.ToString()).Replace("[file]", selectedFile), Color.Red, font: GUI.Font);
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
                                DebugConsole.ThrowError(GetCharacterEditorTranslation("AnimationTypeNotImplemented").Replace("[type]", selectedType.ToString()));
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
                                DebugConsole.ThrowError(GetCharacterEditorTranslation("AnimationTypeNotImplemented").Replace("[type]", selectedType.ToString()));
                                break;
                        }
                    }
                    GUI.AddMessage(GetCharacterEditorTranslation("AnimationOfTypeLoaded").Replace("[type]", selectedType.ToString()).Replace("[file]", selectedFile), Color.WhiteSmoke, font: GUI.Font);
                    character.AnimController.AllAnimParams.ForEach(a => a.Reset(forceReload: true));
                    ResetParamsEditor();
                    loadBox.Close();
                    return true;
                };
                return true;
            };
            var reloadTexturesButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ReloadTextures"));
            reloadTexturesButton.OnClicked += (button, userData) =>
            {
                foreach (var limb in character.AnimController.Limbs)
                {
                    limb.ActiveSprite.ReloadTexture();
                    limb.WearingItems.ForEach(i => i.Sprite.ReloadTexture());
                    limb.OtherWearables.ForEach(w => w.Sprite.ReloadTexture());
                }
                CreateTextures();
                return true;
            };
            new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("RecreateRagdoll"))
            {
                ToolTip = GetCharacterEditorTranslation("RecreateRagdollTooltip"),
                OnClicked = (button, data) =>
                {
                    RecreateRagdoll();
                    character.AnimController.ResetLimbs();
                    return true;
                }
            };
            new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("CreateNewCharacter"))
            {
                OnClicked = (button, data) =>
                {
                    editLimbsToggle.Selected = false;
                    editAnimsToggle.Selected = false;
                    spritesheetToggle.Selected = false;
                    jointsToggle.Selected = false;
                    paramsToggle.Selected = false;
                    Wizard.Instance.SelectTab(Wizard.Tab.Character);
                    return true;
                }
            };

            fileEditToggle = new ToggleButton(new RectTransform(new Vector2(0.1f, 1), fileEditPanel.RectTransform, Anchor.CenterLeft, Pivot.CenterRight), Direction.Right);
        }
        #endregion

        #region ToggleButtons 

        private enum Direction
        {
            Left,
            Right
        }

        private class ToggleButton
        {
            public readonly Direction dir;
            public readonly GUIButton toggleButton;

            public float OpenState { get; private set; } = 1;

            private bool isHidden;
            public bool IsHidden
            {
                get { return isHidden; }
                set
                {
                    isHidden = value;
                    RefreshToggleButtonState();
                }
            }

            public ToggleButton(RectTransform rectT, Direction dir)
            {
                toggleButton = new GUIButton(rectT, style: "UIToggleButton")
                {
                    Color = toggleButtonColor,
                    OnClicked = (button, data) =>
                    {
                        IsHidden = !IsHidden;
                        return true;
                    }
                };
                this.dir = dir;
                RefreshToggleButtonState();
            }

            public void RefreshToggleButtonState()
            {
                foreach (GUIComponent child in toggleButton.Children)
                {
                    switch (dir)
                    {
                        case Direction.Right:
                            child.SpriteEffects = isHidden ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                            break;
                        case Direction.Left:
                            child.SpriteEffects = isHidden ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                            break;
                    }
                }
            }

            public void UpdateOpenState(float deltaTime, Vector2 hiddenPos, RectTransform panel)
            {
                panel.AbsoluteOffset = Vector2.SmoothStep(hiddenPos, Vector2.Zero, OpenState).ToPoint();
                OpenState = isHidden ? Math.Max(OpenState - deltaTime * 2, 0) : Math.Min(OpenState + deltaTime * 2, 1);
            }
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
            else
            {
                //if (selectedJoints.Any())
                //{
                //    foreach (var jointParams in RagdollParams.Joints)
                //    {
                //        if (selectedJoints.Any(j => j.jointParams == jointParams))
                //        {
                //            jointParams?.AddToEditor(ParamsEditor.Instance);
                //        }
                //    }
                //}
                //if (selectedLimbs.Any())
                //{
                //    foreach (var limbParams in RagdollParams.Limbs)
                //    {
                //        if (limbParams == null) { continue; }
                //        var selectedLimb = selectedLimbs.Find(l => l.limbParams == limbParams);
                //        if (selectedLimb != null)
                //        {
                //            limbParams.AddToEditor(ParamsEditor.Instance);
                //            if (selectedLimb.attack != null)
                //            {
                //                var attackEditor = new SerializableEntityEditor(ParamsEditor.Instance.EditorBox.Content.RectTransform, selectedLimb.attack, false, true);
                //            }
                //        }
                //    }
                //}
                foreach (var joint in selectedJoints)
                {
                    joint.jointParams.AddToEditor(ParamsEditor.Instance);
                }
                foreach (var limb in selectedLimbs)
                {
                    limb.limbParams.AddToEditor(ParamsEditor.Instance);
                    if (limb.attack != null)
                    {
                        new SerializableEntityEditor(ParamsEditor.Instance.EditorBox.Content.RectTransform, limb.attack, inGame: false, showName: true);
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
                    DebugConsole.ThrowError(GetCharacterEditorTranslation("NoFieldForParameterFound").Replace("[parameter]", name));
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

        private bool IsMatchingLimb(Limb limb1, Limb limb2, LimbJoint joint1, LimbJoint joint2) =>
            joint1.BodyA == limb1.body.FarseerBody && joint2.BodyA == limb2.body.FarseerBody ||
            joint1.BodyB == limb1.body.FarseerBody && joint2.BodyB == limb2.body.FarseerBody;

        private void ValidateJoint(LimbJoint limbJoint)
        {
            if (limbJoint.UpperLimit < limbJoint.LowerLimit)
            {
                if (limbJoint.LowerLimit > 0.0f)
                {
                    limbJoint.LowerLimit -= MathHelper.TwoPi;
                }
                if (limbJoint.UpperLimit < 0.0f)
                {
                    limbJoint.UpperLimit += MathHelper.TwoPi;
                }
            }

            if (limbJoint.UpperLimit - limbJoint.LowerLimit > MathHelper.TwoPi)
            {
                limbJoint.LowerLimit = MathUtils.WrapAnglePi(limbJoint.LowerLimit);
                limbJoint.UpperLimit = MathUtils.WrapAnglePi(limbJoint.UpperLimit);
            }
        }

        private Limb GetClosestLimbOnRagdoll(Vector2 targetPos, Func<Limb, bool> filter = null)
        {
            Limb closestLimb = null;
            float closestDistance = float.MaxValue;
            foreach (Limb l in character.AnimController.Limbs) 
            {
                if (filter == null ? true : filter(l)) 
                {
                    float distance = Vector2.DistanceSquared(SimToScreen(l.SimPosition), targetPos);
                    if (distance < closestDistance) 
                    {
                        closestLimb = l;
                        closestDistance = distance;
                    }
                }
            }
            return closestLimb;
        }

        private Limb GetClosestLimbOnSpritesheet(Vector2 targetPos, Func<Limb, bool> filter = null)
        {
            Limb closestLimb = null;
            float closestDistance = float.MaxValue;
            foreach (Limb l in character.AnimController.Limbs) 
            {
                if (l == null) { continue; }
                if (filter == null ? true : filter(l)) 
                {
                    float distance = Vector2.DistanceSquared(GetLimbSpritesheetRect(l).Center.ToVector2(), targetPos);
                    if (distance < closestDistance) 
                    {
                        closestLimb = l;
                        closestDistance = distance;
                    }
                }
            }
            return closestLimb;
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
                    break;
                }
            }
            return rect;
        }

        // TODO: refactor this so that it can be used in all cases
        private void UpdateSourceRect(Limb limb, Rectangle newRect)
        {
            limb.ActiveSprite.SourceRect = newRect;
            if (limb.DamagedSprite != null)
            {
                limb.DamagedSprite.SourceRect = limb.ActiveSprite.SourceRect;
            }
            RecalculateOrigin(limb);
            TryUpdateLimbParam(limb, "sourcerect", newRect);
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
                    RecalculateOrigin(otherLimb);
                });
            };

            void RecalculateOrigin(Limb l)
            {
                // Keeps the relative origin unchanged. The absolute origin will be recalculated.
                l.ActiveSprite.RelativeOrigin = l.ActiveSprite.RelativeOrigin;

                // TODO:
                //if (lockSpriteOrigin)
                //{
                //    // Keeps the absolute origin unchanged. The relative origin will be recalculated.
                //    var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(l));
                //    l.ActiveSprite.Origin = (originWidget.DrawPos - spritePos - l.ActiveSprite.SourceRect.Location.ToVector2() * spriteSheetZoom) / spriteSheetZoom;
                //    TryUpdateLimbParam(l, "origin", l.ActiveSprite.RelativeOrigin);
                //}
                //else
                //{
                //    // Keeps the relative origin unchanged. The absolute origin will be recalculated.
                //    l.ActiveSprite.RelativeOrigin = l.ActiveSprite.RelativeOrigin;
                //}
            }
        }

        private void DrawJointCreationOnSpritesheet(SpriteBatch spriteBatch, Vector2 startPos)
        {
            // Spritesheet
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 200, GameMain.GraphicsHeight - 200), GetCharacterEditorTranslation("SelectTargetLimbForJointEnd"), Color.White, Color.Black * 0.5f, 10, GUI.Font);
            GUI.DrawLine(spriteBatch, startPos, PlayerInput.MousePosition, Color.LightGreen, width: 3);
            if (targetLimb != null && targetLimb.ActiveSprite != null)
            {
                GUI.DrawRectangle(spriteBatch, GetLimbSpritesheetRect(targetLimb), Color.LightGreen, thickness: 3);
            }
        }

        private void DrawJointCreationOnRagdoll(SpriteBatch spriteBatch, Vector2 startPos)
        {
            // Ragdoll
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 200, GameMain.GraphicsHeight - 200), GetCharacterEditorTranslation("SelectTargetLimbForJointEnd"), Color.White, Color.Black * 0.5f, 10, GUI.Font);
            GUI.DrawLine(spriteBatch, startPos, PlayerInput.MousePosition, Color.LightGreen, width: 3);
            if (targetLimb != null && targetLimb.ActiveSprite != null)
            {
                var sourceRect = targetLimb.ActiveSprite.SourceRect;
                Vector2 size = sourceRect.Size.ToVector2() * Cam.Zoom * targetLimb.Scale * targetLimb.TextureScale;
                Vector2 up = VectorExtensions.BackwardFlipped(targetLimb.Rotation);
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
            float margin = 20;
            if (textures == null || textures.None())
            {
                spriteSheetMaxZoom = 1;
            }
            else if (height > width)
            {
                spriteSheetMaxZoom = (centerArea.Rect.Bottom - spriteSheetOffsetY - margin) / height;
            }
            else
            {
                spriteSheetMaxZoom = (centerArea.Rect.Left - spriteSheetOffsetX - margin) / width;
            }
            spriteSheetMinZoom = spriteSheetMinZoom > spriteSheetMaxZoom ? spriteSheetMaxZoom : 0.25f;
            spriteSheetZoom = MathHelper.Clamp(1, spriteSheetMinZoom, spriteSheetMaxZoom);
        }

        private void HandleLimbSelection(Limb limb)
        {
            if (!selectedLimbs.Contains(limb))
            {
                if (!Widget.EnableMultiSelect)
                {
                    selectedLimbs.Clear();
                }
                selectedLimbs.Add(limb);
                ResetParamsEditor();
                //RagdollParams.StoreState();
            }
            else if (Widget.EnableMultiSelect)
            {
                selectedLimbs.Remove(limb);
                ResetParamsEditor();
            }
        }

        private void OpenDoors()
        {
            foreach (var item in Item.ItemList)
            {
                foreach (var component in item.Components)
                {
                    if (component is Items.Components.Door door)
                    {
                        door.IsOpen = true;
                    }
                }
            }
        }

        private void SaveSnapshot()
        {
            if (editJoints || editLimbs || editIK)
            {
                RagdollParams.CreateSnapshot();
            }
            if (editAnimations)
            {
                CurrentAnimation.CreateSnapshot();
            }
        }
        #endregion

        #region Animation Controls
        private void DrawAnimationControls(SpriteBatch spriteBatch, float deltaTime)
        {
            var collider = character.AnimController.Collider;
            var colliderDrawPos = SimToScreen(collider.SimPosition);
            var animParams = character.AnimController.CurrentAnimationParams;
            var groundedParams = animParams as GroundedMovementParams;
            var humanParams = animParams as IHumanAnimation;
            var humanGroundedParams = animParams as HumanGroundedParams;
            var humanSwimParams = animParams as HumanSwimParams;
            var fishParams = animParams as IFishAnimation;
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
            // Note: the main collider rotates only when swimming
            float dir = character.AnimController.Dir;
            Vector2 GetSimSpaceForward() => animParams.IsSwimAnimation ? Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation)) : Vector2.UnitX * character.AnimController.Dir;
            Vector2 GetScreenSpaceForward() => animParams.IsSwimAnimation ? VectorExtensions.BackwardFlipped(collider.Rotation, 1) : Vector2.UnitX * character.AnimController.Dir;
            bool ShowCycleWidget() => PlayerInput.KeyDown(Keys.LeftAlt) && (CurrentAnimation is IHumanAnimation || CurrentAnimation is GroundedMovementParams);
            if (!PlayerInput.KeyDown(Keys.LeftAlt) && (animParams is IHumanAnimation || animParams is GroundedMovementParams))
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 120, 100), GetCharacterEditorTranslation("HoldLeftAltToAdjustCycleSpeed"), Color.White, Color.Black * 0.5f, 10, GUI.Font);
            }
            // Widgets for all anims -->
            Vector2 referencePoint = SimToScreen(head != null ? head.SimPosition: collider.SimPosition);
            Vector2 drawPos = referencePoint;
            if (ShowCycleWidget())
            {
                GetAnimationWidget("CycleSpeed", Color.MediumPurple, size: 20, sizeMultiplier: 1.5f, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    float multiplier = 0.5f;
                    w.tooltip = GetCharacterEditorTranslation("CycleSpeed");
                    w.refresh = () =>
                    {
                        var refPoint = SimToScreen(head != null ? head.SimPosition : collider.SimPosition);
                        w.DrawPos = refPoint + GetScreenSpaceForward() * ConvertUnits.ToDisplayUnits(CurrentAnimation.CycleSpeed * multiplier) * Cam.Zoom;
                        // Update tooltip, because the cycle speed might be automatically adjusted by the movement speed widget.
                        w.tooltip = $"{GetCharacterEditorTranslation("CycleSpeed")}: {CurrentAnimation.CycleSpeed.FormatDoubleDecimal()}";
                    };
                    w.MouseHeld += dTime =>
                    {
                        // TODO: clamp so that cannot manipulate the local y axis -> remove the additional refresh callback in below
                        //Vector2 newPos = PlayerInput.MousePosition;
                        //w.DrawPos = newPos;
                        float speed = CurrentAnimation.CycleSpeed + ConvertUnits.ToSimUnits(Vector2.Multiply(PlayerInput.MouseSpeed / multiplier, GetScreenSpaceForward()).Combine()) / Cam.Zoom;
                        TryUpdateAnimParam("cyclespeed", speed);
                        w.tooltip = $"{GetCharacterEditorTranslation("CycleSpeed")}: {CurrentAnimation.CycleSpeed.FormatDoubleDecimal()}";
                    };
                    // Additional check, which overrides the previous value (because evaluated last)
                    w.PreUpdate += dTime =>
                    {
                        if (!ShowCycleWidget())
                        {
                            w.Enabled = false;
                        }
                    };
                    // Additional (remove if the position is updated when the mouse is held)
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
                            GUI.DrawLine(spriteBatch, w.DrawPos, SimToScreen(head != null ? head.SimPosition : collider.SimPosition), Color.MediumPurple);
                        }
                    };
                }).Draw(spriteBatch, deltaTime);
            }
            else
            {
                GetAnimationWidget("MovementSpeed", Color.Turquoise, size: 20, sizeMultiplier: 1.5f, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    float multiplier = 0.5f;
                    w.tooltip = GetCharacterEditorTranslation("MovementSpeed");
                    w.refresh = () =>
                    {
                        var refPoint = SimToScreen(head != null ? head.SimPosition : collider.SimPosition);
                        w.DrawPos = refPoint + GetScreenSpaceForward() * ConvertUnits.ToDisplayUnits(CurrentAnimation.MovementSpeed * multiplier) * Cam.Zoom;
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
                        w.tooltip = $"{GetCharacterEditorTranslation("MovementSpeed")}: {CurrentAnimation.MovementSpeed.FormatSingleDecimal()}";
                    };
                    // Additional check, which overrides the previous value (because evaluated last)
                    w.PreUpdate += dTime =>
                    {
                        if (ShowCycleWidget())
                        {
                            w.Enabled = false;
                        }
                    };
                    // Additional (remove if the position is updated when the mouse is held)
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
                            GUI.DrawLine(spriteBatch, w.DrawPos, SimToScreen(head != null ? head.SimPosition : collider.SimPosition), Color.Turquoise);
                        }
                    };
                }).Draw(spriteBatch, deltaTime);
            }

            if (head != null)
            {
                // Head angle
                DrawRadialWidget(spriteBatch, SimToScreen(head.SimPosition), animParams.HeadAngle, GetCharacterEditorTranslation("HeadAngle"), Color.White,
                    angle => TryUpdateAnimParam("headangle", angle), circleRadius: 25, rotationOffset: collider.Rotation + MathHelper.Pi, clockWise: dir < 0, wrapAnglePi: true);
                // Head position and leaning
                if (animParams.IsGroundedAnimation)
                {
                    if (humanGroundedParams != null && character.AnimController is HumanoidAnimController humanAnimController)
                    {
                        GetAnimationWidget("HeadPosition", Color.Red, initMethod: w =>
                        {
                            w.tooltip = GetCharacterEditorTranslation("Head");
                            w.refresh = () => w.DrawPos = SimToScreen(head.SimPosition.X + humanAnimController.HeadLeanAmount * character.AnimController.Dir, head.PullJointWorldAnchorB.Y);
                            bool isHorizontal = false;
                            bool isDirectionSet = false;
                            w.MouseDown += () => isDirectionSet = false;
                            w.MouseHeld += dTime =>
                            {
                                if (PlayerInput.MouseSpeed.NearlyEquals(Vector2.Zero)) { return; }
                                if (!isDirectionSet)
                                {
                                    isHorizontal = Math.Abs(PlayerInput.MouseSpeed.X) > Math.Abs(PlayerInput.MouseSpeed.Y);
                                    isDirectionSet = true;
                                }
                                var scaledInput = ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed) / Cam.Zoom;
                                if (PlayerInput.KeyDown(Keys.LeftAlt))
                                {
                                    if (isHorizontal)
                                    {
                                        TryUpdateAnimParam("headleanamount", humanGroundedParams.HeadLeanAmount + scaledInput.X * character.AnimController.Dir);
                                        w.refresh();
                                        w.DrawPos = new Vector2(PlayerInput.MousePosition.X, w.DrawPos.Y);
                                    }
                                    else
                                    {
                                        TryUpdateAnimParam("headposition", humanGroundedParams.HeadPosition - scaledInput.Y / RagdollParams.JointScale);
                                        w.refresh();
                                        w.DrawPos = new Vector2(w.DrawPos.X, PlayerInput.MousePosition.Y);
                                    }
                                }
                                else
                                {
                                    TryUpdateAnimParam("headleanamount", humanGroundedParams.HeadLeanAmount + scaledInput.X * character.AnimController.Dir);
                                    w.refresh();
                                    w.DrawPos = new Vector2(PlayerInput.MousePosition.X, w.DrawPos.Y);
                                    TryUpdateAnimParam("headposition", humanGroundedParams.HeadPosition - scaledInput.Y / RagdollParams.JointScale);
                                    w.refresh();
                                    w.DrawPos = new Vector2(w.DrawPos.X, PlayerInput.MousePosition.Y);
                                }
                            };
                            w.PostDraw += (sB, dTime) =>
                            {
                                if (w.IsControlled && isDirectionSet)
                                {
                                    if (PlayerInput.KeyDown(Keys.LeftAlt))
                                    {
                                        if (isHorizontal)
                                        {
                                            GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), Color.Red);
                                        }
                                        else
                                        {
                                            GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), Color.Red);
                                        }
                                    }
                                    else
                                    {
                                        GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), Color.Red);
                                        GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), Color.Red);
                                    }
                                }
                                else if (w.IsSelected)
                                {
                                    GUI.DrawLine(spriteBatch, w.DrawPos, SimToScreen(head.SimPosition), Color.Red);
                                }
                            };
                        }).Draw(spriteBatch, deltaTime);
                    }
                    else
                    {
                        GetAnimationWidget("HeadPosition", Color.Red, initMethod: w =>
                        {
                            w.tooltip = GetCharacterEditorTranslation("HeadPosition");
                            w.refresh = () => w.DrawPos = SimToScreen(head.SimPosition.X, head.PullJointWorldAnchorB.Y);
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = SimToScreen(head.SimPosition.X, head.PullJointWorldAnchorB.Y);
                                var scaledInput = ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed) / Cam.Zoom / RagdollParams.JointScale;
                                TryUpdateAnimParam("headposition", groundedParams.HeadPosition - scaledInput.Y);
                            };
                            w.PostDraw += (sB, dTime) =>
                            {
                                if (w.IsControlled)
                                {
                                    GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), Color.Red);
                                }
                            };
                        }).Draw(spriteBatch, deltaTime);
                    }
                }
            }
            if (torso != null)
            {
                referencePoint = torso.SimPosition;
                if (animParams is HumanGroundedParams || animParams is HumanSwimParams)
                {
                    var f = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation));
                    referencePoint -= f * 0.25f;
                }
                // Torso angle
                DrawRadialWidget(spriteBatch, SimToScreen(referencePoint), animParams.TorsoAngle, GetCharacterEditorTranslation("TorsoAngle"), Color.White,
                    angle => TryUpdateAnimParam("torsoangle", angle), rotationOffset: collider.Rotation + MathHelper.Pi, clockWise: dir < 0, wrapAnglePi: true);

                if (animParams.IsGroundedAnimation)
                {
                    // Torso position and leaning
                    if (humanGroundedParams != null && character.AnimController is HumanoidAnimController humanAnimController)
                    {
                        GetAnimationWidget("TorsoPosition", Color.DarkRed, initMethod: w =>
                        {
                            w.tooltip = GetCharacterEditorTranslation("Torso");
                            w.refresh = () => w.DrawPos = SimToScreen(torso.SimPosition.X +  humanAnimController.TorsoLeanAmount * character.AnimController.Dir, torso.PullJointWorldAnchorB.Y);
                            bool isHorizontal = false;
                            bool isDirectionSet = false;
                            w.MouseDown += () => isDirectionSet = false;
                            w.MouseHeld += dTime =>
                            {
                                if (PlayerInput.MouseSpeed.NearlyEquals(Vector2.Zero)) { return; }
                                if (!isDirectionSet)
                                {
                                    isHorizontal = Math.Abs(PlayerInput.MouseSpeed.X) > Math.Abs(PlayerInput.MouseSpeed.Y);
                                    isDirectionSet = true;
                                }
                                var scaledInput = ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed) / Cam.Zoom;
                                if (PlayerInput.KeyDown(Keys.LeftAlt))
                                {
                                    if (isHorizontal)
                                    {
                                        TryUpdateAnimParam("torsoleanamount", humanGroundedParams.TorsoLeanAmount + scaledInput.X * character.AnimController.Dir);
                                        w.refresh();
                                        w.DrawPos = new Vector2(PlayerInput.MousePosition.X, w.DrawPos.Y);
                                    }
                                    else
                                    {
                                        TryUpdateAnimParam("torsoposition", humanGroundedParams.TorsoPosition - scaledInput.Y / RagdollParams.JointScale);
                                        w.refresh();
                                        w.DrawPos = new Vector2(w.DrawPos.X, PlayerInput.MousePosition.Y);
                                    }
                                }
                                else
                                {
                                    TryUpdateAnimParam("torsoleanamount", humanGroundedParams.TorsoLeanAmount + scaledInput.X * character.AnimController.Dir);
                                    w.refresh();
                                    w.DrawPos = new Vector2(PlayerInput.MousePosition.X, w.DrawPos.Y);
                                    TryUpdateAnimParam("torsoposition", humanGroundedParams.TorsoPosition - scaledInput.Y / RagdollParams.JointScale);
                                    w.refresh();
                                    w.DrawPos = new Vector2(w.DrawPos.X, PlayerInput.MousePosition.Y);
                                }
                            };
                            w.PostDraw += (sB, dTime) =>
                            {
                                if (w.IsControlled && isDirectionSet)
                                {
                                    if (PlayerInput.KeyDown(Keys.LeftAlt))
                                    {
                                        if (isHorizontal)
                                        {
                                            GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), Color.DarkRed);
                                        }
                                        else
                                        {
                                            GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), Color.DarkRed);
                                        }
                                    }
                                    else
                                    {
                                        GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), Color.DarkRed);
                                        GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), Color.DarkRed);
                                    }
                                }
                                else if (w.IsSelected)
                                {
                                    GUI.DrawLine(spriteBatch, w.DrawPos, SimToScreen(torso.SimPosition), Color.DarkRed);
                                }
                            };
                        }).Draw(spriteBatch, deltaTime);
                    }
                    else
                    {
                        GetAnimationWidget("TorsoPosition", Color.DarkRed, initMethod: w =>
                        {
                            w.tooltip = GetCharacterEditorTranslation("TorsoPosition");
                            w.refresh = () => w.DrawPos = SimToScreen(torso.SimPosition.X, torso.PullJointWorldAnchorB.Y);
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = SimToScreen(torso.SimPosition.X, torso.PullJointWorldAnchorB.Y);
                                var scaledInput = ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed) / Cam.Zoom / RagdollParams.JointScale;
                                TryUpdateAnimParam("torsoposition", groundedParams.TorsoPosition - scaledInput.Y);
                            };
                            w.PostDraw += (sB, dTime) =>
                            {
                                if (w.IsControlled)
                                {
                                    GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), Color.DarkRed);
                                }
                            };
                        }).Draw(spriteBatch, deltaTime);
                    }
                }
            }
            // Tail angle
            if (tail != null && fishParams != null)
            {
                DrawRadialWidget(spriteBatch, SimToScreen(tail.SimPosition), fishParams.TailAngle, GetCharacterEditorTranslation("TailAngle"), Color.White,
                    angle => TryUpdateAnimParam("tailangle", angle), circleRadius: 25, rotationOffset: collider.Rotation + MathHelper.Pi, clockWise: dir < 0, wrapAnglePi: true);
            }
            // Foot angle
            if (foot != null)
            {
                if (fishParams != null)
                {
                    Vector2 colliderBottom = character.AnimController.GetColliderBottom();
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
                            GetCharacterEditorTranslation("FootAngle"), Color.White,
                            angle =>
                            {
                                fishParams.FootAnglesInRadians[limb.limbParams.ID] = MathHelper.ToRadians(angle);
                                TryUpdateAnimParam("footangles", fishParams.FootAngles);
                            },
                            circleRadius: 25, rotationOffset: collider.Rotation, clockWise: dir < 0, wrapAnglePi: true);
                    }
                }
                else if (humanParams != null)
                {
                    DrawRadialWidget(spriteBatch, SimToScreen(foot.SimPosition), humanParams.FootAngle, GetCharacterEditorTranslation("FootAngle"), Color.White,
                        angle => TryUpdateAnimParam("footangle", angle), circleRadius: 25, rotationOffset: collider.Rotation + MathHelper.Pi, clockWise: dir < 0, wrapAnglePi: true);
                }
                // Grounded only
                if (groundedParams != null)
                {
                    GetAnimationWidget("StepSize", Color.LimeGreen, initMethod: w =>
                    {
                        w.tooltip = GetCharacterEditorTranslation("StepSize");
                        w.refresh = () =>
                        {
                            var refPoint = SimToScreen(character.AnimController.GetColliderBottom());
                            var stepSize = ConvertUnits.ToDisplayUnits(character.AnimController.StepSize.Value);
                            w.DrawPos = refPoint + new Vector2(stepSize.X * character.AnimController.Dir, -stepSize.Y) * Cam.Zoom;
                        };
                        w.MouseHeld += dTime =>
                        {
                            w.DrawPos = PlayerInput.MousePosition;
                            var transformedInput = ConvertUnits.ToSimUnits(new Vector2(PlayerInput.MouseSpeed.X * character.AnimController.Dir, -PlayerInput.MouseSpeed.Y)) / Cam.Zoom / RagdollParams.JointScale;
                            TryUpdateAnimParam("stepsize", groundedParams.StepSize + transformedInput);
                            w.tooltip = $"{GetCharacterEditorTranslation("StepSize")}: {groundedParams.StepSize.FormatDoubleDecimal()}";
                        };
                        w.PostDraw += (sp, dTime) =>
                        {
                            if (w.IsSelected)
                            {
                                GUI.DrawLine(sp, w.DrawPos, SimToScreen(character.AnimController.GetColliderBottom()), Color.LimeGreen);
                            }
                        };
                    }).Draw(spriteBatch, deltaTime);
                }
            }
            // Human grounded only -->
            if (humanGroundedParams != null)
            {
                if (hand != null || arm != null)
                {
                    GetAnimationWidget("HandMoveAmount", Color.LightGreen, initMethod: w =>
                    {
                        w.tooltip = GetCharacterEditorTranslation("HandMoveAmount");
                        float offset = 0.1f;
                        w.refresh = () =>
                        {
                            var refPoint = SimToScreen(collider.SimPosition + GetSimSpaceForward() * offset);
                            var handMovement = ConvertUnits.ToDisplayUnits(humanGroundedParams.HandMoveAmount);
                            w.DrawPos = refPoint + new Vector2(handMovement.X * character.AnimController.Dir, handMovement.Y) * Cam.Zoom;
                        };
                        w.MouseHeld += dTime =>
                        {
                            w.DrawPos = PlayerInput.MousePosition;
                            var transformedInput = ConvertUnits.ToSimUnits(new Vector2(PlayerInput.MouseSpeed.X * character.AnimController.Dir, PlayerInput.MouseSpeed.Y) / Cam.Zoom);
                            TryUpdateAnimParam("handmoveamount", humanGroundedParams.HandMoveAmount + transformedInput);
                            w.tooltip = $"{GetCharacterEditorTranslation("HandMoveAmount")}: {humanGroundedParams.HandMoveAmount.FormatDoubleDecimal()}";
                        };
                        w.PostDraw += (sp, dTime) =>
                        {
                            if (w.IsSelected)
                            {
                                GUI.DrawLine(sp, w.DrawPos, SimToScreen(collider.SimPosition + GetSimSpaceForward() * offset), Color.LightGreen);
                            }
                        };
                    }).Draw(spriteBatch, deltaTime);
                }
            }
            // Fish swim only -->
            else if (tail != null && fishSwimParams != null)
            {
                float amplitudeMultiplier = 0.5f;
                float lengthMultiplier = 20;
                int points = 1000;
                float GetAmplitude() => ConvertUnits.ToDisplayUnits(fishSwimParams.WaveAmplitude) * Cam.Zoom / amplitudeMultiplier;
                float GetWaveLength() => ConvertUnits.ToDisplayUnits(fishSwimParams.WaveLength) * Cam.Zoom / lengthMultiplier;
                Vector2 GetRefPoint() => SimToScreen(collider.SimPosition) - GetScreenSpaceForward() * ConvertUnits.ToDisplayUnits(collider.radius) * 3 * Cam.Zoom;
                Vector2 GetDrawPos() => GetRefPoint() - GetScreenSpaceForward() * GetWaveLength();
                Vector2 GetDir() => GetRefPoint() - GetDrawPos();
                Vector2 GetStartPoint() => GetDrawPos() + GetDir() / 2;
                Vector2 GetControlPoint() => GetStartPoint() + GetScreenSpaceForward().Right() * character.AnimController.Dir * GetAmplitude();
                var lengthWidget = GetAnimationWidget("WaveLength", Color.NavajoWhite, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    w.tooltip = GetCharacterEditorTranslation("TailMovementSpeed");
                    w.refresh = () => w.DrawPos = GetDrawPos();
                    w.MouseHeld += dTime =>
                    {
                        float input = Vector2.Multiply(ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed), GetScreenSpaceForward()).Combine() / Cam.Zoom * lengthMultiplier;
                        TryUpdateAnimParam("wavelength", MathHelper.Clamp(fishSwimParams.WaveLength - input, 0, 150));
                    };
                    // Additional
                    w.PreDraw += (sp, dTime) =>
                    {
                        if (w.IsControlled)
                        {
                            w.refresh();
                        }
                    };
                });
                var amplitudeWidget = GetAnimationWidget("WaveAmplitude", Color.NavajoWhite, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    w.tooltip = GetCharacterEditorTranslation("TailMovementAmount");
                    w.refresh = () => w.DrawPos = GetControlPoint();
                    w.MouseHeld += dTime =>
                    {
                        float input = Vector2.Multiply(ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed), GetScreenSpaceForward().Right()).Combine() * character.AnimController.Dir / Cam.Zoom * amplitudeMultiplier;
                        TryUpdateAnimParam("waveamplitude", MathHelper.Clamp(fishSwimParams.WaveAmplitude + input, -4, 4));
                    };
                    // Additional
                    w.PreDraw += (sp, dTime) =>
                    {
                        if (w.IsControlled)
                        {
                            w.refresh();
                        }
                    };
                });
                if (lengthWidget.IsControlled || amplitudeWidget.IsControlled)
                {
                    GUI.DrawSineWithDots(spriteBatch, GetRefPoint(), -GetDir(), GetAmplitude(), GetWaveLength(), 5000, points, Color.NavajoWhite);
                }
                lengthWidget.Draw(spriteBatch, deltaTime);
                amplitudeWidget.Draw(spriteBatch, deltaTime);
            }
            // Human swim only -->
            else if (humanSwimParams != null)
            {
                // Legs
                float amplitudeMultiplier = 5;
                float lengthMultiplier = 5;
                int points = 1000;
                float GetAmplitude() => ConvertUnits.ToDisplayUnits(humanSwimParams.LegMoveAmount) * Cam.Zoom / amplitudeMultiplier;
                float GetWaveLength() => ConvertUnits.ToDisplayUnits(humanSwimParams.LegCycleLength) * Cam.Zoom / lengthMultiplier;
                Vector2 GetRefPoint() => SimToScreen(character.SimPosition - GetScreenSpaceForward() / 2);
                Vector2 GetDrawPos() => GetRefPoint() - GetScreenSpaceForward() * GetWaveLength();
                Vector2 GetDir() => GetRefPoint() - GetDrawPos();
                Vector2 GetStartPoint() => GetDrawPos() + GetDir() / 2;
                Vector2 GetControlPoint() => GetStartPoint() + GetScreenSpaceForward().Right() * character.AnimController.Dir * GetAmplitude();
                var lengthWidget = GetAnimationWidget("LegMovementSpeed", Color.NavajoWhite, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    w.tooltip = GetCharacterEditorTranslation("LegMovementSpeed");
                    w.refresh = () => w.DrawPos = GetDrawPos();
                    w.MouseHeld += dTime =>
                    {
                        float input = Vector2.Multiply(ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed), GetScreenSpaceForward()).Combine() / Cam.Zoom * lengthMultiplier;
                        TryUpdateAnimParam("legcyclelength", MathHelper.Clamp(humanSwimParams.LegCycleLength - input, 0, 20));
                    };
                    // Additional
                    w.PreDraw += (sp, dTime) =>
                    {
                        if (w.IsControlled)
                        {
                            w.refresh();
                        }
                    };
                });
                var amplitudeWidget = GetAnimationWidget("LegMovementAmount", Color.NavajoWhite, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    w.tooltip = GetCharacterEditorTranslation("LegMovementAmount");
                    w.refresh = () => w.DrawPos = GetControlPoint();
                    w.MouseHeld += dTime =>
                    {
                        float input = Vector2.Multiply(ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed), GetScreenSpaceForward().Right()).Combine() * character.AnimController.Dir / Cam.Zoom * amplitudeMultiplier;
                        TryUpdateAnimParam("legmoveamount", MathHelper.Clamp(humanSwimParams.LegMoveAmount + input, -2, 2));
                    };
                    // Additional
                    w.PreDraw += (sp, dTime) =>
                    {
                        if (w.IsControlled)
                        {
                            w.refresh();
                        }
                    };
                });
                if (lengthWidget.IsControlled || amplitudeWidget.IsControlled)
                {
                    GUI.DrawSineWithDots(spriteBatch, GetRefPoint(), -GetDir(), GetAmplitude(), GetWaveLength(), 5000, points, Color.NavajoWhite);
                }
                lengthWidget.Draw(spriteBatch, deltaTime);
                amplitudeWidget.Draw(spriteBatch, deltaTime);
                // Arms
                GetAnimationWidget("HandMoveAmount", Color.LightGreen, initMethod: w =>
                {
                    w.tooltip = GetCharacterEditorTranslation("HandMoveAmount");
                    float offset = 0.4f;
                    w.refresh = () =>
                    {
                        var refPoint = SimToScreen(collider.SimPosition + GetSimSpaceForward() * offset);
                        var handMovement = ConvertUnits.ToDisplayUnits(humanSwimParams.HandMoveAmount);
                        w.DrawPos = refPoint + new Vector2(handMovement.X * character.AnimController.Dir, handMovement.Y) * Cam.Zoom;
                    };
                    w.MouseHeld += dTime =>
                    {
                        w.DrawPos = PlayerInput.MousePosition;
                        Vector2 transformedInput = ConvertUnits.ToSimUnits(new Vector2(PlayerInput.MouseSpeed.X * character.AnimController.Dir, PlayerInput.MouseSpeed.Y)) / Cam.Zoom;
                        Vector2 handMovement = humanSwimParams.HandMoveAmount + transformedInput;
                        TryUpdateAnimParam("handmoveamount", handMovement);
                        TryUpdateAnimParam("handcyclespeed", handMovement.X * 4);
                        w.tooltip = $"{GetCharacterEditorTranslation("HandMoveAmount")}: {humanSwimParams.HandMoveAmount.FormatDoubleDecimal()}";
                    };
                    w.PostDraw += (sp, dTime) =>
                    {
                        if (w.IsSelected)
                        {
                            GUI.DrawLine(sp, w.DrawPos, SimToScreen(collider.SimPosition + GetSimSpaceForward() * offset), Color.LightGreen);
                        }
                    };
                }).Draw(spriteBatch, deltaTime);
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb.type == LimbType.LeftFoot || limb.type == LimbType.RightFoot)
                {
                    GUI.DrawRectangle(spriteBatch, SimToScreen(limb.DebugRefPos) - Vector2.One * 3, Vector2.One * 6, Color.White, isFilled: true);
                    GUI.DrawRectangle(spriteBatch, SimToScreen(limb.DebugTargetPos) - Vector2.One * 3, Vector2.One * 6, Color.LightGreen, isFilled: true);
                }
            }
        }
        #endregion

        #region Ragdoll
        private Vector2[] corners = new Vector2[4];
        private Vector2[] GetLimbPhysicRect(Limb limb)
        {
            Vector2 size = ConvertUnits.ToDisplayUnits(limb.body.GetSize()) * Cam.Zoom;
            Vector2 up = VectorExtensions.BackwardFlipped(limb.Rotation);
            Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
            corners = MathUtils.GetImaginaryRect(corners, up, limbScreenPos, size);
            return corners;
        }

        private void DrawLimbEditor(SpriteBatch spriteBatch)
        {
            float inputMultiplier = 0.5f;
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb == null || limb.ActiveSprite == null) { continue; }
                var origin = limb.ActiveSprite.Origin;
                var sourceRect = limb.ActiveSprite.SourceRect;
                Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
                bool isSelected = selectedLimbs.Contains(limb);
                corners = GetLimbPhysicRect(limb);
                if (isSelected)
                {
                    GUI.DrawRectangle(spriteBatch, corners, Color.White, thickness: 3);
                }
                if (GUI.MouseOn == null && Widget.selectedWidgets.None() && !spriteSheetRect.Contains(PlayerInput.MousePosition) && MathUtils.RectangleContainsPoint(corners, PlayerInput.MousePosition))
                {
                    if (isSelected)
                    {
                        // Origin
                        if (!lockSpriteOrigin && PlayerInput.LeftButtonHeld())
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
                    }
                    else
                    {
                        GUI.DrawRectangle(spriteBatch, corners, Color.White);
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
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 200, 100), GetCharacterEditorTranslation("HoldLeftAltToManipulateJoint"), Color.White, Color.Black * 0.5f, 10, GUI.Font);
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
                            // Is the direction inversed incorrectly?
                            Vector2 to = tformedJointPos + VectorExtensions.ForwardFlipped(joint.LimbB.Rotation + MathHelper.ToRadians(-RagdollParams.SpritesheetOrientation), 20);
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
                                input = input.TransformVector(VectorExtensions.ForwardFlipped(limb.Rotation));
                                if (joint.BodyA == limb.body.FarseerBody)
                                {
                                    joint.LocalAnchorA += input;
                                    Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / RagdollParams.JointScale);
                                    TryUpdateJointParam(joint, "limb1anchor", transformedValue);
                                    // Snap all selected joints to the first selected
                                    if (copyJointSettings)
                                    {
                                        foreach (var j in selectedJoints)
                                        {
                                            j.LocalAnchorA = joint.LocalAnchorA;
                                            TryUpdateJointParam(j, "limb1anchor", transformedValue);
                                        }
                                    }
                                }
                                else if (joint.BodyB == limb.body.FarseerBody)
                                {
                                    joint.LocalAnchorB += input;
                                    Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / RagdollParams.JointScale);
                                    TryUpdateJointParam(joint, "limb2anchor", transformedValue);
                                    // Snap all selected joints to the first selected
                                    if (copyJointSettings)
                                    {
                                        foreach (var j in selectedJoints)
                                        {
                                            j.LocalAnchorB = joint.LocalAnchorB;
                                            TryUpdateJointParam(j, "limb2anchor", transformedValue);
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
                                            TryUpdateJointParam(otherJoint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / RagdollParams.JointScale));
                                        }
                                        else if (joint.BodyB == limb.body.FarseerBody && otherJoint.BodyB == otherLimb.body.FarseerBody)
                                        {
                                            otherJoint.LocalAnchorB = joint.LocalAnchorB;
                                            TryUpdateJointParam(otherJoint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / RagdollParams.JointScale));
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

        private void CalculateSpritesheetPosition()
        {
            //spriteSheetOffsetX = (int)(GameMain.GraphicsWidth * 0.6f);
            spriteSheetOffsetX = 20;
        }

        private void DrawSpritesheetEditor(SpriteBatch spriteBatch, float deltaTime)
        {
            int offsetX = spriteSheetOffsetX;
            int offsetY = spriteSheetOffsetY;
            for (int i = 0; i < Textures.Count; i++)
            {
                var texture = Textures[i];
                if (!hideBodySheet)
                {
                    spriteBatch.Draw(texture,
                        position: new Vector2(offsetX, offsetY),
                        rotation: 0,
                        origin: Vector2.Zero,
                        sourceRectangle: null,
                        scale: spriteSheetZoom,
                        effects: SpriteEffects.None,
                        color: Color.White,
                        layerDepth: 0);
                }
                GUI.DrawRectangle(spriteBatch, new Vector2(offsetX, offsetY), texture.Bounds.Size.ToVector2() * spriteSheetZoom, Color.White);
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (limb.ActiveSprite == null || limb.ActiveSprite.FilePath != texturePaths[i]) continue;
                    Rectangle rect = limb.ActiveSprite.SourceRect;
                    rect.Size = rect.MultiplySize(spriteSheetZoom);
                    rect.Location = rect.Location.Multiply(spriteSheetZoom);
                    rect.X += offsetX;
                    rect.Y += offsetY;
                    Vector2 origin = limb.ActiveSprite.Origin;
                    Vector2 limbScreenPos = new Vector2(rect.X + origin.X * spriteSheetZoom, rect.Y + origin.Y * spriteSheetZoom);
                    // Draw the clothes
                    foreach (var wearable in limb.WearingItems)
                    {
                        Vector2 orig = limb.ActiveSprite.Origin;
                        if (!wearable.InheritOrigin)
                        {
                            orig = wearable.Sprite.Origin;
                            // If the wearable inherits the origin, flipping is already handled.
                            if (limb.body.Dir == -1.0f)
                            {
                                orig.X = wearable.Sprite.SourceRect.Width - orig.X;
                            }
                        }
                        spriteBatch.Draw(wearable.Sprite.Texture,
                            position: limbScreenPos,
                            rotation: 0,
                            origin: orig,
                            sourceRectangle: wearable.InheritSourceRect ? limb.ActiveSprite.SourceRect : wearable.Sprite.SourceRect,
                            scale: (wearable.InheritTextureScale ? 1 : 1 / RagdollParams.TextureScale) * spriteSheetZoom,
                            effects: SpriteEffects.None,
                            color: Color.White,
                            layerDepth: 0);
                    }
                    GUI.DrawRectangle(spriteBatch, rect, selectedLimbs.Contains(limb) ? Color.Yellow : Color.Red);
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
                        int widgetSize = 8;
                        int halfSize = widgetSize / 2;
                        Vector2 stringOffset = new Vector2(5, 14);
                        var topLeft = rect.Location.ToVector2();
                        var topRight = new Vector2(topLeft.X + rect.Width, topLeft.Y);
                        var bottomRight = new Vector2(topRight.X, topRight.Y + rect.Height);
                        if (selectedLimbs.Contains(limb))
                        {
                            var sprite = limb.ActiveSprite;
                            Vector2 GetTopLeft() => sprite.SourceRect.Location.ToVector2();
                            Vector2 GetTopRight() => new Vector2(GetTopLeft().X + sprite.SourceRect.Width, GetTopLeft().Y);
                            Vector2 GetBottomRight() => new Vector2(GetTopRight().X, GetTopRight().Y + sprite.SourceRect.Height);
                            var originWidget = GetLimbEditWidget($"{limb.limbParams.ID}_origin", limb, widgetSize, Widget.Shape.Cross, initMethod: w =>
                            {
                                w.refresh = () => w.tooltip = $"{GetCharacterEditorTranslation("Origin")}: {sprite.RelativeOrigin.FormatDoubleDecimal()}";
                                w.refresh();
                                w.MouseHeld += dTime =>
                                {
                                    var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(limb));
                                    w.DrawPos = PlayerInput.MousePosition.Clamp(spritePos + GetTopLeft() * spriteSheetZoom, spritePos + GetBottomRight() * spriteSheetZoom);
                                    sprite.Origin = (w.DrawPos - spritePos - sprite.SourceRect.Location.ToVector2() * spriteSheetZoom) / spriteSheetZoom;
                                    if (limb.DamagedSprite != null)
                                    {
                                        limb.DamagedSprite.RelativeOrigin = sprite.RelativeOrigin;
                                    }
                                    TryUpdateLimbParam(limb, "origin", sprite.RelativeOrigin);
                                    if (limbPairEditing)
                                    {
                                        UpdateOtherLimbs(limb, otherLimb =>
                                        {
                                            otherLimb.ActiveSprite.RelativeOrigin = sprite.RelativeOrigin;
                                            if (otherLimb.DamagedSprite != null)
                                            {
                                                otherLimb.DamagedSprite.RelativeOrigin = sprite.RelativeOrigin;
                                            }
                                            TryUpdateLimbParam(otherLimb, "origin", sprite.RelativeOrigin);
                                        });
                                    }
                                };
                                w.PreUpdate += dTime =>
                                {
                                    // Additional condition
                                    if (w.Enabled)
                                    {
                                        w.Enabled = !lockSpriteOrigin;
                                    }
                                };
                                w.PreDraw += (sb, dTime) =>
                                {
                                    var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(limb));
                                    w.DrawPos = (spritePos + (sprite.Origin + sprite.SourceRect.Location.ToVector2()) * spriteSheetZoom)
                                        .Clamp(spritePos + GetTopLeft() * spriteSheetZoom, spritePos + GetBottomRight() * spriteSheetZoom);
                                    w.refresh();
                                };
                            });
                            originWidget.Draw(spriteBatch, deltaTime);
                            if (!lockSpritePosition)
                            {
                                var positionWidget = GetLimbEditWidget($"{limb.limbParams.ID}_position", limb, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                                {
                                    w.refresh = () => w.tooltip = $"{GetCharacterEditorTranslation("Position")}: {limb.ActiveSprite.SourceRect.Location}";
                                    w.refresh();
                                    w.MouseHeld += dTime =>
                                    {
                                        w.DrawPos = PlayerInput.MousePosition;
                                        var newRect = limb.ActiveSprite.SourceRect;
                                        newRect.Location = new Point(
                                            (int)((PlayerInput.MousePosition.X + halfSize - spriteSheetOffsetX) / spriteSheetZoom),
                                            (int)((PlayerInput.MousePosition.Y + halfSize - GetOffsetY(limb)) / spriteSheetZoom));
                                        limb.ActiveSprite.SourceRect = newRect;
                                        if (limb.DamagedSprite != null)
                                        {
                                            limb.DamagedSprite.SourceRect = limb.ActiveSprite.SourceRect;
                                        }
                                        RecalculateOrigin(limb);
                                        TryUpdateLimbParam(limb, "sourcerect", newRect);
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
                                                RecalculateOrigin(otherLimb);
                                            });
                                        };
                                        void RecalculateOrigin(Limb l)
                                        {
                                            if (lockSpriteOrigin)
                                            {
                                                // Keeps the absolute origin unchanged. The relative origin will be recalculated.
                                                var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(l));
                                                l.ActiveSprite.Origin = (originWidget.DrawPos - spritePos - l.ActiveSprite.SourceRect.Location.ToVector2() * spriteSheetZoom) / spriteSheetZoom;
                                                TryUpdateLimbParam(l, "origin", l.ActiveSprite.RelativeOrigin);
                                            }
                                            else
                                            {
                                                // Keeps the relative origin unchanged. The absolute origin will be recalculated.
                                                l.ActiveSprite.RelativeOrigin = l.ActiveSprite.RelativeOrigin;
                                            }
                                        }
                                    };
                                    w.PreDraw += (sb, dTime) => w.refresh();
                                });    
                                if (!positionWidget.IsControlled)
                                {
                                    positionWidget.DrawPos = topLeft - new Vector2(halfSize);
                                }
                                positionWidget.Draw(spriteBatch, deltaTime);
                            }
                            if (!lockSpriteSize)
                            {
                                var sizeWidget = GetLimbEditWidget($"{limb.limbParams.ID}_size", limb, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                                {
                                    w.refresh = () => w.tooltip = $"{GetCharacterEditorTranslation("Size")}: {limb.ActiveSprite.SourceRect.Size}";
                                    w.refresh();
                                    w.MouseHeld += dTime =>
                                    {
                                        w.DrawPos = PlayerInput.MousePosition;
                                        var newRect = limb.ActiveSprite.SourceRect;
                                        float offset_y = limb.ActiveSprite.SourceRect.Y * spriteSheetZoom + GetOffsetY(limb);
                                        float offset_x = limb.ActiveSprite.SourceRect.X * spriteSheetZoom + spriteSheetOffsetX;
                                        int width = (int)((PlayerInput.MousePosition.X - halfSize - offset_x) / spriteSheetZoom);
                                        int height = (int)((PlayerInput.MousePosition.Y - halfSize - offset_y) / spriteSheetZoom);
                                        newRect.Size = new Point(width, height);
                                        limb.ActiveSprite.SourceRect = newRect;
                                        limb.ActiveSprite.size = new Vector2(width, height);
                                        if (recalculateCollider)
                                        {
                                            RecalculateCollider(limb);
                                        }
                                        RecalculateOrigin(limb);
                                        if (limb.DamagedSprite != null)
                                        {
                                            limb.DamagedSprite.SourceRect = limb.ActiveSprite.SourceRect;
                                        }
                                        TryUpdateLimbParam(limb, "sourcerect", newRect);
                                        if (limbPairEditing)
                                        {
                                            UpdateOtherLimbs(limb, otherLimb =>
                                            {
                                                otherLimb.ActiveSprite.SourceRect = newRect;
                                                RecalculateOrigin(otherLimb);
                                                if (recalculateCollider)
                                                {
                                                    RecalculateCollider(otherLimb);
                                                }
                                                if (otherLimb.DamagedSprite != null)
                                                {
                                                    otherLimb.DamagedSprite.SourceRect = newRect;
                                                }
                                                TryUpdateLimbParam(otherLimb, "sourcerect", newRect);
                                            });
                                        };
                                        void RecalculateCollider(Limb l)
                                        {
                                            // We want the collider to be slightly smaller than the source rect, because the source rect is usually a bit bigger than the graphic.
                                            float multiplier = 0.85f;
                                            l.body.SetSize(new Vector2(ConvertUnits.ToSimUnits(width), ConvertUnits.ToSimUnits(height)) * RagdollParams.LimbScale * RagdollParams.TextureScale * multiplier);
                                            TryUpdateLimbParam(l, "radius", ConvertUnits.ToDisplayUnits(l.body.radius / RagdollParams.LimbScale / RagdollParams.TextureScale));
                                            TryUpdateLimbParam(l, "width", ConvertUnits.ToDisplayUnits(l.body.width / RagdollParams.LimbScale / RagdollParams.TextureScale));
                                            TryUpdateLimbParam(l, "height", ConvertUnits.ToDisplayUnits(l.body.height / RagdollParams.LimbScale / RagdollParams.TextureScale));
                                        }
                                        void RecalculateOrigin(Limb l)
                                        {
                                            if (lockSpriteOrigin)
                                            {
                                                // Keeps the absolute origin unchanged. The relative origin will be recalculated.
                                                l.ActiveSprite.Origin = l.ActiveSprite.Origin;
                                                TryUpdateLimbParam(l, "origin", l.ActiveSprite.RelativeOrigin);
                                            }
                                            else
                                            {
                                                // Keeps the relative origin unchanged. The absolute origin will be recalculated.
                                                l.ActiveSprite.RelativeOrigin = l.ActiveSprite.RelativeOrigin;
                                            }
                                        }
                                    };
                                    w.PreDraw += (sb, dTime) => w.refresh();
                                });
                                if (!sizeWidget.IsControlled)
                                {
                                    sizeWidget.DrawPos = bottomRight + new Vector2(halfSize);
                                }
                                sizeWidget.Draw(spriteBatch, deltaTime);
                            }
                        }
                        else if (rect.Contains(PlayerInput.MousePosition) && GUI.MouseOn == null && Widget.selectedWidgets.None())
                        {
                            // TODO: only one limb name should be displayed (needs to be done in a separate loop)
                            GUI.DrawString(spriteBatch, limbScreenPos + new Vector2(10, -10), limb.Name, Color.White, Color.Black * 0.5f);
                        }
                    }
                }
                offsetY += (int)(texture.Height * spriteSheetZoom);
            }

            int GetTextureHeight(Limb limb)
            {
                int textureIndex = Textures.IndexOf(limb.ActiveSprite.Texture);
                int height = 0;
                foreach (var t in Textures)
                {
                    if (Textures.IndexOf(t) < textureIndex)
                    {
                        height += t.Height;
                    }
                }
                return (int)(height * spriteSheetZoom);
            }

            int GetOffsetY(Limb limb) => spriteSheetOffsetY + GetTextureHeight(limb);
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
                            Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / RagdollParams.JointScale);
                            TryUpdateJointParam(joint, "limb1anchor", transformedValue);
                            // Snap all selected joints to the first selected
                            if (copyJointSettings)
                            {
                                foreach (var j in selectedJoints)
                                {
                                    j.LocalAnchorA = joint.LocalAnchorA;
                                    TryUpdateJointParam(j, "limb1anchor", transformedValue);
                                }
                            }
                        }
                        else if (joint.BodyB == limb.body.FarseerBody)
                        {
                            joint.LocalAnchorB += input;
                            Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / RagdollParams.JointScale);
                            TryUpdateJointParam(joint, "limb2anchor", transformedValue);
                            // Snap all selected joints to the first selected
                            if (copyJointSettings)
                            {
                                foreach (var j in selectedJoints)
                                {
                                    j.LocalAnchorB = joint.LocalAnchorB;
                                    TryUpdateJointParam(j, "limb2anchor", transformedValue);
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
                                    TryUpdateJointParam(otherJoint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / RagdollParams.JointScale));
                                }
                                else if (joint.BodyB == limb.body.FarseerBody && otherJoint.BodyB == otherLimb.body.FarseerBody)
                                {
                                    otherJoint.LocalAnchorB = joint.LocalAnchorB;
                                    TryUpdateJointParam(otherJoint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / RagdollParams.JointScale));
                                }
                            });
                        }
                    }
                }
            }
        }

        private void DrawJointLimitWidgets(SpriteBatch spriteBatch, Limb limb, LimbJoint joint, Vector2 drawPos, bool autoFreeze, bool allowPairEditing, float rotationOffset = 0)
        {
            rotationOffset += MathHelper.ToRadians(RagdollParams.SpritesheetOrientation);
            Color angleColor = joint.UpperLimit - joint.LowerLimit > 0 ? Color.LightGreen * 0.5f : Color.Red;
            DrawRadialWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.UpperLimit), $"joint.jointParams.Name {GetCharacterEditorTranslation("UpperLimit")}", Color.Cyan, angle =>
            {
                joint.UpperLimit = MathHelper.ToRadians(angle);
                ValidateJoint(joint);
                angle = MathHelper.ToDegrees(joint.UpperLimit);
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
            DrawRadialWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.LowerLimit), $"joint.jointParams.Name {GetCharacterEditorTranslation("LowerLimit")}", Color.Yellow, angle =>
            {
                joint.LowerLimit = MathHelper.ToRadians(angle);
                ValidateJoint(joint);
                angle = MathHelper.ToDegrees(joint.LowerLimit);
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
        #endregion

        #region Widgets as methods
        private void DrawRadialWidget(SpriteBatch spriteBatch, Vector2 drawPos, float value, string toolTip, Color color, Action<float> onClick,
            float circleRadius = 30, int widgetSize = 10, float rotationOffset = 0, bool clockWise = true, bool displayAngle = true, bool? autoFreeze = null, bool wrapAnglePi = false)
        {
            var angle = value;
            if (!MathUtils.IsValid(angle))
            {
                angle = 0;
            }
            float drawAngle = clockWise ? -angle : angle;
            var widgetDrawPos = drawPos + VectorExtensions.ForwardFlipped(MathHelper.ToRadians(drawAngle) + rotationOffset, circleRadius);
            GUI.DrawLine(spriteBatch, drawPos, widgetDrawPos, color);
            DrawWidget(spriteBatch, widgetDrawPos, WidgetType.Rectangle, widgetSize, color, toolTip, () =>
            {
                GUI.DrawLine(spriteBatch, drawPos, widgetDrawPos, color, width: 3);
                ShapeExtensions.DrawCircle(spriteBatch, drawPos, circleRadius, 40, color, thickness: 1);
                Vector2 d = PlayerInput.MousePosition - drawPos;
                float newAngle = clockWise
                    ? MathUtils.VectorToAngle(d) - MathHelper.PiOver2 + rotationOffset
                    : -MathUtils.VectorToAngle(d) + MathHelper.PiOver2 - rotationOffset;
                angle = MathHelper.ToDegrees(wrapAnglePi ? MathUtils.WrapAnglePi(newAngle) : MathUtils.WrapAngleTwoPi(newAngle));
                if (displayAngle)
                {
                    GUI.DrawString(spriteBatch, drawPos, angle.FormatZeroDecimal(), Color.Black, backgroundColor: color, font: GUI.SmallFont);
                }
                onClick(angle);
                var zeroPos = drawPos + VectorExtensions.ForwardFlipped(rotationOffset, circleRadius);
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

        private enum WidgetType { Rectangle, Circle }
        private void DrawWidget(SpriteBatch spriteBatch, Vector2 drawPos, WidgetType widgetType, int size, Color color, string toolTip, Action onPressed, bool ? autoFreeze = null, Action onHovered = null)
        {
            var drawRect = new Rectangle((int)drawPos.X - size / 2, (int)drawPos.Y - size / 2, size, size);
            var inputRect = drawRect;
            inputRect.Inflate(size * 0.75f, size * 0.75f);
            bool isMouseOn = inputRect.Contains(PlayerInput.MousePosition);
            bool isSelected = isMouseOn && GUI.MouseOn == null && Widget.selectedWidgets.None();
            switch (widgetType)
            {
                case WidgetType.Rectangle:
                    if (isSelected)
                    {
                        var rect = drawRect;
                        rect.Inflate(size * 0.3f, size * 0.3f);
                        GUI.DrawRectangle(spriteBatch, rect, color, thickness: 3, isFilled: PlayerInput.LeftButtonHeld());
                    }
                    else
                    {
                        GUI.DrawRectangle(spriteBatch, drawRect, color, thickness: 1, isFilled: false);
                    }
                    break;
                case WidgetType.Circle:
                    if (isSelected)
                    {
                        ShapeExtensions.DrawCircle(spriteBatch, drawPos, size * 0.7f, 40, color, thickness: 3);
                    }
                    else
                    {
                        ShapeExtensions.DrawCircle(spriteBatch, drawPos, size * 0.5f, 40, color, thickness: 1);
                    }
                    break;
                default: throw new NotImplementedException(widgetType.ToString());
            }
            if (isSelected)
            {
                // Label/tooltip
                if (onHovered == null)
                {
                    GUI.DrawString(spriteBatch, new Vector2(drawRect.Right + 5, drawRect.Y - drawRect.Height / 2), toolTip, color, Color.Black);
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
                // Might not be entirely reliable, since the method is used inside the draw loop.
                if (PlayerInput.LeftButtonClicked())
                {
                    SaveSnapshot();
                }
            }
        }
        #endregion

        #region Widgets as classes
        private Dictionary<string, Widget> animationWidgets = new Dictionary<string, Widget>();
        private Dictionary<string, Widget> jointSelectionWidgets = new Dictionary<string, Widget>();
        private Dictionary<string, Widget> limbEditWidgets = new Dictionary<string, Widget>();

        private Widget GetAnimationWidget(string name, Color color, int size = 10, float sizeMultiplier = 2, Widget.Shape shape = Widget.Shape.Rectangle, Action<Widget> initMethod = null)
        {
            string id = $"{character.SpeciesName}_{character.AnimController.CurrentAnimationParams.AnimationType.ToString()}_{name}";
            if (!animationWidgets.TryGetValue(id, out Widget widget))
            {
                int selectedSize = (int)Math.Round(size * sizeMultiplier);
                widget = new Widget(id, size, shape)
                {
                    tooltipOffset = new Vector2(selectedSize / 2 + 5, -10),
                    data = character.AnimController.CurrentAnimationParams
                };
                widget.MouseUp += () => CurrentAnimation.CreateSnapshot();
                widget.color = color;
                widget.PreUpdate += dTime =>
                {
                    widget.Enabled = editAnimations;
                    if (widget.Enabled)
                    {
                        AnimationParams data = widget.data as AnimationParams;
                        widget.Enabled = data.AnimationType == character.AnimController.CurrentAnimationParams.AnimationType;
                    }
                };
                widget.PostUpdate += dTime =>
                {
                    widget.inputAreaMargin = widget.IsControlled ? 1000 : 0;
                    widget.size = widget.IsSelected ? selectedSize : size;
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
                    widget.inputAreaMargin = widget.IsControlled ? 1000 : 0;
                    widget.size = widget.IsSelected ? selectedSize : normalSize;
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
                widget.MouseUp += () => RagdollParams.CreateSnapshot();
                widget.tooltip = joint.jointParams.Name;
                jointSelectionWidgets.Add(ID, widget);
                return widget;
            }
        }

        private Widget GetLimbEditWidget(string ID, Limb limb, int size = 5, Widget.Shape shape = Widget.Shape.Rectangle, Action < Widget> initMethod = null)
        {
            if (!limbEditWidgets.TryGetValue(ID, out Widget widget))
            {
                widget = CreateLimbEditWidget();
                limbEditWidgets.Add(ID, widget);
            }
            return widget;

            Widget CreateLimbEditWidget()
            {
                int normalSize = size;
                int selectedSize = (int)Math.Round(size * 1.5f);
                var w = new Widget(ID, size, shape)
                {
                    tooltipOffset = new Vector2(selectedSize / 2 + 5, -10),
                    data = limb,
                    color = Color.Yellow,
                    secondaryColor = Color.Gray,
                    textColor = Color.Yellow
                };
                w.PreUpdate += dTime => w.Enabled = editLimbs && selectedLimbs.Contains(limb);
                w.PostUpdate += dTime =>
                {
                    w.inputAreaMargin = w.IsControlled ? 1000 : 0;
                    w.size = w.IsSelected ? selectedSize : normalSize;
                    w.isFilled = w.IsControlled;
                };
                w.MouseUp += () => RagdollParams.CreateSnapshot();
                initMethod?.Invoke(w);
                return w;
            }
        }
        #endregion

        #region Character Wizard
        private class Wizard
        {
            // Ragdoll data
            private string name = string.Empty;
            private bool isHumanoid = false;
            private bool canEnterSubmarine = true;
            private string texturePath;
            private string xmlPath;
            private string contentPackageName;
            private Dictionary<string, XElement> limbXElements = new Dictionary<string, XElement>();
            private List<GUIComponent> limbGUIElements = new List<GUIComponent>();
            private List<XElement> jointXElements = new List<XElement>();
            private List<GUIComponent> jointGUIElements = new List<GUIComponent>();

            public static Wizard instance;
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
                    var box = new GUIMessageBox(GetCharacterEditorTranslation("CreateNewCharacter"), string.Empty, new string[] { TextManager.Get("Cancel"), TextManager.Get("Next") }, new Vector2(0.5f, 1.0f));
                    box.Content.ChildAnchor = Anchor.TopCenter;
                    box.Content.AbsoluteSpacing = 20;
                    int elementSize = 30;
                    var listBox = new GUIListBox(new RectTransform(new Vector2(1, 0.9f), box.Content.RectTransform));
                    var topGroup = new GUILayoutGroup(new RectTransform(new Point(listBox.Content.Rect.Width, elementSize * 6 + 20), listBox.Content.RectTransform)) { AbsoluteSpacing = 2 };
                    var fields = new List<GUIComponent>();
                    GUITextBox texturePathElement = null;
                    GUITextBox xmlPathElement = null;
                    GUITextBox contentPackageNameElement = null;
                    void UpdatePaths()
                    {
                        string pathBase = $"Mods/Characters/{Name}/{Name}";
                        XMLPath = $"{pathBase}.xml";
                        xmlPathElement.Text = XMLPath;
                        if (string.IsNullOrWhiteSpace(TexturePath))
                        {
                            TexturePath = $"{pathBase}.png";
                            texturePathElement.Text = TexturePath;
                        }
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        var mainElement = new GUIFrame(new RectTransform(new Point(topGroup.RectTransform.Rect.Width, elementSize), topGroup.RectTransform), style: null, color: Color.Gray * 0.25f);
                        fields.Add(mainElement);
                        RectTransform leftElement = new RectTransform(new Vector2(0.5f, 1), mainElement.RectTransform, Anchor.TopLeft);
                        RectTransform rightElement = new RectTransform(new Vector2(0.5f, 1), mainElement.RectTransform, Anchor.TopRight);
                        switch (i)
                        {
                            case 0:
                                new GUITextBlock(leftElement, TextManager.Get("Name"));
                                var nameField = new GUITextBox(rightElement, GetCharacterEditorTranslation("DefaultName")) { CaretColor = Color.White };
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
                                new GUITextBlock(leftElement, GetCharacterEditorTranslation("IsHumanoid"))
                                {
                                    TextColor = Color.White * 0.3f
                                };
                                new GUITickBox(rightElement, string.Empty)
                                {
                                    Selected = IsHumanoid,
                                    OnSelected = (tB) => IsHumanoid = tB.Selected,
                                    Enabled = false
                                };
                                break;
                            case 2:
                                new GUITextBlock(leftElement, GetCharacterEditorTranslation("CanEnterSubmarines"));
                                new GUITickBox(rightElement, string.Empty)
                                {
                                    Selected = CanEnterSubmarine,
                                    OnSelected = (tB) => CanEnterSubmarine = tB.Selected
                                };
                                break;
                            case 3:
                                new GUITextBlock(leftElement, GetCharacterEditorTranslation("ConfigFileOutput"));
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
                            case 4:
                                new GUITextBlock(leftElement, GetCharacterEditorTranslation("TexturePath"));
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
                            case 5:
                                new GUITextBlock(leftElement, GetCharacterEditorTranslation("ContentPackageName"));
                                contentPackageNameElement = new GUITextBox(rightElement, string.Empty)
                                {
                                    CaretColor = Color.White,
                                };
                                contentPackageNameElement.OnTextChanged += (tb, text) =>
                                {
                                    ContentPackageName = text;
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
                        Wizard.Instance.SelectTab(Tab.None);
                        return true;
                    };
                    // Next
                    box.Buttons[1].OnClicked += (b, d) =>
                    {
                        if (!File.Exists(TexturePath))
                        {
                            GUI.AddMessage(GetCharacterEditorTranslation("TextureDoesNotExist"), Color.Red);
                            texturePathElement.Flash(Color.Red);
                            return false;
                        }
                        var path = Path.GetFileName(TexturePath);
                        if (!path.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase))
                        {
                            GUI.AddMessage(TextManager.Get("WrongFileType"), Color.Red);
                            texturePathElement.Flash(Color.Red);
                            return false;
                        }
                        Wizard.Instance.SelectTab(Tab.Ragdoll);
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
                    var box = new GUIMessageBox(GetCharacterEditorTranslation("DefineRagdoll"), string.Empty, new string[] { TextManager.Get("Previous"), TextManager.Get("Create") }, new Vector2(0.5f, 1.0f));
                    box.Content.ChildAnchor = Anchor.TopCenter;
                    box.Content.AbsoluteSpacing = 20;
                    int elementSize = 30;
                    var topGroup = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.05f), box.Content.RectTransform)) { AbsoluteSpacing = 2 };
                    var bottomGroup = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.75f), box.Content.RectTransform)) { AbsoluteSpacing = 10 };
                    // HTML
                    GUIMessageBox htmlBox = null;
                    var loadHtmlButton = new GUIButton(new RectTransform(new Point(topGroup.RectTransform.Rect.Width, elementSize), topGroup.RectTransform), GetCharacterEditorTranslation("LoadFromHTML"));
                    // Limbs
                    var limbsElement = new GUIFrame(new RectTransform(new Vector2(1, 0.05f), bottomGroup.RectTransform), style: null) { CanBeFocused = false };
                    new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), limbsElement.RectTransform), $"{GetCharacterEditorTranslation("Limbs")}: ");
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
                    new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), jointsElement.RectTransform), $"{GetCharacterEditorTranslation("Joints")}: ");
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
                            htmlBox = new GUIMessageBox(GetCharacterEditorTranslation("LoadHTML"), string.Empty, new string[] { TextManager.Get("Close"), TextManager.Get("Load") }, new Vector2(0.5f, 1.0f));
                            var element = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.05f), htmlBox.Content.RectTransform), style: null, color: Color.Gray * 0.25f);
                            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), element.RectTransform), GetCharacterEditorTranslation("HTMLPath"));
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
                                    new XAttribute("type", Name), LimbXElements.Values, JointXElements
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
                        Wizard.Instance.SelectTab(Tab.Character);
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
                            GUI.AddMessage(GetCharacterEditorTranslation("MultipleTorsosDefined"), Color.Red);
                            return false;
                        }
                        XElement torso = torsoAttributes.Single().Parent;
                        int radius = torso.GetAttributeInt("radius", -1);
                        int height = torso.GetAttributeInt("height", -1);
                        int width = torso.GetAttributeInt("width", -1);
                        int colliderHeight = -1;
                        if (radius == -1)
                        {
                            // the collider is a box -> calculate the capsule
                            if (width == height)
                            {
                                radius = width / 2;
                                colliderHeight = width - radius * 2;
                            }
                            else
                            {
                                if (height > width)
                                {
                                    radius = width / 2;
                                    colliderHeight = height - radius * 2;
                                }
                                else
                                {
                                    radius = height / 2;
                                    colliderHeight = width - radius * 2;
                                }
                            }
                            radius = Math.Max(radius, 1);
                        }
                        else if (height > -1 || width > -1)
                        {
                            // the collider is a capsule -> use the capsule as it is
                            colliderHeight = width > height ? width : height;
                        }
                        var colliderAttributes = new List<XAttribute>() { new XAttribute("radius", radius) };
                        if (colliderHeight > -1)
                        {
                            colliderHeight = Math.Max(colliderHeight, 1);
                            if (height > width)
                            {
                                colliderAttributes.Add(new XAttribute("height", colliderHeight));
                            }
                            else
                            {
                                colliderAttributes.Add(new XAttribute("width", colliderHeight));
                            }
                        }
                        var colliderElements = new List<XElement>() { new XElement("collider", colliderAttributes) };
                        if (IsHumanoid)
                        {
                            // For humanoids, we need a secondary, shorter collider for crouching
                            var secondaryCollider = new XElement("collider", new XAttribute("radius", radius));
                            if (colliderHeight > -1)
                            {
                                colliderHeight = Math.Max(colliderHeight, 1);
                                if (height > width)
                                {
                                    secondaryCollider.Add(new XAttribute("height", colliderHeight * 0.75f));
                                }
                                else
                                {
                                    secondaryCollider.Add(new XAttribute("width", colliderHeight * 0.75f));
                                }
                            }
                            colliderElements.Add(secondaryCollider);
                        }
                        var ragdollParams = new object[]
                        {
                            new XAttribute("type", Name),
                            new XAttribute("canentersubmarine", CanEnterSubmarine),
                                colliderElements,
                                LimbXElements.Values,
                                JointXElements
                        };
                        if (CharacterEditorScreen.instance.CreateCharacter(Name, Path.GetDirectoryName(XMLPath), IsHumanoid, ContentPackageName, ragdollParams))
                        {
                            GUI.AddMessage(GetCharacterEditorTranslation("CharacterCreated").Replace("[name]", Name), Color.Green, font: GUI.Font);
                        }
                        Wizard.Instance.SelectTab(Tab.None);
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
                    var limbTypeField = GUI.CreateEnumField(limbType, elementSize, GetCharacterEditorTranslation("LimbType"), group.RectTransform, font: GUI.Font);
                    var sourceRectField = GUI.CreateRectangleField(sourceRect ?? new Rectangle(0, 0, 2, 2), elementSize, GetCharacterEditorTranslation("SourceRectangle"), group.RectTransform, font: GUI.Font);
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), idField.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("ID"));
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
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopLeft), TextManager.Get("Name"));
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
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopLeft), TextManager.Get("Name"));
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
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), limb1Field.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "1"));
                    var limb1InputField = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), limb1Field.RectTransform, Anchor.TopRight), GUINumberInput.NumberType.Int)
                    {
                        MinValueInt = 0,
                        MaxValueInt = byte.MaxValue,
                        IntValue = id1
                    };
                    var limb2Field = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), limb2Field.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "2"));
                    var limb2InputField = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), limb2Field.RectTransform, Anchor.TopRight), GUINumberInput.NumberType.Int)
                    {
                        MinValueInt = 0,
                        MaxValueInt = byte.MaxValue,
                        IntValue = id2
                    };
                    GUI.CreateVector2Field(anchor1 ?? Vector2.Zero, elementSize, GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "1"), group.RectTransform, font: GUI.Font, decimalsToDisplay: 2);
                    GUI.CreateVector2Field(anchor2 ?? Vector2.Zero, elementSize, GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "2"), group.RectTransform, font: GUI.Font, decimalsToDisplay: 2);
                    label.Text = GetJointName(jointName);
                    limb1InputField.OnValueChanged += nInput => label.Text = GetJointName(jointName);
                    limb2InputField.OnValueChanged += nInput => label.Text = GetJointName(jointName);
                    JointGUIElements.Add(jointElement);
                    string GetJointName(string n) => string.IsNullOrWhiteSpace(n) ? $"{GetCharacterEditorTranslation("Joint")} {limb1InputField.IntValue} - {limb2InputField.IntValue}" : n;
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
                public bool CanEnterSubmarine
                {
                    get => Instance.canEnterSubmarine;
                    set => Instance.canEnterSubmarine = value;
                }
                public string ContentPackageName
                {
                    get => Instance.contentPackageName;
                    set => Instance.contentPackageName = value;
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
                        int id = GetField(GetCharacterEditorTranslation("ID")).Parent.GetChild<GUINumberInput>().IntValue;
                        string limbName = GetField(TextManager.Get("Name")).Parent.GetChild<GUITextBox>().Text;
                        LimbType limbType = (LimbType)GetField(GetCharacterEditorTranslation("LimbType")).Parent.GetChild<GUIDropDown>().SelectedData;
                        // Reverse, because the elements are created from right to left
                        var rectInputs = GetField(GetCharacterEditorTranslation("SourceRectangle")).Parent.GetAllChildren().Where(c => c is GUINumberInput).Select(c => c as GUINumberInput).Reverse().ToArray();
                        int width = rectInputs[2].IntValue;
                        int height = rectInputs[3].IntValue;
                        var colliderAttributes = new List<XAttribute>();
                        // Capsules/Circles
                        //if (width == height)
                        //{
                        //    colliderAttributes.Add(new XAttribute("radius", (int)(width / 2 * 0.85f)));
                        //}
                        //else
                        //{
                        //    if (height > width)
                        //    {
                        //        colliderAttributes.Add(new XAttribute("radius", (int)(width / 2 * 0.85f)));
                        //        colliderAttributes.Add(new XAttribute("height",(int) (height - width * 0.85f)));
                        //    }
                        //    else
                        //    {
                        //        colliderAttributes.Add(new XAttribute("radius", (int)(height / 2 * 0.85f)));
                        //        colliderAttributes.Add(new XAttribute("width", (int)(width - height * 0.85f)));
                        //    }
                        //}
                        // Rectangles
                        colliderAttributes.Add(new XAttribute("height", (int)(height * 0.85f)));
                        colliderAttributes.Add(new XAttribute("width", (int)(width * 0.85f)));
                        idToCodeName.TryGetValue(id, out string notes);
                        LimbXElements.Add(id.ToString(), new XElement("limb",
                            new XAttribute("id", id),
                            new XAttribute("name", limbName),
                            new XAttribute("type", limbType.ToString()),
                            colliderAttributes,
                            new XElement("sprite",
                                new XAttribute("texture", TexturePath),
                                new XAttribute("sourcerect", $"{rectInputs[0].IntValue}, {rectInputs[1].IntValue}, {width}, {height}")),
                            new XAttribute("notes", null ?? string.Empty)
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
                        string jointName = GetField(TextManager.Get("Name")).Parent.GetChild<GUITextBox>().Text;
                        int limb1ID = GetField(GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "1")).Parent.GetChild<GUINumberInput>().IntValue;
                        int limb2ID = GetField(GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "2")).Parent.GetChild<GUINumberInput>().IntValue;
                        // Reverse, because the elements are created from right to left
                        var anchor1Inputs = GetField(GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "1")).Parent.GetAllChildren().Where(c => c is GUINumberInput).Select(c => c as GUINumberInput).Reverse().ToArray();
                        var anchor2Inputs = GetField(GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "2")).Parent.GetAllChildren().Where(c => c is GUINumberInput).Select(c => c as GUINumberInput).Reverse().ToArray();
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
                        DebugConsole.ThrowError(GetCharacterEditorTranslation("FailedToReadHTML").Replace("[path]", path), e);
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
                                DebugConsole.ThrowError(GetCharacterEditorTranslation("MultipleItemsWithSameHierarchy").Replace("[hierarchy]", hierarchy).Replace("[name]", codeName));
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
                                    string jointName = $"{GetCharacterEditorTranslation("Joint")} {parentName} - {limbName}";
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
