using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma.RuinGeneration
{
    [Flags]
    enum RuinStructureType
    {
        Wall = 1, CorridorWall = 2, Prop = 4, Back = 8, Door=16, Hatch=32
    }

    class RuinStructure
    {
        const string ConfigFile = "Content/Map/RuinConfig.xml";

        private static List<RuinStructure> list;

        public readonly MapEntityPrefab Prefab;

        public readonly Alignment Alignment;

        public readonly RuinStructureType Type;

        private int commonness;

        private RuinStructure(XElement element)
        {
            string prefab = ToolBox.GetAttributeString(element, "prefab", "").ToLowerInvariant();
            Prefab = MapEntityPrefab.list.Find(s => s.Name.ToLowerInvariant() == prefab);

            if (Prefab == null)
            {
                DebugConsole.ThrowError("Loading ruin structure failed - structure prefab ''"+prefab+" not found");
                return;
            }

            string alignmentStr = ToolBox.GetAttributeString(element,"alignment","Bottom");
            if (!Enum.TryParse<Alignment>(alignmentStr, true, out Alignment))
            {
                DebugConsole.ThrowError("Error in ruin structure ''"+prefab+"'' - "+alignmentStr+" is not a valid alignment");
            }
            

            string typeStr = ToolBox.GetAttributeString(element,"type","");
            if (!Enum.TryParse<RuinStructureType>(typeStr,true, out Type))
            {
                DebugConsole.ThrowError("Error in ruin structure ''" + prefab + "'' - " + typeStr + " is not a valid type");
                return;
            }

            commonness = ToolBox.GetAttributeInt(element, "commonness", 1);

            list.Add(this);
        }

        private static void Load()
        {
            list = new List<RuinStructure>();

            XDocument doc = ToolBox.TryLoadXml(ConfigFile);
            if (doc == null || doc.Root == null) return;

            foreach (XElement element in doc.Root.Elements())
            {
                new RuinStructure(element);
            }
        }

        public static RuinStructure GetRandom(RuinStructureType type, Alignment alignment)
        {
            if (list==null)
            {
                DebugConsole.Log("Loading ruin structures...");
                Load();
            }

            var matchingStructures = list.FindAll(rs => rs.Type.HasFlag(type) && rs.Alignment.HasFlag(alignment));

            if (!matchingStructures.Any()) return null;

            int totalCommonness = matchingStructures.Sum(m => m.commonness);

            int randomNumber = Rand.Int(totalCommonness + 1, false);

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
