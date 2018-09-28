using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        private RectTransform currentListBoxParent;

        public bool Dropped { get; set; }

        public object SelectedItemData
        {
            get
            {
                if (listBox.SelectedComponent == null) return null;
                return listBox.SelectedComponent.UserData;
            }
        }

        public override bool Enabled
        {
            get { return listBox.Enabled; }
            set { listBox.Enabled = value; }
        }

        public GUIComponent Selected
        {
            get { return listBox.SelectedComponent; }
        }

        public GUIListBox ListBox
        {
            get { return listBox; }
        }

        public object SelectedData
        {
            get
            {
                return (listBox.SelectedComponent == null) ? null : listBox.SelectedComponent.UserData;
            }
        }

        public int SelectedIndex
        {
            get
            {
                if (listBox.SelectedComponent == null) return -1;
                return listBox.Content.GetChildIndex(listBox.SelectedComponent);
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
        
        public GUIDropDown(RectTransform rectT, string text = "", int elementCount = 4, string style = "") : base(style, rectT)
        {
            button = new GUIButton(new RectTransform(Vector2.One, rectT), text, Alignment.CenterLeft, style: "GUIDropDown")
            {
                OnClicked = OnClicked
            };
            GUI.Style.Apply(button, "", this);
            
            listBox = new GUIListBox(new RectTransform(new Point(Rect.Width, Rect.Height * MathHelper.Clamp(elementCount, 2, 10)), rectT, Anchor.BottomLeft, Pivot.TopLeft)
            {
                IsFixedSize = false
            }, style: style)
            {
                OnSelected = SelectItem
            };

            currentListBoxParent = FindListBoxParent();
            currentListBoxParent.GUIComponent.OnAddedToGUIUpdateList += AddListBoxToGUIUpdateList;
            rectT.ParentChanged += (RectTransform newParent) =>
            {
                currentListBoxParent.GUIComponent.OnAddedToGUIUpdateList -= AddListBoxToGUIUpdateList;
                if (newParent != null)
                {
                    currentListBoxParent = FindListBoxParent();
                    currentListBoxParent.GUIComponent.OnAddedToGUIUpdateList += AddListBoxToGUIUpdateList;
                }
            };
        }


        /// <summary>
        /// Finds the component after which the listbox should be drawn. Usually the parent of the dropdown, but if the dropdown
        /// is the child of another GUIListBox, we need to draw our listbox after that because listboxes clip everything outside their rect.
        /// </summary>
        private RectTransform FindListBoxParent()
        {
            List<RectTransform> parents = new List<RectTransform>() { RectTransform.Parent };
            while (parents.Last().Parent != null)
            {
                parents.Add(parents.Last().Parent);
            }
            //find the parent GUIListBox highest in the hierarchy
            for (int i = parents.Count - 1; i >= 0; i--)
            {
                if (parents[i].GUIComponent is GUIListBox) return parents[i];
            }
            //or just go with the direct parent if there are no listboxes in the hierarchy
            return RectTransform.Parent;
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
                ToolTip = toolTip
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
            if (Dropped && Enabled)
            {
                OnDropped?.Invoke(this, userData);                
            }
            return true;
        }

        private void AddListBoxToGUIUpdateList(GUIComponent parent)
        {            
            //the parent is not our parent anymore :(
            //can happen when subscribed to a parent higher in the hierarchy (instead of the direct parent),
            //and somewhere between this component and the higher parent a component was removed
            if (!parent.IsParentOf(this))
            {
                parent.OnAddedToGUIUpdateList -= AddListBoxToGUIUpdateList;
                return;
            }
            if (Dropped)
            {
                listBox.AddToGUIUpdateList(false, UpdateOrder);
            }
        }

        public override void DrawManually(SpriteBatch spriteBatch, bool alsoChildren = false, bool recursive = true)
        {
            if (!Visible) return;

            AutoDraw = false;
            Draw(spriteBatch);
            if (alsoChildren)
            {
                button.DrawManually(spriteBatch, alsoChildren, recursive);
            }
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            base.AddToGUIUpdateList(true, order);
            if (!ignoreChildren)
            {
                button.AddToGUIUpdateList(false, order);
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
                if (!listBoxRect.Contains(PlayerInput.MousePosition) && !button.Rect.Contains(PlayerInput.MousePosition))
                {
                    Dropped = false;
                }
            }
        }
    }
}
