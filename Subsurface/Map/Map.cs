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

        private Location currentLocation;
        private Location selectedLocation;

        public Map(int seed, int size)
        {
            this.seed = seed;

            this.size = size;

            levels = new List<Level>();

            locations = new List<Location>();

            connections = new List<LocationConnection>();

            GenerateLocations();

            //for (int i = 0; i<10; i++)
            //{
            //    Vector2 pos = new Vector2((float)Game1.random.NextDouble() * size, (float)Game1.random.NextDouble() * size);

            //    Location location = 
            //    locations.Add(location);
            //}

            //for (int i = 0; i < 10; i++)
            //{

            //    int closestIndex = 0;
            //    float closestDistance = 0.0f;
            //    for (int j = 0; j<10; j++)
            //    {
            //        if (j == i) continue;

            //        //ignore if already connected
            //        bool alreadyConnected = false;
            //        foreach (LocationConnection connection in connections)
            //        {
            //            if (connection.Locations.Contains(locations[i]) && connection.Locations.Contains(locations[j]))
            //            {
            //                alreadyConnected = true;
            //                break;
            //            }
            //        }

            //        if (alreadyConnected) continue;

            //        float dist = Vector2.Distance(locations[i].MapPosition, locations[j].MapPosition);
            //        if (closestDistance > 0.0f && dist > closestDistance) continue;

            //        closestDistance = dist;
            //        closestIndex = j;
            //    }

                
            //    connections.Add(new LocationConnection(locations[i], locations[closestIndex], level));
            //}

            currentLocation = locations[0];
        }

        private void GenerateLocations()
        {
            Voronoi voronoi = new Voronoi(0.5f);

            List<Vector2> sites = new List<Vector2>();
            for (int i = 0; i < 50; i++)
            {
                sites.Add(new Vector2((float)Game1.random.NextDouble() * size, (float)Game1.random.NextDouble() * size));
            }
            List<GraphEdge> edges = voronoi.MakeVoronoiGraph(sites, size, size);
            
            sites.Clear();
            foreach (GraphEdge edge in edges)
            {
                if (edge.point1 == edge.point2) continue;

                Location[] newLocations = new Location[2];
                newLocations[0] = locations.Find(l => l.MapPosition == edge.point1 || l.MapPosition == edge.point2);
                newLocations[1] = locations.Find(l => l != newLocations[0] && (l.MapPosition == edge.point1 || l.MapPosition == edge.point2));
                
                for (int i = 0; i < 2; i++)
                {
                    if (newLocations[i] != null) continue;

                    Vector2[] points = new Vector2[] { edge.point1, edge.point2 };

                    int positionIndex = Game1.random.Next(0, 1);

                    Vector2 position = points[positionIndex];
                    if (newLocations[1 - i] != null && newLocations[1 - i].MapPosition == position) position = points[1 - positionIndex];

                    newLocations[i] = Location.CreateRandom(position);
                    locations.Add(newLocations[i]);
                }               

                connections.Add(new LocationConnection(newLocations[0], newLocations[1], Level.CreateRandom()));
            }

            float minDistance = 50.0f;
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                LocationConnection connection = connections[i];

                if (Vector2.Distance(connection.Locations[0].MapPosition, connection.Locations[1].MapPosition) > minDistance) continue;

                locations.Remove(connection.Locations[0]);
                connections.Remove(connection);
           
                foreach (LocationConnection connection2 in connections)
                {
                    if (connection2.Locations[0] == connection.Locations[0]) connection2.Locations[0] = connection.Locations[1];
                    if (connection2.Locations[1] == connection.Locations[0]) connection2.Locations[1] = connection.Locations[1];
                }
            }

        }

        public void Draw(SpriteBatch spriteBatch, Rectangle rect)
        {
            GUI.DrawRectangle(spriteBatch, rect, Color.DarkBlue, true);

            Vector2 scale = new Vector2((float)rect.Width/ size, (float)rect.Height/size);

            float maxDist = 20.0f;
            float closestDist = 0.0f;
            Location highlightedLocation = null;
            foreach (Location location in locations)
            {
                Vector2 pos = location.MapPosition * scale;
                GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X + (int)pos.X, rect.Y + (int)pos.Y, 5, 5), Color.White, true);

                if (currentLocation == location)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X + (int)pos.X - 4, rect.Y + (int)pos.Y - 4, 5+8, 5+8), Color.Red, false);
                }

                float dist = Vector2.Distance(PlayerInput.MousePosition, new Vector2(rect.X + pos.X, rect.Y + pos.Y));
                if (dist < maxDist && (highlightedLocation == null || dist < closestDist))
                {
                    closestDist = dist;
                    highlightedLocation = location;
                }
            }

            if (highlightedLocation!=null)
            {
                Vector2 pos = highlightedLocation.MapPosition * scale;
                spriteBatch.DrawString(GUI.font, highlightedLocation.Name, pos + new Vector2(rect.X - 50, rect.Y), Color.White);
                GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X + (int)pos.X - 4, rect.Y + (int)pos.Y - 4, 5 + 8, 5 + 8), Color.White, false);
            }

            if (selectedLocation != null)
            {
                Vector2 pos = selectedLocation.MapPosition * scale;
                spriteBatch.DrawString(GUI.font, selectedLocation.Name, pos + new Vector2(rect.X - 50, rect.Y), Color.White);
                GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X + (int)pos.X - 4, rect.Y + (int)pos.Y - 4, 5 + 8, 5 + 8), Color.White, false);
            }

            Vector2 rectCorner = new Vector2(rect.X, rect.Y);
            foreach (LocationConnection connection in connections)
            {
                GUI.DrawLine(spriteBatch, 
                    connection.Locations[0].MapPosition * scale + rectCorner, 
                    connection.Locations[1].MapPosition * scale + rectCorner, Color.LightGray);
                
                if (highlightedLocation!=currentLocation &&
                    connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(currentLocation))
                {
                    GUI.DrawLine(spriteBatch,
                        connection.Locations[0].MapPosition * scale + rectCorner +Vector2.One,
                        connection.Locations[1].MapPosition * scale + rectCorner + Vector2.One, Color.White);

                    if (PlayerInput.LeftButtonClicked())
                        if(selectedLocation!=highlightedLocation && highlightedLocation!=null)
                    {
                        //currentLocation = highlightedLocation;
                        Game1.LobbyScreen.SelectLocation(highlightedLocation, connection);
                        selectedLocation = highlightedLocation;
                    }                
                }

                if (selectedLocation != currentLocation &&
                    (connection.Locations.Contains(selectedLocation) && connection.Locations.Contains(currentLocation)))
                {
                    GUI.DrawLine(spriteBatch,
                        connection.Locations[0].MapPosition * scale + rectCorner + Vector2.One,
                        connection.Locations[1].MapPosition * scale + rectCorner + Vector2.One, Color.White);

                }
            }

        }
    }


    class LocationConnection
    {
        Location[] locations;
        Level level;

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
