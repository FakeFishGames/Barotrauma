using Barotrauma.Tutorials;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
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
        private GUITickBox contextualTutorialBox;

        private GUIButton loadGameButton;
        
        public Action<Submarine, string, string> StartNewGame;
        public Action<string> LoadGame;
        public bool TutorialSelected
        {
            get
            {
                if (contextualTutorialBox == null) return false;
                return contextualTutorialBox.Selected;
            }
        }

        private bool isMultiplayer;

        public CampaignSetupUI(bool isMultiplayer, GUIComponent newGameContainer, GUIComponent loadGameContainer, IEnumerable<string> saveFiles=null)
        {
            this.isMultiplayer = isMultiplayer;
            this.newGameContainer = newGameContainer;
            this.loadGameContainer = loadGameContainer;
            
            var columnContainer = new GUILayoutGroup(new RectTransform(Vector2.One, newGameContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            var leftColumn = new GUILayoutGroup(new RectTransform(Vector2.One, columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var rightColumn = new GUILayoutGroup(new RectTransform(Vector2.One, columnContainer.RectTransform))
            {
                RelativeSpacing = 0.02f
            };

            // New game left side
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), leftColumn.RectTransform), TextManager.Get("SelectedSub") + ":", textAlignment: Alignment.BottomLeft);
            subList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.65f), leftColumn.RectTransform));

            UpdateSubList();

            // New game right side
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), rightColumn.RectTransform), TextManager.Get("SaveName") + ":", textAlignment: Alignment.BottomLeft);
            saveNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.1f), rightColumn.RectTransform), string.Empty);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), rightColumn.RectTransform), TextManager.Get("MapSeed") + ":", textAlignment: Alignment.BottomLeft);
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.1f), rightColumn.RectTransform), ToolBox.RandomSeed(8));

            if (!isMultiplayer)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), rightColumn.RectTransform), "Tutorial active" + ":", textAlignment: Alignment.BottomLeft);
                contextualTutorialBox = new GUITickBox(new RectTransform(new Point(30, 30), rightColumn.RectTransform), string.Empty);
                UpdateTutorialSelection();
            }

            var startButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.13f), rightColumn.RectTransform, Anchor.BottomRight), TextManager.Get("StartCampaignButton"), style: "GUIButtonLarge")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (GUIButton btn, object userData) =>
                {
                    if (string.IsNullOrWhiteSpace(saveNameBox.Text))
                    {
                        saveNameBox.Flash(Color.Red);
                        return false;
                    }

                    Submarine selectedSub = subList.SelectedData as Submarine;
                    if (selectedSub == null) return false;
                    
                    if (string.IsNullOrEmpty(selectedSub.MD5Hash.Hash))
                    {
                        ((GUITextBlock)subList.SelectedComponent).TextColor = Color.DarkRed * 0.8f;
                        subList.SelectedComponent.CanBeFocused = false;
                        subList.Deselect();
                        return false;
                    }

                    string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer, saveNameBox.Text);
                    bool hasRequiredContentPackages = selectedSub.RequiredContentPackages.All(cp => GameMain.SelectedPackages.Any(cp2 => cp2.Name == cp));

                    if (selectedSub.HasTag(SubmarineTag.Shuttle) || !hasRequiredContentPackages)
                    {
                        if (!hasRequiredContentPackages)
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                                TextManager.Get("ContentPackageMismatchWarning")
                                    .Replace("[requiredcontentpackages]", string.Join(", ", selectedSub.RequiredContentPackages)),
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
                }
            };

            UpdateLoadMenu(saveFiles);
        }

        public void CreateDefaultSaveName()
        {
            string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            saveNameBox.Text = Path.GetFileNameWithoutExtension(savePath);
        }

        public void UpdateSubList()
        {
#if DEBUG
            var subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus));
#else
            var subsToShow = Submarine.SavedSubmarines;
