﻿using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using PlayerBalanceElement = Barotrauma.CampaignUI.PlayerBalanceElement;

namespace Barotrauma
{
    /// <summary>
    /// The "HR manager" UI, which is used to hire/fire characters and rename crewmates.
    /// </summary>
    class HRManagerUI
    {
        private CampaignMode campaign => campaignUI.Campaign;
        private readonly CampaignUI campaignUI;
        private readonly GUIComponent parentComponent;

        private GUIComponent pendingAndCrewPanel;
        private GUIListBox hireableList, pendingList, crewList;
        private GUIFrame characterPreviewFrame;
        private GUIDropDown sortingDropDown;
        private GUITextBlock totalBlock;
        private GUIButton validateHiresButton;
        private GUIButton clearAllButton;

        private PlayerBalanceElement? playerBalanceElement;

        private List<CharacterInfo> PendingHires => campaign.Map?.CurrentLocation?.HireManager?.PendingHires;


        private bool wasReplacingPermanentlyDeadCharacter;
        /// <summary>
        /// Is the player hiring a new character for themselves instead of bots for the crew?
        /// The window can only be used for one of these purposes at the same time.
        /// </summary>
        private static bool ReplacingPermanentlyDeadCharacter => 
            GameMain.NetworkMember?.ServerSettings is { RespawnMode: RespawnMode.Permadeath, IronmanMode: false } &&
            GameMain.Client?.CharacterInfo is { PermanentlyDead: true };
        
        private static bool ReserveBenchEnabled => GameMain.GameSession?.Campaign is MultiPlayerCampaign;

