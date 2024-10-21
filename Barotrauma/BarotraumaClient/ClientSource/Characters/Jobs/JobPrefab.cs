using Microsoft.Xna.Framework;
using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class JobPrefab : PrefabWithUintIdentifier
    {
        public GUIButton CreateInfoFrame(out GUIComponent buttonContainer)
        {
            int width = 500, height = 400;

            GUIButton frameHolder = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, frameHolder.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            GUIFrame frame = new GUIFrame(new RectTransform(new Point(width, height), frameHolder.RectTransform, Anchor.Center));
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), Name, font: GUIStyle.LargeFont);

            var descriptionBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.15f) },
                Description, font: GUIStyle.SmallFont, wrap: true);

            var skillContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.5f), paddedFrame.RectTransform)
                { RelativeOffset = new Vector2(0.0f, 0.2f + descriptionBlock.RectTransform.RelativeSize.Y) });
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                TextManager.Get("Skills"), font: GUIStyle.LargeFont);
            foreach (SkillPrefab skill in Skills)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                    "   - " + TextManager.AddPunctuation(':', TextManager.Get("SkillName." + skill.Identifier), (int)skill.LevelRange.Start + " - " + (int)skill.LevelRange.End), 
                    font: GUIStyle.SmallFont);
            }

            buttonContainer = paddedFrame;

            /*if (!ItemIdentifiers.TryGetValue(variant, out var itemIdentifiers)) { return backFrame; }
            var itemContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.5f), paddedFrame.RectTransform, Anchor.TopRight)
            { RelativeOffset = new Vector2(0.0f, 0.2f + descriptionBlock.RectTransform.RelativeSize.Y) })
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), itemContainer.RectTransform),
                TextManager.Get("Items", "mapentitycategory.equipment"), font: GUIStyle.LargeFont);
            foreach (string identifier in itemIdentifiers.Distinct())
            {
                if (!(MapEntityPrefab.Find(name: null, identifier: identifier) is ItemPrefab itemPrefab)) { continue; }
                int count = itemIdentifiers.Count(i => i == identifier);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), itemContainer.RectTransform),
                    "   - " + (count == 1 ? itemPrefab.Name : itemPrefab.Name + " x" + count),
                    font: GUIStyle.SmallFont);
            }*/

            return frameHolder;
        }

        public class OutfitPreview
        {
            public readonly List<(Sprite sprite, Vector2 drawOffset)> Sprites;

            public Vector2 Dimensions;

            public OutfitPreview()
            {
                Sprites = new List<(Sprite sprite, Vector2 drawOffset)>();
                Dimensions = Vector2.One;
            }

            public void AddSprite(Sprite sprite, Vector2 drawOffset)
            {
                Sprites.Add((sprite, drawOffset));
            }
        }

        public List<OutfitPreview> GetJobOutfitSprites(CharacterInfoPrefab charInfoPrefab, bool useInventoryIcon, out Vector2 maxDimensions)
        {
            List<OutfitPreview> outfitPreviews = new List<OutfitPreview>();
            maxDimensions = Vector2.One;

            var equipIdentifiers = Element.GetChildElements("ItemSet").Elements().Where(e => e.GetAttributeBool("outfit", false)).Select(e => e.GetAttributeIdentifier("identifier", ""));

            List<ItemPrefab> outfitPrefabs = new List<ItemPrefab>();
            foreach (var equipIdentifier in equipIdentifiers)
            {
                var itemPrefab = ItemPrefab.Prefabs.Find(ip => ip.Identifier == equipIdentifier);
                if (itemPrefab != null) { outfitPrefabs.Add(itemPrefab); }
            }

            if (!outfitPrefabs.Any()) { return null; }

            for (int i = 0; i < outfitPrefabs.Count; i++)
            {
                var outfitPreview = new OutfitPreview();

                if (!ItemSets.TryGetValue(i, out var itemSetElement)) { continue; }
                var previewElement = itemSetElement.GetChildElement("PreviewSprites");
                if (previewElement == null || useInventoryIcon)
                {
                    if (outfitPrefabs[i] is ItemPrefab prefab && prefab.InventoryIcon != null)
                    {
                        outfitPreview.AddSprite(prefab.InventoryIcon, Vector2.Zero);
                        outfitPreview.Dimensions = prefab.InventoryIcon.SourceRect.Size.ToVector2();
                        maxDimensions.X = MathHelper.Max(maxDimensions.X, outfitPreview.Dimensions.X);
                        maxDimensions.Y = MathHelper.Max(maxDimensions.Y, outfitPreview.Dimensions.Y);
                    }
                    outfitPreviews.Add(outfitPreview);
                    continue;
                }

                var children = previewElement.Elements().ToList();
                for (int n = 0; n < children.Count; n++)
                {
                    var spriteElement = children[n];
                    string spriteTexture = charInfoPrefab.ReplaceVars(spriteElement.GetAttributeString("texture", ""), charInfoPrefab.Heads.First());
                    var sprite = new Sprite(spriteElement, file: spriteTexture);
                    sprite.size = new Vector2(sprite.SourceRect.Width, sprite.SourceRect.Height);
                    outfitPreview.AddSprite(sprite, children[n].GetAttributeVector2("offset", Vector2.Zero));
                }

                outfitPreview.Dimensions = previewElement.GetAttributeVector2("dims", Vector2.One);
                maxDimensions.X = MathHelper.Max(maxDimensions.X, outfitPreview.Dimensions.X);
                maxDimensions.Y = MathHelper.Max(maxDimensions.Y, outfitPreview.Dimensions.Y);

                outfitPreviews.Add(outfitPreview);
            }

            return outfitPreviews;
        }
    }

    internal partial class JobVariant
    {
        private const int ItemsPerRow = 5;

        private IEnumerable<JobPrefab.PreviewItem> PreviewItems => Prefab.PreviewItems[Variant].Where(it => it.ShowPreview);
        private IEnumerable<Identifier> PreviewItemIdentifiers => PreviewItems.Select(it => it.ItemIdentifier).Distinct();

        public GUIButton CreateButton(GUILayoutGroup parent, bool selected, GUIButton.OnClickedHandler onClicked)
        {
            GUIButton button = new GUIButton(new RectTransform(Vector2.One, parent.RectTransform, scaleBasis: ScaleBasis.BothHeight), (Variant + 1).ToString(), style: "JobVariantButton")
            {
                Selected = selected,
                OnClicked = onClicked,
                UserData = this
            };

            return button;
        }

        public GUIFrame CreateTooltip(GUIComponent parent, Point size, Point? position = null, Pivot? pivot = null, object data = null)
        {
            GUIFrame jobVariantTooltip = new GUIFrame(new RectTransform(size, GUI.Canvas, pivot: pivot), "GUIToolTip")
            {
                UserData = data
            };

            jobVariantTooltip.RectTransform.AbsoluteOffset = position.TryGetValue(out Point pos) ? pos : parent.Rect.Location;

            GUILayoutGroup content = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), jobVariantTooltip.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            new GUITextBlock(new RectTransform(Vector2.UnitX, content.RectTransform) { IsFixedSize = true }, TextManager.GetWithVariable("startingequipmentname", "[number]", (Variant + 1).ToString()), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);

            int rows = (int)Math.Max(Math.Ceiling(PreviewItemIdentifiers.Count() / (float)ItemsPerRow), 1);

            new GUICustomComponent(new RectTransform(new Vector2(1f, 0.4f * rows), content.RectTransform, Anchor.BottomCenter), onDraw: DrawPreviewItems);

            jobVariantTooltip.RectTransform.MinSize = new Point(0, content.RectTransform.Children.Sum(c => c.Rect.Height + content.AbsoluteSpacing));

            return jobVariantTooltip;
        }

        private void DrawPreviewItems(SpriteBatch spriteBatch, GUIComponent parent)
        {
            Point slotSize = new Point(parent.Rect.Height);
            int spacing = (int)(5 * GUI.Scale);
            int slotCount = PreviewItemIdentifiers.Count();
            int slotCountPerRow = Math.Min(slotCount, ItemsPerRow);
            int rows = (int)Math.Max(Math.Ceiling(PreviewItemIdentifiers.Count() / (float)ItemsPerRow), 1);

            float totalWidth = slotSize.X * slotCountPerRow + spacing * (slotCountPerRow - 1);
            float totalHeight = slotSize.Y * rows + spacing * (rows - 1);
            if (totalWidth > parent.Rect.Width)
            {
                slotSize = new Point(Math.Min((int)Math.Floor((slotSize.X - spacing) * (parent.Rect.Width / totalWidth)), (int)Math.Floor((slotSize.Y - spacing) * (parent.Rect.Height / totalHeight))));
            }
            int i = 0;
            Rectangle tooltipRect = Rectangle.Empty;
            LocalizedString tooltip = null;
            foreach (Identifier itemIdentifier in PreviewItemIdentifiers)
            {
                if (MapEntityPrefab.FindByIdentifier(itemIdentifier) is not ItemPrefab itemPrefab) { continue; }

                int row = (int)Math.Floor(i / (float)slotCountPerRow);
                int slotsPerThisRow = Math.Min((slotCount - row * slotCountPerRow), slotCountPerRow);
                Vector2 slotPos = new Vector2(parent.Rect.Center.X + (slotSize.X + spacing) * (i % slotCountPerRow - slotsPerThisRow * 0.5f), parent.Rect.Bottom - (rows * (slotSize.Y + spacing)) + (slotSize.Y + spacing) * row);

                Rectangle slotRect = new Rectangle(slotPos.ToPoint(), slotSize);
                Inventory.SlotSpriteSmall.Draw(spriteBatch, slotPos, scale: slotSize.X / (float)Inventory.SlotSpriteSmall.SourceRect.Width, color: slotRect.Contains(PlayerInput.MousePosition) ? Color.White : Color.White * 0.6f);

                Sprite icon = itemPrefab.InventoryIcon ?? itemPrefab.Sprite;
                float iconScale = Math.Min(Math.Min(slotSize.X / icon.size.X, slotSize.Y / icon.size.Y), 2f) * 0.9f;
                icon.Draw(spriteBatch, slotPos + slotSize.ToVector2() * 0.5f, scale: iconScale);

                int count = PreviewItems.Count(it => it.ItemIdentifier == itemIdentifier);
                if (count > 1)
                {
                    string itemCountText = "x" + count;
                    GUIStyle.Font.DrawString(spriteBatch, itemCountText, slotPos + slotSize.ToVector2() - GUIStyle.Font.MeasureString(itemCountText) - Vector2.UnitX * 5, Color.White);
                }

                if (slotRect.Contains(PlayerInput.MousePosition))
                {
                    tooltipRect = slotRect;
                    tooltip = itemPrefab.Name + '\n' + itemPrefab.Description;
                }
                i++;
            }
            if (!tooltip.IsNullOrEmpty())
            {
                GUIComponent.DrawToolTip(spriteBatch, tooltip, tooltipRect);
            }
        }
    }
}
