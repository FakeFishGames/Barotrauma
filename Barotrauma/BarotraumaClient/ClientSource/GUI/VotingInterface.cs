using System;
using System.Globalization;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class VotingInterface
    {
        public bool VoteRunning = false;

        private GUIFrame frame;
        private GUITextBlock votingTextBlock, votedTextBlock, voteCounter;
        private GUIProgressBar votingTimer;
        private GUIButton yesVoteButton, noVoteButton;
        private Action onVoteEnd;

        private int yesVotes, noVotes, maxVotes;
        private Func<int> getYesVotes, getNoVotes, getMaxVotes;
        private bool votePassed;

        private RichString votingOnText;
        private float votingTime = 100f;
        private float timer;
        private VoteType currentVoteType;
        private Color SubmarineColor => GUIStyle.Orange;
        private Point createdForResolution;

        public static VotingInterface CreateSubmarineVotingInterface(Client starter, SubmarineInfo info, VoteType type, bool transferItems, float votingTime)
        {
            if (starter == null || info == null) { return null; }

            var subVoting = new VotingInterface()
            {
                votingTime = votingTime,
                getYesVotes = () => GameMain.NetworkMember?.Voting?.GetVoteCountYes(type) ?? 0,
                getNoVotes = () => GameMain.NetworkMember?.Voting?.GetVoteCountNo(type) ?? 0,
                getMaxVotes = () => GameMain.NetworkMember?.Voting?.GetVoteCountMax(type) ?? 0,
            };
            subVoting.onVoteEnd = () => subVoting.SendSubmarineVoteEndMessage(info, type);
            subVoting.SetSubmarineVotingText(starter, info, transferItems, type);
            subVoting.Initialize(starter, type);
            return subVoting;
        }

        public static VotingInterface CreateMoneyTransferVotingInterface(Client starter, Client from, Client to, int amount, float votingTime)
        {
            if (starter == null) { return null; }
            if (from == null && to == null) { return null; }

            var transferVoting = new VotingInterface()
            {
                votingTime = votingTime,
                getYesVotes = () => GameMain.NetworkMember?.Voting?.GetVoteCountYes(VoteType.TransferMoney) ?? 0,
                getNoVotes = () => GameMain.NetworkMember?.Voting?.GetVoteCountNo(VoteType.TransferMoney) ?? 0,
                getMaxVotes = () => GameMain.NetworkMember?.Voting?.GetVoteCountMax(VoteType.TransferMoney) ?? 0,
            };
            transferVoting.onVoteEnd = () => transferVoting.SendMoneyTransferVoteEndMessage(from, to, amount);
            transferVoting.SetMoneyTransferVotingText(starter, from, to, amount);
            transferVoting.Initialize(starter, VoteType.TransferMoney);
            return transferVoting;
        }


        private void Initialize(Client starter, VoteType type)
        {
            currentVoteType = type;
            CreateVotingGUI();
            if (starter.SessionId == GameMain.Client.SessionId) { SetGUIToVotedState(2); }
            VoteRunning = true;
        }

        private void CreateVotingGUI()
        {
            createdForResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            frame?.Parent.RemoveChild(frame);
            frame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.VotingArea, GameMain.Client.InGameHUD.RectTransform), style: "");

            int padding = HUDLayoutSettings.Padding * 2;
            int spacing = HUDLayoutSettings.Padding;
            int yOffset = padding;
            int paddedWidth = frame.Rect.Width - padding * 2;

            votingTextBlock = new GUITextBlock(new RectTransform(new Point(paddedWidth, 0), frame.RectTransform), votingOnText, wrap: true);
            votingTextBlock.RectTransform.NonScaledSize = votingTextBlock.RectTransform.MinSize = votingTextBlock.RectTransform.MaxSize = new Point(votingTextBlock.Rect.Width, votingTextBlock.Rect.Height);
            votingTextBlock.RectTransform.IsFixedSize = true;
            votingTextBlock.RectTransform.AbsoluteOffset = new Point(padding, yOffset);

            yOffset += votingTextBlock.Rect.Height + spacing;

            voteCounter = new GUITextBlock(new RectTransform(new Point(paddedWidth, 0), frame.RectTransform), "(0/0)", GUIStyle.Green, textAlignment: Alignment.Center);
            voteCounter.RectTransform.NonScaledSize = voteCounter.RectTransform.MinSize = voteCounter.RectTransform.MaxSize = new Point(voteCounter.Rect.Width, voteCounter.Rect.Height);
            voteCounter.RectTransform.IsFixedSize = true;
            voteCounter.RectTransform.AbsoluteOffset = new Point(padding, yOffset);

            yOffset += voteCounter.Rect.Height + spacing;       

            votingTimer = new GUIProgressBar(new RectTransform(new Point(paddedWidth, Math.Max(spacing, 8)), frame.RectTransform) { AbsoluteOffset = new Point(padding, yOffset) }, HUDLayoutSettings.Padding);
            votingTimer.RectTransform.IsFixedSize = true;
            yOffset += votingTimer.Rect.Height + spacing;

            int buttonWidth = (int)(paddedWidth * 0.3f);
            yesVoteButton = new GUIButton(new RectTransform(new Point(buttonWidth, 0), frame.RectTransform) { AbsoluteOffset = new Point((int)(frame.Rect.Width / 2f - buttonWidth - spacing), yOffset) }, TextManager.Get("yes"))
            {
                OnClicked = (applyButton, obj) =>
                {
                    SetGUIToVotedState(2);
                    GameMain.Client.Vote(currentVoteType, 2);
                    return true;
                }
            };

            noVoteButton = new GUIButton(new RectTransform(new Point(buttonWidth, 0), frame.RectTransform) { AbsoluteOffset = new Point(yesVoteButton.RectTransform.AbsoluteOffset.X + yesVoteButton.Rect.Width + padding, yOffset) }, TextManager.Get("no"))
            {
                OnClicked = (applyButton, obj) =>
                {
                    SetGUIToVotedState(1);
                    GameMain.Client.Vote(currentVoteType, 1);
                    return true;
                }
            };

            votedTextBlock = new GUITextBlock(new RectTransform(new Point(paddedWidth, yesVoteButton.Rect.Height), frame.RectTransform), string.Empty, textAlignment: Alignment.Center);
            votedTextBlock.RectTransform.IsFixedSize = true;
            votedTextBlock.RectTransform.AbsoluteOffset = new Point(padding, yOffset);
            votedTextBlock.Visible = false;

            yOffset += yesVoteButton.Rect.Height;

            frame.RectTransform.NonScaledSize = new Point(frame.Rect.Width, yOffset + padding);
        }

        private void SetGUIToVotedState(int vote)
        {
            yesVoteButton.Visible = noVoteButton.Visible = false;
            votedTextBlock.Text = TextManager.Get(vote == 2 ? "yesvoted" : "novoted");
            votedTextBlock.Visible = true;
        }

        public void Update(float deltaTime)
        {
            if (!VoteRunning) { return; }
            if (GameMain.GraphicsWidth != createdForResolution.X || GameMain.GraphicsHeight != createdForResolution.Y) { CreateVotingGUI(); }
            yesVotes = getYesVotes();
            noVotes = getNoVotes();
            maxVotes = getMaxVotes();
            voteCounter.Text = $"({yesVotes + noVotes}/{maxVotes})";
            timer += deltaTime;
            votingTimer.BarSize = timer / votingTime;
        }

        public void EndVote(bool passed, int yesVoteFinal, int noVoteFinal)
        {
            VoteRunning = false;
            votePassed = passed;
            yesVotes = yesVoteFinal;
            noVotes = noVoteFinal;
            onVoteEnd?.Invoke();
        }

        #region Submarine Voting
        
        private void SetSubmarineVotingText(Client starter, SubmarineInfo info, bool transferItems, VoteType type)
        {
            string name = starter.Name;
            JobPrefab prefab = starter?.Character?.Info?.Job?.Prefab;
            Color nameColor = prefab != null ? prefab.UIColor : Color.White;
            string characterRichString = $"‖color:{nameColor.R},{nameColor.G},{nameColor.B}‖{name}‖color:end‖";
            string submarineRichString = $"‖color:{SubmarineColor.R},{SubmarineColor.G},{SubmarineColor.B}‖{info.DisplayName}‖color:end‖";
            string tag = string.Empty;
            LocalizedString text = string.Empty;
            switch (type)
            {
                case VoteType.PurchaseAndSwitchSub:
                    tag = transferItems ? "submarinepurchaseandswitchwithitemsvote" : "submarinepurchaseandswitchvote";
                    text = TextManager.GetWithVariables(tag,
                        ("[playername]", characterRichString),
                        ("[submarinename]", submarineRichString),
                        ("[amount]", info.Price.ToString()),
                        ("[currencyname]", TextManager.Get("credit").ToLower()));
                    break;
                case VoteType.PurchaseSub:
                    text = TextManager.GetWithVariables("submarinepurchasevote",
                        ("[playername]", characterRichString),
                        ("[submarinename]", submarineRichString),
                        ("[amount]", info.Price.ToString()),
                        ("[currencyname]", TextManager.Get("credit").ToLower()));
                    break;
                case VoteType.SwitchSub:
                    int deliveryFee = SubmarineSelection.DeliveryFeePerDistanceTravelled * GameMain.GameSession.Map.DistanceToClosestLocationWithOutpost(GameMain.GameSession.Map.CurrentLocation, out Location endLocation);
                    if (deliveryFee > 0)
                    {
                        tag = transferItems ? "submarineswitchwithitemsfeevote" : "submarineswitchfeevote";
                        text = TextManager.GetWithVariables(tag,
                            ("[playername]", characterRichString),
                            ("[submarinename]", submarineRichString),
                            ("[locationname]", endLocation.Name),
                            ("[amount]", deliveryFee.ToString()),
                            ("[currencyname]", TextManager.Get("credit").ToLower()));
                    }
                    else
                    {
                        tag = transferItems ? "submarineswitchwithitemsnofeevote" : "submarineswitchnofeevote";
                        text = TextManager.GetWithVariables(tag,
                            ("[playername]", characterRichString),
                            ("[submarinename]", submarineRichString));
                    }
                    break;
            }
            votingOnText = RichString.Rich(text);
        }

        private void SendSubmarineVoteEndMessage(SubmarineInfo info, VoteType type)
        {
            GameMain.NetworkMember.AddChatMessage(GetSubmarineVoteResultMessage(info, type, yesVotes, noVotes, votePassed).Value, ChatMessageType.Server);
        }

        private LocalizedString GetSubmarineVoteResultMessage(SubmarineInfo info, VoteType type, int yesVoteCount, int noVoteCount, bool votePassed)
        {
            LocalizedString result = string.Empty;

            switch (type)
            {
                case VoteType.PurchaseAndSwitchSub:
                    result = TextManager.GetWithVariables(votePassed ? "submarinepurchaseandswitchvotepassed" : "submarinepurchaseandswitchvotefailed",
                        ("[submarinename]", info.DisplayName),
                        ("[amount]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", info.Price)),
                        ("[currencyname]", TextManager.Get("credit").ToLower()),
                        ("[yesvotecount]", yesVoteCount.ToString()),
                        ("[novotecount]" , noVoteCount.ToString()));
                    break;
                case VoteType.PurchaseSub:
                    result = TextManager.GetWithVariables(votePassed ? "submarinepurchasevotepassed" : "submarinepurchasevotefailed",
                        ("[submarinename]", info.DisplayName),
                        ("[amount]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", info.Price)),
                        ("[currencyname]", TextManager.Get("credit").ToLower()),
                        ("[yesvotecount]", yesVoteCount.ToString()),
                        ("[novotecount]", noVoteCount.ToString()));
                    break;
                case VoteType.SwitchSub:
                    int deliveryFee = SubmarineSelection.DeliveryFeePerDistanceTravelled * GameMain.GameSession.Map.DistanceToClosestLocationWithOutpost(GameMain.GameSession.Map.CurrentLocation, out Location endLocation);

                    if (deliveryFee > 0)
                    {
                        result = TextManager.GetWithVariables(votePassed ? "submarineswitchfeevotepassed" : "submarineswitchfeevotefailed",
                            ("[submarinename]", info.DisplayName),
                            ("[locationname]", endLocation.Name),
                            ("[amount]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", deliveryFee)),
                            ("[currencyname]", TextManager.Get("credit").ToLower()),
                            ("[yesvotecount]", yesVoteCount.ToString()),
                            ("[novotecount]", noVoteCount.ToString()));
                    }
                    else
                    {
                        result = TextManager.GetWithVariables(votePassed ? "submarineswitchnofeevotepassed" : "submarineswitchnofeevotefailed",
                            ("[submarinename]", info.DisplayName),
                            ("[yesvotecount]", yesVoteCount.ToString()),
                            ("[novotecount]", noVoteCount.ToString()));
                    }
                    break;
                default:
                    break;
            }
            return result;
        }
        #endregion


        private void SetMoneyTransferVotingText(Client starter, Client from, Client to, int amount)
        {
            string name = starter.Name;
            JobPrefab prefab = starter?.Character?.Info?.Job?.Prefab;
            Color nameColor = prefab != null ? prefab.UIColor : Color.White;
            string characterRichString = $"‖color:{nameColor.R},{nameColor.G},{nameColor.B}‖{name}‖color:end‖";

            LocalizedString text = string.Empty;
            if (from == null && to != null)
            {
                text = TextManager.GetWithVariables("crewwallet.requestbanktoselfvote",
                    ("[requester]", characterRichString),
                    ("[amount]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", amount)));
            }
            else if (from != null && to == null)
            {
                text = TextManager.GetWithVariables("crewwallet.requestselftobankvote",
                    ("[requester]", characterRichString),
                    ("[amount]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", amount)));
            }
            else
            {
                //not supported atm: clients can only requests transfers between their own wallet and the bank
                LocalizedString bankName = TextManager.Get("crewwallet.bank");
                text = TextManager.GetWithVariables("crewwallet.requesttransfervote",
                    ("[requester]", characterRichString),
                    ("[player1]", from?.Character == null ? bankName : from.Character.Name),
                    ("[player2]", to?.Character == null ? bankName : to.Character.Name),
                    ("[amount]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", amount)));
            }

            votingOnText = RichString.Rich(text);
        }
        private void SendMoneyTransferVoteEndMessage(Client from, Client to, int amount)
        {
            GameMain.NetworkMember.AddChatMessage(GetMoneyTransferVoteResultMessage(from, to, amount, yesVotes, noVotes, votePassed).Value, ChatMessageType.Server);
        }

        public static LocalizedString GetMoneyTransferVoteResultMessage(Client from, Client to, int transferAmount, int yesVoteCount, int noVoteCount, bool votePassed)
        {
            LocalizedString result = string.Empty;
            if (from == null && to != null)
            {
                result = TextManager.GetWithVariables(votePassed ? "crewwallet.banktoplayer.votepassed" : "crewwallet.banktoplayer.votefailed",
                    ("[playername]", to.Name),
                    ("[amount]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", transferAmount)),
                    ("[yesvotecount]", yesVoteCount.ToString()),
                    ("[novotecount]", noVoteCount.ToString()));
            }          
            return result;
        }
        public void Remove()
        {
            if (frame != null)
            {
                frame.Parent.RemoveChild(frame);
                frame = null;
            }
        }
    }
}
