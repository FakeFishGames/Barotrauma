using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
#if DEBUG
using System.IO;
#else
using Barotrauma.IO;
#endif

namespace Barotrauma.CharacterEditor
{
    class CharacterEditorScreen : EditorScreen
    {
        public static CharacterEditorScreen Instance { get; private set; }

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

        private bool ShowExtraRagdollControls => editLimbs || editJoints;

        private Character character;
        private Vector2 spawnPosition;

        private bool editCharacterInfo;
        private bool editRagdoll;
        private bool editAnimations;
        private bool editLimbs;
        private bool editJoints;
        private bool editIK;

        private bool drawSkeleton;
        private bool drawDamageModifiers;
        private bool showParamsEditor;
        private bool showSpritesheet;
        private bool isFrozen;
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
        private bool onlyShowSourceRectForSelectedLimbs;
        private bool unrestrictSpritesheet;

        private enum JointCreationMode
        {
            None,
            Select,
            Create
        }

        private JointCreationMode jointCreationMode;
        private bool isDrawingLimb;

        private Rectangle newLimbRect;
        private Limb jointStartLimb;
        private Limb jointEndLimb;
        private Vector2? anchor1Pos;

        private const float holdTime = 0.2f;
        private double holdTimer;

        private float spriteSheetZoom = 1;
        private float spriteSheetMinZoom = 0.25f;
        private float spriteSheetMaxZoom = 1;
        private int spriteSheetOffsetY = 20;
        private int spriteSheetOffsetX = 30;
        private bool hideBodySheet;
        private Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1.0f);
        private Vector2 cameraOffset;

        private List<LimbJoint> selectedJoints = new List<LimbJoint>();
        private List<Limb> selectedLimbs = new List<Limb>();
        private HashSet<Character> editedCharacters = new HashSet<Character>();

        private bool isEndlessRunner;

        private Rectangle spriteSheetRect;

        private Rectangle CalculateSpritesheetRectangle() => 
            Textures == null || Textures.None() ? Rectangle.Empty :
            new Rectangle(
                spriteSheetOffsetX, 
                spriteSheetOffsetY, 
                (int)(Textures.OrderByDescending(t => t.Width).First().Width * spriteSheetZoom), 
                (int)(Textures.Sum(t => t.Height) * spriteSheetZoom));

        private const string screenTextTag = "CharacterEditor.";

        public override void Select()
        {
            base.Select();

            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", 0.0f, 0);

            GUI.ForceMouseOn(null);
            if (Submarine.MainSub == null)
            {
                ResetVariables();
                var subInfo = new SubmarineInfo("Content/AnimEditor.sub");
                Submarine.MainSub = new Submarine(subInfo);
                if (Submarine.MainSub.PhysicsBody != null)
                {
                    Submarine.MainSub.PhysicsBody.Enabled = false;
                }
                originalWall = new WallGroup(new List<Structure>(Structure.WallList));
                CloneWalls();
                CalculateMovementLimits();
                isEndlessRunner = true;
                GameMain.LightManager.LightingEnabled = false;
            }
            else if (Instance == null)
            {
                ResetVariables();
            }
            Submarine.MainSub.GodMode = true;
            if (Character.Controlled == null)
            {
                var humanSpeciesName = CharacterPrefab.HumanSpeciesName;
                if (humanSpeciesName.IsEmpty)
                {
                    SpawnCharacter(AllSpecies.First());
                }
                else
                {
                    SpawnCharacter(humanSpeciesName);
                }
            }
            else
            {
                OnPreSpawn();
                character = Character.Controlled;
                OnPostSpawn();
            }
            OpenDoors();
            GameMain.Instance.ResolutionChanged += OnResolutionChanged;
            Instance = this;

            if (!GameSettings.CurrentConfig.EditorDisclaimerShown)
            {
                GameMain.Instance.ShowEditorDisclaimer();
            }
        }

        private void ResetVariables()
        {
            editCharacterInfo = false;
            editRagdoll = false;
            editAnimations = false;
            editLimbs = false;
            editJoints = false;
            editIK = false;
            drawSkeleton = false;
            drawDamageModifiers = false;
            showParamsEditor = false;
            showSpritesheet = false;
            isFrozen = false;
            autoFreeze = false;
            limbPairEditing = false;
            uniformScaling = true;
            lockSpriteOrigin = true;
            lockSpritePosition = false;
            lockSpriteSize = false;
            recalculateCollider = false;
            copyJointSettings = false;
            showColliders = false;
            displayWearables = true;
            displayBackgroundColor = false;
            jointCreationMode = JointCreationMode.None;
            isDrawingLimb = false;
            newLimbRect = Rectangle.Empty;
            cameraOffset = Vector2.Zero;
            jointEndLimb = null;
            anchor1Pos = null;
            jointStartLimb = null;
            allSpecies = null;
            onlyShowSourceRectForSelectedLimbs = false;
            unrestrictSpritesheet = false;
            editedCharacters.Clear();
            selectedJoints.Clear();
            selectedLimbs.Clear();
            if (character != null)
            {
                if (character.AnimController != null)
                {
                    if (character.AnimController.Collider != null)
                    {
                        character.AnimController.Collider.PhysEnabled = true;
                    }
                }
            }
            character = null;
            Wizard.instance?.Reset();
        }

        private void Reset(IEnumerable<Character> characters = null)
        {
            if (characters == null)
            {
                characters = editedCharacters;
            }
            characters.ForEach(c => ResetParams(c));
            ResetVariables();
        }

        private void ResetParams(Character character)
        {
            character.Params.Reset(true);
            foreach (var animation in character.AnimController.AllAnimParams)
            {
                animation.Reset(true);
                animation.ClearHistory();
            }
            character.AnimController.RagdollParams.Reset(true);
            character.AnimController.RagdollParams.ClearHistory();
            character.ForceRun = false;
            character.AnimController.ForceSelectAnimationType = AnimationType.NotDefined;
        }

        public override void Deselect()
        {
            base.Deselect();
            SoundPlayer.OverrideMusicType = Identifier.Empty;
            GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", GameSettings.CurrentConfig.Audio.SoundVolume, 0);
            GUI.ForceMouseOn(null);
            if (isEndlessRunner)
            {
                Submarine.MainSub?.Remove();
                GameMain.World.ProcessChanges();
                isEndlessRunner = false;
                Reset();
                if (character != null && !character.Removed)
                {
                    character.Remove();
                }
            }
            else
            {
#if !DEBUG
                Reset(Character.CharacterList.Where(c => VanillaCharacters.Any(vchar => vchar == c.Prefab.ContentFile)));
#endif
            }
            GameMain.Instance.ResolutionChanged -= OnResolutionChanged;
            GameMain.LightManager.LightingEnabled = true;
            ClearWidgets();
            ClearSelection();
        }

        private void OnResolutionChanged()
        {
            CreateGUI();
        }

