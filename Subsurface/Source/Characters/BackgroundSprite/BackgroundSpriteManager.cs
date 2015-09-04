using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class BackgroundSpriteManager
    {
        const int MaxSprites = 100;

        private List<BackgroundSpritePrefab> prefabs;
        private List<BackgroundSprite> activeSprites;

        public BackgroundSpriteManager(string configPath)
        {
            XDocument doc = ToolBox.TryLoadXml(configPath);
            if (doc == null) return;

            activeSprites = new List<BackgroundSprite>();
            prefabs = new List<BackgroundSpritePrefab>();

            foreach (XElement element in doc.Root.Elements())
            {
                prefabs.Add(new BackgroundSpritePrefab(element));
            }
        }

        public void Update(float deltaTime)
        {
            if (activeSprites.Count < MaxSprites)
            {
                WayPoint wp = WayPoint.WayPointList[Rand.Int(WayPoint.WayPointList.Count)];

                Vector2 pos = new Vector2(wp.Rect.X, wp.Rect.Y);
                pos += Rand.Vector(200.0f);

                var prefab = prefabs[Rand.Int(prefabs.Count)];

                int amount = Rand.Range(prefab.SwarmMin,prefab.SwarmMax);
                List<BackgroundSprite> swarmMembers = new List<BackgroundSprite>();
                
                for (int i = 0; i<amount; i++)
                {
                    var newSprite = new BackgroundSprite(prefab, pos);
                    activeSprites.Add(newSprite);
                    swarmMembers.Add(newSprite);
                }
                if (amount>0)
                {
                    Swarm swarm = new Swarm(swarmMembers, prefab.SwarmRadius);
                }


            }

            foreach (BackgroundSprite sprite in activeSprites)
            {
                sprite.Update(deltaTime);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            foreach (BackgroundSprite sprite in activeSprites)
            {
                sprite.Draw(spriteBatch);
            }
        }
    }
}