#endif

            subList.ClearChildren();

            foreach (Submarine sub in subsToShow)
            {
                var textBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0.1f), subList.Content.RectTransform)
                    {
                        AbsoluteOffset = new Point(10, 0)
                    },
                    ToolBox.LimitString(sub.Name, GUI.Font, subList.Rect.Width - 65), style: "ListBoxElement")
                    {
                        ToolTip = sub.Description,
                        UserData = sub
                    };


                var infoButton = new GUIButton(new RectTransform(new Vector2(0.12f, 0.8f), textBlock.RectTransform, Anchor.CenterRight), text: "?")
                {
                    UserData = sub
                };
                infoButton.OnClicked += (component, userdata) =>
                {
                    // TODO: use relative size
                    ((Submarine)userdata).CreatePreviewWindow(new GUIMessageBox("", "", 550, 400));
                    return true;
                };

                if (sub.HasTag(SubmarineTag.Shuttle))
                {
                    textBlock.TextColor = textBlock.TextColor * 0.85f;

                    var shuttleText = new GUITextBlock(new RectTransform(new Point(100, textBlock.Rect.Height), textBlock.RectTransform, Anchor.CenterRight)
                    {
                        IsFixedSize = false,
                        RelativeOffset = new Vector2(infoButton.RectTransform.RelativeSize.X + 0.01f, 0)
                    },
                        TextManager.Get("Shuttle"), textAlignment: Alignment.Right, font: GUI.SmallFont)
                    {
                        TextColor = textBlock.TextColor * 0.8f,
                        ToolTip = textBlock.ToolTip
                    };
                }
            }
            if (Submarine.SavedSubmarines.Any())
            {
                var nonShuttles = subsToShow.Where(s => !s.HasTag(SubmarineTag.Shuttle)).ToList();
                if (nonShuttles.Count > 0)
                {
                    subList.Select(nonShuttles[Rand.Int(nonShuttles.Count)]);
                }
            }
        }

        public void UpdateLoadMenu(IEnumerable<string> saveFiles=null)
        {
            loadGameContainer.ClearChildren();

            if (saveFiles == null)
            {
                saveFiles = SaveUtil.GetSaveFiles(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            }

            saveList = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), loadGameContainer.RectTransform, Anchor.CenterLeft))
            {
                OnSelected = SelectSaveFile
            };

            foreach (string saveFile in saveFiles)
            {
                XDocument doc = SaveUtil.LoadGameSessionDoc(saveFile);
                var saveFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), saveList.Content.RectTransform), style: "ListBoxElement")
                {
                    UserData = saveFile
                };

                var nameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform),
                    text: Path.GetFileNameWithoutExtension(saveFile));
                if (doc?.Root == null)
                {
                    DebugConsole.ThrowError("Error loading save file \"" + saveFile + "\". The file may be corrupted.");
                    nameText.Color = Color.Red;
                    continue;
                }

                string submarineName = doc.Root.GetAttributeString("submarine", "");
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                    text: submarineName, font: GUI.SmallFont)
                {
                    UserData = saveFile
                };

                string saveTime = doc.Root.GetAttributeString("savetime", "");
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                    text: saveTime, textAlignment: Alignment.Right, font: GUI.SmallFont)
                {
                    UserData = saveFile
                };
            }

            saveList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                string file1 = c1.GUIComponent.UserData as string;
                string file2 = c2.GUIComponent.UserData as string;
                DateTime file1WriteTime = DateTime.MinValue;
                DateTime file2WriteTime = DateTime.MinValue;
                try
                {
                    file1WriteTime = File.GetLastWriteTime(file1);
                }
                catch
                { 
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                try
                {
                    file2WriteTime = File.GetLastWriteTime(file2);
                }
                catch
                {
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                return file2WriteTime.CompareTo(file1WriteTime);
            });

            loadGameButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.12f), loadGameContainer.RectTransform, Anchor.BottomRight), TextManager.Get("LoadButton"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, obj) =>
                {
                    if (string.IsNullOrWhiteSpace(saveList.SelectedData as string)) return false;
                    LoadGame?.Invoke(saveList.SelectedData as string);
                    return true;
                },
                Enabled = false
            };
        }

        public void UpdateTutorialSelection()
        {
            if (isMultiplayer) return;
            Tutorial contextualTutorial = Tutorial.Tutorials.Find(t => t is ContextualTutorial);
            contextualTutorialBox.Selected = (contextualTutorial != null) ? !GameMain.Config.CompletedTutorialNames.Contains(contextualTutorial.Name) : true;
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

            var saveFileFrame = new GUIFrame(new RectTransform(new Vector2(0.45f, 0.6f), loadGameContainer.RectTransform, Anchor.TopRight)
            {
                RelativeOffset = new Vector2(0.0f, 0.1f)
            }, style: "InnerFrame")
            {
                UserData = "savefileframe"
            };

            new GUITextBlock(new RectTransform(new Vector2(1, 0.2f), saveFileFrame.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, 0.05f)
            }, 
            Path.GetFileNameWithoutExtension(fileName), font: GUI.LargeFont, textAlignment: Alignment.Center);

            var layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.5f), saveFileFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            });

            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("Submarine")} : {subName}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("LastSaved")} : {saveTime}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("MapSeed")} : {mapseed}", font: GUI.SmallFont);

            new GUIButton(new RectTransform(new Vector2(0.4f, 0.15f), saveFileFrame.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, TextManager.Get("Delete"))
            {
                UserData = fileName,
                OnClicked = DeleteSave
            };

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
            foreach (GUIComponent child in loadGameContainer.Children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            loadGameContainer.RemoveChild(prevFrame);
        }

    }
}