        public static LocalizedString GetCharacterEditorTranslation(string tag)
        {
            return TextManager.Get(screenTextTag + tag);
        }

#region Main methods
        public override void AddToGUIUpdateList()
        {
            rightArea.AddToGUIUpdateList();
            leftArea.AddToGUIUpdateList();

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
                Limb lastLimb = selectedLimbs.LastOrDefault();
                if (lastLimb == null)
                {
                    var lastJoint = selectedJoints.LastOrDefault();
                    if (lastJoint != null)
                    {
                        lastLimb = PlayerInput.KeyDown(Keys.LeftAlt) ? lastJoint.LimbB : lastJoint.LimbA;
                    }
                }
                if (lastLimb != null)
                {
                    resetSpriteOrientationButtonParent.AddToGUIUpdateList();
                }
            }
            if (editRagdoll)
            {
                ragdollControls.AddToGUIUpdateList();
            }
            if (editJoints)
            {
                jointControls.AddToGUIUpdateList();
            }
            if (editLimbs && !unrestrictSpritesheet)
            {
                limbControls.AddToGUIUpdateList();
            }
            if (ShowExtraRagdollControls)
            {
                createLimbButton.Enabled = editLimbs;
                duplicateLimbButton.Enabled = selectedLimbs.Any();
                deleteSelectedButton.Enabled = selectedLimbs.Any() || selectedJoints.Any();
                createJointButton.Enabled = selectedLimbs.Any() || selectedJoints.Any();
                extraRagdollControls.AddToGUIUpdateList();
                if (createLimbButton.Enabled)
                {
                    if (isDrawingLimb)
                    {
                        createLimbButton.Color = Color.Yellow;
                        createLimbButton.HoverColor = Color.Yellow;
                    }
                    else
                    {
                        createLimbButton.Color = Color.White;
                        createLimbButton.HoverColor = Color.White;
                    }
                }
                if (createJointButton.Enabled)
                {
                    switch (jointCreationMode)
                    {
                        case JointCreationMode.Select:
                        case JointCreationMode.Create:
                            createJointButton.HoverColor = Color.Yellow;
                            createJointButton.Color = Color.Yellow;
                            break;
                        default:
                            createJointButton.HoverColor = Color.White;
                            createJointButton.Color = Color.White;
                            break;
                    }
                }
            }
            if (showParamsEditor)
            {
                ParamsEditor.Instance.EditorBox.Parent.AddToGUIUpdateList();
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            if (Wizard.instance != null) { return; }

            GameMain.LightManager?.Update((float)deltaTime);

            spriteSheetRect = CalculateSpritesheetRectangle();
            // Handle shortcut keys
            if (PlayerInput.KeyHit(Keys.F1))
            {
                SetToggle(paramsToggle, !paramsToggle.Selected);
            }
            if (PlayerInput.KeyHit(Keys.F5))
            {
                RecreateRagdoll();
            }
            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (PlayerInput.KeyHit(Keys.D1))
                {
                    SetToggle(characterInfoToggle, !characterInfoToggle.Selected);
                }
                else if (PlayerInput.KeyHit(Keys.D2))
                {
                    SetToggle(ragdollToggle, !ragdollToggle.Selected);
                }
                else if (PlayerInput.KeyHit(Keys.D3))
                {
                    SetToggle(limbsToggle, !limbsToggle.Selected);
                }
                else if (PlayerInput.KeyHit(Keys.D4))
                {
                    SetToggle(jointsToggle, !jointsToggle.Selected);
                }
                else if (PlayerInput.KeyHit(Keys.D5))
                {
                    SetToggle(animsToggle, !animsToggle.Selected);
                }
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
                            ResetParamsEditor();
                        }
                        if (editAnimations)
                        {
                            CurrentAnimation.Undo();
                            ClearWidgets();
                            ResetParamsEditor();
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
                            ResetParamsEditor();
                        }
                        if (editAnimations)
                        {
                            CurrentAnimation.Redo();
                            ClearWidgets();
                            ResetParamsEditor();
                        }
                    }
                }
                else
                {
                    Widget.EnableMultiSelect = false;
                    if (PlayerInput.KeyHit(Keys.C))
                    {
                        SetToggle(showCollidersToggle, !showCollidersToggle.Selected);
                    }
                    if (PlayerInput.KeyHit(Keys.L))
                    {
                        SetToggle(lightsToggle, !lightsToggle.Selected);
                    }
                    if (PlayerInput.KeyHit(Keys.M))
                    {
                        SetToggle(damageModifiersToggle, !damageModifiersToggle.Selected);
                    }
                    if (PlayerInput.KeyHit(Keys.N))
                    {
                        SetToggle(skeletonToggle, !skeletonToggle.Selected);
                    }
                    if (PlayerInput.KeyHit(Keys.T))
                    {
                        SetToggle(spritesheetToggle, !spritesheetToggle.Selected);
                    }
                    if (PlayerInput.KeyHit(Keys.I))
                    {
                        SetToggle(ikToggle, !ikToggle.Selected);
                    }
                }
                if (PlayerInput.KeyDown(InputType.Left) || PlayerInput.KeyDown(InputType.Right) || PlayerInput.KeyDown(InputType.Up) || PlayerInput.KeyDown(InputType.Down))
                {
                    // Enable the main collider physics when the user is trying to move the character.
                    // It's possible that the physics are disabled, because the angle widgets handle input logic in the draw method (which they shouldn't)
                    character.AnimController.Collider.PhysEnabled = true;
                }
                animTestPoseToggle.Enabled = CurrentAnimation.IsGroundedAnimation;
                if (animTestPoseToggle.Enabled)
                {
                    if (PlayerInput.KeyHit(Keys.X))
                    {
                        SetToggle(animTestPoseToggle, !animTestPoseToggle.Selected);
                    }
                }
                else
                {
                    animTestPoseToggle.Selected = false;
                }
                if (PlayerInput.KeyHit(InputType.Run))
                {
                    int index = 0;
                    bool isSwimming = character.AnimController.ForceSelectAnimationType == AnimationType.SwimFast || character.AnimController.ForceSelectAnimationType == AnimationType.SwimSlow;
                    bool isMovingFast = character.AnimController.ForceSelectAnimationType == AnimationType.Run || character.AnimController.ForceSelectAnimationType == AnimationType.SwimFast;
                    if (character.AnimController.CanWalk)
                    {
                        if (isMovingFast)
                        {
                            if (isSwimming)
                            {
                                index = 2;
                            }
                            else
                            {
                                index = 0;
                            }
                        }
                        else
                        {
                            if (isSwimming)
                            {
                                index = 3;
                            }
                            else
                            {
                                index = 1;
                            }
                        }
                    }
                    else
                    {
                        index = isMovingFast ? 0 : 1;
                    }
                    if (animSelection.SelectedIndex != index)
                    {
                        CurrentAnimation.ClearHistory();
                        animSelection.Select(index);
                        CurrentAnimation.StoreSnapshot();
                    }
                }
                if (!PlayerInput.KeyDown(Keys.LeftControl) && PlayerInput.KeyHit(Keys.E))
                {
                    bool isSwimming = character.AnimController.ForceSelectAnimationType == AnimationType.SwimFast || character.AnimController.ForceSelectAnimationType == AnimationType.SwimSlow;
                    if (isSwimming)
                    {
                        animSelection.Select(0);
                    }
                    else
                    {
                        animSelection.Select(2);
                    }
                }
                if (PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.KeyHit(Keys.Escape))
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
                    jointCreationMode = JointCreationMode.None;
                    isDrawingLimb = false;
                }
                if (PlayerInput.KeyHit(Keys.Delete))
                {
                    DeleteSelected();
                }
                if (ShowExtraRagdollControls && PlayerInput.KeyDown(Keys.LeftControl))
                {
                    if (PlayerInput.KeyHit(Keys.E))
                    {
                        ToggleJointCreationMode();
                    }
                }
                UpdateJointCreation();
                UpdateLimbCreation();
                if (PlayerInput.KeyHit(Keys.Left))
                {
                    Nudge(Keys.Left);
                }
                if (PlayerInput.KeyHit(Keys.Right))
                {
                    Nudge(Keys.Right);
                }
                if (PlayerInput.KeyHit(Keys.Down))
                {
                    Nudge(Keys.Down);
                }
                if (PlayerInput.KeyHit(Keys.Up))
                {
                    Nudge(Keys.Up);
                }
                if (PlayerInput.KeyDown(Keys.Left))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Left);
                    }
                }
                else if (PlayerInput.KeyDown(Keys.Right))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Right);
                    }
                }
                else if (PlayerInput.KeyDown(Keys.Down))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Down);
                    }
                }
                else if (PlayerInput.KeyDown(Keys.Up))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Up);
                    }
                }
                else
                {
                    holdTimer = 0;
                }
                if (isFrozen)
                {
                    float moveSpeed = (float)deltaTime * 300.0f / Cam.Zoom;
                    if (PlayerInput.KeyDown(Keys.LeftShift))
                    {
                        moveSpeed *= 4;
                    }
                    if (PlayerInput.KeyDown(Keys.W))
                    {
                        cameraOffset.Y += moveSpeed;
                    }
                    if (PlayerInput.KeyDown(Keys.A))
                    {
                        cameraOffset.X -= moveSpeed;
                    }
                    if (PlayerInput.KeyDown(Keys.S))
                    {
                        cameraOffset.Y -= moveSpeed;
                    }
                    if (PlayerInput.KeyDown(Keys.D))
                    {
                        cameraOffset.X += moveSpeed;
                    }
                    Vector2 max = new Vector2(GameMain.GraphicsWidth * 0.3f, GameMain.GraphicsHeight * 0.38f) / Cam.Zoom;
                    Vector2 min = -max;
                    cameraOffset = Vector2.Clamp(cameraOffset, min, max);
                }
            }
            if (!isFrozen)
            {
                foreach (PhysicsBody body in PhysicsBody.List)
                {
                    body.SetPrevTransform(body.SimPosition, body.Rotation);
                    body.Update();
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
                try
                {
                    GameMain.World.Step((float)Timing.Step);
                }
                catch (WorldLockedException e)
                {
                    string errorMsg = "Attempted to modify the state of the physics simulation while a time step was running.";
                    DebugConsole.ThrowError(errorMsg, e);
                    GameAnalyticsManager.AddErrorEventOnce("CharacterEditorScreen.Update:WorldLockedException" + e.Message, GameAnalyticsManager.ErrorSeverity.Critical, errorMsg);
                }
            }
            // Camera
            Cam.MoveCamera((float)deltaTime, allowMove: false, allowZoom: GUI.MouseOn == null);
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
            if (PlayerInput.PrimaryMouseButtonDown() && GUI.MouseOn == null && Widget.selectedWidgets.None())
            {
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (limb == null || limb.ActiveSprite == null) { continue; }
                    if (selectedJoints.Any(j => j.LimbA == limb || j.LimbB == limb)) { continue; }
                    // Select limbs on ragdoll
                    if (editLimbs && !spriteSheetRect.Contains(PlayerInput.MousePosition) && MathUtils.RectangleContainsPoint(GetLimbPhysicRect(limb), PlayerInput.MousePosition))
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
            optionsToggle?.UpdateOpenState((float)deltaTime, new Vector2(-optionsPanel.Rect.Width - rightArea.RectTransform.AbsoluteOffset.X, 0), optionsPanel.RectTransform);
            fileEditToggle?.UpdateOpenState((float)deltaTime, new Vector2(-fileEditPanel.Rect.Width - rightArea.RectTransform.AbsoluteOffset.X, 0), fileEditPanel.RectTransform);
            characterPanelToggle?.UpdateOpenState((float)deltaTime, new Vector2(-characterSelectionPanel.Rect.Width - rightArea.RectTransform.AbsoluteOffset.X, 0), characterSelectionPanel.RectTransform);
            minorModesToggle?.UpdateOpenState((float)deltaTime, new Vector2(-minorModesPanel.Rect.Width - leftArea.RectTransform.AbsoluteOffset.X, 0), minorModesPanel.RectTransform);
            modesToggle?.UpdateOpenState((float)deltaTime, new Vector2(-modesPanel.Rect.Width - leftArea.RectTransform.AbsoluteOffset.X, 0), modesPanel.RectTransform);
            buttonsPanelToggle?.UpdateOpenState((float)deltaTime, new Vector2(-buttonsPanel.Rect.Width - leftArea.RectTransform.AbsoluteOffset.X, 0), buttonsPanel.RectTransform);
        }

        public CursorState GetMouseCursorState()
        {
            foreach (var limb in character.AnimController.Limbs)
            {
                if (limb?.ActiveSprite == null) { continue; }
                if (selectedJoints.Any(j => j.LimbA == limb || j.LimbB == limb)) { continue; }
                // character limbs
                if (editLimbs && !spriteSheetRect.Contains(PlayerInput.MousePosition) &&
                    MathUtils.RectangleContainsPoint(GetLimbPhysicRect(limb), PlayerInput.MousePosition)) { return CursorState.Hand; }
                // spritesheet
                if (showSpritesheet && GetLimbSpritesheetRect(limb).Contains(PlayerInput.MousePosition)) { return CursorState.Hand; }
            }
            return CursorState.Default;
        }

        /// <summary>
        /// Fps independent mouse input. The draw method is called multiple times per frame.
        /// </summary>
        private Vector2 scaledMouseSpeed;
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            if (isFrozen)
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
                GameMain.LightManager.RenderLightMap(graphics, spriteBatch, cam);
                GameMain.LightManager.UpdateObstructVision(graphics, spriteBatch, cam, Character.Controlled.CursorWorldPosition);
            }
            base.Draw(deltaTime, graphics, spriteBatch);

            graphics.Clear(backgroundColor);

            // Submarine
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: Cam.Transform);
            Submarine.Draw(spriteBatch, isEndlessRunner);
            spriteBatch.End();

            // Character(s)
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: Cam.Transform);
            Character.CharacterList.ForEach(c => c.Draw(spriteBatch, Cam));
            if (GameMain.DebugDraw)
            {
                character.AnimController.DebugDraw(spriteBatch);
            }
            else if (showColliders)
            {
                character.AnimController.Collider.DebugDraw(spriteBatch, Color.White, forceColor: true);
                foreach (var limb in character.AnimController.Limbs)
                {
                    if (!limb.Hide)
                    {
                        limb.body.DebugDraw(spriteBatch, GUIStyle.Green, forceColor: true);
                    }
                }
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
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            if (drawDamageModifiers)
            {
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (selectedLimbs.Contains(limb) || selectedLimbs.None())
                    {
                        limb.DrawDamageModifiers(spriteBatch, cam, SimToScreen(limb.SimPosition), isScreenSpace: true);
                    }
                }
            }
            if (editAnimations)
            {
                DrawAnimationControls(spriteBatch, (float)deltaTime);
            }
            if (editLimbs)
            {
                DrawLimbEditor(spriteBatch);
            }
            if (drawSkeleton || editRagdoll || editJoints || editLimbs || editIK)
            {
                DrawRagdoll(spriteBatch, (float)deltaTime);
            }
            // Mouth
            Limb head = character.AnimController.GetLimb(LimbType.Head);
            if (head != null && character.CanEat && selectedLimbs.Contains(head))
            {
                var mouthPos = character.AnimController.GetMouthPosition();
                if (mouthPos.HasValue)
                {
                    ShapeExtensions.DrawPoint(spriteBatch, SimToScreen(mouthPos.Value), GUIStyle.Red, size: 8);
                }
            }
            if (showSpritesheet)
            {
                DrawSpritesheetEditor(spriteBatch, (float)deltaTime);
            }
            if (isDrawingLimb)
            {
                GUI.DrawRectangle(spriteBatch, newLimbRect, Color.Yellow);
            }
            if (jointCreationMode != JointCreationMode.None)
            {
                var textPos = new Vector2(GameMain.GraphicsWidth / 2 - 240, GameMain.GraphicsHeight / 4);
                if (jointCreationMode == JointCreationMode.Select)
                {
                    GUI.DrawString(spriteBatch, textPos, GetCharacterEditorTranslation("SelectAnchor1Pos"), Color.Yellow, font: GUIStyle.LargeFont);
                }
                else
                {
                    GUI.DrawString(spriteBatch, textPos, GetCharacterEditorTranslation("SelectLimbToConnect"), Color.Yellow, font: GUIStyle.LargeFont);
                }
                if (jointStartLimb != null && jointStartLimb.ActiveSprite != null)
                {
                    GUI.DrawRectangle(spriteBatch, GetLimbSpritesheetRect(jointStartLimb), Color.Yellow, thickness: 3);
                    GUI.DrawRectangle(spriteBatch, GetLimbPhysicRect(jointStartLimb), Color.Yellow, thickness: 3);
                }
                if (jointEndLimb != null && jointEndLimb.ActiveSprite != null)
                {
                    GUI.DrawRectangle(spriteBatch, GetLimbSpritesheetRect(jointEndLimb), GUIStyle.Green, thickness: 3);
                    GUI.DrawRectangle(spriteBatch, GetLimbPhysicRect(jointEndLimb), GUIStyle.Green, thickness: 3);
                }
                if (spriteSheetRect.Contains(PlayerInput.MousePosition))
                {
                    if (jointStartLimb != null)
                    {
                        var startPos = GetLimbSpritesheetRect(jointStartLimb).Center.ToVector2();
                        var offset = anchor1Pos ?? Vector2.Zero;
                        offset = -offset;
                        startPos += offset;
                        GUI.DrawLine(spriteBatch, startPos, PlayerInput.MousePosition, GUIStyle.Green, width: 3);
                    }
                }
                else
                {
                    if (jointStartLimb != null)
                    {
                        // TODO: there's something wrong here
                        var offset = anchor1Pos.HasValue ? Vector2.Transform(ConvertUnits.ToSimUnits(anchor1Pos.Value), Matrix.CreateRotationZ(jointStartLimb.Rotation)) : Vector2.Zero;
                        var startPos = SimToScreen(jointStartLimb.SimPosition + offset);
                        GUI.DrawLine(spriteBatch, startPos, PlayerInput.MousePosition, GUIStyle.Green, width: 3);
                    }
                }
            }
            if (isDrawingLimb)
            {
                var textPos = new Vector2(GameMain.GraphicsWidth / 2 - 200, GameMain.GraphicsHeight / 4);
                GUI.DrawString(spriteBatch, textPos, GetCharacterEditorTranslation("DrawLimbOnSpritesheet"), Color.Yellow, font: GUIStyle.LargeFont);
            }
            if (isEndlessRunner)
            {
                Structure wall = CurrentWall.walls.FirstOrDefault();
                Vector2 indicatorPos = wall == null ? originalWall.walls.First().DrawPosition : wall.DrawPosition;
                GUI.DrawIndicator(spriteBatch, indicatorPos, Cam, 700, GUIStyle.SubmarineLocationIcon.Value.Sprite, Color.White);
            }
            GUI.Draw(Cam, spriteBatch);
            if (isFrozen)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 40, 200), GetCharacterEditorTranslation("Frozen"), Color.Blue, Color.White * 0.5f, 10, GUIStyle.LargeFont);
            }
            if (animTestPoseToggle.Selected)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 100, 300), GetCharacterEditorTranslation("AnimationTestPoseEnabled"), Color.White, Color.Black * 0.5f, 10, GUIStyle.LargeFont);
            }
            if (selectedJoints.Count == 1)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 20), $"{GetCharacterEditorTranslation("Selected")}: {selectedJoints.First().Params.Name}", Color.White, font: GUIStyle.LargeFont);
            }
            if (selectedLimbs.Count == 1)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 20), $"{GetCharacterEditorTranslation("Selected")}: {selectedLimbs.First().Params.Name}", Color.White, font: GUIStyle.LargeFont);
            }
            if (showSpritesheet)
            {
                Limb lastLimb = selectedLimbs.LastOrDefault();
                if (lastLimb == null)
                {
                    var lastJoint = selectedJoints.LastOrDefault();
                    if (lastJoint != null)
                    {
                        lastLimb = PlayerInput.KeyDown(Keys.LeftAlt) ? lastJoint.LimbB : lastJoint.LimbA;
                    }
                }
                if (lastLimb != null)
                {
                    var topLeft = spriteSheetControls.RectTransform.TopLeft;
                    bool useSpritesheetOrientation = float.IsNaN(lastLimb.Params.SpriteOrientation);
                    GUI.DrawString(spriteBatch, new Vector2(topLeft.X + 350 * GUI.xScale, GameMain.GraphicsHeight - 95 * GUI.yScale), GetCharacterEditorTranslation("SpriteOrientation") + ":", useSpritesheetOrientation ? Color.White : Color.Yellow, Color.Gray * 0.5f, 10, GUIStyle.Font);
                    float orientation = useSpritesheetOrientation ? RagdollParams.SpritesheetOrientation : lastLimb.Params.SpriteOrientation;
                    DrawRadialWidget(spriteBatch, new Vector2(topLeft.X + 610 * GUI.xScale, GameMain.GraphicsHeight - 75 * GUI.yScale), orientation, string.Empty, useSpritesheetOrientation ? Color.White : Color.Yellow,
                        angle =>
                        {
                            TryUpdateSubParam(lastLimb.Params, "spriteorientation".ToIdentifier(), angle);
                            selectedLimbs.ForEach(l => TryUpdateSubParam(l.Params, "spriteorientation".ToIdentifier(), angle));
                            if (limbPairEditing)
                            {
                                UpdateOtherLimbs(lastLimb, l => TryUpdateSubParam(l.Params, "spriteorientation".ToIdentifier(), angle));
                            }
                        }, circleRadius: 40, widgetSize: 15, rotationOffset: 0, autoFreeze: false, rounding: 10);
                }
                else
                {
                    var topLeft = spriteSheetControls.RectTransform.TopLeft;
                    GUI.DrawString(spriteBatch, new Vector2(topLeft.X + 350 * GUI.xScale, GameMain.GraphicsHeight - 95 * GUI.yScale), GetCharacterEditorTranslation("SpriteSheetOrientation") + ":", Color.White, Color.Gray * 0.5f, 10, GUIStyle.Font);
                    DrawRadialWidget(spriteBatch, new Vector2(topLeft.X + 610 * GUI.xScale, GameMain.GraphicsHeight - 75 * GUI.yScale), RagdollParams.SpritesheetOrientation, string.Empty, Color.White,
                        angle => TryUpdateRagdollParam("spritesheetorientation", angle), circleRadius: 40, widgetSize: 15, rotationOffset: 0, autoFreeze: false, rounding: 10);
                }
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

                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 0), $"Cursor World Pos: {character.CursorWorldPosition}", Color.White, font: GUIStyle.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 20), $"Cursor Pos: {character.CursorPosition}", Color.White, font: GUIStyle.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 40), $"Cursor Screen Pos: {PlayerInput.MousePosition}", Color.White, font: GUIStyle.SmallFont);

                // Collider
                var collider = character.AnimController.Collider;
                var colliderDrawPos = SimToScreen(collider.SimPosition);
                Vector2 forward = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(collider.Rotation));
                var endPos = SimToScreen(collider.SimPosition + forward * collider.radius);
                GUI.DrawLine(spriteBatch, colliderDrawPos, endPos, GUIStyle.Green);
                GUI.DrawLine(spriteBatch, colliderDrawPos, SimToScreen(collider.SimPosition + forward * 0.25f), Color.Blue);
                Vector2 left = forward.Left();
                GUI.DrawLine(spriteBatch, colliderDrawPos, SimToScreen(collider.SimPosition + left * 0.25f), GUIStyle.Red);
                ShapeExtensions.DrawCircle(spriteBatch, colliderDrawPos, (endPos - colliderDrawPos).Length(), 40, GUIStyle.Green);
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 300, 0), $"Collider rotation: {MathHelper.ToDegrees(MathUtils.WrapAngleTwoPi(collider.Rotation))}", Color.White, font: GUIStyle.SmallFont);
            }
            spriteBatch.End();
        }
#endregion

