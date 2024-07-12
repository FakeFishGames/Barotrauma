#nullable enable

using System;
using System.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal partial class CircuitBoxComponent
    {
        public static Option<GUIComponent> EditingHUD = Option.None;

        private Sprite Sprite => Item.Prefab.InventoryIcon ?? Item.Prefab.Sprite;

        private CircuitBoxLabel? label;
        private CircuitBoxLabel Label
        {
            get
            {
                if (label is { } l)
                {
                    return l;
                }

                var name = TextManager.Get($"circuitboxnode.{Item.Prefab.Identifier}").Fallback($"[FALLBACK] {Item.Name}");
                label = new CircuitBoxLabel(name, GUIStyle.LargeFont);
                return label.Value;
            }
        }

        public void UpdateEditing(RectTransform parent)
        {
            if (EditingHUD.TryUnwrap(out var editor))
            {
                if (editor.UserData == this) { return; }
                RemoveEditingHUD();
            }
            EditingHUD = Option.Some(CreateEditingHUD(parent));
        }

        public static void RemoveEditingHUD()
        {
            if (!EditingHUD.TryUnwrap(out var editor)) { return; }

            editor.RectTransform.Parent = null;
            EditingHUD = Option.None;
        }

        public GUIComponent CreateEditingHUD(RectTransform parent)
        {
            GUIFrame frame = new(new RectTransform(new Vector2(0.4f, 0.3f), parent, Anchor.TopRight))
            {
                UserData = this
            };

            GUIListBox listBox = new(new RectTransform(ToolBox.PaddingSizeParentRelative(frame.RectTransform, 0.8f), frame.RectTransform, Anchor.Center))
            {
                KeepSpaceForScrollBar = true,
                AutoHideScrollBar = false,
                CanTakeKeyBoardFocus = false
            };

            bool isEditor = Screen.Selected is { IsEditor: true };

            GUILayoutGroup titleHolder = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), listBox.Content.RectTransform));
            new GUITextBlock(new RectTransform(Vector2.One, titleHolder.RectTransform), Item.Prefab.Name, font: GUIStyle.LargeFont)
            {
                TextColor = Color.White,
                Color = Color.Black
            };
            int fieldCount = 0;

            foreach (ItemComponent ic in Item.Components)
            {
                if (ic is Holdable) { continue; }
                if (!ic.AllowInGameEditing && Screen.Selected is not { IsEditor: true }) { continue; }
                if (SerializableProperty.GetProperties<InGameEditable>(ic).Count == 0 &&
                    !SerializableProperty.GetProperties<ConditionallyEditable>(ic).Any(p => p.GetAttribute<ConditionallyEditable>().IsEditable(ic)))
                {
                    continue;
                }

                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), listBox.Content.RectTransform), style: "HorizontalLine");

                var componentEditor = new SerializableEntityEditor(listBox.Content.RectTransform, ic, inGame: !isEditor, showName: false, titleFont: GUIStyle.SubHeadingFont)
                {
                    Readonly = CircuitBox.Locked
                };
                fieldCount += componentEditor.Fields.Count;

                ic.CreateEditingHUD(componentEditor);
                componentEditor.Recalculate();
            }

            if (fieldCount == 0)
            {
                frame.Visible = false;
            }

            return frame;
        }

        public override void DrawHeader(SpriteBatch spriteBatch, RectangleF drawRect, Color color)
        {
            // scale to topRect height
            Vector2 scale = new(drawRect.Height / MathF.Min(Sprite.size.X, Sprite.size.Y)),
                    spritePosition = new(drawRect.Left, drawRect.Top);

            float spriteWidth = Sprite.size.X * scale.X;

            Sprite.Draw(spriteBatch, spritePosition, Color.White, Vector2.Zero, 0f, scale);
            GUI.DrawString(spriteBatch, new Vector2(spritePosition.X + spriteWidth + CircuitBoxSizes.NodeHeaderTextPadding, drawRect.Center.Y - Label.Size.Y / 2f), Label.Value, GUIStyle.TextColorNormal, font: GUIStyle.LargeFont);
        }
    }
}