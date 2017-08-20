using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundSpritePrefab
    {
        [Flags]
        public enum SpawnPosType
        {
            None = 0,
            Wall = 1,
            RuinWall = 2,
            SeaFloor = 4
        }

        public readonly Sprite Sprite;

        public readonly Alignment Alignment;

        public readonly Vector2 Scale;

        public SpawnPosType SpawnPos;

        public readonly bool AlignWithSurface;
        
        public readonly Vector2 RandomRotation;

        public readonly Vector2 DepthRange;

        public readonly Particles.ParticleEmitter ParticleEmitter;
        public readonly Vector2 EmitterPosition;

        public readonly float SwingAmount;

        public readonly int Commonness;

        public Dictionary<string, int> OverrideCommonness;

        public BackgroundSpritePrefab(XElement element)
        {
            string alignmentStr = ToolBox.GetAttributeString(element, "alignment", "");

            if (string.IsNullOrEmpty(alignmentStr) || !Enum.TryParse(alignmentStr, out Alignment))
            {
                Alignment = Alignment.Top | Alignment.Bottom | Alignment.Left | Alignment.Right;
            }

            Commonness = ToolBox.GetAttributeInt(element, "commonness", 1);
            
            string[] spawnPosStrs = ToolBox.GetAttributeString(element, "spawnpos", "Wall").Split(',');
            foreach (string spawnPosStr in spawnPosStrs)
            {
                SpawnPosType parsedSpawnPos;
                if (Enum.TryParse(spawnPosStr.Trim(), out parsedSpawnPos))
                {
                    SpawnPos |= parsedSpawnPos;
                }
            }

            Scale.X = ToolBox.GetAttributeFloat(element, "minsize", 1.0f);
            Scale.Y = ToolBox.GetAttributeFloat(element, "maxsize", 1.0f);

            DepthRange = ToolBox.GetAttributeVector2(element, "depthrange", new Vector2(0.0f, 1.0f));

            AlignWithSurface = ToolBox.GetAttributeBool(element, "alignwithsurface", false);

            RandomRotation = ToolBox.GetAttributeVector2(element, "randomrotation", Vector2.Zero);
            RandomRotation.X = MathHelper.ToRadians(RandomRotation.X);
            RandomRotation.Y = MathHelper.ToRadians(RandomRotation.Y);

            SwingAmount = MathHelper.ToRadians(ToolBox.GetAttributeFloat(element, "swingamount", 0.0f));

            OverrideCommonness = new Dictionary<string, int>();

            foreach (XElement subElement in element.Elements())
            {
                switch(subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement);
                        break;
                    case "overridecommonness":
                        string levelType = ToolBox.GetAttributeString(subElement, "leveltype", "");
                        if (!OverrideCommonness.ContainsKey(levelType))
                        {
                            OverrideCommonness.Add(levelType, ToolBox.GetAttributeInt(subElement, "commonness", 1));
                        }
                        break;
                    case "particleemitter":
                        ParticleEmitter = new Particles.ParticleEmitter(subElement);
                        EmitterPosition = ToolBox.GetAttributeVector2(subElement, "position", Vector2.Zero);
                        break;
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
