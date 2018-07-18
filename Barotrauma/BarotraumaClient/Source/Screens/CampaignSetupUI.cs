using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CampaignSetupUI
    {
        private GUIComponent newGameContainer, loadGameContainer;

        private GUIListBox subList;
        private GUIListBox saveList;

        private GUITextBox saveNameBox, seedBox;

        private GUIButton loadGameButton;
        
        public Action<Submarine, string, string> StartNewGame;
        public Action<string> LoadGame;

        private bool isMultiplayer;

        public CampaignSetupUI(bool isMultiplayer, GUIComponent newGameContainer, GUIComponent loadGameContainer)
        {
            this.isMultiplayer = isMultiplayer;
            this.newGameContainer = newGameContainer;
            this.loadGameContainer = loadGameContainer;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), TextManager.Get("SelectedSub") + ":", null, null, Alignment.Left, "", newGameContainer);
            subList = new GUIListBox(new Rectangle(0, 30, 230, newGameContainer.Rect.Height - 100), "", newGameContainer);

            UpdateSubList();

            new GUITextBlock(new Rectangle((int)(subList.Rect.Width + 20), 0, 100, 20),
                TextManager.Get("SaveName") + ": ", "", Alignment.Left, Alignment.Left, newGameContainer);

            saveNameBox = new GUITextBox(new Rectangle((int)(subList.Rect.Width + 30), 30, 180, 20),
                Alignment.TopLeft, "", newGameContainer);

            new GUITextBlock(new Rectangle((int)(subList.Rect.Width + 20), 60, 100, 20),
                TextManager.Get("MapSeed") + ": ", "", Alignment.Left, Alignment.Left, newGameContainer);

            seedBox = new GUITextBox(new Rectangle((int)(subList.Rect.Width + 30), 90, 180, 20),
                Alignment.TopLeft, "", newGameContainer);
            seedBox.Text = ToolBox.RandomSeed(8);
            
            var startButton = new GUIButton(new Rectangle(0, 0, 100, 30), TextManager.Get("StartCampaignButton"), Alignment.BottomRight, "", newGameContainer);
            startButton.OnClicked = (GUIButton btn, object userData) =>
            {
                if (string.IsNullOrWhiteSpace(saveNameBox.Text))
                {
                    saveNameBox.Flash(Color.Red);
                    return false;
                }
                
                Submarine selectedSub = subList.SelectedData as Submarine;
                if (selectedSub == null) return false;

                string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer, saveNameBox.Text);
                if (selectedSub.HasTag(SubmarineTag.Shuttle) || !selectedSub.CompatibleContentPackages.Contains(GameMain.SelectedPackage.Name))
                {
                    if (!selectedSub.CompatibleContentPackages.Contains(GameMain.SelectedPackage.Name))
                    {
                        var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                            TextManager.Get("ContentPackageMismatchWarning")
                                .Replace("[selectedcontentpackage]", GameMain.SelectedPackage.Name),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                        msgBox.Buttons[0].OnClicked = msgBox.Close;
                        msgBox.Buttons[0].OnClicked += (button, obj) => 
                        {
                            if (GUIMessageBox.MessageBoxes.Count == 0) StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                            return true;
                        };

                        msgBox.Buttons[1].OnClicked = msgBox.Close;
                    }

                    if (selectedSub.HasTag(SubmarineTag.Shuttle))
                    {
                        var msgBox = new GUIMessageBox(TextManager.Get("ShuttleSelected"),
                            TextManager.Get("ShuttleWarning"),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                        msgBox.Buttons[0].OnClicked = (button, obj) => { StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text); return true; };
                        msgBox.Buttons[0].OnClicked += msgBox.Close;

                        msgBox.Buttons[1].OnClicked = msgBox.Close;
                        return false;
                    }
                }
                else
                {
                    StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                }

                return true;
            };

            UpdateLoadMenu();
        }

        public void CreateDefaultSaveName()
        {
            string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            saveNameBox.Text = Path.GetFileNameWithoutExtension(savePath);
        }

        public void UpdateSubList()
        {
            var subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus));

            subList.ClearChildren();

            foreach (Submarine sub in subsToShow)
            {
                var textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    ToolBox.LimitString(sub.Name, GUI.Font, subList.Rect.Width - 65), "ListBoxElement",
                    Alignment.Left, Alignment.Left, subList)
                {
                    Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f),
                    ToolTip = sub.Description,
                    UserData = sub
                };

                if (sub.HasTag(SubmarineTag.Shuttle))
                {
                    textBlock.TextColor = textBlock.TextColor * 0.85f;

                    var shuttleText = new GUITextBlock(new Rectangle(-20, 0, 0, 25), TextManager.Get("Shuttle"), "", Alignment.CenterRight, Alignment.CenterRight, textBlock, false, GUI.SmallFont);
                    shuttleText.TextColor = textBlock.TextColor * 0.8f;
                    shuttleText.ToolTip = textBlock.ToolTip;
                }

                GUIButton infoButton = new GUIButton(new Rectangle(0, 0, 20, 20), "?", Alignment.CenterRight, "", textBlock);
                infoButton.UserData = sub;
                infoButton.OnClicked += (component, userdata) =>
                {
                    var msgBox = new GUIMessageBox("", "", 550, 400);
                    ((Submarine)userdata).CreatePreviewWindow(msgBox.InnerFrame);
                    return true;
                };
            }
            if (Submarine.SavedSubmarines.Any()) subList.Select(Submarine.SavedSubmarines.First());
        }

        public void UpdateLoadMenu()
        {
            loadGameContainer.ClearChildren();

            string[] saveFiles = SaveUtil.GetSaveFiles(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);

            saveList = new GUIListBox(new Rectangle(0, 0, 200, loadGameContainer.Rect.Height - 80), Color.White, "", loadGameContainer);
            saveList.OnSelected = SelectSaveFile;

            foreach (string saveFile in saveFiles)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    Path.GetFileNameWithoutExtension(saveFile),
                    "ListBoxElement",
                    Alignment.Left,
                    Alignment.Left,
                    saveList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = saveFile;
            }

            loadGameButton = new GUIButton(new Rectangle(0, 0, 100, 30), TextManager.Get("LoadButton"), Alignment.Right | Alignment.Bottom, "", loadGameContainer);
            loadGameButton.OnClicked = (btn, obj) => 
            {
                if (string.IsNullOrWhiteSpace(saveList.SelectedData as string)) return false;
                LoadGame?.Invoke(saveList.SelectedData as string);
                return true;
            };
            loadGameButton.Enabled = false;
        }

        private bool SelectSaveFile(GUIComponent component, object obj)
        {
            string fileName = (string)obj;

            XDocument doc = SaveUtil.LoadGameSessionDoc(fileName);

            if (doc == null)
            {
                DebugConsole.ThrowError("Error loading save file \"" + fileName + "\". The file may be corrupted.");
                return false;
            }

            loadGameButton.Enabled = true;

            RemoveSaveFrame();

            string subName = doc.Root.GetAttributeString("submarine", "");
            string saveTime = doc.Root.GetAttributeString("savetime", "unknown");
            string mapseed = doc.Root.GetAttributeString("mapseed", "unknown");

            GUIFrame saveFileFrame = new GUIFrame(new Rectangle((int)(saveList.Rect.Width + 20), 0, 200, 230), Color.Black * 0.4f, "", loadGameContainer);
            saveFileFrame.UserData = "savefileframe";
            saveFileFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0, 0, 0, 20), Path.GetFileNameWithoutExtension(fileName), "", Alignment.TopLeft, Alignment.TopLeft, saveFileFrame, false, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 35, 0, 20), TextManager.Get("Submarine") + ":", "", saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 52, 0, 20), subName, "", saveFileFrame).Font = GUI.SmallFont;

            new GUITextBlock(new Rectangle(0, 70, 0, 20), TextManager.Get("LastSaved") + ":", "", saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 85, 0, 20), saveTime, "", saveFileFrame).Font = GUI.SmallFont;

            new GUITextBlock(new Rectangle(0, 105, 0, 20), TextManager.Get("MapSeed") + ":", "", saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 120, 0, 20), mapseed, "", saveFileFrame).Font = GUI.SmallFont;

            var deleteSaveButton = new GUIButton(new Rectangle(0, 0, 100, 20), TextManager.Get("Delete"), Alignment.BottomCenter, "", saveFileFrame);
            deleteSaveButton.UserData = fileName;
            deleteSaveButton.OnClicked = DeleteSave;

            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;

            if (obj == null) return false;

            SaveUtil.DeleteSave(saveFile);

            UpdateLoadMenu();

            return true;
        }

        private void RemoveSaveFrame()
        {
            GUIComponent prevFrame = null;
            foreach (GUIComponent child in loadGameContainer.children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            loadGameContainer.RemoveChild(prevFrame);
        }

    }
}
