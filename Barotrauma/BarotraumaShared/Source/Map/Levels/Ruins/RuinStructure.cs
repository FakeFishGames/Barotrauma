using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.RuinGeneration
{
    [Flags]
    enum RuinStructureType
    {
        Wall = 1, CorridorWall = 2, Prop = 4, Back = 8, Door=16, Hatch=32, HeavyWall=64
    }

    class RuinStructure
    {
        private static List<RuinStructure> list;

        public readonly MapEntityPrefab Prefab;

        public readonly Alignment Alignment;

        public readonly RuinStructureType Type;

        private int commonness;

        private RuinStructure(XElement element)
        {
            string name = element.GetAttributeString("prefab", "");
            Prefab = MapEntityPrefab.Find(name);

            if (Prefab == null)
            {
                DebugConsole.ThrowError("Loading ruin structure failed - structure prefab \"" + name + " not found");
                return;
            }

            string alignmentStr = element.GetAttributeString("alignment", "Bottom");
            if (!Enum.TryParse(alignmentStr, true, out Alignment))
            {
                DebugConsole.ThrowError("Error in ruin structure \"" + name + "\" - " + alignmentStr + " is not a valid alignment");
            }


            string typeStr = element.GetAttributeString("type", "");
            if (!Enum.TryParse(typeStr, true, out Type))
            {
                DebugConsole.ThrowError("Error in ruin structure \"" + name + "\" - " + typeStr + " is not a valid type");
                return;
            }

            commonness = element.GetAttributeInt("commonness", 1);

            list.Add(this);
        }

        private static void Load()
        {
            list = new List<RuinStructure>();
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.RuinConfig))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc == null || doc.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    new RuinStructure(element);
                }
            }
        }

        public static RuinStructure GetRandom(RuinStructureType type, Alignment alignment)
        {
            if (list == null)
            {
                DebugConsole.Log("Loading ruin structures...");
                Load();
            }

            var matchingStructures = list.FindAll(rs => rs.Type.HasFlag(type) && rs.Alignment.HasFlag(alignment));

            if (!matchingStructures.Any()) return null;

            int totalCommonness = matchingStructures.Sum(m => m.commonness);

            int randomNumber = Rand.Int(totalCommonness + 1, Rand.RandSync.Server);

            foreach (RuinStructure ruinStructure in matchingStructures)
            {
                if (randomNumber <= ruinStructure.commonness)
                {
                    return ruinStructure;
                }

                randomNumber -= ruinStructure.commonness;
            }

            return null;
        }
    }
}
