using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma
{
    abstract class CampaignSetupUI
    {
        protected readonly GUIComponent newGameContainer, loadGameContainer;

        protected GUIListBox saveList;

        protected GUITextBox saveNameBox, seedBox;

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

        protected List<CampaignMode.SaveInfo> prevSaveFiles;
        protected GUIComponent CreateSaveElement(CampaignMode.SaveInfo saveInfo)
        {
            if (string.IsNullOrEmpty(saveInfo.FilePath))
            {
                DebugConsole.AddWarning("Error when updating campaign load menu: path to a save file was empty.\n" + Environment.StackTrace);
                return null;
            }

            var saveFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), saveList.Content.RectTransform) { MinSize = new Point(0, 45) }, style: "ListBoxElement")
            {
                UserData = saveInfo.FilePath
            };

            var nameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform), Path.GetFileNameWithoutExtension(saveInfo.FilePath))
            {
                CanBeFocused = false
            };

            if (saveInfo.EnabledContentPackageNames != null && saveInfo.EnabledContentPackageNames.Any())
            {
                if (!GameSession.IsCompatibleWithEnabledContentPackages(saveInfo.EnabledContentPackageNames, out LocalizedString errorMsg))
                {
                    nameText.TextColor = GUIStyle.Red;
                    saveFrame.ToolTip = string.Join("\n", errorMsg, TextManager.Get("campaignmode.contentpackagemismatchwarning"));
                }
            }

            prevSaveFiles ??= new List<CampaignMode.SaveInfo>();
            prevSaveFiles.Add(saveInfo);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                text: saveInfo.SubmarineName, font: GUIStyle.SmallFont)
            {
                CanBeFocused = false,
                UserData = saveInfo.FilePath
            };


            string saveTimeStr = string.Empty;
            if (saveInfo.SaveTime > 0)
            {
                DateTime time = ToolBox.Epoch.ToDateTime(saveInfo.SaveTime);
                saveTimeStr = time.ToString();
            }
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                text: saveTimeStr, textAlignment: Alignment.Right, font: GUIStyle.SmallFont)
            {
                CanBeFocused = false,
                UserData = saveInfo.FilePath
            };

            return saveFrame;
        }
    }
}