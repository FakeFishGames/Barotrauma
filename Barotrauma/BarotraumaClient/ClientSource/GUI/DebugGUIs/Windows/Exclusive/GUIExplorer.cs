using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal sealed class GUIExplorer : ExclusiveDebugWindow<GUIComponent>
    {
        private readonly GUITextBox filterBox;
        private readonly GUIListBox componentList;
        private readonly List<GUITextBlock> componentEntries = new();

        private GUIExplorer(GUIComponent component) : base(component, createRefreshButton: true)
        {
            filterBox = CreateFilterBox(Content, (_, text) =>
            {
                FilterEntries(componentList, text);
                return true;
            });

            componentList = new(new(Vector2.One, Content.RectTransform))
            {
                OnSelected = (_, obj) =>
                {
                    if (obj is GUIComponent component)
                    {
                        TryOpenNew(component);
                    }
                    return true;
                }
            };

            Refresh();
        }

        public static void TryOpenNew(GUIComponent component)
        {
            if (!WindowExists(component))
            {
                new GUIExplorer(component);
            }
        }

        protected override void Refresh()
        {
            IEnumerable<GUIComponent> parentHeirarchy = new List<GUIComponent>();
            GUIComponent parent = FocusedObject;
            while (parent != null)
            {
                parentHeirarchy = parentHeirarchy.Prepend(parent);
                parent = parent.Parent;
            }

            componentList.ClearChildren();
            foreach (GUIComponent component1 in parentHeirarchy.Concat(FocusedObject.GetAllChildren()))
            {
                GUITextBlock entry = CreateListEntry(componentList, component1, out GUILayoutGroup right);
                entry.Text = RichString.Rich($"‖color:gui.green‖{component1.GetType().Name}‖end‖ {(component1.Style != null ? $"({component1.Style.Name})" : "")}");

                new GUIButton(new(new Point(right.Rect.Height), right.RectTransform), style: "GUIMinusButton", color: GUIStyle.Red)
                {
                    Enabled = component1.RectTransform.Parent != GUI.Canvas,
                    ToolTip = component1.RectTransform.Parent != GUI.Canvas ? "Deparent" : "Cannot orphan children of the GUI canvas.",
                    OnClicked = (_, _) =>
                    {
                        if (parentHeirarchy.Contains(component1))
                        {
                            Close();
                            if (component1.Parent != null)
                            {
                                TryOpenNew(component1.Parent);
                            }
                        }
                        else
                        {
                            Refresh();
                        }
                        component1.RectTransform.Parent = null;
                        return true;
                    }
                };

                GUITickBox visibleCheck = new(new(new Point(right.Rect.Height), right.RectTransform), "", style: "GUITickBoxNoMinimum")
                {
                    ToolTip = TextManager.Get("VisibleSubmarines"),
                    Selected = component1.Visible,
                    OnSelected = (obj) =>
                    {
                        component1.Visible = obj.Selected;
                        return true;
                    }
                };

                new GUITextBlock(new(new Vector2(0.1f, 1f), right.RectTransform), component1.UpdateOrder.ToString(), Color.Gray, textAlignment: Alignment.Right)
                {
                    CanBeFocused = false
                };

                if (component1 == FocusedObject)
                {
                    entry.Flash(GUIStyle.Green);
                }

                componentEntries.Add(entry);
            }

            FilterEntries(componentList, filterBox.Text);
        }

        protected override void Update()
        {
            base.Update();
            componentEntries.RemoveAll(i => i.Parent == null);
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            spriteBatch.Begin();
            componentEntries.Where(i => i.State is GUIComponent.ComponentState.Hover or GUIComponent.ComponentState.HoverSelected).ForEach(i => (i.UserData as GUIComponent).DrawGUIDebugOverlay(spriteBatch));
            spriteBatch.End();
        }
    }
}
