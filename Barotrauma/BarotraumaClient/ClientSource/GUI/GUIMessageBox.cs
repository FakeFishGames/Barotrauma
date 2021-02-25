using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    public class GUIMessageBox : GUIFrame
    {
        public static List<GUIComponent> MessageBoxes = new List<GUIComponent>();
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
            Vote
        }

        public List<GUIButton> Buttons { get; private set; } = new List<GUIButton>();
        //public GUIFrame BackgroundFrame { get; private set; }
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

        private readonly bool alwaysVisible;

        private float openState;
        private float iconState;
        private bool iconSwitching;
        private bool closing;

        private readonly Type type;

        public Type MessageBoxType => type;

        public static GUIComponent VisibleBox => MessageBoxes.LastOrDefault();

        public GUIMessageBox(string headerText, string text, Vector2? relativeSize = null, Point? minSize = null)
            : this(headerText, text, new string[] { "OK" }, relativeSize, minSize)
        {
            this.Buttons[0].OnClicked = Close;
        }

        public GUIMessageBox(string headerText, string text, string[] buttons, Vector2? relativeSize = null, Point? minSize = null, Alignment textAlignment = Alignment.TopLeft, Type type = Type.Default, string tag = "", Sprite icon = null, string iconStyle = "", Sprite backgroundIcon = null)
            : base(new RectTransform(GUI.Canvas.RelativeSize, GUI.Canvas, Anchor.Center), style: GUI.Style.GetComponentStyle("GUIMessageBox." + type) != null ? "GUIMessageBox." + type : "GUIMessageBox")
        {
            int width = (int)(DefaultWidth * (type == Type.Default ? 1.0f : 1.5f)), height = 0;
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
            GUI.Style.Apply(InnerFrame, "", this);
            this.type = type;
            Tag = tag;

            if (type == Type.Default || type == Type.Vote)
            {
                Content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), InnerFrame.RectTransform, Anchor.Center)) { AbsoluteSpacing = 5 };
                            
                Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), 
                    headerText, font: GUI.SubHeadingFont, textAlignment: Alignment.Center, wrap: true);
                GUI.Style.Apply(Header, "", this);
                Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), text, textAlignment: textAlignment, wrap: true);
                    GUI.Style.Apply(Text, "", this);
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
                var buttonStyle = GUI.Style.GetComponentStyle("GUIButton");
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
                alwaysVisible = true;
                CanBeFocused = false;
                AutoClose = true;
                GUI.Style.Apply(InnerFrame, "", this);

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
                if (GameMain.Config.KeyBind(InputType.Use).MouseButton == MouseButton.None)
                {
                    closeInput = InputType.Use;
                }
                else if (GameMain.Config.KeyBind(InputType.Select).MouseButton == MouseButton.None)
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
                            btn?.Flash(GUI.Style.Green);
                        }
                    };
                }

                Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), headerText, wrap: true);
                GUI.Style.Apply(Header, "", this);
                Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), text, textAlignment: textAlignment, wrap: true);
                    GUI.Style.Apply(Text, "", this);
                    Content.Recalculate();
                    Text.RectTransform.NonScaledSize = Text.RectTransform.MinSize = Text.RectTransform.MaxSize =
                        new Point(Text.Rect.Width, Text.Rect.Height);
                    Text.RectTransform.IsFixedSize = true;
                    if (string.IsNullOrWhiteSpace(headerText))
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
            
            MessageBoxes.Add(this);
        }

        public static void AddActiveToGUIUpdateList()
        {
            for (int i = 0; i < MessageBoxes.Count; i++)
            {
                if (MessageBoxes[i] is GUIMessageBox alwaysVisibleMsgBox && alwaysVisibleMsgBox.alwaysVisible)
                {
                    alwaysVisibleMsgBox.AddToGUIUpdateList();
                    break;
                }
            }
            for (int i = MessageBoxes.Count - 1; i >= 0; i--)
            {
                if (MessageBoxes[i].UserData as string == "verificationprompt" ||
                    MessageBoxes[i].UserData as string == "bugreporter")
                {
                    continue;
                }
                if (!(MessageBoxes[i] is GUIMessageBox msgBox) || !msgBox.alwaysVisible)
                {
                    MessageBoxes[i].AddToGUIUpdateList();
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

            if (type == Type.InGame)
            {
                Vector2 initialPos = new Vector2(0.0f, GameMain.GraphicsHeight);
                Vector2 defaultPos = new Vector2(0.0f, HUDLayoutSettings.InventoryAreaLower.Y - InnerFrame.Rect.Height - 20 * GUI.Scale);
                Vector2 endPos = new Vector2(GameMain.GraphicsWidth, defaultPos.Y);

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
                        if (Parent != null) { Parent.RemoveChild(this); }
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
            if (type == Type.InGame)
            {
                closing = true;
            }
            else
            {
                if (Parent != null) { Parent.RemoveChild(this); }
                if (MessageBoxes.Contains(this)) { MessageBoxes.Remove(this); }
            }

            Closed = true;
        }

        public bool Close(GUIButton button, object obj)
        {
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
