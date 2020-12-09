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
        protected string overlayText, overlayTextBottom;
        protected Color overlayTextColor;
        protected Sprite overlaySprite;

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
                showCampaignUI = value;
            }
        }

        public override void ShowStartMessage()
        {
            if (Mission == null) return;

            new GUIMessageBox(Mission.Name, Mission.Description, new string[0], type: GUIMessageBox.Type.InGame, icon: Mission.Prefab.Icon)
            {
                IconColor = Mission.Prefab.IconColor,
                UserData = "missionstartmessage"
            };
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
        public bool AllowedToManageCampaign()
        {
            //allow ending the round if the client has permissions, is the owner, the only client in the server,
            //or if no-one has management permissions
            if (GameMain.Client == null) { return true; }
            return
                GameMain.Client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Client.ConnectedClients.Count == 1 ||
                GameMain.Client.IsServerOwner ||
                GameMain.Client.ConnectedClients.None(c =>
                    c.InGame && (c.IsOwner || c.HasPermission(ClientPermissions.ManageCampaign)));
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
                if (!string.IsNullOrEmpty(overlayText) && overlayTextColor.A > 0)
                {
                    var backgroundSprite = GUI.Style.GetComponentStyle("CommandBackground").GetDefaultSprite();
                    Vector2 centerPos = new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2;
                    backgroundSprite.Draw(spriteBatch, 
                        centerPos, 
                        Color.White * (overlayTextColor.A / 255.0f), 
                        origin: backgroundSprite.size / 2,
                        rotate: 0.0f,
                        scale: new Vector2(1.5f, 0.7f) * (GameMain.GraphicsWidth / 3 / backgroundSprite.size.X));

                    string wrappedText = ToolBox.WrapText(overlayText, GameMain.GraphicsWidth / 3, GUI.Font);
                    Vector2 textSize = GUI.Font.MeasureString(wrappedText);
                    Vector2 textPos = centerPos - textSize / 2;
                    GUI.DrawString(spriteBatch, textPos + Vector2.One, wrappedText, Color.Black * (overlayTextColor.A / 255.0f));
                    GUI.DrawString(spriteBatch, textPos, wrappedText, overlayTextColor);

                    if (!string.IsNullOrEmpty(overlayTextBottom))
                    {
                        Vector2 bottomTextPos = centerPos + new Vector2(0.0f, textSize.Y + 30 * GUI.Scale) - GUI.Font.MeasureString(overlayTextBottom) / 2;
                        GUI.DrawString(spriteBatch, bottomTextPos + Vector2.One, overlayTextBottom, Color.Black * (overlayTextColor.A / 255.0f));
                        GUI.DrawString(spriteBatch, bottomTextPos, overlayTextBottom, overlayTextColor);
                    }
                }
            }

            if (GUI.DisableHUD || GUI.DisableUpperHUD || ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition"))
            {
                endRoundButton.Visible = false;
                if (ReadyCheckButton != null) { ReadyCheckButton.Visible = false; }
                return; 
            }
            if (Submarine.MainSub == null) { return; }

            endRoundButton.Visible = false;
            var availableTransition = GetAvailableTransition(out _, out Submarine leavingSub);
            string buttonText = "";
            switch (availableTransition)
            {
                case TransitionType.ProgressToNextLocation:
                case TransitionType.ProgressToNextEmptyLocation:
                    if (Level.Loaded.EndOutpost == null || !Level.Loaded.EndOutpost.DockedTo.Contains(leavingSub))
                    {
                        buttonText = TextManager.GetWithVariable("EnterLocation", "[locationname]", Level.Loaded.EndLocation?.Name ?? "[ERROR]");
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
                        buttonText = TextManager.GetWithVariable("EnterLocation", "[locationname]", Level.Loaded.StartLocation?.Name ?? "[ERROR]");
                        endRoundButton.Visible = !ForceMapUI && !ShowCampaignUI;
                    }

                    break;
                case TransitionType.None:
                default:
                    if (Level.Loaded.Type == LevelData.LevelType.Outpost &&
                        (Character.Controlled?.Submarine?.Info.Type == SubmarineType.Player || (Character.Controlled?.CurrentHull?.OutpostModuleTags?.Contains("airlock") ?? false)))
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
                if (!AllowedToEndRound()) { buttonText = TextManager.Get("map"); }
                endRoundButton.Text = ToolBox.LimitString(buttonText, endRoundButton.Font, endRoundButton.Rect.Width - 5);
                if (endRoundButton.Text != buttonText)
                {
                    endRoundButton.ToolTip = buttonText;
                }
                if (Character.Controlled?.ViewTarget is Item item)
                {
                    Turret turret = item.GetComponent<Turret>();
                    endRoundButton.RectTransform.ScreenSpaceOffset = turret == null ? Point.Zero : new Point(0, (int)(turret.UIElementHeight * 1.25f));
                }
                else if (Character.Controlled?.CharacterHealth?.SuicideButton?.Visible ?? false)
                {
                    endRoundButton.RectTransform.ScreenSpaceOffset = new Point(0, Character.Controlled.CharacterHealth.SuicideButton.Rect.Height);
                }
                else
                {
                    endRoundButton.RectTransform.ScreenSpaceOffset = Point.Zero;
                }
            }
            endRoundButton.DrawManually(spriteBatch);
            if (this is MultiPlayerCampaign)
            {
                ReadyCheckButton?.DrawManually(spriteBatch);
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
                    return;
                case InteractionType.Upgrade when !UpgradeManager.CanUpgradeSub():
                    UpgradeManager.CreateUpgradeErrorMessage(TextManager.Get("Dialog.CantUpgrade"), IsSinglePlayer, npc);
                    return;
                case InteractionType.Crew when GameMain.NetworkMember != null:
                    CampaignUI.CrewManagement.SendCrewState(false);
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
                if (CampaignUI?.UpgradeStore?.HoveredItem != null)
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