        private bool hadPermissionToHire;
        private static bool HasPermissionToHire => ReplacingPermanentlyDeadCharacter ?
            GameMain.NetworkMember?.ServerSettings.ReplaceCostPercentage <= 0 || CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageMoney) || CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageHires) :
            CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageHires);

        private Point resolutionWhenCreated;

        private bool needsHireableRefresh;

        private enum SortingMethod
        {
            AlphabeticalAsc,
            JobAsc,
            PriceAsc,
            PriceDesc,
            SkillAsc,
            SkillDesc
        }

        public HRManagerUI(CampaignUI campaignUI, GUIComponent parentComponent)
        {
            this.campaignUI = campaignUI;
            this.parentComponent = parentComponent;

            CreateUI();
            UpdateLocationView(campaignUI.Campaign.Map.CurrentLocation, true);

            campaignUI.Campaign.Map.OnLocationChanged.RegisterOverwriteExisting(
                "CrewManagement.UpdateLocationView".ToIdentifier(), 
                (locationChangeInfo) => UpdateLocationView(locationChangeInfo.NewLocation, true, locationChangeInfo.PrevLocation));
            Reputation.OnAnyReputationValueChanged.RegisterOverwriteExisting(
                "CrewManagement.UpdateLocationView".ToIdentifier(), _ => needsHireableRefresh = true);

            hadPermissionToHire = HasPermissionToHire;
            wasReplacingPermanentlyDeadCharacter = ReplacingPermanentlyDeadCharacter;
        }

        public void RefreshUI()
        {
            RefreshCrewFrames(hireableList);
            RefreshCrewFrames(crewList);
            RefreshCrewFrames(pendingList);
            if (clearAllButton != null) { clearAllButton.Enabled = HasPermissionToHire; }
            hadPermissionToHire = HasPermissionToHire;
            wasReplacingPermanentlyDeadCharacter = ReplacingPermanentlyDeadCharacter;
        }

        private void RefreshCrewFrames(GUIListBox listBox)
        {
            if (listBox == null) { return; }
            listBox.CanBeFocused = HasPermissionToHire;
            foreach (GUIComponent child in listBox.Content.Children)
            {
                if (child.FindChild(c => c is GUIButton && c.UserData is CharacterInfo, true) is GUIButton buyButton)
                {
                    CharacterInfo characterInfo = buyButton.UserData as CharacterInfo;
                    buyButton.Enabled = 
                        //"normal buying" is disabled when replacing a dead character
                        !ReplacingPermanentlyDeadCharacter && 
                        HasPermissionToHire &&
                        EnoughReputationToHire(characterInfo) && campaign.CanAffordNewCharacter(characterInfo);
                    foreach (GUITextBlock text in child.GetAllChildren<GUITextBlock>())
                    {
                        text.TextColor = new Color(text.TextColor, buyButton.Enabled ? 1.0f : 0.6f);
                    }
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

            pendingAndCrewPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 13.25f / 14.0f), pendingAndCrewMainGroup.RectTransform)
            {
                MaxSize = new Point(panelMaxWidth, campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Rect.Height)
            });           

            var pendingAndCrewGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), anchor: Anchor.Center,
                parent: pendingAndCrewPanel.RectTransform));

            float height = 0.05f;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), TextManager.Get("campaigncrew.pending"), font: GUIStyle.SubHeadingFont);
            pendingList = new GUIListBox(new RectTransform(new Vector2(1.0f, 8 * height), pendingAndCrewGroup.RectTransform))
            {
                Spacing = 1
            };

            var crewHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, height), pendingAndCrewGroup.RectTransform), TextManager.Get("campaignmenucrew"), font: GUIStyle.SubHeadingFont);

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), crewHeader.RectTransform, Anchor.CenterRight), string.Empty, textAlignment: Alignment.CenterRight)
            {
                TextGetter = () => 
                {
                    int crewSize = campaign?.CrewManager?.GetCharacterInfos()?.Count() ?? 0;
                    return $"{crewSize}/{CrewManager.MaxCrewSize}";
                }
            };
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
                OnClicked = (b, o) => ValidateHires(PendingHires, createNetworkEvent: true)
            };
            clearAllButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), group.RectTransform), text: TextManager.Get("campaignstore.clearall"))
            {
                ClickSound = GUISoundType.Cart,
                ForceUpperCase = ForceUpperCase.Yes,
                Enabled = HasPermissionToHire,
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
                    PendingHires?.ForEach(ci => AddPendingHire(ci, createNetworkMessage: false));
                }
                SetTotalHireCost();
            }
            UpdateCrew();
        }

        /// <summary>
        /// This will simply update each of the HR view lists (hireables, pending hires, and crew) from the most up to date information.
        /// It is a sane version of UpdateLocationView that won't break things even if used outside of whatever arbitrary conditions that one was made for.
        /// </summary>
        public void RefreshHRView()
        {
            if (campaign?.CurrentLocation is not Location currentLocation)
            {
                return;
            }
            
            if (characterPreviewFrame != null)
            {
                characterPreviewFrame.Parent?.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }
            
            UpdateHireables(currentLocation);
            
            if (pendingList != null)
            {
                pendingList.Content.ClearChildren();
                PendingHires?.ForEach(ci => AddPendingHire(ci, checkCrewSizeLimit: false, createNetworkMessage: false)); // don't check limits here, just display the data as it is
                SetTotalHireCost();
            }
            
            UpdateCrew();
        }

        public void UpdateHireables()
        {
            UpdateHireables(campaign?.CurrentLocation);
        }

        private void UpdateHireables(Location location)
        {
            if (hireableList == null) { return; }            
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
                    if (c == null || PendingHires.Contains(c)) { continue; }
                    CreateCharacterFrame(c, hireableList);
                }
            }
            sortingDropDown.SelectItem(SortingMethod.JobAsc);
            hireableList.UpdateScrollBarSize();            
        }

        public void SetHireables(Location location, List<CharacterInfo> availableHires)
        {
            HireManager hireManager = location.HireManager;
            if (hireManager == null) { return; }
            int hireVal = hireManager.AvailableCharacters.Aggregate(0, (curr, hire) => curr + hire.ID);
            int newVal = availableHires.Aggregate(0, (curr, hire) => curr + hire.ID);
            if (hireVal != newVal)
            {
                location.HireManager.AvailableCharacters = availableHires;
                UpdateHireables(location);
            }
        }

        public void UpdateCrew()
        {
            crewList.Content.Children.ToList().ForEach(c => crewList.Content.RemoveChild(c));
            foreach (CharacterInfo ci in GameMain.GameSession.CrewManager.GetCharacterInfos(includeReserveBench: true))
            {
                // CrewManager is used to store info on all characters including players, but we only want bots in HR
                if (ci.Character != null && (ci.Character.IsRemotePlayer || !ci.Character.IsBot)) { continue; }
                CreateCharacterFrame(ci, crewList);
            }
            SortCharacters(crewList, SortingMethod.JobAsc);
            crewList.UpdateScrollBarSize();
        }

        private void SortCharacters(GUIListBox list, SortingMethod sortingMethod)
        {
            if (sortingMethod == SortingMethod.AlphabeticalAsc)
            {
                list.Content.RectTransform.SortChildren((x, y) =>
                    CompareReputationRequirement(x.GUIComponent, y.GUIComponent) ??
                    ((InfoSkill)x.GUIComponent.UserData).CharacterInfo.Name.CompareTo(((InfoSkill)y.GUIComponent.UserData).CharacterInfo.Name));
            }
            else if (sortingMethod == SortingMethod.JobAsc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    CompareReputationRequirement(x.GUIComponent, y.GUIComponent) ??
                    string.Compare(((InfoSkill)x.GUIComponent.UserData).CharacterInfo.Job.Name.Value, ((InfoSkill)y.GUIComponent.UserData).CharacterInfo.Job.Name.Value, StringComparison.Ordinal));
            }
            else if (sortingMethod == SortingMethod.PriceAsc || sortingMethod == SortingMethod.PriceDesc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    CompareReputationRequirement(x.GUIComponent, y.GUIComponent) ??
                    ((InfoSkill)x.GUIComponent.UserData).CharacterInfo.Salary.CompareTo(((InfoSkill)y.GUIComponent.UserData).CharacterInfo.Salary));
                if (sortingMethod == SortingMethod.PriceDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
            else if (sortingMethod == SortingMethod.SkillAsc || sortingMethod == SortingMethod.SkillDesc)
            {
                SortCharacters(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    CompareReputationRequirement(x.GUIComponent, y.GUIComponent) ??
                    ((InfoSkill)x.GUIComponent.UserData).SkillLevel.CompareTo(((InfoSkill)y.GUIComponent.UserData).SkillLevel));
                if (sortingMethod == SortingMethod.SkillDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
            
            // Always apply this in the end to group by reserve bench status (does nothing if there are no reserve benched bots)
            list.Content.RectTransform.SortChildren((x, y) =>
                    ((InfoSkill)x.GUIComponent.UserData).CharacterInfo.BotStatus.CompareTo(((InfoSkill)y.GUIComponent.UserData).CharacterInfo.BotStatus));

            int? CompareReputationRequirement(GUIComponent c1, GUIComponent c2)
            {
                CharacterInfo info1 = ((InfoSkill)c1.UserData).CharacterInfo;
                CharacterInfo info2 = ((InfoSkill)c2.UserData).CharacterInfo;
                float requirement1 = EnoughReputationToHire(info1) ? 0 : info1.MinReputationToHire.reputation;
                float requirement2 = EnoughReputationToHire(info2) ? 0 : info2.MinReputationToHire.reputation;
                if (MathUtils.NearlyEqual(requirement1, 0.0f) && MathUtils.NearlyEqual(requirement2, 0.0f))
                {
                    return null;
                }
                else
                {
                    return requirement1.CompareTo(requirement2);
                }
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
        
        public GUIComponent CreateCharacterFrame(CharacterInfo characterInfo, GUIListBox listBox, bool hideSalary = false)
        {
            string characterName = listBox == hireableList ? characterInfo.OriginalName : characterInfo.Name;

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
                AbsoluteSpacing = 1,
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
                characterName,
                textColor: jobColor, textAlignment: Alignment.BottomLeft)
            {
                CanBeFocused = false
            };
            const float smallColumnWidth = 0.6f / 3;
            const float skillColumnWidth = smallColumnWidth * 0.7f;
            const float buttonWidth = 0.12f;
            
            GUITextBlock jobBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), nameAndJobGroup.RectTransform),
                characterInfo.Title ?? characterInfo.Job.Name, textColor: Color.White, font: GUIStyle.SmallFont, textAlignment: Alignment.TopLeft)
            {
                CanBeFocused = false
            };
            if (!characterInfo.MinReputationToHire.factionId.IsEmpty)
            {
                var faction = campaign.Factions.Find(f => f.Prefab.Identifier == characterInfo.MinReputationToHire.factionId);
                if (faction != null)
                {
                    jobBlock.TextColor = faction.Prefab.IconColor;
                }
            }
            var fullJobText = jobBlock.Text;
            if (characterInfo.Job != null && skill != null)
            {
                GUILayoutGroup skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(skillColumnWidth, 0.6f), mainGroup.RectTransform), isHorizontal: true);
                float iconWidth = (float)skillGroup.Rect.Height / skillGroup.Rect.Width;
                new GUITextBlock(new RectTransform(new Vector2(1.0f - iconWidth, 1.0f), skillGroup.RectTransform), ((int)skill.Level).ToString(), 
                    textAlignment: Alignment.CenterRight)
                {
                    Padding = Vector4.Zero,
                    CanBeFocused = false
                };
                GUIImage skillIcon = new GUIImage(new RectTransform(Vector2.One, skillGroup.RectTransform, scaleBasis: ScaleBasis.Smallest), skill.Icon, scaleToFit: true)
                {
                    CanBeFocused = false
                };
                if (jobColor.HasValue) { skillIcon.Color = jobColor.Value; }
            }
            
            if (!hideSalary)
            {
                if (listBox != crewList)
                {
                    new GUITextBlock(new RectTransform(new Vector2(smallColumnWidth, 1.0f), mainGroup.RectTransform),
                        TextManager.FormatCurrency(ReplacingPermanentlyDeadCharacter ? campaign.NewCharacterCost(characterInfo) : HireManager.GetSalaryFor(characterInfo)),
                        textAlignment: Alignment.Center)
                    {
                        CanBeFocused = false
                    };
                }
                else
                {
                    // Just a bit of padding to make list layouts similar
                    new GUIFrame(new RectTransform(new Vector2(smallColumnWidth, 1.0f), mainGroup.RectTransform), style: null) { CanBeFocused = false };
                }
            }

            if (listBox == hireableList)
            {
                var hireButton = new GUIButton(new RectTransform(new Vector2(buttonWidth, 0.9f), mainGroup.RectTransform), style: "CrewManagementAddButton")
                {
                    ToolTip = TextManager.Get(ReserveBenchEnabled ? "hirebutton.crew" : "hirebutton"),
                    ClickSound = GUISoundType.Cart,
                    UserData = characterInfo,
                    Enabled = CanHire(characterInfo) && !ReplacingPermanentlyDeadCharacter,
                    OnClicked = (b, o) =>
                    {
                        var currentCharacterInfo = (CharacterInfo)o;
                        currentCharacterInfo.BotStatus = BotStatus.PendingHireToActiveService;
                        return AddPendingHire(currentCharacterInfo);
                    }
                };
                hireButton.OnAddedToGUIUpdateList += (GUIComponent btn) =>
                {
                    if (ReplacingPermanentlyDeadCharacter)
                    {
                        return;
                    }
                    if (PendingHires.Count(ci => ci.BotStatus == BotStatus.PendingHireToActiveService) + campaign.CrewManager.GetCharacterInfos().Count() >= CrewManager.MaxCrewSize)
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
                        btn.Enabled = CanHire(characterInfo);
                    }
                };

                if (ReplacingPermanentlyDeadCharacter)
                {
                    bool canHire = CanHire(characterInfo) && campaign.CanAffordNewCharacter(characterInfo);
                    var takeoverButton = new GUIButton(new RectTransform(new Vector2(buttonWidth, 0.9f), mainGroup.RectTransform), style: "CrewManagementTakeControlButton")
                    {
                        ToolTip = canHire ? TextManager.Get("hireandtakecontrol") : TextManager.Get("hireandtakecontroldisabled"),
                        ClickSound = GUISoundType.ConfirmTransaction,
                        UserData = characterInfo,
                        Enabled = canHire,
                        OnClicked = (b, o) => 
                        {
                            if (GameMain.Client is not GameClient gameClient)
                            {
                                return false;
                            }
                            Client client = gameClient.ConnectedClients.FirstOrDefault(c => c.SessionId == gameClient.SessionId);
                            if (!campaign.TryPurchase(client, campaign.NewCharacterCost(characterInfo)))
                            {
                                return false;
                            }
                            gameClient.SendTakeOverBotRequest(characterInfo);
                            needsHireableRefresh = true;
                            campaign.ShowCampaignUI = false;
                            return true; 
                        }
                    };
                    takeoverButton.OnAddedToGUIUpdateList += (GUIComponent btn) =>
                    {
                        bool canHireCurrently = ReplacingPermanentlyDeadCharacter && CanHire(characterInfo) && campaign.CanAffordNewCharacter(characterInfo);
                        btn.ToolTip = TextManager.Get(canHireCurrently ? "hireandtakecontrol" : "hireandtakecontroldisabled");
                        btn.Visible = GameMain.GameSession is { AllowHrManagerBotTakeover: true };
                        btn.Enabled = canHireCurrently;
                    };
                }
                
                if (ReserveBenchEnabled && !ReplacingPermanentlyDeadCharacter)
                {
                    var hireToReserveBenchButton = new GUIButton(new RectTransform(new Vector2(buttonWidth, 0.9f), mainGroup.RectTransform), style: "CrewManagementAddAsReserveButton")
                    {
                        ToolTip = TextManager.Get("hirebutton.reservebench"),
                        ClickSound = GUISoundType.Cart,
                        UserData = characterInfo,
                        Enabled = CanHire(characterInfo),
                        OnClicked = (b, o) =>
                        {
                            var currentCharacterInfo = (CharacterInfo)o;
                            currentCharacterInfo.BotStatus = BotStatus.PendingHireToReserveBench;
                            return AddPendingHire(currentCharacterInfo, checkCrewSizeLimit: false);
                        }
                    };
                    hireToReserveBenchButton.OnAddedToGUIUpdateList += (GUIComponent btn) =>
                    {
                        btn.Visible = ReserveBenchEnabled;
                        btn.Enabled = CanHire(characterInfo) && !ReplacingPermanentlyDeadCharacter;
                    };
                }
            }
            else if (listBox == pendingList)
            {
                if (ReserveBenchEnabled && !ReplacingPermanentlyDeadCharacter)
                {
                    new GUIButton(new RectTransform(new Vector2(buttonWidth, 0.9f), mainGroup.RectTransform),
                        style: characterInfo.BotStatus == BotStatus.PendingHireToActiveService ? "CrewManagementReserveBenchButtonActive" : "CrewManagementReserveBenchButtonReserve")
                    {
                        UserData = characterInfo,
                        ToolTip = TextManager.Get(characterInfo.BotStatus == BotStatus.PendingHireToActiveService ? "ReserveBenchTogglePendingHire.Active" : "ReserveBenchTogglePendingHire.Reserve"),
                        Enabled = CanHire(characterInfo) && (characterInfo.BotStatus == BotStatus.PendingHireToActiveService || !ActiveServiceFull()), // note that this is a toggle
                        OnClicked = (btn, obj) =>
                        {
                            SelectCharacter(null, null, null);
                            var currentCharacterInfo = (CharacterInfo)obj;
                            GameMain.Client?.ToggleReserveBench(currentCharacterInfo, pendingHire: true);
                            return true;
                        }
                    };
                }

                new GUIButton(new RectTransform(new Vector2(buttonWidth, 0.9f), mainGroup.RectTransform), style: "CrewManagementRemoveButton")
                {
                    ClickSound = GUISoundType.Cart,
                    UserData = characterInfo,
                    Enabled = CanHire(characterInfo), // =just check user's rights
                    OnClicked = (b, o) => RemovePendingHire(o as CharacterInfo)
                };
            }
            else if (listBox == crewList && campaign != null)
            {
                if (ReserveBenchEnabled && !ReplacingPermanentlyDeadCharacter)
                {
                    new GUIButton(new RectTransform(new Vector2(buttonWidth, 0.9f), mainGroup.RectTransform),
                        style: characterInfo.BotStatus == BotStatus.ActiveService ? "CrewManagementReserveBenchButtonActive" : "CrewManagementReserveBenchButtonReserve")
                    {
                        UserData = characterInfo,
                        ToolTip = TextManager.Get(characterInfo.BotStatus == BotStatus.ActiveService ? "ReserveBenchToggle.Active" : "ReserveBenchToggle.Reserve"),
                        Enabled = CanHire(characterInfo) && (characterInfo.BotStatus == BotStatus.ActiveService || !ActiveServiceFull()), // note that this is a toggle
                        OnClicked = (btn, obj) =>
                        {
                            SelectCharacter(null, null, null);
                            var currentCharacterInfo = (CharacterInfo)obj;
                            if (currentCharacterInfo.BotStatus == BotStatus.ActiveService && // switching to reserve bench
                                characterInfo.Character != null) // may not have a Character to remove if not spawned this round
                            {
                                GameMain.GameSession.CrewManager.RemoveCharacter(characterInfo.Character, removeInfo: true, resetCrewListIndex: true);
                            }
                            GameMain.Client?.ToggleReserveBench(currentCharacterInfo); // update changes to server
                            return true;
                        }
                    };
                }
                
                var cm = GameMain.GameSession.CrewManager;
                // Can't fire if there's only one character in active service
                var fireButtonEnabled = HasPermissionToHire && (characterInfo.IsOnReserveBench ||
                                        (cm.GetCharacterInfos().Contains(characterInfo) && cm.GetCharacterInfos().Count() > 1));
                new GUIButton(new RectTransform(new Vector2(buttonWidth, 0.9f), mainGroup.RectTransform), style: "CrewManagementFireButton")
                {
                    UserData = characterInfo,
                    Enabled = fireButtonEnabled,
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
            else
            {
                if (ReserveBenchEnabled && characterInfo.IsOnReserveBench) // Applies to unspecified listings like the death prompt and the bot list after permadeath
                {
                    new GUIImage(new RectTransform(new Vector2(smallColumnWidth / 2, 0.6f), mainGroup.RectTransform), style: "CrewManagementReserveBenchIconReserve")
                    {
                        ToolTip = TextManager.Get("ReserveBenchStatus.Reserve.WillSpawn")
                    };
                }
                else
                {
                    new GUILayoutGroup(new RectTransform(new Vector2(smallColumnWidth / 2, 0.6f), mainGroup.RectTransform)) { CanBeFocused = false };
                }
            }

            if (listBox == pendingList || listBox == crewList)
            {
                nameBlock.RectTransform.Resize(new Point(nameBlock.Rect.Width - nameBlock.Rect.Height, nameBlock.Rect.Height));
                nameBlock.Text = ToolBox.LimitString(characterName, nameBlock.Font, nameBlock.Rect.Width);
                nameBlock.RectTransform.Resize(new Point((int)(nameBlock.Padding.X + nameBlock.TextSize.X + nameBlock.Padding.Z), nameBlock.Rect.Height));
                Point size = new Point((int)(0.7f * nameBlock.Rect.Height));
                new GUIImage(new RectTransform(size, nameGroup.RectTransform), "EditIcon") { CanBeFocused = false };
                size = new Point(3 * mainGroup.AbsoluteSpacing + icon.Rect.Width + nameAndJobGroup.Rect.Width, mainGroup.Rect.Height);
                new GUIButton(new RectTransform(size, frame.RectTransform) { RelativeOffset = new Vector2(0.025f) }, style: null)
                {
                    Enabled = CanHire(characterInfo),
                    ToolTip = TextManager.GetWithVariable("campaigncrew.givenicknametooltip", "[mouseprimary]", PlayerInput.PrimaryMouseLabel),
                    UserData = characterInfo,
                    OnClicked = CreateRenamingComponent
                };
            }

            //recalculate everything and truncate texts if needed
            mainGroup.Recalculate();
            nameBlock.Text = ToolBox.LimitString(characterName, nameBlock.Font, nameBlock.Rect.Width);
            jobBlock.Text = ToolBox.LimitString(fullJobText, jobBlock.Font, jobBlock.Rect.Width);
            if (jobBlock.Text != fullJobText)
            {
                jobBlock.ToolTip = fullJobText;
                jobBlock.CanBeFocused = true;
            }

            bool CanHire(CharacterInfo thisCharacterInfo)
            {
                if (!HasPermissionToHire) { return false; }
                return EnoughReputationToHire(thisCharacterInfo);
            }

            return frame;
        }
        
        /// <summary>
        /// Is there (going to be) no space left in active service?
        /// </summary>
        private bool ActiveServiceFull()
        {
            int pendingHireCount = PendingHires?.Count(ci => ci.BotStatus == BotStatus.PendingHireToActiveService) ?? 0;
            return pendingHireCount + campaign.CrewManager.GetCharacterInfos().Count() >= CrewManager.MaxCrewSize;
        }

        private bool EnoughReputationToHire(CharacterInfo characterInfo)
        {
            if (characterInfo.MinReputationToHire.factionId != Identifier.Empty)
            {
                if (MathF.Round(campaign.GetReputation(characterInfo.MinReputationToHire.factionId)) < characterInfo.MinReputationToHire.reputation)
                {
                    return false;
                }
            }
            return true;
        }

        private void CreateCharacterPreviewFrame(GUIListBox listBox, GUIFrame characterFrame, CharacterInfo characterInfo)
        {
            Pivot pivot = listBox == hireableList ? Pivot.TopLeft : Pivot.TopRight;
            Point absoluteOffset = new Point(
                pivot == Pivot.TopLeft ? listBox.Parent.Parent.Rect.Right + 5 : listBox.Parent.Parent.Rect.Left - 5,
                characterFrame.Rect.Top);
            Point frameSize = new Point(GUI.IntScale(300), GUI.IntScale(350));
            if (GameMain.GraphicsHeight - (absoluteOffset.Y + frameSize.Y) < 0)
            {
                pivot = listBox == hireableList ? Pivot.BottomLeft : Pivot.BottomRight;
                absoluteOffset.Y = characterFrame.Rect.Bottom;
            }
            characterPreviewFrame = new GUIFrame(new RectTransform(frameSize, parent: campaignUI.GetTabContainer(CampaignMode.InteractionType.Crew).Parent.RectTransform, pivot: pivot)
                {
                    AbsoluteOffset = absoluteOffset
                }, style: "InnerFrame")
            {
                UserData = characterInfo
            };
            GUILayoutGroup mainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), characterPreviewFrame.RectTransform, anchor: Anchor.Center))
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            // Character info
            GUILayoutGroup infoGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.475f), mainGroup.RectTransform), isHorizontal: true);
            GUILayoutGroup infoLabelGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), infoGroup.RectTransform)) { Stretch = true };
            GUILayoutGroup infoValueGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), infoGroup.RectTransform)) { Stretch = true };
            float blockHeight = 1.0f / 4;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("name"), textColor: GUIStyle.TextColorBright);
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), "");
            string name = listBox == hireableList ? characterInfo.OriginalName : characterInfo.Name;
            nameBlock.Text = ToolBox.LimitString(name, nameBlock.Font, nameBlock.Rect.Width);

            if (characterInfo.HasSpecifierTags)
            {
                var menuCategoryVar = characterInfo.Prefab.MenuCategoryVar;
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get(menuCategoryVar), textColor: GUIStyle.TextColorBright);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), TextManager.Get(characterInfo.ReplaceVars($"[{menuCategoryVar}]")));
            }
            if (characterInfo.Job is Job job)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("tabmenu.job"), textColor: GUIStyle.TextColorBright);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform), job.Name);
            }
            if (characterInfo.PersonalityTrait is NPCPersonalityTrait trait)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform), TextManager.Get("PersonalityTrait"), textColor: GUIStyle.TextColorBright);
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
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), skillNameGroup.RectTransform), TextManager.Get("SkillName." + skill.Identifier), font: GUIStyle.SmallFont);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), skillLevelGroup.RectTransform), ((int)skill.Level).ToString(), textAlignment: Alignment.Right);
            }

            if (characterInfo.MinReputationToHire.reputation > 0.0f)
            {
                var repStr = TextManager.GetWithVariables(
                    "campaignstore.reputationrequired",
                    ("[amount]", ((int)characterInfo.MinReputationToHire.reputation).ToString()),
                    ("[faction]", TextManager.Get("faction." + characterInfo.MinReputationToHire.factionId).Value));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), mainGroup.RectTransform),
                    repStr, textColor: !EnoughReputationToHire(characterInfo) ? GUIStyle.Orange : GUIStyle.Green, 
                    font: GUIStyle.SmallFont, wrap: true, textAlignment: Alignment.Center);
            }
            mainGroup.Recalculate();
            characterPreviewFrame.RectTransform.MinSize = 
                new Point(0, (int)(mainGroup.Children.Sum(c => c.Rect.Height + mainGroup.Rect.Height * mainGroup.RelativeSpacing) / mainGroup.RectTransform.RelativeSize.Y));
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

        private bool AddPendingHire(CharacterInfo characterInfo, bool checkCrewSizeLimit = true, bool createNetworkMessage = true)
        {
            if (checkCrewSizeLimit && characterInfo.BotStatus == BotStatus.PendingHireToActiveService && ActiveServiceFull())
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
            var infos = pendingList.Content.Children.Select(static c => ((InfoSkill)c.UserData).CharacterInfo).ToArray();
            int total = HireManager.GetSalaryFor(infos);
            totalBlock.Text = TextManager.FormatCurrency(total);
            bool enoughMoney = campaign == null || campaign.CanAfford(total);
            totalBlock.TextColor = enoughMoney ? Color.White : Color.Red;
            validateHiresButton.Enabled = enoughMoney && HasPermissionToHire && pendingList.Content.RectTransform.Children.Any();
        }

        public bool ValidateHires(List<CharacterInfo> hires, bool takeMoney = true, bool createNetworkEvent = false, bool createNotification = true)
        {
            if (hires == null || hires.None()) { return false; }

            List<CharacterInfo> nonDuplicateHires = new List<CharacterInfo>();
            hires.ForEach(hireInfo =>
            {
                if (campaign.CrewManager.GetCharacterInfos(includeReserveBench: true).None(crewInfo => crewInfo.IsNewHire && crewInfo.GetIdentifierUsingOriginalName() == hireInfo.GetIdentifierUsingOriginalName()))
                {
                    nonDuplicateHires.Add(hireInfo);
                }
            });

            if (nonDuplicateHires.None()) { return false; }

            if (takeMoney)
            {
                int total = HireManager.GetSalaryFor(nonDuplicateHires);
                if (!campaign.CanAfford(total)) { return false; }
            }

            bool atLeastOneHiredToActiveDuty = false;
            bool atLeastOneHiredToReserveBench = false;
            foreach (CharacterInfo ci in nonDuplicateHires)
            {
                bool toReserveBench = ci.BotStatus == BotStatus.PendingHireToReserveBench;
                if (campaign.TryHireCharacter(campaign.Map.CurrentLocation, ci, takeMoney: takeMoney))
                {
                    if (toReserveBench)
                    {
                        atLeastOneHiredToReserveBench = true;
                    }
                    else
                    {
                        atLeastOneHiredToActiveDuty = true;
                    }
                }
                else
                {
                    break;
                }
            }

            if (atLeastOneHiredToActiveDuty || atLeastOneHiredToReserveBench)
            {
                UpdateLocationView(campaign.Map.CurrentLocation, true);
                SelectCharacter(null, null, null);
                if (createNotification)
                {
                    LocalizedString msg = string.Empty;
                    if (atLeastOneHiredToActiveDuty) 
                    { 
                        msg += TextManager.GetWithVariable("crewhiredmessage", "[location]", campaignUI?.Campaign?.Map?.CurrentLocation?.DisplayName); 
                    }
                    if (atLeastOneHiredToReserveBench) 
                    {
                        if (!msg.IsNullOrEmpty()) { msg += "\n\n"; }
                        msg += GameMain.NetworkMember?.ServerSettings is { RespawnMode: RespawnMode.Permadeath, IronmanMode: false } ?
                            TextManager.Get("crewhiredmessage.reservebench.permadeath") :
                            TextManager.Get( "crewhiredmessage.reservebench");
                    }

                    var dialog = new GUIMessageBox(
                        TextManager.Get("newcrewmembers"), msg,
                        new LocalizedString[] { TextManager.Get("Ok") });
                    dialog.Buttons[0].OnClicked += dialog.Close;
                }
            }

            if (createNetworkEvent)
            {
                SendCrewState(true, validateHires: true);
            }

            return false;
        }

        private bool CreateRenamingComponent(GUIButton button, object userData)
        {
            if (!HasPermissionToHire || userData is not CharacterInfo characterInfo) { return false; }
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
            nameBox.OnTextChanged += (GUITextBox textBox, string text) =>
            {
                if (text.Contains('\n') || text.Contains('\r'))
                {
                    textBox.Text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                }
                return true;
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
            if (selection is not CharacterInfo characterInfo) { return false; }

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

            // When showing this window to someone hiring a new character, the right side panels aren't needed
            pendingAndCrewPanel.Visible = !ReplacingPermanentlyDeadCharacter;

            if (hadPermissionToHire != HasPermissionToHire || 
                wasReplacingPermanentlyDeadCharacter != ReplacingPermanentlyDeadCharacter)
            {
                RefreshUI();
            }

            if (needsHireableRefresh)
            {
                RefreshCrewFrames(hireableList);
                if (sortingDropDown?.SelectedItemData != null)
                {
                    SortCharacters(hireableList, (SortingMethod)sortingDropDown.SelectedItemData);
                }
                needsHireableRefresh = false;
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

        public void SetPendingHires(List<UInt16> characterInfos, bool[] characterInfoReserveBenchStatuses, Location location, bool checkCrewSizeLimit)
        {
            List<CharacterInfo> oldHires = PendingHires.ToList();
            foreach (CharacterInfo pendingHire in oldHires)
            {
                RemovePendingHire(pendingHire, createNetworkMessage: false);
            }
            PendingHires.Clear();
            int i = 0;
            foreach (UInt16 identifier in characterInfos)
            {
                CharacterInfo match = location.HireManager.AvailableCharacters.Find(info => info.ID == identifier);
                if (match != null)
                {
                    match.BotStatus = characterInfoReserveBenchStatuses[i] ? BotStatus.PendingHireToReserveBench : BotStatus.PendingHireToActiveService;
                    AddPendingHire(match, checkCrewSizeLimit: checkCrewSizeLimit, createNetworkMessage: false);
                    if (!PendingHires.Contains(match))
                    {
                        DebugConsole.ThrowError("Failed to add a pending hire");
                    }
                    System.Diagnostics.Debug.Assert(PendingHires.Contains(match));
                }
                else
                {
                    DebugConsole.ThrowError("Received a hire that doesn't exist.");
                }
                i++;
            }
        }

        /// <summary>
        /// Notify the server of crew changes
        /// </summary>
        /// <param name="updatePending">When set to true will tell the server to update the pending hires</param>
        /// <param name="renameCharacter">When not null tell the server to rename this character. Item1 is the character to rename, Item2 is the new name, Item3 indicates whether the renamed character is already a part of the crew.</param>
        /// <param name="firedCharacter">When not null tell the server to fire this character</param>
        /// <param name="validateHires">When set to true will tell the server to validate pending hires</param>
        public void SendCrewState(bool updatePending = false, (CharacterInfo info, string newName) renameCharacter = default, CharacterInfo firedCharacter = null, bool validateHires = false)
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
                        msg.WriteUInt16(pendingHire.ID);
                        msg.WriteBoolean(pendingHire.BotStatus == BotStatus.PendingHireToReserveBench);
                    }
                }

                msg.WriteBoolean(validateHires);

                bool validRenaming = renameCharacter.info != null && !string.IsNullOrEmpty(renameCharacter.newName);
                msg.WriteBoolean(validRenaming);
                if (validRenaming)
                {
                    msg.WriteUInt16(renameCharacter.info.ID);
                    msg.WriteString(renameCharacter.newName);
                    bool existingCrewMember =
                        campaign.CrewManager is CrewManager crewManager && 
                        crewManager.GetCharacterInfos(includeReserveBench: true).Any(ci => ci.ID == renameCharacter.info.ID);
                    msg.WriteBoolean(existingCrewMember);
                }

                msg.WriteBoolean(firedCharacter != null);
                if (firedCharacter != null)
                {
                    msg.WriteUInt16(firedCharacter.ID);
                }

                GameMain.Client.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
            }
        }
    }
}
