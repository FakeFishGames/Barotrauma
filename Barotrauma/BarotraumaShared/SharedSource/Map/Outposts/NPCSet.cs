#nullable enable
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal class NPCSet : Prefab
    {
        public readonly static PrefabCollection<NPCSet> Sets = new PrefabCollection<NPCSet>();

        private readonly ImmutableArray<HumanPrefab> Humans;

        public NPCSet(ContentXElement element, NPCSetsFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Humans = element.Elements().Select(npcElement => new HumanPrefab(npcElement, file, Identifier)).ToImmutableArray();
        }

        public static HumanPrefab? Get(Identifier setIdentifier, Identifier npcidentifier, bool logError = true)
        {
            HumanPrefab? prefab = Sets.Where(set => set.Identifier == setIdentifier).SelectMany(npcSet => npcSet.Humans.Where(npcSetHuman => npcSetHuman.Identifier == npcidentifier)).FirstOrDefault();

            if (prefab == null)
            {
                if (logError)
                {
                    DebugConsole.ThrowError($"Could not find human prefab \"{npcidentifier}\" from \"{setIdentifier}\".");
                }
                return null;
            }
            return prefab;
        }

        public override void Dispose() { }
    }
}