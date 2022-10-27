#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using static Barotrauma.TalentTree;
using static Barotrauma.TalentTree.TalentTreeStageState;

namespace Barotrauma
{
    internal readonly record struct TalentButton(GUIButton Button,
                                                 GUIComponent IconComponent,
                                                 TalentPrefab Prefab)
    {
        public Identifier Identifier => Prefab.Identifier;
    }

    internal readonly record struct TalentCornerIcon(Identifier TalentTree,
                                                     int Index,
                                                     GUIImage IconComponent,
                                                     GUIFrame BackgroundComponent,
                                                     GUIFrame GlowComponent);

    internal readonly struct TalentTreeStyle
    {
        public readonly GUIComponentStyle ComponentStyle;
        public readonly Color Color;

        public TalentTreeStyle(string componentStyle, Color color)
        {
            ComponentStyle = GUIStyle.GetComponentStyle(componentStyle);
            Color = color;
        }
    }

    internal sealed class TalentMenu
    {
        private static readonly Color unselectedColor = new Color(240, 255, 255, 225),
                                      unselectableColor = new Color(100, 100, 100, 225),
                                      pressedColor = new Color(60, 60, 60, 225),
                                      lockedColor = new Color(48, 48, 48, 255),
                                      unlockedColor = new Color(24, 37, 31, 255),
                                      availableColor = new Color(50, 47, 33, 255);

        private static readonly ImmutableDictionary<TalentTreeStageState, TalentTreeStyle> talentStageStyles =
            new Dictionary<TalentTreeStageState, TalentTreeStyle>
            {
                [Invalid] = new TalentTreeStyle("TalentTreeLocked", lockedColor),
                [Locked] = new TalentTreeStyle("TalentTreeLocked", lockedColor),
                [Unlocked] = new TalentTreeStyle("TalentTreePurchased", unlockedColor),
                [Available] = new TalentTreeStyle("TalentTreeUnlocked", availableColor),
                [Highlighted] = new TalentTreeStyle("TalentTreeAvailable", availableColor)
            }.ToImmutableDictionary();

        private readonly HashSet<TalentButton> talentButtons = new HashSet<TalentButton>();
        private readonly HashSet<GUIComponent> showCaseTalentFrames = new HashSet<GUIComponent>();
        private readonly HashSet<TalentCornerIcon> talentCornerIcons = new HashSet<TalentCornerIcon>();
        private HashSet<Identifier> selectedTalents = new HashSet<Identifier>();

        private GUIListBox? skillListBox;
        private GUITextBlock? talentPointText;
        private GUIProgressBar? experienceBar;
        private GUITextBlock? experienceText;
        private GUILayoutGroup? skillLayout;

        private GUIButton? talentApplyButton,
                           talentResetButton;

        public void CreateGUI(GUIFrame parent)
        {
            parent.ClearChildren();
            talentButtons.Clear();
            talentCornerIcons.Clear();
            showCaseTalentFrames.Clear();

            GUIFrame background = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");
            int padding = GUI.IntScale(15);
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(background.Rect.Width - padding, background.Rect.Height - padding), parent.RectTransform, Anchor.Center), style: null);

            GUIFrame content = new GUIFrame(new RectTransform(new Vector2(0.98f), frame.RectTransform, Anchor.Center), style: null);

            GUILayoutGroup contentLayout = new GUILayoutGroup(new RectTransform(Vector2.One, content.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                AbsoluteSpacing = GUI.IntScale(10),
                Stretch = true
            };

            Character? controlledCharacter = Character.Controlled;
            CharacterInfo? info = controlledCharacter?.Info ?? GameMain.Client?.CharacterInfo;
            if (info is null) { return; }

            CreateStatPanel(contentLayout, info);

            new GUIFrame(new RectTransform(new Vector2(1f, 1f), contentLayout.RectTransform), style: "HorizontalLine");

            if (JobTalentTrees.TryGet(info.Job.Prefab.Identifier, out TalentTree? talentTree))
            {
                CreateTalentMenu(contentLayout, info, talentTree!);
            }

            CreateFooter(contentLayout, info);
            UpdateTalentInfo();

            if (GameMain.NetworkMember != null)
            {
                CreateMultiplayerCharacterSettings(frame, content);
            }
        }

