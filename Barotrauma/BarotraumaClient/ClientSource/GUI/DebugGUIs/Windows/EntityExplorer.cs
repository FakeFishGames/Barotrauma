using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    internal sealed class EntityExplorer : DebugWindow
    {
        private readonly GUITextBox filterBox;
        private readonly GUIListBox entityList;

        private readonly IEnumerable<Entity> focusedEntities;

        private EntityExplorer(IEnumerable<Entity> entities = null) : base(createRefreshButton: entities == null)
        {
            focusedEntities = entities ?? Entity.GetEntities();

            filterBox = CreateFilterBox(Content, (_, text) =>
            {
                FilterEntries(entityList, text);
                return true;
            });

            entityList = new(new(Vector2.One, Content.RectTransform));
            entityList.OnSelected += (component, obj) =>
            {
                if (obj is Entity { Removed: false } entity)
                {
                    EntityEditor.TryOpenNew(entity);
                    return true;
                }

                component.RectTransform.Parent = null;
                return false;
            };

            Refresh();
        }

        public static void OpenNew(IEnumerable<Entity> entities = null) => new EntityExplorer(entities);

        protected override void Refresh()
        {
            entityList.ClearChildren();
            foreach (Entity entity in focusedEntities)
            {
                if (entity is not { Removed: false }) { return; }

                GUITextBlock entry = CreateListEntry(entityList, entity, out GUILayoutGroup right);
                entry.Text = RichString.Rich($"{entity.GetName()} (‖color:GUI.Green‖{entity.GetType().Name}‖end‖) {entity.WorldPosition.ToPoint()}");

                new GUIButton(new(new Point(right.Rect.Height), right.RectTransform), style: "GUIMinusButton", color: GUIStyle.Red)
                {
                    Enabled = entity is not Submarine,
                    ToolTip = entity is not Submarine ? TextManager.Get("BanListRemove") : "Cannot remove submarines.",
                    OnClicked = (_, _) =>
                    {
                        entry.RectTransform.Parent = null;
                        if (!entity.Removed) { entity.Remove(); }

                        return true;
                    }
                };

                new GUITextBlock(new((0.1f, 1f), right.RectTransform), entity.ID.ToString(), Color.Gray, textAlignment: Alignment.Right)
                {
                    CanBeFocused = false
                };
            }

            FilterEntries(entityList, filterBox.Text);
        }
    }
}
