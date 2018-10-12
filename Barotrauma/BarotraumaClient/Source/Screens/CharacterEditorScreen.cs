using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;

namespace Barotrauma
{
    class CharacterEditorScreen : Screen
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
        private bool showAnimControls;
        private bool editSpriteDimensions;
        private bool editRagdoll;
        private bool editJointPositions;
        private bool editJointLimits;
        private bool showParamsEditor;
        private bool showSpritesheet;
        private bool isFreezed;
        private bool autoFreeze = true;
        private bool limbPairEditing;
        private bool uniformScaling;
        private bool lockSpriteOrigin;
        private bool lockSpritePosition;
        private bool lockSpriteSize;

        private float spriteSheetZoom;
        private int spriteSheetOffsetY = 30;
        private int spriteSheetOffsetX = 30;
        private Color backgroundColor = new Color(0.12f, 0.298f, 0.542f, 1.0f);

        private float spriteSheetOrientation;

        public override void Select()
        {
            base.Select();
            Submarine.MainSub = new Submarine("Content/AnimEditor.sub");
            Submarine.MainSub.Load(unloadPrevious: true, showWarningMessages: false);
            Submarine.MainSub.GodMode = true;
            originalWall = new WallGroup(new List<Structure>(Structure.WallList));
            CloneWalls();
            CalculateMovementLimits();
            currentCharacterConfig = Character.HumanConfigFile;
            SpawnCharacter(currentCharacterConfig);
            GameMain.Instance.OnResolutionChanged += OnResolutionChanged;
        }

        public override void Deselect()
        {
            base.Deselect();
            Submarine.MainSub.Remove();
            GameMain.Instance.OnResolutionChanged -= OnResolutionChanged;
        }

        private void OnResolutionChanged()
        {
            CreateGUI();
        }

