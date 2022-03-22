using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
            if (tabMenu == null && !(GameMode is TutorialMode) && !ConversationAction.IsDialogOpen)
            {
                tabMenu = new TabMenu();
                HintManager.OnShowTabMenu();
            }
            else
            {
                tabMenu = null;
                NetLobbyScreen.JobInfoFrame = null;
            }
            return true;
        }

        private GUILayoutGroup topLeftButtonGroup;
        private GUIButton crewListButton, commandButton, tabMenuButton;
        private GUIImage talentPointNotification;

        private GUIComponent respawnInfoFrame, respawnButtonContainer;
        private GUITextBlock respawnInfoText;
        private GUITickBox respawnTickBox;

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
            topLeftButtonGroup.RectTransform.ParentChanged += (_) =>
            {
                GameMain.Instance.ResolutionChanged -= CreateTopLeftButtons;
            };
            int buttonHeight = GUI.IntScale(40);
            Vector2 buttonSpriteSize = GUI.Style.GetComponentStyle("CrewListToggleButton").GetDefaultSprite().size;
            int buttonWidth = (int)((buttonHeight / buttonSpriteSize.Y) * buttonSpriteSize.X);
            Point buttonSize = new Point(buttonWidth, buttonHeight);
            crewListButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "CrewListToggleButton")
            {
                ToolTip = TextManager.GetWithVariable("hudbutton.crewlist", "[key]", GameMain.Config.KeyBindText(InputType.CrewOrders)),
                OnClicked = (GUIButton btn, object userdata) =>
                {
                    if (CrewManager == null) { return false; }
                    CrewManager.IsCrewMenuOpen = !CrewManager.IsCrewMenuOpen;
                    return true;
                }
            };
            commandButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "CommandButton")
            {
                ToolTip = TextManager.GetWithVariable("hudbutton.commandinterface", "[key]", GameMain.Config.KeyBindText(InputType.Command)),
                OnClicked = (button, userData) =>
                {
                    if (CrewManager == null) { return false; }
                    CrewManager.ToggleCommandUI();
                    return true;
                }
            };
            tabMenuButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "TabMenuButton")
            {
                ToolTip = TextManager.GetWithVariable("hudbutton.tabmenu", "[key]", GameMain.Config.KeyBindText(InputType.InfoTab)),
                OnClicked = (button, userData) => ToggleTabMenu()
            };

            talentPointNotification = CreateTalentIconNotification(tabMenuButton);

            GameMain.Instance.ResolutionChanged += CreateTopLeftButtons;

            respawnInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 1.0f), parent: topLeftButtonGroup.RectTransform)
            { MaxSize = new Point(HUDLayoutSettings.ButtonAreaTop.Width / 3, int.MaxValue) }, style: null)
            {
                Visible = false
            };
            respawnInfoText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), respawnInfoFrame.RectTransform), "", wrap: true);
            respawnButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), respawnInfoFrame.RectTransform, Anchor.CenterRight), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                AbsoluteSpacing = HUDLayoutSettings.Padding,
                Stretch = true,
                Visible = false
            };
            respawnTickBox = new GUITickBox(new RectTransform(Vector2.One * 0.9f, respawnButtonContainer.RectTransform, Anchor.Center), TextManager.Get("respawnquestionpromptrespawn"))
            {
                ToolTip = TextManager.Get("respawnquestionprompt"),
                OnSelected = (tickbox) =>
                {
                    GameMain.Client?.SendRespawnPromptResponse(waitForNextRoundRespawn: !tickbox.Selected);
                    return true;
                }
            };
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) { return; }
            GameMode?.AddToGUIUpdateList();
            tabMenu?.AddToGUIUpdateList();

            if ((!(GameMode is CampaignMode campaign) || (!campaign.ForceMapUI && !campaign.ShowCampaignUI)) &&
                !CoroutineManager.IsCoroutineRunning("LevelTransition") && !CoroutineManager.IsCoroutineRunning("SubmarineTransition"))
            {
                if (topLeftButtonGroup == null)
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
        }

        public static GUIImage CreateTalentIconNotification(GUIComponent parent, bool offset = true)
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

        public static void UpdateTalentNotificationIndicator(GUIImage indicator)
        {
            if (indicator != null)
            {
                if (Character.Controlled?.Info == null)
                {
                    indicator.Visible = false;
                }
                else
                {
                    indicator.Visible = Character.Controlled.Info.GetAvailableTalentPoints() > 0 && !Character.Controlled.HasUnlockedAllTalents();
                }
            }
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
                tabMenu.Update();
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
        }

        public void SetRespawnInfo(bool visible, string text, Color textColor, bool buttonsVisible, bool waitForNextRoundRespawn)
        {
            if (topLeftButtonGroup == null) { return; }
            respawnInfoFrame.Visible = visible;
            if (!visible) { return; }
            respawnInfoText.Text = text;
            respawnInfoText.TextColor = textColor;
            respawnButtonContainer.Visible = buttonsVisible;
            respawnTickBox.Selected = !waitForNextRoundRespawn;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            GameMode?.Draw(spriteBatch);
        }
    }
}
