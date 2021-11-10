using System;
using System.Collections.Generic;

namespace Barotrauma
{
    abstract class CampaignSetupUI
    {
        protected readonly GUIComponent newGameContainer, loadGameContainer;

        protected GUIListBox subList;
        protected GUIListBox saveList;
        protected List<GUITickBox> subTickBoxes;

        protected GUITextBox saveNameBox, seedBox;

        protected GUILayoutGroup subPreviewContainer;

        protected GUIButton loadGameButton;
        
        public Action<SubmarineInfo, string, string, CampaignSettings> StartNewGame;
        public Action<string> LoadGame;

        protected enum CategoryFilter { All = 0, Vanilla = 1, Custom = 2 };            
        protected CategoryFilter subFilter = CategoryFilter.All;

        public GUIButton StartButton
        {
            get;
            protected set;
        }

        public GUITextBlock InitialMoneyText
        {
            get;
            protected set;
        }
        
        public GUITickBox EnableRadiationToggle { get; set; }
        public GUILayoutGroup CampaignSettingsContent { get; set; }

        public GUIButton CampaignCustomizeButton { get; set; }
        public GUIMessageBox CampaignCustomizeSettings { get; set; }

        public GUITextBlock MaxMissionCountText;

        public CampaignSetupUI(GUIComponent newGameContainer, GUIComponent loadGameContainer)
        {
            this.newGameContainer = newGameContainer;
            this.loadGameContainer = loadGameContainer;
        }
    }
}