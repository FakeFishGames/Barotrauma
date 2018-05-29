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

        public override bool Enabled
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
                return listBox.Content.GetChildIndex(listBox.Selected);
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
        
        public GUIDropDown(RectTransform rectT, string text = "", int elementCount = 3, string style = "") : base(style, rectT)
        {
            button = new GUIButton(new RectTransform(Vector2.One, rectT), text, Alignment.CenterLeft, style: "GUIDropDown")
            {
                OnClicked = OnClicked
            };
            GUI.Style.Apply(button, "", this);
            
            listBox = new GUIListBox(new RectTransform(new Point(Rect.Width, Rect.Height * MathHelper.Clamp(elementCount - 1, 5, 10)), rectT, Anchor.BottomLeft, Pivot.TopLeft)
            {
                IsFixedSize = false
            }, style: style)
            {
                OnSelected = SelectItem
            };
        }
        
        public void AddItem(string text, object userData = null, string toolTip = "")
        {
            GUITextBlock textBlock = null;

            textBlock = new GUITextBlock(new RectTransform(new Point(button.Rect.Width, button.Rect.Height), listBox.Content.RectTransform)
            {
                IsFixedSize = false
            }, text, style: "ListBoxElement")
            {
                UserData = userData,
                ToolTip = toolTip,
                ClampMouseRectToParent = false
            };
        }

        public override void ClearChildren()
        {
            listBox.ClearChildren();
        }

        public IEnumerable<GUIComponent> GetChildren()
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
                //TODO: this doesn't work if the dropdown is in a GUILayoutGroup
                RectTransform.SetAsLastChild();
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
