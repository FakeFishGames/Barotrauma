using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundSpriteManager
    {
        const int MaxSprites = 100;

        const float checkActiveInterval = 1.0f;

        float checkActiveTimer;

        private List<BackgroundSpritePrefab> prefabs;
        private List<BackgroundSprite> activeSprites;

        public BackgroundSpriteManager(string configPath)
        {
            activeSprites = new List<BackgroundSprite>();
            prefabs = new List<BackgroundSpritePrefab>();

            XDocument doc = ToolBox.TryLoadXml(configPath);
            if (doc == null || doc.Root == null) return;

            foreach (XElement element in doc.Root.Elements())
            {
                prefabs.Add(new BackgroundSpritePrefab(element));
            }
        }

        public void SpawnSprites(int count, Vector2? position = null)
        {
            activeSprites.Clear();

            if (prefabs.Count == 0) return;

            count = Math.Min(count, MaxSprites);

            for (int i = 0; i < count; i++ )
            {
                Vector2 pos = Vector2.Zero;

                if (position == null)
                {
                    if (WayPoint.WayPointList.Count>0)
                    {
                        WayPoint wp = WayPoint.WayPointList[Rand.Int(WayPoint.WayPointList.Count)];

                        pos = new Vector2(wp.Rect.X, wp.Rect.Y);
                        pos += Rand.Vector(200.0f);
                    }
                    else
                    {
                        pos = Rand.Vector(2000.0f);
                    } 
                }
                else
                {
                    pos = (Vector2)position;
                }


                var prefab = prefabs[Rand.Int(prefabs.Count)];

                int amount = Rand.Range(prefab.SwarmMin, prefab.SwarmMax);
                List<BackgroundSprite> swarmMembers = new List<BackgroundSprite>();

                for (int n = 0; n < amount; n++)
                {
                    var newSprite = new BackgroundSprite(prefab, pos);
                    activeSprites.Add(newSprite);
                    swarmMembers.Add(newSprite);
                }
                if (amount > 0)
                {
                    new Swarm(swarmMembers, prefab.SwarmRadius);
                }
            }
        }

        public void ClearSprites()
        {
            activeSprites.Clear();
        }

        public void Update(Camera cam, float deltaTime)
        {
            if (checkActiveTimer<0.0f)
            {
                foreach (BackgroundSprite sprite in activeSprites)
                {
                    sprite.Enabled = (Math.Abs(sprite.TransformedPosition.X - cam.WorldViewCenter.X) < 4000.0f &&
                    Math.Abs(sprite.TransformedPosition.Y - cam.WorldViewCenter.Y) < 4000.0f);                    
                }

                checkActiveTimer = checkActiveInterval;
            }
            else
            {
                checkActiveTimer -= deltaTime;
            }

            foreach (BackgroundSprite sprite in activeSprites)
            {
                if (!sprite.Enabled) continue;
                sprite.Update(deltaTime);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            foreach (BackgroundSprite sprite in activeSprites)
            {
                if (!sprite.Enabled) continue;
                sprite.Draw(spriteBatch);
            }
        }
    }
}
