using Barotrauma.IO;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    static class WikiImage
    {
        public static Rectangle CalculateBoundingBox(Character character)
        {
            Rectangle boundingBox = new Rectangle(character.WorldPosition.ToPoint(), Point.Zero);

            void addPointsToBBox(float extentX, float extentY, Vector2 worldPos, Vector2 origin, float rotation)
            {
                float sinRotation = (float)Math.Sin((double)rotation);
                float cosRotation = (float)Math.Cos((double)rotation);

                origin = new Vector2(
                    origin.X * cosRotation + origin.Y * sinRotation,
                    origin.X * sinRotation - origin.Y * cosRotation);
                var limbPos = worldPos.ToPoint();
                boundingBox.AddPoint(limbPos);
                Vector2 xExtend = new Vector2((extentX * cosRotation), (extentX * sinRotation));
                Vector2 yExtend = new Vector2((extentY * sinRotation), (-extentY * cosRotation));
                boundingBox.AddPoint(limbPos + (xExtend + yExtend - origin).ToPoint());
                boundingBox.AddPoint(limbPos + (xExtend - yExtend - origin).ToPoint());
                boundingBox.AddPoint(limbPos + (-xExtend - yExtend - origin).ToPoint());
                boundingBox.AddPoint(limbPos + (-xExtend + yExtend - origin).ToPoint());
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb.ActiveSprite == null) { continue; }
                float extentX = (float)limb.ActiveSprite.size.X * limb.Scale * limb.TextureScale * 0.5f;
                //extentX = ConvertUnits.ToDisplayUnits(extentX);
                float extentY = (float)limb.ActiveSprite.size.Y * limb.Scale * limb.TextureScale * 0.5f;
                //extentY = ConvertUnits.ToDisplayUnits(extentY);

                Vector2 origin = (limb.ActiveSprite.Origin - (limb.ActiveSprite.SourceRect.Size.ToVector2() * 0.5f)) * limb.Scale * limb.TextureScale;
                addPointsToBBox(extentX, extentY, limb.WorldPosition, origin, limb.body.Rotation);
            }


            if (character.Inventory != null)
            {
                foreach (var item in character.Inventory.AllItems)
                {
                    if (item?.Sprite != null && item?.body != null)
                    {
                        float extentX = (float)item.Sprite.size.X * item.Scale * 0.5f;
                        //extentX = ConvertUnits.ToDisplayUnits(extentX);
                        float extentY = (float)item.Sprite.size.Y * item.Scale * 0.5f;
                        //extentY = ConvertUnits.ToDisplayUnits(extentY);

                        Vector2 origin = (item.Sprite.Origin - (item.Sprite.SourceRect.Size.ToVector2() * 0.5f)) * item.Scale;
                        addPointsToBBox(extentX, extentY, item.WorldPosition, origin, item.body.Rotation);
                    }
                }
            }

            boundingBox.X -= 25; boundingBox.Y -= 25;
            boundingBox.Width += 50; boundingBox.Height += 50;

            return boundingBox;
        }

        public static void Create(Character character)
        {
            Rectangle boundingBox = CalculateBoundingBox(character);

            int texWidth = Math.Clamp((int)(boundingBox.Width * 2.5f), 512, 4096);
            float zoom = (float)texWidth / (float)boundingBox.Width;
            int texHeight = (int)(zoom * boundingBox.Height);

            using Camera cam = new Camera();
            cam.SetResolution(new Point(texWidth, texHeight));
            cam.MaxZoom = zoom;
            cam.MinZoom = zoom * 0.5f;
            cam.Zoom = zoom;
            cam.Position = boundingBox.Center.ToVector2();
            cam.UpdateTransform(false);

            using (RenderTarget2D rt = new RenderTarget2D(
                 GameMain.Instance.GraphicsDevice,
                 texWidth, texHeight, false, SurfaceFormat.Color, DepthFormat.None))
            {
                using (SpriteBatch spriteBatch = new SpriteBatch(GameMain.Instance.GraphicsDevice))
                {
                    Viewport prevViewport = GameMain.Instance.GraphicsDevice.Viewport;
                    GameMain.Instance.GraphicsDevice.Viewport = new Viewport(0, 0, texWidth, texHeight);
                    GameMain.Instance.GraphicsDevice.SetRenderTarget(rt);
                    GameMain.Instance.GraphicsDevice.Clear(Color.Transparent);
                    spriteBatch.Begin(SpriteSortMode.BackToFront, transformMatrix: cam.Transform);
                    character.Draw(spriteBatch, cam);
                    if (character.Inventory != null)
                    {
                        foreach (var item in character.Inventory.AllItems)
                        {
                            if (item != null)
                            {
                                item.Draw(spriteBatch, false, false);
                                item.Draw(spriteBatch, false, true);
                            }
                        }
                    }
                    spriteBatch.End();
                    GameMain.Instance.GraphicsDevice.SetRenderTarget(null);
                    GameMain.Instance.GraphicsDevice.Viewport = prevViewport;
                    using (FileStream fs = File.Open("wikiimage.png", System.IO.FileMode.Create))
                    {
                        rt.SaveAsPng(fs, texWidth, texHeight);
                    }
                }
            }
        }

        public static void Create(Submarine sub)
        {
            int width = 4096; int height = 4096;

            Rectangle subDimensions = sub.CalculateDimensions(false);
            Vector2 viewPos = subDimensions.Center.ToVector2();
            float scale = Math.Min(width / (float)subDimensions.Width, height / (float)subDimensions.Height);

            var viewMatrix = Matrix.CreateTranslation(new Vector3(width / 2.0f, height / 2.0f, 0));
            var transform = Matrix.CreateTranslation(
                new Vector3(-viewPos.X, viewPos.Y, 0)) *
                Matrix.CreateScale(new Vector3(scale, scale, 1)) *
                viewMatrix;

            using (RenderTarget2D rt = new RenderTarget2D(
                 GameMain.Instance.GraphicsDevice,
                 width, height, false, SurfaceFormat.Color, DepthFormat.None))
            using (SpriteBatch spriteBatch = new SpriteBatch(GameMain.Instance.GraphicsDevice))
            {
                Viewport prevViewport = GameMain.Instance.GraphicsDevice.Viewport;
                GameMain.Instance.GraphicsDevice.Viewport = new Viewport(0, 0, width, height);
                GameMain.Instance.GraphicsDevice.SetRenderTarget(rt);
                GameMain.Instance.GraphicsDevice.Clear(Color.Transparent);

                spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, null, null, null, null, transform);
                Submarine.Draw(spriteBatch);
                Submarine.DrawFront(spriteBatch);
                Submarine.DrawDamageable(spriteBatch, null);
                spriteBatch.End();

                GameMain.Instance.GraphicsDevice.SetRenderTarget(null);
                GameMain.Instance.GraphicsDevice.Viewport = prevViewport;
                using (FileStream fs = File.Open("wikiimage.png", System.IO.FileMode.Create))
                {
                    rt.SaveAsPng(fs, width, height);
                }
            }
        }
    }
}
