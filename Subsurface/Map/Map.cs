using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Voronoi2;

namespace Subsurface
{
    class Map
    {
        private List<Level> levels;

        private List<Location> locations;

        private List<LocationConnection> connections;
        
        private int seed;
        private int size;

        private Texture2D iceTexture;
        private Texture2D iceCraters;
        private Texture2D iceCrack;

        private Location currentLocation;
        private Location selectedLocation;

        public Location CurrentLocation
        {
            get { return currentLocation; }
        }

        public Location SelectedLocation
        {
            get { return selectedLocation; }
        }


        public Map(int seed, int size)
        {
            this.seed = seed;

            this.size = size;

            levels = new List<Level>();

            locations = new List<Location>();

            connections = new List<LocationConnection>();

            iceTexture  = Game1.textureLoader.FromFile("Content/Map/iceSurface.png");
            iceCraters  = Game1.textureLoader.FromFile("Content/Map/iceCraters.png");
            iceCrack    = Game1.textureLoader.FromFile("Content/Map/iceCrack.png");

            GenerateLocations();

            currentLocation = locations[locations.Count/2];
        }

        private void GenerateLocations()
        {
            Voronoi voronoi = new Voronoi(0.5f);

            List<Vector2> sites = new List<Vector2>();
            for (int i = 0; i < 50; i++)
            {
                sites.Add(new Vector2(Rand.Range(0.0f, size), Rand.Range(0.0f, size)));
            }
            
            List<GraphEdge> edges = voronoi.MakeVoronoiGraph(sites, size, size);
            
            sites.Clear();
            foreach (GraphEdge edge in edges)
            {
                if (edge.point1 == edge.point2) continue;

                //remove points from the edge of the map
                if (edge.point1.X == 0 || edge.point1.X == size) continue;
                if (edge.point1.Y == 0 || edge.point1.Y == size) continue;
                if (edge.point2.X == 0 || edge.point2.X == size) continue;
                if (edge.point2.Y == 0 || edge.point2.Y == size) continue;

                Location[] newLocations = new Location[2];
                newLocations[0] = locations.Find(l => l.MapPosition == edge.point1 || l.MapPosition == edge.point2);
                newLocations[1] = locations.Find(l => l != newLocations[0] && (l.MapPosition == edge.point1 || l.MapPosition == edge.point2));
                
                for (int i = 0; i < 2; i++)
                {
                    if (newLocations[i] != null) continue;

                    Vector2[] points = new Vector2[] { edge.point1, edge.point2 };

                    int positionIndex = Rand.Int(1);

                    Vector2 position = points[positionIndex];
                    if (newLocations[1 - i] != null && newLocations[1 - i].MapPosition == position) position = points[1 - positionIndex];

                    newLocations[i] = Location.CreateRandom(position);
                    locations.Add(newLocations[i]);
                }
                int seed = (newLocations[0].GetHashCode() | newLocations[1].GetHashCode());
                connections.Add(new LocationConnection(newLocations[0], newLocations[1], Level.CreateRandom(seed.ToString())));


            }

            float minDistance = 50.0f;
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                LocationConnection connection = connections[i];

                if (Vector2.Distance(connection.Locations[0].MapPosition, connection.Locations[1].MapPosition) > minDistance)
                {
                    continue;
                }

                locations.Remove(connection.Locations[0]);
                connections.Remove(connection);

                foreach (LocationConnection connection2 in connections)
                {
                    if (connection == connection2) continue;
                    if (connection2.Locations[0] == connection.Locations[0]) connection2.Locations[0] = connection.Locations[1];
                    if (connection2.Locations[1] == connection.Locations[0]) connection2.Locations[1] = connection.Locations[1];
                }
            }

            for (int i = connections.Count - 1; i >= 0; i--)
            {
                LocationConnection connection = connections[i];

                for (int n = i-1; n >= 0; n--)
                {
                    if (connection.Locations.Contains(connections[n].Locations[0])
                        && connection.Locations.Contains(connections[n].Locations[1]))
                    {
                        connections.RemoveAt(i);
                    }
                }
            }

            foreach (LocationConnection connection in connections)
            {
                Vector2 start = connection.Locations[0].MapPosition;
                Vector2 end = connection.Locations[1].MapPosition;
                int generations = (int)(Math.Sqrt(Vector2.Distance(start, end) / 10.0f));
                connection.CrackSegments = GenerateCrack(start, end, generations);
            }
        }

