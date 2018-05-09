using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

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
                return listBox.Children.FindIndex(x => x == listBox.Selected);
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

        public GUIDropDown(Rectangle rect, string text, string style, GUIComponent parent = null)
            : this(rect, text, style, Alignment.TopLeft, parent)
        {
        }

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
            if (!string.IsNullOrEmpty(style))
            {
                GUI.Style.Apply(button, style, this);
            }
            listBox = new GUIListBox(new RectTransform(new Point(Rect.Width, Rect.Height * MathHelper.Clamp(elementCount - 1, 2, 10)), rectT, Anchor.BottomLeft, Pivot.TopLeft), style: style)
            {
                OnSelected = SelectItem
            };
        }

        public override void AddChild(GUIComponent child)
        {
            listBox.AddChild(child);
        }

        public void AddItem(string text, object userData = null, string toolTip = "")
        {
            GUITextBlock textBlock = null;
            if (RectTransform != null)
            {
                textBlock = new GUITextBlock(new RectTransform(new Point(button.Rect.Width, button.Rect.Height), listBox.RectTransform), text, style: "ListBoxElement");
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
                textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 20), text, "ListBoxElement", Alignment.TopLeft, Alignment.CenterLeft, listBox);
            }
            textBlock.UserData = userData;
            textBlock.ToolTip = toolTip;
        }

        public override void ClearChildren()
        {
            listBox.ClearChildren();
        }

        public List<GUIComponent> GetChildren()
        {
            return listBox.Children;
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
            // TODO: cleanup
            //GUIComponent child = listBox.children.FirstOrDefault(c => c.UserData == userData);

            //if (child == null) return;

            listBox.Select(userData);

            //SelectItem(child, userData);
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
                // Used to enforce the dropdown to be the last element, not necessary if the listbox is added last on the guiupdatelist.
                //if (Parent != null && Parent.Children.Last() != this)
                //{
                //    Parent.RemoveChild(this);
                //    Parent.AddChild(this);
                //}
            }
            return true;
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, bool updateLast = false)
        {
            base.AddToGUIUpdateList(true, updateLast);
            if (!ignoreChildren)
            {
                button.AddToGUIUpdateList(false, updateLast);
                if (Dropped)
                {
                    // Enforces the listbox to be rendered on top of other elements.
                    listBox.AddToGUIUpdateList(false, true);
                }
            }
        }

        public override void Update(float deltaTime)
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

        //public override void Draw(SpriteBatch spriteBatch)
        //{
        //    if (!Visible) return;
        //    base.Draw(spriteBatch);
        //    button.Draw(spriteBatch);
        //    if (Dropped)
        //    {
        //        listBox.Draw(spriteBatch);
        //    }
        //}
    }
}