#region Ragdoll Manipulation
        private void UpdateJointCreation()
        {
            if (jointCreationMode == JointCreationMode.None)
            {
                jointStartLimb = null;
                jointEndLimb = null;
                anchor1Pos = null;
                return;
            }
            if (editJoints)
            {
                var selectedJoint = selectedJoints.LastOrDefault();
                if (selectedJoint != null)
                {
                    if (jointCreationMode == JointCreationMode.Create)
                    {
                        if (spriteSheetRect.Contains(PlayerInput.MousePosition))
                        {
                            jointEndLimb = GetClosestLimbOnSpritesheet(PlayerInput.MousePosition, l => l != null && l != jointStartLimb && l.ActiveSprite != null);
                            if (jointEndLimb != null && PlayerInput.PrimaryMouseButtonClicked())
                            {
                                Vector2 anchor1 = anchor1Pos.HasValue ? anchor1Pos.Value / spriteSheetZoom : Vector2.Zero;
                                anchor1.X = -anchor1.X;
                                Vector2 anchor2 = (GetLimbSpritesheetRect(jointEndLimb).Center.ToVector2() - PlayerInput.MousePosition) / spriteSheetZoom;
                                anchor2.X = -anchor2.X;
                                CreateJoint(jointStartLimb.Params.ID, jointEndLimb.Params.ID, anchor1, anchor2);
                                jointCreationMode = JointCreationMode.None;
                            }
                        }
                        else
                        {
                            jointEndLimb = GetClosestLimbOnRagdoll(PlayerInput.MousePosition, l => l != null && l != jointStartLimb && l.ActiveSprite != null);
                            if (jointEndLimb != null && PlayerInput.PrimaryMouseButtonClicked())
                            {
                                Vector2 anchor2 = ConvertUnits.ToDisplayUnits(jointEndLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                                CreateJoint(jointStartLimb.Params.ID, jointEndLimb.Params.ID, anchor1Pos, anchor2);
                                jointCreationMode = JointCreationMode.None;
                            }
                        }
                    }
                    else
                    {
                        jointStartLimb = selectedJoint.LimbB;
                        if (spriteSheetRect.Contains(PlayerInput.MousePosition))
                        {
                            anchor1Pos = GetLimbSpritesheetRect(jointStartLimb).Center.ToVector2() - PlayerInput.MousePosition;
                        }
                        else
                        {
                            anchor1Pos = ConvertUnits.ToDisplayUnits(jointStartLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                        }
                        if (PlayerInput.PrimaryMouseButtonClicked())
                        {
                            jointCreationMode = JointCreationMode.Create;
                        }
                    }
                }
                else
                {
                    jointCreationMode = JointCreationMode.None;
                }
            }
            else if (editLimbs)
            {
                if (selectedLimbs.Any())
                {
                    if (spriteSheetRect.Contains(PlayerInput.MousePosition))
                    {
                        if (jointCreationMode == JointCreationMode.Create)
                        {
                            jointEndLimb = GetClosestLimbOnSpritesheet(PlayerInput.MousePosition, l => l != null && l != jointStartLimb && l.ActiveSprite != null && !l.Hidden);
                            if (jointEndLimb != null && PlayerInput.PrimaryMouseButtonClicked())
                            {
                                Vector2 anchor1 = anchor1Pos.HasValue ? anchor1Pos.Value / spriteSheetZoom : Vector2.Zero;
                                anchor1.X = -anchor1.X;
                                Vector2 anchor2 = (GetLimbSpritesheetRect(jointEndLimb).Center.ToVector2() - PlayerInput.MousePosition) / spriteSheetZoom;
                                anchor2.X = -anchor2.X;
                                CreateJoint(jointStartLimb.Params.ID, jointEndLimb.Params.ID, anchor1, anchor2);
                                jointCreationMode = JointCreationMode.None;
                            }
                        }
                        else if (PlayerInput.PrimaryMouseButtonClicked())
                        {
                            jointStartLimb = GetClosestLimbOnSpritesheet(PlayerInput.MousePosition, l => selectedLimbs.Contains(l) && !l.Hidden);
                            anchor1Pos = GetLimbSpritesheetRect(jointStartLimb).Center.ToVector2() - PlayerInput.MousePosition;
                            jointCreationMode = JointCreationMode.Create;
                        }
                    }
                    else
                    {
                        if (jointCreationMode == JointCreationMode.Create)
                        {
                            jointEndLimb = GetClosestLimbOnRagdoll(PlayerInput.MousePosition, l => l != null && l != jointStartLimb && l.ActiveSprite != null && !l.Hidden);
                            if (jointEndLimb != null && PlayerInput.PrimaryMouseButtonClicked())
                            {
                                Vector2 anchor1 = anchor1Pos ?? Vector2.Zero;
                                Vector2 anchor2 = ConvertUnits.ToDisplayUnits(jointEndLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                                CreateJoint(jointStartLimb.Params.ID, jointEndLimb.Params.ID, anchor1, anchor2);
                                jointCreationMode = JointCreationMode.None;
                            }
                        }
                        else if (PlayerInput.PrimaryMouseButtonClicked())
                        {
                            jointStartLimb = GetClosestLimbOnRagdoll(PlayerInput.MousePosition, l => selectedLimbs.Contains(l) && !l.Hidden);
                            anchor1Pos = ConvertUnits.ToDisplayUnits(jointStartLimb.body.FarseerBody.GetLocalPoint(ScreenToSim(PlayerInput.MousePosition)));
                            jointCreationMode = JointCreationMode.Create;
                        }
                    }
                }
                else
                {
                    jointCreationMode = JointCreationMode.None;
                }
            }
        }

        private void UpdateLimbCreation()
        {
            if (!isDrawingLimb)
            {
                newLimbRect = Rectangle.Empty;
                return;
            }
            if (!editLimbs)
            {
                SetToggle(limbsToggle, true);
            }
            if (PlayerInput.PrimaryMouseButtonHeld())
            {
                if (newLimbRect == Rectangle.Empty)
                {
                    newLimbRect = new Rectangle((int)PlayerInput.MousePosition.X, (int)PlayerInput.MousePosition.Y, 0, 0);
                }
                else
                {
                    newLimbRect.Size = new Point((int)PlayerInput.MousePosition.X - newLimbRect.X, (int)PlayerInput.MousePosition.Y - newLimbRect.Y);
                }
                newLimbRect.Size = new Point(Math.Max(newLimbRect.Width, 2), Math.Max(newLimbRect.Height, 2));
            }
            if (PlayerInput.PrimaryMouseButtonClicked())
            {
                // Take the offset and the zoom into account
                newLimbRect.Location = new Point(newLimbRect.X - spriteSheetOffsetX, newLimbRect.Y - spriteSheetOffsetY);
                newLimbRect = newLimbRect.Divide(spriteSheetZoom);
                CreateNewLimb(newLimbRect);
                isDrawingLimb = false;
                newLimbRect = Rectangle.Empty;
            }
        }

        private void CopyLimb(Limb limb)
        {
            if (limb == null) { return; }
            // TODO: copy all params and sub params -> use a generic method/reflection?
            var rect = limb.ActiveSprite.SourceRect;
            var spriteParams = limb.Params.GetSprite();
            var newLimbElement = new XElement("limb",
                new XAttribute("id", RagdollParams.Limbs.Last().ID + 1),
                new XAttribute("radius", limb.Params.Radius),
                new XAttribute("width", limb.Params.Width),
                new XAttribute("height", limb.Params.Height),
                new XElement("sprite",
                    new XAttribute("texture", spriteParams.Texture),
                    new XAttribute("sourcerect", $"{rect.X}, {rect.Y}, {rect.Size.X}, {rect.Size.Y}"))).FromPackage(character.Prefab.ContentPackage);
            CreateLimb(newLimbElement);
        }

        private void CreateNewLimb(Rectangle sourceRect)
        {
            var newLimbElement = new XElement("limb",
                new XAttribute("id", RagdollParams.Limbs.Last().ID + 1),
                new XAttribute("width", sourceRect.Width * RagdollParams.TextureScale),
                new XAttribute("height", sourceRect.Height * RagdollParams.TextureScale),
                new XElement("sprite",
                    new XAttribute("texture", RagdollParams.Limbs.First().GetSprite().Texture),
                    new XAttribute("sourcerect", $"{sourceRect.X}, {sourceRect.Y}, {sourceRect.Width}, {sourceRect.Height}"))).FromPackage(character.Prefab.ContentPackage);
            CreateLimb(newLimbElement);
            lockSpriteOriginToggle.Selected = false;
            recalculateColliderToggle.Selected = true;
        }

        private void CreateLimb(ContentXElement newElement)
        {
            var lastElement = RagdollParams.MainElement.GetChildElements("limb").LastOrDefault();
            if (lastElement != null)
            {
                lastElement.AddAfterSelf(newElement);
            }
            else
            {
                RagdollParams.MainElement.AddFirst(newElement);
            }
            var newLimbParams = new RagdollParams.LimbParams(newElement, RagdollParams);
            RagdollParams.Limbs.Add(newLimbParams);
            character.AnimController.Recreate();
            CreateTextures();
            TeleportTo(spawnPosition);
            ClearWidgets();
            ClearSelection();
            selectedLimbs.Add(character.AnimController.Limbs.Single(l => l.Params == newLimbParams));
            ResetParamsEditor();
        }

        /// <summary>
        /// Creates a new joint using the limb IDs.
        /// </summary>
        private void CreateJoint(int fromLimb, int toLimb, Vector2? anchor1 = null, Vector2? anchor2 = null)
        {
            if (RagdollParams.Joints.Any(j => j.Limb1 == fromLimb && j.Limb2 == toLimb))
            {
                DebugConsole.ThrowError(GetCharacterEditorTranslation("ExistingJointFound").Replace("[limbid1]", fromLimb.ToString()).Replace("[limbid2]", toLimb.ToString()));
                return;
            }
            //RagdollParams.StoreState();
            Vector2 a1 = anchor1 ?? Vector2.Zero;
            Vector2 a2 = anchor2 ?? Vector2.Zero;
            var newJointElement = new XElement("joint",
                new XAttribute("limb1", fromLimb),
                new XAttribute("limb2", toLimb),
                new XAttribute("limb1anchor", $"{a1.X.Format(2)}, {a1.Y.Format(2)}"),
                new XAttribute("limb2anchor", $"{a2.X.Format(2)}, {a2.Y.Format(2)}")
            ).FromPackage(character.Prefab.ContentPackage);
            var lastJointElement = RagdollParams.MainElement.GetChildElements("joint").LastOrDefault() ?? RagdollParams.MainElement.GetChildElements("limb").LastOrDefault();
            if (lastJointElement == null)
            {
                DebugConsole.ThrowError(GetCharacterEditorTranslation("CantAddJointsNoLimbElements"));
                return;
            }
            lastJointElement.AddAfterSelf(newJointElement);
            var newJointParams = new RagdollParams.JointParams(newJointElement, RagdollParams);
            RagdollParams.Joints.Add(newJointParams);
            character.AnimController.Recreate();
            CreateTextures();
            TeleportTo(spawnPosition);
            ClearWidgets();
            ClearSelection();
            SetToggle(jointsToggle, true);
            selectedJoints.Add(character.AnimController.LimbJoints.Single(j => j.Params == newJointParams));
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
                joint.Params.Element.Remove();
                RagdollParams.Joints.Remove(joint.Params);
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
                if (limb == character.AnimController.MainLimb)
                {
                    DebugConsole.ThrowError("Can't remove the main limb, because it will cause unreveratable issues.");
                    continue;
                }
                removedIDs.Add(limb.Params.ID);
                limb.Params.Element.Remove();
                RagdollParams.Limbs.Remove(limb.Params);
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
            var jointsToRemove = new List<RagdollParams.JointParams>();
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
        private Identifier currentCharacterIdentifier;
        private Identifier selectedJob = Identifier.Empty;

        private List<Identifier> allSpecies;
        private List<Identifier> AllSpecies
        {
            get
            {
                if (allSpecies == null)
                {
#if DEBUG
                    allSpecies = CharacterPrefab.Prefabs.Keys.OrderBy(p => p).ToList();
#else
                    allSpecies = CharacterPrefab.Prefabs.Keys.Where(p => !p.Contains("variant")).OrderBy(p => p).ToList();
#endif
                    allSpecies.ForEach(f => DebugConsole.NewMessage(f.Value, Color.White));
                }
                return allSpecies;
            }
        }

        private List<CharacterFile> vanillaCharacters;
        private List<CharacterFile> VanillaCharacters
        {
            get
            {
                if (vanillaCharacters == null)
                {
                    vanillaCharacters = GameMain.VanillaContent.GetFiles<CharacterFile>().ToList();
                }
                return vanillaCharacters;
            }
        }

        private Identifier GetNextCharacterIdentifier()
        {
            GetCurrentCharacterIndex();
            IncreaseIndex();
            currentCharacterIdentifier = AllSpecies[characterIndex];
            return currentCharacterIdentifier;
        }

        private Identifier GetPreviousCharacterIdentifier()
        {
            GetCurrentCharacterIndex();
            ReduceIndex();
            currentCharacterIdentifier = AllSpecies[characterIndex];
            return currentCharacterIdentifier;
        }

        private void GetCurrentCharacterIndex()
        {
            characterIndex = AllSpecies.IndexOf(character.SpeciesName);
        }

        private void IncreaseIndex()
        {
            characterIndex++;
            if (characterIndex > AllSpecies.Count - 1)
            {
                characterIndex = 0;
            }
        }

        private void ReduceIndex()
        {
            characterIndex--;
            if (characterIndex < 0)
            {
                characterIndex = AllSpecies.Count - 1;
            }
        }

        private Character SpawnCharacter(Identifier speciesName, RagdollParams ragdoll = null)
        {
            DebugConsole.NewMessage(GetCharacterEditorTranslation("TryingToSpawnCharacter").Replace("[config]", speciesName.ToString()), Color.HotPink);
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
            if (speciesName == CharacterPrefab.HumanSpeciesName && !selectedJob.IsEmpty)
            {
                var characterInfo = new CharacterInfo(speciesName, jobOrJobPrefab: JobPrefab.Prefabs[selectedJob.Value]);
                character = Character.Create(speciesName, spawnPosition, ToolBox.RandomSeed(8), characterInfo, hasAi: false, ragdoll: ragdoll);
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
                character = Character.Create(speciesName, spawnPosition, ToolBox.RandomSeed(8), hasAi: false, ragdoll: ragdoll);
                selectedJob = Identifier.Empty;
            }
            if (character != null)
            {
                character.dontFollowCursor = dontFollowCursor;
            }
            if (character == null)
            {
                if (currentCharacterIdentifier == speciesName)
                {
                    return null;
                }
                else
                {
                    // Respawn the current character;
                    SpawnCharacter(currentCharacterIdentifier);
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
        }

        private void OnPostSpawn()
        {
            currentCharacterIdentifier = character.SpeciesName;
            GetCurrentCharacterIndex();
            character.Submarine = Submarine.MainSub;
            character.AnimController.forceStanding = character.AnimController.CanWalk;
            character.AnimController.ForceSelectAnimationType = character.AnimController.CanWalk ? AnimationType.Walk : AnimationType.SwimSlow;
            Character.Controlled = character;
            SetWallCollisions(character.AnimController.forceStanding);
            CreateTextures();
            CreateGUI();
            ClearWidgets();
            ClearSelection();
            ResetParamsEditor();
            CurrentAnimation.StoreSnapshot();
            RagdollParams.StoreSnapshot();
            Cam.Position = character.WorldPosition;
            editedCharacters.Add(character);
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
            foreach (var w in jointSelectionWidgets.Values)
            {
                w.refresh();
                w.linkedWidget?.refresh();
            }
        }

        private void RecreateRagdoll(RagdollParams ragdoll = null)
        {
            RagdollParams.Apply();
            character.AnimController.Recreate(ragdoll);
            TeleportTo(spawnPosition);
            // For some reason Enumerable.Contains() method does not find the match, threfore the conversion to a list.
            var selectedJointParams = selectedJoints.Select(j => j.Params).ToList();
            var selectedLimbParams = selectedLimbs.Select(l => l.Params).ToList();
            CreateTextures();
            ClearWidgets();
            ClearSelection();
            foreach (var joint in character.AnimController.LimbJoints)
            {
                if (selectedJointParams.Contains(joint.Params))
                {
                    selectedJoints.Add(joint);
                }
            }
            foreach (var limb in character.AnimController.Limbs)
            {
                if (selectedLimbParams.Contains(limb.Params))
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

        public bool CreateCharacter(Identifier name, string mainFolder, bool isHumanoid, ContentPackage contentPackage, XElement ragdoll, XElement config = null, IEnumerable<AnimationParams> animations = null)
        {
            var vanilla = GameMain.VanillaContent;
            
            if (contentPackage == null)
            {
#if DEBUG
                contentPackage = ContentPackageManager.EnabledPackages.All.LastOrDefault();
#else
                contentPackage = ContentPackageManager.EnabledPackages.All.LastOrDefault(cp => cp != vanilla);
#endif
            }
            if (contentPackage == null)
            {
                // This should not be possible.
                DebugConsole.ThrowError(GetCharacterEditorTranslation("NoContentPackageSelected"));
                return false;
            }
#if !DEBUG
            if (vanilla != null && contentPackage == vanilla)
            {
                GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), GUIStyle.Red, font: GUIStyle.LargeFont);
                return false;
            }
#endif
            // Content package
            if (contentPackage is RegularPackage regular && !ContentPackageManager.EnabledPackages.Regular.Contains(regular))
            {
                ContentPackageManager.EnabledPackages.EnableRegular(regular);
            }
            GameSettings.SaveCurrentConfig();

            // Config file
            string configFilePath = Path.Combine(mainFolder, $"{name}.xml").Replace(@"\", @"/");
            var duplicate = CharacterPrefab.ConfigElements.FirstOrDefault(e => e.GetAttributeIdentifier("speciesname", Identifier.Empty) == name);
            XElement overrideElement = null;
            if (duplicate != null)
            {
                allSpecies = null;
                if (!File.Exists(configFilePath))
                {
                    // If the file exists, we just want to overwrite it.
                    // If the file does not exist, it's part of a different content package -> we'll want to override it.
                    overrideElement = new XElement("override");
                }
            }

            if (config == null)
            {
                config = new XElement("Character",
                    new XAttribute("speciesname", name),
                    new XAttribute("humanoid", isHumanoid),
                    new XElement("ragdolls", CreateRagdollPath()),
                    new XElement("animations", CreateAnimationPath()),
                    new XElement("health"),
                    new XElement("ai"));
            }
            else
            {
                config.SetAttributeValue("speciesname", name);
                config.SetAttributeValue("humanoid", isHumanoid);
                var ragdollElement = config.Element("ragdolls");
                if (ragdollElement == null)
                {
                    config.Add(new XElement("ragdolls", CreateRagdollPath()));
                }
                else
                {
                    var path = ragdollElement.GetAttributeString("folder", "");
                    if (!string.IsNullOrEmpty(path) && !path.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        ragdollElement.ReplaceWith(new XElement("ragdolls", CreateRagdollPath()));
                    }
                }
                var animationElement = config.Element("animations");
                if (animationElement == null)
                {
                    config.Add(new XElement("animations", CreateAnimationPath()));
                }
                else
                {
                    var path = animationElement.GetAttributeString("folder", "");
                    if (!string.IsNullOrEmpty(path) && !path.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        animationElement.ReplaceWith(new XElement("animations", CreateAnimationPath()));
                    }
                }
            }

            XAttribute CreateRagdollPath() => new XAttribute("folder", Path.Combine(mainFolder, $"Ragdolls/").Replace(@"\", @"/"));
            XAttribute CreateAnimationPath() => new XAttribute("folder", Path.Combine(mainFolder, $"Animations/").Replace(@"\", @"/"));

            if (overrideElement != null)
            {
                overrideElement.Add(config);
                config = overrideElement;
            }
            XDocument doc = new XDocument(config);
            
            ContentPath configFileContentPath = ContentPath.FromRaw(contentPackage, configFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(configFileContentPath.Value));
#if DEBUG
            doc.Save(configFileContentPath.Value);
#else
            doc.SaveSafe(configFileContentPath.Value);
#endif
            // Add to the selected content package
            var modProject = new ModProject(contentPackage);
            var newFile = ModProject.File.FromPath<CharacterFile>(configFilePath);
            modProject.AddFile(newFile);

            modProject.Save(contentPackage.Path);
            contentPackage = ContentPackageManager.ReloadContentPackage(contentPackage);

            DebugConsole.NewMessage(GetCharacterEditorTranslation("ContentPackageSaved").Replace("[path]", contentPackage.Path));      

            // Ragdoll
            RagdollParams.ClearCache();
            string ragdollPath = RagdollParams.GetDefaultFile(name, contentPackage);
            RagdollParams ragdollParams = isHumanoid
                ? RagdollParams.CreateDefault<HumanRagdollParams>(ragdollPath, name, ragdoll)
                : RagdollParams.CreateDefault<FishRagdollParams>(ragdollPath, name, ragdoll) as RagdollParams;

            // Animations
            AnimationParams.ClearCache();
            string animFolder = AnimationParams.GetFolder(name);
            if (animations != null)
            {
                if (!Directory.Exists(animFolder))
                {
                    Directory.CreateDirectory(animFolder);
                }
                foreach (var animation in animations)
                {
                    XElement element = animation.MainElement;
                    element.SetAttributeValue("type", name);
                    string fullPath = AnimationParams.GetDefaultFile(name, animation.AnimationType);
                    element.Name = AnimationParams.GetDefaultFileName(name, animation.AnimationType);
#if DEBUG
                    element.Save(fullPath);
#else
                    element.SaveSafe(fullPath);
#endif
                }
            }
            else
            {
                foreach (AnimationType animType in Enum.GetValues(typeof(AnimationType)))
                {
                    switch (animType)
                    {
                        case AnimationType.Walk:
                        case AnimationType.Run:
                            if (!ragdollParams.CanWalk) { continue; }
                            break;
                        case AnimationType.Crouch:
                            if (!ragdollParams.CanWalk || !isHumanoid) { continue; }
                            break;
                        case AnimationType.SwimSlow:
                        case AnimationType.SwimFast:
                            break;
                        default: continue;
                    }
                    Type type = AnimationParams.GetParamTypeFromAnimType(animType, isHumanoid);
                    string fullPath = AnimationParams.GetDefaultFile(name, animType);
                    AnimationParams.Create(fullPath, name, animType, type);
                }
            }
            if (!AllSpecies.Contains(name))
            {
                AllSpecies.Add(name);
            }
            SpawnCharacter(name, ragdollParams);
            limbPairEditing = false;
            limbsToggle.Selected = true;
            recalculateColliderToggle.Selected = true;
            lockSpriteOriginToggle.Selected = false;
            selectedLimbs.Add(character.AnimController.Limbs.First());
            return true;
        }

        private void ShowWearables()
        {
            if (character.Inventory == null) { return; }
            foreach (var item in character.Inventory.AllItems)
            {
                // Temp condition, todo: remove
                if (item.AllowedSlots.Contains(InvSlotType.Head) || item.AllowedSlots.Contains(InvSlotType.Headset)) { continue; }
                item.Equip(character);
            }
        }

        private void HideWearables()
        {
            character.Inventory?.AllItemsMod.ForEach(i => i.Unequip(character));
        }
#endregion

#region GUI
        private static Vector2 innerScale = new Vector2(0.95f, 0.95f);

        private GUILayoutGroup rightArea, leftArea;
        private GUIFrame centerArea;

        private GUIFrame characterSelectionPanel;
        private GUIFrame fileEditPanel;
        private GUIFrame modesPanel;
        private GUIFrame buttonsPanel;
        private GUIFrame optionsPanel;
        private GUIFrame minorModesPanel;

        private GUIFrame ragdollControls;
        private GUIFrame jointControls;
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
        private GUITickBox recalculateColliderToggle;
        private GUIFrame resetSpriteOrientationButtonParent;

        private GUITickBox characterInfoToggle;
        private GUITickBox ragdollToggle;
        private GUITickBox animsToggle;
        private GUITickBox limbsToggle;
        private GUITickBox paramsToggle;
        private GUITickBox jointsToggle;
        private GUITickBox spritesheetToggle;
        private GUITickBox skeletonToggle;
        private GUITickBox lightsToggle;
        private GUITickBox damageModifiersToggle;
        private GUITickBox ikToggle;
        private GUITickBox lockSpriteOriginToggle;

        private GUIFrame extraRagdollControls;
        private GUIButton createJointButton;
        private GUIButton createLimbButton;
        private GUIButton deleteSelectedButton;
        private GUIButton duplicateLimbButton;

        private ToggleButton modesToggle;
        private ToggleButton minorModesToggle;
        private ToggleButton buttonsPanelToggle;
        private ToggleButton optionsToggle;
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
            rightArea = new GUILayoutGroup(new RectTransform(new Vector2(0.15f, 1.0f), parent: Frame.RectTransform, anchor: Anchor.CenterRight), childAnchor: Anchor.BottomRight)
            {
                RelativeSpacing = 0.02f
            };
            centerArea = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.TopRight)
            {
                AbsoluteOffset = new Point((int)(rightArea.RectTransform.ScaledSize.X + rightArea.RectTransform.RelativeOffset.X * rightArea.RectTransform.Parent.ScaledSize.X + (int)(20 * GUI.xScale)), (int)(20 * GUI.yScale))

            }, style: null)
            { CanBeFocused = false };
            leftArea = new GUILayoutGroup(new RectTransform(new Vector2(0.15f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.CenterLeft), childAnchor: Anchor.BottomLeft)
            {
                RelativeSpacing = 0.02f
            };

            Vector2 toggleSize = new Vector2(1.0f, 0.03f);

            CreateFileEditPanel();
            CreateOptionsPanel(toggleSize);
            CreateCharacterSelectionPanel();
            if (rightArea.RectTransform.Children.Sum(c => c.Rect.Height) > GameMain.GraphicsHeight)
            {
                fileEditPanel.GetAllChildren().Where(c => c is GUIButton).ForEach(b => b.RectTransform.MinSize = ((GUIButton)b).Frame.RectTransform.MinSize = b.RectTransform.MinSize.Multiply(new Vector2(1.0f, 0.75f)));
                fileEditPanel.RectTransform.MinSize = new Point(0, (int)(fileEditPanel.GetChild<GUILayoutGroup>().RectTransform.Children.Sum(c => c.Rect.Height) / innerScale.Y));
                optionsPanel.GetAllChildren().Where(c => c is GUITickBox).ForEach(t => t.RectTransform.MinSize = t.RectTransform.MinSize.Multiply(new Vector2(1.0f, 0.75f)));
                optionsPanel.RectTransform.MinSize = new Point(0, (int)(optionsPanel.GetChild<GUILayoutGroup>().RectTransform.Children.Sum(c => c.Rect.Height) / innerScale.Y));
                rightArea.Recalculate();
            }

            CreateButtonsPanel();
            CreateModesPanel(toggleSize);
            CreateMinorModesPanel(toggleSize);

            CreateContextualControls();
        }

        private void CreateMinorModesPanel(Vector2 toggleSize)
        {
            minorModesPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), leftArea.RectTransform));
            var layoutGroup = new GUILayoutGroup(new RectTransform(innerScale, minorModesPanel.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 2,
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.03f, 0.0f), layoutGroup.RectTransform), GetCharacterEditorTranslation("MinorModesTitle"), font: GUIStyle.LargeFont);
            paramsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ShowParameters")) { Selected = showParamsEditor };
            paramsToggle.OnSelected = box =>
            {
                showParamsEditor = box.Selected;
                return true;
            };
            spritesheetToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ShowSpriteSheet")) { Selected = showSpritesheet };
            spritesheetToggle.OnSelected = box =>
            {
                showSpritesheet = box.Selected;
                return true;
            };
            showCollidersToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ShowColliders"))
            {
                Selected = showColliders,
                OnSelected = box =>
                {
                    showColliders = box.Selected;
                    return true;
                }
            };
            ikToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditIKTargets")) { Selected = editIK };
            ikToggle.OnSelected = box =>
            {
                editIK = box.Selected;
                return true;
            };
            skeletonToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("DrawSkeleton")) { Selected = drawSkeleton };
            skeletonToggle.OnSelected = box =>
            {
                drawSkeleton = box.Selected;
                return true;
            };
            lightsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EnableLights")) { Selected = GameMain.LightManager.LightingEnabled };
            lightsToggle.OnSelected = box =>
            {
                GameMain.LightManager.LightingEnabled = box.Selected;
                return true;
            };
            damageModifiersToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("DrawDamageModifiers")) { Selected = drawDamageModifiers };
            damageModifiersToggle.OnSelected = box =>
            {
                drawDamageModifiers = box.Selected;
                return true;
            };
            minorModesToggle = new ToggleButton(new RectTransform(new Vector2(0.08f, 1), minorModesPanel.RectTransform, Anchor.CenterRight, Pivot.CenterLeft), Direction.Left);
            minorModesPanel.RectTransform.MinSize = new Point(0, (int)(layoutGroup.RectTransform.Children.Sum(c => c.MinSize.Y + layoutGroup.AbsoluteSpacing) * 1.2f));
        }

        private void CreateModesPanel(Vector2 toggleSize)
        {
            modesPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), leftArea.RectTransform));
            var layoutGroup = new GUILayoutGroup(new RectTransform(innerScale, modesPanel.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 2,
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.03f, 0.0f), layoutGroup.RectTransform), GetCharacterEditorTranslation("ModesPanel"), font: GUIStyle.LargeFont);
            characterInfoToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditCharacter")) { Selected = editCharacterInfo };
            ragdollToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditRagdoll")) { Selected = editRagdoll };
            limbsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditLimbs")) { Selected = editLimbs };
            jointsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditJoints")) { Selected = editJoints };
            animsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("EditAnimations")) { Selected = editAnimations };
            animsToggle.OnSelected = box =>
            {
                editAnimations = box.Selected;
                if (editAnimations)
                {
                    SetToggle(limbsToggle, false);
                    SetToggle(jointsToggle, false);
                    SetToggle(ragdollToggle, false);
                    SetToggle(characterInfoToggle, false);
                    spritesheetToggle.Selected = false;
                }
                ClearSelection();
                ResetParamsEditor();
                return true;
            };
            limbsToggle.OnSelected = box =>
            {
                editLimbs = box.Selected;
                if (editLimbs)
                {
                    SetToggle(animsToggle, false);
                    SetToggle(jointsToggle, false);
                    SetToggle(ragdollToggle, false);
                    SetToggle(characterInfoToggle, false);
                    spritesheetToggle.Selected = true;
                }
                ClearSelection();
                ResetParamsEditor();
                return true;
            };
            jointsToggle.OnSelected = box =>
            {
                editJoints = box.Selected;
                if (editJoints)
                {
                    SetToggle(limbsToggle, false);
                    SetToggle(animsToggle, false);
                    SetToggle(ragdollToggle, false);
                    SetToggle(characterInfoToggle, false);
                    ikToggle.Selected = false;
                    spritesheetToggle.Selected = true;
                }
                ClearSelection();
                ResetParamsEditor();
                return true;
            };
            ragdollToggle.OnSelected = box =>
            {
                editRagdoll = box.Selected;
                if (editRagdoll)
                {
                    SetToggle(limbsToggle, false);
                    SetToggle(animsToggle, false);
                    SetToggle(jointsToggle, false);
                    SetToggle(characterInfoToggle, false);
                    paramsToggle.Selected = true;
                }
                ClearSelection();
                ResetParamsEditor();
                return true;
            };
            characterInfoToggle.OnSelected = box =>
            {
                editCharacterInfo = box.Selected;
                if (editCharacterInfo)
                {
                    SetToggle(limbsToggle, false);
                    SetToggle(animsToggle, false);
                    SetToggle(ragdollToggle, false);
                    SetToggle(jointsToggle, false);
                    paramsToggle.Selected = true;
                }
                ClearSelection();
                ResetParamsEditor();
                return true;
            };
            modesToggle = new ToggleButton(new RectTransform(new Vector2(0.08f, 1), modesPanel.RectTransform, Anchor.CenterRight, Pivot.CenterLeft), Direction.Left);
            modesPanel.RectTransform.MinSize = new Point(0, (int)(layoutGroup.RectTransform.Children.Sum(c => c.MinSize.Y + layoutGroup.AbsoluteSpacing) * 1.2f));
        }

        private void SetToggle(GUITickBox toggle, bool value)
        {
            if (toggle.Selected != value)
            {
                if (value)
                {
                    toggle.Box.Flash(GUIStyle.Green, useRectangleFlash: true);
                }
                else
                {
                    toggle.Box.Flash(GUIStyle.Red, useRectangleFlash: true);
                }
            }
            toggle.Selected = value;
        }

        private void CreateButtonsPanel()
        {
            buttonsPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), leftArea.RectTransform));
            Vector2 buttonSize = new Vector2(1, 0.45f);
            var parent = new GUIFrame(new RectTransform(new Vector2(0.85f, 0.70f), buttonsPanel.RectTransform, Anchor.Center), style: null);
            var reloadTexturesButton = new GUIButton(new RectTransform(buttonSize, parent.RectTransform, Anchor.TopCenter), GetCharacterEditorTranslation("ReloadTextures"));
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
            var recreateButton = new GUIButton(new RectTransform(buttonSize, parent.RectTransform, Anchor.BottomCenter), GetCharacterEditorTranslation("RecreateRagdoll"))
            {
                ToolTip = GetCharacterEditorTranslation("RecreateRagdollTooltip"),
                OnClicked = (button, data) =>
                {
                    RecreateRagdoll();
                    character.AnimController.ResetLimbs();
                    return true;
                }
            };
            GUITextBlock.AutoScaleAndNormalize(reloadTexturesButton.TextBlock, recreateButton.TextBlock);
            buttonsPanelToggle = new ToggleButton(new RectTransform(new Vector2(0.08f, 1), buttonsPanel.RectTransform, Anchor.CenterRight, Pivot.CenterLeft), Direction.Left);
            buttonsPanel.RectTransform.MinSize = new Point(0, (int)(parent.RectTransform.Children.Sum(c => c.MinSize.Y) * 1.5f));
        }


        private void CreateOptionsPanel(Vector2 toggleSize)
        {
            optionsPanel = new GUIFrame(new RectTransform(new Vector2(1, 0.3f), rightArea.RectTransform));
            var layoutGroup = new GUILayoutGroup(new RectTransform(innerScale, optionsPanel.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 2,
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.03f, 0.0f), layoutGroup.RectTransform), GetCharacterEditorTranslation("OptionsPanel"), font: GUIStyle.LargeFont);
            freezeToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("Freeze"))
            {
                Selected = isFrozen,
                OnSelected = box =>
                {
                    isFrozen = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("AutoFreeze"))
            {
                Selected = autoFreeze,
                OnSelected = box =>
                {
                    autoFreeze = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("LimbPairEditing"))
            {
                Selected = limbPairEditing,
                Enabled = character.IsHumanoid,
                OnSelected = box =>
                {
                    limbPairEditing = box.Selected;
                    return true;
                }
            };
            animTestPoseToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("AnimationTestPose"))
            {
                Selected = character.AnimController.AnimationTestPose,
                Enabled = true,
                OnSelected = box =>
                {
                    character.AnimController.AnimationTestPose = box.Selected;
                    return true;
                }
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
            optionsToggle = new ToggleButton(new RectTransform(new Vector2(0.08f, 1), optionsPanel.RectTransform, Anchor.CenterLeft, Pivot.CenterRight), Direction.Right);
            optionsPanel.RectTransform.MinSize = new Point(0, (int)(layoutGroup.RectTransform.Children.Sum(c => c.MinSize.Y + layoutGroup.AbsoluteSpacing) * 1.2f));
        }

        private void CreateContextualControls()
        {
            Point elementSize = new Point(120, 20).Multiply(GUI.Scale);
            int textAreaHeight = 20;
            // General controls
            backgroundColorPanel = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerArea.RectTransform, Anchor.TopRight)
            {
                AbsoluteOffset = new Point(10, 0).Multiply(GUI.Scale)
            }, style: null)
            {
                CanBeFocused = false
            };
            // Background color
            var frame = new GUIFrame(new RectTransform(new Point(500, 80).Multiply(GUI.Scale), backgroundColorPanel.RectTransform, Anchor.TopRight), style: null, color: Color.Black * 0.4f);
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), frame.RectTransform)
            {
                MinSize = new Point(80, 26)
            }, GetCharacterEditorTranslation("BackgroundColor") + ":", textColor: Color.WhiteSmoke);
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1), frame.RectTransform, Anchor.TopRight)
            {
                AbsoluteOffset = new Point(20, 0).Multiply(GUI.Scale)
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
                    font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int, relativeButtonAreaWidth: 0.25f)
                {
                    Font = GUIStyle.SmallFont
                };
                numberInput.MinValueInt = 0;
                numberInput.MaxValueInt = 255;
                numberInput.Font = GUIStyle.SmallFont;
                switch (i)
                {
                    case 0:
                        colorLabel.TextColor = GUIStyle.Red;
                        numberInput.IntValue = backgroundColor.R;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.R = (byte)numInput.IntValue;
                        break;
                    case 1:
                        colorLabel.TextColor = GUIStyle.Green;
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
            spriteSheetControls = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerArea.RectTransform, Anchor.BottomLeft)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, style: null)
            {
                CanBeFocused = false
            };
            var layoutGroupSpriteSheet = new GUILayoutGroup(new RectTransform(Vector2.One, spriteSheetControls.RectTransform))
            {
                AbsoluteSpacing = 5,
                CanBeFocused = false
            };
            new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), GetCharacterEditorTranslation("SpriteSheetZoom") + ":", Color.White);
            var spriteSheetControlElement = new GUIFrame(new RectTransform(new Point(elementSize.X * 2, textAreaHeight), layoutGroupSpriteSheet.RectTransform), style: null);
            CalculateSpritesheetZoom();
            spriteSheetZoomBar = new GUIScrollBar(new RectTransform(new Vector2(0.69f, 1), spriteSheetControlElement.RectTransform, Anchor.CenterLeft), barSize: 0.2f, style: "GUISlider")
            {
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(spriteSheetMinZoom, spriteSheetMaxZoom, spriteSheetZoom)),
                Step = 0.01f,
                OnMoved = (scrollBar, value) =>
                {
                    spriteSheetZoom = MathHelper.Lerp(spriteSheetMinZoom, spriteSheetMaxZoom, value);
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.25f), spriteSheetControlElement.RectTransform, Anchor.CenterRight), GetCharacterEditorTranslation("Reset"), style: "GUIButtonFreeScale")
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
            new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), GetCharacterEditorTranslation("Unrestrict"))
            {
                TextColor = Color.White,
                Selected = unrestrictSpritesheet,
                OnSelected = (GUITickBox box) =>
                {
                    SetSpritesheetRestriction(box.Selected);
                    return true;
                }
            };
            resetSpriteOrientationButtonParent = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.025f), centerArea.RectTransform, Anchor.BottomCenter)
            {
                AbsoluteOffset = new Point(0, -5).Multiply(GUI.Scale),
                RelativeOffset = new Vector2(-0.05f, 0)
            }, style: null)
            {
                CanBeFocused = false
            };
            new GUIButton(new RectTransform(Vector2.One, resetSpriteOrientationButtonParent.RectTransform, Anchor.TopRight), GetCharacterEditorTranslation("Reset"), style: "GUIButtonFreeScale")
            {
                OnClicked = (box, data) =>
                {
                    IEnumerable<Limb> limbs = selectedLimbs;
                    if (limbs.None())
                    {
                        limbs = selectedJoints.Select(j => PlayerInput.KeyDown(Keys.LeftAlt) ? j.LimbB : j.LimbA);
                    }
                    foreach (var limb in limbs)
                    {
                        TryUpdateSubParam(limb.Params, "spriteorientation".ToIdentifier(), float.NaN);
                        if (limbPairEditing)
                        {
                            UpdateOtherLimbs(limb, l => TryUpdateSubParam(l.Params, "spriteorientation".ToIdentifier(), float.NaN));
                        }
                    }
                    return true;
                }
            };
            // Limb controls
            limbControls = new GUIFrame(new RectTransform(Vector2.One, centerArea.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupLimbControls = new GUILayoutGroup(new RectTransform(Vector2.One, limbControls.RectTransform), childAnchor: Anchor.TopLeft) { CanBeFocused = false };
            lockSpriteOriginToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("LockSpriteOrigin"))
            {
                TextColor = Color.White,
                Selected = lockSpriteOrigin,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpriteOrigin = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("LockSpritePosition"))
            {
                TextColor = Color.White,
                Selected = lockSpritePosition,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpritePosition = box.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("LockSpriteSize"))
            {
                TextColor = Color.White,
                Selected = lockSpriteSize,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpriteSize = box.Selected;
                    return true;
                }
            };
            recalculateColliderToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("AdjustCollider"))
            {
                TextColor = Color.White,
                Selected = recalculateCollider,
                OnSelected = (GUITickBox box) =>
                {
                    recalculateCollider = box.Selected;
                    showCollidersToggle.Selected = recalculateCollider;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupLimbControls.RectTransform), GetCharacterEditorTranslation("OnlyShowSelectedLimbs"))
            {
                TextColor = Color.White,
                Selected = onlyShowSourceRectForSelectedLimbs,
                OnSelected = (GUITickBox box) =>
                {
                    onlyShowSourceRectForSelectedLimbs = box.Selected;
                    return true;
                }
            };

            // Joint controls
            Point sliderSize = new Point(300, 20).Multiply(GUI.Scale);
            jointControls = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.075f), centerArea.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupJoints = new GUILayoutGroup(new RectTransform(Vector2.One, jointControls.RectTransform), childAnchor: Anchor.TopLeft) { CanBeFocused = false };
            copyJointsToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupJoints.RectTransform), GetCharacterEditorTranslation("CopyJointSettings"))
            {
                ToolTip = GetCharacterEditorTranslation("CopyJointSettingsTooltip"),
                Selected = copyJointSettings,
                TextColor = copyJointSettings ? GUIStyle.Red : Color.White,
                OnSelected = (GUITickBox box) =>
                {
                    copyJointSettings = box.Selected;
                    box.TextColor = copyJointSettings ? GUIStyle.Red : Color.White;
                    return true;
                }
            };
            // Ragdoll controls
            ragdollControls = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.25f), centerArea.RectTransform), style: null) { CanBeFocused = false };
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
            var jointScaleElement = new GUIFrame(new RectTransform(sliderSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var jointScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), jointScaleElement.RectTransform), $"{GetCharacterEditorTranslation("JointScale")}: {RagdollParams.JointScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
            var limbScaleElement = new GUIFrame(new RectTransform(sliderSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var limbScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), limbScaleElement.RectTransform), $"{GetCharacterEditorTranslation("LimbScale")}: {RagdollParams.LimbScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
            jointScaleBar = new GUIScrollBar(new RectTransform(sliderSize, jointScaleElement.RectTransform, Anchor.BottomLeft), barSize: 0.1f, style: "GUISlider")
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
            limbScaleBar = new GUIScrollBar(new RectTransform(sliderSize, limbScaleElement.RectTransform, Anchor.BottomLeft), barSize: 0.1f, style: "GUISlider")
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
                RagdollParams.StoreSnapshot();
                return true;
            };
            jointScaleBar.Bar.OnClicked += (button, data) =>
            {
                if (uniformScaling)
                {
                    RecreateRagdoll();
                }
                RagdollParams.StoreSnapshot();
                return true;
            };

            // Just an approximation
            Point buttonSize = new Point(200, 40).Multiply(GUI.Scale);
            extraRagdollControls = new GUIFrame(new RectTransform(new Point(buttonSize.X, buttonSize.Y * 4), centerArea.RectTransform, Anchor.BottomRight)
            {
                AbsoluteOffset = new Point(30, 0).Multiply(GUI.Scale),
                MinSize = new Point(0, 120)
            }, style: null, color: Color.Black)
            {
                CanBeFocused = false
            };
            var paddedFrame = new GUILayoutGroup(new RectTransform(Vector2.One * 0.95f, extraRagdollControls.RectTransform, Anchor.Center))
            {
                Stretch = true,
                AbsoluteSpacing = 5
            };
            var buttons = GUI.CreateButtons(4, new Vector2(1, 0.25f), paddedFrame.RectTransform, Anchor.TopCenter, style: "GUIButtonSmallFreeScale");
            deleteSelectedButton = buttons[0];
            deleteSelectedButton.Text = GetCharacterEditorTranslation("DeleteSelected");
            deleteSelectedButton.OnClicked = (button, data) =>
            {
                DeleteSelected();
                return true;
            };
            duplicateLimbButton = buttons[1];
            duplicateLimbButton.Text = GetCharacterEditorTranslation("DuplicateLimb");
            duplicateLimbButton.OnClicked = (button, data) =>
            {
                CopyLimb(selectedLimbs.FirstOrDefault());
                return true;
            };
            createJointButton = buttons[2];
            createJointButton.Text = GetCharacterEditorTranslation("CreateJoint");
            createJointButton.OnClicked = (button, data) =>
            {
                ToggleJointCreationMode();
                return true;
            };
            createLimbButton = buttons[3];
            createLimbButton.Text = GetCharacterEditorTranslation("CreateLimb");
            createLimbButton.OnClicked = (button, data) =>
            {
                ToggleLimbCreationMode();
                return true;
            };
            GUITextBlock.AutoScaleAndNormalize(buttons.Select(b => b.TextBlock));

            // Animation
            animationControls = new GUIFrame(new RectTransform(Vector2.One, centerArea.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupAnimation = new GUILayoutGroup(new RectTransform(Vector2.One, animationControls.RectTransform), childAnchor: Anchor.TopLeft) { CanBeFocused = false };
            var animationSelectionElement = new GUIFrame(new RectTransform(new Point(elementSize.X * 2 - (int)(5 * GUI.xScale), elementSize.Y), layoutGroupAnimation.RectTransform), style: null);
            var animationSelectionText = new GUITextBlock(new RectTransform(new Point(elementSize.X, elementSize.Y), animationSelectionElement.RectTransform), GetCharacterEditorTranslation("SelectedAnimation") + ": ", Color.WhiteSmoke, textAlignment: Alignment.Center);
            animSelection = new GUIDropDown(new RectTransform(new Point((int)(100 * GUI.xScale), elementSize.Y), animationSelectionElement.RectTransform, Anchor.TopRight), elementCount: 5);
            if (character.AnimController.CanWalk)
            {
                animSelection.AddItem(AnimationType.Walk.ToString(), AnimationType.Walk);
                animSelection.AddItem(AnimationType.Run.ToString(), AnimationType.Run);
            }
            animSelection.AddItem(AnimationType.SwimSlow.ToString(), AnimationType.SwimSlow);
            animSelection.AddItem(AnimationType.SwimFast.ToString(), AnimationType.SwimFast);
            if (character.AnimController.CanWalk && character.IsHumanoid)
            {
                animSelection.AddItem(AnimationType.Crouch.ToString(), AnimationType.Crouch);
            }
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
                    case AnimationType.Run:
                    case AnimationType.Crouch:
                        character.AnimController.forceStanding = true;
                        character.ForceRun = character.AnimController.ForceSelectAnimationType == AnimationType.Run;
                        if (!wallCollisionsEnabled)
                        {
                            SetWallCollisions(true);
                        }
                        if (previousAnim != AnimationType.Walk && previousAnim != AnimationType.Run && previousAnim != AnimationType.Crouch)
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
                ResetParamsEditor();
                return true;
            };
        }

        private void CreateCharacterSelectionPanel()
        {
            characterSelectionPanel = new GUIFrame(new RectTransform(new Vector2(1, 0.2f), rightArea.RectTransform));
            var content = new GUILayoutGroup(new RectTransform(innerScale, characterSelectionPanel.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            // Character selection
            var characterLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), GetCharacterEditorTranslation("CharacterPanel"), font: GUIStyle.LargeFont);
            var disclaimerBtn = new GUIButton(new RectTransform(new Vector2(0.2f, 0.7f), characterLabel.RectTransform, Anchor.CenterRight), style: "GUINotificationButton")
            {
                OnClicked = (btn, userdata) => { GameMain.Instance.ShowEditorDisclaimer(); return true; }
            };

            var characterDropDown = new GUIDropDown(new RectTransform(new Vector2(1, 0.2f), content.RectTransform)
            {
                RelativeOffset = new Vector2(0, 0.2f)
            }, elementCount: 8, style: null);
            characterDropDown.ListBox.Color = new Color(characterDropDown.ListBox.Color.R, characterDropDown.ListBox.Color.G, characterDropDown.ListBox.Color.B, byte.MaxValue);
            foreach (var file in AllSpecies)
            {
                characterDropDown.AddItem(file.Value.CapitaliseFirstInvariant(), file);
            }
            characterDropDown.SelectItem(currentCharacterIdentifier);
            characterDropDown.OnSelected = (component, data) =>
            {
                Identifier characterIdentifier = (Identifier)data;
                try
                {
                    SpawnCharacter(characterIdentifier);
                }
                catch (Exception e)
                {
                    HandleSpawnException(characterIdentifier, e);
                }
                return true;
            };
            if (currentCharacterIdentifier == CharacterPrefab.HumanSpeciesName)
            {
                var jobDropDown = new GUIDropDown(new RectTransform(new Vector2(1, 0.15f), content.RectTransform)
                {
                    RelativeOffset = new Vector2(0, 0.45f)
                }, elementCount: 8, style: null);
                jobDropDown.ListBox.Color = new Color(jobDropDown.ListBox.Color.R, jobDropDown.ListBox.Color.G, jobDropDown.ListBox.Color.B, byte.MaxValue);
                jobDropDown.AddItem("None");
                JobPrefab.Prefabs.ForEach(j => jobDropDown.AddItem(j.Name, j.Identifier));
                jobDropDown.SelectItem(selectedJob);
                jobDropDown.OnSelected = (component, data) =>
                {
                    Identifier newJob = data is Identifier jobIdentifier ? jobIdentifier : Identifier.Empty;
                    if (newJob != selectedJob)
                    {
                        selectedJob = newJob;
                        SpawnCharacter(currentCharacterIdentifier);
                    }
                    return true;
                };
            }
            var charButtons = new GUIFrame(new RectTransform(new Vector2(1, 0.25f), parent: content.RectTransform, anchor: Anchor.BottomLeft), style: null);
            var prevCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), charButtons.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("PreviousCharacter"));
            prevCharacterButton.TextBlock.AutoScaleHorizontal = true;
            prevCharacterButton.OnClicked += (b, obj) =>
            {
                Identifier characterIdentifier = GetPreviousCharacterIdentifier();
                try
                {
                    SpawnCharacter(characterIdentifier);
                }
                catch (Exception e)
                {
                    HandleSpawnException(characterIdentifier, e);
                }
                return true;
            };
            var nextCharacterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), charButtons.RectTransform, Anchor.TopRight), GetCharacterEditorTranslation("NextCharacter"));
            prevCharacterButton.TextBlock.AutoScaleHorizontal = true;
            nextCharacterButton.OnClicked += (b, obj) =>
            {
                Identifier characterIdentifier = GetNextCharacterIdentifier();
                try
                {
                    SpawnCharacter(characterIdentifier);
                }
                catch (Exception e)
                {
                    HandleSpawnException(characterIdentifier, e);
                }
                return true;
            };
            charButtons.RectTransform.MinSize = new Point(0, prevCharacterButton.RectTransform.MinSize.Y);
            characterPanelToggle = new ToggleButton(new RectTransform(new Vector2(0.08f, 1), characterSelectionPanel.RectTransform, Anchor.CenterLeft, Pivot.CenterRight), Direction.Right);
            characterSelectionPanel.RectTransform.MinSize = new Point(0, (int)(content.RectTransform.Children.Sum(c => c.MinSize.Y) * 1.2f));

            void HandleSpawnException(Identifier characterIdentifier, Exception e)
            {
                if (characterIdentifier != CharacterPrefab.HumanSpeciesName)
                {
                    DebugConsole.ThrowError($"Failed to spawn the character \"{characterIdentifier}\".", e);
                    SpawnCharacter(CharacterPrefab.HumanSpeciesName);
                }
                else
                {
                    throw new Exception($"Failed to spawn the character \"{characterIdentifier}\".", innerException: e);
                }
            }
        }

        private void CreateFileEditPanel()
        {
            Vector2 buttonSize = new Vector2(1, 0.04f);

            fileEditPanel = new GUIFrame(new RectTransform(new Vector2(1, 0.4f), rightArea.RectTransform));
            var layoutGroup = new GUILayoutGroup(new RectTransform(innerScale, fileEditPanel.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = 1,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(0.03f, 0.0f), layoutGroup.RectTransform), GetCharacterEditorTranslation("FileEditPanel"), font: GUIStyle.LargeFont);

            // Spacing
            new GUIFrame(new RectTransform(buttonSize / 2, layoutGroup.RectTransform), style: null) { CanBeFocused = false };
            var saveAllButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), TextManager.Get("editor.saveall"));
            saveAllButton.Color = GUIStyle.Green;
            saveAllButton.OnClicked += (button, userData) =>
            {
#if !DEBUG
                if (VanillaCharacters != null && VanillaCharacters.Contains(CharacterPrefab.Prefabs[currentCharacterIdentifier].ContentFile))
                {
                    GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), GUIStyle.Red, font: GUIStyle.LargeFont);
                    return false;
                }
