using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal enum InspectorMode
    {
        Disabled,
        Entities,
        GUI
    }

    internal static class EntityInspector
    {
        public static InspectorMode Mode = InspectorMode.Disabled;

        public static void Update()
        {
            if (Mode == InspectorMode.Disabled) { return; }

            if (PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.KeyHit(Keys.Escape))
            {
                Mode = InspectorMode.Disabled;
            }
            else if (PlayerInput.PrimaryMouseButtonClicked())
            {
                switch (Mode)
                {
                    case InspectorMode.Entities:
                        IEnumerable<Entity> EntitiesUnderCursor = Entity.GetEntities(entity => entity.IsUnderCursor);
                        EntityExplorer.OpenNew(EntitiesUnderCursor.Concat(EntitiesUnderCursor.OfType<Item>().SelectManyRecursive(item => item.ContainedItems)).Concat(EntitiesUnderCursor.OfType<Character>().SelectMany(character => character.Inventory.AllItems.Concat(character.Inventory.AllItems.SelectManyRecursive(item => item.ContainedItems)))));
                        break;
                    case InspectorMode.GUI when GUI.MouseOn != null:
                        GUIExplorer.TryOpenNew(GUI.MouseOn);
                        break;
                }
                Mode = InspectorMode.Disabled;
            }
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            if (Mode == InspectorMode.Disabled)
            {
                if (!GameMain.DebugDraw) { return; }
                spriteBatch.Begin();
                GUI.MouseOn?.DrawGUIDebugOverlay(spriteBatch);
                spriteBatch.End();
                return;
            }

            spriteBatch.Begin();

            Vector2 cursorOffset = PlayerInput.MousePosition + (25f, 0f);
            string tooltip = $"Inspector mode (RMB to cancel)\nCursor pos: {PlayerInput.MouseWorldPosition}";

            switch (Mode)
            {
                case InspectorMode.Entities:
                    IEnumerable<Entity> entities = Entity.GetEntities(entity => entity.IsUnderCursor);
                    tooltip += $"\nEntities below cursor: {entities.Count()}";
                    entities.ForEach(e => tooltip += $"\n- {e.GetName()} (‖color:GUI.Green‖{e.GetType().Name}‖end‖)");
                    break;
                case InspectorMode.GUI when GUI.MouseOn != null:
                    tooltip += $"\nSelected GUIComponent: ‖color:GUI.Green‖{GUI.MouseOn.GetType().Name}‖end‖ ({GUI.MouseOn.Style?.Name ?? "no style"})";
                    GUI.MouseOn.DrawGUIDebugOverlay(spriteBatch);
                    break;
                case InspectorMode.GUI when GUI.MouseOn == null:
                    tooltip += "\nNo GUIComponent selected";
                    break;
            }

            ImmutableArray<RichTextData>? data = RichTextData.GetRichTextData(tooltip, out tooltip);
            GUI.DrawStringWithColors(spriteBatch, cursorOffset, tooltip, GUIStyle.TextColorNormal, in data, backgroundColor: Color.Black * 0.5f);

            spriteBatch.End();
        }
    }
}
