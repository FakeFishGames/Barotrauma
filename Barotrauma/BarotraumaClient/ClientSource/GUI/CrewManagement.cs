using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PlayerBalanceElement = Barotrauma.CampaignUI.PlayerBalanceElement;

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

        private PlayerBalanceElement? playerBalanceElement;

        private List<CharacterInfo> PendingHires => campaign.Map?.CurrentLocation?.HireManager?.PendingHires;
        private bool HasPermission => CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageHires);

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

            campaignUI.Campaign.Map.OnLocationChanged.RegisterOverwriteExisting(
                "CrewManagement.UpdateLocationView".ToIdentifier(), 
                (locationChangeInfo) => UpdateLocationView(locationChangeInfo.NewLocation, true, locationChangeInfo.PrevLocation));
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

            int panelMaxWidth = (int)(GUI.xScale * (GUI.HorizontalAspectRatio < 1.4f ? 650 : 560));
            var availableMainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).RectTransform)
                {
                    MaxSize = new Point(panelMaxWidth, campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Rect.Height)
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
            new GUITextBlock(new RectTransform(new Vector2(1.0f - imageWidth, 1.0f), headerGroup.RectTransform), TextManager.Get("campaigncrew.header"), font: GUIStyle.LargeFont)
            {
                CanBeFocused = false,
                ForceUpperCase = ForceUpperCase.Yes
            };

            var hireablesGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), anchor: Anchor.Center,
                    parent: new GUIFrame(new RectTransform(new Vector2(1.0f, 13.25f / 14.0f), availableMainGroup.RectTransform)).RectTransform))
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };

            var sortGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), hireablesGroup.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), sortGroup.RectTransform), text: TextManager.Get("campaignstore.sortby"));
            sortingDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1.0f), sortGroup.RectTransform), elementCount: 5)
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
                MaxSize = new Point(panelMaxWidth, campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Rect.Height)
            })
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            playerBalanceElement = CampaignUI.AddBalanceElement(pendingAndCrewMainGroup, new Vector2(1.0f, 0.75f / 14.0f));

            var pendingAndCrewGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), anchor: Anchor.Center,
                parent: new GUIFrame(new RectTransform(new Vector2(1.0f, 13.25f / 14.0f), pendingAndCrewMainGroup.RectTransform)
                        {
                            MaxSize = new Point(panelMaxWidth, campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Rect.Height)
                        }).RectTransform));

            float height = 0.05f;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), TextManager.Get("campaigncrew.pending"), font: GUIStyle.SubHeadingFont);
            pendingList = new GUIListBox(new RectTransform(new Vector2(1.0f, 8 * height), pendingAndCrewGroup.RectTransform))
            {
                Spacing = 1
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), TextManager.Get("campaignmenucrew"), font: GUIStyle.SubHeadingFont);
            crewList = new GUIListBox(new RectTransform(new Vector2(1.0f, 8 * height), pendingAndCrewGroup.RectTransform))
            {
                Spacing = 1
            };

            var group = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), group.RectTransform), TextManager.Get("campaignstore.total"));
            totalBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), group.RectTransform), "", font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Right)
            {
                TextScale = 1.1f
            };
            group = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.01f
            };
            validateHiresButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), group.RectTransform), text: TextManager.Get("campaigncrew.validate"))
            {
                ClickSound = GUISoundType.ConfirmTransaction,
                ForceUpperCase = ForceUpperCase.Yes,
                OnClicked = (b, o) => ValidateHires(PendingHires, true)
            };
            clearAllButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), group.RectTransform), text: TextManager.Get("campaignstore.clearall"))
            {
                ClickSound = GUISoundType.Cart,
                ForceUpperCase = ForceUpperCase.Yes,
                Enabled = HasPermission,
                OnClicked = (b, o) => RemoveAllPendingHires()
            };
            GUITextBlock.AutoScaleAndNormalize(validateHiresButton.TextBlock, clearAllButton.TextBlock);

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
                    ((InfoSkill)x.GUIComponent.UserData).CharacterInfo.Name.CompareTo(((InfoSkill)y.GUIComponent.UserData).CharacterInfo.Name));
            }
            else if (sortingMethod == SortingMethod.JobAsc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    String.Compare(((InfoSkill)x.GUIComponent.UserData).CharacterInfo.Job.Name.Value, ((InfoSkill)y.GUIComponent.UserData).CharacterInfo.Job.Name.Value, StringComparison.Ordinal));
            }
            else if (sortingMethod == SortingMethod.PriceAsc || sortingMethod == SortingMethod.PriceDesc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    ((InfoSkill)x.GUIComponent.UserData).CharacterInfo.Salary.CompareTo(((InfoSkill)y.GUIComponent.UserData).CharacterInfo.Salary));
                if (sortingMethod == SortingMethod.PriceDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
            else if (sortingMethod == SortingMethod.SkillAsc || sortingMethod == SortingMethod.SkillDesc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    ((InfoSkill)x.GUIComponent.UserData).SkillLevel.CompareTo(((InfoSkill)y.GUIComponent.UserData).SkillLevel));
                if (sortingMethod == SortingMethod.SkillDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
        }

        private readonly struct InfoSkill
        {
            public readonly CharacterInfo CharacterInfo;
            public readonly float SkillLevel;

            public InfoSkill(CharacterInfo characterInfo, float skillLevel)
            {
                CharacterInfo = characterInfo;
                SkillLevel = skillLevel;
            }
        }
        
        private void CreateCharacterFrame(CharacterInfo characterInfo, GUIListBox listBox)
        {
            Skill skill = null;
            Color? jobColor = null;
            if (characterInfo.Job != null)
            {
                skill = characterInfo.Job?.PrimarySkill ?? characterInfo.Job.GetSkills().OrderByDescending(s => s.Level).FirstOrDefault();
                jobColor = characterInfo.Job.Prefab.UIColor;
            }

            GUIFrame frame = new GUIFrame(new RectTransform(new Point(listBox.Content.Rect.Width, (int)(GUI.yScale * 55)), parent: listBox.Content.RectTransform), "ListBoxElement")
            {
                UserData = new InfoSkill(characterInfo, skill?.Level ?? 0.0f)
            };
            GUILayoutGroup mainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), frame.RectTransform, anchor: Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            float portraitWidth = (0.8f * mainGroup.Rect.Height) / mainGroup.Rect.Width;
            var icon = new GUICustomComponent(new RectTransform(new Vector2(portraitWidth, 0.8f), mainGroup.RectTransform),
                onDraw: (sb, component) => characterInfo.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
            {
                CanBeFocused = false
            };

            GUILayoutGroup nameAndJobGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f - portraitWidth, 0.8f), mainGroup.RectTransform)) { CanBeFocused = false };
            GUILayoutGroup nameGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), nameAndJobGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { CanBeFocused = false };
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(Vector2.One, nameGroup.RectTransform),
                listBox == hireableList ? characterInfo.OriginalName : characterInfo.Name,
                textColor: jobColor, textAlignment: Alignment.BottomLeft)
            {
                CanBeFocused = false
            };
            nameBlock.Text = ToolBox.LimitString(nameBlock.Text, nameBlock.Font, nameBlock.Rect.Width);

            GUITextBlock jobBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), nameAndJobGroup.RectTransform),
                characterInfo.Job.Name, textColor: Color.White, font: GUIStyle.SmallFont, textAlignment: Alignment.TopLeft)
            {
                CanBeFocused = false
            };
            jobBlock.Text = ToolBox.LimitString(jobBlock.Text, jobBlock.Font, jobBlock.Rect.Width);

            float width =  0.6f / 3;
            if (characterInfo.Job != null && skill != null)
            {
                GUILayoutGroup skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(width, 0.6f), mainGroup.RectTransform), isHorizontal: true);
                float iconWidth = (float)skillGroup.Rect.Height / skillGroup.Rect.Width;
                GUIImage skillIcon = new GUIImage(new RectTransform(Vector2.One, skillGroup.RectTransform, scaleBasis: ScaleBasis.Smallest), skill.Icon, scaleToFit: true)
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
                new GUITextBlock(new RectTransform(new Vector2(width, 1.0f), mainGroup.RectTransform),
                    TextManager.FormatCurrency(characterInfo.Salary),
                    textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
            }
            else
            {
                // Just a bit of padding to make list layouts similar
                new GUIFrame(new RectTransform(new Vector2(width, 1.0f), mainGroup.RectTransform), style: null) { CanBeFocused = false };
            }

            if (listBox == hireableList)
            {
                var hireButton = new GUIButton(new RectTransform(new Vector2(width, 0.9f), mainGroup.RectTransform), style: "CrewManagementAddButton")
                {
                    ClickSound = GUISoundType.Cart,
                    UserData = characterInfo,
                    Enabled = HasPermission,
                    OnClicked = (b, o) => AddPendingHire(o as CharacterInfo)
                };
                hireButton.OnAddedToGUIUpdateList += (GUIComponent btn) =>
                {
                    if (PendingHires.Count + campaign.CrewManager.GetCharacterInfos().Count() >= CrewManager.MaxCrewSize)
                    {
                        if (btn.Enabled)
                        {
                            btn.ToolTip = TextManager.Get("canthiremorecharacters");
                            btn.Enabled = false;
                        }
                    }
                    else if (!btn.Enabled)
                    {
                        btn.ToolTip = string.Empty;
                        btn.Enabled = HasPermission;
                    }
                };

            }
            else if (listBox == pendingList)
            {
                new GUIButton(new RectTransform(new Vector2(width, 0.9f), mainGroup.RectTransform), style: "CrewManagementRemoveButton")
                {
                    ClickSound = GUISoundType.Cart,
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
                            new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });
                        confirmDialog.Buttons[0].UserData = (CharacterInfo)obj;
                        confirmDialog.Buttons[0].OnClicked = FireCharacter;
                        confirmDialog.Buttons[0].OnClicked += confirmDialog.Close;
                        confirmDialog.Buttons[1].OnClicked = confirmDialog.Close;
                        return true;
                    }
                };
            }

            if (listBox == pendingList || listBox == crewList)
            {
                nameBlock.RectTransform.Resize(new Point(nameBlock.Rect.Width - nameBlock.Rect.Height, nameBlock.Rect.Height));
                nameBlock.Text = ToolBox.LimitString(nameBlock.Text, nameBlock.Font, nameBlock.Rect.Width);
                nameBlock.RectTransform.Resize(new Point((int)(nameBlock.Padding.X + nameBlock.TextSize.X + nameBlock.Padding.Z), nameBlock.Rect.Height));
                Point size = new Point((int)(0.7f * nameBlock.Rect.Height));
                new GUIImage(new RectTransform(size, nameGroup.RectTransform), "EditIcon") { CanBeFocused = false };
                size = new Point(3 * mainGroup.AbsoluteSpacing + icon.Rect.Width + nameAndJobGroup.Rect.Width, mainGroup.Rect.Height);
                new GUIButton(new RectTransform(size, frame.RectTransform) { RelativeOffset = new Vector2(0.025f) }, style: null)
                {
                    Enabled = HasPermission,
                    ToolTip = TextManager.GetWithVariable("campaigncrew.givenicknametooltip", "[mouseprimary]", TextManager.Get($"input.{(PlayerInput.MouseButtonsSwapped() ? "rightmouse" : "leftmouse")}")), 
                    UserData = characterInfo,
                    OnClicked = CreateRenamingComponent
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
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), "");
            string name = listBox == hireableList ? characterInfo.OriginalName : characterInfo.Name;
            nameBlock.Text = ToolBox.LimitString(name, nameBlock.Font, nameBlock.Rect.Width);

            if (characterInfo.HasSpecifierTags)
            {
                var menuCategoryVar = characterInfo.Prefab.MenuCategoryVar;
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get(menuCategoryVar));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), TextManager.Get(characterInfo.ReplaceVars($"[{menuCategoryVar}]")));
            }
            if (characterInfo.Job is Job job)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("tabmenu.job"));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), job.Name);
            }
            if (characterInfo.PersonalityTrait is NPCPersonalityTrait trait)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("PersonalityTrait"));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), trait.DisplayName);
            }
            infoLabelGroup.Recalculate();
            infoValueGroup.Recalculate();

            new GUIImage(new RectTransform(new Vector2(1.0f, 0.05f), mainGroup.RectTransform), "HorizontalLine");

            // Skills
            GUILayoutGroup skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.475f), mainGroup.RectTransform), isHorizontal: true);
            GUILayoutGroup skillNameGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1.0f), skillGroup.RectTransform));
            GUILayoutGroup skillLevelGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1.0f), skillGroup.RectTransform));
            var characterSkills = characterInfo.Job.GetSkills();
            blockHeight = 1.0f / characterSkills.Count();
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
            if (PendingHires.Count + campaign.CrewManager.GetCharacters().Count() >= CrewManager.MaxCrewSize)
            {
                return false;
            }

            hireableList.Content.RemoveChild(hireableList.Content.FindChild(c => ((InfoSkill)c.UserData).CharacterInfo == characterInfo));
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
            pendingList.Content.RemoveChild(pendingList.Content.FindChild(c => ((InfoSkill)c.UserData).CharacterInfo == characterInfo));
            pendingList.UpdateScrollBarSize();

            // Server will reset the names to originals in multiplayer
            if (!GameMain.IsMultiplayer) { characterInfo?.ResetName(); }

            if (campaign.Map.CurrentLocation.HireManager.AvailableCharacters.Any(info => info.GetIdentifierUsingOriginalName() == characterInfo.GetIdentifierUsingOriginalName()) &&
                hireableList.Content.Children.None(c => c.UserData is InfoSkill userData && userData.CharacterInfo.GetIdentifierUsingOriginalName() == characterInfo.GetIdentifierUsingOriginalName()))
            {
                CreateCharacterFrame(characterInfo, hireableList);
                SortCharacters(hireableList, (SortingMethod)sortingDropDown.SelectedItemData);
                hireableList.UpdateScrollBarSize();
            }
            
            if (setTotalHireCost) { SetTotalHireCost(); }
            if (createNetworkMessage) { SendCrewState(true); }
            return true;
        }

        private bool RemoveAllPendingHires(bool createNetworkMessage = true)
        {
            pendingList.Content.Children.ToList().ForEach(c => RemovePendingHire(((InfoSkill)c.UserData).CharacterInfo, setTotalHireCost: false, createNetworkMessage));
            SetTotalHireCost();
            return true;
        }

        private void SetTotalHireCost()
        {
            if (pendingList == null || totalBlock == null || validateHiresButton == null) { return; }
            int total = 0;
            pendingList.Content.Children.ForEach(c =>
            {
                total += ((InfoSkill)c.UserData).CharacterInfo.Salary;
            });
            totalBlock.Text = TextManager.FormatCurrency(total);
            bool enoughMoney = campaign == null || campaign.CanAfford(total);
            totalBlock.TextColor = enoughMoney ? Color.White : Color.Red;
            validateHiresButton.Enabled = enoughMoney && HasPermission && pendingList.Content.RectTransform.Children.Any();
        }

        public bool ValidateHires(List<CharacterInfo> hires, bool createNetworkEvent = false)
        {
            if (hires == null || hires.None()) { return false; }

            List<CharacterInfo> nonDuplicateHires = new List<CharacterInfo>();
            hires.ForEach(hireInfo =>
            {
                if(campaign.CrewManager.GetCharacterInfos().None(crewInfo => crewInfo.IsNewHire && crewInfo.GetIdentifierUsingOriginalName() == hireInfo.GetIdentifierUsingOriginalName()))
                {
                    nonDuplicateHires.Add(hireInfo);
                }
            });

            if (nonDuplicateHires.None()) { return false; }

            int total = nonDuplicateHires.Aggregate(0, (total, info) => total + info.Salary);

            if (!campaign.CanAfford(total)) { return false; }

            bool atLeastOneHired = false;
            foreach (CharacterInfo ci in nonDuplicateHires)
            {
                if (campaign.TryHireCharacter(campaign.Map.CurrentLocation, ci))
                {
                    atLeastOneHired = true;
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
                    new LocalizedString[] { TextManager.Get("Ok") });
                dialog.Buttons[0].OnClicked += dialog.Close;
            }

            if (createNetworkEvent)
            {
                SendCrewState(true, validateHires: true);
            }

            return false;
        }

        private bool CreateRenamingComponent(GUIButton button, object userData)
        {
            if (!HasPermission || !(userData is CharacterInfo characterInfo)) { return false; }
            var outerGlowFrame = new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), parentComponent.RectTransform, Anchor.Center),
                style: "OuterGlow", color: Color.Black * 0.7f);
            var frame = new GUIFrame(new RectTransform(new Vector2(0.33f, 0.4f), outerGlowFrame.RectTransform, anchor: Anchor.Center)
            {
                MaxSize = new Point(400, 300).Multiply(GUI.Scale)
            });
            var layoutGroup = new GUILayoutGroup(new RectTransform((frame.Rect.Size - GUIStyle.ItemFrameMargin).Multiply(new Vector2(0.75f, 1.0f)), frame.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), layoutGroup.RectTransform), TextManager.Get("campaigncrew.givenickname"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center, wrap: true);
            var groupElementSize = new Vector2(1.0f, 0.25f);
            var nameBox = new GUITextBox(new RectTransform(groupElementSize, layoutGroup.RectTransform))
            {
                MaxTextLength = Client.MaxNameLength
            };
            new GUIButton(new RectTransform(groupElementSize, layoutGroup.RectTransform), text: TextManager.Get("confirm"))
            {
                OnClicked = (button, userData) =>
                {
                    if (RenameCharacter(characterInfo, nameBox.Text?.Trim()))
                    {
                        parentComponent.RemoveChild(outerGlowFrame);
                        return true;
                    }
                    else
                    {
                        nameBox.Flash(color: Color.Red);
                        return false;
                    }
                    
                }
            };
            new GUIButton(new RectTransform(groupElementSize, layoutGroup.RectTransform), text: TextManager.Get("cancel"))
            {
                OnClicked = (button, userData) =>
                {
                    parentComponent.RemoveChild(outerGlowFrame);
                    return true;
                }
            };
            layoutGroup.Recalculate();
            return true;
        }

        public bool RenameCharacter(CharacterInfo characterInfo, string newName)
        {
            if (characterInfo == null || string.IsNullOrEmpty(newName)) { return false; }
            if (newName == characterInfo.Name) { return false; }
            if (GameMain.IsMultiplayer)
            {
                SendCrewState(false, renameCharacter: (characterInfo, newName));
            }
            else
            {
                var crewComponent = crewList.Content.FindChild(c => ((InfoSkill)c.UserData).CharacterInfo == characterInfo);
                if (crewComponent != null)
                {
                    crewList.Content.RemoveChild(crewComponent);
                    campaign.CrewManager.RenameCharacter(characterInfo, newName);
                    CreateCharacterFrame(characterInfo, crewList);
                    SortCharacters(crewList, SortingMethod.JobAsc);
                }
                else
                {
                    var pendingComponent = pendingList.Content.FindChild(c => ((InfoSkill)c.UserData).CharacterInfo == characterInfo);
                    if (pendingComponent != null)
                    {
                        pendingList.Content.RemoveChild(pendingComponent);
                        campaign.Map.CurrentLocation.HireManager.RenameCharacter(characterInfo, newName);
                        CreateCharacterFrame(characterInfo, pendingList);
                        SortCharacters(pendingList, SortingMethod.JobAsc);
                        SetTotalHireCost();
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return true;
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
            else
            {
                playerBalanceElement = CampaignUI.UpdateBalanceElement(playerBalanceElement);
            }

            (GUIComponent highlightedFrame, CharacterInfo highlightedInfo) = FindHighlightedCharacter(GUI.MouseOn);
            if (highlightedFrame != null && highlightedInfo != null)
            {
                if (characterPreviewFrame == null || highlightedInfo != characterPreviewFrame.UserData)
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
                        SelectCharacter(listBox, highlightedFrame as GUIFrame, highlightedInfo);
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

            static (GUIComponent GuiComponent, CharacterInfo CharacterInfo) FindHighlightedCharacter(GUIComponent c)
            {
                if (c == null)
                {
                    return default;
                }
                if (c.UserData is InfoSkill highlightedData)
                {
                    return (c, highlightedData.CharacterInfo);
                }
                if (c.Parent != null)
                {
                    if (c.Parent is GUIListBox)
                    {
                        return default;
                    }
                    return FindHighlightedCharacter(c.Parent);
                }
                return default;
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
                CharacterInfo match = location.HireManager.AvailableCharacters.Find(info => info.GetIdentifierUsingOriginalName() == identifier);
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
        /// <param name="renameCharacter">When not null tell the server to rename this character. Item1 is the character to rename, Item2 is the new name, Item3 indicates whether the renamed character is already a part of the crew.</param>
        /// <param name="firedCharacter">When not null tell the server to fire this character</param>
        /// <param name="validateHires">When set to true will tell the server to validate pending hires</param>
        public void SendCrewState(bool updatePending, (CharacterInfo info, string newName) renameCharacter = default, CharacterInfo firedCharacter = null, bool validateHires = false)
        {
            if (campaign is MultiPlayerCampaign)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ClientPacketHeader.CREW);
                
                msg.WriteBoolean(updatePending);
                if (updatePending)
                {
                    msg.WriteUInt16((ushort)PendingHires.Count);
                    foreach (CharacterInfo pendingHire in PendingHires)
                    {
                        msg.WriteInt32(pendingHire.GetIdentifierUsingOriginalName());
                    }
                }

                msg.WriteBoolean(validateHires);

                bool validRenaming = renameCharacter.info != null && !string.IsNullOrEmpty(renameCharacter.newName);
                msg.WriteBoolean(validRenaming);
                if (validRenaming)
                {
                    int identifier = renameCharacter.info.GetIdentifierUsingOriginalName();
                    msg.WriteInt32(identifier);
                    msg.WriteString(renameCharacter.newName);
                    bool existingCrewMember = campaign.CrewManager?.GetCharacterInfos().Any(ci => ci.GetIdentifierUsingOriginalName() == identifier) ?? false;
                    msg.WriteBoolean(existingCrewMember);
                }

                msg.WriteBoolean(firedCharacter != null);
                if (firedCharacter != null)
                {
                    msg.WriteInt32(firedCharacter.GetIdentifier());
                }

                GameMain.Client.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
            }
        }
    }
}
