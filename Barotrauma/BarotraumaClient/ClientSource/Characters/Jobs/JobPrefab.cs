using Microsoft.Xna.Framework;
using System.Linq;
using System;
using System.Xml.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class JobPrefab : IPrefab, IDisposable
    {
        public GUIButton CreateInfoFrame(out GUIComponent buttonContainer)
        {
            int width = 500, height = 400;

            GUIButton frameHolder = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, frameHolder.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            GUIFrame frame = new GUIFrame(new RectTransform(new Point(width, height), frameHolder.RectTransform, Anchor.Center));
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), Name, font: GUI.LargeFont);

            var descriptionBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.15f) },
                Description, font: GUI.SmallFont, wrap: true);

            var skillContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.5f), paddedFrame.RectTransform)
                { RelativeOffset = new Vector2(0.0f, 0.2f + descriptionBlock.RectTransform.RelativeSize.Y) });
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                TextManager.Get("Skills"), font: GUI.LargeFont);
            foreach (SkillPrefab skill in Skills)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), skillContainer.RectTransform),
                    "   - " + TextManager.AddPunctuation(':', TextManager.Get("SkillName." + skill.Identifier), (int)skill.LevelRange.X + " - " + (int)skill.LevelRange.Y), 
                    font: GUI.SmallFont);
            }

            buttonContainer = paddedFrame;

            /*if (!ItemIdentifiers.TryGetValue(variant, out var itemIdentifiers)) { return backFrame; }
            var itemContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.5f), paddedFrame.RectTransform, Anchor.TopRight)
            { RelativeOffset = new Vector2(0.0f, 0.2f + descriptionBlock.RectTransform.RelativeSize.Y) })
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), itemContainer.RectTransform),
                TextManager.Get("Items", fallBackTag: "mapentitycategory.equipment"), font: GUI.LargeFont);
            foreach (string identifier in itemIdentifiers.Distinct())
            {
                if (!(MapEntityPrefab.Find(name: null, identifier: identifier) is ItemPrefab itemPrefab)) { continue; }
                int count = itemIdentifiers.Count(i => i == identifier);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), itemContainer.RectTransform),
                    "   - " + (count == 1 ? itemPrefab.Name : itemPrefab.Name + " x" + count),
                    font: GUI.SmallFont);
            }*/

            return frameHolder;
        }


        public class OutfitPreview
        {
            /// <summary>
            /// Pair.First = sprite, Pair.Second = draw offset
            /// </summary>
            public readonly List<Pair<Sprite, Vector2>> Sprites;
            public Vector2 Dimensions;

            public OutfitPreview()
            {
                Sprites = new List<Pair<Sprite, Vector2>>();
                Dimensions = Vector2.One;
            }

            public void AddSprite(Sprite sprite, Vector2 drawOffset)
            {
                Sprites.Add(new Pair<Sprite, Vector2>(sprite, drawOffset));
            }
        }

        public List<OutfitPreview> GetJobOutfitSprites(Gender gender, bool useInventoryIcon, out Vector2 maxDimensions)
        {
            List<OutfitPreview> outfitPreviews = new List<OutfitPreview>();
            maxDimensions = Vector2.One;

            var equipIdentifiers = Element.GetChildElements("ItemSet").Elements().Where(e => e.GetAttributeBool("outfit", false)).Select(e => e.GetAttributeString("identifier", ""));

            var outfitPrefabs = ItemPrefab.Prefabs.Where(itemPrefab => equipIdentifiers.Contains(itemPrefab.Identifier)).ToList();
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
                    XElement spriteElement = children[n];
                    string spriteTexture = spriteElement.GetAttributeString("texture", "").Replace("[GENDER]", (gender == Gender.Female) ? "female" : "male");
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
}