#endif
                if (!character.IsHuman && !string.IsNullOrEmpty(RagdollParams.Texture) && !File.Exists(RagdollParams.Texture))
                {
                    DebugConsole.ThrowError($"Invalid texture path: {RagdollParams.Texture}");
                    return false;
                }
                else
                {
                    character.Params.Save();
                    GUI.AddMessage(GetCharacterEditorTranslation("CharacterSavedTo").Replace("[path]", CharacterParams.Path.Value), GUIStyle.Green, font: GUIStyle.Font, lifeTime: 5);
                    character.AnimController.SaveRagdoll();
                    GUI.AddMessage(GetCharacterEditorTranslation("RagdollSavedTo").Replace("[path]", RagdollParams.Path.Value), GUIStyle.Green, font: GUIStyle.Font, lifeTime: 5);
                    AnimParams.ForEach(p => p.Save());
                }
                return true;
            };

            // Spacing
            new GUIFrame(new RectTransform(buttonSize / 2, layoutGroup.RectTransform), style: null) { CanBeFocused = false };

            Vector2 messageBoxRelSize = new Vector2(0.5f, 0.7f);
            var saveRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("SaveRagdoll"));
            saveRagdollButton.OnClicked += (button, userData) =>
            {
                var box = new GUIMessageBox(GetCharacterEditorTranslation("SaveRagdoll"), $"{GetCharacterEditorTranslation("ProvideFileName")}: ", new LocalizedString[] { TextManager.Get("Cancel"), TextManager.Get("Save") }, messageBoxRelSize);
                var inputField = new GUITextBox(new RectTransform(new Point(box.Content.Rect.Width, (int)(30 * GUI.yScale)), box.Content.RectTransform, Anchor.Center), RagdollParams.Name.RemoveWhitespace());
                box.Buttons[0].OnClicked += (b, d) =>
                {
                    box.Close();
                    return true;
                };
                box.Buttons[1].OnClicked += (b, d) =>
                {
#if !DEBUG
                    if (VanillaCharacters != null && VanillaCharacters.Contains(CharacterPrefab.Prefabs[currentCharacterIdentifier].ContentFile))
                    {
                        GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), GUIStyle.Red, font: GUIStyle.LargeFont);
                        box.Close();
                        return false;
                    }
