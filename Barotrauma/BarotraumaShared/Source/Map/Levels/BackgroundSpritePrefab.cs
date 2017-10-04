using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class BackgroundSpritePrefab
    {
        [Flags]
        public enum SpawnPosType
        {
            None = 0,
            Wall = 1,
            RuinWall = 2,
            SeaFloor = 4
        }
        
        public readonly Alignment Alignment;

        public readonly Vector2 DepthRange;

        public readonly Sprite Sprite;

        public readonly Vector2 Scale;

        public SpawnPosType SpawnPos;

        public readonly bool AlignWithSurface;
        
        public readonly Vector2 RandomRotation;
        
        public readonly float SwingAmount;

        public readonly int Commonness;

        public Dictionary<string, int> OverrideCommonness;

        public readonly XElement LevelTriggerElement;

        public BackgroundSpritePrefab(XElement element)
        {
            string alignmentStr = element.GetAttributeString("alignment", "");

            if (string.IsNullOrEmpty(alignmentStr) || !Enum.TryParse(alignmentStr, out Alignment))
            {
                Alignment = Alignment.Top | Alignment.Bottom | Alignment.Left | Alignment.Right;
            }

            Commonness = element.GetAttributeInt("commonness", 1);
            
            string[] spawnPosStrs = element.GetAttributeString("spawnpos", "Wall").Split(',');
            foreach (string spawnPosStr in spawnPosStrs)
            {
                SpawnPosType parsedSpawnPos;
                if (Enum.TryParse(spawnPosStr.Trim(), out parsedSpawnPos))
                {
                    SpawnPos |= parsedSpawnPos;
                }
            }

            Scale.X = element.GetAttributeFloat("minsize", 1.0f);
            Scale.Y = element.GetAttributeFloat("maxsize", 1.0f);

            DepthRange = element.GetAttributeVector2("depthrange", new Vector2(0.0f, 1.0f));

            AlignWithSurface = element.GetAttributeBool("alignwithsurface", false);

            RandomRotation = element.GetAttributeVector2("randomrotation", Vector2.Zero);
            RandomRotation.X = MathHelper.ToRadians(RandomRotation.X);
            RandomRotation.Y = MathHelper.ToRadians(RandomRotation.Y);

            SwingAmount = MathHelper.ToRadians(element.GetAttributeFloat("swingamount", 0.0f));

            OverrideCommonness = new Dictionary<string, int>();

            foreach (XElement subElement in element.Elements())
            {
                switch(subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement);
                        break;
                    case "overridecommonness":
                        string levelType = subElement.GetAttributeString("leveltype", "");
                        if (!OverrideCommonness.ContainsKey(levelType))
                        {
                            OverrideCommonness.Add(levelType, subElement.GetAttributeInt("commonness", 1));
                        }
                        break;
                    case "leveltrigger":
                    case "trigger":
                        LevelTriggerElement = subElement;
                        break;
#if CLIENT
                    case "particleemitter":
                        if (ParticleEmitterPrefabs == null)
                        {
                            ParticleEmitterPrefabs = new List<Particles.ParticleEmitterPrefab>();
                            EmitterPositions = new List<Vector2>();
                        }

                        ParticleEmitterPrefabs.Add(new Particles.ParticleEmitterPrefab(subElement));
                        EmitterPositions.Add(subElement.GetAttributeVector2("position", Vector2.Zero));
                        break;
                    case "sound":
                        SoundElement = subElement;
                        SoundPosition = subElement.GetAttributeVector2("position", Vector2.Zero);
                        break;
#endif
                }
            }
        }

        public int GetCommonness(string levelType)
        {
            int commonness = 0;
            if (!OverrideCommonness.TryGetValue(levelType, out commonness))
            {
                return Commonness;
            }

            return commonness;
        }

    }
}
