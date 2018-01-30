using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class Map
    {
        private static Sprite iceTexture;
        private static Texture2D iceCraters;
        private static Texture2D iceCrack;

        private static Texture2D circleTexture;
        
        private Location highlightedLocation;

        public void Update(float deltaTime, Rectangle rect, float scale = 1.0f)
        {
            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);
            Vector2 offset = -currentLocation.MapPosition;

            float maxDist = 20.0f;
            float closestDist = 0.0f;
            highlightedLocation = null;
            for (int i = 0; i < locations.Count; i++)
            {
                Location location = locations[i];
                Vector2 pos = rectCenter + (location.MapPosition + offset) * scale;

                if (!rect.Contains(pos)) continue;

                float dist = Vector2.Distance(PlayerInput.MousePosition, pos);
                if (dist < maxDist && (highlightedLocation == null || dist < closestDist))
                {
                    closestDist = dist;
                    highlightedLocation = location;
                }
            }

            foreach (LocationConnection connection in connections)
            {
                if (highlightedLocation != currentLocation &&
                    connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(currentLocation))
                {
                    if (PlayerInput.LeftButtonClicked() &&
                        selectedLocation != highlightedLocation && highlightedLocation != null)
                    {
                        selectedConnection = connection;
                        selectedLocation = highlightedLocation;
                        
                        //clients aren't allowed to select the location without a permission
                        if (GameMain.Client == null || GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
                        {
                            OnLocationSelected?.Invoke(selectedLocation, selectedConnection);
                            GameMain.Client?.SendCampaignState();
                        }
                    }
                }
            }

#if DEBUG
            if (PlayerInput.DoubleClicked() && highlightedLocation != null)
            {
                var passedConnection = currentLocation.Connections.Find(c => c.OtherLocation(currentLocation) == highlightedLocation);
                if (passedConnection != null)
                {
                    passedConnection.Passed = true;
                }

                currentLocation = highlightedLocation;
                CurrentLocation.Discovered = true;
                OnLocationChanged?.Invoke(currentLocation);
                ProgressWorld();
            }
#endif
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle rect, float scale = 1.0f)
        {
            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);
            Vector2 offset = -currentLocation.MapPosition;

            Rectangle prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;
            GameMain.Instance.GraphicsDevice.ScissorRectangle = rect;

            iceTexture.DrawTiled(spriteBatch, new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height), Vector2.Zero, Color.White * 0.8f);


            for (int i = 0; i < locations.Count; i++)
            {
                Location location = locations[i];

                if (location.Type.HaloColor.A > 0)
                {
                    Vector2 pos = rectCenter + (location.MapPosition + offset) * scale;

                    spriteBatch.Draw(circleTexture, pos, null, location.Type.HaloColor * 0.1f, 0.0f,
                        new Vector2(512, 512), scale * 0.1f, SpriteEffects.None, 0);
                }
            }

            foreach (LocationConnection connection in connections)
            {
                Color crackColor = Color.White;
                
                if (selectedLocation != currentLocation &&
                    (connection.Locations.Contains(selectedLocation) && connection.Locations.Contains(currentLocation)))
                {
                    crackColor = Color.Red;
                }
                else if (highlightedLocation != currentLocation &&
                (connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(currentLocation)))
                {
                    crackColor = Color.Red * 0.5f;
                }
                else if (!connection.Passed)
                {
                    crackColor *= 0.5f;
                }

                for (int i = 0; i < connection.CrackSegments.Count; i++)
                {
                    var segment = connection.CrackSegments[i];

                    Vector2 start = rectCenter + (segment[0] + offset) * scale;
                    Vector2 end = rectCenter + (segment[1] + offset) * scale;

                    if (!rect.Contains(start) && !rect.Contains(end))
                    {
                        continue;
                    }
                    else
                    {
                        Vector2? intersection = MathUtils.GetLineRectangleIntersection(start, end, new Rectangle(rect.X, rect.Y + rect.Height, rect.Width, rect.Height));
                        if (intersection != null)
                        {
                            if (!rect.Contains(start))
                            {
                                start = (Vector2)intersection;
                            }
                            else
                            {
                                end = (Vector2)intersection;
                            }
                        }
                    }

                    float dist = Vector2.Distance(start, end);

                    int width = (int)(MathHelper.Lerp(5.0f, 25f, connection.Difficulty / 100.0f) * scale);

                    spriteBatch.Draw(iceCrack,
                        new Rectangle((int)start.X, (int)start.Y, (int)dist + 2, width),
                        new Rectangle(0, 0, iceCrack.Width, 60), crackColor, MathUtils.VectorToAngle(end - start),
                        new Vector2(0, 30), SpriteEffects.None, 0.01f);
                }



                if (GameMain.DebugDraw)
                {
                    Vector2 center = rectCenter + (connection.CenterPos + offset) * scale;
                    GUI.DrawString(spriteBatch, center, connection.Biome.Name + " (" + connection.Difficulty + ")", Color.White);
                }
            }

            for (int i = 0; i < DifficultyZones; i++)
            {
                float radius = size / 2 * ((i + 1.0f) / DifficultyZones);
                float textureSize = (radius / (circleTexture.Width / 2) * scale);

                spriteBatch.Draw(circleTexture, rectCenter + (offset + new Vector2(size / 2, size / 2)) * scale, null, Color.Black * 0.05f, 0.0f,
                    new Vector2(512, 512), textureSize, SpriteEffects.None, 0);
            }

            rect.Inflate(8, 8);
            GUI.DrawRectangle(spriteBatch, rect, Color.Black, false, 0.0f, 8);
            GUI.DrawRectangle(spriteBatch, rect, Color.LightGray);

            for (int i = 0; i < locations.Count; i++)
            {
                Location location = locations[i];
                Vector2 pos = rectCenter + (location.MapPosition + offset) * scale;

                Rectangle drawRect = location.Type.Sprite.SourceRect;
                drawRect.X = (int)pos.X - drawRect.Width / 2;
                drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                if (!rect.Intersects(drawRect)) continue;

                Color color = location.Connections.Find(c => c.Locations.Contains(currentLocation)) == null ? Color.White : Color.Green;
                color *= (location.Discovered) ? 0.8f : 0.5f;
                if (location == currentLocation) color = Color.Orange;
                
                spriteBatch.Draw(location.Type.Sprite.Texture, pos, null, color, 0.0f, location.Type.Sprite.size / 2, 0.25f * scale, SpriteEffects.None, 0.0f);
            }

            for (int i = 0; i < 3; i++)
            {
                Location location = (i == 0) ? highlightedLocation : selectedLocation;
                if (i == 2) location = currentLocation;

                if (location == null) continue;

                Vector2 pos = rectCenter + (location.MapPosition + offset) * scale;
                pos.X = (int)(pos.X + location.Type.Sprite.SourceRect.Width * 0.6f);
                pos.Y = (int)(pos.Y - 10);
                GUI.DrawString(spriteBatch, pos, location.Name, Color.White, Color.Black * 0.8f, 3);
                GUI.DrawString(spriteBatch, pos + Vector2.UnitY * 25, location.Type.DisplayName, Color.White, Color.Black * 0.8f, 3);
            }

            GameMain.Instance.GraphicsDevice.ScissorRectangle = prevScissorRect;
        }
    }
}