#endif
                    character.AnimController.SaveRagdoll(inputField.Text);
                    GUI.AddMessage(GetCharacterEditorTranslation("RagdollSavedTo").Replace("[path]", RagdollParams.Path.Value), Color.Green, font: GUIStyle.Font);
                    box.Close();
                    return true;
                };
                return true;
            };
            var loadRagdollButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("LoadRagdoll"));
            loadRagdollButton.OnClicked += (button, userData) =>
            {
                var loadBox = new GUIMessageBox(GetCharacterEditorTranslation("LoadRagdoll"), "", new LocalizedString[] { TextManager.Get("Cancel"), TextManager.Get("Load"), TextManager.Get("Delete") }, messageBoxRelSize);
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
                                ToolBox.LimitString(Path.GetFileNameWithoutExtension(path), GUIStyle.Font, listBox.Rect.Width - 80))
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
                        TextManager.GetWithVariable("DeleteDialogQuestion", "[file]", selectedFile),
                        new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
                    msgBox.Buttons[0].OnClicked += (b, d) =>
                    {
                        try
                        {
                            File.Delete(selectedFile);
                            GUI.AddMessage(GetCharacterEditorTranslation("RagdollDeletedFrom").Replace("[file]", selectedFile), GUIStyle.Red, font: GUIStyle.Font);
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
                    GUI.AddMessage(GetCharacterEditorTranslation("RagdollLoadedFrom").Replace("[file]", selectedFile), Color.WhiteSmoke, font: GUIStyle.Font);
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
                var box = new GUIMessageBox(GetCharacterEditorTranslation("SaveAnimation"), string.Empty, new LocalizedString[] { TextManager.Get("Cancel"), TextManager.Get("Save") }, messageBoxRelSize);
                var textArea = new GUIFrame(new RectTransform(new Vector2(1, 0.1f), box.Content.RectTransform) { MinSize = new Point(350, 30) }, style: null);
                var inputLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), textArea.RectTransform, Anchor.CenterLeft) { MinSize = new Point(250, 30) }, $"{GetCharacterEditorTranslation("ProvideFileName")}: ");
                var inputField = new GUITextBox(new RectTransform(new Vector2(0.45f, 1), textArea.RectTransform, Anchor.CenterRight) { MinSize = new Point(100, 30) }, CurrentAnimation.Name);
                // Type filtering
                var typeSelectionArea = new GUIFrame(new RectTransform(new Vector2(1f, 0.1f), box.Content.RectTransform) { MinSize = new Point(0, 30) }, style: null);
                var typeLabel = new GUITextBlock(new RectTransform(new Vector2(0.45f, 1), typeSelectionArea.RectTransform, Anchor.CenterLeft), $"{GetCharacterEditorTranslation("SelectAnimationType")}: ");
                var typeDropdown = new GUIDropDown(new RectTransform(new Vector2(0.45f, 1), typeSelectionArea.RectTransform, Anchor.CenterRight), elementCount: 4);
                foreach (object enumValue in Enum.GetValues(typeof(AnimationType)))
                {
                    if (!(enumValue is AnimationType.NotDefined))
                    {
                        typeDropdown.AddItem(enumValue.ToString(), enumValue);
                    }
                }
                AnimationType selectedType = character.AnimController.ForceSelectAnimationType;
                typeDropdown.OnSelected = (component, data) =>
                {
                    selectedType = (AnimationType)data;
                    inputField.Text = character.AnimController.GetAnimationParamsFromType(selectedType)?.Name.RemoveWhitespace();
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
                    if (VanillaCharacters != null && VanillaCharacters.Contains(CharacterPrefab.Prefabs[currentCharacterIdentifier].ContentFile))
                    {
                        GUI.AddMessage(GetCharacterEditorTranslation("CannotEditVanillaCharacters"), GUIStyle.Red, font: GUIStyle.LargeFont);
                        box.Close();
                        return false;
                    }
#endif
                    var animParams = character.AnimController.GetAnimationParamsFromType(selectedType);
                    if (animParams == null) { return true; }
                    animParams.Save(inputField.Text);
                    GUI.AddMessage(GetCharacterEditorTranslation("AnimationOfTypeSavedTo").Replace("[type]", animParams.AnimationType.ToString()).Replace("[path]", animParams.Path.Value), Color.Green, font: GUIStyle.Font);
                    ResetParamsEditor();
                    box.Close();
                    return true;
                };
                return true;
            };
            var loadAnimationButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("LoadAnimation"));
            loadAnimationButton.OnClicked += (button, userData) =>
            {
                var loadBox = new GUIMessageBox(GetCharacterEditorTranslation("LoadAnimation"), "", new LocalizedString[] { TextManager.Get("Cancel"), TextManager.Get("Load"), TextManager.Get("Delete") }, messageBoxRelSize);
                loadBox.Buttons[0].OnClicked += loadBox.Close;
                var listBox = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.6f), loadBox.Content.RectTransform));
                var deleteButton = loadBox.Buttons[2];
                deleteButton.Enabled = false;
                // Type filtering
                var typeSelectionArea = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.1f), loadBox.Content.RectTransform) { MinSize = new Point(0, 30) }, style: null);
                var typeLabel = new GUITextBlock(new RectTransform(new Vector2(0.45f, 1), typeSelectionArea.RectTransform, Anchor.CenterLeft), $"{GetCharacterEditorTranslation("SelectAnimationType")}: ");
                var typeDropdown = new GUIDropDown(new RectTransform(new Vector2(0.45f, 1), typeSelectionArea.RectTransform, Anchor.CenterRight), elementCount: 4);
                foreach (object enumValue in Enum.GetValues(typeof(AnimationType)))
                {
                    if (!(enumValue is AnimationType.NotDefined))
                    {
                        typeDropdown.AddItem(enumValue.ToString(), enumValue);
                    }
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
                            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform) { MinSize = new Point(0, 30) }, ToolBox.LimitString(Path.GetFileNameWithoutExtension(path), GUIStyle.Font, listBox.Rect.Width - 80))
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
                        TextManager.GetWithVariable("DeleteDialogQuestion", "[file]", selectedFile),
                        new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
                    msgBox.Buttons[0].OnClicked += (b, d) =>
                    {
                        try
                        {
                            File.Delete(selectedFile);
                            GUI.AddMessage(GetCharacterEditorTranslation("AnimationOfTypeDeleted").Replace("[type]", selectedType.ToString()).Replace("[file]", selectedFile), GUIStyle.Red, font: GUIStyle.Font);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError(TextManager.GetWithVariable("DeleteFileError", "[file]", selectedFile), e);
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
                    if (character.IsHumanoid && character.AnimController is HumanoidAnimController humanAnimController)
                    {
                        switch (selectedType)
                        {
                            case AnimationType.Walk:
                                humanAnimController.WalkParams = HumanWalkParams.GetAnimParams(character, fileName);
                            break;
                            case AnimationType.Run:
                                humanAnimController.RunParams = HumanRunParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.Crouch:
                                humanAnimController.HumanCrouchParams = HumanCrouchParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.SwimSlow:
                                humanAnimController.SwimSlowParams = HumanSwimSlowParams.GetAnimParams(character, fileName);
                                break;
                            case AnimationType.SwimFast:
                                humanAnimController.SwimFastParams = HumanSwimFastParams.GetAnimParams(character, fileName);
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
                    GUI.AddMessage(GetCharacterEditorTranslation("AnimationOfTypeLoaded").Replace("[type]", selectedType.ToString()).Replace("[file]", selectedFile), Color.WhiteSmoke, font: GUIStyle.Font);
                    character.AnimController.AllAnimParams.ForEach(a => a.Reset(forceReload: true));
                    ResetParamsEditor();
                    loadBox.Close();
                    return true;
                };
                return true;
            };

            // Spacing
            new GUIFrame(new RectTransform(buttonSize / 2, layoutGroup.RectTransform), style: null) { CanBeFocused = false };
            var resetButton = new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("ResetButton"));
            resetButton.Color = GUIStyle.Red;
            resetButton.OnClicked += (button, userData) =>
            {
                CharacterParams.Reset(true);
                AnimParams.ForEach(p => p.Reset(true));
                character.AnimController.ResetRagdoll(forceReload: true);
                RecreateRagdoll();
                jointCreationMode = JointCreationMode.None;
                isDrawingLimb = false;
                newLimbRect = Rectangle.Empty;
                jointStartLimb = null;
                CreateGUI();
                return true;
            };

            // Spacing
            new GUIFrame(new RectTransform(buttonSize / 2, layoutGroup.RectTransform), style: null) { CanBeFocused = false };
            new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("CreateNewCharacter"))
            {
                OnClicked = (button, data) =>
                {
                    ResetView();
                    Wizard.Instance.SelectTab(Wizard.Tab.Character);
                    return true;
                }
            };
            new GUIButton(new RectTransform(buttonSize, layoutGroup.RectTransform), GetCharacterEditorTranslation("CopyCharacter"))
            {
                ToolTip = GetCharacterEditorTranslation("CopyCharacterToolTip"),
                OnClicked = (button, data) =>
                {
                    ResetView();
                    CharacterParams.Serialize();
                    RagdollParams.Serialize();
                    AnimParams.ForEach(a => a.Serialize());
                    Wizard.Instance.CopyExisting(CharacterParams, RagdollParams, AnimParams);
                    Wizard.Instance.SelectTab(Wizard.Tab.Character);
                    return true;
                }
            };

            GUITextBlock.AutoScaleAndNormalize(layoutGroup.Children.Where(c => c is GUIButton).Select(c => ((GUIButton)c).TextBlock));

            fileEditToggle = new ToggleButton(new RectTransform(new Vector2(0.08f, 1), fileEditPanel.RectTransform, Anchor.CenterLeft, Pivot.CenterRight), Direction.Right);

            void ResetView()
            {
                characterInfoToggle.Selected = false;
                ragdollToggle.Selected = false;
                limbsToggle.Selected = false;
                animsToggle.Selected = false;
                spritesheetToggle.Selected = false;
                jointsToggle.Selected = false;
                paramsToggle.Selected = false;
                skeletonToggle.Selected = false;
                damageModifiersToggle.Selected = false;
            }

            fileEditPanel.RectTransform.MinSize = new Point(0, (int)(layoutGroup.RectTransform.Children.Sum(c => c.MinSize.Y + layoutGroup.AbsoluteSpacing) * 1.2f));
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
                panel.AbsoluteOffset = new Vector2(MathHelper.SmoothStep(hiddenPos.X, 0.0f, OpenState), panel.AbsoluteOffset.Y).ToPoint();
                OpenState = isHidden ? Math.Max(OpenState - deltaTime * 5, 0) : Math.Min(OpenState + deltaTime * 5, 1);
            }
        }

