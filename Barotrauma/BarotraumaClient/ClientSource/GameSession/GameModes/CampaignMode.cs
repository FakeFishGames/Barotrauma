﻿using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        protected bool crewDead;

        protected Color overlayColor;
        protected Sprite overlaySprite;

        private TransitionType prevCampaignUIAutoOpenType;

        protected GUIButton endRoundButton;

        public GUIButton ReadyCheckButton;
        public GUIButton EndRoundButton => endRoundButton;

        protected GUIFrame campaignUIContainer;
        public CampaignUI CampaignUI;

        public SlideshowPlayer SlideshowPlayer
        {
            get;
            protected set;
        }

        private CancellationTokenSource startRoundCancellationToken;

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
                if (!value)
                {
                    switch (CampaignUI?.SelectedTab)
                    {
                        case InteractionType.PurchaseSub:
                            SubmarinePreview.Close();
                            break;
                        case InteractionType.MedicalClinic:
                            CampaignUI.MedicalClinic?.OnDeselected();
                            break;
                    }
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
                if (!mission.Prefab.ShowStartMessage) { continue; }
                new GUIMessageBox(
                    RichString.Rich(mission.Prefab.IsSideObjective ? TextManager.AddPunctuation(':', TextManager.Get("sideobjective"), mission.Name) : mission.Name), 
                    RichString.Rich(mission.Description), Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, icon: mission.Prefab.Icon)
                {
                    IconColor = mission.Prefab.IconColor,
                    UserData = "missionstartmessage"
                };
            }
        }

        private static bool IsOwner(Client client) => client != null && client.IsOwner;

        /// <summary>
        /// There is a server-side implementation of the method in <see cref="MultiPlayerCampaign"/>
        /// </summary>
        public static bool AllowedToManageCampaign(ClientPermissions permissions)
        {
            //allow managing the round if the client has permissions, is the owner, the only client in the server,
            //or if no-one has management permissions
            if (GameMain.Client == null) { return true; }
            return
                GameMain.Client.HasPermission(permissions) ||
                GameMain.Client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Client.IsServerOwner ||
                AnyOneAllowedToManageCampaign(permissions);
        }

        public static bool AllowedToManageWallets()
        {
            return AllowedToManageCampaign(ClientPermissions.ManageMoney);
        }
        protected GUIButton CreateEndRoundButton()
        {
            int buttonWidth = (int)(450 * GUI.xScale * (GUI.IsUltrawide ? 3.0f : 1.0f));
            int buttonHeight = (int)(40 * GUI.yScale);
            var rectT = HUDLayoutSettings.ToRectTransform(new Rectangle((GameMain.GraphicsWidth / 2), HUDLayoutSettings.ButtonAreaTop.Center.Y, buttonWidth, buttonHeight), GUI.Canvas);
            rectT.Pivot = Pivot.Center;
            return new GUIButton(rectT, TextManager.Get("EndRound"), textAlignment: Alignment.Center, style: "EndRoundButton")
            {
                Pulse = true,
                TextBlock =
                {
                    Shadow = true,
                    AutoScaleHorizontal = true
                }
            };
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
            }

            SlideshowPlayer?.DrawManually(spriteBatch);

            if (GUI.DisableHUD || GUI.DisableUpperHUD || ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition"))
            {
                endRoundButton.Visible = false;
                if (ReadyCheckButton != null) { ReadyCheckButton.Visible = false; }
                return; 
            }
            if (Submarine.MainSub == null || Level.Loaded == null) { return; }

            bool allowEndingRound = false;
            endRoundButton.Color = endRoundButton.Style.Color;
            endRoundButton.HoverColor = endRoundButton.Style.HoverColor;
            RichString overrideEndRoundButtonToolTip = string.Empty;
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
                        allowEndingRound = !ForceMapUI && !ShowCampaignUI;
                    }
                    break;
                case TransitionType.LeaveLocation:
                    buttonText = TextManager.GetWithVariable("LeaveLocation", "[locationname]", Level.Loaded.StartLocation?.Name ?? "[ERROR]");
                    allowEndingRound = !ForceMapUI && !ShowCampaignUI;
                    break;
                case TransitionType.ReturnToPreviousLocation:
                case TransitionType.ReturnToPreviousEmptyLocation:
                    if (Level.Loaded.StartOutpost == null || !Level.Loaded.StartOutpost.DockedTo.Contains(leavingSub))
                    {
                        string textTag = availableTransition == TransitionType.ReturnToPreviousLocation ? "EnterLocation" : "EnterEmptyLocation";
                        buttonText = TextManager.GetWithVariable(textTag, "[locationname]", Level.Loaded.StartLocation?.Name ?? "[ERROR]");
                        allowEndingRound = !ForceMapUI && !ShowCampaignUI;
                    }
                    break;
                case TransitionType.None:
                default:
                    bool inFriendlySub = Character.Controlled is { IsInFriendlySub: true };
                    if (Level.Loaded.Type == LevelData.LevelType.Outpost && !Level.Loaded.IsEndBiome &&
                        (inFriendlySub || (Character.Controlled?.CurrentHull?.OutpostModuleTags.Contains("airlock".ToIdentifier()) ?? false)))
                    {
                        if (Missions.Any(m => m is SalvageMission salvageMission && salvageMission.AnyTargetNeedsToBeRetrievedToSub))
                        {
                            overrideEndRoundButtonToolTip = TextManager.Get("SalvageTargetNotInSub");
                            endRoundButton.Color = GUIStyle.Red * 0.7f;
                            endRoundButton.HoverColor = GUIStyle.Red;
                        }
                        buttonText = TextManager.GetWithVariable("LeaveLocation", "[locationname]", Level.Loaded.StartLocation?.Name ?? "[ERROR]");
                        allowEndingRound = !ForceMapUI && !ShowCampaignUI;
                    }
                    else
                    {
                        allowEndingRound = false;
                    }
                    break;
            }
            if (Level.IsLoadedOutpost && !ObjectiveManager.AllActiveObjectivesCompleted())
            {
                allowEndingRound = false;
            }
            if (ReadyCheckButton != null) { ReadyCheckButton.Visible = allowEndingRound; }

            endRoundButton.Visible = allowEndingRound && Character.Controlled is { IsIncapacitated: false };
            if (endRoundButton.Visible)
            {
                if (!AllowedToManageCampaign(ClientPermissions.ManageMap)) 
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
                if (overrideEndRoundButtonToolTip != string.Empty)
                {
                    endRoundButton.ToolTip = overrideEndRoundButtonToolTip;
                }
                else if (endRoundButton.Text != buttonText)
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

            startRoundCancellationToken = new CancellationTokenSource();
            var loadTask = Task.Run(async () =>
            {
                await Task.Yield();
                Rand.ThreadId = Environment.CurrentManagedThreadId;
                try
                {
                    GameMain.GameSession.StartRound(newLevel, mirrorLevel: mirror, startOutpost: GetPredefinedStartOutpost());
                }
                catch (Exception e)
                {
                    roundSummaryScreen.LoadException = e;
                }
                Rand.ThreadId = 0;
                startRoundCancellationToken = null;
            }, startRoundCancellationToken.Token);
            TaskPool.Add("AsyncCampaignStartRound", loadTask, (t) =>
            {
                overlayColor = Color.Transparent;
                action?.Invoke();
            });

            return loadTask;
        }

        public void CancelStartRound()
        {
            startRoundCancellationToken?.Cancel();
        }

        public void ThrowIfStartRoundCancellationRequested()
        {
            if (startRoundCancellationToken != null && 
                startRoundCancellationToken.Token.IsCancellationRequested)
            {
                startRoundCancellationToken.Token.ThrowIfCancellationRequested();
                startRoundCancellationToken = null;
            }
        }

        protected SubmarineInfo GetPredefinedStartOutpost()
        {
            if (Map?.CurrentLocation?.Type?.GetForcedOutpostGenerationParams() is OutpostGenerationParams parameters && !parameters.OutpostFilePath.IsNullOrEmpty())
            {
                return new SubmarineInfo(parameters.OutpostFilePath.Value)
                {
                    OutpostGenerationParams = parameters
                };
            }
            return null;
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
                    CampaignUI.SelectTab(npc.CampaignInteractionType, npc);
                    CampaignUI.UpgradeStore?.RequestRefresh();
                    break;
            }

            if (npc.AIController is HumanAIController humanAi && humanAi.IsInHostileFaction())
            {
                npc.Speak(TextManager.Get("dialoglowrepcampaigninteraction").Value, identifier: "dialoglowrepcampaigninteraction".ToIdentifier(), minDurationBetweenSimilar: 60.0f);
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

        protected void TryEndRoundWithFuelCheck(Action onConfirm, Action onReturnToMapScreen)
        {
            Submarine.MainSub.CheckFuel();
            SubmarineInfo nextSub = PendingSubmarineSwitch ?? Submarine.MainSub.Info;
            bool lowFuel = nextSub.Name == Submarine.MainSub.Info.Name ? Submarine.MainSub.Info.LowFuel : nextSub.LowFuel;
            if (Level.IsLoadedFriendlyOutpost && lowFuel && CargoManager.PurchasedItems.None(i => i.Value.Any(pi => pi.ItemPrefab.Tags.Contains("reactorfuel"))))
            {
                var extraConfirmationBox =
                    new GUIMessageBox(TextManager.Get("lowfuelheader"),
                    TextManager.Get("lowfuelwarning"),
                    new LocalizedString[2] { TextManager.Get("ok"), TextManager.Get("cancel") });
                extraConfirmationBox.Buttons[0].OnClicked = (b, o) => { Confirm(); return true; };
                extraConfirmationBox.Buttons[0].OnClicked += extraConfirmationBox.Close;
                extraConfirmationBox.Buttons[1].OnClicked = extraConfirmationBox.Close;
            }
            else
            {
                Confirm();
            }

            void Confirm()
            {
                var availableTransition = GetAvailableTransition(out _, out _);
                if (Character.Controlled != null &&
                    availableTransition == TransitionType.ReturnToPreviousLocation &&
                    Character.Controlled?.Submarine == Level.Loaded?.StartOutpost)
                {
                    onConfirm();
                }
                else if (Character.Controlled != null &&
                    availableTransition == TransitionType.ProgressToNextLocation &&
                    Character.Controlled?.Submarine == Level.Loaded?.EndOutpost)
                {
                    onConfirm();
                }
                else
                {
                    onReturnToMapScreen();
                }
            }
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
