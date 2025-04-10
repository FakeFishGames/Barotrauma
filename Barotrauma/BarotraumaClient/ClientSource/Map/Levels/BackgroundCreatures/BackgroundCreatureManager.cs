using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundCreatureManager
    {
        const int MaxCreatures = 100;

        const float VisibilityCheckInterval = 1.0f;

        private float checkVisibleTimer;

        private readonly List<BackgroundCreature> creatures = new List<BackgroundCreature>();

        private readonly List<BackgroundCreature> visibleCreatures = new List<BackgroundCreature>();

        public BackgroundCreatureManager()
        {
            /*foreach(var file in files)
            {
                LoadConfig(file.Path);
            }*/
        }

        /*public BackgroundCreatureManager(string path)
        {
            DebugConsole.AddWarning($"Couldn't find any BackgroundCreaturePrefabs files, falling back to {path}");
            LoadConfig(ContentPath.FromRaw(null, path));
        }

        private void LoadConfig(ContentPath configPath)
        {
            try
            {
                XDocument doc = XMLExtensions.TryLoadXml(configPath);
                if (doc == null) { return; }
                var mainElement = doc.Root.FromPackage(configPath.ContentPackage);
                if (mainElement.IsOverride())
                {
                    mainElement = mainElement.FirstElement();
                    Prefabs.Clear();
                    DebugConsole.NewMessage($"Overriding all background creatures with '{configPath}'", Color.MediumPurple);
                }
                else if (Prefabs.Any())
                {
                    DebugConsole.NewMessage($"Loading additional background creatures from file '{configPath}'");
                }

                foreach (var element in mainElement.Elements())
                {
                    Prefabs.Add(new BackgroundCreaturePrefab(element));
                };
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError(String.Format("Failed to load BackgroundCreatures from {0}", configPath), e);
            }
        }*/

        public void SpawnCreatures(Level level, int count, Vector2? position = null)
        {
            creatures.Clear();

            List<BackgroundCreaturePrefab> availablePrefabs = new List<BackgroundCreaturePrefab>(BackgroundCreaturePrefab.Prefabs.OrderBy(p => p.Identifier.Value));
            if (availablePrefabs.Count == 0) { return; }

            count = Math.Min(count, MaxCreatures);

            for (int i = 0; i < count; i++)
            {
                Vector2 pos = Vector2.Zero;
                if (position == null)
                {
                    var wayPoints = WayPoint.WayPointList.FindAll(wp => wp.Submarine == null);
                    if (wayPoints.Any())
                    {
                        WayPoint wp = wayPoints[Rand.Int(wayPoints.Count, Rand.RandSync.ClientOnly)];
                        pos = new Vector2(wp.Rect.X, wp.Rect.Y);
                        pos += Rand.Vector(200.0f, Rand.RandSync.ClientOnly);
                    }
                    else
                    {
                        pos = Rand.Vector(2000.0f, Rand.RandSync.ClientOnly);
                    }
                }
                else
                {
                    pos = (Vector2)position;
                }

                var prefab = ToolBox.SelectWeightedRandom(availablePrefabs, availablePrefabs.Select(p => p.GetCommonness(level?.LevelData)).ToList(), Rand.RandSync.ClientOnly);
                if (prefab == null) { break; }

                int amount = Rand.Range(prefab.SwarmMin, prefab.SwarmMax + 1, Rand.RandSync.ClientOnly);
                List<BackgroundCreature> swarmMembers = new List<BackgroundCreature>();
                for (int n = 0; n < amount; n++)
                {
                    var creature = new BackgroundCreature(prefab, pos + Rand.Vector(Rand.Range(0.0f, prefab.SwarmRadius, Rand.RandSync.ClientOnly), Rand.RandSync.ClientOnly));
                    creatures.Add(creature);
                    swarmMembers.Add(creature);
                }
                if (amount > 1)
                {
                    new Swarm(swarmMembers, prefab.SwarmRadius, prefab.SwarmCohesion);
                }
                if (creatures.Count(c => c.Prefab == prefab) > prefab.MaxCount)
                {
                    availablePrefabs.Remove(prefab);
                    if (availablePrefabs.Count <= 0) { break; }
                }
            }
        }

        public void Clear()
        {
            creatures.Clear();
        }

        public void Update(float deltaTime, Camera cam)
        {
            if (checkVisibleTimer < 0.0f)
            {
                visibleCreatures.Clear();
                int margin = 500;
                foreach (BackgroundCreature creature in creatures)
                {
                    Rectangle extents = creature.GetExtents(cam);
                    creature.Visible =
                        extents.Right >= cam.WorldView.X - margin &&
                        extents.X <= cam.WorldView.Right + margin &&
                        extents.Bottom >= cam.WorldView.Y - cam.WorldView.Height - margin &&
                        extents.Y <= cam.WorldView.Y + margin;
                    if (creature.Visible)
                    {
                        //insertion sort according to depth
                        int i = 0;
                        while (i < visibleCreatures.Count)
                        {
                            if (visibleCreatures[i].Depth < creature.Depth) { break; }
                            i++;
                        }
                        visibleCreatures.Insert(i, creature);                        
                    }
                }

                checkVisibleTimer = VisibilityCheckInterval;
            }
            else
            {
                checkVisibleTimer -= deltaTime;
            }

            foreach (BackgroundCreature creature in visibleCreatures)
            {
                creature.Update(deltaTime);
            }
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            foreach (BackgroundCreature creature in visibleCreatures)
            {
                creature.Draw(spriteBatch, cam);
            }
        }

        public void DrawLights(SpriteBatch spriteBatch, Camera cam)
        {
            foreach (BackgroundCreature creature in visibleCreatures)
            {
                creature.DrawLightSprite(spriteBatch, cam);
            }
        }
    }
}