#endregion

#region Params
        private CharacterParams CharacterParams => character.Params;
        private List<AnimationParams> AnimParams => character.AnimController.AllAnimParams;
        private AnimationParams CurrentAnimation => character.AnimController.CurrentAnimationParams;
        private RagdollParams RagdollParams => character.AnimController.RagdollParams;
        
        private void ResetParamsEditor()
        {
            ParamsEditor.Instance.Clear();
            if (!editRagdoll && !editCharacterInfo && !editJoints && !editLimbs && !editAnimations)
            {
                paramsToggle.Selected = false;
                return;
            }
            if (editCharacterInfo)
            {
                var mainEditor = ParamsEditor.Instance;
                CharacterParams.AddToEditor(mainEditor, space: 10);
                var characterEditor = CharacterParams.SerializableEntityEditor;
                // Add some space after the title
                characterEditor.AddCustomContent(new GUIFrame(new RectTransform(new Point(characterEditor.Rect.Width, (int)(10 * GUI.yScale)), characterEditor.RectTransform), style: null) { CanBeFocused = false }, 1);
                if (CharacterParams.AI != null)
                {
                    CreateAddButton(CharacterParams.AI.SerializableEntityEditor, () => CharacterParams.AI.TryAddEmptyTarget(out _), GetCharacterEditorTranslation("AddAITarget"));
                    foreach (var target in CharacterParams.AI.Targets)
                    {
                        CreateCloseButton(target.SerializableEntityEditor, () => CharacterParams.AI.RemoveTarget(target), size: 0.8f);
                    }
                }
                foreach (var emitter in CharacterParams.BloodEmitters)
                {
                    CreateCloseButton(emitter.SerializableEntityEditor, () => CharacterParams.RemoveBloodEmitter(emitter));
                }
                foreach (var emitter in CharacterParams.GibEmitters)
                {
                    CreateCloseButton(emitter.SerializableEntityEditor, () => CharacterParams.RemoveGibEmitter(emitter));
                }
                foreach (var emitter in CharacterParams.DamageEmitters)
                {
                    CreateCloseButton(emitter.SerializableEntityEditor, () => CharacterParams.RemoveDamageEmitter(emitter));
                }
                foreach (var sound in CharacterParams.Sounds)
                {
                    CreateCloseButton(sound.SerializableEntityEditor, () => CharacterParams.RemoveSound(sound));
                }
                foreach (var inventory in CharacterParams.Inventories)
                {
                    var editor = inventory.SerializableEntityEditor;
                    CreateCloseButton(editor, () => CharacterParams.RemoveInventory(inventory));
                    foreach (var item in inventory.Items)
                    {
                        CreateCloseButton(item.SerializableEntityEditor, () => inventory.RemoveItem(item), size: 0.8f);
                    }
                    CreateAddButton(editor, () => inventory.AddItem(), GetCharacterEditorTranslation("AddInventoryItem"));
                }
                CreateAddButtonAtLast(mainEditor, () => CharacterParams.AddBloodEmitter(), GetCharacterEditorTranslation("AddBloodEmitter"));
                CreateAddButtonAtLast(mainEditor, () => CharacterParams.AddGibEmitter(), GetCharacterEditorTranslation("AddGibEmitter"));
                CreateAddButtonAtLast(mainEditor, () => CharacterParams.AddDamageEmitter(), GetCharacterEditorTranslation("AddDamageEmitter"));
                CreateAddButtonAtLast(mainEditor, () => CharacterParams.AddSound(), GetCharacterEditorTranslation("AddSound"));
                CreateAddButtonAtLast(mainEditor, () => CharacterParams.AddInventory(), GetCharacterEditorTranslation("AddInventory"));
            }
            else if (editAnimations)
            {
                character.AnimController.CurrentAnimationParams?.AddToEditor(ParamsEditor.Instance, space: 10);
            }
            else
            {
                if (editRagdoll)
                {
                    RagdollParams.AddToEditor(ParamsEditor.Instance, alsoChildren: false, space: 10);
                    RagdollParams.Colliders.ForEach(c => c.AddToEditor(ParamsEditor.Instance, false, 10));
                }
                else if (editJoints)
                {
                    if (selectedJoints.Any())
                    {
                        selectedJoints.ForEach(j => j.Params.AddToEditor(ParamsEditor.Instance, true, space: 10));
                    }
                    else
                    {
                        RagdollParams.Joints.ForEach(jp => jp.AddToEditor(ParamsEditor.Instance, false, space: 10));
                    }
                }
                else if (editLimbs)
                {
                    if (selectedLimbs.Any())
                    {
                        foreach (var limb in selectedLimbs)
                        {
                            var mainEditor = ParamsEditor.Instance;
                            var limbEditor = limb.Params.SerializableEntityEditor;
                            limb.Params.AddToEditor(mainEditor, true, space: 0);
                            foreach (var damageModifier in limb.Params.DamageModifiers)
                            {
                                CreateCloseButton(damageModifier.SerializableEntityEditor, () => limb.Params.RemoveDamageModifier(damageModifier));
                            }
                            if (limb.Params.Sound == null)
                            {
                                CreateAddButtonAtLast(mainEditor, () => limb.Params.AddSound(), GetCharacterEditorTranslation("AddSound"));
                            }
                            else
                            {
                                CreateCloseButton(limb.Params.Sound.SerializableEntityEditor, () => limb.Params.RemoveSound());
                            }
                            if (limb.Params.LightSource == null)
                            {
                                CreateAddButtonAtLast(mainEditor, () => limb.Params.AddLight(), GetCharacterEditorTranslation("AddLightSource"));
                            }
                            else
                            {
                                CreateCloseButton(limb.Params.LightSource.SerializableEntityEditor, () => limb.Params.RemoveLight());
                            }
                            if (limb.Params.Attack == null)
                            {
                                CreateAddButtonAtLast(mainEditor, () => limb.Params.AddAttack(), GetCharacterEditorTranslation("AddAttack"));
                            }
                            else
                            {
                                var attackParams = limb.Params.Attack;
                                foreach (var affliction in attackParams.Attack.Afflictions)
                                {
                                    if (attackParams.AfflictionEditors.TryGetValue(affliction.Key, out SerializableEntityEditor afflictionEditor))
                                    {
                                        CreateCloseButton(afflictionEditor, () => attackParams.RemoveAffliction(affliction.Value), size: 0.8f);
                                    }
                                }
                                var attackEditor = attackParams.SerializableEntityEditor;
                                CreateAddButton(attackEditor, () => attackParams.AddNewAffliction(), GetCharacterEditorTranslation("AddAffliction"));
                                CreateCloseButton(attackEditor, () => limb.Params.RemoveAttack());
                                var space = new GUIFrame(new RectTransform(new Point(attackEditor.RectTransform.Rect.Width, (int)(20 * GUI.yScale)), attackEditor.RectTransform), style: null, color: ParamsEditor.Color)
                                {
                                    CanBeFocused = false
                                };
                                attackEditor.AddCustomContent(space, attackEditor.ContentCount);
                            }
                            CreateAddButtonAtLast(mainEditor, () => limb.Params.AddDamageModifier(), GetCharacterEditorTranslation("AddDamageModifier"));
                        }
                    }
                    else
                    {
                        character.AnimController.Limbs.ForEach(l => l.Params.AddToEditor(ParamsEditor.Instance, false, space: 10));
                    }
                }
            }

            void CreateCloseButton(SerializableEntityEditor editor, Action onButtonClicked, float size = 1)
            {
                if (editor == null) { return; }
                int height = 30;
                var parent = new GUIFrame(new RectTransform(new Point(editor.Rect.Width, (int)(height * size * GUI.yScale)), editor.RectTransform, isFixedSize: true), style: null)
                {
                    CanBeFocused = false
                };
                new GUIButton(new RectTransform(new Vector2(0.9f), parent.RectTransform, Anchor.BottomRight, scaleBasis: ScaleBasis.BothHeight), style: "GUICancelButton", color: GUIStyle.Red)
                {
                    OnClicked = (button, data) =>
                    {
                        onButtonClicked();
                        ResetParamsEditor();
                        return true;
                    }
                };
                editor.AddCustomContent(parent, 0);
            }

            void CreateAddButtonAtLast(ParamsEditor editor, Action onButtonClicked, LocalizedString text)
            {
                if (editor == null) { return; }
                var parentFrame = new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, (int)(50 * GUI.yScale)), editor.EditorBox.Content.RectTransform), style: null, color: ParamsEditor.Color)
                {
                    CanBeFocused = false
                };
                new GUIButton(new RectTransform(new Vector2(0.45f, 0.6f), parentFrame.RectTransform, Anchor.Center), text)
                {
                    OnClicked = (button, data) =>
                    {
                        onButtonClicked();
                        ResetParamsEditor();
                        return true;
                    }
                };
            }

            void CreateAddButton(SerializableEntityEditor editor, Action onButtonClicked, LocalizedString text)
            {
                if (editor == null) { return; }
                var parent = new GUIFrame(new RectTransform(new Point(editor.Rect.Width, (int)(60 * GUI.yScale)), editor.RectTransform), style: null)
                {
                    CanBeFocused = false
                };
                new GUIButton(new RectTransform(new Vector2(0.45f, 0.4f), parent.RectTransform, Anchor.CenterLeft), text)
                {
                    OnClicked = (button, data) =>
                    {
                        onButtonClicked();
                        ResetParamsEditor();
                        return true;
                    }
                };
                editor.AddCustomContent(parent, editor.ContentCount);
            }
        }

        private void TryUpdateAnimParam(string name, object value) => TryUpdateAnimParam(name.ToIdentifier(), value);
        private void TryUpdateAnimParam(Identifier name, object value) => TryUpdateParam(character.AnimController.CurrentAnimationParams, name, value);
        private void TryUpdateRagdollParam(string name, object value) => TryUpdateRagdollParam(name.ToIdentifier(), value);
        private void TryUpdateRagdollParam(Identifier name, object value) => TryUpdateParam(RagdollParams, name, value);

        private void TryUpdateParam(EditableParams editableParams, Identifier name, object value)
        {
            if (editableParams.SerializableEntityEditor == null)
            {
                editableParams.AddToEditor(ParamsEditor.Instance);
            }
            if (editableParams.SerializableProperties.TryGetValue(name, out SerializableProperty p))
            {
                editableParams.SerializableEntityEditor.UpdateValue(p, value);
            }
        }

        private void TryUpdateJointParam(LimbJoint joint, string name, object value) => TryUpdateJointParam(joint, name.ToIdentifier(), value);
        private void TryUpdateJointParam(LimbJoint joint, Identifier name, object value) => TryUpdateSubParam(joint.Params, name, value);
        private void TryUpdateLimbParam(Limb limb, string name, object value) => TryUpdateLimbParam(limb, name.ToIdentifier(), value);
        private void TryUpdateLimbParam(Limb limb, Identifier name, object value) => TryUpdateSubParam(limb.Params, name, value);

        private void TryUpdateSubParam(RagdollParams.SubParam ragdollSubParams, Identifier name, object value)
        {
            if (ragdollSubParams.SerializableEntityEditor == null)
            {
                ragdollSubParams.AddToEditor(ParamsEditor.Instance);
            }
            if (ragdollSubParams.SerializableProperties.TryGetValue(name, out SerializableProperty p))
            {
                ragdollSubParams.SerializableEntityEditor.UpdateValue(p, value);
            }
            else
            {
                var subParams = ragdollSubParams.SubParams.Where(sp => sp.SerializableProperties.ContainsKey(name)).FirstOrDefault();
                if (subParams != null)
                {
                    if (subParams.SerializableProperties.TryGetValue(name, out p))
                    {
                        if (subParams.SerializableEntityEditor == null)
                        {
                            subParams.AddToEditor(ParamsEditor.Instance);
                        }
                        subParams.SerializableEntityEditor.UpdateValue(p, value);
                    }
                }
                else
                {
                    DebugConsole.ThrowError(GetCharacterEditorTranslation("NoFieldForParameterFound").Replace("[parameter]", name.Value));
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
            limbJoint.LowerLimit = MathUtils.WrapAnglePi(limbJoint.LowerLimit);
            limbJoint.UpperLimit = MathUtils.WrapAnglePi(limbJoint.UpperLimit);
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

        private void UpdateSourceRect(Limb limb, Rectangle newRect, bool resize)
        {
            Sprite activeSprite = limb.ActiveSprite;
            activeSprite.SourceRect = newRect;
            if (limb.DamagedSprite != null)
            {
                limb.DamagedSprite.SourceRect = activeSprite.SourceRect;
            }
            Vector2 colliderSize = new Vector2(ConvertUnits.ToSimUnits(newRect.Width), ConvertUnits.ToSimUnits(newRect.Height));
            if (resize)
            {
                if (recalculateCollider)
                {
                    RecalculateCollider(limb, colliderSize);
                }
            }
            var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(activeSprite));
            var originWidget = GetLimbEditWidget($"{limb.Params.ID}_origin", limb);
            if (!resize && originWidget != null)
            {
                Vector2 newOrigin = (originWidget.DrawPos - spritePos - activeSprite.SourceRect.Location.ToVector2() * spriteSheetZoom) / spriteSheetZoom;
                RecalculateOrigin(limb, newOrigin);
            }
            else
            {
                RecalculateOrigin(limb);
            }
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
                    if (resize)
                    {
                        if (recalculateCollider)
                        {
                            RecalculateCollider(otherLimb, colliderSize);
                        }
                    }
                    if (!resize && originWidget != null)
                    {
                        Vector2 newOrigin = (originWidget.DrawPos - spritePos - activeSprite.SourceRect.Location.ToVector2() * spriteSheetZoom) / spriteSheetZoom;
                        RecalculateOrigin(otherLimb, newOrigin);
                    }
                    else
                    {
                        RecalculateOrigin(otherLimb);
                    }
                    TryUpdateLimbParam(otherLimb, "sourcerect", newRect);
                });
            };
        }

        private void CalculateSpritesheetZoom()
        {
            var texture = textures.OrderByDescending(t => t.Width).FirstOrDefault();
            if (texture == null)
            {
                spriteSheetZoom = 1;
                return;
            }
            float width = texture.Width;
            float height = textures.Sum(t => t.Height);
            float margin = 20;
            if (unrestrictSpritesheet)
            {
                spriteSheetMaxZoom = (GameMain.GraphicsWidth - spriteSheetOffsetX * 2 - margin - leftArea.Rect.Width) / width;
            }
            else
            {
                if (height > width)
                {
                    spriteSheetMaxZoom = (centerArea.Rect.Bottom - spriteSheetOffsetY - margin) / height;
                }
                else
                {
                    spriteSheetMaxZoom = (centerArea.Rect.Left - spriteSheetOffsetX - margin) / width;
                }
            }
            spriteSheetMinZoom = spriteSheetMinZoom > spriteSheetMaxZoom ? spriteSheetMaxZoom : 0.25f;
            spriteSheetZoom = MathHelper.Clamp(1, spriteSheetMinZoom, spriteSheetMaxZoom);
        }

        private void HandleLimbSelection(Limb limb)
        {
            if (!editLimbs)
            {
                SetToggle(limbsToggle, true);
            }
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
                RagdollParams.StoreSnapshot();
            }
            if (editAnimations)
            {
                CurrentAnimation.StoreSnapshot();
            }
        }

        private void ToggleJointCreationMode()
        {
            switch (jointCreationMode)
            {
                case JointCreationMode.None:
                    jointCreationMode = JointCreationMode.Select;
                    SetToggle(spritesheetToggle, true);
                    break;
                case JointCreationMode.Select:
                case JointCreationMode.Create:
                    jointCreationMode = JointCreationMode.None;
                    break;
            }
        }

        private void ToggleLimbCreationMode()
        {
            isDrawingLimb = !isDrawingLimb;
            if (isDrawingLimb)
            {
                SetToggle(spritesheetToggle, true);
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
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 120, 150), GetCharacterEditorTranslation("HoldLeftAltToAdjustCycleSpeed"), Color.White, Color.Black * 0.5f, 10, GUIStyle.Font);
            }
            // Widgets for all anims -->
            Vector2 referencePoint = SimToScreen(head != null ? head.SimPosition: collider.SimPosition);
            Vector2 drawPos = referencePoint;
            if (ShowCycleWidget())
            {
                GetAnimationWidget("CycleSpeed", Color.MediumPurple, Color.Black, size: 20, sizeMultiplier: 1.5f, shape: Widget.Shape.Circle, initMethod: w =>
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
                GetAnimationWidget("MovementSpeed", Color.Turquoise, Color.Black, size: 20, sizeMultiplier: 1.5f, shape: Widget.Shape.Circle, initMethod: w =>
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
                    angle => TryUpdateAnimParam("headangle", angle), circleRadius: 25, rotationOffset: -collider.Rotation + head.Params.GetSpriteOrientation() * dir, clockWise: dir < 0, wrapAnglePi: true, holdPosition: true);
                // Head position and leaning
                Color color = GUIStyle.Red;
                if (animParams.IsGroundedAnimation)
                {
                    if (humanGroundedParams != null && character.AnimController is HumanoidAnimController humanAnimController)
                    {
                        GetAnimationWidget("HeadPosition", color, Color.Black, initMethod: w =>
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
                                            GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), color);
                                        }
                                        else
                                        {
                                            GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), color);
                                        }
                                    }
                                    else
                                    {
                                        GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), color);
                                        GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), color);
                                    }
                                }
                                else if (w.IsSelected)
                                {
                                    GUI.DrawLine(spriteBatch, w.DrawPos, SimToScreen(head.SimPosition), color);
                                }
                            };
                        }).Draw(spriteBatch, deltaTime);
                    }
                    else
                    {
                        GetAnimationWidget("HeadPosition", color, Color.Black, initMethod: w =>
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
                                    GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), color);
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
                    angle => TryUpdateAnimParam("torsoangle", angle), rotationOffset: -collider.Rotation + torso.Params.GetSpriteOrientation() * dir, clockWise: dir < 0, wrapAnglePi: true, holdPosition: true);
                Color color = Color.DodgerBlue;
                if (animParams.IsGroundedAnimation)
                {
                    // Torso position and leaning
                    if (humanGroundedParams != null && character.AnimController is HumanoidAnimController humanAnimController)
                    {
                        GetAnimationWidget("TorsoPosition", color, Color.Black, initMethod: w =>
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
                                            GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), color);
                                        }
                                        else
                                        {
                                            GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), color);
                                        }
                                    }
                                    else
                                    {
                                        GUI.DrawLine(spriteBatch, new Vector2(0, w.DrawPos.Y), new Vector2(GameMain.GraphicsWidth, w.DrawPos.Y), color);
                                        GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), color);
                                    }
                                }
                                else if (w.IsSelected)
                                {
                                    GUI.DrawLine(spriteBatch, w.DrawPos, SimToScreen(torso.SimPosition), color);
                                }
                            };
                        }).Draw(spriteBatch, deltaTime);
                    }
                    else
                    {
                        GetAnimationWidget("TorsoPosition", color, Color.Black, initMethod: w =>
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
                                    GUI.DrawLine(spriteBatch, new Vector2(w.DrawPos.X, 0), new Vector2(w.DrawPos.X, GameMain.GraphicsHeight), color);
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
                    angle => TryUpdateAnimParam("tailangle", angle), circleRadius: 25, rotationOffset: -collider.Rotation + tail.Params.GetSpriteOrientation() * dir, clockWise: dir < 0, wrapAnglePi: true, holdPosition: true);
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
                        
                        if (!fishParams.FootAnglesInRadians.ContainsKey(limb.Params.ID))
                        {
                            fishParams.FootAnglesInRadians[limb.Params.ID] = 0.0f;
                        }

                        DrawRadialWidget(spriteBatch, 
                            SimToScreen(new Vector2(limb.SimPosition.X, colliderBottom.Y)), 
                            MathHelper.ToDegrees(fishParams.FootAnglesInRadians[limb.Params.ID]),
                            GetCharacterEditorTranslation("FootAngle"), Color.White,
                            angle =>
                            {
                                fishParams.FootAnglesInRadians[limb.Params.ID] = MathHelper.ToRadians(angle);
                                TryUpdateAnimParam("footangles", fishParams.FootAngles);
                            },
                            circleRadius: 25, rotationOffset: -collider.Rotation + limb.Params.GetSpriteOrientation() * dir, clockWise: dir < 0, wrapAnglePi: true, autoFreeze: true);
                    }
                }
                else if (humanParams != null)
                {
                    DrawRadialWidget(spriteBatch, SimToScreen(foot.SimPosition), humanParams.FootAngle, GetCharacterEditorTranslation("FootAngle"), Color.White,
                        angle => TryUpdateAnimParam("footangle", angle), circleRadius: 25, rotationOffset: -collider.Rotation + foot.Params.GetSpriteOrientation() * dir, clockWise: dir > 0, wrapAnglePi: true);
                }
                // Grounded only
                if (groundedParams != null)
                {
                    GetAnimationWidget("StepSize", Color.LimeGreen, Color.Black, initMethod: w =>
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
                    GetAnimationWidget("HandMoveAmount", GUIStyle.Green, Color.Black, initMethod: w =>
                    {
                        w.tooltip = GetCharacterEditorTranslation("HandMoveAmount");
                        float offset = 0.1f;
                        w.refresh = () =>
                        {
                            var refPoint = SimToScreen(character.AnimController.Collider.SimPosition + GetSimSpaceForward() * offset);
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
                                GUI.DrawLine(sp, w.DrawPos, SimToScreen(character.AnimController.Collider.SimPosition + GetSimSpaceForward() * offset), GUIStyle.Green);
                            }
                        };
                    }).Draw(spriteBatch, deltaTime);
                }
            }
            // Fish swim only -->
            else if (tail != null && fishSwimParams != null)
            {
                float amplitudeMultiplier = 20;
                float lengthMultiplier = 20;
                int points = 1000;
                float GetAmplitude() => ConvertUnits.ToDisplayUnits(fishSwimParams.WaveAmplitude) * Cam.Zoom / amplitudeMultiplier;
                float GetWaveLength() => ConvertUnits.ToDisplayUnits(fishSwimParams.WaveLength) * Cam.Zoom / lengthMultiplier;
                Vector2 GetRefPoint() => SimToScreen(collider.SimPosition) - GetScreenSpaceForward() * ConvertUnits.ToDisplayUnits(collider.radius) * 3 * Cam.Zoom;
                Vector2 GetDrawPos() => GetRefPoint() - GetScreenSpaceForward() * GetWaveLength();
                Vector2 GetDir() => GetRefPoint() - GetDrawPos();
                Vector2 GetStartPoint() => GetDrawPos() + GetDir() / 2;
                Vector2 GetControlPoint() => GetStartPoint() + GetScreenSpaceForward().Right() * character.AnimController.Dir * GetAmplitude();
                var lengthWidget = GetAnimationWidget("WaveLength", Color.NavajoWhite, Color.Black, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    w.tooltip = GetCharacterEditorTranslation("TailMovementSpeed");
                    w.refresh = () => w.DrawPos = GetDrawPos();
                    w.MouseHeld += dTime =>
                    {
                        float input = Vector2.Multiply(ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed), GetScreenSpaceForward()).Combine() / Cam.Zoom * lengthMultiplier;
                        TryUpdateAnimParam("wavelength", MathHelper.Clamp(fishSwimParams.WaveLength - input, 0, 200));
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
                var amplitudeWidget = GetAnimationWidget("WaveAmplitude", Color.NavajoWhite, Color.Black, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
                {
                    w.tooltip = GetCharacterEditorTranslation("TailMovementAmount");
                    w.refresh = () => w.DrawPos = GetControlPoint();
                    w.MouseHeld += dTime =>
                    {
                        float input = Vector2.Multiply(ConvertUnits.ToSimUnits(PlayerInput.MouseSpeed), GetScreenSpaceForward().Right()).Combine() * character.AnimController.Dir / Cam.Zoom * amplitudeMultiplier;
                        TryUpdateAnimParam("waveamplitude", MathHelper.Clamp(fishSwimParams.WaveAmplitude + input, -100, 100));
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
                var lengthWidget = GetAnimationWidget("LegMovementSpeed", Color.NavajoWhite, Color.Black, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
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
                var amplitudeWidget = GetAnimationWidget("LegMovementAmount", Color.NavajoWhite, Color.Black, size: 15, shape: Widget.Shape.Circle, initMethod: w =>
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
                GetAnimationWidget("HandMoveAmount", GUIStyle.Green, Color.Black, initMethod: w =>
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
                            GUI.DrawLine(sp, w.DrawPos, SimToScreen(collider.SimPosition + GetSimSpaceForward() * offset), GUIStyle.Green);
                        }
                    };
                }).Draw(spriteBatch, deltaTime);
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb.type == LimbType.LeftFoot || limb.type == LimbType.RightFoot)
                {
                    GUI.DrawRectangle(spriteBatch, SimToScreen(limb.DebugRefPos) - Vector2.One * 3, Vector2.One * 6, Color.White, isFilled: true);
                    GUI.DrawRectangle(spriteBatch, SimToScreen(limb.DebugTargetPos) - Vector2.One * 3, Vector2.One * 6, GUIStyle.Green, isFilled: true);
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
                if (isSelected && jointStartLimb != limb && jointEndLimb != limb)
                {
                    GUI.DrawRectangle(spriteBatch, corners, Color.Yellow, thickness: 3);
                }
                if (GUI.MouseOn == null && Widget.selectedWidgets.None() && !spriteSheetRect.Contains(PlayerInput.MousePosition) && MathUtils.RectangleContainsPoint(corners, PlayerInput.MousePosition))
                {
                    if (isSelected)
                    {
                        // Origin
                        if (!lockSpriteOrigin && PlayerInput.PrimaryMouseButtonHeld())
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

            if (!altDown && editJoints && selectedJoints.Any() && jointCreationMode == JointCreationMode.None)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 180, 100), GetCharacterEditorTranslation("HoldLeftAltToManipulateJoint"), Color.White, Color.Black * 0.5f, 10, GUIStyle.Font);
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (editIK)
                {
                    if (limb.type == LimbType.LeftFoot || limb.type == LimbType.RightFoot || limb.type == LimbType.LeftHand || limb.type == LimbType.RightHand)
                    {
                        var pullJointWidgetSize = new Vector2(5, 5);
                        Vector2 tformedPullPos = SimToScreen(limb.PullJointWorldAnchorA);
                        GUI.DrawRectangle(spriteBatch, tformedPullPos - pullJointWidgetSize / 2, pullJointWidgetSize, GUIStyle.Red, true);
                        DrawWidget(spriteBatch, tformedPullPos, WidgetType.Rectangle, 8, Color.Cyan, $"IK ({limb.Name})", () =>
                        {
                            if (!selectedLimbs.Contains(limb))
                            {
                                selectedLimbs.Add(limb);
                                ResetParamsEditor();
                            }
                            limb.PullJointWorldAnchorA = ScreenToSim(PlayerInput.MousePosition);
                            TryUpdateLimbParam(limb, "pullpos", ConvertUnits.ToDisplayUnits(limb.PullJointLocalAnchorA / limb.Params.Scale / limb.Params.Ragdoll.LimbScale));
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
                    if (drawSkeleton)
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
                        var selectionWidget = GetJointSelectionWidget($"{joint.Params.Name} selection widget ragdoll", joint);
                        selectionWidget.DrawPos = tformedJointPos;
                        selectionWidget.Draw(spriteBatch, deltaTime);
                        if (selectedJoints.Contains(joint))
                        {
                            if (joint.LimitEnabled && jointCreationMode == JointCreationMode.None)
                            {
                                var otherBody = limb == joint.LimbA ? joint.LimbB : joint.LimbA;
                                float rotation = -otherBody.Rotation + limb.Params.GetSpriteOrientation();
                                if (character.AnimController.Dir < 0)
                                {
                                    rotation -= MathHelper.Pi;
                                }
                                DrawJointLimitWidgets(spriteBatch, limb, joint, tformedJointPos, autoFreeze: true, allowPairEditing: true, rotationOffset: rotation, holdPosition: true);
                            }
                            // Is the direction inversed incorrectly?
                            Vector2 to = tformedJointPos + VectorExtensions.ForwardFlipped(joint.LimbB.Rotation - joint.LimbB.Params.GetSpriteOrientation(), 20);
                            GUI.DrawLine(spriteBatch, tformedJointPos, to, Color.Magenta, width: 2);
                            var dotSize = new Vector2(5, 5);
                            var rect = new Rectangle((tformedJointPos - dotSize / 2).ToPoint(), dotSize.ToPoint());
                            //GUI.DrawRectangle(spriteBatch, tformedJointPos - dotSize / 2, dotSize, color, true);
                            //GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + up * 20, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.Yellow, width: 3);
                            //GUI.DrawRectangle(spriteBatch, inputRect, GUIStyle.Red);
                            GUI.DrawString(spriteBatch, tformedJointPos + new Vector2(dotSize.X, -dotSize.Y) * 2, $"{joint.Params.Name} {jointPos.FormatZeroDecimal()}", Color.White, Color.Black * 0.5f);
                            if (PlayerInput.PrimaryMouseButtonHeld())
                            {
                                if (!selectionWidget.IsControlled) { continue; }
                                if (jointCreationMode != JointCreationMode.None) { continue; }
                                if (autoFreeze)
                                {
                                    isFrozen = true;
                                }
                                else
                                {
                                    character.AnimController.Collider.PhysEnabled = false;
                                }
                                Vector2 input = ConvertUnits.ToSimUnits(scaledMouseSpeed) / Cam.Zoom;
                                input.Y = -input.Y;
                                input = input.TransformVector(VectorExtensions.ForwardFlipped(limb.Rotation));
                                if (joint.BodyA == limb.body.FarseerBody)
                                {
                                    joint.LocalAnchorA += input;
                                    Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / joint.Scale);
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
                                    Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / joint.Scale);
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
                                            TryUpdateJointParam(otherJoint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / joint.Scale));
                                        }
                                        else if (joint.BodyB == limb.body.FarseerBody && otherJoint.BodyB == otherLimb.body.FarseerBody)
                                        {
                                            otherJoint.LocalAnchorB = joint.LocalAnchorB;
                                            TryUpdateJointParam(otherJoint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / joint.Scale));
                                        }
                                    });
                                }
                            }
                            else
                            {
                                isFrozen = freezeToggle.Selected;
                                character.AnimController.Collider.PhysEnabled = true;
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
                if (limb.ActiveSprite == null || texturePaths.Contains(limb.ActiveSprite.FilePath.Value)) { continue; }
                if (limb.ActiveSprite.Texture == null) { continue; }
                textures.Add(limb.ActiveSprite.Texture);
                texturePaths.Add(limb.ActiveSprite.FilePath.Value);
            }
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
                    if (limb.ActiveSprite == null || limb.ActiveSprite.FilePath != texturePaths[i]) { continue; }
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
                            scale: (wearable.InheritScale ? 1 : wearable.Scale / RagdollParams.TextureScale) * spriteSheetZoom,
                            effects: SpriteEffects.None,
                            color: Color.White,
                            layerDepth: 0);
                    }
                    // The origin is manipulated when the character is flipped. We have to undo it here.
                    if (character.AnimController.Dir < 0)
                    {
                        limbScreenPos.X = rect.X + rect.Width - (float)Math.Round(origin.X * spriteSheetZoom);
                    }
                    if (editJoints)
                    {
                        DrawSpritesheetJointEditor(spriteBatch, deltaTime, limb, limbScreenPos);
                    }
                    bool isMouseOn = rect.Contains(PlayerInput.MousePosition);
                    if (editLimbs)
                    {
                        int widgetSize = 8;
                        int halfSize = widgetSize / 2;
                        Vector2 stringOffset = new Vector2(5, 14);
                        var topLeft = rect.Location.ToVector2();
                        var topRight = new Vector2(topLeft.X + rect.Width, topLeft.Y);
                        var bottomRight = new Vector2(topRight.X, topRight.Y + rect.Height);
                        bool isSelected = selectedLimbs.Contains(limb);
                        if (jointStartLimb != limb && jointEndLimb != limb)
                        {
                            if (isSelected || !onlyShowSourceRectForSelectedLimbs)
                            {
                                GUI.DrawRectangle(spriteBatch, rect, isSelected ? Color.Yellow : (isMouseOn ? Color.White : GUIStyle.Red));
                            }
                        }
                        if (isSelected)
                        {
                            var sprite = limb.ActiveSprite;
                            Vector2 GetTopLeft() => sprite.SourceRect.Location.ToVector2();
                            Vector2 GetTopRight() => new Vector2(GetTopLeft().X + sprite.SourceRect.Width, GetTopLeft().Y);
                            Vector2 GetBottomRight() => new Vector2(GetTopRight().X, GetTopRight().Y + sprite.SourceRect.Height);
                            var originWidget = GetLimbEditWidget($"{limb.Params.ID}_origin", limb, widgetSize, Widget.Shape.Cross, initMethod: w =>
                            {
                                w.refresh = () => w.tooltip = $"{GetCharacterEditorTranslation("Origin")}: {sprite.RelativeOrigin.FormatDoubleDecimal()}";
                                w.refresh();
                                w.MouseHeld += dTime =>
                                {
                                    var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(limb.ActiveSprite));
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
                                    var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(limb.ActiveSprite));
                                    w.DrawPos = (spritePos + (sprite.Origin + sprite.SourceRect.Location.ToVector2()) * spriteSheetZoom)
                                        .Clamp(spritePos + GetTopLeft() * spriteSheetZoom, spritePos + GetBottomRight() * spriteSheetZoom);
                                    w.refresh();
                                };
                            });
                            originWidget.Draw(spriteBatch, deltaTime);
                            if (!lockSpritePosition && (limb.type != LimbType.Head || !character.IsHuman))
                            {
                                var positionWidget = GetLimbEditWidget($"{limb.Params.ID}_position", limb, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                                {
                                    w.refresh = () => w.tooltip = $"{GetCharacterEditorTranslation("Position")}: {limb.ActiveSprite.SourceRect.Location}";
                                    w.refresh();
                                    w.MouseHeld += dTime =>
                                    {
                                        w.DrawPos = PlayerInput.MousePosition;
                                        Sprite activeSprite = limb.ActiveSprite;
                                        var newRect = activeSprite.SourceRect;
                                        newRect.Location = new Point(
                                            (int)((PlayerInput.MousePosition.X + halfSize - spriteSheetOffsetX) / spriteSheetZoom),
                                            (int)((PlayerInput.MousePosition.Y + halfSize - GetOffsetY(activeSprite)) / spriteSheetZoom));
                                        activeSprite.SourceRect = newRect;
                                        if (limb.DamagedSprite != null)
                                        {
                                            limb.DamagedSprite.SourceRect = activeSprite.SourceRect;
                                        }
                                        TryUpdateLimbParam(limb, "sourcerect", newRect);
                                        var spritePos = new Vector2(spriteSheetOffsetX, GetOffsetY(activeSprite));
                                        Vector2 newOrigin = (originWidget.DrawPos - spritePos - activeSprite.SourceRect.Location.ToVector2() * spriteSheetZoom) / spriteSheetZoom;
                                        RecalculateOrigin(limb, newOrigin);
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
                                                RecalculateOrigin(otherLimb, newOrigin);
                                            });
                                        };
                                    };
                                    w.PreDraw += (sb, dTime) => w.refresh();
                                });    
                                if (!positionWidget.IsControlled)
                                {
                                    positionWidget.DrawPos = topLeft - new Vector2(halfSize);
                                }
                                positionWidget.Draw(spriteBatch, deltaTime);
                            }
                            if (!lockSpriteSize && (limb.type != LimbType.Head || !character.IsHuman))
                            {
                                var sizeWidget = GetLimbEditWidget($"{limb.Params.ID}_size", limb, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                                {
                                    w.refresh = () => w.tooltip = $"{GetCharacterEditorTranslation("Size")}: {limb.ActiveSprite.SourceRect.Size}";
                                    w.refresh();
                                    w.MouseHeld += dTime =>
                                    {
                                        w.DrawPos = PlayerInput.MousePosition;
                                        Sprite activeSprite = limb.ActiveSprite;
                                        Rectangle newRect = activeSprite.SourceRect;
                                        float offset_y = activeSprite.SourceRect.Y * spriteSheetZoom + GetOffsetY(activeSprite);
                                        float offset_x = activeSprite.SourceRect.X * spriteSheetZoom + spriteSheetOffsetX;
                                        int width = (int)((PlayerInput.MousePosition.X - halfSize - offset_x) / spriteSheetZoom);
                                        int height = (int)((PlayerInput.MousePosition.Y - halfSize - offset_y) / spriteSheetZoom);
                                        newRect.Size = new Point(width, height);
                                        activeSprite.SourceRect = newRect;
                                        activeSprite.size = new Vector2(width, height);
                                        Vector2 colliderSize = new Vector2(ConvertUnits.ToSimUnits(width), ConvertUnits.ToSimUnits(height));
                                        if (recalculateCollider)
                                        {
                                            RecalculateCollider(limb, colliderSize);
                                        }
                                        RecalculateOrigin(limb);
                                        if (limb.DamagedSprite != null)
                                        {
                                            limb.DamagedSprite.SourceRect = activeSprite.SourceRect;
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
                                                    RecalculateCollider(otherLimb, colliderSize);
                                                }
                                                if (otherLimb.DamagedSprite != null)
                                                {
                                                    otherLimb.DamagedSprite.SourceRect = newRect;
                                                }
                                                TryUpdateLimbParam(otherLimb, "sourcerect", newRect);
                                            });
                                        };
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
                        else if (isMouseOn && GUI.MouseOn == null && Widget.selectedWidgets.None())
                        {
                            // TODO: only one limb name should be displayed (needs to be done in a separate loop)
                            GUI.DrawString(spriteBatch, limbScreenPos + new Vector2(10, -10), limb.Name, Color.White, Color.Black * 0.5f);
                        }
                    }
                    else
                    {
                        GUI.DrawRectangle(spriteBatch, rect, isMouseOn ? Color.White : Color.Gray);
                        if (isMouseOn && GUI.MouseOn == null && Widget.selectedWidgets.None())
                        {
                            // TODO: only one limb name should be displayed (needs to be done in a separate loop)
                            GUI.DrawString(spriteBatch, limbScreenPos + new Vector2(10, -10), limb.Name, Color.White, Color.Black * 0.5f);
                        }
                    }
                }
                offsetY += (int)(texture.Height * spriteSheetZoom);
            }
        }

        private int GetTextureHeight(Sprite sprite)
        {
            int textureIndex = Textures.IndexOf(sprite.Texture);
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

        private int GetOffsetY(Sprite sprite) => spriteSheetOffsetY + GetTextureHeight(sprite);

        private void RecalculateCollider(Limb l, Vector2 size)
        {
            // We want the collider to be slightly smaller than the source rect, because the source rect is usually a bit bigger than the graphic.
            float multiplier = 0.9f;
            l.body.SetSize(new Vector2(size.X, size.Y) * l.Scale * RagdollParams.TextureScale * multiplier);
            TryUpdateLimbParam(l, "radius", ConvertUnits.ToDisplayUnits(l.body.radius / l.Params.Scale / RagdollParams.LimbScale / RagdollParams.TextureScale));
            TryUpdateLimbParam(l, "width", ConvertUnits.ToDisplayUnits(l.body.width / l.Params.Scale / RagdollParams.LimbScale / RagdollParams.TextureScale));
            TryUpdateLimbParam(l, "height", ConvertUnits.ToDisplayUnits(l.body.height / l.Params.Scale / RagdollParams.LimbScale / RagdollParams.TextureScale));
        }

        private void RecalculateOrigin(Limb l, Vector2? newOrigin = null)
        {
            Sprite activeSprite = l.ActiveSprite;
            if (lockSpriteOrigin)
            {
                // Keeps the absolute origin unchanged. The relative origin will be recalculated.
                activeSprite.Origin = newOrigin ?? activeSprite.Origin;
                TryUpdateLimbParam(l, "origin", activeSprite.RelativeOrigin);
            }
            else
            {
                // Keeps the relative origin unchanged. The absolute origin will be recalculated.
                activeSprite.RelativeOrigin = activeSprite.RelativeOrigin;
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
                Vector2 tformedJointPos = jointPos = jointPos / joint.Scale / limb.TextureScale * spriteSheetZoom;
                tformedJointPos.Y = -tformedJointPos.Y;
                tformedJointPos.X *= character.AnimController.Dir;
                tformedJointPos += limbScreenPos;
                var jointSelectionWidget = GetJointSelectionWidget($"{joint.Params.Name} selection widget {anchorID}", joint, $"{joint.Params.Name} selection widget {otherID}");
                jointSelectionWidget.DrawPos = tformedJointPos;
                jointSelectionWidget.Draw(spriteBatch, deltaTime);
                var otherWidget = GetJointSelectionWidget($"{joint.Params.Name} selection widget {otherID}", joint, $"{joint.Params.Name} selection widget {anchorID}");
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
                    if (joint.LimitEnabled && jointCreationMode == JointCreationMode.None)
                    {
                        DrawJointLimitWidgets(spriteBatch, limb, joint, tformedJointPos, autoFreeze: false, allowPairEditing: true, holdPosition: false, rotationOffset: joint.LimbB.Params.GetSpriteOrientation());
                    }
                    if (jointSelectionWidget.IsControlled)
                    {
                        Vector2 input = ConvertUnits.ToSimUnits(scaledMouseSpeed);
                        input.Y = -input.Y;
                        input.X *= character.AnimController.Dir;
                        input *= joint.Scale * limb.TextureScale / spriteSheetZoom;
                        if (joint.BodyA == limb.body.FarseerBody)
                        {
                            joint.LocalAnchorA += input;
                            Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / joint.Scale);
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
                            Vector2 transformedValue = ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / joint.Scale);
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
                                    TryUpdateJointParam(otherJoint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA / joint.Scale));
                                }
                                else if (joint.BodyB == limb.body.FarseerBody && otherJoint.BodyB == otherLimb.body.FarseerBody)
                                {
                                    otherJoint.LocalAnchorB = joint.LocalAnchorB;
                                    TryUpdateJointParam(otherJoint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB / joint.Scale));
                                }
                            });
                        }
                    }
                }
            }
        }

        private void DrawJointLimitWidgets(SpriteBatch spriteBatch, Limb limb, LimbJoint joint, Vector2 drawPos, bool autoFreeze, bool allowPairEditing, bool holdPosition, float rotationOffset = 0)
        {
            bool clockWise = joint.Params.ClockWiseRotation;
            Color angleColor = joint.UpperLimit - joint.LowerLimit > 0 ? GUIStyle.Green * 0.5f : GUIStyle.Red;
            DrawRadialWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.UpperLimit), $"{joint.Params.Name}: {GetCharacterEditorTranslation("UpperLimit")}", Color.Cyan, angle =>
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
                GUI.DrawString(spriteBatch, drawPos, angle.FormatZeroDecimal(), Color.Black, backgroundColor: Color.Cyan, font: GUIStyle.SmallFont);
            }, circleRadius: 40, rotationOffset: rotationOffset, displayAngle: false, clockWise: clockWise, holdPosition: holdPosition);
            DrawRadialWidget(spriteBatch, drawPos, MathHelper.ToDegrees(joint.LowerLimit), $"{joint.Params.Name}: {GetCharacterEditorTranslation("LowerLimit")}", Color.Yellow, angle =>
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
                GUI.DrawString(spriteBatch, drawPos, angle.FormatZeroDecimal(), Color.Black, backgroundColor: Color.Yellow, font: GUIStyle.SmallFont);
            }, circleRadius: 25, rotationOffset: rotationOffset, displayAngle: false, clockWise: clockWise, holdPosition: holdPosition);
            void DrawAngle(float radius, Color color, float thickness = 5)
            {
                float angle = joint.UpperLimit - joint.LowerLimit;
                float offset = clockWise ? rotationOffset + joint.LowerLimit - MathHelper.PiOver2 : rotationOffset - joint.UpperLimit - MathHelper.PiOver2;
                ShapeExtensions.DrawSector(spriteBatch, drawPos, radius, angle, 40, color, offset: offset, thickness: thickness);
            }
        }

        private void Nudge(Keys key)
        {
            switch (key)
            {
                case Keys.Left:
                    foreach (var limb in selectedLimbs)
                    {
                        // Can't edit human heads
                        if (limb.type == LimbType.Head && character.IsHuman) { continue; }
                        var newRect = limb.ActiveSprite.SourceRect;
                        bool resize = PlayerInput.KeyDown(Keys.LeftControl);
                        if (resize)
                        {
                            if (lockSpriteSize) { return; }
                            newRect.Width--;
                        }
                        else
                        {
                            if (lockSpritePosition) { return; }
                            newRect.X--;
                        }
                        UpdateSourceRect(limb, newRect, resize);
                    }
                    break;
                case Keys.Right:
                    foreach (var limb in selectedLimbs)
                    {
                        // Can't edit human heads
                        if (limb.type == LimbType.Head && character.IsHuman) { continue; }
                        var newRect = limb.ActiveSprite.SourceRect;
                        bool resize = PlayerInput.KeyDown(Keys.LeftControl);
                        if (resize)
                        {
                            if (lockSpriteSize) { return; }
                            newRect.Width++;
                        }
                        else
                        {
                            if (lockSpritePosition) { return; }
                            newRect.X++;
                        }
                        UpdateSourceRect(limb, newRect, resize);
                    }
                    break;
                case Keys.Down:
                    foreach (var limb in selectedLimbs)
                    {
                        // Can't edit human heads
                        if (limb.type == LimbType.Head && character.IsHuman) { continue; }
                        var newRect = limb.ActiveSprite.SourceRect;
                        bool resize = PlayerInput.KeyDown(Keys.LeftControl);
                        if (resize)
                        {
                            if (lockSpriteSize) { return; }
                            newRect.Height++;
                        }
                        else
                        {
                            if (lockSpritePosition) { return; }
                            newRect.Y++;
                        }
                        UpdateSourceRect(limb, newRect, resize);
                    }
                    break;
                case Keys.Up:
                    foreach (var limb in selectedLimbs)
                    {
                        // Can't edit human heads
                        if (limb.type == LimbType.Head && character.IsHuman) { continue; }
                        var newRect = limb.ActiveSprite.SourceRect;
                        bool resize = PlayerInput.KeyDown(Keys.LeftControl);
                        if (resize)
                        {
                            if (lockSpriteSize) { return; }
                            newRect.Height--;
                        }
                        else
                        {
                            if (lockSpritePosition) { return; }
                            newRect.Y--;
                        }
                        UpdateSourceRect(limb, newRect, resize);
                    }
                    break;
            }
            RagdollParams.StoreSnapshot();
        }

        private void SetSpritesheetRestriction(bool value)
        {
            unrestrictSpritesheet = value;
            CalculateSpritesheetZoom();
            spriteSheetZoomBar.BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(spriteSheetMinZoom, spriteSheetMaxZoom, spriteSheetZoom));
        }
