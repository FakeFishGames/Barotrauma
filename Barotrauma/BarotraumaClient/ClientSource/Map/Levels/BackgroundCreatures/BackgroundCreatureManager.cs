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

        private readonly List<BackgroundCreaturePrefab> prefabs = new List<BackgroundCreaturePrefab>();
        private readonly List<BackgroundCreature> creatures = new List<BackgroundCreature>();

        public BackgroundCreatureManager(string configPath)
        {
            LoadConfig(new ContentFile(configPath, ContentType.BackgroundCreaturePrefabs));
        }

        public BackgroundCreatureManager(IEnumerable<ContentFile> files)
        {
            foreach(var file in files)
            {
                LoadConfig(file);
            }
        }

        private void LoadConfig(ContentFile config)
        {
            try
            {
                XDocument doc = XMLExtensions.TryLoadXml(config.Path);
                if (doc == null) { return; }
                var mainElement = doc.Root;
                if (mainElement.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    prefabs.Clear();
                    DebugConsole.NewMessage($"Overriding all background creatures with '{config.Path}'", Color.Yellow);
                }
                else if (prefabs.Any())
                {
                    DebugConsole.NewMessage($"Loading additional background creatures from file '{config.Path}'");
                }

                foreach (XElement element in mainElement.Elements())
                {
                    prefabs.Add(new BackgroundCreaturePrefab(element));
                };
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError(String.Format("Failed to load BackgroundCreatures from {0}", config.Path), e);
            }
        }

        public void SpawnCreatures(Level level, int count, Vector2? position = null)
        {
            creatures.Clear();

            if (prefabs.Count == 0) { return; }

            count = Math.Min(count, MaxCreatures);

            List<BackgroundCreaturePrefab> availablePrefabs = new List<BackgroundCreaturePrefab>(prefabs);

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

                var prefab = ToolBox.SelectWeightedRandom(availablePrefabs, availablePrefabs.Select(p => p.GetCommonness(level.GenerationParams)).ToList(), Rand.RandSync.ClientOnly);
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
                int margin = 500;
                foreach (BackgroundCreature creature in creatures)
                {
                    Rectangle extents = creature.GetExtents(cam);
                    bool wasVisible = creature.Visible;
                    creature.Visible =
                        extents.Right >= cam.WorldView.X - margin &&
                        extents.X <= cam.WorldView.Right + margin &&
                        extents.Bottom >= cam.WorldView.Y - cam.WorldView.Height - margin &&
                        extents.Y <= cam.WorldView.Y + margin;
                }

                checkVisibleTimer = VisibilityCheckInterval;
            }
            else
            {
                checkVisibleTimer -= deltaTime;
            }

            foreach (BackgroundCreature creature in creatures)
            {
                if (!creature.Visible) { continue; }
                creature.Update(deltaTime);
            }
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            foreach (BackgroundCreature creature in creatures)
            {
                if (!creature.Visible) { continue; }
                creature.Draw(spriteBatch, cam);
            }
        }

        public void DrawLights(SpriteBatch spriteBatch, Camera cam)
        {
            foreach (BackgroundCreature creature in creatures)
            {
                if (!creature.Visible) { continue; }
                creature.DrawLightSprite(spriteBatch, cam);
            }
        }
    }
}
