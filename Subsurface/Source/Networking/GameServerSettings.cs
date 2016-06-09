using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

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
        public const string SettingsFile = "serversettings.xml";

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

        private bool saveServerLogs = true;

        private bool allowFileTransfers = true;

        public bool AutoRestart
        {
            get { return (connectedClients.Count != 0) && autoRestart; }
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

        public bool AllowVoteKick
        {
            get;
            private set;
        }

        public float EndVoteRequiredRatio = 0.5f;

        public float KickVoteRequiredRatio = 0.5f;

        private void SaveSettings()
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.NewLineOnAttributes = true;

            using (var writer = XmlWriter.Create(SettingsFile, settings))
            {
                writer.WriteStartElement("serversettings");
                writer.WriteAttributeString("AllowSpectating", allowSpectating.ToString());
                writer.WriteAttributeString("RandomizeSeed", randomizeSeed.ToString());

                writer.WriteAttributeString("EndRoundAtLevelEnd", endRoundAtLevelEnd.ToString());
                writer.WriteAttributeString("AllowFileTransfers", allowFileTransfers.ToString());
                writer.WriteAttributeString("MaxFileTransferDuration", ((int)FileStreamSender.MaxTransferDuration.TotalSeconds).ToString());
                writer.WriteAttributeString("SaveServerLogs", saveServerLogs.ToString());
                writer.WriteAttributeString("LinesPerLogFile", log.LinesPerFile.ToString());
                writer.WriteAttributeString("SubSelection", subSelectionMode.ToString());
                writer.WriteAttributeString("ModeSelection", modeSelectionMode.ToString());

                writer.Flush();
            }
        }

        private void LoadSettings()
        {
            XDocument doc = null;
            if (System.IO.File.Exists(SettingsFile))
            {
                doc = ToolBox.TryLoadXml(SettingsFile);
            }
            else
            {
                return;
            }

            if (doc == null)
            {
                doc = new XDocument(new XElement("serversettings"));
            }

            allowSpectating = ToolBox.GetAttributeBool(doc.Root, "AllowSpectating", true);
            randomizeSeed = ToolBox.GetAttributeBool(doc.Root, "RandomizeSeed", true);
            endRoundAtLevelEnd = ToolBox.GetAttributeBool(doc.Root, "EndRoundAtLevelEnd", true);
            allowFileTransfers = ToolBox.GetAttributeBool(doc.Root, "AllowFileTransfers", true);

            saveServerLogs = ToolBox.GetAttributeBool(doc.Root, "SaveServerLogs", true);
            log.LinesPerFile = ToolBox.GetAttributeInt(doc.Root, "LinesPerLogFile", 800);

            subSelectionMode = SelectionMode.Manual;
            Enum.TryParse<SelectionMode>(ToolBox.GetAttributeString(doc.Root, "SubSelection", "Manual"), out subSelectionMode);
            Voting.AllowSubVoting = subSelectionMode == SelectionMode.Vote;

            modeSelectionMode = SelectionMode.Manual;
            Enum.TryParse<SelectionMode>(ToolBox.GetAttributeString(doc.Root, "ModeSelection", "Manual"), out modeSelectionMode);
            Voting.AllowModeVoting = modeSelectionMode == SelectionMode.Vote;

            FileStreamSender.MaxTransferDuration = new TimeSpan(0,0,ToolBox.GetAttributeInt(doc.Root, "MaxFileTransferDuration", 150));            
        }

        private void CreateSettingsFrame()
        {
            settingsFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f);            

            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, 400, 420), null, Alignment.Center, GUI.Style, settingsFrame);
            innerFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0, -5, 0, 20), "Server settings", GUI.Style, innerFrame, GUI.LargeFont);

            int y = 40;

            var endBox = new GUITickBox(new Rectangle(0, y, 20, 20), "End round when destination reached", Alignment.Left, innerFrame);
            endBox.Selected = endRoundAtLevelEnd;
            endBox.OnSelected = (GUITickBox) => { endRoundAtLevelEnd = GUITickBox.Selected; return true; };


            y += 30;

            var endVoteBox = new GUITickBox(new Rectangle(0, y, 20, 20), "End round by voting", Alignment.Left, innerFrame);
            endVoteBox.Selected = Voting.AllowEndVoting;
            endVoteBox.OnSelected = (GUITickBox) =>
            {
                Voting.AllowEndVoting = !Voting.AllowEndVoting;
                GameMain.Server.UpdateVoteStatus();
                return true;
            };

            var votesRequiredText = new GUITextBlock(new Rectangle(20, y+20, 20, 20), "Votes required: 50 %", GUI.Style, innerFrame, GUI.SmallFont);

            var votesRequiredSlider = new GUIScrollBar(new Rectangle(150, y+22, 100, 10), GUI.Style, 0.1f, innerFrame);
            votesRequiredSlider.UserData = votesRequiredText;
            votesRequiredSlider.Step = 0.2f;
            votesRequiredSlider.BarScroll = (EndVoteRequiredRatio - 0.5f) * 2.0f;
            votesRequiredSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock voteText = scrollBar.UserData as GUITextBlock;

                EndVoteRequiredRatio = barScroll / 2.0f + 0.5f;
                voteText.Text = "Votes required: " + (int)MathUtils.Round(EndVoteRequiredRatio * 100.0f, 10.0f) + " %";
                return true;
            };
            votesRequiredSlider.OnMoved(votesRequiredSlider, votesRequiredSlider.BarScroll);
            
            y += 40;

            var voteKickBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Allow vote kicking", Alignment.Left, innerFrame);
            voteKickBox.Selected = Voting.AllowVoteKick;
            voteKickBox.OnSelected = (GUITickBox) =>
            {
                Voting.AllowVoteKick = !Voting.AllowVoteKick;
                GameMain.Server.UpdateVoteStatus();
                return true;
            };

            var kickVotesRequiredText = new GUITextBlock(new Rectangle(20, y + 20, 20, 20), "Votes required: 50 %", GUI.Style, innerFrame, GUI.SmallFont);

            var kickVoteSlider = new GUIScrollBar(new Rectangle(150, y + 22, 100, 10), GUI.Style, 0.1f, innerFrame);
            kickVoteSlider.UserData = kickVotesRequiredText;
            kickVoteSlider.Step = 0.2f;
            kickVoteSlider.BarScroll = (KickVoteRequiredRatio - 0.5f) * 2.0f;
            kickVoteSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock voteText = scrollBar.UserData as GUITextBlock;

                KickVoteRequiredRatio = barScroll / 2.0f + 0.5f;
                voteText.Text = "Votes required: " + (int)MathUtils.Round(KickVoteRequiredRatio * 100.0f, 10.0f) + " %";
                return true;
            };
            kickVoteSlider.OnMoved(kickVoteSlider, kickVoteSlider.BarScroll);

            y += 40;
            
            var randomizeLevelBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Randomize level seed between rounds", Alignment.Left, innerFrame);
            randomizeLevelBox.Selected = randomizeSeed;
            randomizeLevelBox.OnSelected = (GUITickBox) =>
            {
                randomizeSeed = GUITickBox.Selected;
                return true;
            };
            
            y += 40;

            var shareSubsBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Share submarine files with players", Alignment.Left, innerFrame);
            shareSubsBox.Selected = allowFileTransfers;
            shareSubsBox.OnSelected = (GUITickBox) =>
            {
                allowFileTransfers = GUITickBox.Selected;
                return true;
            };


            y += 40;


            new GUITextBlock(new Rectangle(0, y, 100, 20), "Submarine selection:", GUI.Style, innerFrame);
            var selectionFrame = new GUIFrame(new Rectangle(0, y+20, 300, 20), null, innerFrame);
            for (int i = 0; i<3; i++)
            {
                var selectionTick = new GUITickBox(new Rectangle(i * 100, 0, 20, 20), ((SelectionMode)i).ToString(), Alignment.Left, selectionFrame);
                selectionTick.Selected = i == (int)subSelectionMode;
                selectionTick.OnSelected = SwitchSubSelection;
                selectionTick.UserData = (SelectionMode)i;
            }

            y += 45;

            new GUITextBlock(new Rectangle(0, y, 100, 20), "Mode selection:", GUI.Style, innerFrame);
            selectionFrame = new GUIFrame(new Rectangle(0, y+20, 300, 20), null, innerFrame);
            for (int i = 0; i<3; i++)
            {
                var selectionTick = new GUITickBox(new Rectangle(i*100, 0, 20, 20), ((SelectionMode)i).ToString(), Alignment.Left, selectionFrame);
                selectionTick.Selected = i == (int)modeSelectionMode;
                selectionTick.OnSelected = SwitchModeSelection;
                selectionTick.UserData = (SelectionMode)i;
            }

            y += 60;

            var allowSpecBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Allow spectating", Alignment.Left, innerFrame);
            allowSpecBox.Selected = allowSpectating;
            allowSpecBox.OnSelected = (GUITickBox) =>
            {
                allowSpectating = GUITickBox.Selected;
                return true;
            };

            y += 30;

            var saveLogsBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Save server logs", Alignment.Left, innerFrame);
            saveLogsBox.Selected = saveServerLogs;
            saveLogsBox.OnSelected = (GUITickBox) =>
            {
                saveServerLogs = GUITickBox.Selected;
                showLogButton.Visible = saveServerLogs;
                return true;
            };
            
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


        public bool ToggleSettingsFrame(GUIButton button, object obj)
        {
            if (settingsFrame==null)
            {
                CreateSettingsFrame();
            }
            else
            {
                settingsFrame = null;
                SaveSettings();
            }

            return false;
        }
    }
}