#endregion

#region Widgets as methods
        private void DrawRadialWidget(SpriteBatch spriteBatch, Vector2 drawPos, float value, LocalizedString toolTip, Color color, Action<float> onClick,
            float circleRadius = 30, int widgetSize = 10, float rotationOffset = 0, bool clockWise = true, bool displayAngle = true, bool? autoFreeze = null, bool wrapAnglePi = false, bool holdPosition = false, int rounding = 1)
        {
            var angle = value;
            if (!MathUtils.IsValid(angle))
            {
                angle = 0;
            }
            float drawAngle = clockWise ? angle : -angle;
            var widgetDrawPos = drawPos + VectorExtensions.Forward(MathHelper.ToRadians(drawAngle) + rotationOffset - MathHelper.PiOver2, circleRadius);
            GUI.DrawLine(spriteBatch, drawPos, widgetDrawPos, color);
            DrawWidget(spriteBatch, widgetDrawPos, WidgetType.Rectangle, widgetSize, color, toolTip, () =>
            {
                GUI.DrawLine(spriteBatch, drawPos, widgetDrawPos, color, width: 3);
                ShapeExtensions.DrawCircle(spriteBatch, drawPos, circleRadius, 40, color, thickness: 1);
                Vector2 d = PlayerInput.MousePosition - drawPos;
                float newAngle = clockWise
                    ? MathUtils.VectorToAngle(d) + MathHelper.PiOver2 - rotationOffset
                    : -MathUtils.VectorToAngle(d) - MathHelper.PiOver2 + rotationOffset;
                angle = MathHelper.ToDegrees(wrapAnglePi ? MathUtils.WrapAnglePi(newAngle) : MathUtils.WrapAngleTwoPi(newAngle));
                angle = (float)Math.Round(angle / rounding) * rounding;
                if (angle >= 360 || angle <= -360) { angle = 0; }
                if (displayAngle)
                {
                    GUI.DrawString(spriteBatch, drawPos, angle.FormatZeroDecimal(), Color.Black, backgroundColor: color, font: GUIStyle.SmallFont);
                }
                onClick(angle);
                var zeroPos = drawPos + VectorExtensions.Forward(rotationOffset - MathHelper.PiOver2, circleRadius);
                GUI.DrawLine(spriteBatch, drawPos, zeroPos, GUIStyle.Red, width: 3);
            }, autoFreeze, holdPosition, onHovered: () =>
            {
                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    GUI.DrawString(spriteBatch, new Vector2(drawPos.X + 5, drawPos.Y - widgetSize / 2),
                        $"{toolTip} ({angle.FormatZeroDecimal()})", color, Color.Black * 0.5f);
                }    
            });
        }

        private enum WidgetType { Rectangle, Circle }
        private void DrawWidget(SpriteBatch spriteBatch, Vector2 drawPos, WidgetType widgetType, int size, Color color, LocalizedString toolTip, Action onPressed, bool? autoFreeze = null, bool holdPosition = false, Action onHovered = null)
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
                        GUI.DrawRectangle(spriteBatch, rect, color, thickness: 3, isFilled: PlayerInput.PrimaryMouseButtonHeld());
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
                if (PlayerInput.PrimaryMouseButtonHeld())
                {
                    if (autoFreeze ?? this.autoFreeze)
                    {
                        isFrozen = true;
                    }
                    if (holdPosition == true)
                    {
                        character.AnimController.Collider.PhysEnabled = false;
                    }
                    onPressed();
                }
                else
                {
                    isFrozen = freezeToggle.Selected;
                    character.AnimController.Collider.PhysEnabled = true;
                }
                // Might not be entirely reliable, since the method is used inside the draw loop.
                if (PlayerInput.PrimaryMouseButtonClicked())
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

        private Widget GetAnimationWidget(string name, Color innerColor, Color? outerColor = null, int size = 10, float sizeMultiplier = 2, Widget.Shape shape = Widget.Shape.Rectangle, Action<Widget> initMethod = null)
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
                widget.MouseUp += () => CurrentAnimation.StoreSnapshot();
                widget.color = innerColor;
                widget.secondaryColor = outerColor;
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
                    widget.color = selectedJoints.Contains(joint) ? Color.Yellow : GUIStyle.Red;
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
                    if (jointCreationMode != JointCreationMode.None) { return; }
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
                widget.MouseUp += () =>
                {
                    if (jointCreationMode == JointCreationMode.None)
                    {
                        RagdollParams.StoreSnapshot();
                    }
                };
                widget.tooltip = joint.Params.Name;
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
                w.MouseUp += () => RagdollParams.StoreSnapshot();
                initMethod?.Invoke(w);
                return w;
            }
        }
#endregion
    }
}
