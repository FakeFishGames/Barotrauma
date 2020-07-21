using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class CrewManagement
    {
        private CampaignMode campaign => campaignUI.Campaign;
        private readonly CampaignUI campaignUI;
        private readonly GUIComponent parentComponent;

        private GUIListBox hireableList, pendingList, crewList;
        private GUIFrame characterPreviewFrame;
        private GUIDropDown sortingDropDown;
        private GUITextBlock totalBlock;
        private GUIButton validateHiresButton;
        private GUIButton clearAllButton;

        private List<CharacterInfo> PendingHires => campaign.Map?.CurrentLocation?.HireManager?.PendingHires;
        private bool HasPermission => campaignUI.Campaign.AllowedToManageCampaign();

        private Point resolutionWhenCreated;

        private enum SortingMethod
        {
            AlphabeticalAsc,
            JobAsc,
            PriceAsc,
            PriceDesc,
            SkillAsc,
            SkillDesc
        }

        public CrewManagement(CampaignUI campaignUI, GUIComponent parentComponent)
        {
            this.campaignUI = campaignUI;
            this.parentComponent = parentComponent;

            CreateUI();
            UpdateLocationView(campaignUI.Campaign.Map.CurrentLocation, true);

            campaignUI.Campaign.Map.OnLocationChanged += (prevLocation, newLocation) => UpdateLocationView(newLocation, true, prevLocation);
        }

        public void RefreshPermissions()
        {
            RefreshCrewFrames(hireableList);
            RefreshCrewFrames(crewList);
            RefreshCrewFrames(pendingList);
            if (clearAllButton != null) { clearAllButton.Enabled = HasPermission; }
        }

        private void RefreshCrewFrames(GUIListBox listBox)
        {
            if (listBox == null) { return; }
            listBox.CanBeFocused = HasPermission;
            foreach (GUIComponent child in listBox.Content.Children)
            {
                if (child.FindChild(c => c is GUIButton && c.UserData is CharacterInfo, true) is GUIButton buyButton)
                {
                    buyButton.Enabled = HasPermission;
                }
            }
        }

        private void CreateUI()
        {
            if (parentComponent.FindChild(c => c.UserData as string == "glow") is GUIComponent glowChild)
            {
                parentComponent.RemoveChild(glowChild);
            }
            if (parentComponent.FindChild(c => c.UserData as string == "container") is GUIComponent containerChild)
            {
                parentComponent.RemoveChild(containerChild);
            }

            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), parentComponent.RectTransform, Anchor.Center),
                style: "OuterGlow", color: Color.Black * 0.7f)
            {
                UserData = "glow"
            };
            new GUIFrame(new RectTransform(new Vector2(0.95f), parentComponent.RectTransform, anchor: Anchor.Center),
                style: null)
            {
                CanBeFocused = false,
                UserData = "container"
            };

            var availableMainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).RectTransform)
                {
                    MaxSize = new Point(560, campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Rect.Height)
                })
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            // Header ------------------------------------------------
            var headerGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), availableMainGroup.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.005f
            };
            var imageWidth = (float)headerGroup.Rect.Height / headerGroup.Rect.Width;
            new GUIImage(new RectTransform(new Vector2(imageWidth, 1.0f), headerGroup.RectTransform), "CrewManagementHeaderIcon");
            new GUITextBlock(new RectTransform(new Vector2(1.0f - imageWidth, 1.0f), headerGroup.RectTransform), TextManager.Get("campaigncrew.header"), font: GUI.LargeFont)
            {
                CanBeFocused = false,
                ForceUpperCase = true
            };

            var hireablesGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), anchor: Anchor.Center,
                    parent: new GUIFrame(new RectTransform(new Vector2(1.0f, 13.25f / 14.0f), availableMainGroup.RectTransform)).RectTransform))
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };

            var sortGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), hireablesGroup.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.015f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), sortGroup.RectTransform), text: TextManager.Get("campaignstore.sortby"));
            sortingDropDown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1.0f), sortGroup.RectTransform), elementCount: 5)
            {
                OnSelected = (child, userData) =>
                {
                    SortCharacters(hireableList, (SortingMethod)userData);
                    return true;
                }
            };
            var tag = "sortingmethod.";
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.JobAsc), userData: SortingMethod.JobAsc);
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.SkillAsc), userData: SortingMethod.SkillAsc);
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.SkillDesc), userData: SortingMethod.SkillDesc);
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.PriceAsc), userData: SortingMethod.PriceAsc);
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.PriceDesc), userData: SortingMethod.PriceDesc);

            hireableList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.96f), 
                hireablesGroup.RectTransform,
                anchor: Anchor.Center))
            {
                Spacing = 1
            };

            var pendingAndCrewMainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).RectTransform, anchor: Anchor.TopRight)
            {
                MaxSize = new Point(560, campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Rect.Height)
            })
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var playerBalanceContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), pendingAndCrewMainGroup.RectTransform), childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.005f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), playerBalanceContainer.RectTransform),
                TextManager.Get("campaignstore.balance"), font: GUI.Font, textAlignment: Alignment.BottomRight)
            {
                AutoScaleVertical = true,
                ForceUpperCase = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), playerBalanceContainer.RectTransform),
                "", font: GUI.SubHeadingFont, textAlignment: Alignment.TopRight)
            {
                AutoScaleVertical = true,
                TextScale = 1.1f,
                TextGetter = () => FormatCurrency(campaign.Money)
            };

            var pendingAndCrewGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), anchor: Anchor.Center,
                parent: new GUIFrame(new RectTransform(new Vector2(1.0f, 13.25f / 14.0f), pendingAndCrewMainGroup.RectTransform)
                        {
                            MaxSize = new Point(560, campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Rect.Height)
                        }).RectTransform));

            float height = 0.05f;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), TextManager.Get("campaigncrew.pending"), font: GUI.SubHeadingFont);
            pendingList = new GUIListBox(new RectTransform(new Vector2(1.0f, 8 * height), pendingAndCrewGroup.RectTransform))
            {
                Spacing = 1
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), TextManager.Get("campaignmenucrew"), font: GUI.SubHeadingFont);
            crewList = new GUIListBox(new RectTransform(new Vector2(1.0f, (8)* height), pendingAndCrewGroup.RectTransform))
            {
                Spacing = 1
            };

            var group = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), group.RectTransform), TextManager.Get("campaignstore.total"));
            totalBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), group.RectTransform), "", font: GUI.SubHeadingFont, textAlignment: Alignment.Right)
            {
                TextScale = 1.1f
            };
            group = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.01f
            };
            validateHiresButton = new GUIButton(new RectTransform(new Vector2(1.0f / 3.0f, 1.0f), group.RectTransform), text: TextManager.Get("campaigncrew.validate"))
            {
                ForceUpperCase = true,
                OnClicked = (b, o) => ValidatePendingHires(true)
            };
            clearAllButton = new GUIButton(new RectTransform(new Vector2(1.0f / 3.0f, 1.0f), group.RectTransform), text: TextManager.Get("campaignstore.clearall"))
            {
                ForceUpperCase = true,
                Enabled = HasPermission,
                OnClicked = (b, o) => RemoveAllPendingHires()
            };

            resolutionWhenCreated = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private void UpdateLocationView(Location location, bool removePending, Location prevLocation = null)
        {
            if (prevLocation != null && prevLocation == location && GameMain.NetworkMember != null) { return; }

            if (characterPreviewFrame != null)
            {
                characterPreviewFrame.Parent?.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }
            UpdateHireables(location);
            if (pendingList != null)
            {
                if (removePending)
                {
                    PendingHires?.Clear();
                    pendingList.Content.ClearChildren();
                }
                else
                {
                    PendingHires?.ForEach(ci => AddPendingHire(ci));
                }
                SetTotalHireCost();
            }
            UpdateCrew();
        }

        private void UpdateHireables(Location location)
        {
            if (hireableList != null)
            {
                hireableList.Content.Children.ToList().ForEach(c => hireableList.RemoveChild(c));
                var hireableCharacters = location.GetHireableCharacters();
                if (hireableCharacters.None())
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), hireableList.Content.RectTransform), TextManager.Get("HireUnavailable"), textAlignment: Alignment.Center)
                    {
                        CanBeFocused = false
                    };
                }
                else
                {
                    foreach (CharacterInfo c in hireableCharacters)
                    {
                        if (c == null) { continue; }
                        CreateCharacterFrame(c, hireableList);
                    }
                }
                sortingDropDown.SelectItem(SortingMethod.JobAsc);
                hireableList.UpdateScrollBarSize();
            }
        }

        public void SetHireables(Location location, List<CharacterInfo> availableHires)
        {
            HireManager hireManager = location.HireManager;
            if (hireManager == null) { return; }
            int hireVal = hireManager.AvailableCharacters.Aggregate(0, (curr, hire) => curr + hire.GetIdentifier());
            int newVal = availableHires.Aggregate(0, (curr, hire) => curr + hire.GetIdentifier());
            if (hireVal != newVal)
            {
                location.HireManager.AvailableCharacters = availableHires;
                UpdateHireables(location);
            }
        }

        public void UpdateCrew()
        {
            crewList.Content.Children.ToList().ForEach(c => crewList.Content.RemoveChild(c));
            foreach (CharacterInfo c in GameMain.GameSession.CrewManager.GetCharacterInfos())
            {
                if (c == null || !((c.Character?.IsBot ?? true) || campaign is SinglePlayerCampaign)) { continue; }
                CreateCharacterFrame(c, crewList);
            }
            SortCharacters(crewList, SortingMethod.JobAsc);
            crewList.UpdateScrollBarSize();
        }

        private void SortCharacters(GUIListBox list, SortingMethod sortingMethod)
        {
            if (sortingMethod == SortingMethod.AlphabeticalAsc)
            {
                list.Content.RectTransform.SortChildren((x, y) =>
                    (x.GUIComponent.UserData as Tuple<CharacterInfo, float>).Item1.Name.CompareTo((y.GUIComponent.UserData as Tuple<CharacterInfo, float>).Item1.Name));
            }
            else if (sortingMethod == SortingMethod.JobAsc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    String.Compare((x.GUIComponent.UserData as Tuple<CharacterInfo, float>)?.Item1.Job.Name, (y.GUIComponent.UserData as Tuple<CharacterInfo, float>).Item1.Job.Name, StringComparison.Ordinal));
            }
            else if (sortingMethod == SortingMethod.PriceAsc || sortingMethod == SortingMethod.PriceDesc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    (x.GUIComponent.UserData as Tuple<CharacterInfo, float>).Item1.Salary.CompareTo((y.GUIComponent.UserData as Tuple<CharacterInfo, float>).Item1.Salary));
                if (sortingMethod == SortingMethod.PriceDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
            else if (sortingMethod == SortingMethod.SkillAsc || sortingMethod == SortingMethod.SkillDesc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    (x.GUIComponent.UserData as Tuple<CharacterInfo, float>).Item2.CompareTo((y.GUIComponent.UserData as Tuple<CharacterInfo, float>).Item2));
                if (sortingMethod == SortingMethod.SkillDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
        }

        private void CreateCharacterFrame(CharacterInfo characterInfo, GUIListBox listBox)
        {
            Skill skill = null;
            Color? jobColor = null;
            if (characterInfo.Job != null)
            {
                skill = characterInfo.Job?.PrimarySkill ?? characterInfo.Job.Skills.OrderByDescending(s => s.Level).FirstOrDefault();
                jobColor = characterInfo.Job.Prefab.UIColor;
            }

            GUIFrame frame = new GUIFrame(new RectTransform(new Point(listBox.Content.Rect.Width, 55), parent: listBox.Content.RectTransform), "ListBoxElement")
            {
                UserData = new Tuple<CharacterInfo, float>(characterInfo, skill != null ? skill.Level : 0.0f)
            };
            GUILayoutGroup mainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), frame.RectTransform, anchor: Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            float portraitWidth = (0.8f * mainGroup.Rect.Height) / mainGroup.Rect.Width;
            new GUICustomComponent(new RectTransform(new Vector2(portraitWidth, 0.8f), mainGroup.RectTransform),
                onDraw: (sb, component) => characterInfo.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
            {
                CanBeFocused = false
            };

            GUILayoutGroup nameAndJobGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f - portraitWidth, 0.8f), mainGroup.RectTransform));
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), nameAndJobGroup.RectTransform),
                characterInfo.Name, textColor: jobColor, textAlignment: Alignment.BottomLeft)
            {
                CanBeFocused = false
            };
            nameBlock.Text = ToolBox.LimitString(nameBlock.Text, nameBlock.Font, nameBlock.Rect.Width);
            GUITextBlock jobBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), nameAndJobGroup.RectTransform),
                characterInfo.Job.Name, textColor: Color.White, font: GUI.SmallFont, textAlignment: Alignment.TopLeft)
            {
                CanBeFocused = false
            };
            jobBlock.Text = ToolBox.LimitString(jobBlock.Text, jobBlock.Font, jobBlock.Rect.Width);

            float width =  0.6f / 3;
            if (characterInfo.Job != null)
            {
                GUILayoutGroup skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(width, 0.6f), mainGroup.RectTransform), isHorizontal: true);
                float iconWidth = (float)skillGroup.Rect.Height / skillGroup.Rect.Width;
                GUIImage skillIcon = new GUIImage(new RectTransform(new Vector2(iconWidth, 1.0f), skillGroup.RectTransform), skill.Icon)
                {
                    CanBeFocused = false
                };
                if (jobColor.HasValue) { skillIcon.Color = jobColor.Value; }
                new GUITextBlock(new RectTransform(new Vector2(1.0f - iconWidth, 1.0f), skillGroup.RectTransform), ((int)skill.Level).ToString(), textAlignment: Alignment.CenterLeft)
                {
                    CanBeFocused = false
                };
            }

            if (listBox != crewList)
            {
                new GUITextBlock(new RectTransform(new Vector2(width, 1.0f), mainGroup.RectTransform), FormatCurrency(characterInfo.Salary), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
            }

            if (listBox == hireableList)
            {
                new GUIButton(new RectTransform(new Vector2(width, 0.9f), mainGroup.RectTransform), style: "CrewManagementAddButton")
                {
                    UserData = characterInfo,
                    Enabled = HasPermission,
                    OnClicked = (b, o) => AddPendingHire(o as CharacterInfo)
                };
            }
            else if (listBox == pendingList)
            {
                new GUIButton(new RectTransform(new Vector2(width, 0.9f), mainGroup.RectTransform), style: "CrewManagementRemoveButton")
                {
                    UserData = characterInfo,
                    Enabled = HasPermission,
                    OnClicked = (b, o) => RemovePendingHire(o as CharacterInfo)
                };
            }
            else if (listBox == crewList && campaign != null)
            {
                var currentCrew = GameMain.GameSession.CrewManager.GetCharacterInfos();
                new GUIButton(new RectTransform(new Vector2(width, 0.9f), mainGroup.RectTransform), style: "CrewManagementFireButton")
                {
                    UserData = characterInfo,
                    //can't fire if there's only one character in the crew
                    Enabled = currentCrew.Contains(characterInfo) && currentCrew.Count() > 1 && HasPermission,
                    OnClicked = (btn, obj) =>
                    {
                        var confirmDialog = new GUIMessageBox(
                            TextManager.Get("FireWarningHeader"),
                            TextManager.GetWithVariable("FireWarningText", "[charactername]", ((CharacterInfo)obj).Name),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                        confirmDialog.Buttons[0].UserData = (CharacterInfo)obj;
                        confirmDialog.Buttons[0].OnClicked = FireCharacter;
                        confirmDialog.Buttons[0].OnClicked += confirmDialog.Close;
                        confirmDialog.Buttons[1].OnClicked = confirmDialog.Close;
                        return true;
                    }
                };
            }
        }

        private void CreateCharacterPreviewFrame(GUIListBox listBox, GUIFrame characterFrame, CharacterInfo characterInfo)
        {
            Pivot pivot = listBox == hireableList ? Pivot.TopLeft : Pivot.TopRight;
            Point absoluteOffset = new Point(
                pivot == Pivot.TopLeft ? listBox.Parent.Parent.Rect.Right + 5 : listBox.Parent.Parent.Rect.Left - 5,
                characterFrame.Rect.Top);
            int frameSize = (int)(GUI.Scale * 300);
            if (GameMain.GraphicsHeight - (absoluteOffset.Y + frameSize) < 0)
            {
                pivot = listBox == hireableList ? Pivot.BottomLeft : Pivot.BottomRight;
                absoluteOffset.Y = characterFrame.Rect.Bottom;
            }
            characterPreviewFrame = new GUIFrame(new RectTransform(new Point(frameSize), parent: campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Parent.RectTransform, pivot: pivot)
                {
                    AbsoluteOffset = absoluteOffset
                }, style: "InnerFrame")
            {
                UserData = characterInfo
            };
            GUILayoutGroup mainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), characterPreviewFrame.RectTransform, anchor: Anchor.Center))
            {
                RelativeSpacing = 0.01f
            };

            // Character info
            GUILayoutGroup infoGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.475f), mainGroup.RectTransform), isHorizontal: true);
            GUILayoutGroup infoLabelGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), infoGroup.RectTransform)) { Stretch = true };
            GUILayoutGroup infoValueGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), infoGroup.RectTransform)) { Stretch = true };
            float blockHeight = 1.0f / 4;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("name"));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), characterInfo.Name);
            if (characterInfo.HasGenders)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("gender"));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), TextManager.Get(characterInfo.Gender.ToString()));
            }
            if (characterInfo.Job is Job job)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("tabmenu.job"));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), job.Name);
            }
            if (characterInfo.PersonalityTrait is NPCPersonalityTrait trait)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("PersonalityTrait"));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), TextManager.Get("personalitytrait." + trait.Name.Replace(" ", "")));
            }
            infoLabelGroup.Recalculate();
            infoValueGroup.Recalculate();

            new GUIImage(new RectTransform(new Vector2(1.0f, 0.05f), mainGroup.RectTransform), "HorizontalLine");

            // Skills
            GUILayoutGroup skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.475f), mainGroup.RectTransform), isHorizontal: true);
            GUILayoutGroup skillNameGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1.0f), skillGroup.RectTransform));
            GUILayoutGroup skillLevelGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1.0f), skillGroup.RectTransform));
            List<Skill> characterSkills = characterInfo.Job.Skills;
            blockHeight = 1.0f / characterSkills.Count;
            foreach (Skill skill in characterSkills)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), skillNameGroup.RectTransform), TextManager.Get("SkillName." + skill.Identifier));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), skillLevelGroup.RectTransform), ((int)skill.Level).ToString(), textAlignment: Alignment.Right);
            }
        }

        private bool SelectCharacter(GUIListBox listBox, GUIFrame characterFrame, CharacterInfo characterInfo)
        {
            if (characterPreviewFrame != null && characterPreviewFrame.UserData != characterInfo)
            {
                characterPreviewFrame.Parent?.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }

            if (listBox == null || characterFrame == null || characterInfo == null) { return false; }

            if (characterPreviewFrame == null)
            {
                CreateCharacterPreviewFrame(listBox, characterFrame, characterInfo);
            }

            return true;
        }

        private bool AddPendingHire(CharacterInfo characterInfo, bool createNetworkMessage = true)
        {
            hireableList.Content.RemoveChild(hireableList.Content.FindChild(c => (c.UserData as Tuple<CharacterInfo, float>).Item1 == characterInfo));
            hireableList.UpdateScrollBarSize();
            if (!PendingHires.Contains(characterInfo)) { PendingHires.Add(characterInfo); }
            CreateCharacterFrame(characterInfo, pendingList);
            SortCharacters(pendingList, SortingMethod.JobAsc);
            pendingList.UpdateScrollBarSize();
            SetTotalHireCost();
            if (createNetworkMessage) { SendCrewState(true); }
            return true;
        }

        private bool RemovePendingHire(CharacterInfo characterInfo, bool setTotalHireCost = true, bool createNetworkMessage = true)
        {
            if (PendingHires.Contains(characterInfo)) { PendingHires.Remove(characterInfo); }
            pendingList.Content.RemoveChild(pendingList.Content.FindChild(c => (c.UserData as Tuple<CharacterInfo, float>).Item1 == characterInfo));
            pendingList.UpdateScrollBarSize();
            CreateCharacterFrame(characterInfo, hireableList);
            SortCharacters(hireableList, (SortingMethod)sortingDropDown.SelectedItemData);
            hireableList.UpdateScrollBarSize();
            if (setTotalHireCost) { SetTotalHireCost(); }
            if (createNetworkMessage) { SendCrewState(true); }
            return true;
        }

        private bool RemoveAllPendingHires(bool createNetworkMessage = true)
        {
            pendingList.Content.Children.ToList().ForEach(c => RemovePendingHire((c.UserData as Tuple<CharacterInfo, float>).Item1, setTotalHireCost: false, createNetworkMessage));
            SetTotalHireCost();
            return true;
        }

        private void SetTotalHireCost()
        {
            if (pendingList == null || totalBlock == null || validateHiresButton == null) { return; }
            int total = 0;
            pendingList.Content.Children.ForEach(c => total += (c.UserData as Tuple<CharacterInfo, float>).Item1.Salary);
            totalBlock.Text = FormatCurrency(total);
            bool enoughMoney = campaign != null ? total <= campaign.Money : true;
            totalBlock.TextColor = enoughMoney ? Color.White : Color.Red;
            validateHiresButton.Enabled = enoughMoney && pendingList.Content.RectTransform.Children.Any();
        }

        public bool ValidatePendingHires(bool createNetworkEvent = false)
        {
            List<CharacterInfo> hires = new List<CharacterInfo>();
            int total = 0;
            foreach (GUIComponent c in pendingList.Content.Children.ToList())
            {
                if (c.UserData is Tuple<CharacterInfo, float> info)
                {
                    hires.Add(info.Item1);
                    total += info.Item1.Salary;
                }
            }

            if (hires.None() || total > campaign.Money) { return false; }

            bool atLeastOneHired = false;
            foreach (CharacterInfo ci in hires)
            {
                if (campaign.TryHireCharacter(campaign.Map.CurrentLocation, ci))
                {
                    atLeastOneHired = true;
                    PendingHires.Remove(ci);
                    pendingList.Content.RemoveChild(pendingList.Content.FindChild(c => (c.UserData as Tuple<CharacterInfo, float>).Item1 == ci));
                }
                else
                {
                    break;
                }
            }

            if (atLeastOneHired)
            {
                UpdateLocationView(campaign.Map.CurrentLocation, true);
                SelectCharacter(null, null, null);
                var dialog = new GUIMessageBox(
                    TextManager.Get("newcrewmembers"),
                    TextManager.GetWithVariable("crewhiredmessage", "[location]", campaignUI?.Campaign?.Map?.CurrentLocation?.Name),
                    new string[] { TextManager.Get("Ok") });
                dialog.Buttons[0].OnClicked += dialog.Close;
            }

            if (createNetworkEvent)
            {
                SendCrewState(true, validateHires: true);
            }

            return false;
        }

        private bool FireCharacter(GUIButton button, object selection)
        {
            if (!(selection is CharacterInfo characterInfo)) { return false; }

            campaign.CrewManager.FireCharacter(characterInfo);
            SelectCharacter(null, null, null);
            UpdateCrew();

            SendCrewState(false, firedCharacter: characterInfo);
            return false;
        }

        public void Update()
        {
            if (GameMain.GraphicsWidth != resolutionWhenCreated.X || GameMain.GraphicsHeight != resolutionWhenCreated.Y)
            {
                CreateUI();
                UpdateLocationView(campaign.Map.CurrentLocation, false);
            }

            if ((GUI.MouseOn?.UserData as Tuple<CharacterInfo, float>)?.Item1 is CharacterInfo characterInfo)
            {
                if (characterPreviewFrame == null || characterInfo != characterPreviewFrame.UserData)
                {
                    GUIComponent component = GUI.MouseOn;
                    GUIListBox listBox = null;
                    do
                    {
                        if (component.Parent is GUIListBox)
                        {
                            listBox = component.Parent as GUIListBox;
                            break;
                        }
                        else if (component.Parent != null)
                        {
                            component = component.Parent;
                        }
                        else
                        {
                            break;
                        }
                    } while (listBox == null);

                    if (listBox != null)
                    {
                        SelectCharacter(listBox, GUI.MouseOn as GUIFrame, characterInfo);
                    }
                }
                else
                {
                    // TODO: Reposition the current preview panel if necessary
                    // Could happen if we scroll a list while hovering?
                }
            }
            else if (characterPreviewFrame != null)
            {
                characterPreviewFrame.Parent?.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }
        }

        public void SetPendingHires(List<int> characterInfos, Location location)
        {
            List<CharacterInfo> oldHires = PendingHires.ToList();
            foreach (CharacterInfo pendingHire in oldHires)
            {
                RemovePendingHire(pendingHire, createNetworkMessage: false);
            }
            PendingHires.Clear();
            foreach (int identifier in characterInfos)
            {
                CharacterInfo match = location.HireManager.AvailableCharacters.Find(info => info.GetIdentifier() == identifier);
                if (match != null)
                {
                    PendingHires.Add(match);
                    AddPendingHire(match, createNetworkMessage: false);
                }
                else
                {
                    DebugConsole.ThrowError("Received a hire that doesn't exist.");
                }
            }
        }

        /// <summary>
        /// Notify the server of crew changes
        /// </summary>
        /// <param name="updatePending">When set to true will tell the server to update the pending hires</param>
        /// <param name="firedCharacter">When not null tell the server to fire this character</param>
        /// <param name="validateHires">When set to true will tell the server to validate pending hires</param>
        public void SendCrewState(bool updatePending, CharacterInfo firedCharacter = null, bool validateHires = false)
        {
            if (campaign is MultiPlayerCampaign)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ClientPacketHeader.CREW);
                
                msg.Write(updatePending);
                if (updatePending)
                {
                    msg.Write((ushort)PendingHires.Count);
                    foreach (CharacterInfo pendingHire in PendingHires)
                    {
                        msg.Write(pendingHire.GetIdentifier());
                    }
                }

                msg.Write(validateHires);

                msg.Write(firedCharacter != null);
                if (firedCharacter != null)
                {
                    msg.Write(firedCharacter.GetIdentifier());
                }

                GameMain.Client.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
            }
        }

        private string FormatCurrency(int currency) =>  TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", currency));
    }
}