        private void CreateMultiplayerCharacterSettings(GUIComponent parent, GUIComponent content)
        {
            if (skillLayout is null) { return; }

            GUIFrame characterSettingsFrame = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform), style: null) { Visible = false };
            GUILayoutGroup characterLayout = new GUILayoutGroup(new RectTransform(Vector2.One, characterSettingsFrame.RectTransform));
            GUIFrame containerFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.9f), characterLayout.RectTransform), style: null);
            GUIFrame playerFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.7f), containerFrame.RectTransform, Anchor.Center), style: null);
            GameMain.NetLobbyScreen.CreatePlayerFrame(playerFrame, alwaysAllowEditing: true, createPendingText: false);

            GUIButton newCharacterBox = new GUIButton(new RectTransform(new Vector2(0.5f, 0.2f), skillLayout.RectTransform, Anchor.BottomRight),
                text: GameMain.NetLobbyScreen.CampaignCharacterDiscarded ? TextManager.Get("settings") : TextManager.Get("createnew"), style: "GUIButtonSmall")
            {
                IgnoreLayoutGroups = false,
                TextBlock =
                {
                    AutoScaleHorizontal = true
                }
            };

            newCharacterBox.OnClicked = (button, o) =>
            {
                if (!GameMain.NetLobbyScreen.CampaignCharacterDiscarded)
                {
                    GameMain.NetLobbyScreen.TryDiscardCampaignCharacter(() =>
                    {
                        newCharacterBox.Text = TextManager.Get("settings");
                        if (TabMenu.PendingChangesFrame != null)
                        {
                            NetLobbyScreen.CreateChangesPendingFrame(TabMenu.PendingChangesFrame);
                        }

                        OpenMenu();
                    });
                    return true;
                }

                OpenMenu();
                return true;

                void OpenMenu()
                {
                    characterSettingsFrame!.Visible = true;
                    content.Visible = false;
                }
            };

            GUILayoutGroup characterCloseButtonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), characterLayout.RectTransform), childAnchor: Anchor.BottomCenter);
            new GUIButton(new RectTransform(new Vector2(0.4f, 1f), characterCloseButtonLayout.RectTransform), TextManager.Get("ApplySettingsButton")) //TODO: Is this text appropriate for this circumstance for all languages?
            {
                OnClicked = (button, o) =>
                {
                    GameMain.Client?.SendCharacterInfo(GameMain.Client.PendingName);
                    characterSettingsFrame!.Visible = false;
                    content.Visible = true;
                    return true;
                }
            };
        }

        private void CreateStatPanel(GUIComponent parent, CharacterInfo info)
        {
            Job job = info.Job;

            GUILayoutGroup topLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), parent.RectTransform, Anchor.Center), isHorizontal: true);

            new GUICustomComponent(new RectTransform(new Vector2(0.25f, 1f), topLayout.RectTransform), onDraw: (batch, component) =>
            {
                float posY = component.Rect.Center.Y - component.Rect.Width / 2;
                info.DrawPortrait(batch, new Vector2(component.Rect.X, posY), Vector2.Zero, component.Rect.Width, false, false);
            });

            GUILayoutGroup nameLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1f), topLayout.RectTransform))
            {
                AbsoluteSpacing = GUI.IntScale(5),
                CanBeFocused = true
            };

            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), nameLayout.RectTransform), info.Name, font: GUIStyle.SubHeadingFont);

            if (!info.OmitJobInMenus)
            {
                nameBlock.TextColor = job.Prefab.UIColor;
                GUITextBlock jobBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), nameLayout.RectTransform), job.Name, font: GUIStyle.SmallFont) { TextColor = job.Prefab.UIColor };
            }

            if (info.PersonalityTrait != null)
            {
                LocalizedString traitString = TextManager.AddPunctuation(':', TextManager.Get("PersonalityTrait"), info.PersonalityTrait.DisplayName);
                Vector2 traitSize = GUIStyle.SmallFont.MeasureString(traitString);
                GUITextBlock traitBlock = new GUITextBlock(new RectTransform(Vector2.One, nameLayout.RectTransform), traitString, font: GUIStyle.SmallFont);
                traitBlock.RectTransform.NonScaledSize = traitSize.Pad(traitBlock.Padding).ToPoint();
            }

            ImmutableHashSet<TalentPrefab?> talentsOutsideTree = info.GetUnlockedTalentsOutsideTree().Select(static e => TalentPrefab.TalentPrefabs.Find(c => c.Identifier == e)).ToImmutableHashSet();
            if (talentsOutsideTree.Any())
            {
                //spacing
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), nameLayout.RectTransform), style: null);

                GUILayoutGroup extraTalentLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), nameLayout.RectTransform), childAnchor: Anchor.TopCenter);

                talentPointText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), extraTalentLayout.RectTransform, anchor: Anchor.Center), TextManager.Get("talentmenu.extratalents"), font: GUIStyle.SubHeadingFont);
                talentPointText.RectTransform.MaxSize = new Point(int.MaxValue, (int)talentPointText.TextSize.Y);

                var extraTalentList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.8f), extraTalentLayout.RectTransform, anchor: Anchor.Center), isHorizontal: true)
                {
                    AutoHideScrollBar = false,
                    ResizeContentToMakeSpaceForScrollBar = false
                };
                extraTalentList.ScrollBar.RectTransform.SetPosition(Anchor.BottomCenter, Pivot.TopCenter);
                extraTalentList.RectTransform.MinSize = new Point(0, GUI.IntScale(65));
                extraTalentLayout.Recalculate();
                extraTalentList.ForceLayoutRecalculation();

                foreach (var extraTalent in talentsOutsideTree)
                {
                    if (extraTalent is null) { continue; }
                    GUIImage talentImg = new GUIImage(new RectTransform(Vector2.One, extraTalentList.Content.RectTransform, scaleBasis: ScaleBasis.BothHeight), sprite: extraTalent.Icon, scaleToFit: true)
                    {
                        ToolTip = RichString.Rich($"‖color:{Color.White.ToStringHex()}‖{extraTalent.DisplayName}‖color:end‖" + "\n\n" + extraTalent.Description),
                        Color = GUIStyle.Green
                    };
                }
            }

            skillLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 1f), topLayout.RectTransform), childAnchor: Anchor.TopRight)
            {
                AbsoluteSpacing = GUI.IntScale(5),
                Stretch = true
            };

            GUITextBlock skillBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillLayout.RectTransform), TextManager.Get("skills"), font: GUIStyle.SubHeadingFont);

            skillListBox = new GUIListBox(new RectTransform(new Vector2(1f, 1f - skillBlock.RectTransform.RelativeSize.Y), skillLayout.RectTransform), style: null);
            TabMenu.CreateSkillList(info.Character, info, skillListBox);
        }

        private void CreateTalentMenu(GUIComponent parent, CharacterInfo info, TalentTree tree)
        {
            GUIListBox mainList = new GUIListBox(new RectTransform(new Vector2(1f, 0.9f), parent.RectTransform, anchor: Anchor.TopCenter));

            selectedTalents = info.GetUnlockedTalentsInTree().ToHashSet();

            List<GUITextBlock> subTreeNames = new List<GUITextBlock>();
            foreach (var subTree in tree.TalentSubTrees)
            {
                GUIListBox talentList;
                GUIComponent talentParent;
                Vector2 treeSize;
                switch (subTree.Type)
                {
                    case TalentTreeType.Primary:
                        talentList = mainList;
                        treeSize = new Vector2(1f, 0.5f);
                        break;
                    case TalentTreeType.Specialization:
                        talentList = GetSpecializationList();
                        treeSize = new Vector2(0.333f, 1f);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException($"Invalid TalentTreeType \"{subTree.Type}\"");
                }
                talentParent = talentList.Content;

                GUILayoutGroup subTreeLayoutGroup = new GUILayoutGroup(new RectTransform(treeSize, talentParent.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter)
                {
                    Stretch = true
                };

                if (subTree.Type != TalentTreeType.Primary)
                {
                    GUIFrame subtreeTitleFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.05f), subTreeLayoutGroup.RectTransform, anchor: Anchor.TopCenter) 
                        { MinSize = new Point(0, GUI.IntScale(30)) }, style: null);
                    subtreeTitleFrame.RectTransform.IsFixedSize = true;
                    int elementPadding = GUI.IntScale(8);
                    Point headerSize = subtreeTitleFrame.RectTransform.NonScaledSize;
                    GUIFrame subTreeTitleBackground = new GUIFrame(new RectTransform(new Point(headerSize.X - elementPadding, headerSize.Y), subtreeTitleFrame.RectTransform, anchor: Anchor.Center), style: "SubtreeHeader");
                    subTreeNames.Add(new GUITextBlock(new RectTransform(Vector2.One, subTreeTitleBackground.RectTransform, anchor: Anchor.TopCenter), subTree.DisplayName, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center));
                }

                int optionAmount = subTree.TalentOptionStages.Length;
                for (int i = 0; i < optionAmount; i++)
                {
                    TalentOption option = subTree.TalentOptionStages[i];
                    CreateTalentOption(subTreeLayoutGroup, subTree, i, option, info);
                }
                subTreeLayoutGroup.RectTransform.Resize(new Point(subTreeLayoutGroup.Rect.Width, 
                    subTreeLayoutGroup.Children.Sum(c => c.Rect.Height + subTreeLayoutGroup.AbsoluteSpacing)));
                subTreeLayoutGroup.RectTransform.MinSize = new Point(subTreeLayoutGroup.Rect.Width, subTreeLayoutGroup.Rect.Height);
                subTreeLayoutGroup.Recalculate();
                if (subTree.Type == TalentTreeType.Specialization)
                {
                    talentList.RectTransform.Resize(new Point(talentList.Rect.Width, Math.Max(subTreeLayoutGroup.Rect.Height, talentList.Rect.Height)));
                    talentList.RectTransform.MinSize = new Point(0, talentList.Rect.Height);
                }
            }

            var specializationList = GetSpecializationList();
            GetSpecializationList().RectTransform.Resize(new Point(specializationList.Content.Children.Sum(c => c.Rect.Width), specializationList.Rect.Height));

            GUITextBlock.AutoScaleAndNormalize(subTreeNames);

            GUIListBox GetSpecializationList()
            {
                if (mainList.Content.Children.LastOrDefault() is GUIListBox specializationList)
                {
                    return specializationList;
                }

                GUIListBox newSpecializationList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), mainList.Content.RectTransform, Anchor.TopCenter), isHorizontal: true, style: null);
                return newSpecializationList;
            }
        }

        private void CreateTalentOption(GUIComponent parent, TalentSubTree subTree, int index, TalentOption talentOption, CharacterInfo info)
        {
            int elementPadding = GUI.IntScale(8);
            GUIFrame talentOptionFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.01f), parent.RectTransform, anchor: Anchor.TopCenter) 
                { MinSize = new Point(0, GUI.IntScale(65)) }, style: null);

            Point talentFrameSize = talentOptionFrame.RectTransform.NonScaledSize;

            GUIFrame talentBackground = new GUIFrame(new RectTransform(new Point(talentFrameSize.X - elementPadding, talentFrameSize.Y - elementPadding), talentOptionFrame.RectTransform, anchor: Anchor.Center),
                style: "TalentBackground")
            {
                Color = talentStageStyles[Locked].Color
            };
            GUIFrame talentBackgroundHighlight = new GUIFrame(new RectTransform(Vector2.One, talentBackground.RectTransform, anchor: Anchor.Center), style: "TalentBackgroundGlow") { Visible = false };

            GUIImage cornerIcon = new GUIImage(new RectTransform(new Vector2(0.2f), talentOptionFrame.RectTransform, anchor: Anchor.BottomRight, scaleBasis: ScaleBasis.BothHeight) { MaxSize = new Point(16) }, style: null)
            {
                CanBeFocused = false,
                Color = talentStageStyles[Locked].Color
            };

            Point iconSize = cornerIcon.RectTransform.NonScaledSize;
            cornerIcon.RectTransform.AbsoluteOffset = new Point(iconSize.X / 2, iconSize.Y / 2);

            GUILayoutGroup talentOptionCenterGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 0.9f), talentOptionFrame.RectTransform, Anchor.Center), childAnchor: Anchor.CenterLeft);
            GUILayoutGroup talentOptionLayoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, talentOptionCenterGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };

            HashSet<Identifier> talentOptionIdentifiers = talentOption.TalentIdentifiers.OrderBy(static t => t).ToHashSet();
            bool hasShowcase = talentOption.ShowcaseTalent.TryUnwrap(out Identifier showcaseTalentIdentifier);
            GUILayoutGroup showcaseLayout = talentOptionLayoutGroup;

            if (hasShowcase)
            {
                talentOptionIdentifiers.Add(showcaseTalentIdentifier);
                Point parentSize = talentBackground.RectTransform.NonScaledSize;
                GUIFrame showCaseFrame = new GUIFrame(new RectTransform(new Point((int)(parentSize.X / 3f * (talentOptionIdentifiers.Count - 1)), parentSize.Y)), style: "GUITooltip")
                {
                    UserData = showcaseTalentIdentifier,
                    IgnoreLayoutGroups = true,
                    Visible = false
                };
                GUILayoutGroup showcaseCenterGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.7f), showCaseFrame.RectTransform, Anchor.Center), childAnchor: Anchor.CenterLeft);
                showcaseLayout = new GUILayoutGroup(new RectTransform(Vector2.One, showcaseCenterGroup.RectTransform), isHorizontal: true) { Stretch = true };
                showCaseTalentFrames.Add(showCaseFrame);
            }

            foreach (Identifier talentId in talentOptionIdentifiers)
            {
                if (!TalentPrefab.TalentPrefabs.TryGet(talentId, out TalentPrefab? talent)) { continue; }

                bool isShowCaseTalent = hasShowcase && talentId == showcaseTalentIdentifier;
                GUIComponent talentParent;

                if (hasShowcase && talentId != showcaseTalentIdentifier)
                {
                    talentParent = showcaseLayout;
                }
                else
                {
                    talentParent = talentOptionLayoutGroup;
                }

                GUIFrame talentFrame = new GUIFrame(new RectTransform(Vector2.One, talentParent.RectTransform), style: null)
                {
                    CanBeFocused = false
                };

                GUIFrame croppedTalentFrame = new GUIFrame(new RectTransform(Vector2.One, talentFrame.RectTransform, anchor: Anchor.Center, scaleBasis: ScaleBasis.BothHeight), style: null);
                GUIButton talentButton = new GUIButton(new RectTransform(Vector2.One, croppedTalentFrame.RectTransform, anchor: Anchor.Center), style: null)
                {
                    ToolTip = RichString.Rich($"‖color:{Color.White.ToStringHex()}‖{talent.DisplayName}‖color:end‖" + "\n\n" + talent.Description),
                    UserData = talent.Identifier,
                    PressedColor = pressedColor,
                    Enabled = info.Character != null,
                    OnClicked = (button, userData) =>
                    {
                        if (isShowCaseTalent)
                        {
                            foreach (GUIComponent component in showCaseTalentFrames)
                            {
                                if (component.UserData is Identifier showcaseIdentifier && showcaseIdentifier == talentId)
                                {
                                    component.RectTransform.ScreenSpaceOffset = new Point((int)(button.Rect.Location.X - component.Rect.Width / 2f + button.Rect.Width / 2f), button.Rect.Location.Y - component.Rect.Height);
                                    component.Visible = true;
                                }
                                else
                                {
                                    component.Visible = false;
                                }
                            }

                            return true;
                        }

                        Character? controlledCharacter = info.Character;
                        if (controlledCharacter is null) { return false; }

                        if (talentOption.MaxChosenTalents is 1)
                        {
                            // deselect other buttons in tier by removing their selected talents from pool
                            foreach (GUIButton guiButton in talentOptionLayoutGroup.GetAllChildren<GUIButton>())
                            {
                                if (guiButton.UserData is Identifier otherTalentIdentifier && guiButton != button)
                                {
                                    if (!controlledCharacter.HasTalent(otherTalentIdentifier))
                                    {
                                        selectedTalents.Remove(otherTalentIdentifier);
                                    }
                                }
                            }
                        }

                        Identifier talentIdentifier = (Identifier)userData;

                        if (IsViableTalentForCharacter(info.Character, talentIdentifier, selectedTalents))
                        {
                            if (!selectedTalents.Contains(talentIdentifier))
                            {
                                selectedTalents.Add(talentIdentifier);
                            }
                            else
                            {
                                selectedTalents.Remove(talentIdentifier);
                            }
                        }
                        else if (!controlledCharacter.HasTalent(talentIdentifier))
                        {
                            selectedTalents.Remove(talentIdentifier);
                        }

                        UpdateTalentInfo();
                        return true;
                    },
                };

                talentButton.Color = talentButton.HoverColor = talentButton.PressedColor = talentButton.SelectedColor = talentButton.DisabledColor = Color.Transparent;

                GUIComponent iconImage;
                if (talent.Icon is null)
                {
                    iconImage = new GUITextBlock(new RectTransform(Vector2.One, talentButton.RectTransform, anchor: Anchor.Center), text: "???", font: GUIStyle.LargeFont, textAlignment: Alignment.Center, style: null)
                    {
                        OutlineColor = GUIStyle.Red,
                        TextColor = GUIStyle.Red,
                        PressedColor = unselectableColor,
                        DisabledColor = unselectableColor,
                        CanBeFocused = false,
                    };
                }
                else
                {
                    iconImage = new GUIImage(new RectTransform(Vector2.One, talentButton.RectTransform, anchor: Anchor.Center), sprite: talent.Icon, scaleToFit: true)
                    {
                        Color = talent.ColorOverride.TryUnwrap(out Color color) ? color : Color.White,
                        PressedColor = unselectableColor,
                        DisabledColor = unselectableColor * 0.5f,
                        CanBeFocused = false,
                    };
                }

                iconImage.Enabled = talentButton.Enabled;
                if (isShowCaseTalent) { continue; }

                talentButtons.Add(new TalentButton(talentButton, iconImage, talent));
            }

            talentCornerIcons.Add(new TalentCornerIcon(subTree.Identifier, index, cornerIcon, talentBackground, talentBackgroundHighlight));
        }

        private void CreateFooter(GUIComponent parent, CharacterInfo info)
        {
            GUILayoutGroup bottomLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.07f), parent.RectTransform, Anchor.TopCenter), isHorizontal: true)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            GUILayoutGroup experienceLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.59f, 1f), bottomLayout.RectTransform));
            GUIFrame experienceBarFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), experienceLayout.RectTransform), style: null);

            experienceBar = new GUIProgressBar(new RectTransform(new Vector2(1f, 1f), experienceBarFrame.RectTransform, Anchor.CenterLeft),
                barSize: info.GetProgressTowardsNextLevel(), color: GUIStyle.Green)
            {
                IsHorizontal = true,
            };

            experienceText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), experienceBarFrame.RectTransform, anchor: Anchor.Center), "", font: GUIStyle.Font, textAlignment: Alignment.CenterRight)
            {
                Shadow = true,
                ToolTip = TextManager.Get("experiencetooltip")
            };

            talentPointText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), experienceLayout.RectTransform, anchor: Anchor.Center), "", font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterRight)
                { AutoScaleVertical = true };

            talentResetButton = new GUIButton(new RectTransform(new Vector2(0.19f, 1f), bottomLayout.RectTransform), text: TextManager.Get("reset"), style: "GUIButtonFreeScale")
            {
                OnClicked = ResetTalentSelection
            };
            talentApplyButton = new GUIButton(new RectTransform(new Vector2(0.19f, 1f), bottomLayout.RectTransform), text: TextManager.Get("applysettingsbutton"), style: "GUIButtonFreeScale")
            {
                OnClicked = ApplyTalentSelection,
            };
            GUITextBlock.AutoScaleAndNormalize(talentResetButton.TextBlock, talentApplyButton.TextBlock);
        }

        private bool ResetTalentSelection(GUIButton guiButton, object userData)
        {
            UpdateTalentInfo();
            return true;
        }

        private void ApplyTalents(Character controlledCharacter)
        {
            foreach (Identifier talent in CheckTalentSelection(controlledCharacter, selectedTalents))
            {
                controlledCharacter.GiveTalent(talent);
                if (GameMain.Client != null)
                {
                    GameMain.Client.CreateEntityEvent(controlledCharacter, new Character.UpdateTalentsEventData());
                }
            }

            UpdateTalentInfo();
        }

        private bool ApplyTalentSelection(GUIButton guiButton, object userData)
        {
            Character controlledCharacter = Character.Controlled;
            if (controlledCharacter is null) { return false; }

            ApplyTalents(controlledCharacter);
            return true;
        }

        public void UpdateTalentInfo()
        {
            if (!(Character.Controlled is { Info: var info } character)) { return; }

            bool unlockedAllTalents = character.HasUnlockedAllTalents();

            if (experienceBar is null || experienceText is null) { return; }

            if (unlockedAllTalents)
            {
                experienceText.Text = string.Empty;
                experienceBar.BarSize = 1f;
            }
            else
            {
                experienceText.Text = $"{info.ExperiencePoints - info.GetExperienceRequiredForCurrentLevel()} / {info.GetExperienceRequiredToLevelUp() - info.GetExperienceRequiredForCurrentLevel()}";
                experienceBar.BarSize = info.GetProgressTowardsNextLevel();
            }

            selectedTalents = CheckTalentSelection(character, selectedTalents).ToHashSet();

            string pointsLeft = info.GetAvailableTalentPoints().ToString();

            int talentCount = selectedTalents.Count - info.GetUnlockedTalentsInTree().Count();

            if (unlockedAllTalents)
            {
                talentPointText?.SetRichText($"‖color:{Color.Gray.ToStringHex()}‖{TextManager.Get("talentmenu.alltalentsunlocked")}‖color:end‖");
            }
            else if (talentCount > 0)
            {
                string pointsUsed = $"‖color:{XMLExtensions.ToStringHex(GUIStyle.Red)}‖{-talentCount}‖color:end‖";
                LocalizedString localizedString = TextManager.GetWithVariables("talentmenu.points.spending", ("[amount]", pointsLeft), ("[used]", pointsUsed));
                talentPointText?.SetRichText(localizedString);
            }
            else
            {
                talentPointText?.SetRichText(TextManager.GetWithVariable("talentmenu.points", "[amount]", pointsLeft));
            }

            foreach (TalentCornerIcon cornerIcon in talentCornerIcons)
            {
                TalentTreeStageState state = GetTalentOptionStageState(character, cornerIcon.TalentTree, cornerIcon.Index, selectedTalents);
                TalentTreeStyle style = talentStageStyles[state];
                GUIComponentStyle newStyle = style.ComponentStyle;
                cornerIcon.IconComponent.ApplyStyle(newStyle);
                cornerIcon.IconComponent.Color = newStyle.Color;
                cornerIcon.BackgroundComponent.Color = style.Color;
                cornerIcon.GlowComponent.Visible = state == Highlighted;
            }

            foreach (TalentButton talentButton in talentButtons)
            {
                Identifier talentIdentifier = talentButton.Identifier;
                bool unselectable = !IsViableTalentForCharacter(character, talentIdentifier, selectedTalents) || character.HasTalent(talentIdentifier);
                Color newTalentColor = unselectable ? unselectableColor : unselectedColor;
                Color hoverColor = Color.White;
                bool selected = false;

                if (character.HasTalent(talentIdentifier))
                {
                    selected = true;
                    newTalentColor = GUIStyle.Green;
                }
                else if (selectedTalents.Contains(talentIdentifier))
                {
                    selected = true;
                    newTalentColor = GUIStyle.Orange;
                    hoverColor = Color.Lerp(GUIStyle.Orange, Color.White, 0.7f);
                }

                bool shouldOverride = !unselectable || selected;

                if (shouldOverride && talentButton.Prefab.ColorOverride.TryUnwrap(out Color overrideColor))
                {
                    newTalentColor = overrideColor;
                }

                talentButton.IconComponent.Color = newTalentColor;
                talentButton.IconComponent.HoverColor = hoverColor;
            }

            if (skillListBox is null) { return; }

            TabMenu.CreateSkillList(character, info, skillListBox);
        }

        public void AddToGUIUpdateList()
        {
            bool mouseInteracted = PlayerInput.PrimaryMouseButtonClicked() || PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.ScrollWheelSpeed != 0;
            bool keyboardInteracted = PlayerInput.KeyHit(Keys.Escape) || GameSettings.CurrentConfig.KeyMap.Bindings[InputType.InfoTab].IsHit();

            foreach (GUIComponent component in showCaseTalentFrames)
            {
                component.AddToGUIUpdateList(order: 1);
                if (!component.Visible) { continue; }

                if (keyboardInteracted || (mouseInteracted && !component.Rect.Contains(PlayerInput.MousePosition)))
                {
                    component.Visible = false;
                }
            }
        }

        public void Update()
        {
            if (Character.Controlled?.Info is not { } characterInfo || talentResetButton is null || talentApplyButton is null) { return; }

            int talentCount = selectedTalents.Count - characterInfo.GetUnlockedTalentsInTree().Count();
            talentResetButton.Enabled = talentApplyButton.Enabled = talentCount > 0;
            if (talentApplyButton.Enabled && talentApplyButton.FlashTimer <= 0.0f)
            {
                talentApplyButton.Flash(GUIStyle.Orange);
            }
        }
    }
}