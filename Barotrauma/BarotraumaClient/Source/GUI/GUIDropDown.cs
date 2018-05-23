using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class GUIDropDown : GUIComponent
    {
        public delegate bool OnSelectedHandler(GUIComponent selected, object obj = null);
        public OnSelectedHandler OnSelected;
        public OnSelectedHandler OnDropped;

        private GUIButton button;
        private GUIListBox listBox;

        public bool Dropped { get; set; }

        public object SelectedItemData
        {
            get
            {
                if (listBox.Selected == null) return null;
                return listBox.Selected.UserData;
            }
        }

        public bool Enabled
        {
            get { return listBox.Enabled; }
            set { listBox.Enabled = value; }
        }

        public GUIComponent Selected
        {
            get { return listBox.Selected; }
        }

        public GUIListBox ListBox
        {
            get { return listBox; }
        }

        public object SelectedData
        {
            get
            {
                return (listBox.Selected == null) ? null : listBox.Selected.UserData;
            }
        }

        public int SelectedIndex
        {
            get
            {
                if (listBox.Selected == null) return -1;
                return listBox.Content.Children.FindIndex(x => x == listBox.Selected);
            }
        }

        public override string ToolTip
        {
            get
            {
                return base.ToolTip;
            }
            set
            {
                base.ToolTip    = value;
                button.ToolTip  = value;
                listBox.ToolTip = value;
            }
        }


        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }

            set
            {
                Point moveAmount = value.Location - Rect.Location;
                base.Rect = value;

                button.Rect = new Rectangle(button.Rect.Location + moveAmount, button.Rect.Size);
                listBox.Rect = new Rectangle(listBox.Rect.Location + moveAmount, listBox.Rect.Size);
            }
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIDropDown(Rectangle rect, string text, string style, GUIComponent parent = null)
            : this(rect, text, style, Alignment.TopLeft, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIDropDown(Rectangle rect, string text, string style, Alignment alignment, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;

            if (parent != null) parent.AddChild(this);

            button = new GUIButton(this.rect, text, Color.White, alignment, Alignment.CenterLeft, "GUIDropDown", null);
            GUI.Style.Apply(button, style, this);

            button.OnClicked = OnClicked;

            listBox = new GUIListBox(new Rectangle(this.rect.X, this.rect.Bottom, this.rect.Width, 200), style, null);
            listBox.OnSelected = SelectItem;
            // In the old system the GUIDropDown seems to be drawn in a wrong place, so let's disable drawing.
            // Seems to not happen in the new system.
            AutoDraw = false;
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIDropDown(RectTransform rectT, string text = "", int elementCount = 3, string style = "") : base(style, rectT)
        {
            button = new GUIButton(new RectTransform(Vector2.One, rectT), text, Alignment.CenterLeft, style: "GUIDropDown")
            {
                OnClicked = OnClicked
            };
            GUI.Style.Apply(button, style, this);
            
            listBox = new GUIListBox(new RectTransform(new Point(Rect.Width, Rect.Height * MathHelper.Clamp(elementCount - 1, 5, 10)), rectT, Anchor.BottomLeft, Pivot.TopLeft)
            {
                IsFixedSize = false
            }, style: style)
            {
                OnSelected = SelectItem
            };
        }

        public override void AddChild(GUIComponent child)
        {
            listBox.AddChild(child);
            child.ClampMouseRectToParent = false;
        }

        public void AddItem(string text, object userData = null, string toolTip = "")
        {
            GUITextBlock textBlock = null;
            if (RectTransform != null)
            {
                textBlock = new GUITextBlock(new RectTransform(new Point(button.Rect.Width, button.Rect.Height), listBox.Content.RectTransform)
                {
                    IsFixedSize = false
                }, text, style: "ListBoxElement");
                // In the old system, this is automatically called, because it's defined in the GUIComponent level.
                // The trick is that since the old textbox constructor calls parent.AddChild, it uses listboxes overloaded method, which is quite different from the GUIComponent method.
                // We will want to use this method with the new system also, because it updates the scroll bar.
                // However, we don't want to call it in the textbox constructor, because in the new system, we don't want to manually add children on just any elements.
                // Instead, parenting is handled by assigning the parent of the rect transform.
                // Therefore we have to call listbox.AddChild here, but not with the old elements.
                listBox.AddChild(textBlock);
            }
            else
            {
                textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 20), text, "ListBoxElement", Alignment.TopLeft, Alignment.CenterLeft, listBox.Content);
            }
            textBlock.UserData = userData;
            textBlock.ToolTip = toolTip;
            textBlock.ClampMouseRectToParent = false;
        }

        public override void ClearChildren()
        {
            listBox.ClearChildren();
        }

        public List<GUIComponent> GetChildren()
        {
            return listBox.Content.Children;
        }

        private bool SelectItem(GUIComponent component, object obj)
        {
            GUITextBlock textBlock = component as GUITextBlock;
            if (textBlock == null)
            {
                textBlock = component.GetChild<GUITextBlock>();
                if (textBlock == null) return false;
            }
            button.Text = textBlock.Text;
            Dropped = false;
            OnSelected?.Invoke(component, component.UserData);
            return true;
        }

        public void SelectItem(object userData)
        {
            listBox.Select(userData);
        }

        public void Select(int index)
        {
            listBox.Select(index);
        }

        private bool wasOpened;

        private bool OnClicked(GUIComponent component, object obj)
        {
            if (wasOpened) return false;
            
            wasOpened = true;
            Dropped = !Dropped;
            if (Dropped)
            {
                if (Enabled)
                {
                    OnDropped?.Invoke(this, userData);
                }
                if (RectTransform != null)
                {
                    RectTransform.SetAsLastChild();
                }
                else
                {
                    if (Parent != null && Parent.Children.Last() != this)
                    {
                        Parent.RemoveChild(this);
                        Parent.AddChild(this);
                    }
                }
            }
            return true;
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            base.AddToGUIUpdateList(true, order);
            if (!ignoreChildren)
            {
                button.AddToGUIUpdateList(false, order);
                if (Dropped)
                {
                    listBox.AddToGUIUpdateList(false, order);
                }
            }
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;
            wasOpened = false;
            base.Update(deltaTime);
            if (Dropped && PlayerInput.LeftButtonClicked())
            {
                Rectangle listBoxRect = listBox.Rect;
                listBoxRect.Width += 20; // ?
                if (!listBoxRect.Contains(PlayerInput.MousePosition) && !button.Rect.Contains(PlayerInput.MousePosition))
                {
                    Dropped = false;
                }
            }
        }
    }
}