        #region Inifinite runner
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
            wallCollisionsEnabled = enabled;
            var collisionCategory = enabled ? FarseerPhysics.Dynamics.Category.Cat1 : FarseerPhysics.Dynamics.Category.None;
            AllWalls.ForEach(w => w.SetCollisionCategory(collisionCategory));
            GameMain.World.ProcessChanges();
        }
        #endregion

        #region Character spawning
        private int characterIndex = -1;
        private string currentCharacterConfig;
        private List<string> allFiles;
        private List<string> AllFiles
        {
            get
            {
                if (allFiles == null)
                {
                    allFiles = GameMain.Instance.GetFilesOfType(ContentType.Character).ToList();
                    allFiles.ForEach(f => DebugConsole.NewMessage(f, Color.White));
                }
                return allFiles;
            }
        }

        private string GetNextConfigFile()
        {
            CheckAndGetIndex();
            IncreaseIndex();
            currentCharacterConfig = AllFiles[characterIndex];
            return currentCharacterConfig;
        }

        private string GetPreviousConfigFile()
        {
            CheckAndGetIndex();
            ReduceIndex();
            currentCharacterConfig = AllFiles[characterIndex];
            return currentCharacterConfig;
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

        private Character SpawnCharacter(string configFile, RagdollParams ragdoll = null)
        {
            DebugConsole.NewMessage($"Trying to spawn {configFile}", Color.HotPink);
            spawnPosition = WayPoint.GetRandom(sub: Submarine.MainSub).WorldPosition;
            var character = Character.Create(configFile, spawnPosition, ToolBox.RandomSeed(8), hasAi: false, ragdoll: ragdoll);
            character.Submarine = Submarine.MainSub;
            character.AnimController.forceStanding = character.AnimController.CanWalk;
            character.AnimController.ForceSelectAnimationType = character.AnimController.CanWalk ? AnimationType.Walk : AnimationType.SwimSlow;
            character.dontFollowCursor = true;
            Character.Controlled = character;
            SetWallCollisions(character.AnimController.forceStanding);
            this.character = character;
            CreateTextures();
            CreateGUI();
            widgets.Clear();
            ResetParamsEditor();
            return character;
        }

        private void TeleportTo(Vector2 position)
        {
            character.AnimController.SetPosition(ConvertUnits.ToSimUnits(position), false);
        }
        #endregion

        #region GUI
        private GUIFrame rightPanel;
        private GUIFrame centerPanel;
        private GUIFrame ragdollControls;
        private GUIFrame animationControls;
        private GUIFrame spriteControls;
        private GUIFrame spriteSheetControls;
        private GUIFrame generalControls;
        private GUIDropDown animSelection;
        private GUITickBox freezeToggle;
        private GUITickBox animTestPoseToggle;
        private GUIScrollBar jointScaleBar;
        private GUIScrollBar limbScaleBar;
        private GUITickBox pixelPerfectToggle;
        private GUIScrollBar spriteSheetZoomBar;

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
            generalControls = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerPanel.RectTransform, Anchor.TopRight), style: null) { CanBeFocused = false };
            var layoutGroupGeneral = new GUILayoutGroup(new RectTransform(Vector2.One, generalControls.RectTransform), childAnchor: Anchor.TopRight)
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
            spriteSheetControls = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), centerPanel.RectTransform), style: null) { CanBeFocused = false };
            var layoutGroupSpriteSheet = new GUILayoutGroup(new RectTransform(Vector2.One, spriteSheetControls.RectTransform)) { AbsoluteSpacing = 5, CanBeFocused = false };
            new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), "Spritesheet zoom:", Color.White);
            float spriteMinScale = 0.25f;
            float spriteMaxScale = (centerPanel.Rect.Left - spriteSheetOffsetX) / (float)(Textures.OrderByDescending(t => t.Width).First().Width);
            spriteSheetZoom = MathHelper.Clamp(1, spriteMinScale, spriteMaxScale);
            spriteSheetZoomBar = new GUIScrollBar(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), barSize: 0.2f)
            {
                Enabled = spriteMaxScale < 1,
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(spriteMinScale, spriteMaxScale, spriteSheetZoom)),
                Step = 0.01f,
                OnMoved = (scrollBar, value) =>
                {
                    spriteSheetZoom = MathHelper.Lerp(spriteMinScale, spriteMaxScale, value);
                    return true;
                }
            };
            pixelPerfectToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteSheet.RectTransform), "Zoom 100%")
            {
                Enabled = spriteMaxScale >= 1,
                Selected = spriteMaxScale >= 1,
                TextColor = spriteMaxScale >= 1 ? Color.White : Color.Gray,
                OnSelected = (tickBox) =>
                {
                    spriteSheetZoomBar.Enabled = !tickBox.Selected;
                    spriteSheetZoom = Math.Min(1, spriteMaxScale);
                    spriteSheetZoomBar.BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(spriteMinScale, spriteMaxScale, spriteSheetZoom));
                    return true;
                }
            };
            // Sprite controls
            spriteControls = new GUIFrame(new RectTransform(new Vector2(1, 0.9f), centerPanel.RectTransform) { RelativeOffset = new Vector2(0, 0.1f) }, style: null) { CanBeFocused = false };
            var layoutGroupSpriteControls = new GUILayoutGroup(new RectTransform(Vector2.One, spriteControls.RectTransform)) { CanBeFocused = false };
            // Spacing
            new GUIFrame(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteControls.RectTransform), style: null) { CanBeFocused = false };

            var lockSpriteOriginToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteControls.RectTransform), "Lock Sprite Origin")
            {
                Selected = lockSpriteOrigin,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpriteOrigin = box.Selected;
                    return true;
                }
            };
            lockSpriteOriginToggle.TextColor = Color.White;
            var lockSpritePositionToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteControls.RectTransform), "Lock Sprite Position")
            {
                Selected = lockSpritePosition,
                OnSelected = (GUITickBox box) =>
                {
                    lockSpritePosition = box.Selected;
                    return true;
                }
            };
            lockSpritePositionToggle.TextColor = Color.White;
            var lockSpriteSizeToggle = new GUITickBox(new RectTransform(new Point(elementSize.X, textAreaHeight), layoutGroupSpriteControls.RectTransform), "Lock Sprite Size")
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
            ragdollControls = new GUIFrame(new RectTransform(new Vector2(1, 0.9f), centerPanel.RectTransform) { RelativeOffset = new Vector2(0, 0.1f) }, style: null) { CanBeFocused = false };
            var layoutGroupRagdoll = new GUILayoutGroup(new RectTransform(Vector2.One, ragdollControls.RectTransform)) { CanBeFocused = false };
            var jointScaleElement = new GUIFrame(new RectTransform(elementSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var jointScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), jointScaleElement.RectTransform), $"Joint Scale: {RagdollParams.JointScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
            var limbScaleElement = new GUIFrame(new RectTransform(elementSize + new Point(0, textAreaHeight), layoutGroupRagdoll.RectTransform), style: null);
            var limbScaleText = new GUITextBlock(new RectTransform(new Point(elementSize.X, textAreaHeight), limbScaleElement.RectTransform), $"Limb Scale: {RagdollParams.LimbScale.FormatDoubleDecimal()}", Color.WhiteSmoke, textAlignment: Alignment.Center);
            jointScaleBar = new GUIScrollBar(new RectTransform(elementSize, jointScaleElement.RectTransform, Anchor.BottomLeft), barSize: 0.2f)
            {
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE, RagdollParams.JointScale)),
                Step = 0.01f,
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
            limbScaleBar = new GUIScrollBar(new RectTransform(elementSize, limbScaleElement.RectTransform, Anchor.BottomLeft), barSize: 0.2f)
            {
                BarScroll = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE, RagdollParams.LimbScale)),
                Step = 0.01f,
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
                character.AnimController.Recreate(RagdollParams);
                TeleportTo(spawnPosition);
                return true;
            };
            jointScaleBar.Bar.OnClicked += (button, data) =>
            {
                if (uniformScaling)
                {
                    character.AnimController.Recreate(RagdollParams);
                    TeleportTo(spawnPosition);
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
            Vector2 buttonSize = new Vector2(1, 0.05f);
            Vector2 toggleSize = new Vector2(0.03f, 0.03f);
            Point margin = new Point(40, 60);
            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.15f, 0.95f), parent: Frame.RectTransform, anchor: Anchor.CenterRight) { RelativeOffset = new Vector2(0.01f, 0) });
            var layoutGroup = new GUILayoutGroup(new RectTransform(new Point(rightPanel.Rect.Width - margin.X, rightPanel.Rect.Height - margin.Y), rightPanel.RectTransform, Anchor.Center));
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
            var editAnimsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Animations") { Selected = showAnimControls };
            var spriteDimensionsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Sprite Dimensions") { Selected = editSpriteDimensions };
            var ragdollToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Ragdoll") { Selected = editRagdoll };
            var jointPositionsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Joint Positions") { Selected = editJointPositions };
            var jointLimitsToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Edit Joints Limits") { Selected = editJointLimits };
            freezeToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Freeze") { Selected = isFreezed };
            var autoFreezeToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Auto Freeze") { Selected = autoFreeze };
            var limbPairEditToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Limb Pair Editing") { Selected = limbPairEditing };
            animTestPoseToggle = new GUITickBox(new RectTransform(toggleSize, layoutGroup.RectTransform), "Animation Test Pose") { Selected = character.AnimController.AnimationTestPose };
            editAnimsToggle.OnSelected = box =>
            {
                showAnimControls = box.Selected;
                if (showAnimControls)
                {
                    spritesheetToggle.Selected = false;
                    spriteDimensionsToggle.Selected = false;
                    ragdollToggle.Selected = false;
                    ResetParamsEditor();
                }
                return true;
            };
            paramsToggle.OnSelected = box =>
            {
                showParamsEditor = box.Selected;
                if (showParamsEditor)
                {
                    spritesheetToggle.Selected = false;
                }
                return true;
            };
            spriteDimensionsToggle.OnSelected = box =>
            {
                editSpriteDimensions = box.Selected;
                if (editSpriteDimensions)
                {
                    ragdollToggle.Selected = false;
                    editAnimsToggle.Selected = false;
                    spritesheetToggle.Selected = true;
                    ResetParamsEditor();
                }
                return true;
            };
            ragdollToggle.OnSelected = box =>
            {
                editRagdoll = box.Selected;
                if (editRagdoll)
                {
                    spriteDimensionsToggle.Selected = false;
                    editAnimsToggle.Selected = false;
                    ResetParamsEditor();
                }
                else
                {
                    jointPositionsToggle.Selected = false;
                    jointLimitsToggle.Selected = false;
                }
                return true;
            };
            jointPositionsToggle.OnSelected = box =>
            {
                editJointPositions = box.Selected;
                if (editJointPositions)
                {
                    ragdollToggle.Selected = true;
                    spritesheetToggle.Selected = !paramsToggle.Selected;
                    jointLimitsToggle.Selected = false;
                }
                return true;
            };
            jointLimitsToggle.OnSelected = box =>
            {
                editJointLimits = box.Selected;
                if (editJointLimits)
                {
                    ragdollToggle.Selected = true;
                    spritesheetToggle.Selected = !paramsToggle.Selected;
                    jointPositionsToggle.Selected = false;
                }
                return true;
            };
            spritesheetToggle.OnSelected = box =>
            {
                showSpritesheet = box.Selected;
                if (showSpritesheet)
                {
                    editAnimsToggle.Selected = false;
                    paramsToggle.Selected = false;
                }
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
                OnSelected = (GUITickBox box) =>
                {
                    character.OverrideMovement = box.Selected ? new Vector2(1, 0) as Vector2? : null;
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
                GUI.AddMessage($"Ragdoll saved", Color.Green, font: GUI.Font);
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
                character.AnimController.ResetRagdoll();
                CreateCenterPanel();
                ResetParamsEditor();
                widgets.Values.ForEach(w => w.refresh?.Invoke());
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
                    character.AnimController.Recreate(ragdoll);
                    TeleportTo(spawnPosition);
                    ResetParamsEditor();
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
                    character.AnimController.Recreate(RagdollParams);
                    TeleportTo(spawnPosition);
                    character.AnimController.ResetLimbs();
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
            if (editRagdoll || editSpriteDimensions)
            {
                RagdollParams.AddToEditor(ParamsEditor.Instance);
            }
            else
            {
                AnimParams.ForEach(p => p.AddToEditor(ParamsEditor.Instance));
            }
        }

        private void TryUpdateAnimParam(string name, object value) => TryUpdateParam(character.AnimController.CurrentAnimationParams, name, value);
        private void TryUpdateRagdollParam(string name, object value) => TryUpdateParam(RagdollParams, name, value);

        private void TryUpdateParam(EditableParams editableParams, string name, object value)
        {
            if (editableParams.SerializableProperties.TryGetValue(name, out SerializableProperty p))
            {
                editableParams.SerializableEntityEditor.UpdateValue(p, value);
            }
        }

        private void TryUpdateJointParam(LimbJoint joint, string name, object value) => TryUpdateSubParam(joint.jointParams, name, value);
        private void TryUpdateLimbParam(Limb limb, string name, object value) => TryUpdateSubParam(limb.limbParams, name, value);

        private void TryUpdateSubParam(RagdollSubParams ragdollParams, string name, object value)
        {
            if (ragdollParams.SerializableProperties.TryGetValue(name, out SerializableProperty p))
            {
                ragdollParams.SerializableEntityEditor.UpdateValue(p, value);
            }
            else
            {
                var subParams = ragdollParams.SubParams.Where(sp => sp.SerializableProperties.ContainsKey(name)).FirstOrDefault();
                if (subParams != null)
                {
                    if (subParams.SerializableProperties.TryGetValue(name, out p))
                    {
                        subParams.SerializableEntityEditor.UpdateValue(p, value);
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

        public override void AddToGUIUpdateList()
        {
            //base.AddToGUIUpdateList();
            rightPanel.AddToGUIUpdateList();
            generalControls.AddToGUIUpdateList();
            if (showAnimControls)
            {
                animationControls.AddToGUIUpdateList();
            }
            if (showSpritesheet)
            {
                spriteSheetControls.AddToGUIUpdateList();
            }
            if (editRagdoll)
            {
                ragdollControls.AddToGUIUpdateList();
            }
            if (editSpriteDimensions)
            {
                spriteControls.AddToGUIUpdateList();
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
                if (character.Position.X < min)
                {
                    UpdateWalls(false);
                }
                else if (character.Position.X > max)
                {
                    UpdateWalls(true);
                }
                GameMain.World.Step((float)deltaTime);
            }
            //Cam.TargetPos = Vector2.Zero;
            Cam.MoveCamera((float)deltaTime, allowMove: false, allowZoom: GUI.MouseOn == null);
            Cam.Position = character.Position;
            widgets.Values.ForEach(w => w.Update((float)deltaTime));
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

            base.Draw(deltaTime, graphics, spriteBatch);
            scaledMouseSpeed = PlayerInput.MouseSpeedPerSecond * (float)deltaTime;
            graphics.Clear(backgroundColor);
            Cam.UpdateTransform(true);

            // Submarine
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            Submarine.Draw(spriteBatch, true);
            spriteBatch.End();

            // Character
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            character.Draw(spriteBatch, Cam);
            if (GameMain.DebugDraw)
            {
                character.AnimController.DebugDraw(spriteBatch);
            }
            spriteBatch.End();

            // GUI
            spriteBatch.Begin(SpriteSortMode.Immediate, rasterizerState: GameMain.ScissorTestEnable);
            if (showAnimControls)
            {
                DrawAnimationControls(spriteBatch);
            }
            if (editSpriteDimensions)
            {
                DrawSpriteOriginEditor(spriteBatch);
            }
            if (editRagdoll)
            {
                DrawRagdollEditor(spriteBatch, (float)deltaTime);
            }
            if (showSpritesheet)
            {
                DrawSpritesheetEditor(spriteBatch, (float)deltaTime);
            }
            Structure wall = CurrentWall.walls.FirstOrDefault();
            Vector2 indicatorPos = wall == null ? originalWall.walls.First().DrawPosition : wall.DrawPosition;
            GUI.DrawIndicator(spriteBatch, indicatorPos, Cam, 700, GUI.SubmarineIcon, Color.White);
            GUI.Draw(Cam, spriteBatch);
            if (isFreezed)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 35, 100), "FREEZED", Color.Blue, Color.White * 0.5f, 10, GUI.Font);
            }
            if (animTestPoseToggle.Selected)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 100, 150), "Animation Test Pose Enabled", Color.Blue, Color.White * 0.5f, 10, GUI.Font);
            }
            if (editJointLimits)
            {
                GUI.DrawString(spriteBatch, new Vector2(spriteControls.Rect.Center.X, 50), "Spritesheet Orientation:", Color.White, Color.Gray * 0.5f, 10, GUI.Font);
                DrawRadialWidget(spriteBatch, new Vector2(spriteControls.Rect.Center.X + 210, 60), spriteSheetOrientation, string.Empty, Color.White, 
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

        #region Helpers
        private Vector2 ScreenToSim(float x, float y) => ScreenToSim(new Vector2(x, y));
        private Vector2 ScreenToSim(Vector2 p) => ConvertUnits.ToSimUnits(Cam.ScreenToWorld(p));
        private Vector2 SimToScreen(float x, float y) => SimToScreen(new Vector2(x, y));
        private Vector2 SimToScreen(Vector2 p) => Cam.WorldToScreen(ConvertUnits.ToDisplayUnits(p));

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
        #endregion

        #region Animation Controls
        private void DrawAnimationControls(SpriteBatch spriteBatch)
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

            if (GameMain.DebugDraw)
            {
                //GUI.DrawLine(spriteBatch, charDrawPos, charDrawPos + screenSpaceForward * 40, Color.Blue);
                //GUI.DrawLine(spriteBatch, charDrawPos, charDrawPos + screenSpaceLeft * 40, Color.Red);
            }

            bool altDown = PlayerInput.KeyDown(Keys.LeftAlt);
            if (!altDown)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 100, 20), "HOLD \"Left Alt\" TO ADJUST THE CYCLE SPEED", Color.White, Color.Black * 0.5f, 10, GUI.Font);
            }
            // Widgets for all anims -->
            Vector2 referencePoint = SimToScreen(head != null ? head.SimPosition : collider.SimPosition);
            Vector2 drawPos = referencePoint;
            if (altDown)
            {
                if (selectedWidget == "Movement Speed") { selectedWidget = null; }
                // Cycle speed
                float multiplier = 0.25f;
                drawPos += forward * ConvertUnits.ToDisplayUnits(animParams.CycleSpeed * multiplier) * Cam.Zoom;
                DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 20, Color.MediumPurple, "Cycle Speed", () =>
                {
                    float speed = animParams.CycleSpeed + ConvertUnits.ToSimUnits(Vector2.Multiply(scaledMouseSpeed / multiplier, forward).Combine()) / Cam.Zoom;
                    TryUpdateAnimParam("cyclespeed", speed);
                    GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.MediumPurple);
                });
                GUI.DrawLine(spriteBatch, drawPos + forward * 10, drawPos + forward * 15, Color.MediumPurple);
            }
            else
            {
                if (selectedWidget == "Cycle Speed") { selectedWidget = null; }
                // Movement speed
                float multiplier = 0.5f;
                drawPos += forward * ConvertUnits.ToDisplayUnits(animParams.MovementSpeed * multiplier) * Cam.Zoom;
                DrawWidget(spriteBatch, drawPos, WidgetType.Circle, 20, Color.Turquoise, "Movement Speed", () =>
                {
                    float speed = animParams.MovementSpeed + ConvertUnits.ToSimUnits(Vector2.Multiply(scaledMouseSpeed / multiplier, forward).Combine()) / Cam.Zoom;
                    TryUpdateAnimParam("movementspeed", MathHelper.Clamp(speed, 0.1f, Ragdoll.MAX_SPEED));
                    GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.Turquoise);
                    if (humanSwimParams != null)
                    {
                        TryUpdateAnimParam("cyclespeed", animParams.MovementSpeed);
                    }
                });
                GUI.DrawLine(spriteBatch, drawPos + forward * 10, drawPos + forward * 15, Color.Turquoise);
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
            if (foot != null)
            {
                // Fish only
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
                /*if (legs != null || foot != null)
                {
                    drawPos = SimToScreen(colliderBottom + simSpaceForward * 0.3f);
                    float multiplier = 10;
                    DrawCircularWidget(spriteBatch, drawPos, humanGroundedParams.LegCorrectionTorque * multiplier, "Leg Angle", Color.LightBlue, angle =>
                    {
                        TryUpdateAnimParam("legcorrectiontorque", angle / multiplier);
                        GUI.DrawString(spriteBatch, drawPos, humanGroundedParams.LegCorrectionTorque.FormatSingleDecimal(), Color.Black, Color.LightBlue, font: GUI.SmallFont);
                    }, circleRadius: 25, rotationOffset: collider.Rotation, clockWise: dir < 0, displayAngle: false);
                }*/
                if (hand != null || arm != null)
                {
                    referencePoint = SimToScreen(collider.SimPosition + simSpaceForward * 0.2f);
                    var v = ConvertUnits.ToDisplayUnits(humanGroundedParams.HandMoveAmount);
                    drawPos = referencePoint + new Vector2(v.X * dir, v.Y) * Cam.Zoom;
                    var origin = drawPos - new Vector2(widgetDefaultSize / 2, 0) * -dir;
                    DrawWidget(spriteBatch, drawPos, WidgetType.Rectangle, widgetDefaultSize, Color.LightGreen, "Hand Move Amount", () =>
                    {
                        var transformedInput = ConvertUnits.ToSimUnits(new Vector2(scaledMouseSpeed.X * dir, scaledMouseSpeed.Y)) / Cam.Zoom;
                        TryUpdateAnimParam("handmoveamount", humanGroundedParams.HandMoveAmount + transformedInput);
                        GUI.DrawLine(spriteBatch, origin, referencePoint, Color.LightGreen);
                    });
                    GUI.DrawLine(spriteBatch, origin, origin + Vector2.UnitX * 5 * dir, Color.LightGreen);
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
                    //GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, amplitude, length, 5000, points, Color.NavajoWhite);

                });
                // Amplitude
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.NavajoWhite, "Wave Amplitude", () =>
                {
                    var input = Vector2.Multiply(ConvertUnits.ToSimUnits(scaledMouseSpeed), screenSpaceLeft).Combine() * dir / Cam.Zoom * amplitudeMultiplier;
                    TryUpdateAnimParam("waveamplitude", MathHelper.Clamp(fishSwimParams.WaveAmplitude + input, -4, 4));
                    //GUI.DrawLine(spriteBatch, start, control, Color.Purple);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.Purple);
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
                    //GUI.DrawLine(spriteBatch, drawPos, referencePoint, Color.NavajoWhite);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.NavajoWhite);
                    GUI.DrawSineWithDots(spriteBatch, referencePoint, -toRefPoint, legMoveAmount, legCycleLength, 5000, points, Color.NavajoWhite);
                });
                // Movement amount
                DrawWidget(spriteBatch, control, WidgetType.Circle, 15, Color.NavajoWhite, "Leg Movement Amount", () =>
                {
                    float input = Vector2.Multiply(ConvertUnits.ToSimUnits(scaledMouseSpeed), screenSpaceLeft).Combine() * dir / Cam.Zoom * lengthMultiplier;
                    TryUpdateAnimParam("legmoveamount", MathHelper.Clamp(humanSwimParams.LegMoveAmount + input, -2, 2));
                    //GUI.DrawLine(spriteBatch, start, control, Color.NavajoWhite);
                    //GUI.DrawBezierWithDots(spriteBatch, referencePoint, drawPos, control, points, Color.NavajoWhite);
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
        private void DrawSpriteOriginEditor(SpriteBatch spriteBatch)
        {
            float inputMultiplier = 0.5f;
            Limb selectedLimb = null;
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb == null || limb.ActiveSprite == null) { continue; }
                var origin = limb.ActiveSprite.Origin;
                var sourceRect = limb.ActiveSprite.SourceRect;
                Vector2 size = sourceRect.Size.ToVector2() * Cam.Zoom * limb.Scale;
                Vector2 up = VectorExtensions.Backward(limb.Rotation);
                Vector2 left = up.Right();
                Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
                var relativeOrigin = new Vector2(origin.X / sourceRect.Width, origin.Y / sourceRect.Height);
                var relativeOffset = relativeOrigin - new Vector2(0.5f, 0.5f);
                Vector2 offset = new Vector2(relativeOffset.X * sourceRect.Width, relativeOffset.Y * sourceRect.Height);
                offset = offset.X * left + offset.Y * up;
                // There's a calculation error in here somewhere, but the magic number 1.8 seems to do the trick.
                Vector2 center = limbScreenPos + offset * 1.8f;
                corners = MathUtils.GetImaginaryRect(corners, up, center, size);
                //GUI.DrawRectangle(spriteBatch, center - Vector2.One * 2, Vector2.One * 4, Color.Black, isFilled: true);
                GUI.DrawRectangle(spriteBatch, corners, Color.Red);
                //GUI.DrawLine(spriteBatch, limbScreenPos, limbScreenPos + up * 20, Color.White, width: 3);
                //GUI.DrawLine(spriteBatch, limbScreenPos, limbScreenPos + up * 20, Color.Red);
                // Limb positions
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.White, width: 3);
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.White, width: 3);
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitY * 5.0f, limbScreenPos - Vector2.UnitY * 5.0f, Color.Red);
                GUI.DrawLine(spriteBatch, limbScreenPos + Vector2.UnitX * 5.0f, limbScreenPos - Vector2.UnitX * 5.0f, Color.Red);
                if (PlayerInput.LeftButtonHeld() && selectedWidget == null && MathUtils.RectangleContainsPoint(corners, PlayerInput.MousePosition))
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
                float multiplier = 0.5f;
                Vector2 up = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(selectedLimb.Rotation));
                var input = -scaledMouseSpeed * inputMultiplier * Cam.Zoom / selectedLimb.Scale * multiplier;
                var sprite = selectedLimb.ActiveSprite;
                var origin = sprite.Origin;
                origin += input.TransformVector(up);
                var sourceRect = sprite.SourceRect;
                var max = new Vector2(sourceRect.Width, sourceRect.Height);
                sprite.Origin = origin.Clamp(Vector2.Zero, max);
                if (selectedLimb.DamagedSprite != null)
                {
                    selectedLimb.DamagedSprite.Origin = sprite.Origin;
                }
                if (character.AnimController.IsFlipped)
                {
                    origin.X = Math.Abs(origin.X - sourceRect.Width);
                }
                var relativeOrigin = new Vector2(origin.X / sourceRect.Width, origin.Y / sourceRect.Height);
                TryUpdateLimbParam(selectedLimb, "origin", relativeOrigin);
                if (limbPairEditing)
                {
                    UpdateOtherJoints(selectedLimb, (otherLimb, otherJoint) =>
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
        }

        private void DrawRagdollEditor(SpriteBatch spriteBatch, float deltaTime)
        {
            bool altDown = PlayerInput.KeyDown(Keys.LeftAlt);
            if (!altDown && editJointPositions)
            {
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2 - 200, 20), "HOLD \"Left Alt\" TO MANIPULATE THE OTHER END OF THE JOINT", Color.White, Color.Black * 0.5f, 10, GUI.Font);
            }
            foreach (Limb limb in character.AnimController.Limbs)
            {
                Vector2 limbScreenPos = SimToScreen(limb.SimPosition);
                
                if (limb.type == LimbType.LeftFoot || limb.type == LimbType.RightFoot || limb.type == LimbType.LeftHand || limb.type == LimbType.RightHand)
                {
                    var pullJointWidgetSize = new Vector2(5, 5);
                    Vector2 tformedPullPos = SimToScreen(limb.PullJointWorldAnchorA);
                    GUI.DrawRectangle(spriteBatch, tformedPullPos - pullJointWidgetSize / 2, pullJointWidgetSize, Color.Red, true);
                    DrawWidget(spriteBatch, tformedPullPos, WidgetType.Rectangle, 8, Color.Cyan, "IK Target",
                    () =>
                    {
                        Vector2 simPullPos = ScreenToSim(PlayerInput.MousePosition);
                        limb.PullJointWorldAnchorA = simPullPos;
                        TryUpdateLimbParam(limb, "pullpos", ConvertUnits.ToDisplayUnits(limb.PullJointLocalAnchorA / limb.limbParams.Ragdoll.LimbScale));
                        GUI.DrawLine(spriteBatch, SimToScreen(limb.SimPosition), tformedPullPos, Color.MediumPurple);
                    });
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
                    var f = Vector2.Transform(jointPos, Matrix.CreateRotationZ(limb.Rotation));
                    f.Y = -f.Y;
                    Vector2 tformedJointPos = limbScreenPos + f * Cam.Zoom;
                    ShapeExtensions.DrawPoint(spriteBatch, limbScreenPos, Color.Black, size: 5);
                    ShapeExtensions.DrawPoint(spriteBatch, limbScreenPos, Color.White, size: 1);
                    GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.Black, width: 3);
                    GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.White, width: 1);
                    if (editJointLimits)
                    {
                        if (joint.BodyA != limb.body.FarseerBody) { continue; }
                        var toggleWidget = GetToggleWidget($"{joint.jointParams.Name} limits toggle ragdoll", $"{joint.jointParams.Name} limits toggle spritesheet", joint);
                        toggleWidget.DrawPos = tformedJointPos;
                        toggleWidget.Draw(spriteBatch, deltaTime);
                        if (joint.LimitEnabled)
                        {
                            Vector2 to = tformedJointPos + VectorExtensions.Forward(joint.LimbB.Rotation + MathHelper.ToRadians(-spriteSheetOrientation), 20);
                            DrawJointLimitWidgets(spriteBatch, limb, joint, tformedJointPos, autoFreeze: true, allowPairEditing: true, rotationOffset: limb.Rotation);
                            GUI.DrawLine(spriteBatch, tformedJointPos, to, Color.Magenta, width: 2);
                        }
                    }
                    else if (editJointPositions)
                    {
                        if (altDown && joint.BodyA == limb.body.FarseerBody)
                        {
                            continue;
                        }
                        if (!altDown && joint.BodyB == limb.body.FarseerBody)
                        {
                            continue;
                        }
                        Color color = joint.BodyA == limb.body.FarseerBody ? Color.Red : Color.Blue;
                        var widgetSize = new Vector2(5, 5);
                        var rect = new Rectangle((tformedJointPos - widgetSize / 2).ToPoint(), widgetSize.ToPoint());
                        GUI.DrawRectangle(spriteBatch, tformedJointPos - widgetSize / 2, widgetSize, color, true);
                        var inputRect = rect;
                        inputRect.Inflate(widgetSize.X, widgetSize.Y);
                        //GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + up * 20, Color.White);
                        if (inputRect.Contains(PlayerInput.MousePosition))
                        {
                            //GUI.DrawLine(spriteBatch, tformedJointPos, tformedJointPos + up * 20, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbScreenPos, tformedJointPos, Color.Yellow, width: 3);
                            GUI.DrawRectangle(spriteBatch, inputRect, Color.Red);
                            GUI.DrawString(spriteBatch, tformedJointPos + new Vector2(widgetSize.X, -widgetSize.Y) * 2, $"{joint.jointParams.Name} {jointPos.FormatZeroDecimal()}", Color.White, Color.Black * 0.5f);
                            if (PlayerInput.LeftButtonHeld())
                            {
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
                                }
                                else
                                {
                                    joint.LocalAnchorB += input;
                                    TryUpdateJointParam(joint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
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

        private void UpdateOtherJoints(Limb limb, Action<Limb, LimbJoint> updateAction)
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

                    GUI.DrawRectangle(spriteBatch, rect, Color.Red);
                    Vector2 origin = limb.ActiveSprite.Origin;
                    Vector2 limbBodyPos = new Vector2(rect.X + origin.X * spriteSheetZoom, rect.Y + origin.Y * spriteSheetZoom);
                    // The origin is manipulated when the character is flipped. We have to undo it here.
                    if (character.AnimController.Dir < 0)
                    {
                        limbBodyPos.X = rect.X + rect.Width - (float)Math.Round(origin.X * spriteSheetZoom);
                    }
                    if (editRagdoll)
                    {
                        DrawSpritesheetRagdollEditor(spriteBatch, deltaTime, limb, limbBodyPos);
                    }
                    if (editSpriteDimensions)
                    {
                        if (!lockSpriteOrigin)
                        {
                            // Draw the sprite origins
                            GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.White, width: 3);
                            GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitY * 5.0f, limbBodyPos - Vector2.UnitY * 5.0f, Color.Red);
                            GUI.DrawLine(spriteBatch, limbBodyPos + Vector2.UnitX * 5.0f, limbBodyPos - Vector2.UnitX * 5.0f, Color.Red);
                        }
                        // Draw the source rect widgets
                        int widgetSize = 8;
                        int halfSize = widgetSize / 2;
                        Vector2 stringOffset = new Vector2(5, 14);
                        var topLeft = rect.Location.ToVector2();
                        var topRight = new Vector2(topLeft.X + rect.Width, topLeft.Y);
                        var bottomRight = new Vector2(topRight.X, topRight.Y + rect.Height);
                        //var bottomLeft = new Vector2(topLeft.X, bottomRight.Y);
                        if (!lockSpritePosition)
                        {
                            DrawWidget(spriteBatch, topLeft - new Vector2(halfSize), WidgetType.Rectangle, widgetSize, Color.Red, "Position", () =>
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
                                GUI.DrawString(spriteBatch, topLeft + new Vector2(stringOffset.X, -stringOffset.Y * 1.5f), limb.ActiveSprite.SourceRect.Location.ToString(), Color.White, Color.Black * 0.5f);
                            }, autoFreeze: false);
                        }
                        if (!lockSpriteSize)
                        {
                            DrawWidget(spriteBatch, bottomRight, WidgetType.Rectangle, widgetSize, Color.White, "Size", () =>
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
                                // Also the origin should be adjusted to the new width, so that it will remain at the same position relative to the source rect location.
                                limb.ActiveSprite.Origin = new Vector2(origin.X - dx, origin.Y);
                                if (limb.DamagedSprite != null)
                                {
                                    limb.DamagedSprite.SourceRect = limb.ActiveSprite.SourceRect;
                                    limb.DamagedSprite.Origin = limb.ActiveSprite.Origin;
                                }
                                if (character.AnimController.IsFlipped)
                                {
                                    origin.X = Math.Abs(origin.X - newRect.Width);
                                }
                                var relativeOrigin = new Vector2(origin.X / newRect.Width, origin.Y / newRect.Height);
                                TryUpdateLimbParam(limb, "origin", relativeOrigin);
                                TryUpdateLimbParam(limb, "sourcerect", newRect);
                                GUI.DrawString(spriteBatch, bottomRight + stringOffset, limb.ActiveSprite.size.FormatZeroDecimal(), Color.White, Color.Black * 0.5f);
                            }, autoFreeze: false);
                        }
                        if (PlayerInput.LeftButtonHeld() && selectedWidget == null)
                        {
                            if (!lockSpriteOrigin && rect.Contains(PlayerInput.MousePosition))
                            {
                                var input = scaledMouseSpeed;
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
                                GUI.DrawString(spriteBatch, limbBodyPos + new Vector2(10, -10), relativeOrigin.FormatDoubleDecimal(), Color.White, Color.Black * 0.5f);
                            }
                        }
                    }
                }
                offsetY += (int)(texture.Height * spriteSheetZoom);
            }
        }

        private void DrawSpritesheetRagdollEditor(SpriteBatch spriteBatch, float deltaTime, Limb limb, Vector2 limbScreenPos, float spriteRotation = 0)
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
                Vector2 tformedJointPos = jointPos = jointPos / RagdollParams.JointScale * spriteSheetZoom;
                tformedJointPos.Y = -tformedJointPos.Y;
                tformedJointPos.X *= character.AnimController.Dir;
                tformedJointPos += limbScreenPos;
                if (editJointLimits)
                {
                    if (joint.BodyA == limb.body.FarseerBody)
                    {
                        var toggleWidget = GetToggleWidget($"{joint.jointParams.Name} limits toggle spritesheet", $"{joint.jointParams.Name} limits toggle ragdoll", joint);
                        toggleWidget.DrawPos = tformedJointPos;
                        toggleWidget.Draw(spriteBatch, deltaTime);
                        if (joint.LimitEnabled)
                        {
                            DrawJointLimitWidgets(spriteBatch, limb, joint, tformedJointPos, autoFreeze: false, allowPairEditing: true);
                        }
                    }
                }
                else if (editJointPositions)
                {
                    Color color = joint.BodyA == limb.body.FarseerBody ? Color.Red : Color.Blue;
                    Vector2 widgetSize = new Vector2(5.0f, 5.0f); ;
                    var rect = new Rectangle((tformedJointPos - widgetSize / 2).ToPoint(), widgetSize.ToPoint());
                    var inputRect = rect;
                    inputRect.Inflate(widgetSize.X * 0.75f, widgetSize.Y * 0.75f);
                    GUI.DrawRectangle(spriteBatch, rect, color, isFilled: true);
                    if (inputRect.Contains(PlayerInput.MousePosition))
                    {          
                        GUI.DrawString(spriteBatch, tformedJointPos + Vector2.One * 10.0f, $"{joint.jointParams.Name} {jointPos.FormatZeroDecimal()}", Color.White, Color.Black * 0.5f);
                        GUI.DrawRectangle(spriteBatch, inputRect, color);
                        if (PlayerInput.LeftButtonHeld())
                        {
                            Vector2 input = ConvertUnits.ToSimUnits(scaledMouseSpeed);
                            input.Y = -input.Y;
                            input.X *= character.AnimController.Dir;
                            input *= limb.Scale / spriteSheetZoom;
                            if (joint.BodyA == limb.body.FarseerBody)
                            {
                                joint.LocalAnchorA += input;
                                TryUpdateJointParam(joint, "limb1anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorA));
                            }
                            else
                            {
                                joint.LocalAnchorB += input;
                                TryUpdateJointParam(joint, "limb2anchor", ConvertUnits.ToDisplayUnits(joint.LocalAnchorB));
                            }
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
                if (allowPairEditing && limbPairEditing)
                {
                    UpdateOtherJoints(limb, (otherLimb, otherJoint) =>
                    {
                        if (IsMatchingLimb(limb, otherLimb, joint, otherJoint))
                        {
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
                if (allowPairEditing && limbPairEditing)
                {
                    UpdateOtherJoints(limb, (otherLimb, otherJoint) =>
                    {
                        if (IsMatchingLimb(limb, otherLimb, joint, otherJoint))
                        {
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

        #region Widgets as classes (experimental)
        private Dictionary<string, Widget> widgets = new Dictionary<string, Widget>();

        private Widget GetToggleWidget(string id, string linkedId, LimbJoint joint)
        {
            // Joint creation method
            Widget CreateJointLimitToggle(string ID, LimbJoint j)
            {
                var widget = new Widget(ID, 10, Widget.Shape.Circle);
                widget.refresh = () =>
                {
                    if (j.LimitEnabled)
                    {
                        widget.tooltip = j.jointParams.Name + " Disable Joint Limits";
                        widget.color = Color.LightGreen;
                    }
                    else
                    {
                        widget.tooltip = j.jointParams.Name + " Enable Joint Limits";
                        widget.color = Color.Red;
                    }
                };
                widget.refresh();
                widget.Clicked += () =>
                {
                    j.LimitEnabled = !j.LimitEnabled;
                    TryUpdateJointParam(j, "limitenabled", j.LimitEnabled);
                    if (j.LimitEnabled)
                    {
                        if (float.IsNaN(j.jointParams.UpperLimit))
                        {
                            joint.UpperLimit = 0;
                            TryUpdateJointParam(j, "upperlimit", 0);
                        }
                        if (float.IsNaN(j.jointParams.LowerLimit))
                        {
                            joint.LowerLimit = 0;
                            TryUpdateJointParam(j, "lowerlimit", 0);
                        }
                    }
                    widget.refresh();
                    widget.linkedWidget?.refresh();
                    if (limbPairEditing)
                    {
                        UpdateOtherJoints(j.LimbA, (otherLimb, otherJoint) =>
                        {
                            if (IsMatchingLimb(j.LimbA, otherLimb, joint, otherJoint))
                            {
                                if (widgets.TryGetValue($"{otherJoint.jointParams.Name} limits toggle spritesheet", out Widget otherWidget))
                                {
                                    otherJoint.LimitEnabled = joint.LimitEnabled;
                                    TryUpdateJointParam(otherJoint, "limitenabled", joint.LimitEnabled);
                                    if (joint.LimitEnabled)
                                    {
                                        if (float.IsNaN(otherJoint.jointParams.UpperLimit))
                                        {
                                            otherJoint.UpperLimit = 0;
                                            TryUpdateJointParam(otherJoint, "upperlimit", 0);
                                        }
                                        if (float.IsNaN(otherJoint.jointParams.LowerLimit))
                                        {
                                            otherJoint.LowerLimit = 0;
                                            TryUpdateJointParam(otherJoint, "lowerlimit", 0);
                                        }
                                    }
                                    otherWidget.refresh();
                                    otherWidget.linkedWidget?.refresh();
                                }
                            }
                        });
                    }
                };
                widget.PreUpdate += dTime => widget.Enabled = editJointLimits;
                widgets.Add(ID, widget);
                return widget;
            }
            // Handle joint linking and create the joints
            if (!widgets.TryGetValue(id, out Widget toggleWidget))
            {
                if (!widgets.TryGetValue(linkedId, out Widget linkedWidget))
                {
                    linkedWidget = CreateJointLimitToggle(linkedId, joint);
                }
                toggleWidget = CreateJointLimitToggle(id, joint);
                toggleWidget.linkedWidget = linkedWidget;
                linkedWidget.linkedWidget = toggleWidget;
            }
            return toggleWidget;
        }

        //// TODO: test and fix
        //private class RadialWidget : Widget
        //{
        //    public float angle;
        //    public float circleRadius = 30;
        //    public float rotationOffset;
        //    public bool clockWise = true;
        //    public bool displayAngle = true;
        //    public Vector2 center;

        //    public RadialWidget(string id, Shape shape, Vector2 center) : base(id, center, shape)
        //    {
        //        this.center = center;
        //    }

        //    public override void Update(float deltaTime)
        //    {
        //        if (!MathUtils.IsValid(angle))
        //        {
        //            angle = 0;
        //        }
        //        var up = -VectorExtensions.Forward(rotationOffset, circleRadius);
        //        drawPos = center + up;
        //        drawPos = MathUtils.RotatePointAroundTarget(drawPos, center, angle, clockWise);
        //        base.Update(deltaTime);
        //        if (IsControlled)
        //        {
        //            var rotationOffsetInDegrees = MathHelper.ToDegrees(MathUtils.WrapAngleTwoPi(rotationOffset));
        //            // Collider rotation is counter-clockwise, todo: this should be handled before passing the arguments
        //            var transformedRot = clockWise ? angle - rotationOffsetInDegrees : angle + rotationOffsetInDegrees;
        //            if (transformedRot > 360)
        //            {
        //                transformedRot -= 360;
        //            }
        //            else if (transformedRot < -360)
        //            {
        //                transformedRot += 360;
        //            }
        //            var input = PlayerInput.MouseSpeed * 1.5f;
        //            float x = input.X;
        //            float y = input.Y;
        //            if (clockWise)
        //            {
        //                if ((transformedRot > 90 && transformedRot < 270) || (transformedRot < -90 && transformedRot > -270))
        //                {
        //                    x = -x;
        //                }
        //                if (transformedRot > 180 || (transformedRot < 0 && transformedRot > -180))
        //                {
        //                    y = -y;
        //                }
        //            }
        //            else
        //            {
        //                if (transformedRot < 90 && transformedRot > -90)
        //                {
        //                    x = -x;
        //                }
        //                if (transformedRot < 0 && transformedRot > -180)
        //                {
        //                    y = -y;
        //                }
        //            }
        //            angle += x + y;
        //            if (angle > 360 || angle < -360)
        //            {
        //                angle = 0;
        //            }
        //        }
        //    }

        //    public override void Draw(SpriteBatch spriteBatch, float deltaTime)
        //    {
        //        base.Draw(spriteBatch, deltaTime);
        //        GUI.DrawLine(spriteBatch, drawPos, drawPos, color);
        //        // Draw controller widget
        //        if (IsSelected)
        //        {
        //            //var up = -VectorExtensions.Forward(rotationOffset, circleRadius);
        //            //GUI.DrawLine(spriteBatch, drawPos, drawPos + up, Color.Red);
        //            ShapeExtensions.DrawCircle(spriteBatch, drawPos, circleRadius, sides, color, thickness: 1);
        //            if (displayAngle)
        //            {
        //                GUI.DrawString(spriteBatch, drawPos, angle.FormatAsInt(), textColor, textBackgroundColor, font: GUI.SmallFont);
        //            }
        //        }
        //    }
        //}
        #endregion
    }
}
