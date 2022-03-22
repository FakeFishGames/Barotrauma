using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        protected bool crewDead;

        protected Color overlayColor;
        protected LocalizedString overlayText, overlayTextBottom;
        protected Color overlayTextColor;
        protected Sprite overlaySprite;

        private TransitionType prevCampaignUIAutoOpenType;

        protected GUIButton endRoundButton;

        public GUIButton ReadyCheckButton;
        public GUIButton EndRoundButton => endRoundButton;

        protected GUIFrame campaignUIContainer;
        public CampaignUI CampaignUI;

        public bool ForceMapUI
        {
            get;
            protected set;
        }

        private bool showCampaignUI;
        private bool wasChatBoxOpen;
        public bool ShowCampaignUI
        {
            get { return showCampaignUI; }
            set
            {
                if (value == showCampaignUI) { return; }
                var chatBox = CrewManager?.ChatBox ?? GameMain.Client?.ChatBox;
                if (value)
                {
                    if (chatBox != null)
                    {
                        wasChatBoxOpen = chatBox.ToggleOpen;
                        chatBox.ToggleOpen = false;
                    }
                }
                else if (chatBox != null)
                {
                    chatBox.ToggleOpen = wasChatBoxOpen;
                }
                if (!value && CampaignUI?.SelectedTab == InteractionType.PurchaseSub)
                {
                    SubmarinePreview.Close();
                }
                showCampaignUI = value;
            }
        }

        /// <summary>
        /// Gets the current personal wallet
        /// In singleplayer this is the campaign bank and in multiplayer this is the personal wallet
        /// </summary>
        public virtual Wallet Wallet => GetWallet();

        public override void ShowStartMessage()
        {
            foreach (Mission mission in Missions.ToList())
            {
                new GUIMessageBox(
                    RichString.Rich(mission.Prefab.IsSideObjective ? TextManager.AddPunctuation(':', TextManager.Get("sideobjective"), mission.Name) : mission.Name), 
                    RichString.Rich(mission.Description), Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, icon: mission.Prefab.Icon)
                {
                    IconColor = mission.Prefab.IconColor,
                    UserData = "missionstartmessage"
                };
            }
        }

        /// <summary>
        /// There is a server-side implementation of the method in <see cref="MultiPlayerCampaign"/>
        /// </summary>
        public bool AllowedToEndRound()
        {
            //allow ending the round if the client has permissions, is the owner, the only client in the server
            //or if no-one has management permissions
            if (GameMain.Client == null) { return true; }
            return
                GameMain.Client.HasPermission(ClientPermissions.ManageRound) ||
                GameMain.Client.HasPermission(ClientPermissions.ManageCampaign) || 
                GameMain.Client.ConnectedClients.Count == 1 ||
                GameMain.Client.IsServerOwner ||
                GameMain.Client.ConnectedClients.None(c =>
                    c.InGame && (c.IsOwner || c.HasPermission(ClientPermissions.ManageRound) || c.HasPermission(ClientPermissions.ManageCampaign)));
        }

        /// <summary>
        /// There is a server-side implementation of the method in <see cref="MultiPlayerCampaign"/>
        /// </summary>
        public bool AllowedToManageCampaign(ClientPermissions permissions = ClientPermissions.ManageCampaign)
        {
            //allow managing the round if the client has permissions, is the owner, the only client in the server,
            //or if no-one has management permissions
            if (GameMain.Client == null) { return true; }
            return
                GameMain.Client.HasPermission(permissions) ||
                GameMain.Client.ConnectedClients.Count == 1 ||
                GameMain.Client.IsServerOwner ||
                GameMain.Client.ConnectedClients.None(c => c.InGame && (c.IsOwner || c.HasPermission(permissions)));
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (overlayColor.A > 0)
            {
                if (overlaySprite != null)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * (overlayColor.A / 255.0f), isFilled: true);
                    float scale = Math.Max(GameMain.GraphicsWidth / overlaySprite.size.X, GameMain.GraphicsHeight / overlaySprite.size.Y);
                    overlaySprite.Draw(spriteBatch, new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2, overlayColor, overlaySprite.size / 2, scale: scale);
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), overlayColor, isFilled: true);
                }
                if (!overlayText.IsNullOrEmpty() && overlayTextColor.A > 0)
                {
                    var backgroundSprite = GUIStyle.GetComponentStyle("CommandBackground").GetDefaultSprite();
                    Vector2 centerPos = new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2;
                    LocalizedString wrappedText = ToolBox.WrapText(overlayText, GameMain.GraphicsWidth / 3, GUIStyle.Font);
                    Vector2 textSize = GUIStyle.Font.MeasureString(wrappedText);
                    Vector2 textPos = centerPos - textSize / 2;
                    backgroundSprite.Draw(spriteBatch, 
                        centerPos, 
                        Color.White * (overlayTextColor.A / 255.0f), 
                        origin: backgroundSprite.size / 2,
                        rotate: 0.0f,
                        scale: new Vector2(GameMain.GraphicsWidth / 2 / backgroundSprite.size.X, textSize.Y / backgroundSprite.size.Y * 1.5f));

                    GUI.DrawString(spriteBatch, textPos + Vector2.One, wrappedText, Color.Black * (overlayTextColor.A / 255.0f));
                    GUI.DrawString(spriteBatch, textPos, wrappedText, overlayTextColor);

                    if (!overlayTextBottom.IsNullOrEmpty())
                    {
                        Vector2 bottomTextPos = centerPos + new Vector2(0.0f, textSize.Y / 2 + 40 * GUI.Scale) - GUIStyle.Font.MeasureString(overlayTextBottom) / 2;
                        GUI.DrawString(spriteBatch, bottomTextPos + Vector2.One, overlayTextBottom.Value, Color.Black * (overlayTextColor.A / 255.0f));
                        GUI.DrawString(spriteBatch, bottomTextPos, overlayTextBottom.Value, overlayTextColor);
                    }
                }
            }

            if (GUI.DisableHUD || GUI.DisableUpperHUD || ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition"))
            {
                endRoundButton.Visible = false;
                if (ReadyCheckButton != null) { ReadyCheckButton.Visible = false; }
                return; 
            }
            if (Submarine.MainSub == null || Level.Loaded == null) { return; }

            endRoundButton.Visible = false;
            var availableTransition = GetAvailableTransition(out _, out Submarine leavingSub);
            LocalizedString buttonText = "";
            switch (availableTransition)
            {
                case TransitionType.ProgressToNextLocation:
                case TransitionType.ProgressToNextEmptyLocation:
                    if (Level.Loaded.EndOutpost == null || !Level.Loaded.EndOutpost.DockedTo.Contains(leavingSub))
                    {
                        string textTag = availableTransition == TransitionType.ProgressToNextLocation ? "EnterLocation" : "EnterEmptyLocation";
                        buttonText = TextManager.GetWithVariable(textTag, "[locationname]", Level.Loaded.EndLocation?.Name ?? "[ERROR]");
                        endRoundButton.Visible = !ForceMapUI && !ShowCampaignUI;
                    }
                    break;
                case TransitionType.LeaveLocation:
                    buttonText = TextManager.GetWithVariable("LeaveLocation", "[locationname]", Level.Loaded.StartLocation?.Name ?? "[ERROR]");
                    endRoundButton.Visible = !ForceMapUI && !ShowCampaignUI;
                    break;
                case TransitionType.ReturnToPreviousLocation:
                case TransitionType.ReturnToPreviousEmptyLocation:
                    if (Level.Loaded.StartOutpost == null || !Level.Loaded.StartOutpost.DockedTo.Contains(leavingSub))
                    {
                        string textTag = availableTransition == TransitionType.ReturnToPreviousLocation ? "EnterLocation" : "EnterEmptyLocation";
                        buttonText = TextManager.GetWithVariable(textTag, "[locationname]", Level.Loaded.StartLocation?.Name ?? "[ERROR]");
                        endRoundButton.Visible = !ForceMapUI && !ShowCampaignUI;
                    }

                    break;
                case TransitionType.None:
                default:
                    if (Level.Loaded.Type == LevelData.LevelType.Outpost &&
                        (Character.Controlled?.Submarine?.Info.Type == SubmarineType.Player || (Character.Controlled?.CurrentHull?.OutpostModuleTags.Contains("airlock".ToIdentifier()) ?? false)))
                    {
                        buttonText = TextManager.GetWithVariable("LeaveLocation", "[locationname]", Level.Loaded.StartLocation?.Name ?? "[ERROR]");
                        endRoundButton.Visible = !ForceMapUI && !ShowCampaignUI;
                    }
                    else
                    {
                        endRoundButton.Visible = false;
                    }
                    break;
            }

            if (ReadyCheckButton != null) { ReadyCheckButton.Visible = endRoundButton.Visible; }

            if (endRoundButton.Visible)
            {
                if (!AllowedToEndRound()) 
                { 
                    buttonText = TextManager.Get("map"); 
                }
                else if (prevCampaignUIAutoOpenType != availableTransition && 
                        (availableTransition == TransitionType.ProgressToNextEmptyLocation || availableTransition == TransitionType.ReturnToPreviousEmptyLocation))
                {
                    HintManager.OnAvailableTransition(availableTransition);
                    //opening the campaign map pauses the game and prevents HintManager from running -> update it manually to get the hint to show up immediately
                    HintManager.Update();
                    Map.SelectLocation(-1);
                    endRoundButton.OnClicked(EndRoundButton, null);
                    prevCampaignUIAutoOpenType = availableTransition;
                }
                endRoundButton.Text = ToolBox.LimitString(buttonText.Value, endRoundButton.Font, endRoundButton.Rect.Width - 5);
                if (endRoundButton.Text != buttonText)
                {
                    endRoundButton.ToolTip = buttonText;
                }
                if (Character.Controlled?.CharacterHealth?.SuicideButton?.Visible ?? false)
                {
                    endRoundButton.RectTransform.ScreenSpaceOffset = new Point(0, Character.Controlled.CharacterHealth.SuicideButton.Rect.Height);
                }
                else if (GameMain.Client != null && GameMain.Client.IsFollowSubTickBoxVisible)
                {
                    endRoundButton.RectTransform.ScreenSpaceOffset = new Point(0, HUDLayoutSettings.Padding + GameMain.Client.FollowSubTickBox.Rect.Height);
                }
                else
                {
                    endRoundButton.RectTransform.ScreenSpaceOffset = Point.Zero;
                }
            }
            endRoundButton.DrawManually(spriteBatch);
            if (this is MultiPlayerCampaign && ReadyCheckButton != null)
            {
                ReadyCheckButton.RectTransform.ScreenSpaceOffset = endRoundButton.RectTransform.ScreenSpaceOffset;
                ReadyCheckButton.DrawManually(spriteBatch);
                if (ReadyCheck.ReadyCheckCooldown > DateTime.Now)
                {
                    float progress = (ReadyCheck.ReadyCheckCooldown - DateTime.Now).Seconds / 60.0f;
                    ReadyCheckButton.Color = ToolBox.GradientLerp(progress, Color.White, GUIStyle.Red);
                }
            }
        }

        public Task SelectSummaryScreen(RoundSummary roundSummary, LevelData newLevel, bool mirror, Action action)
        {
            var roundSummaryScreen = RoundSummaryScreen.Select(overlaySprite, roundSummary);

            GUI.ClearCursorWait();

            var loadTask = Task.Run(async () =>
            {
                await Task.Yield();
                Rand.ThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                try
                {
                    GameMain.GameSession.StartRound(newLevel, mirrorLevel: mirror);
                }
                catch (Exception e)
                {
                    roundSummaryScreen.LoadException = e;
                }
                Rand.ThreadId = 0;
            });
            TaskPool.Add("AsyncCampaignStartRound", loadTask, (t) =>
            {
                overlayColor = Color.Transparent;
                action?.Invoke();
            });

            return loadTask;
        }

        partial void NPCInteractProjSpecific(Character npc, Character interactor)
        {
            if (npc == null || interactor == null) { return; }

            switch (npc.CampaignInteractionType)
            {
                case InteractionType.None:
                case InteractionType.Talk:
                case InteractionType.Examine:
                    return;
                case InteractionType.Upgrade when !UpgradeManager.CanUpgradeSub():
                    UpgradeManager.CreateUpgradeErrorMessage(TextManager.Get("Dialog.CantUpgrade").Value, IsSinglePlayer, npc);
                    return;
                case InteractionType.Crew when GameMain.NetworkMember != null:
                    CampaignUI.CrewManagement.SendCrewState(false);
                    goto default;
                case InteractionType.MedicalClinic:
                    CampaignUI.MedicalClinic.RequestLatestPending();
                    goto default;
                default:
                    ShowCampaignUI = true;
                    CampaignUI.SelectTab(npc.CampaignInteractionType);
                    CampaignUI.UpgradeStore?.RefreshAll();
                    break;
            }
        }

        public override void AddToGUIUpdateList()
        {
            if (ShowCampaignUI || ForceMapUI)
            {
                campaignUIContainer?.AddToGUIUpdateList();
                if (CampaignUI?.UpgradeStore?.HoveredEntity != null)
                {
                    if (CampaignUI.SelectedTab != InteractionType.Upgrade) { return; }
                    CampaignUI?.UpgradeStore?.ItemInfoFrame.AddToGUIUpdateList(order: 1);
                }
            }
            base.AddToGUIUpdateList();
            CrewManager.AddToGUIUpdateList();
            endRoundButton.AddToGUIUpdateList();
            ReadyCheckButton?.AddToGUIUpdateList();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            MedicalClinic?.Update(deltaTime);

            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData is RoundSummary);
            }

            if (ShowCampaignUI || ForceMapUI)
            {
                CampaignUI?.Update(deltaTime);
            }
        }
    }
}
