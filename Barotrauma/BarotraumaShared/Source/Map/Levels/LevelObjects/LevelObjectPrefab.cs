using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LevelObjectPrefab : ISerializableEntity
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
        
        public readonly Sprite Sprite;

        public readonly Vector2 Scale;

        public SpawnPosType SpawnPos;

        public readonly XElement LevelTriggerElement;
        public Dictionary<string, float> OverrideCommonness;

        [Serialize("0.0,1.0", false)]
        public Vector2 DepthRange
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool AlignWithSurface
        {
            get;
            private set;
        }

        private Vector2 randomRotation;
        [Serialize("0.0,0.0", false)]
        public Vector2 RandomRotation
        {
            get { return randomRotation; }
            private set
            {
                randomRotation = new Vector2(MathHelper.ToRadians(value.X), MathHelper.ToRadians(value.Y));
            }
        }

        private float swingAmount;
        [Serialize(0.0f, false)]
        public float SwingAmount
        {
            get { return swingAmount; }
            private set
            {
                swingAmount = MathHelper.ToRadians(value);
            }
        }

        [Serialize(0.0f, false)]
        public float SwingFrequency
        {
            get;
            private set;
        }

        [Serialize("0.0,0.0", false)]
        public Vector2 ScaleOscillation
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float ScaleOscillationFrequency
        {
            get;
            private set;
        }

        [Serialize(1.0f, false)]
        public float Commonness
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float SonarDisruption
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            private set;
        }
        
        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get; private set;
        }

        public override string ToString()
        {
            return "LevelObjectPrefab (" + Name + ")";
        }

        public LevelObjectPrefab(XElement element)
        {
            string alignmentStr = element.GetAttributeString("alignment", "");

            SerializableProperty.DeserializeProperties(this, element);

            if (string.IsNullOrEmpty(alignmentStr) || !Enum.TryParse(alignmentStr, out Alignment))
            {
                Alignment = Alignment.Top | Alignment.Bottom | Alignment.Left | Alignment.Right;
            }
            
            string[] spawnPosStrs = element.GetAttributeString("spawnpos", "Wall").Split(',');
            foreach (string spawnPosStr in spawnPosStrs)
            {
                if (Enum.TryParse(spawnPosStr.Trim(), out SpawnPosType parsedSpawnPos))
                {
                    SpawnPos |= parsedSpawnPos;
                }
            }

            Scale.X = element.GetAttributeFloat("minsize", 1.0f);
            Scale.Y = element.GetAttributeFloat("maxsize", 1.0f);
            
            OverrideCommonness = new Dictionary<string, float>();

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
                            OverrideCommonness.Add(levelType, subElement.GetAttributeFloat("commonness", 1.0f));
                        }
                        break;
                    case "leveltrigger":
                    case "trigger":
                        LevelTriggerElement = subElement;
                        break;
                }
            }

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public float GetCommonness(string levelType)
        {
            if (!OverrideCommonness.TryGetValue(levelType, out float commonness))
            {
                return Commonness;
            }
            return commonness;
        }
    }
}
