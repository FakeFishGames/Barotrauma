using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class UISprite
    {
        public Sprite Sprite
        {
            get;
            private set;
        }

        public bool Tile
        {
            get;
            private set;
        }

        public bool Slice
        {
            get;
            set;
        }

        public Rectangle[] Slices
        {
            get;
            set;
        }

        public bool MaintainAspectRatio
        {
            get;
            private set;
        }

        public UISprite(Sprite sprite, bool tile, bool maintainAspectRatio)
        {
            Sprite = sprite;
            Tile = tile;
            MaintainAspectRatio = maintainAspectRatio;
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle rect, Color color, SpriteEffects spriteEffects)
        {
            if (Slice)
            {
                Vector2 pos = new Vector2(rect.X, rect.Y);

                int centerWidth = Math.Max(rect.Width - Slices[0].Width - Slices[2].Width, 0);
                int centerHeight = Math.Max(rect.Height - Slices[0].Height - Slices[8].Height, 0);

                Vector2 scale = new Vector2(
                    MathHelper.Clamp((float)rect.Width / (Slices[0].Width + Slices[2].Width), 0, 1),
                    MathHelper.Clamp((float)rect.Height / (Slices[0].Height + Slices[6].Height), 0, 1));

                for (int x = 0; x < 3; x++)
                {
                    float width = (x == 1 ? centerWidth : Slices[x].Width) * scale.X;
                    for (int y = 0; y < 3; y++)
                    {
                        float height = (y == 1 ? centerHeight : Slices[x + y * 3].Height) * scale.Y;

                        spriteBatch.Draw(Sprite.Texture,
                            new Rectangle((int)pos.X, (int)pos.Y, (int)width, (int)height),
                            Slices[x + y * 3],
                            color);

                        pos.Y += height;
                    }
                    pos.X += width;
                    pos.Y = rect.Y;
                }
            }
            else if (Tile)
            {
                Vector2 startPos = new Vector2(rect.X, rect.Y);
                Vector2 size = new Vector2(Math.Min(Sprite.SourceRect.Width, rect.Width), Math.Min(Sprite.SourceRect.Height, rect.Height));

                if (Sprite.size.X == 0.0f) size.X = rect.Width;
                if (Sprite.size.Y == 0.0f) size.Y = rect.Height;

                Sprite.DrawTiled(spriteBatch, startPos, size, null, color);
            }
            else
            {
                if (MaintainAspectRatio)
                {
                    float scale = (float)(rect.Width) / Sprite.SourceRect.Width;

                    spriteBatch.Draw(Sprite.Texture, rect,
                        new Rectangle(Sprite.SourceRect.X, Sprite.SourceRect.Y, (int)(Sprite.SourceRect.Width), (int)(rect.Height / scale)),
                        color, 0.0f, Vector2.Zero, spriteEffects, 0.0f);
                }
                else
                {
                    spriteBatch.Draw(Sprite.Texture, rect, Sprite.SourceRect, color, 0, Vector2.Zero, spriteEffects, 0);
                }
            }
        }
    }

    public class GUIComponentStyle
    {
        public readonly Vector4 Padding;

        public readonly Color Color;

        public readonly Color textColor;

        public readonly Color HoverColor;
        public readonly Color SelectedColor;

        public readonly Color OutlineColor;

        public readonly Dictionary<GUIComponent.ComponentState, List<UISprite>> Sprites;
        
        public Dictionary<string, GUIComponentStyle> ChildStyles;

        public GUIComponentStyle(XElement element)
        {
            Sprites = new Dictionary<GUIComponent.ComponentState, List<UISprite>>();
            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
            {
                Sprites[state] = new List<UISprite>();
            }

            ChildStyles = new Dictionary<string, GUIComponentStyle>();

            Padding = element.GetAttributeVector4("padding", Vector4.Zero);

            Vector4 colorVector = element.GetAttributeVector4("color", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            Color = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("textcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            textColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("hovercolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            HoverColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("selectedcolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            SelectedColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("outlinecolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            OutlineColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite sprite = new Sprite(subElement);
                        bool maintainAspect = subElement.GetAttributeBool("maintainaspectratio",false);
                        bool tile = subElement.GetAttributeBool("tile", true);

                        bool allStates = false;
                        GUIComponent.ComponentState spriteState = GUIComponent.ComponentState.None;
                        if (subElement.Attribute("state") != null)
                        {
                            string stateStr = subElement.GetAttributeString("state", "None");
                            Enum.TryParse(stateStr, out spriteState);
                        }
                        else
                        {
                            allStates = true;
                        }

                        UISprite newSprite = new UISprite(sprite, tile, maintainAspect);
                        Vector4 sliceVec = subElement.GetAttributeVector4("slice", Vector4.Zero);
                        if (sliceVec != Vector4.Zero)
                        {
                            Rectangle slice = new Rectangle((int)sliceVec.X, (int)sliceVec.Y, (int)(sliceVec.Z - sliceVec.X), (int)(sliceVec.W - sliceVec.Y));

                            newSprite.Slice = true;

                            newSprite.Slices = new Rectangle[9];

                            //top-left
                            newSprite.Slices[0] = new Rectangle(newSprite.Sprite.SourceRect.Location, slice.Location - newSprite.Sprite.SourceRect.Location);
                            //top-mid
                            newSprite.Slices[1] = new Rectangle(slice.Location.X, newSprite.Slices[0].Y, slice.Width, newSprite.Slices[0].Height);
                            //top-right
                            newSprite.Slices[2] = new Rectangle(slice.Right, newSprite.Slices[0].Y, newSprite.Sprite.SourceRect.Right - slice.Right, newSprite.Slices[0].Height);

                            //mid-left
                            newSprite.Slices[3] = new Rectangle(newSprite.Slices[0].X, slice.Y, newSprite.Slices[0].Width, slice.Height);
                            //center
                            newSprite.Slices[4] = slice;
                            //mid-right
                            newSprite.Slices[5] = new Rectangle(newSprite.Slices[2].X, slice.Y, newSprite.Slices[2].Width, slice.Height);

                            //bottom-left
                            newSprite.Slices[6] = new Rectangle(newSprite.Slices[0].X, slice.Bottom, newSprite.Slices[0].Width, newSprite.Sprite.SourceRect.Bottom - slice.Bottom);
                            //bottom-mid
                            newSprite.Slices[7] = new Rectangle(newSprite.Slices[1].X, slice.Bottom, newSprite.Slices[1].Width, newSprite.Sprite.SourceRect.Bottom - slice.Bottom);
                            //bottom-right
                            newSprite.Slices[8] = new Rectangle(newSprite.Slices[2].X, slice.Bottom, newSprite.Slices[2].Width, newSprite.Sprite.SourceRect.Bottom - slice.Bottom);
                        }

                        if (allStates)
                        {
                            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
                            {
                                Sprites[state].Add(newSprite);
                            }
                        }
                        else
                        {
                            Sprites[spriteState].Add(newSprite);
                        }

                        break;
                    default:
                        ChildStyles.Add(subElement.Name.ToString().ToLowerInvariant(), new GUIComponentStyle(subElement));
                        break;
                }
            }
        }
    }
}
