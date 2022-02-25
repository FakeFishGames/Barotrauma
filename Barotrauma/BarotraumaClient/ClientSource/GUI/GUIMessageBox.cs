using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class GUIMessageBox : GUIFrame
    {
        #warning TODO: change this to List<GUIMessageBox> and fix incorrect uses of this list
        public readonly static List<GUIComponent> MessageBoxes = new List<GUIComponent>();
        private static int DefaultWidth
        {
            get { return Math.Max(400, (int)(400 * (GameMain.GraphicsWidth / GUI.ReferenceResolution.X))); }
        }

        private float inGameCloseTimer = 0.0f;
        private const float inGameCloseTime = 15f;

        public enum Type
        {
            Default,
            InGame,
            Vote,
            Hint
        }

        public List<GUIButton> Buttons { get; private set; } = new List<GUIButton>();
        public GUILayoutGroup Content { get; private set; }
        public GUIFrame InnerFrame { get; private set; }
        public GUITextBlock Header { get; private set; }
        public GUITextBlock Text { get; private set; }
        public string Tag { get; private set; }
        public bool Closed { get; private set; }

        public GUIImage Icon
        {
            get;
            private set;
        }

        public Color IconColor
        {
            get { return Icon == null ? Color.White : Icon.Color; }
            set
            {
                if (Icon == null) { return; }
                Icon.Color = value;
            }
        }
        
        public bool Draggable { get; set; }
        public Vector2 DraggingPosition = Vector2.Zero;

        public GUIImage BackgroundIcon { get; private set; }
        private GUIImage newBackgroundIcon;

        public bool AutoClose;

        private float openState;
        private float iconState;
        private bool iconSwitching;
        private bool closing;

        private readonly Type type;

        public Type MessageBoxType => type;

        public static GUIComponent VisibleBox => MessageBoxes.LastOrDefault();

        public GUIMessageBox(LocalizedString headerText, LocalizedString text, Vector2? relativeSize = null, Point? minSize = null)
            : this(headerText, text, new LocalizedString[] { "OK" }, relativeSize, minSize)
        {
            this.Buttons[0].OnClicked = Close;
        }

        public GUIMessageBox(RichString headerText, RichString text, LocalizedString[] buttons, Vector2? relativeSize = null, Point? minSize = null, Alignment textAlignment = Alignment.TopLeft, Type type = Type.Default, string tag = "", Sprite icon = null, string iconStyle = "", Sprite backgroundIcon = null)
            : base(new RectTransform(GUI.Canvas.RelativeSize, GUI.Canvas, Anchor.Center), style: GUIStyle.GetComponentStyle("GUIMessageBox." + type) != null ? "GUIMessageBox." + type : "GUIMessageBox")
        {
            int width = (int)(DefaultWidth * type switch
            {
                Type.Default => 1.0f,
                Type.Hint => 1.25f,
                _ => 1.5f
            });
            int height = 0;

            if (relativeSize.HasValue)
            {
                width = (int)(GameMain.GraphicsWidth * relativeSize.Value.X);
                height = (int)(GameMain.GraphicsHeight * relativeSize.Value.Y);
            }
            if (minSize.HasValue)
            {
                width = Math.Max(width, minSize.Value.X);
                if (height > 0)
                {
                    height = Math.Max(height, minSize.Value.Y);
                }
            }

            if (backgroundIcon != null)
            {
                BackgroundIcon = new GUIImage(new RectTransform(backgroundIcon.size.ToPoint(), RectTransform), backgroundIcon)
                {
                    IgnoreLayoutGroups = true,
                    Color = Color.Transparent
                };
            }

            Anchor anchor = type switch
            {
                Type.InGame => Anchor.TopCenter,
                Type.Hint => Anchor.TopRight,
                Type.Vote => Anchor.TopRight,
                _ => Anchor.Center
            };

            InnerFrame = new GUIFrame(new RectTransform(new Point(width, height), RectTransform, anchor) { IsFixedSize = false }, style: null);
            if (type == Type.Vote)
            {
                int offset = GUI.IntScale(64);
                InnerFrame.RectTransform.ScreenSpaceOffset = new Point(-offset, offset);
                CanBeFocused = false;
            }
            GUIStyle.Apply(InnerFrame, "", this);
            this.type = type;
            Tag = tag;

            #warning TODO: These should be broken into separate methods at least
            if (type == Type.Default || type == Type.Vote)
            {
                Content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), InnerFrame.RectTransform, Anchor.Center)) { AbsoluteSpacing = 5 };
                            
                Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), 
                    headerText, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center, wrap: true);
                GUIStyle.Apply(Header, "", this);
                Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

                if (!text.IsNullOrWhiteSpace())
                {
                    Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), text, textAlignment: textAlignment, wrap: true);
                    GUIStyle.Apply(Text, "", this);
                    Text.RectTransform.NonScaledSize = Text.RectTransform.MinSize = Text.RectTransform.MaxSize = 
                        new Point(Text.Rect.Width, Text.Rect.Height);
                    Text.RectTransform.IsFixedSize = true;
                }

                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), Content.RectTransform, Anchor.BottomCenter), childAnchor: Anchor.TopCenter)
                {
                    AbsoluteSpacing = 5,
                    IgnoreLayoutGroups = true
                };

                int buttonSize = 35;
                var buttonStyle = GUIStyle.GetComponentStyle("GUIButton");
                if (buttonStyle != null && buttonStyle.Height.HasValue)
                {
                    buttonSize = buttonStyle.Height.Value;
                }

                buttonContainer.RectTransform.NonScaledSize = buttonContainer.RectTransform.MinSize = buttonContainer.RectTransform.MaxSize = 
                    new Point(buttonContainer.Rect.Width, (int)((buttonSize + 5) * buttons.Length));
                buttonContainer.RectTransform.IsFixedSize = true;

                if (height == 0)
                {
                    height += Header.Rect.Height + Content.AbsoluteSpacing;
                    height += (Text == null ? 0 : Text.Rect.Height) + Content.AbsoluteSpacing;
                    height += buttonContainer.Rect.Height + 20;
                    if (minSize.HasValue) { height = Math.Max(height, minSize.Value.Y); }

                    InnerFrame.RectTransform.NonScaledSize = 
                        new Point(InnerFrame.Rect.Width, (int)Math.Max(height / Content.RectTransform.RelativeSize.Y, height + (int)(50 * GUI.yScale)));
                    Content.RectTransform.NonScaledSize =
                        new Point(Content.Rect.Width, height);
                }

                Buttons = new List<GUIButton>(buttons.Length);
                for (int i = 0; i < buttons.Length; i++)
                {
                    var button = new GUIButton(new RectTransform(new Vector2(0.6f, 1.0f / buttons.Length), buttonContainer.RectTransform), buttons[i]);
                    Buttons.Add(button);
                }
            }
            else if (type == Type.InGame)
            {
                InnerFrame.RectTransform.AbsoluteOffset = new Point(0, GameMain.GraphicsHeight);
                CanBeFocused = false;
                AutoClose = true;
                GUIStyle.Apply(InnerFrame, "", this);

                var horizontalLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.95f), InnerFrame.RectTransform, Anchor.Center), 
                    isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };
                if (icon != null)
                {
                    Icon = new GUIImage(new RectTransform(new Vector2(0.2f, 0.95f), horizontalLayoutGroup.RectTransform), icon, scaleToFit: true);
                }
                else if (iconStyle != string.Empty)
                {
                    Icon = new GUIImage(new RectTransform(new Vector2(0.2f, 0.95f), horizontalLayoutGroup.RectTransform), iconStyle, scaleToFit: true);
                }

                Content = new GUILayoutGroup(new RectTransform(new Vector2(Icon != null ? 0.65f : 0.85f, 1.0f), horizontalLayoutGroup.RectTransform));

                var buttonContainer = new GUIFrame(new RectTransform(new Vector2(0.15f, 1.0f), horizontalLayoutGroup.RectTransform), style: null);
                Buttons = new List<GUIButton>(1)
                {
                    new GUIButton(new RectTransform(new Vector2(0.3f, 0.5f), buttonContainer.RectTransform, Anchor.Center), 
                        style: "UIToggleButton")
                    {
                        OnClicked = Close
                    }
                };

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
                    Buttons[0].ToolTip = TextManager.ParseInputTypes($"{TextManager.Get("Close")} ([InputType.{closeInput.Value}])");
                    Buttons[0].OnAddedToGUIUpdateList += (GUIComponent component) =>
                    {
                        if (!closing && openState >= 1.0f && PlayerInput.KeyHit(closeInput.Value))
                        {
                            GUIButton btn = component as GUIButton;
                            btn?.OnClicked(btn, btn.UserData);
                            btn?.Flash(GUIStyle.Green);
                        }
                    };
                }

                Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), headerText, wrap: true);
                GUIStyle.Apply(Header, "", this);
                Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

                if (!text.IsNullOrWhiteSpace())
                {
                    Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), text, textAlignment: textAlignment, wrap: true);
                    GUIStyle.Apply(Text, "", this);
                    Content.Recalculate();
                    Text.RectTransform.NonScaledSize = Text.RectTransform.MinSize = Text.RectTransform.MaxSize =
                        new Point(Text.Rect.Width, Text.Rect.Height);
                    Text.RectTransform.IsFixedSize = true;
                    if (headerText.IsNullOrWhiteSpace())
                    {
                        Content.ChildAnchor = Anchor.Center;
                    }
                }

                if (height == 0)
                {
                    height += Header.Rect.Height + Content.AbsoluteSpacing;
                    height += (Text == null ? 0 : Text.Rect.Height) + Content.AbsoluteSpacing;
                    if (minSize.HasValue) { height = Math.Max(height, minSize.Value.Y); }

                    InnerFrame.RectTransform.NonScaledSize =
                        new Point(InnerFrame.Rect.Width, (int)Math.Max(height / Content.RectTransform.RelativeSize.Y, height + (int)(50 * GUI.yScale)));
                    Content.RectTransform.NonScaledSize =
                        new Point(Content.Rect.Width, height);
                }
                Buttons[0].RectTransform.MaxSize = new Point((int)(0.4f * Buttons[0].Rect.Y), Buttons[0].Rect.Y);
            }
            else if (type == Type.Hint)
            {
                CanBeFocused = false;
                GUIStyle.Apply(InnerFrame, "", this);

                Point absoluteSpacing = GUIStyle.ItemFrameMargin.Multiply(1.0f / 5.0f);
                var verticalLayoutGroup = new GUILayoutGroup(new RectTransform(GetVerticalLayoutGroupSize(), parent: InnerFrame.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter)
                {
                    AbsoluteSpacing = absoluteSpacing.Y,
                    Stretch = true
                };

                var topHorizontalLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.7f), verticalLayoutGroup.RectTransform),
                    isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };

                int iconMaxHeight = 0;
                if (icon != null)
                {
                    Icon = new GUIImage(new RectTransform(new Vector2(0.15f, 0.95f), topHorizontalLayoutGroup.RectTransform), icon, scaleToFit: true);
                    iconMaxHeight = (int)Icon.Sprite.size.Y;
                }
                else
                {
                    bool iconStyleDefined = !string.IsNullOrEmpty(iconStyle);
                    Icon = new GUIImage(new RectTransform(new Vector2(0.15f, 0.95f), topHorizontalLayoutGroup.RectTransform),
                        iconStyleDefined ? iconStyle : "GUIButtonInfo", scaleToFit: true);
                    if (!iconStyleDefined)
                    {
                        Icon.Color = Color.Orange;
                    }
                    iconMaxHeight = (int)(Icon.Style.GetDefaultSprite()?.size.Y ?? GUI.yScale * 40);
                }

                iconMaxHeight = Math.Min((int)(GUI.yScale * 40), iconMaxHeight);
                int iconMinHeight = Math.Min((int)(GUI.yScale * 40), iconMaxHeight);
                Icon.RectTransform.MinSize = new Point(Icon.Rect.Width, iconMinHeight);
                Icon.RectTransform.MaxSize = new Point(Icon.Rect.Width, iconMaxHeight);

                Content = new GUILayoutGroup(new RectTransform(new Vector2(Icon != null ? 0.85f : 1.0f, 1.0f), topHorizontalLayoutGroup.RectTransform))
                {
                    AbsoluteSpacing = absoluteSpacing.Y,
                };

                var bottomContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.3f), verticalLayoutGroup.RectTransform), style: null)
                {
                    CanBeFocused = true
                };

                var tickBoxLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.67f, 1.0f), bottomContainer.RectTransform, anchor: Anchor.CenterLeft))
                {
                    CanBeFocused = true,
                    Stretch = true
                };
                Vector2 tickBoxRelativeSize = new Vector2(1.0f, 0.5f);
                var dontShowAgainTickBox = new GUITickBox(new RectTransform(tickBoxRelativeSize, tickBoxLayoutGroup.RectTransform),
                    TextManager.Get("hintmessagebox.dontshowagain"))
                {
                    ToolTip = TextManager.Get("hintmessagebox.dontshowagaintooltip"),
                    UserData = "dontshowagain"
                };
                var disableHintsTickBox = new GUITickBox(new RectTransform(tickBoxRelativeSize, tickBoxLayoutGroup.RectTransform),
                    TextManager.Get("hintmessagebox.disablehints"))
                {
                    ToolTip = TextManager.Get("hintmessagebox.disablehintstooltip"),
                    UserData = "disablehints"
                };

                Buttons = new List<GUIButton>(1)
                {
                    new GUIButton(new RectTransform(new Vector2(0.33f, 1.0f), bottomContainer.RectTransform, Anchor.CenterRight),
                        text: TextManager.Get("hintmessagebox.dismiss"), style: "GUIButtonSmall")
                    {
                        OnClicked = Close
                    }
                };

                Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), headerText, wrap: true);
                GUIStyle.Apply(Header, "", this);
                Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

                if (!text.IsNullOrWhiteSpace())
                {
                    Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), text, textAlignment: textAlignment, wrap: true);
                    GUIStyle.Apply(Text, "", this);
                    Content.Recalculate();
                    Text.RectTransform.NonScaledSize = Text.RectTransform.MinSize = Text.RectTransform.MaxSize =
                        new Point(Text.Rect.Width, Text.Rect.Height);
                    Text.RectTransform.IsFixedSize = true;
                    if (headerText.IsNullOrWhiteSpace())
                    {
                        Header.RectTransform.Parent = null;
                        Content.ChildAnchor = Anchor.Center;
                    }
                }

                if (height == 0)
                {
                    height = absoluteSpacing.Y;
                    int upperContainerHeight = absoluteSpacing.Y;
                    if (Header.Rect.Height > 0) { upperContainerHeight += Header.Rect.Height + Content.AbsoluteSpacing; }
                    if (Text != null) { upperContainerHeight += Text.Rect.Height + Content.AbsoluteSpacing; }
                    upperContainerHeight = Math.Max(upperContainerHeight, Icon.Rect.Height);
                    height += upperContainerHeight;
                    height += absoluteSpacing.Y;
                    int bottomContainerHeight = dontShowAgainTickBox.Rect.Height + disableHintsTickBox.Rect.Height;
                    height += bottomContainerHeight;
                    height += absoluteSpacing.Y;
                    if (minSize.HasValue) { height = Math.Max(height, minSize.Value.Y); }

                    InnerFrame.RectTransform.NonScaledSize = new Point(InnerFrame.Rect.Width, height);
                    verticalLayoutGroup.RectTransform.NonScaledSize = GetVerticalLayoutGroupSize();
                    float upperContainerRelativeHeight = (float)upperContainerHeight / (upperContainerHeight + bottomContainerHeight);
                    topHorizontalLayoutGroup.RectTransform.RelativeSize = new Vector2(topHorizontalLayoutGroup.RectTransform.RelativeSize.X, upperContainerRelativeHeight);
                    bottomContainer.RectTransform.RelativeSize = new Vector2(bottomContainer.RectTransform.RelativeSize.X, 1.0f - upperContainerRelativeHeight);
                    verticalLayoutGroup.Recalculate();
                    topHorizontalLayoutGroup.Recalculate();
                    Content.Recalculate();
                    tickBoxLayoutGroup.Recalculate();
                }

                InnerFrame.RectTransform.AbsoluteOffset = new Point(GUI.IntScale(64), -InnerFrame.Rect.Height);

                Point GetVerticalLayoutGroupSize()
                {
                    return InnerFrame.Rect.Size - absoluteSpacing.Multiply(2);
                }
            }

            MessageBoxes.Add(this);
        }

        /// <summary>
        /// Use to create a message box of Hint type
        /// </summary>
        public GUIMessageBox(Identifier hintIdentifier, LocalizedString text, Sprite icon) : this("", text, Array.Empty<LocalizedString>(), textAlignment: Alignment.CenterLeft, type: Type.Hint, icon: icon)
        {
            if (InnerFrame.FindChild("dontshowagain", recursive: true) is GUITickBox dontShowAgainTickBox)
            {
                dontShowAgainTickBox.OnSelected = HintManager.OnDontShowAgain;
                dontShowAgainTickBox.UserData = hintIdentifier;
            }
            if (InnerFrame.FindChild("disablehints", recursive: true) is GUITickBox disableHintsTickBox)
            {
                disableHintsTickBox.OnSelected = HintManager.OnDisableHints;
                disableHintsTickBox.UserData = hintIdentifier;
            }
        }

        private static Type[] messageBoxTypes;

        public static void AddActiveToGUIUpdateList()
        {
            messageBoxTypes ??= (Type[])Enum.GetValues(typeof(Type));

            foreach (var type in messageBoxTypes)
            {
                // Don't display hints when HUD is disabled
                if (type == Type.Hint && GUI.DisableHUD) { continue; } 

                for (int i = 0; i < MessageBoxes.Count; i++)
                {
                    if (MessageBoxes[i] == null) { continue; }
                    if (!(MessageBoxes[i] is GUIMessageBox messageBox))
                    {
                        if (type == Type.Default)
                        {
                            // Message box not of type GUIMessageBox is likely the round summary
                            MessageBoxes[i].AddToGUIUpdateList();
                            break;
                        }
                        continue;
                    }
                    if (messageBox.type != type) { continue; }

                    // These are handled separately in GUI.HandlePersistingElements()
                    if (MessageBoxes[i].UserData as string == "verificationprompt") { continue; }
                    if (MessageBoxes[i].UserData as string == "bugreporter") { continue; }

                    messageBox.AddToGUIUpdateList();
                    break;
                }
            }
        }

        public void SetBackgroundIcon(Sprite icon)
        {
            if (icon == null) { return; }
            GUIImage newIcon = new GUIImage(new RectTransform(icon.size.ToPoint(), RectTransform), icon)
            {
                IgnoreLayoutGroups = true,
                Color = Color.Transparent
            };

            if (newBackgroundIcon != null)
            {
                RemoveChild(newBackgroundIcon);
                newBackgroundIcon = null;
            }
            newBackgroundIcon = newIcon;
        }

        protected override void Update(float deltaTime)
        {
            if (Draggable)
            {
                GUIComponent parent = GUI.MouseOn?.Parent?.Parent;
                if ((GUI.MouseOn == InnerFrame || InnerFrame.IsParentOf(GUI.MouseOn)) && !(GUI.MouseOn is GUIButton || GUI.MouseOn is GUIColorPicker || GUI.MouseOn is GUITextBox || parent is GUITextBox))
                {
                    GUI.MouseCursor = CursorState.Move;
                    if (PlayerInput.PrimaryMouseButtonDown())
                    {
                        DraggingPosition = RectTransform.ScreenSpaceOffset.ToVector2() - PlayerInput.MousePosition;
                    }
                }

                if (PlayerInput.PrimaryMouseButtonHeld() && DraggingPosition != Vector2.Zero)
                {
                    GUI.MouseCursor = CursorState.Dragging;
                    RectTransform.ScreenSpaceOffset = (PlayerInput.MousePosition + DraggingPosition).ToPoint();
                }
                else
                {
                    DraggingPosition = Vector2.Zero;
                }
            }

            if (type == Type.InGame || type == Type.Hint)
            {
                Vector2 initialPos, defaultPos, endPos;
                if (type == Type.InGame)
                {
                    initialPos = new Vector2(0.0f, GameMain.GraphicsHeight);
                    defaultPos = new Vector2(0.0f, HUDLayoutSettings.InventoryAreaLower.Y - InnerFrame.Rect.Height - 20 * GUI.Scale);
                    endPos = new Vector2(GameMain.GraphicsWidth, defaultPos.Y);
                }
                else
                {
                    initialPos = new Vector2(GUI.IntScale(64), -InnerFrame.Rect.Height);
                    defaultPos = new Vector2(initialPos.X, HUDLayoutSettings.ButtonAreaTop.Height + GUI.IntScale(64));
                    endPos = new Vector2(-InnerFrame.Rect.Width, defaultPos.Y);
                }

                if (!closing)
                {
                    Point step = Vector2.SmoothStep(initialPos, defaultPos, openState).ToPoint();
                    InnerFrame.RectTransform.AbsoluteOffset = step;
                    if (BackgroundIcon != null)
                    {
                        BackgroundIcon.RectTransform.AbsoluteOffset = new Point(InnerFrame.Rect.Location.X - (int)(BackgroundIcon.Rect.Size.X / 1.25f), (int)defaultPos.Y - BackgroundIcon.Rect.Size.Y / 2);
                        if (!MathUtils.NearlyEqual(openState, 1.0f))
                        {
                            BackgroundIcon.Color = ToolBox.GradientLerp(openState, Color.Transparent, Color.White);
                        }
                    }
                    if (!(Screen.Selected is RoundSummaryScreen) && !MessageBoxes.Any(mb => mb.UserData is RoundSummary))
                    {
                        openState = Math.Min(openState + deltaTime * 2.0f, 1.0f);
                    }

                    if (GUI.MouseOn != InnerFrame && !InnerFrame.IsParentOf(GUI.MouseOn) && AutoClose)
                    {
                        inGameCloseTimer += deltaTime;
                    }

                    if (inGameCloseTimer >= inGameCloseTime)
                    {
                        Close();
                    }
                }
                else
                {
                    openState += deltaTime * 2.0f;
                    Point step = Vector2.SmoothStep(defaultPos, endPos, openState - 1.0f).ToPoint();
                    InnerFrame.RectTransform.AbsoluteOffset = step;
                    if (BackgroundIcon != null)
                    {
                        BackgroundIcon.Color *= 0.9f;
                    }
                    if (openState >= 2.0f)
                    {
                        Parent?.RemoveChild(this);
                        if (MessageBoxes.Contains(this)) { MessageBoxes.Remove(this); }
                    }
                }

                if (newBackgroundIcon != null)
                {
                    if (!iconSwitching)
                    {
                        if (BackgroundIcon != null)
                        {
                            BackgroundIcon.Color *= 0.9f;
                            if (BackgroundIcon.Color.A == 0)
                            {
                                BackgroundIcon = null;
                                iconSwitching = true;
                                RemoveChild(BackgroundIcon);
                            }
                        }
                        else
                        {
                            iconSwitching = true;
                        }
                        iconState = 0;
                    }
                    else
                    {
                        newBackgroundIcon.SetAsFirstChild();
                        newBackgroundIcon.RectTransform.AbsoluteOffset = new Point(InnerFrame.Rect.Location.X - (int)(newBackgroundIcon.Rect.Size.X / 1.25f), (int)defaultPos.Y - newBackgroundIcon.Rect.Size.Y / 2);
                        newBackgroundIcon.Color = ToolBox.GradientLerp(iconState, Color.Transparent, Color.White);
                        if (newBackgroundIcon.Color.A == 255)
                        {
                            BackgroundIcon = newBackgroundIcon;
                            BackgroundIcon.SetAsFirstChild();
                            newBackgroundIcon = null;
                            iconSwitching = false;
                        }

                        iconState = Math.Min(iconState + deltaTime * 2.0f, 1.0f);
                    }
                }
            }
        }


        public void Close()
        {
            if (type == Type.InGame || type == Type.Hint)
            {
                closing = true;
            }
            else
            {
                Parent?.RemoveChild(this);
                if (MessageBoxes.Contains(this)) { MessageBoxes.Remove(this); }
            }

            Closed = true;
        }

        public bool Close(GUIButton button, object obj)
        {
            RectTransform.Parent = null;
            Close();            
            return true;
        }

        public static void CloseAll()
        {
            MessageBoxes.Clear();
        }

        /// <summary>
        /// Parent does not matter. It's overridden.
        /// </summary>
        public void AddButton(RectTransform rectT, string text, GUIButton.OnClickedHandler onClick)
        {
            rectT.Parent = RectTransform;
            Buttons.Add(new GUIButton(rectT, text) { OnClicked = onClick });
        }
    }
}
