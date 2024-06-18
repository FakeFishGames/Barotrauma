using Barotrauma.Abilities;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class GameSession
    {
        public RoundSummary RoundSummary
        {
            get;
            private set;
        }

        public static bool IsTabMenuOpen => GameMain.GameSession?.tabMenu != null;
        public static TabMenu TabMenuInstance => GameMain.GameSession?.tabMenu;

        private TabMenu tabMenu;

        public bool ToggleTabMenu()
        {
            if (GameMain.NetworkMember != null && GameMain.NetLobbyScreen != null)
            {
                GameMain.NetLobbyScreen.CharacterAppearanceCustomizationMenu?.Dispose();
                GameMain.NetLobbyScreen.CharacterAppearanceCustomizationMenu = null;
                if (GameMain.NetLobbyScreen.JobSelectionFrame != null) { GameMain.NetLobbyScreen.JobSelectionFrame.Visible = false; }
            }
            if (tabMenu == null && GameMode is not TutorialMode && !ConversationAction.IsDialogOpen)
            {
                tabMenu = new TabMenu();
                HintManager.OnShowTabMenu();
            }
            else
            {
                tabMenu?.OnClose();
                tabMenu = null;
                NetLobbyScreen.JobInfoFrame = null;
            }
            return true;
        }

        private GUILayoutGroup topLeftButtonGroup;
        private GUIButton crewListButton, commandButton, tabMenuButton;
        private GUIImage talentPointNotification;

        private GUIComponent deathChoiceInfoFrame, deathChoiceButtonContainer;
        private GUITextBlock respawnInfoText;
        private GUITickBox deathChoiceTickBox;
        private GUIButton takeOverBotButton;
        private GUIButton hrManagerButton;
        public DeathPrompt DeathPrompt;

        private GUIImage eventLogNotification;

        private Point prevTopLeftButtonsResolution;
        
        public bool AllowHrManagerBotTakeover => GameMain.NetworkMember?.ServerSettings is { RespawnMode: RespawnMode.Permadeath, IronmanMode: false }
                                                 && Level.IsLoadedFriendlyOutpost;

        private void CreateTopLeftButtons()
        {
            if (topLeftButtonGroup != null)
            {
                topLeftButtonGroup.RectTransform.Parent = null;
                topLeftButtonGroup = null;
                crewListButton = commandButton = tabMenuButton = null;
            }
            topLeftButtonGroup = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, GUI.Canvas), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                AbsoluteSpacing = HUDLayoutSettings.Padding,
                CanBeFocused = false
            };
            int buttonHeight = GUI.IntScale(40);
            Vector2 buttonSpriteSize = GUIStyle.GetComponentStyle("CrewListToggleButton").GetDefaultSprite().size;
            int buttonWidth = (int)((buttonHeight / buttonSpriteSize.Y) * buttonSpriteSize.X);
            Point buttonSize = new Point(buttonWidth, buttonHeight);
            crewListButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "CrewListToggleButton")
            {
                ToolTip = TextManager.GetWithVariable("hudbutton.crewlist", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.CrewOrders)),
                OnClicked = (GUIButton btn, object userdata) =>
                {
                    if (CrewManager == null) { return false; }
                    CrewManager.IsCrewMenuOpen = !CrewManager.IsCrewMenuOpen;
                    return true;
                }
            };
            commandButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "CommandButton")
            {
                ToolTip = TextManager.GetWithVariable("hudbutton.commandinterface", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Command)),
                OnClicked = (button, userData) =>
                {
                    if (CrewManager == null) { return false; }
                    CrewManager.ToggleCommandUI();
                    return true;
                }
            };
            tabMenuButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "TabMenuButton")
            {
                ToolTip = TextManager.GetWithVariable("hudbutton.tabmenu", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.InfoTab)),
                OnClicked = (button, userData) => ToggleTabMenu()
            };

            talentPointNotification = CreateNotificationIcon(tabMenuButton);
            eventLogNotification = CreateNotificationIcon(tabMenuButton);
            
            // The visibility of the following contents of deathChoiceInfoFrame is controlled by SetRespawnInfo()
            
            deathChoiceInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 1.0f), parent: topLeftButtonGroup.RectTransform)
                { MaxSize = new Point(HUDLayoutSettings.ButtonAreaTop.Width / 3, int.MaxValue) }, style: null)
            {
                Visible = false
            };
            respawnInfoText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), deathChoiceInfoFrame.RectTransform), "", wrap: true);
            deathChoiceButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), deathChoiceInfoFrame.RectTransform, Anchor.CenterRight), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                AbsoluteSpacing = HUDLayoutSettings.Padding,
                Stretch = true,
                Visible = false
            };

            takeOverBotButton = new GUIButton(new RectTransform(Vector2.One * 0.9f, deathChoiceButtonContainer.RectTransform, Anchor.Center),
                TextManager.Get("takeoverbotquestionprompttakeoverbot"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    DeathPrompt.CreateTakeOverBotPanel();
                    return true;
                }
            };
            takeOverBotButton.TextBlock.AutoScaleHorizontal = true;

            hrManagerButton = new GUIButton(new RectTransform(Vector2.One * 0.9f, deathChoiceButtonContainer.RectTransform, Anchor.Center), 
                TextManager.Get("npctitle.hrmanager"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    if (GameMain.GameSession?.Campaign is { } campaign)
                    {
                        campaign.ShowCampaignUI = true;
                        campaign.CampaignUI?.SelectTab(CampaignMode.InteractionType.Crew);
                    }
                    return true;
                }
            };
            hrManagerButton.TextBlock.AutoScaleHorizontal = true;

            var questionText = 
                TextManager.GetWithVariable(
                    "respawnquestionprompt", "[percentage]",
                    ((int)Math.Round(RespawnManager.SkillLossPercentageOnImmediateRespawn)).ToString());
            deathChoiceTickBox = new GUITickBox(new RectTransform(Vector2.One * 0.9f, deathChoiceButtonContainer.RectTransform, Anchor.Center),
                TextManager.Get("respawnquestionpromptrespawn"))
            {
                ToolTip = questionText,
                OnSelected = (tickbox) =>
                {
                    GameMain.Client?.SendRespawnPromptResponse(waitForNextRoundRespawn: !tickbox.Selected);                        
                    return true;
                }
            };

            prevTopLeftButtonsResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) { return; }
            GameMode?.AddToGUIUpdateList();
            tabMenu?.AddToGUIUpdateList();
            ObjectiveManager.AddToGUIUpdateList();

            if ((GameMode is not CampaignMode campaign || (!campaign.ForceMapUI && !campaign.ShowCampaignUI)) &&
                !CoroutineManager.IsCoroutineRunning("LevelTransition") && !CoroutineManager.IsCoroutineRunning("SubmarineTransition"))
            {
                if (topLeftButtonGroup == null ||
                    prevTopLeftButtonsResolution.X != GameMain.GraphicsWidth || prevTopLeftButtonsResolution.Y != GameMain.GraphicsHeight)
                {
                    CreateTopLeftButtons();
                }
                crewListButton.Selected = CrewManager != null && CrewManager.IsCrewMenuOpen;
                commandButton.Selected = CrewManager.IsCommandInterfaceOpen;
                commandButton.Enabled = CrewManager.CanIssueOrders;
                tabMenuButton.Selected = IsTabMenuOpen;
                topLeftButtonGroup.AddToGUIUpdateList();
            }

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetLobbyScreen.CharacterAppearanceCustomizationMenu?.AddToGUIUpdateList();
                GameMain.NetLobbyScreen?.JobSelectionFrame?.AddToGUIUpdateList();
            }

            DeathPrompt?.AddToGUIUpdateList();
        }

        public static GUIImage CreateNotificationIcon(GUIComponent parent, bool offset = true)
        {
            GUIImage indicator = new GUIImage(new RectTransform(new Vector2(0.45f), parent.RectTransform, anchor: Anchor.TopRight, scaleBasis: ScaleBasis.BothWidth), style: "TalentPointNotification")
            {
                Visible = false,
                CanBeFocused = false
            };
            Point notificationSize = indicator.RectTransform.NonScaledSize;
            if (offset)
            {
                indicator.RectTransform.AbsoluteOffset = new Point(-(notificationSize.X / 2), -(notificationSize.Y / 2));
            }
            return indicator;
        }

        public void EnableEventLogNotificationIcon(bool enabled)
        {
            if (eventLogNotification == null) { return; }
            if (!eventLogNotification.Visible && enabled)
            {
                eventLogNotification.Pulsate(Vector2.One, Vector2.One * 2, 1.0f);
            }
            eventLogNotification.Visible = enabled;
        }

        public static void UpdateTalentNotificationIndicator(GUIImage indicator)
        {
            if (indicator == null) { return; }
            indicator.Visible =
                Character.Controlled?.Info != null &&
                Character.Controlled.Info.GetAvailableTalentPoints() > 0 && !Character.Controlled.HasUnlockedAllTalents();
        }

        public void HUDScaleChanged()
        {
            CreateTopLeftButtons();
            GameMode?.HUDScaleChanged();
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (GUI.DisableHUD) { return; }

            if (tabMenu == null)
            {
                if (PlayerInput.KeyHit(InputType.InfoTab) && !(GUI.KeyboardDispatcher.Subscriber is GUITextBox))
                {
                    ToggleTabMenu();
                }
            }
            else
            {
                tabMenu.Update(deltaTime);
                if ((PlayerInput.KeyHit(InputType.InfoTab) || PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape)) &&
                    !(GUI.KeyboardDispatcher.Subscriber is GUITextBox))
                {
                    ToggleTabMenu();
                }
            }

            UpdateTalentNotificationIndicator(talentPointNotification);

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetLobbyScreen?.CharacterAppearanceCustomizationMenu?.Update();
                if (GameMain.NetLobbyScreen?.JobSelectionFrame != null)
                {
                    if (GameMain.NetLobbyScreen.JobSelectionFrame != null && PlayerInput.PrimaryMouseButtonDown() && !GUI.IsMouseOn(GameMain.NetLobbyScreen.JobSelectionFrame))
                    {
                        GameMain.NetLobbyScreen.JobList.Deselect();
                        GameMain.NetLobbyScreen.JobSelectionFrame.Visible = false;
                    }
                }
            }

            HintManager.Update();
            ObjectiveManager.VideoPlayer.Update();
        }
        
        /// <summary>
        /// This method controls the content and visibility logic of the respawn-related GUI elements at the top left of the game screen.
        /// </summary>
        /// <param name="waitForNextRoundRespawn">Has the player chosen to wait until next round</param>
        /// <param name="hideButtons">Hide the respawn buttons even if they would otherwise be visible</param>
        public void SetRespawnInfo(string text, Color textColor, bool waitForNextRoundRespawn, bool hideButtons = false)
        {
            if (topLeftButtonGroup == null) { return; }
            
            bool permadeathMode = GameMain.NetworkMember?.ServerSettings is { RespawnMode: RespawnMode.Permadeath };
            bool ironmanMode = GameMain.NetworkMember is { ServerSettings: { RespawnMode: RespawnMode.Permadeath, IronmanMode: true } };
            
            bool hasRespawnOptions;
            if (permadeathMode)
            {
                // In permadeath mode you can (in ironman, must) always at least wait, and possibly buy a new character from HR or take control of a bot
                hasRespawnOptions = !ironmanMode &&
                                    GameMain.Client is GameClient client && (client.CharacterInfo == null || client.CharacterInfo.PermanentlyDead);
            }
            else // "classic" respawn modes
            {
                //can choose between midround respawning with a penalty or waiting
                //if we're in a non-outpost level, and either don't have an existing character or have already spawned during the round
                //(otherwise, e.g. when joining a campaign in which we have an existing character, we can respawn mid-round "for free" and there's no reason to make a choice)
                hasRespawnOptions = Level.Loaded?.Type != LevelData.LevelType.Outpost &&
                                    (GameMain.Client is GameClient client && (client.CharacterInfo == null || client.HasSpawned));
            }
            
            // Are the death choice elements shown at all, at least with the text? 
            deathChoiceInfoFrame.Visible = !text.IsNullOrEmpty() || hasRespawnOptions;
            if (!deathChoiceInfoFrame.Visible) { return; }
            respawnInfoText.Text = text;
            respawnInfoText.TextColor = textColor;
            
            // Determine if we even bother considering showing the buttons 
            if (GameMain.GameSession.GameMode is not CampaignMode || Character.Controlled != null)
            {
                // Disable the button container in case it was left visible earlier
                deathChoiceButtonContainer.Visible = false;
                return;
            }

            deathChoiceButtonContainer.Visible = hasRespawnOptions && !hideButtons;
            if (deathChoiceButtonContainer.Visible)
            {
                hrManagerButton.Visible = AllowHrManagerBotTakeover;
                
                if (permadeathMode && ironmanMode)
                {
                    takeOverBotButton.Visible = false;
                    deathChoiceTickBox.Visible = false;
                    deathChoiceTickBox.Selected = false;
                }
                else
                {
                    takeOverBotButton.Visible = permadeathMode && GameMain.NetworkMember?.ServerSettings is { AllowBotTakeoverOnPermadeath: true };
                    deathChoiceTickBox.Visible = !permadeathMode;
                    deathChoiceTickBox.Selected = !waitForNextRoundRespawn;    
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            GameMode?.Draw(spriteBatch);
        }
    }
}
