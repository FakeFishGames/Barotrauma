﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using static Barotrauma.TalentTree;
using static Barotrauma.TalentTree.TalentStages;

namespace Barotrauma
{
    internal readonly record struct TalentShowCaseButton(ImmutableHashSet<TalentButton> Buttons,
                                                         GUIComponent IconComponent);

    internal readonly record struct TalentButton(GUIComponent IconComponent,
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
        public const string ManageBotTalentsButtonUserData = "managebottalentsbutton";

        private Character? character;
        private CharacterInfo? characterInfo;

        private static readonly Color unselectedColor = new Color(240, 255, 255, 225),
                                      unselectableColor = new Color(100, 100, 100, 225),
                                      pressedColor = new Color(60, 60, 60, 225),
                                      lockedColor = new Color(48, 48, 48, 255),
                                      unlockedColor = new Color(24, 37, 31, 255),
                                      availableColor = new Color(50, 47, 33, 255);

        private static readonly ImmutableDictionary<TalentStages, TalentTreeStyle> talentStageStyles =
            new Dictionary<TalentStages, TalentTreeStyle>
            {
                [Invalid] = new TalentTreeStyle("TalentTreeLocked", lockedColor),
                [Locked] = new TalentTreeStyle("TalentTreeLocked", lockedColor),
                [Unlocked] = new TalentTreeStyle("TalentTreePurchased", unlockedColor),
                [Available] = new TalentTreeStyle("TalentTreeUnlocked", availableColor),
                [Highlighted] = new TalentTreeStyle("TalentTreeAvailable", availableColor)
            }.ToImmutableDictionary();

        private readonly HashSet<TalentButton> talentButtons = new HashSet<TalentButton>();
        private readonly HashSet<TalentShowCaseButton> talentShowCaseButtons = new HashSet<TalentShowCaseButton>();
        private readonly HashSet<GUIComponent> showCaseTalentFrames = new HashSet<GUIComponent>();
        private readonly HashSet<TalentCornerIcon> talentCornerIcons = new HashSet<TalentCornerIcon>();
        private HashSet<Identifier> selectedTalents = new HashSet<Identifier>();

        private readonly Queue<Identifier> showCaseClosureQueue = new();
        
        private GUITextBlock? nameBlock;
        private GUIButton? renameButton;
        private GUIListBox? skillListBox;
        private GUITextBlock? talentPointText;
        private GUIProgressBar? experienceBar;
        private GUITextBlock? experienceText;
        private GUILayoutGroup? skillLayout;

        private GUIButton? talentApplyButton,
                           talentResetButton;

        private delegate void StartAnimation(RectangleF start, RectangleF end, float duration);
        private StartAnimation? startAnimation;
        private GUIComponent? talentMainArea;

        public void CreateGUI(GUIFrame parent, CharacterInfo? characterInfo)
        {
            this.characterInfo = characterInfo;
            character = characterInfo?.Character;

            parent.ClearChildren();
            talentButtons.Clear();
            talentShowCaseButtons.Clear();
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

            if (characterInfo is null) { return; }

            CreateStatPanel(contentLayout, characterInfo);

            new GUIFrame(new RectTransform(new Vector2(1f, 1f), contentLayout.RectTransform), style: "HorizontalLine");

            if (JobTalentTrees.TryGet(characterInfo.Job.Prefab.Identifier, out TalentTree? talentTree))
            {
                CreateTalentMenu(contentLayout, characterInfo, talentTree!);
            }

            CreateFooter(contentLayout, characterInfo);
            UpdateTalentInfo();

            if (GameMain.NetworkMember != null && IsOwnCharacter(characterInfo))
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
            GUILayoutGroup playerFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), containerFrame.RectTransform, Anchor.TopCenter));
            GameMain.NetLobbyScreen.CreatePlayerFrame(playerFrame, alwaysAllowEditing: true, createPendingText: false);
            
            if (!GameMain.NetLobbyScreen.PermadeathMode && GameMain.GameSession?.GameMode is not PvPMode)
            {
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
            }
            else if (characterInfo != null)
            {
                renameButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.2f), skillLayout.RectTransform, Anchor.BottomRight),
                    text: TextManager.Get("button.RenameCharacter"), style: "GUIButtonSmall")
                {
                    Enabled = characterInfo.RenamingEnabled,
                    ToolTip = TextManager.Get("permadeath.rename.description"),
                    IgnoreLayoutGroups = false,
                    TextBlock =
                    {
                        AutoScaleHorizontal = true
                    },
                    OnClicked = (_, _) =>
                    {
                        CreateRenamePopup();
                        return true;
                    }
                };
            }

            GUILayoutGroup characterCloseButtonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), characterLayout.RectTransform), childAnchor: Anchor.BottomCenter);
            new GUIButton(new RectTransform(new Vector2(0.4f, 1f), characterCloseButtonLayout.RectTransform), TextManager.Get("ApplySettingsButton")) //TODO: Is this text appropriate for this circumstance for all languages?
            {
                OnClicked = (button, o) =>
                {
                    GameMain.Client?.SendCharacterInfo(GameMain.Client.PendingName);
                    GameMain.NetLobbyScreen.CampaignCharacterDiscarded = false;
                    characterSettingsFrame.Visible = false;
                    content.Visible = true;
                    return true;
                }
            };
        }

        private void CreateRenamePopup()
        {
            GUIMessageBox renamePopup = new(
                TextManager.Get("button.RenameCharacter"), TextManager.Get("permadeath.rename.description"),
                new LocalizedString[] { TextManager.Get("Confirm"), TextManager.Get("Cancel") }, minSize: new Point(0, GUI.IntScale(230)));
            GUITextBox newNameBox = new(new(Vector2.One, renamePopup.Content.RectTransform), "")
            {
                OnEnterPressed = (textBox, text) =>
                {
                    textBox.Text = text.Trim();
                    return true;
                }
            };
            renamePopup.Buttons[0].OnClicked += (_, _) =>
            {
                if (newNameBox.Text?.Trim() is string newName && newName != "")
                {
                    if (characterInfo != null)
                    {
                        if (newNameBox.Text == characterInfo.Name)
                        {
                            renamePopup.Close();
                            return true;
                        }
                        if (GameMain.GameSession?.Campaign?.CampaignUI?.HRManagerUI is { } crewManagement)
                        {
                            crewManagement.RenameCharacter(characterInfo, newName);
                            if (nameBlock != null)
                            {
                                nameBlock.Text = newName;
                            }
                            if (renameButton != null)
                            {
                                renameButton.Enabled = false;
                            }
                            renamePopup.Close();
                        }
                        return true;
                    }
                    DebugConsole.ThrowError("Tried to rename character, but CharacterInfo completely missing!");
                    return true;
                }
                else
                {
                    newNameBox.Flash();
                    return false;
                }
            };
            renamePopup.Buttons[1].OnClicked += renamePopup.Close;
        }

        private void CreateStatPanel(GUIComponent parent, CharacterInfo info)
        {
            Job job = info.Job;

            GUILayoutGroup topLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), parent.RectTransform, Anchor.Center), isHorizontal: true);

            new GUICustomComponent(new RectTransform(new Vector2(0.25f, 1f), topLayout.RectTransform), onDraw: (batch, component) =>
            {
                info.DrawIcon(batch, component.Rect.Center.ToVector2(), component.Rect.Size.ToVector2());
            });

            GUILayoutGroup nameLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1f), topLayout.RectTransform))
            {
                AbsoluteSpacing = GUI.IntScale(5),
                CanBeFocused = true
            };

            nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), nameLayout.RectTransform), info.Name, font: GUIStyle.SubHeadingFont);

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
            if (talentsOutsideTree.Any(static t => t != null && !t.IsHiddenExtraTalent))
            {
                //spacing
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), nameLayout.RectTransform), style: null);

                GUILayoutGroup extraTalentLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.55f), nameLayout.RectTransform), childAnchor: Anchor.TopCenter);

                talentPointText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), extraTalentLayout.RectTransform, anchor: Anchor.Center), TextManager.Get("talentmenu.extratalents"), font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleVertical = true
                };
                talentPointText.RectTransform.MaxSize = new Point(int.MaxValue, (int)talentPointText.TextSize.Y);

                var extraTalentList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.7f), extraTalentLayout.RectTransform, anchor: Anchor.Center), isHorizontal: true)
                {
                    AutoHideScrollBar = false,
                    ResizeContentToMakeSpaceForScrollBar = false
                };
                extraTalentList.ScrollBar.RectTransform.SetPosition(Anchor.BottomCenter, Pivot.TopCenter);
                extraTalentLayout.Recalculate();
                extraTalentList.ForceLayoutRecalculation();

                foreach (var extraTalent in talentsOutsideTree)
                {
                    if (extraTalent is null) { continue; }
                    if (extraTalent.IsHiddenExtraTalent) { continue; }
                    GUIImage talentImg = new GUIImage(new RectTransform(Vector2.One, extraTalentList.Content.RectTransform, scaleBasis: ScaleBasis.BothHeight), sprite: extraTalent.Icon, scaleToFit: true)
                    {
                        ToolTip = GetTalentTooltip(extraTalent, characterInfo),
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
            talentMainArea = new GUIFrame(new RectTransform(new Vector2(1f, 0.9f), parent.RectTransform, Anchor.TopCenter), style: null );

            GUIListBox mainList = new GUIListBox(new RectTransform(Vector2.One, talentMainArea.RectTransform));
            startAnimation = CreatePopupAnimationHandler(talentMainArea);

            if (info is { TalentRefundPoints: > 0, ShowTalentResetPopupOnOpen: true })
            {
                CreateTalentResetPopup(talentMainArea);
            }

            selectedTalents = info.GetUnlockedTalentsInTree().ToHashSet();

            var specializationCount = tree.TalentSubTrees.Count(t => t.Type == TalentTreeType.Specialization);

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
                        treeSize = new Vector2(Math.Max(0.333f, 1.0f / tree.TalentSubTrees.Count(t => t.Type == TalentTreeType.Specialization)), 1f);
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
                    CreateTalentOption(subTreeLayoutGroup, subTree, i, option, info, specializationCount);
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
            //resize (scale up) children if there's less than 3 of them to make them cover the whole width of the menu
            specializationList.Content.RectTransform.Resize(new Point(specializationList.Content.Children.Sum(static c => c.Rect.Width), specializationList.Rect.Height), 
                resizeChildren: specializationCount < 3);
            //make room for scrollbar if there's more than the default amount of specializations
            if (specializationCount > 3)
            {
                specializationList.RectTransform.MinSize = new Point(specializationList.Rect.Width, specializationList.Content.Rect.Height + (int)(specializationList.ScrollBar.Rect.Height * 0.9f));
            }

            GUITextBlock.AutoScaleAndNormalize(subTreeNames);

            GUIListBox GetSpecializationList()
            {
                if (mainList.Content.Children.LastOrDefault() is GUIListBox specList)
                {
                    return specList;
                }
                GUIListBox newSpecializationList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), mainList.Content.RectTransform, Anchor.TopCenter), isHorizontal: true, style: null);
                return newSpecializationList;
            }
        }

        private void CreateTalentResetPopup(GUIComponent parent)
        {
            int talentResetCount = 0;
            if (character?.Info != null)
            {
                talentResetCount = Math.Min(character.Info.TalentResetCount, character.Info.GetCurrentLevel());
            }
            bool hasResetTalentsBefore = talentResetCount > 0;
            var bgBlocker = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform, anchor: Anchor.Center), style: "GUIBackgroundBlocker")
            {
                IgnoreLayoutGroups = true
            };

            var popup = new GUIFrame(new RectTransform(new Vector2(0.6f, 0.8f), bgBlocker.RectTransform, Anchor.Center));

            var popupLayout = new GUILayoutGroup(new RectTransform(ToolBox.PaddingSizeParentRelative(popup.RectTransform, 0.95f), popup.RectTransform, Anchor.Center), isHorizontal: false);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), popupLayout.RectTransform), TextManager.Get("talentresetheader"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, hasResetTalentsBefore ? 0.25f : 0.5f), popupLayout.RectTransform), TextManager.Get("talentresetprompt"), wrap: true);

            if (hasResetTalentsBefore)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), popupLayout.RectTransform), 
                    TextManager.GetWithVariable("talentresetpromptwarning", "[count]", talentResetCount.ToString()), wrap: true)
                {
                    TextColor = GUIStyle.Red
                };
            }

            var buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.35f), popupLayout.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true);

            var confirmButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonLayout.RectTransform), TextManager.Get("holdtoconfirm"))
            {
                RequireHold = true,
                HoldDurationSeconds = 1.5f,
                OnClicked = (button, o) =>
                {
                    if (character is null || characterInfo is null) { return false; }

                    characterInfo.RefundTalents();
                    selectedTalents.Clear();
                    UpdateTalentInfo();
                    bgBlocker.Visible = false;
                    return true;
                }
            };
            var denyButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonLayout.RectTransform), TextManager.Get("decidelater"))
            {
                RequireHold = false,
                OnClicked = (button, userData) =>
                {
                    if (talentResetButton is not { } resetButton) { return false; }
                    startAnimation?.Invoke(popup.Rect, resetButton.Rect, 0.25f);
                    resetButton.Flash(GUIStyle.Green);
                    bgBlocker.Visible = false;
                    if (characterInfo != null)
                    {
                        characterInfo.ShowTalentResetPopupOnOpen = false;
                    }
                    return true;
                }
            };
        }

        private static StartAnimation CreatePopupAnimationHandler(GUIComponent parent)
        {
            bool drawAnimation = false;

            float animDur = 1f,
                  animTimer = 0f;

            RectangleF drawRect = RectangleF.Empty,
                       animStartRect = RectangleF.Empty,
                       animEndRect = RectangleF.Empty;

            void StartAnimation(RectangleF start, RectangleF end, float duration)
            {
                animStartRect = start;
                animEndRect = end;
                animTimer = 0;
                animDur = duration;
                drawRect = start;
                drawAnimation = true;
            }

            void OnDraw(SpriteBatch batch, GUICustomComponent component)
            {
                if (!drawAnimation) { return; }

                GUIComponentStyle style = GUIStyle.GetComponentStyle("GUIFrame");

                style.Sprites[GUIComponent.ComponentState.None][0].Draw(batch, drawRect, Color.White);
            }

            void OnUpdate(float f, GUICustomComponent component)
            {
                if (!drawAnimation) { return; }

                animTimer += f;
                if (animTimer > animDur)
                {
                    drawRect = animEndRect;
                    drawAnimation = false;
                    return;
                }

                float lerp = animTimer / animDur;

                drawRect = new RectangleF(
                    MathHelper.Lerp(animStartRect.X, animEndRect.X, lerp),
                    MathHelper.Lerp(animStartRect.Y, animEndRect.Y, lerp),
                    MathHelper.Lerp(animStartRect.Width, animEndRect.Width, lerp),
                    MathHelper.Lerp(animStartRect.Height, animEndRect.Height, lerp));
            }

            new GUICustomComponent(new RectTransform(Vector2.One, parent.RectTransform), onDraw: OnDraw, onUpdate: OnUpdate)
            {
                IgnoreLayoutGroups = true,
                CanBeFocused =  false
            };

            return StartAnimation;
        }

        private void CreateTalentOption(GUIComponent parent, TalentSubTree subTree, int index, TalentOption talentOption, CharacterInfo info, int specializationCount)
        {
            int elementPadding = GUI.IntScale(8);
            int height = GUI.IntScale((GameMain.GameSession?.Campaign == null ? 65 : 60) * (specializationCount > 3 ? 0.97f : 1.0f));
            GUIFrame talentOptionFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.01f), parent.RectTransform, anchor: Anchor.TopCenter) 
                { MinSize = new Point(0, height) }, style: null);

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
            HashSet<TalentButton> buttonsToAdd = new();

            Dictionary<GUILayoutGroup, ImmutableHashSet<Identifier>> showCaseTalentParents = new();
            Dictionary<Identifier, GUIComponent> showCaseTalentButtonsToAdd = new();

            foreach (var (showCaseTalentIdentifier, talents) in talentOption.ShowCaseTalents)
            {
                talentOptionIdentifiers.Add(showCaseTalentIdentifier);
                Point parentSize = talentBackground.RectTransform.NonScaledSize;
                GUIFrame showCaseFrame = new GUIFrame(new RectTransform(new Point((int)(parentSize.X / 3f * (talents.Count - 1)), parentSize.Y)), style: "GUITooltip")
                {
                    UserData = showCaseTalentIdentifier,
                    IgnoreLayoutGroups = true,
                    Visible = false
                };
                GUILayoutGroup showcaseCenterGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.7f), showCaseFrame.RectTransform, Anchor.Center), childAnchor: Anchor.CenterLeft);
                GUILayoutGroup showcaseLayout = new GUILayoutGroup(new RectTransform(Vector2.One, showcaseCenterGroup.RectTransform), isHorizontal: true) { Stretch = true };
                showCaseTalentParents.Add(showcaseLayout, talents);
                showCaseTalentFrames.Add(showCaseFrame);
            }

            foreach (Identifier talentId in talentOptionIdentifiers)
            {
                if (!TalentPrefab.TalentPrefabs.TryGet(talentId, out TalentPrefab? talent)) { continue; }

                bool isShowCaseTalent = talentOption.ShowCaseTalents.ContainsKey(talentId);
                GUIComponent talentParent = talentOptionLayoutGroup;

                foreach (var (key, value) in showCaseTalentParents)
                {
                    if (value.Contains(talentId))
                    {
                        talentParent = key;
                        break;
                    }
                }

                GUIFrame talentFrame = new GUIFrame(new RectTransform(Vector2.One, talentParent.RectTransform), style: null)
                {
                    CanBeFocused = false
                };

                GUIFrame croppedTalentFrame = new GUIFrame(new RectTransform(Vector2.One, talentFrame.RectTransform, anchor: Anchor.Center, scaleBasis: ScaleBasis.BothHeight), style: null);
                GUIButton talentButton = new GUIButton(new RectTransform(Vector2.One, croppedTalentFrame.RectTransform, anchor: Anchor.Center), style: null)
                {
                    ToolTip = GetTalentTooltip(talent, characterInfo),
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

                        if (character is null) { return false; }
                        
                        Identifier talentIdentifier = (Identifier)userData;
                        if (talentOption.MaxChosenTalents is 1)
                        {
                            // deselect other buttons in tier by removing their selected talents from pool
                            foreach (Identifier identifier in selectedTalents)
                            {
                                if (character.HasTalent(identifier) || identifier == talentId) { continue; }

                                if (talentOptionIdentifiers.Contains(identifier))
                                {
                                    selectedTalents.Remove(identifier);
                                }
                            }
                        }

                        if (character.HasTalent(talentIdentifier))
                        {
                            return true;
                        }
                        else if (IsViableTalentForCharacter(info.Character, talentIdentifier, selectedTalents))
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
                        else
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
                if (isShowCaseTalent)
                {
                    showCaseTalentButtonsToAdd.Add(talentId, iconImage);
                    continue;
                }

                buttonsToAdd.Add(new TalentButton(iconImage, talent));
            }

            foreach (TalentButton button in buttonsToAdd)
            {
                talentButtons.Add(button);
            }

            foreach (var (key, value) in showCaseTalentButtonsToAdd)
            {
                HashSet<TalentButton> buttons = new();
                foreach (Identifier identifier in talentOption.ShowCaseTalents[key])
                {
                    if (talentButtons.FirstOrNull(talentButton => talentButton.Identifier == identifier) is not { } button) { continue; }

                    buttons.Add(button);
                }

                talentShowCaseButtons.Add(new TalentShowCaseButton(buttons.ToImmutableHashSet(), value));
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

        private static RichString GetTalentTooltip(TalentPrefab talent, CharacterInfo? character)
        {
            LocalizedString progress = string.Empty;

            if (character is not null && talent.TrackedStat.TryUnwrap(out var stat))
            {
                var statValue = character.GetSavedStatValue(StatTypes.None, stat.PermanentStatIdentifier);
                var intValue = (int)MathF.Round(statValue);
                progress = "\n\n";
                progress += statValue < stat.Max
                    ? TextManager.GetWithVariables("talentprogress", ("[amount]", intValue.ToString()), ("[max]", stat.Max.ToString()))
                    : TextManager.Get("talentprogresscompleted");
            }

            RichString tooltip = RichString.Rich($"‖color:{Color.White.ToStringHex()}‖{talent.DisplayName}‖color:end‖\n\n{ToolBox.ExtendColorToPercentageSigns(talent.Description.Value)}{progress}");
            return tooltip;
        }

        private bool ResetTalentSelection(GUIButton guiButton, object userData)
        {
            if (characterInfo is null) { return false; }

            int newTalentCount = selectedTalents.Count - characterInfo.GetUnlockedTalentsInTree().Count();
            // if we don't have talents selected, and we have points to refund, show the refund popup
            if (characterInfo.TalentRefundPoints > 0 && newTalentCount == 0)
            {
                CreateTalentResetPopup(talentMainArea!);
                return true;
            }

            selectedTalents = characterInfo.GetUnlockedTalentsInTree().ToHashSet();
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
            if (character is null) { return false; }

            ApplyTalents(character);
            return true;
        }

        public void UpdateTalentInfo()
        {
            if (character is null || characterInfo is null) { return; }

            bool unlockedAllTalents = character.HasUnlockedAllTalents();

            if (experienceBar is null || experienceText is null) { return; }

            if (unlockedAllTalents)
            {
                experienceText.Text = string.Empty;
                experienceBar.BarSize = 1f;
            }
            else
            {
                experienceText.Text = $"{characterInfo.ExperiencePoints - characterInfo.GetExperienceRequiredForCurrentLevel()} / {characterInfo.GetExperienceRequiredToLevelUp() - characterInfo.GetExperienceRequiredForCurrentLevel()}";
                experienceBar.BarSize = characterInfo.GetProgressTowardsNextLevel();
            }

            selectedTalents = CheckTalentSelection(character, selectedTalents).ToHashSet();

            string pointsLeft = characterInfo.GetAvailableTalentPoints().ToString();

            int talentCount = selectedTalents.Count - characterInfo.GetUnlockedTalentsInTree().Count();

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
                TalentStages state = GetTalentOptionStageState(character, cornerIcon.TalentTree, cornerIcon.Index, selectedTalents);
                TalentTreeStyle style = talentStageStyles[state];
                GUIComponentStyle newStyle = style.ComponentStyle;
                cornerIcon.IconComponent.ApplyStyle(newStyle);
                cornerIcon.IconComponent.Color = newStyle.Color;
                cornerIcon.BackgroundComponent.Color = style.Color;
                cornerIcon.GlowComponent.Visible = state == Highlighted;
            }

            foreach (TalentButton talentButton in talentButtons)
            {
                TalentStages stage = GetTalentState(character, talentButton.Identifier, selectedTalents);
                ApplyTalentIconColor(stage, talentButton.IconComponent, talentButton.Prefab.ColorOverride);
            }

            foreach (TalentShowCaseButton showCaseTalentButton in talentShowCaseButtons)
            {
                TalentStages collectiveTalentStage = GetCollectiveTalentState(character, showCaseTalentButton.Buttons, selectedTalents);
                ApplyTalentIconColor(collectiveTalentStage, showCaseTalentButton.IconComponent, Option<Color>.None());
            }

            if (skillListBox is null) { return; }

            TabMenu.CreateSkillList(character, characterInfo, skillListBox);

            static TalentStages GetTalentState(Character character, Identifier talentIdentifier, IReadOnlyCollection<Identifier> selectedTalents)
            {
                bool unselectable = !IsViableTalentForCharacter(character, talentIdentifier, selectedTalents) || character.HasTalent(talentIdentifier);
                TalentStages stage = unselectable ? Locked : Available;
                if (unselectable)
                {
                    stage =  Locked;
                }

                if (character.HasTalent(talentIdentifier))
                {
                    stage =  Unlocked;
                }
                else if (selectedTalents.Contains(talentIdentifier))
                {
                    stage =  Highlighted;
                }

                return stage;
            }

            static void ApplyTalentIconColor(TalentStages stage, GUIComponent component, Option<Color> colorOverride)
            {
                Color color = stage switch
                {
                    Invalid => unselectableColor,
                    Locked => unselectableColor,
                    Unlocked => GetColorOrOverride(GUIStyle.Green, colorOverride),
                    Highlighted => GetColorOrOverride(GUIStyle.Orange, colorOverride),
                    Available => GetColorOrOverride(unselectedColor, colorOverride),
                    _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
                };

                component.Color = color;
                component.HoverColor = Color.Lerp(color, Color.White, 0.7f);

                static Color GetColorOrOverride(Color color, Option<Color> colorOverride) => colorOverride.TryUnwrap(out Color overrideColor) ? overrideColor : color;
            }

            // this could also be reused for setting colors for talentCornerIcons but that's for another time
            static TalentStages GetCollectiveTalentState(Character character, IReadOnlyCollection<TalentButton> buttons, IReadOnlyCollection<Identifier> selectedTalents)
            {
                HashSet<TalentStages> talentStages = new HashSet<TalentStages>();
                foreach (TalentButton button in buttons)
                {
                    talentStages.Add(GetTalentState(character, button.Identifier, selectedTalents));
                }

                TalentStages collectiveStage = talentStages.All(static stage => stage is Locked)
                    ? Locked
                    : Available;

                foreach (TalentStages stage in talentStages)
                {
                    if (stage is Highlighted)
                    {
                        collectiveStage = Highlighted;
                        break;
                    }

                    if (stage is Unlocked)
                    {
                        collectiveStage = Unlocked;
                        break;
                    }
                }

                return collectiveStage;
            }
        }

        private static readonly LocalizedString refundText = TextManager.Get("refund"),
                                                resetText = TextManager.Get("reset");

        public void Update()
        {
            if (characterInfo is null || talentResetButton is null || talentApplyButton is null) { return; }

            int talentCount = selectedTalents.Count - characterInfo.GetUnlockedTalentsInTree().Count();
            talentApplyButton.Enabled = character != null && talentCount > 0;
            talentResetButton.Enabled = character != null && (talentCount > 0 || characterInfo.TalentRefundPoints > 0);

            if (talentCount == 0 && characterInfo.TalentRefundPoints > 0)
            {
                if (talentResetButton.FlashTimer <= 0.0f)
                {
                    talentResetButton.Flash(GUIStyle.Orange);
                }

                talentResetButton.Text = refundText;
            }
            else
            {
                talentResetButton.Text = resetText;
            }

            if (talentApplyButton.Enabled && talentApplyButton.FlashTimer <= 0.0f)
            {
                talentApplyButton.Flash(GUIStyle.Orange);
            }

            while (showCaseClosureQueue.TryDequeue(out Identifier identifier))
            {
                foreach (GUIComponent component in showCaseTalentFrames)
                {
                    if (component.UserData is Identifier showcaseIdentifier && showcaseIdentifier == identifier)
                    {
                        component.Visible = false;
                    }
                }
            }

            bool mouseInteracted = PlayerInput.PrimaryMouseButtonClicked() || PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.ScrollWheelSpeed != 0;
            bool keyboardInteracted = PlayerInput.KeyHit(Keys.Escape) || GameSettings.CurrentConfig.KeyMap.Bindings[InputType.InfoTab].IsHit();

            foreach (GUIComponent component in showCaseTalentFrames)
            {
                if (component.UserData is not Identifier identifier) { continue; }

                component.AddToGUIUpdateList(order: 1);
                if (!component.Visible) { continue; }

                if (keyboardInteracted || (mouseInteracted && !component.Rect.Contains(PlayerInput.MousePosition)))
                {
                    showCaseClosureQueue.Enqueue(identifier);
                }
            }

            NetLobbyScreen.UpdateJobVariantSelectionIfNeeded();
        }

        private static bool IsOwnCharacter(CharacterInfo? info)
        {
            if (info is null) { return false; }

            CharacterInfo? ownCharacterInfo = Character.Controlled?.Info ?? GameMain.Client?.CharacterInfo;
            if (ownCharacterInfo is null) { return false; }

            return info.GetIdentifierUsingOriginalName() == ownCharacterInfo.GetIdentifierUsingOriginalName();
        }

        private static bool IsOnSameTeam(CharacterInfo? info)
        {
            if (info is null) { return false; }

            CharacterTeamType? ownCharacterTeam = Character.Controlled?.TeamID ?? GameMain.Client?.MyClient?.TeamID;
            if (ownCharacterTeam is null) { return false; }

            return info.TeamID == ownCharacterTeam;
        }

        private static bool IsSpectatingInMultiplayer()
        {
            if (GameMain.Client?.MyClient is not { } myClient) { return false; }
            return myClient.Spectating;
        }

        public static bool CanManageTalents(CharacterInfo targetInfo)
        {
            // in singleplayer we can do whatever we want
            if (GameMain.IsSingleplayer) { return true; }

            // always allow managing talents for own character
            if (IsOwnCharacter(targetInfo)) { return true; }

            // disallow managing talents while spectating
            if (IsSpectatingInMultiplayer()) { return false; }

            // don't allow controlling non-bot characters
            if (targetInfo.Character is not { IsBot: true }) { return false; }

            // only allow managing talents for bots on the same team
            if (!IsOnSameTeam(targetInfo)) { return false; }

            // lastly, check if we have the permission to do this
            return GameMain.Client is { } client && client.HasPermission(ClientPermissions.ManageBotTalents);
        }
    }
}