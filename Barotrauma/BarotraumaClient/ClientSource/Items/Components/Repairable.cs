using System;
using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IDrawableComponent
    {
        public GUIButton RepairButton { get; private set; }

        public GUIButton SabotageButton { get; private set; }

        public GUIButton TinkerButton { get; private set; }

        private GUIProgressBar progressBar;

        private GUITextBlock progressBarOverlayText;

        private GUILayoutGroup extraButtonContainer;

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();
        //the corresponding particle emitter is active when the condition is within this range
        private readonly List<Vector2> particleEmitterConditionRanges = new List<Vector2>();

        private SoundChannel repairSoundChannel;

        private LocalizedString repairButtonText, repairingText;
        private LocalizedString sabotageButtonText, sabotagingText;
        private LocalizedString tinkerButtonText, tinkeringText;

        private FixActions requestStartFixAction;

        private bool qteSuccess;

        private float qteTimer;
        private const float QteDuration = 0.5f;
        private float qteCooldown;
        private const float QteCooldownDuration = 0.5f;

        public float FakeBrokenTimer;

        [Serialize("", IsPropertySaveable.No, description: "An optional description of the needed repairs displayed in the repair interface.")]
        public string Description
        {
            get;
            set;
        }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        public override bool ShouldDrawHUD(Character character)
        {
            if (item.HiddenInGame) { return false; }
            if (!HasRequiredItems(character, false) || character.SelectedConstruction != item) { return false; }
            if (character.IsTraitor && item.ConditionPercentage > MinSabotageCondition) { return true; }

            float defaultMaxCondition = item.MaxCondition / item.MaxRepairConditionMultiplier;

            if (MathUtils.Percentage(item.Condition, defaultMaxCondition) < RepairThreshold) { return true; }

            if (CurrentFixer == character)
            {
                if (item.Condition < item.MaxCondition)
                {
                    return true;
                }
            }
            if (IsTinkerable(character)) { return true; }

            return false;
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            CreateGUI();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "emitter":
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        float minCondition = subElement.GetAttributeFloat("mincondition", 0.0f);
                        float maxCondition = subElement.GetAttributeFloat("maxcondition", 100.0f);

                        if (maxCondition < minCondition)
                        {
                            DebugConsole.ThrowError("Invalid damage particle configuration in the Repairable component of " + item.Name + ". MaxCondition needs to be larger than MinCondition.");
                            float temp = maxCondition;
                            maxCondition = minCondition;
                            minCondition = temp;
                        }
                        particleEmitterConditionRanges.Add(new Vector2(minCondition, maxCondition));

                        break;
                }
            }
        }
        
        private void RecreateGUI()
        {
            if (GuiFrame != null)
            {
                GuiFrame.ClearChildren();
                CreateGUI();
            }
        }

        protected override void CreateGUI()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.75f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.05f,
                CanBeFocused = true
            };
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                header, textAlignment: Alignment.TopCenter, font: GUIStyle.LargeFont);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                Description, font: GUIStyle.SmallFont, wrap: true);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                TextManager.Get("RequiredRepairSkills"), font: GUIStyle.SubHeadingFont);
            for (int i = 0; i < requiredSkills.Count; i++)
            {
                var skillText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                    "   - " + TextManager.AddPunctuation(':', TextManager.Get("SkillName." + requiredSkills[i].Identifier), ((int) Math.Round(requiredSkills[i].Level * SkillRequirementMultiplier)).ToString()),
                    font: GUIStyle.SmallFont)
                {
                    UserData = requiredSkills[i]
                };
            }

            var progressBarHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            progressBar = new GUIProgressBar(new RectTransform(new Vector2(0.6f, 1.0f), progressBarHolder.RectTransform),
                color: GUIStyle.Green, barSize: 0.0f, style: "DeviceProgressBar");

            progressBarOverlayText = new GUITextBlock(new RectTransform(Vector2.One, progressBar.RectTransform), string.Empty, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center)
            {
                IgnoreLayoutGroups = true
            };

            qteTimer = QteDuration;

            repairButtonText = TextManager.Get("RepairButton");
            repairingText = TextManager.Get("Repairing");
            RepairButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), progressBarHolder.RectTransform, Anchor.TopCenter), repairButtonText)
            {
                OnClicked = (btn, obj) =>
                {
                    requestStartFixAction = FixActions.Repair;
                    item.CreateClientEvent(this);
                    return true;
                },
                OnButtonDown = () =>
                {
                    QTEAction();
                    return true;
                }
            };
            RepairButton.TextBlock.AutoScaleHorizontal = true;
            progressBarHolder.RectTransform.MinSize = RepairButton.RectTransform.MinSize;
            RepairButton.RectTransform.MinSize = new Point((int)(RepairButton.TextBlock.TextSize.X * 1.2f), RepairButton.RectTransform.MinSize.Y);

            extraButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform), isHorizontal: true)
            {
                IgnoreLayoutGroups = true,
                Stretch = true,
                AbsoluteSpacing = GUI.IntScale(5)
            };

            sabotageButtonText = TextManager.Get("SabotageButton");
            sabotagingText = TextManager.Get("Sabotaging");
            SabotageButton = new GUIButton(new RectTransform(Vector2.One, extraButtonContainer.RectTransform), sabotageButtonText, style: "GUIButtonSmall")
            {
                IgnoreLayoutGroups = true,
                Visible = false,
                OnClicked = (btn, obj) =>
                {
                    requestStartFixAction = FixActions.Sabotage;
                    item.CreateClientEvent(this);
                    return true;
                },
                OnButtonDown = () =>
                {
                    QTEAction();
                    return true;
                }
            };

            tinkerButtonText = TextManager.Get("TinkerButton").Fallback("Tinker");
            tinkeringText = TextManager.Get("Tinkering").Fallback("Tinkering");
            TinkerButton = new GUIButton(new RectTransform(Vector2.One, extraButtonContainer.RectTransform), tinkerButtonText, style: "GUIButtonSmall")
            {
                IgnoreLayoutGroups = true,
                Visible = false,
                OnClicked = (btn, obj) =>
                {
                    requestStartFixAction = FixActions.Tinker;
                    item.CreateClientEvent(this);
                    return true;
                }
            };

            extraButtonContainer.RectTransform.MinSize = new Point(0, SabotageButton.RectTransform.MinSize.Y);
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (item.HiddenInGame) { return; }
            if (FakeBrokenTimer > 0.0f)
            {
                item.FakeBroken = true;
                if (Character.Controlled == null || (Character.Controlled.CharacterHealth.GetAffliction("psychosis")?.Strength ?? 0.0f) <= 0.0f)
                {
                    FakeBrokenTimer = 0.0f;
                }
                else
                {
                    FakeBrokenTimer -= deltaTime;
                }
            }
            else
            {
                item.FakeBroken = false;
            }


            if (!GameMain.IsMultiplayer)
            {
                switch (requestStartFixAction)
                {
                    case FixActions.Repair:
                    case FixActions.Sabotage:
                    case FixActions.Tinker:
                        StartRepairing(Character.Controlled, requestStartFixAction);
                        requestStartFixAction = FixActions.None;
                        break;
                    default:
                        requestStartFixAction = FixActions.None;
                        break;
                }
            }

            for (int i = 0; i < particleEmitters.Count; i++)
            {
                if ((item.ConditionPercentage >= particleEmitterConditionRanges[i].X && item.ConditionPercentage <= particleEmitterConditionRanges[i].Y) || FakeBrokenTimer > 0.0f)
                {
                    particleEmitters[i].Emit(deltaTime, item.WorldPosition, item.CurrentHull);
                }
            }

            if (CurrentFixer != null && CurrentFixer.SelectedConstruction == item)
            {
                if (repairSoundChannel == null || !repairSoundChannel.IsPlaying)
                {
                    repairSoundChannel = SoundPlayer.PlaySound("repair", item.WorldPosition, hullGuess: item.CurrentHull);
                }

                if (qteCooldown > 0.0f)
                {
                    qteCooldown -= deltaTime;
                    if (qteCooldown <= 0.0f)
                    {
                        qteTimer = QteDuration;
                    }
                }
                else
                {
                    qteTimer -= deltaTime * (qteTimer / QteDuration);
                    if (qteTimer < 0.0f) { qteTimer = QteDuration; }                
                }
            }
            else
            {
                repairSoundChannel?.FadeOutAndDispose();
                repairSoundChannel = null;
            }
        }
        
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;
            
            float defaultMaxCondition = (item.MaxCondition / item.MaxRepairConditionMultiplier);

            progressBar.BarSize = item.Condition / defaultMaxCondition;
            progressBar.Color = ToolBox.GradientLerp(progressBar.BarSize, GUIStyle.Red, GUIStyle.Orange, GUIStyle.Green);

            Rectangle sliderRect = progressBar.GetSliderRect(1.0f);
            Color qteSliderColor = Color.White;
            if (qteCooldown > 0.0f)
            {
                qteSliderColor = qteSuccess ? GUIStyle.Green : GUIStyle.Red * 0.5f;
                progressBar.Color = ToolBox.GradientLerp(qteCooldown / QteCooldownDuration, progressBar.Color, qteSliderColor, Color.White);
            }
            else
            {
                if (qteTimer / QteDuration <= item.Condition / item.MaxCondition)
                {
                    qteSliderColor = Color.Lerp(qteSliderColor, GUIStyle.Green, 0.5f);
                }
            }

            progressBar.Parent.Parent.Parent.DrawManually(spriteBatch, true);
            GUI.DrawRectangle(spriteBatch,
                    new Rectangle(sliderRect.X + (int)((qteTimer / QteDuration) * sliderRect.Width), sliderRect.Y - 5, 2, sliderRect.Height + 10),
                    qteSliderColor, true);

            if (item.Condition > defaultMaxCondition)
            {
                float extraCondition = item.MaxCondition * (item.MaxRepairConditionMultiplier - 1.0f);
                progressBar.Color = ToolBox.GradientLerp((item.Condition - defaultMaxCondition) / extraCondition, GUIStyle.ColorReputationHigh, GUIStyle.ColorReputationVeryHigh);
                progressBarOverlayText.Visible = true;
                progressBarOverlayText.Text = $"{(int)Math.Round((item.Condition / defaultMaxCondition) * 100)}%";
            }
            else
            {
                progressBarOverlayText.Visible = false;
            }

            RepairButton.Enabled = (currentFixerAction == FixActions.None || CurrentFixer == character) && !item.IsFullCondition;
            RepairButton.Text = (currentFixerAction == FixActions.None || CurrentFixer != character || currentFixerAction != FixActions.Repair) ?
                repairButtonText :
                repairingText + new string('.', ((int)(Timing.TotalTime * 2.0f) % 3) + 1);

            SabotageButton.Visible = character.IsTraitor;
            SabotageButton.IgnoreLayoutGroups = !SabotageButton.Visible;
            SabotageButton.Enabled = (currentFixerAction == FixActions.None || (CurrentFixer == character && currentFixerAction != FixActions.Sabotage)) && character.IsTraitor && item.ConditionPercentage > MinSabotageCondition;
            SabotageButton.Text = (currentFixerAction == FixActions.None || CurrentFixer != character || currentFixerAction != FixActions.Sabotage || !character.IsTraitor) ?
                sabotageButtonText :
                sabotagingText + new string('.', ((int)(Timing.TotalTime * 2.0f) % 3) + 1);

            TinkerButton.Visible = IsTinkerable(character);
            TinkerButton.IgnoreLayoutGroups = !TinkerButton.Visible;
            TinkerButton.Enabled = (currentFixerAction == FixActions.None || (CurrentFixer == character && currentFixerAction != FixActions.Tinker)) && CanTinker(character);
            TinkerButton.Text = (currentFixerAction == FixActions.None || CurrentFixer != character || currentFixerAction != FixActions.Tinker) ?
                tinkerButtonText :
                tinkeringText + new string('.', ((int)(Timing.TotalTime * 2.0f) % 3) + 1);

            System.Diagnostics.Debug.Assert(GuiFrame.GetChild(0) is GUILayoutGroup, "Repair UI hierarchy has changed, could not find skill texts");

            extraButtonContainer.Visible = SabotageButton.Visible || TinkerButton.Visible;
            extraButtonContainer.IgnoreLayoutGroups = !extraButtonContainer.Visible;

            foreach (GUIComponent c in GuiFrame.GetChild(0).Children)
            {
                if (!(c.UserData is Skill skill)) continue;

                GUITextBlock textBlock = (GUITextBlock)c;
                if (character.GetSkillLevel(skill.Identifier) < (skill.Level * SkillRequirementMultiplier))
                {
                    textBlock.TextColor = GUIStyle.Red;
                }
                else
                {
                    textBlock.TextColor = Color.White;
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (GameMain.DebugDraw && Character.Controlled?.FocusedItem == item)
            {
                bool paused = !ShouldDeteriorate();
                if (deteriorationTimer > 0.0f)
                {
                    GUI.DrawString(spriteBatch,
                        new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), "Deterioration delay " + ((int)deteriorationTimer) + (paused ? " [PAUSED]" : ""),
                        paused ? Color.Cyan : Color.Lime, Color.Black * 0.5f);
                }
                else
                {
                    GUI.DrawString(spriteBatch,
                        new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), "Deteriorating at " + (int)(DeteriorationSpeed * 60.0f) + " units/min" + (paused ? " [PAUSED]" : ""),
                        paused ? Color.Cyan : GUIStyle.Red, Color.Black * 0.5f);
                }
                GUI.DrawString(spriteBatch,
                    new Vector2(item.DrawPosition.X, -item.DrawPosition.Y + 20), "Condition: " + (int)item.Condition + "/" + (int)item.MaxCondition,
                    GUIStyle.Orange);
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            repairSoundChannel?.FadeOutAndDispose();
            repairSoundChannel = null;
        }

        private void QTEAction()
        {
            if (currentFixerAction == FixActions.Repair)
            {
                float defaultMaxCondition = item.MaxCondition / item.MaxRepairConditionMultiplier;
                qteSuccess = qteCooldown <= 0.0f && qteTimer / QteDuration <= item.Condition / defaultMaxCondition;
            }
            else
            {
                return;
            }

            if (!GameMain.IsMultiplayer) { RepairBoost(qteSuccess); }

            SoundPlayer.PlayUISound(qteSuccess ? GUISoundType.IncreaseQuantity : GUISoundType.DecreaseQuantity);

            //on failure during cooldown reset cursor to beginning
            if (!qteSuccess && qteCooldown > 0.0f) { qteTimer = QteDuration; }
            qteCooldown = QteCooldownDuration;
            //this will be set on button down so we can reset it here
            requestStartFixAction = FixActions.None;
            item.CreateClientEvent(this);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            deteriorationTimer = msg.ReadSingle();
            deteriorateAlwaysResetTimer = msg.ReadSingle();
            DeteriorateAlways = msg.ReadBoolean();
            tinkeringDuration = msg.ReadSingle();
            tinkeringStrength = msg.ReadSingle();
            tinkeringPowersDevices = msg.ReadBoolean();
            ushort currentFixerID = msg.ReadUInt16();
            currentFixerAction = (FixActions)msg.ReadRangedInteger(0, 2);
            CurrentFixer = currentFixerID != 0 ? Entity.FindEntityByID(currentFixerID) as Character : null;
            item.MaxRepairConditionMultiplier = GetMaxRepairConditionMultiplier(CurrentFixer);
            if (CurrentFixer == null)
            {
                qteTimer = QteDuration;
                qteCooldown = 0.0f;
            }
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            msg.WriteRangedInteger((int)requestStartFixAction, 0, 2);
            msg.Write(qteSuccess);
        }
    }
}
