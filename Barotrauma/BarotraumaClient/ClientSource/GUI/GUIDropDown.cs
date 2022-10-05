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

        private readonly GUIButton button;
        private readonly GUIImage icon;
        private readonly GUIListBox listBox;

        private RectTransform currentHighestParent;
        private List<RectTransform> parentHierarchy = new List<RectTransform>();

        private readonly bool selectMultiple;

        public bool Dropped { get; set; }
        
        public bool AllowNonText { get; set; }

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
            set 
            { 
                button.Enabled = value;
                if (icon != null) { icon.Enabled = value; }
            }
        }

        public GUIComponent SelectedComponent
        {
            get { return listBox.SelectedComponent; }
        }

        public override bool Selected
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

        public Color ButtonTextColor
        {
            get { return button.TextColor; }
            set { button.TextColor = value; }
        }

        public override GUIFont Font 
        {
            get { return button?.Font ?? base.Font; }
            set 
            {
                if (button != null) { button.Font = value;  }               
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

        private readonly List<object> selectedDataMultiple = new List<object>();
        public IEnumerable<object> SelectedDataMultiple
        {
            get { return selectedDataMultiple; }
        }

        private readonly List<int> selectedIndexMultiple = new List<int>();
        public IEnumerable<int> SelectedIndexMultiple
        {
            get { return selectedIndexMultiple; }
        }

        public bool MustSelectAtLeastOne;

        public LocalizedString Text
        {
            get { return button.Text; }
            set { button.Text = value; }
        }

        public override RichString ToolTip
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

        public GUIImage DropDownIcon => icon;

        public Vector4 Padding => button.TextBlock.Padding;
                
        public GUIDropDown(RectTransform rectT, LocalizedString text = null, int elementCount = 4, string style = "", bool selectMultiple = false, bool dropAbove = false, Alignment textAlignment = Alignment.CenterLeft) : base(style, rectT)
        {
            text ??= new RawLString("");

            HoverCursor = CursorState.Hand;
            CanBeFocused = true;

            this.selectMultiple = selectMultiple;

            button = new GUIButton(new RectTransform(Vector2.One, rectT), text, textAlignment, style: "GUIDropDown")
            {
                OnClicked = OnClicked,
                TextBlock = { OverflowClip = true }
            };
            GUIStyle.Apply(button, "", this);
            button.TextBlock.SetTextPos();

            Anchor listAnchor = dropAbove ? Anchor.TopCenter : Anchor.BottomCenter;
            Pivot listPivot = dropAbove ? Pivot.BottomCenter : Pivot.TopCenter;
            listBox = new GUIListBox(new RectTransform(new Point(Rect.Width, Rect.Height * MathHelper.Clamp(elementCount, 2, 10)), rectT, listAnchor, listPivot)
            { IsFixedSize = false }, style: null)
            {
                Enabled = !selectMultiple,
                PlaySoundOnSelect = true,
            };
            if (!selectMultiple) { listBox.OnSelected = SelectItem; }           
            GUIStyle.Apply(listBox, "GUIListBox", this);
            GUIStyle.Apply(listBox.ContentBackground, "GUIListBox", this);

            if (button.Style.ChildStyles.ContainsKey("dropdownicon".ToIdentifier()))
            {
                icon = new GUIImage(new RectTransform(new Vector2(0.6f, 0.6f), button.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.BothHeight) { AbsoluteOffset = new Point(5, 0) }, null, scaleToFit: true);
                icon.ApplyStyle(button.Style.ChildStyles["dropdownicon".ToIdentifier()]);
            }

            currentHighestParent = FindHighestParent();
            currentHighestParent.GUIComponent.OnAddedToGUIUpdateList += AddListBoxToGUIUpdateList;
            rectT.ParentChanged += _ => RefreshListBoxParent();
        }


        /// <summary>
        /// Finds the component after which the listbox should be drawn 
        /// //(= the component highest in the hierarchy, to get the listbox 
        /// //to be rendered on top of all of it's children)
        /// </summary>
        private RectTransform FindHighestParent()
        {
            parentHierarchy.Clear();

            //collect entire parent hierarchy to a list
            parentHierarchy = new List<RectTransform>() { RectTransform.Parent };
            RectTransform parent = parentHierarchy.Last();
            while (parent?.Parent != null)
            {
                parentHierarchy.Add(parent.Parent);
                parent = parent.Parent;
            }

            //find the highest parent that has a guicomponent with a style 
            //(and so should be rendered and not just some empty parent/root element used for constructing a layout)
            for (int i = parentHierarchy.Count - 1; i > 0; i--)
            {
                if (parentHierarchy[i] is GUICanvas ||
                    parentHierarchy[i].GUIComponent == null ||
                    parentHierarchy[i].GUIComponent.Style == null ||
                    parentHierarchy[i].GUIComponent == Screen.Selected?.Frame)
                {
                    parentHierarchy.RemoveAt(i);
                }
                else
                {
                    break;
                }
            }
            return parentHierarchy.Last();
        }
                
        public void AddItem(LocalizedString text, object userData = null, LocalizedString toolTip = null)
        {
            toolTip ??= "";
            if (selectMultiple)
            {
                var frame = new GUIFrame(new RectTransform(new Point(button.Rect.Width, button.Rect.Height), listBox.Content.RectTransform)
                { IsFixedSize = false }, style: "ListBoxElement")
                {
                    UserData = userData,
                    ToolTip = toolTip
                };

                new GUITickBox(new RectTransform(new Vector2(1.0f, 0.8f), frame.RectTransform, anchor: Anchor.CenterLeft) { MaxSize = new Point(int.MaxValue, (int)(button.Rect.Height * 0.8f)) }, text)
                {
                    UserData = userData,
                    ToolTip = toolTip,
                    OnSelected = (GUITickBox tb) =>
                    {
                        if (MustSelectAtLeastOne && selectedIndexMultiple.Count <= 1 && !tb.Selected)
                        {
                            tb.Selected = true;
                            return false;
                        }

                        List<LocalizedString> texts = new List<LocalizedString>();
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
                        button.Text = LocalizedString.Join(", ", texts);
                        // TODO: The callback is called at least twice, remove this?
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
                    if (Equals(obj, child.UserData)) { tickBox.Selected = true; }
                }
            }
            else
            {
                if (!(component is GUITextBlock textBlock))
                {
                    textBlock = component.GetChild<GUITextBlock>();
                    if (textBlock is null && !AllowNonText) { return false; }
                }
                button.Text = textBlock?.Text ?? "";
            }
            Dropped = false;
            // TODO: OnSelected can be called multiple times and when it shouldn't be called -> turn into an event so that nobody else can call it.
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
                OnDropped?.Invoke(this, UserData);
                listBox.UpdateScrollBarSize();
                listBox.UpdateDimensions();

                GUI.KeyboardDispatcher.Subscriber = this;
            }
            else if (GUI.KeyboardDispatcher.Subscriber == this)
            {
                GUI.KeyboardDispatcher.Subscriber = null;
            }
            return true;
        }

        public void RefreshListBoxParent()
        {
            currentHighestParent.GUIComponent.OnAddedToGUIUpdateList -= AddListBoxToGUIUpdateList;
            if (RectTransform.Parent == null) { return; }

            currentHighestParent = FindHighestParent();
            currentHighestParent.GUIComponent.OnAddedToGUIUpdateList += AddListBoxToGUIUpdateList;
        }
        
        private void AddListBoxToGUIUpdateList(GUIComponent parent)
        {
            //the parent is not our parent anymore :(
            //can happen when subscribed to a parent higher in the hierarchy (instead of the direct parent),
            //and somewhere between this component and the higher parent a component was removed
            for (int i = 1; i < parentHierarchy.Count; i++)
            {
                if (parentHierarchy[i].IsParentOf(parentHierarchy[i - 1], recursive: false))
                {
                    continue;
                }

                parent.OnAddedToGUIUpdateList -= AddListBoxToGUIUpdateList;
                return;
            }

            if (Dropped)
            {
                listBox.AddToGUIUpdateList(false, 1);
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
            if (Dropped && PlayerInput.PrimaryMouseButtonClicked())
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
