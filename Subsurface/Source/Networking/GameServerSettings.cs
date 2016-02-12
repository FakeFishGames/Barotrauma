using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    enum SelectionMode : int
    {
        Manual = 0, Random = 1, Vote = 2
    }

    enum YesNoMaybe : int
    {
        No = 0, Maybe = 1, Yes = 2
    }

    partial class GameServer : NetworkMember 
    {
        public bool ShowNetStats;

        private TimeSpan refreshMasterInterval = new TimeSpan(0, 0, 30);
        private TimeSpan sparseUpdateInterval = new TimeSpan(0, 0, 0, 3);

        private SelectionMode subSelectionMode, modeSelectionMode;

        private bool randomizeSeed = true;
        
        private bool registeredToMaster;

        private BanList banList;

        private string password;

        private GUIFrame settingsFrame;

        public float AutoRestartTimer;
        
        private bool autoRestart;

        private bool allowSpectating = true;

        private bool endRoundAtLevelEnd = true;

        public bool AutoRestart
        {
            get { return (ConnectedClients.Count == 0) ? false : autoRestart; }
            set
            {
                autoRestart = value;

                AutoRestartTimer = autoRestart ? 20.0f : 0.0f;
            }
        }

        public YesNoMaybe TraitorsEnabled
        {
            get;
            set;
        }

        public SelectionMode SubSelectionMode
        {
            get { return subSelectionMode; }
        }

        public SelectionMode ModeSelectionMode
        {
            get { return modeSelectionMode; }
        }

        public bool RandomizeSeed
        {
            get { return randomizeSeed; }
        }

        public BanList BanList
        {
            get { return banList; }
        }

        public bool AllowSpectating
        {
            get { return allowSpectating; }
        }

        public float EndVoteRequiredRatio;

        private void CreateSettingsFrame()
        {
            settingsFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f);

            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, 400, 400), null, Alignment.Center, GUI.Style, settingsFrame);

            new GUITextBlock(new Rectangle(0, -15, 0, 20), "Server settings", GUI.Style, innerFrame, GUI.LargeFont);

            var randomizeLevelBox = new GUITickBox(new Rectangle(0, 30, 20, 20), "Randomize level seed between rounds", Alignment.Left, innerFrame);
            randomizeLevelBox.Selected = randomizeSeed;
            randomizeLevelBox.OnSelected = ToggleRandomizeSeed;

            var endBox = new GUITickBox(new Rectangle(0, 60, 20, 20), "End round when destination reached", Alignment.Left, innerFrame);
            endBox.Selected = endRoundAtLevelEnd;
            endBox.OnSelected = (GUITickBox) => { endRoundAtLevelEnd = GUITickBox.Selected; return true; };

            var endVoteBox = new GUITickBox(new Rectangle(0, 90, 20, 20), "End round by voting", Alignment.Left, innerFrame);
            endVoteBox.Selected = Voting.AllowEndVoting;
            endVoteBox.OnSelected = (GUITickBox) => 
            {
                Voting.AllowEndVoting = !Voting.AllowEndVoting;
                GameMain.Server.UpdateVoteStatus();
                return true; 
            };

            var votesRequiredText = new GUITextBlock(new Rectangle(20, 110, 20, 20), "Votes required: 50 %", GUI.Style, innerFrame, GUI.SmallFont);

            var votesRequiredSlider = new GUIScrollBar(new Rectangle(150,115, 100, 10), GUI.Style, 0.1f, innerFrame);
            votesRequiredSlider.UserData = votesRequiredText;
            votesRequiredSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock voteText = scrollBar.UserData as GUITextBlock;
                    
                scrollBar.BarScroll = MathUtils.Round(barScroll, 0.2f);
                EndVoteRequiredRatio = barScroll/2.0f + 0.5f;
                voteText.Text = "Votes required: " + (int)MathUtils.Round(EndVoteRequiredRatio * 100.0f, 10.0f) + " %";
                return true;
            };
            
            new GUITextBlock(new Rectangle(0, 95+50, 100, 20), "Submarine selection:", GUI.Style, innerFrame);
            var selectionFrame = new GUIFrame(new Rectangle(0, 120 + 50, 300, 20), null, innerFrame);
            for (int i = 0; i<3; i++)
            {
                var selectionTick = new GUITickBox(new Rectangle(i * 100, 0, 20, 20), ((SelectionMode)i).ToString(), Alignment.Left, selectionFrame);
                selectionTick.Selected = i == (int)subSelectionMode;
                selectionTick.OnSelected = SwitchSubSelection;
                selectionTick.UserData = (SelectionMode)i;
            }

            new GUITextBlock(new Rectangle(0, 145 + 50, 100, 20), "Mode selection:", GUI.Style, innerFrame);
            selectionFrame = new GUIFrame(new Rectangle(0, 170 + 50, 300, 20), null, innerFrame);
            for (int i = 0; i<3; i++)
            {
                var selectionTick = new GUITickBox(new Rectangle(i*100, 0, 20, 20), ((SelectionMode)i).ToString(), Alignment.Left, selectionFrame);
                selectionTick.Selected = i == (int)modeSelectionMode;
                selectionTick.OnSelected = SwitchModeSelection;
                selectionTick.UserData = (SelectionMode)i;
            }

            var allowSpecBox = new GUITickBox(new Rectangle(0, 210 + 50, 20, 20), "Allow spectating", Alignment.Left, innerFrame);
            allowSpecBox.Selected = true;
            allowSpecBox.OnSelected = ToggleAllowSpectating;            
            
            var closeButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Close", Alignment.BottomRight, GUI.Style, innerFrame);
            closeButton.OnClicked = ToggleSettingsFrame;
        }

        private bool SwitchSubSelection(GUITickBox tickBox)
        {
            subSelectionMode = (SelectionMode)tickBox.UserData;

            foreach (GUIComponent otherTickBox in tickBox.Parent.children)
            {
                if (otherTickBox == tickBox) continue;
                ((GUITickBox)otherTickBox).Selected = false;
            }

            Voting.AllowSubVoting = subSelectionMode == SelectionMode.Vote;

            if (subSelectionMode==SelectionMode.Random)
            {
                GameMain.NetLobbyScreen.SubList.Select(Rand.Range(0, GameMain.NetLobbyScreen.SubList.CountChildren));
            }

            return true;
        }

        private bool SwitchModeSelection(GUITickBox tickBox)
        {
            modeSelectionMode = (SelectionMode)tickBox.UserData;

            foreach (GUIComponent otherTickBox in tickBox.Parent.children)
            {
                if (otherTickBox == tickBox) continue;
                ((GUITickBox)otherTickBox).Selected = false;
            }

            Voting.AllowModeVoting = modeSelectionMode == SelectionMode.Vote;

            if (modeSelectionMode == SelectionMode.Random)
            {
                GameMain.NetLobbyScreen.ModeList.Select(Rand.Range(0, GameMain.NetLobbyScreen.ModeList.CountChildren));
            }

            return true;
        }

        private bool ToggleRandomizeSeed(GUITickBox tickBox)
        {
            randomizeSeed = tickBox.Selected;
            return true;
        }

        private bool ToggleAllowSpectating(GUITickBox tickBox)
        {
            allowSpectating = tickBox.Selected;
            return true;
        }

        public bool ToggleSettingsFrame(GUIButton button, object obj)
        {
            if (settingsFrame==null)
            {
                CreateSettingsFrame();
            }
            else
            {
                settingsFrame = null;
            }

            return false;
        }
    }
}