        private List<Vector2[]> GenerateCrack(Vector2 start, Vector2 end, int generations)
        {
            List<Vector2[]> segments = new List<Vector2[]>();

            segments.Add(new Vector2[] {start, end});

            float offsetAmount = 5.0f;

            for (int n = 0; n < generations; n++)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    Vector2 startSegment = segments[i][0];
                    Vector2 endSegment = segments[i][1];

                    segments.RemoveAt(i);

                    Vector2 midPoint = (startSegment + endSegment) / 2.0f;

                    Vector2 normal = Vector2.Normalize(endSegment - startSegment);
                    normal = new Vector2(-normal.Y, normal.X);
                    midPoint += normal * Rand.Range(-offsetAmount, offsetAmount);

                    segments.Insert(i, new Vector2[] { startSegment, midPoint });
                    segments.Insert(i+1, new Vector2[] { midPoint, endSegment });

                    i++;
                }
            }

            return segments;
        }

        public void MoveToNextLocation()
        {
            currentLocation = selectedLocation;
            selectedLocation = null;
        }

        private Location highlightedLocation;
        public void Draw(SpriteBatch spriteBatch, Rectangle rect)
        {
            //GUI.DrawRectangle(spriteBatch, rect, Color.DarkBlue, true);

            spriteBatch.Draw(iceTexture, rect, Color.White);

            Vector2 rectCorner = new Vector2(rect.X, rect.Y);
            Vector2 scale = new Vector2((float)rect.Width/ size, (float)rect.Height/size);

            float maxDist = 20.0f;
            float closestDist = 0.0f;
            highlightedLocation = null;
            for (int i = 0; i < locations.Count;i++ )
            {
                Location location = locations[i];
                Vector2 pos = rectCorner + location.MapPosition * scale;

                float dist = Vector2.Distance(PlayerInput.MousePosition, new Vector2(pos.X, pos.Y));
                if (dist < maxDist && (highlightedLocation == null || dist < closestDist))
                {
                    closestDist = dist;
                    highlightedLocation = location;
                }
            }


            foreach (LocationConnection connection in connections)
            {
                Color crackColor = Color.White;

                if (highlightedLocation != currentLocation &&
                    connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(currentLocation))
                {
                    crackColor = Color.Red;

                    if (PlayerInput.LeftButtonClicked()&&
                        selectedLocation != highlightedLocation && highlightedLocation != null)
                    {
                        //currentLocation = highlightedLocation;
                        Game1.LobbyScreen.SelectLocation(highlightedLocation, connection);
                        selectedLocation = highlightedLocation;                        
                    } 
                }


                if (selectedLocation != currentLocation &&
                    (connection.Locations.Contains(selectedLocation) && connection.Locations.Contains(currentLocation)))
                {
                    crackColor = Color.Red;
                }

                foreach (Vector2[] segment in connection.CrackSegments)
                {
                    Vector2 start = segment[0] * scale + rectCorner;
                    Vector2 end = segment[1] * scale + rectCorner;
                    float dist = Vector2.Distance(start, end);

                    //spriteBatch.Draw(iceCrack,
                    //    new Rectangle((int)((start.X + end.X) / 2.0f), (int)((start.Y + end.Y) / 2.0f), (int)dist, 30),
                    //    new Rectangle(0, 0, iceCrack.Width, 60), crackColor, MathUtils.VectorToAngle(start - end),
                    //    new Vector2(dist / 2, 30), SpriteEffects.None, 0.01f);
                    GUI.DrawLine(spriteBatch,
                        segment[0] * scale + rectCorner,
                        segment[1] * scale + rectCorner, crackColor);
                }
            }

            for (int i = 0; i < locations.Count; i++)
            {
                Location location = locations[i];
                Vector2 pos = rectCorner  + location.MapPosition * scale;

                int imgIndex = i % 16;
                int xCell = imgIndex % 4;
                int yCell = (int)Math.Floor(imgIndex / 4.0f);
                spriteBatch.Draw(iceCraters, pos, 
                    new Rectangle(xCell * 64, yCell * 64, 64, 64), 
                    Color.White, i, 
                    new Vector2(32, 32), 0.5f*scale, SpriteEffects.None, 0.0f);

            }

            for (int i = 0; i < 3; i++ )
            {
                Location location = (i == 0) ? highlightedLocation : selectedLocation;
                if (i == 2) location = currentLocation;
                
                if (location == null) continue;

                Vector2 pos = rectCorner + location.MapPosition * scale;
                pos.X = (int)pos.X;
                pos.Y = (int)pos.Y;
                if (highlightedLocation==location)
                {
                    spriteBatch.DrawString(GUI.font, location.Name, pos + new Vector2(-50, -20), Color.DarkRed);
                }
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 4, (int)pos.Y - 4, 5 + 8, 5 + 8), Color.DarkRed, false);
            }

        }
    }


    class LocationConnection
    {
        private Location[] locations;
        private Level level;
        
        public List<Vector2[]> CrackSegments;

        public Location[] Locations
        {
            get { return locations; }
        }

        public Level Level
        {
            get { return level; }
        }

        public LocationConnection(Location location1, Location location2, Level level)
        {
            locations = new Location[] { location1, location2 };
            this.level = level;
        }
    }
}
