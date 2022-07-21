using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    internal partial class EntitySpawnerComponent
    {
        public Vector2 DrawSize => Vector2.Zero;

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (!editing) { return; }

            switch (SpawnAreaShape)
            {
                case AreaShape.Rectangle:
                {
                    RectangleF rect = GetAreaRectangle(SpawnAreaBounds, SpawnAreaOffset, draw: true);
                    GUI.DrawRectangle(spriteBatch, rect.Location, rect.Size, GUIStyle.Red, isFilled: false, 0f, 4f);

                    if (MaximumAmountRangePadding > 0f)
                    {
                        rect.Inflate(MaximumAmountRangePadding, MaximumAmountRangePadding);
                        GUI.DrawRectangle(spriteBatch, rect.Location, rect.Size, GUIStyle.Red, isFilled: false, 0f, 2f);
                    }
                    break;
                }
                case AreaShape.Circle:
                    Vector2 center = item.WorldPosition;
                    center += SpawnAreaOffset;
                    center.Y = -center.Y;
                    spriteBatch.DrawCircle(center, SpawnAreaRadius, 32, GUIStyle.Red, thickness: 4f);

                    if (MaximumAmountRangePadding > 0f)
                    {
                        spriteBatch.DrawCircle(center, SpawnAreaRadius + MaximumAmountRangePadding, 32, GUIStyle.Red, thickness: 2f);
                    }
                    break;
            }

            if (!OnlySpawnWhenCrewInRange) { return; }

            switch (CrewAreaShape)
            {
                case AreaShape.Rectangle:
                {
                    RectangleF rect = GetAreaRectangle(CrewAreaBounds, CrewAreaOffset, draw: true);
                    GUI.DrawRectangle(spriteBatch, rect.Location, rect.Size, GUIStyle.Green, isFilled: false, 0f, 4f);
                    break;
                }
                case AreaShape.Circle:
                    Vector2 center = item.WorldPosition;
                    center += CrewAreaOffset;
                    center.Y = -center.Y;
                    spriteBatch.DrawCircle(center, CrewAreaRadius, 32, GUIStyle.Green);
                    break;
            }
        }
    }
}