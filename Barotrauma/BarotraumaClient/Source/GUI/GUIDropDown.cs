using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class GUIDropDown : GUIComponent, IKeyboardSubscriber
    {
        public delegate bool OnSelectedHandler(GUIComponent selected, object obj = null);
        public OnSelectedHandler OnSelected;
        public OnSelectedHandler OnDropped;

        private GUIButton button;
        private GUIListBox listBox;

        private RectTransform currentListBoxParent;
        private List<RectTransform> parentHierarchy = new List<RectTransform>();

        private bool selectMultiple;

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

        public bool ButtonEnabled
        {
            get { return  button.Enabled; }
            set { button.Enabled = value; }
        }

        public GUIComponent SelectedComponent
        {
            get { return listBox.SelectedComponent; }
        }
        
        public bool Selected
        {
            get
            {
                return Dropped;
            }
            set
            {
                Dropped = value;
            }
        }

        public GUIListBox ListBox
        {
            get { return listBox; }
        }

        public object SelectedData
        {
            get
            {
                return listBox.SelectedComponent?.UserData;
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

        public void ReceiveTextInput(char inputChar)
        {
            GUI.KeyboardDispatcher.Subscriber = null;
        }
        public void ReceiveTextInput(string text) { }
        public void ReceiveCommandInput(char command) { }

        public void ReceiveSpecialInput(Keys key)
        {
            switch (key)
            {
                case Keys.Up:
                case Keys.Down:
                    listBox.ReceiveSpecialInput(key);
                    GUI.KeyboardDispatcher.Subscriber = this;
                    break;
                case Keys.Enter:
                case Keys.Space:
                case Keys.Escape:
                    GUI.KeyboardDispatcher.Subscriber = null;
                    break;
            }
        }

        private List<object> selectedDataMultiple = new List<object>();
        public IEnumerable<object> SelectedDataMultiple
        {
            get { return selectedDataMultiple; }
        }

        private List<int> selectedIndexMultiple = new List<int>();
        public IEnumerable<int> SelectedIndexMultiple
        {
            get { return selectedIndexMultiple; }
        }

        public string Text
        {
            get { return button.Text; }
            set { button.Text = value; }
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
                
        public GUIDropDown(RectTransform rectT, string text = "", int elementCount = 4, string style = "", bool selectMultiple = false) : base(style, rectT)
        {
            CanBeFocused = true;

            this.selectMultiple = selectMultiple;

            button = new GUIButton(new RectTransform(Vector2.One, rectT), text, Alignment.CenterLeft, style: "GUIDropDown")
            {
                OnClicked = OnClicked
            };
            GUI.Style.Apply(button, "", this);
            
            listBox = new GUIListBox(new RectTransform(new Point(Rect.Width, Rect.Height * MathHelper.Clamp(elementCount, 2, 10)), rectT, Anchor.BottomLeft, Pivot.TopLeft)
            { IsFixedSize = false }, style: null)
            {
                Enabled = !selectMultiple,
                OnSelected = SelectItem
            };
            GUI.Style.Apply(listBox.Content, "GUIListBox", this);

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
            parentHierarchy.Clear();
            parentHierarchy = new List<RectTransform>() { RectTransform.Parent };
            while (parentHierarchy.Last().Parent != null)
            {
                parentHierarchy.Add(parentHierarchy.Last().Parent);
            }
            //find the parent GUIListBox highest in the hierarchy
            for (int i = parentHierarchy.Count - 1; i >= 0; i--)
            {
                if (parentHierarchy[i].GUIComponent is GUIListBox)
                {
                    if (parentHierarchy[i].Parent != null && parentHierarchy[i].Parent.GUIComponent != null)
                    {
                        return parentHierarchy[i].Parent;
                    }
                    return parentHierarchy[i];
                }
            }
            //or just go with the direct parent if there are no listboxes in the hierarchy
            parentHierarchy.Clear();
            parentHierarchy.Add(RectTransform.Parent);
            return RectTransform.Parent;
        }
                
        public void AddItem(string text, object userData = null, string toolTip = "")
        {
            if (selectMultiple)
            {
                var frame = new GUIFrame(new RectTransform(new Point(button.Rect.Width, button.Rect.Height), listBox.Content.RectTransform)
                { IsFixedSize = false }, style: "ListBoxElement")
                {
                    UserData = userData,
                    ToolTip = toolTip
                };

                new GUITickBox(new RectTransform(new Point((int)(button.Rect.Height * 0.8f)), frame.RectTransform, anchor: Anchor.CenterLeft), text)
                {
                    UserData = userData,
                    ToolTip = toolTip,
                    OnSelected = (GUITickBox tb) =>
                    {
                        List<string> texts = new List<string>();
                        selectedDataMultiple.Clear();
                        selectedIndexMultiple.Clear();
                        int i = 0;
                        foreach (GUIComponent child in ListBox.Content.Children)
                        {
                            var tickBox = child.GetChild<GUITickBox>();
                            if (tickBox.Selected)
                            {
                                selectedDataMultiple.Add(child.UserData);
                                selectedIndexMultiple.Add(i);
                                texts.Add(tickBox.Text);
                            }
                            i++;
                        }
                        button.Text = string.Join(", ", texts);
                        OnSelected?.Invoke(tb.Parent, tb.Parent.UserData);
                        return true;
                    }
                };
            }
            else
            {
                new GUITextBlock(new RectTransform(new Point(button.Rect.Width, button.Rect.Height), listBox.Content.RectTransform)
                { IsFixedSize = false }, text, style: "ListBoxElement")
                {
                    UserData = userData,
                    ToolTip = toolTip
                };
            }
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
            if (selectMultiple)
            {
                foreach (GUIComponent child in ListBox.Content.Children)
                {
                    var tickBox = child.GetChild<GUITickBox>();
                    if (obj == child.UserData) { tickBox.Selected = true; }
                }
            }
            else
            {
                GUITextBlock textBlock = component as GUITextBlock;
                if (textBlock == null)
                {
                    textBlock = component.GetChild<GUITextBlock>();
                    if (textBlock == null) return false;
                }
                button.Text = textBlock.Text;
            }
            Dropped = false;
            OnSelected?.Invoke(component, component.UserData);
            return true;
        }

        public void SelectItem(object userData)
        {
            if (selectMultiple)
            {
                SelectItem(listBox.Content.FindChild(userData), userData);
            }
            else
            {
                listBox.Select(userData);
            }
        }

        public void Select(int index)
        {
            if (selectMultiple)
            {
                var child = listBox.Content.GetChild(index);
                if (child != null)
                {
                    SelectItem(null, child.UserData);
                }
            }
            else
            {
                listBox.Select(index);
            }
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
                listBox.UpdateScrollBarSize();

                GUI.KeyboardDispatcher.Subscriber = this;
            }
            else if (GUI.KeyboardDispatcher.Subscriber == this)
            {
                GUI.KeyboardDispatcher.Subscriber = null;
            }
            return true;
        }

        private void AddListBoxToGUIUpdateList(GUIComponent parent)
        {
            //the parent is not our parent anymore :(
            //can happen when subscribed to a parent higher in the hierarchy (instead of the direct parent),
            //and somewhere between this component and the higher parent a component was removed
            for (int i = 1; i < parentHierarchy.Count; i++)
            {
                if (!parentHierarchy[i].IsParentOf(parentHierarchy[i - 1], recursive: false))
                {
                    parent.OnAddedToGUIUpdateList -= AddListBoxToGUIUpdateList;
                    return;
                }
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
                    if (GUI.KeyboardDispatcher.Subscriber == this)
                    {
                        GUI.KeyboardDispatcher.Subscriber = null;
                    }
                }
            }
        }
    }
}
