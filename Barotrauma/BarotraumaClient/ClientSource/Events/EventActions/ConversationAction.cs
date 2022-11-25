using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class ConversationAction : EventAction
    {
        private GUIMessageBox dialogBox;

        private static ConversationAction lastActiveAction;
        private static GUIMessageBox lastMessageBox;

        public static bool IsDialogOpen
        {
            get
            {
                return GUIMessageBox.MessageBoxes.Any(mb =>
                    mb.UserData as string == "ConversationAction" ||
                    (mb.UserData is Pair<string, UInt16> pair && pair.First == "ConversationAction"));
            }
        }
        public static bool FadeScreenToBlack
        {
            get { return IsDialogOpen && shouldFadeToBlack; }
        }

        private static bool shouldFadeToBlack;

        private bool IsBlockedByAnotherConversation(IEnumerable<Entity> _, float duration)
        {
            return 
                lastActiveAction != null && 
                lastActiveAction.ParentEvent != ParentEvent && 
                Timing.TotalTime < lastActiveAction.lastActiveTime + duration;
        }

        partial void ShowDialog(Character speaker, Character targetCharacter)
        {
            CreateDialog(Text, speaker, Options.Select(opt => opt.Text), GetEndingOptions(), actionInstance: this, spriteIdentifier: EventSprite, fadeToBlack: FadeToBlack, dialogType: DialogType, continueConversation: ContinueConversation);
        }

        public static void CreateDialog(string text, Character speaker, IEnumerable<string> options, int[] closingOptions, string eventSprite, UInt16 actionId, bool fadeToBlack, DialogTypes dialogType, bool continueConversation = false)
        {
            CreateDialog(text, speaker, options, closingOptions, actionInstance: null, actionId: actionId, spriteIdentifier: eventSprite, fadeToBlack: fadeToBlack, dialogType: dialogType, continueConversation: continueConversation);
        }

        private static void CreateDialog(string text, Character speaker, IEnumerable<string> options, int[] closingOptions, string spriteIdentifier = null, 
                                         ConversationAction actionInstance = null, UInt16? actionId = null, bool fadeToBlack = false, DialogTypes dialogType = DialogTypes.Regular, bool continueConversation = false)
        {
            Debug.Assert(actionInstance == null || actionId == null);

            if (GUI.InputBlockingMenuOpen)
            {
                if (actionId.HasValue) { SendIgnore(actionId.Value); }
                return;
            }

            shouldFadeToBlack = fadeToBlack;

            Sprite eventSprite = EventSet.GetEventSprite(spriteIdentifier);

            if (lastMessageBox != null && !lastMessageBox.Closed && GUIMessageBox.MessageBoxes.Contains(lastMessageBox))
            {
                if (eventSprite != null && lastMessageBox.BackgroundIcon == null)
                {
                    //no background icon in the last message box: we need to create a new one
                    lastMessageBox.Close();
                }
                else
                {
                    if (actionId != null && lastMessageBox.UserData is Pair<string, ushort> userData)
                    {
                        if (userData.Second == actionId) { return; }
                        lastMessageBox.UserData = new Pair<string, ushort>("ConversationAction", actionId.Value);
                    }

                    GUIListBox conversationList = lastMessageBox.FindChild("conversationlist", true) as GUIListBox;
                    Debug.Assert(conversationList != null);

                    // gray out the last text block
                    if (conversationList.Content.Children.LastOrDefault() is GUILayoutGroup lastElement)
                    {
                        if (lastElement.FindChild("text", true) is GUITextBlock textLayout)
                        {
                            textLayout.OverrideTextColor(Color.DarkGray * 0.8f);
                        }
                    }

                    float prevSize = conversationList.TotalSize;

                    List<GUIButton> extraButtons = CreateConversation(conversationList, text, speaker, options, string.IsNullOrWhiteSpace(spriteIdentifier));
                    AssignActionsToButtons(extraButtons, lastMessageBox);
                    RecalculateLastMessage(conversationList, true);
                    conversationList.BarScroll = (prevSize - conversationList.Content.Rect.Height) / (conversationList.TotalSize - conversationList.Content.Rect.Height);
                    conversationList.ScrollToEnd(duration: 0.5f);
                    lastMessageBox.SetBackgroundIcon(eventSprite);
                    return;
                }
            }

            var (relative, min) = GetSizes(dialogType);

            GUIMessageBox messageBox = new GUIMessageBox(string.Empty, string.Empty, Array.Empty<LocalizedString>(), 
                relativeSize: relative, minSize: min,
                type: GUIMessageBox.Type.InGame, backgroundIcon: EventSet.GetEventSprite(spriteIdentifier))
            {
                UserData = "ConversationAction"
            };
            messageBox.OnAddedToGUIUpdateList += (GUIComponent component) =>
            {
                if (Screen.Selected is not GameScreen) { messageBox.Close(); }
            };
            lastMessageBox = messageBox;

            messageBox.InnerFrame.ClearChildren();
            messageBox.AutoClose = false;
            GUIStyle.Apply(messageBox.InnerFrame, "DialogBox");

            if (actionInstance != null)
            {
                lastActiveAction = actionInstance;
                actionInstance.dialogBox = messageBox;
            }
            else
            {
                messageBox.UserData = new Pair<string, UInt16>("ConversationAction", actionId.Value);
            }

            int padding = GUI.IntScale(16);

            GUIListBox listBox = new GUIListBox(new RectTransform(messageBox.InnerFrame.Rect.Size - new Point(padding * 2), messageBox.InnerFrame.RectTransform, Anchor.Center), style: null)
            {
                KeepSpaceForScrollBar = true,
                HoverCursor = CursorState.Default,
                UserData = "conversationlist"
            };

            List<GUIButton> buttons = CreateConversation(listBox, text, speaker, options, string.IsNullOrWhiteSpace(spriteIdentifier));
            AssignActionsToButtons(buttons, messageBox);
            RecalculateLastMessage(listBox, false);

            messageBox.InnerFrame.RectTransform.MinSize = new Point(0, Math.Max(listBox.RectTransform.MinSize.Y + padding * 2, (int)(100 * GUI.yScale)));

            var shadow = new GUIFrame(new RectTransform(messageBox.InnerFrame.Rect.Size + new Point(padding * 4), messageBox.InnerFrame.RectTransform, Anchor.Center), style: "OuterGlow")
            {
                Color = Color.Black * 0.7f
            };
            shadow.SetAsFirstChild();

            static void RecalculateLastMessage(GUIListBox conversationList, bool append)
            {
                if (conversationList.Content.Children.LastOrDefault() is GUILayoutGroup lastElement)
                {
                    GUILayoutGroup textLayout = lastElement.GetChild<GUILayoutGroup>();

                    if (textLayout != null)
                    {
                        if (lastElement.Rect.Size.Y < textLayout.Rect.Size.Y && !append)
                        {
                            lastElement.RectTransform.MinSize = textLayout.Rect.Size;
                        }
                        
                        int textHeight = textLayout.Children.Sum(c => c.Rect.Height);
                        textLayout.RectTransform.MaxSize = new Point(lastElement.RectTransform.MaxSize.X, textHeight);
                        textLayout.Recalculate();
                    }
                    int sumHeight = lastElement.Children.Sum(c => c.Rect.Height);
                    lastElement.RectTransform.MaxSize = new Point(lastElement.RectTransform.MaxSize.X, sumHeight);
                    lastElement.Recalculate();
                    conversationList.RecalculateChildren();

                    if (!append || textLayout == null) { return; }

                    foreach (GUIComponent child in textLayout.Children)
                    {
                        conversationList.UpdateScrollBarSize();
                        float wait = conversationList.BarSize < 1.0f ? 0.5f : 0.0f;

                        if (child is GUITextBlock) { child.FadeIn(wait, 0.5f); }

                        if (child is GUIButton btn)
                        {
                            btn.FadeIn(wait, 1.0f);
                            btn.TextBlock.FadeIn(wait, 0.5f);
                        }
                    }
                }
            }

            void AssignActionsToButtons(List<GUIButton> optionButtons, GUIMessageBox target)
            {
                if (!options.Any())
                {
                    GUIButton closeButton = new GUIButton(new RectTransform(Vector2.One, target.InnerFrame.RectTransform, Anchor.BottomRight, scaleBasis: ScaleBasis.Smallest)
                    {
                        MaxSize = new Point(GUI.IntScale(24)),
                        MinSize = new Point(24),
                        AbsoluteOffset = new Point(GUI.IntScale(48), GUI.IntScale(16))
                    }, style: "GUIButtonVerticalArrow")
                    {
                        UserData = "ContinueButton",
                        IgnoreLayoutGroups = true,
                        Bounce = true,
                        OnClicked = (btn, userdata) =>
                        {
                            if (actionInstance != null)
                            {
                                actionInstance.selectedOption = 0;
                            }
                            else if (actionId.HasValue)
                            {
                                SendResponse(actionId.Value, 0);
                            }

                            if (!continueConversation)
                            {
                                target.Close();
                            }
                            else
                            {
                                btn.Frame.FadeOut(0.33f, true);
                            }

                            return true;
                        }
                    };

                    double allowCloseTime = Timing.TotalTime + 0.5;
                    closeButton.Children.ForEach(child => child.SpriteEffects = SpriteEffects.FlipVertically);
                    closeButton.Frame.FadeIn(0.5f, 0.5f);
                    closeButton.SlideIn(0.5f, 0.33f, 16, SlideDirection.Down);

                    InputType? closeInput = null;
                    if (GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Use].MouseButton == MouseButton.None)
                    {
                        closeInput = InputType.Use;
                    }
                    else if (GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Select].MouseButton == MouseButton.None)
                    {
                        closeInput = InputType.Select;
                    }
                    if (closeInput.HasValue)
                    {
                        closeButton.ToolTip = TextManager.ParseInputTypes($"{TextManager.Get("Close")} ([InputType.{closeInput.Value}])");
                        closeButton.OnAddedToGUIUpdateList += (GUIComponent component) =>
                        {
                            if (Timing.TotalTime > allowCloseTime && PlayerInput.KeyHit(closeInput.Value))
                            {
                                GUIButton btn = component as GUIButton;
                                btn?.OnClicked(btn, btn.UserData);
                                btn?.Flash(GUIStyle.Green);
                            }
                        };
                    }
                }
            
                for (int i = 0; i < optionButtons.Count; i++)
                {
                    optionButtons[i].UserData = i;
                    optionButtons[i].OnClicked += (btn, userdata) =>
                    {
                        int selectedOption = (userdata as int?) ?? 0;
                        if (actionInstance != null)
                        {
                            actionInstance.selectedOption = selectedOption;
                            foreach (GUIButton otherButton in optionButtons)
                            {
                                otherButton.CanBeFocused = false;
                                if (otherButton != btn)
                                {
                                    otherButton.TextBlock.OverrideTextColor(Color.DarkGray * 0.8f);
                                }
                            }
                            btn.ExternalHighlight = true;
                            return true;
                        }

                        if (actionId.HasValue)
                        {
                            SendResponse(actionId.Value, selectedOption);
                            btn.CanBeFocused = false;
                            btn.ExternalHighlight = true;
                            foreach (GUIButton otherButton in optionButtons)
                            {
                                otherButton.CanBeFocused = false;
                                if (otherButton != btn)
                                {
                                    otherButton.TextBlock.OverrideTextColor(Color.DarkGray * 0.8f);
                                }
                            }
                            return true;
                        }
                        //should not happen
                        return false;
                    };

                    if (closingOptions.Contains(i)) { optionButtons[i].OnClicked += target.Close; }
                }
            }
        }

        private static Tuple<Vector2, Point> GetSizes(DialogTypes dialogTypes)
        {
            return dialogTypes switch
            {
                DialogTypes.Regular => Tuple.Create(new Vector2(0.3f, 0.2f), new Point(512, 256)),
                                  _ => Tuple.Create(new Vector2(0.3f, 0.15f), new Point(512, 128))
            };
        }

        private static List<GUIButton> CreateConversation(GUIListBox parentBox, string text, Character speaker, IEnumerable<string> options, bool drawChathead = true)
        {
            var content = new GUILayoutGroup(new RectTransform(Vector2.One, parentBox.Content.RectTransform), childAnchor: Anchor.TopLeft, isHorizontal: true)
            {
                Stretch = true,
                CanBeFocused = true,
                AlwaysOverrideCursor = true
            };

            LocalizedString translatedText = TextManager.ParseInputTypes(TextManager.Get(text)).Fallback(text);

            if (speaker?.Info != null && drawChathead)
            {
                // chathead
                new GUICustomComponent(new RectTransform(new Vector2(0.15f, 0.8f), content.RectTransform), onDraw: (sb, customComponent) =>
                {
                    speaker.Info.DrawIcon(sb, customComponent.Rect.Center.ToVector2(), customComponent.Rect.Size.ToVector2());
                });
            }

            var textContent = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0f), content.RectTransform), childAnchor: Anchor.TopCenter)
            {
                AbsoluteSpacing = GUI.IntScale(5)
            };

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform), translatedText, wrap: true)
            {
                AlwaysOverrideCursor = true,
                UserData = "text"
            };

            List<GUIButton> buttons = new List<GUIButton>();
            if (options.Any())
            {
                foreach (string option in options)
                {
                    var btn = new GUIButton(new RectTransform(new Vector2(0.9f, 0.01f), textContent.RectTransform), TextManager.Get(option).Fallback(option), style: "ListBoxElement");
                    btn.TextBlock.TextAlignment = Alignment.CenterLeft;
                    btn.TextColor = btn.HoverTextColor = GUIStyle.Green;
                    btn.TextBlock.Wrap = true;
                    buttons.Add(btn);
                }
            }

            content.Recalculate();
            textContent.Recalculate();
            textBlock.CalculateHeightFromText();
            textBlock.RectTransform.MinSize = new Point(0, textBlock.Rect.Height);
            foreach (GUIButton btn in buttons)
            {
                btn.TextBlock.SetTextPos();
                btn.TextBlock.CalculateHeightFromText();
                btn.RectTransform.MinSize = new Point(0, (int)(btn.TextBlock.Rect.Height * 1.2f));
            }

            textContent.RectTransform.MinSize = new Point(0, textContent.Children.Sum(c => c.Rect.Height) + GUI.IntScale(16));
            content.RectTransform.MinSize = new Point(0, content.Children.Sum(c => c.Rect.Height));

            // Recalculate the text size as it is scaled up and no longer matching the text height due to the textContent's minSize increasing
            textBlock.CalculateHeightFromText();
            textBlock.TextAlignment = Alignment.TopLeft;
            //content.RectTransform.MinSize = new Point(0, textContent.Rect.Height);

            return buttons;
        }

        private static void SendResponse(UInt16 actionId, int selectedOption)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ClientPacketHeader.EVENTMANAGER_RESPONSE);
            outmsg.WriteUInt16(actionId);
            outmsg.WriteByte((byte)selectedOption);
            GameMain.Client?.ClientPeer?.Send(outmsg, DeliveryMethod.Reliable);
        }

        private static void SendIgnore(UInt16 actionId)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ClientPacketHeader.EVENTMANAGER_RESPONSE);
            outmsg.WriteUInt16(actionId);
            outmsg.WriteByte(byte.MaxValue);
            GameMain.Client?.ClientPeer?.Send(outmsg, DeliveryMethod.Reliable);
        }

        // Too broken, left it here if I ever want to come back to it
        /*private static List<RichTextData> GetQuoteHighlights(string text, Color color)
        {
            char[] quotes = { '“', '”', '\"', '\'', '「', '」'};

            List<RichTextData> textColors = new List<RichTextData> { new RichTextData { StartIndex = 0 } };
            bool start = true;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quotes.Contains(c))
                {
                    textColors.Last().EndIndex = i - 1;
                    textColors.Add(new RichTextData { StartIndex = i, Color = start ? color : (Color?) null });
                    start = !start;
                }
            }

            if (textColors.LastOrDefault() is { } last && last.EndIndex == 0)
            {
                last.EndIndex = text.Length;
            }
            return textColors;
        }*/
    }
}
