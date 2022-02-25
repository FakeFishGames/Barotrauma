using System;
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
        private Color submarineColor => GUIStyle.Orange;
        private Point createdForResolution;

        public VotingInterface(Client starter, SubmarineInfo info, VoteType type, float votingTime)
        {
            if (starter == null || info == null) return;
            SetSubmarineVotingText(starter, info, type);
            this.votingTime = votingTime;
            getYesVotes = SubmarineYesVotes;
            getNoVotes = SubmarineNoVotes;
            getMaxVotes = SubmarineMaxVotes;
            onVoteEnd = () => SendSubmarineVoteEndMessage(info, type);

            Initialize(starter, type);
        }

        private void Initialize(Client starter, VoteType type)
        {
            currentVoteType = type;
            CreateVotingGUI();
            if (starter.ID == GameMain.Client.ID) SetGUIToVotedState(2);
            VoteRunning = true;
        }

        private void CreateVotingGUI()
        {
            createdForResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            if (frame != null) frame.Parent.RemoveChild(frame);
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
            if (!VoteRunning) return;
            if (GameMain.GraphicsWidth != createdForResolution.X || GameMain.GraphicsHeight != createdForResolution.Y) CreateVotingGUI();
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
        private void SetSubmarineVotingText(Client starter, SubmarineInfo info, VoteType type)
        {
            string name = starter.Name;
            JobPrefab prefab = starter?.Character?.Info?.Job?.Prefab;
            Color nameColor = prefab != null ? prefab.UIColor : Color.White;
            string characterRichString = $"‖color:{nameColor.R},{nameColor.G},{nameColor.B}‖{name}‖color:end‖";
            string submarineRichString = $"‖color:{submarineColor.R},{submarineColor.G},{submarineColor.B}‖{info.DisplayName}‖color:end‖";

            switch (type)
            {
                case VoteType.PurchaseAndSwitchSub:
                    votingOnText = TextManager.GetWithVariables("submarinepurchaseandswitchvote",
                        ("[playername]", characterRichString),
                        ("[submarinename]", submarineRichString),
                        ("[amount]", info.Price.ToString()),
                        ("[currencyname]", TextManager.Get("credit").ToLower()));
                    break;
                case VoteType.PurchaseSub:
                    votingOnText = TextManager.GetWithVariables("submarinepurchasevote",
                        ("[playername]", characterRichString),
                        ("[submarinename]", submarineRichString),
                        ("[amount]", info.Price.ToString()),
                        ("[currencyname]", TextManager.Get("credit").ToLower()));
                    break;
                case VoteType.SwitchSub:
                    int deliveryFee = SubmarineSelection.DeliveryFeePerDistanceTravelled * GameMain.GameSession.Map.DistanceToClosestLocationWithOutpost(GameMain.GameSession.Map.CurrentLocation, out Location endLocation);

                    if (deliveryFee > 0)
                    {
                        votingOnText = TextManager.GetWithVariables("submarineswitchfeevote",
                            ("[playername]", characterRichString),
                            ("[submarinename]", submarineRichString),
                            ("[locationname]", endLocation.Name),
                            ("[amount]", deliveryFee.ToString()),
                            ("[currencyname]", TextManager.Get("credit").ToLower()));
                    }
                    else
                    {
                        votingOnText = TextManager.GetWithVariables("submarineswitchnofeevote",
                            ("[playername]", characterRichString),
                            ("[submarinename]", submarineRichString));
                    }
                    break;
            }

            votingOnText = RichString.Rich(votingOnText);
        }

        private int SubmarineYesVotes()
        {
            return GameMain.NetworkMember.SubmarineVoteYesCount;
        }

        private int SubmarineNoVotes()
        {
            return GameMain.NetworkMember.SubmarineVoteNoCount;
        }

        private int SubmarineMaxVotes()
        {
            return GameMain.NetworkMember.SubmarineVoteMax;
        }

        private void SendSubmarineVoteEndMessage(SubmarineInfo info, VoteType type)
        {
            GameMain.NetworkMember.AddChatMessage(GetSubmarineVoteResultMessage(info, type, yesVotes.ToString(), noVotes.ToString(), votePassed).Value, ChatMessageType.Server);
        }

        public static LocalizedString GetSubmarineVoteResultMessage(SubmarineInfo info, VoteType type, string yesVoteString, string noVoteString, bool votePassed)
        {
            LocalizedString result = string.Empty;

            switch (type)
            {
                case VoteType.PurchaseAndSwitchSub:
                    result = TextManager.GetWithVariables(votePassed ? "submarinepurchaseandswitchvotepassed" : "submarinepurchaseandswitchvotefailed",
                        ("[submarinename]", info.DisplayName),
                        ("[amount]", info.Price.ToString()),
                        ("[currencyname]", TextManager.Get("credit").ToLower()),
                        ("[yesvotecount]", yesVoteString),
                        ("[novotecount]" , noVoteString));
                    break;
                case VoteType.PurchaseSub:
                    result = TextManager.GetWithVariables(votePassed ? "submarinepurchasevotepassed" : "submarinepurchasevotefailed",
                        ("[submarinename]", info.DisplayName),
                        ("[amount]", info.Price.ToString()),
                        ("[currencyname]", TextManager.Get("credit").ToLower()),
                        ("[yesvotecount]", yesVoteString),
                        ("[novotecount]", noVoteString));
                    break;
                case VoteType.SwitchSub:
                    int deliveryFee = SubmarineSelection.DeliveryFeePerDistanceTravelled * GameMain.GameSession.Map.DistanceToClosestLocationWithOutpost(GameMain.GameSession.Map.CurrentLocation, out Location endLocation);

                    if (deliveryFee > 0)
                    {
                        result = TextManager.GetWithVariables(votePassed ? "submarineswitchfeevotepassed" : "submarineswitchfeevotefailed",
                            ("[submarinename]", info.DisplayName),
                            ("[locationname]", endLocation.Name),
                            ("[amount]", deliveryFee.ToString()),
                            ("[currencyname]", TextManager.Get("credit").ToLower()),
                            ("[yesvotecount]", yesVoteString),
                            ("[novotecount]", noVoteString));
                    }
                    else
                    {
                        result = TextManager.GetWithVariables(votePassed ? "submarineswitchnofeevotepassed" : "submarineswitchnofeevotefailed",
                            ("[submarinename]", info.DisplayName),
                            ("[yesvotecount]", yesVoteString),
                            ("[novotecount]", noVoteString));
                    }
                    break;
                default:
                    break;
            }
            return result;
        }
        #endregion

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
