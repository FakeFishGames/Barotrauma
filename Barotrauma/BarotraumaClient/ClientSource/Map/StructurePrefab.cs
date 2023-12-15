﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    partial class StructurePrefab : MapEntityPrefab
    {
        public readonly Color BackgroundSpriteColor;

        public readonly ImmutableArray<DecorativeSprite> DecorativeSprites;
        public readonly ImmutableDictionary<int, ImmutableArray<DecorativeSprite>> DecorativeSpriteGroups;

        public override void UpdatePlacing(Camera cam)
        {
            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                Selected = null;
                return;
            }
            
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            Vector2 size = ScaledSize;
            Rectangle newRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
            
            if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.PrimaryMouseButtonHeld() && GUI.MouseOn == null)
                {
                    placePosition = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
                }
                newRect.X = (int)position.X;
                newRect.Y = (int)position.Y;
            }
            else
            {
                Vector2 placeSize = size;
                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;

                //don't allow resizing width/height to less than the grid size
                if (ResizeHorizontal && Math.Abs(placeSize.X) < Submarine.GridSize.X)
                {
                    placeSize.X = Submarine.GridSize.X;
                }
                if (ResizeVertical && Math.Abs(placeSize.Y) < Submarine.GridSize.Y)
                {
                    placeSize.Y = Submarine.GridSize.Y;
                }

                newRect = Submarine.AbsRect(placePosition, placeSize);
                if (PlayerInput.PrimaryMouseButtonReleased())
                {                    
                    newRect.Location -= MathUtils.ToPoint(Submarine.MainSub.Position);
                    var structure = new Structure(newRect, this, Submarine.MainSub)
                    {
                        Submarine = Submarine.MainSub
                    };
                    
                    SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { structure }, false));
                    placePosition = Vector2.Zero;
                    if (!PlayerInput.IsShiftDown())
                    {
                        Selected = null;
                    }
                    return;
                }
            }
        }
        
        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            Rectangle newRect = new Rectangle((int)position.X, (int)position.Y, (int)ScaledSize.X, (int)ScaledSize.Y);

            if (placePosition == Vector2.Zero)
            {
                newRect.X = (int)position.X;
                newRect.Y = (int)position.Y;
            }
            else
            {
                Vector2 placeSize = ScaledSize;
                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;

                newRect = Submarine.AbsRect(placePosition, placeSize);
            }

            Sprite.DrawTiled(spriteBatch, new Vector2(newRect.X, -newRect.Y), new Vector2(newRect.Width, newRect.Height), textureScale: TextureScale * Scale);
            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X - GameMain.GraphicsWidth, -newRect.Y, newRect.Width + GameMain.GraphicsWidth * 2, newRect.Height), Color.White);
            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X, -newRect.Y - GameMain.GraphicsHeight, newRect.Width, newRect.Height + GameMain.GraphicsHeight * 2), Color.White);
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Rectangle placeRect, float scale = 1.0f, float rotation = 0.0f, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            var position = placeRect.Location.ToVector2().FlipY();
            position += placeRect.Size.ToVector2() * 0.5f;
            
            Sprite.DrawTiled(
                spriteBatch, 
                position, 
                placeRect.Size.ToVector2(), 
                color: Color.White * 0.8f,
                origin: placeRect.Size.ToVector2() * 0.5f,
                rotation: rotation,
                textureScale: TextureScale * scale,
                spriteEffects: spriteEffects ^ Sprite.effects);
        }
    }
}
