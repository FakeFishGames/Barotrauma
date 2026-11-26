#nullable enable
using Barotrauma.Extensions;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public sealed class NPCSet : Prefab
    {
        public readonly static PrefabCollection<NPCSet> Sets = new PrefabCollection<NPCSet>();

        public readonly ImmutableArray<HumanPrefab> Humans;

        public NPCSet(ContentXElement element, NPCSetsFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Humans = element.Elements().Select(npcElement => new HumanPrefab(npcElement, file, Identifier)).ToImmutableArray();
        }

        public static HumanPrefab? Get(Identifier setIdentifier, Identifier npcidentifier, bool logError = true, ContentPackage? contentPackageToLogInError = null)
        {
            HumanPrefab? prefab = Sets.Where(set => set.Identifier == setIdentifier).SelectMany(npcSet => npcSet.Humans.Where(npcSetHuman => npcSetHuman.Identifier == npcidentifier)).FirstOrDefault();

            if (prefab == null)
            {
                if (logError)
                {
                    DebugConsole.ThrowError($"Could not find human prefab \"{npcidentifier}\" from \"{setIdentifier}\".", contentPackage: contentPackageToLogInError);
                }
                return null;
            }
            return prefab;
        }

        /// <summary>
        /// Gets a random NPC from the specified set.
        /// </summary>
        public static HumanPrefab? Get(Identifier setIdentifier, Rand.RandSync randSync, bool logError = true, ContentPackage? contentPackageToLogInError = null)
        {
            var sets = Sets.Where(set => set.Identifier == setIdentifier);
            if (sets.None())
            {
                if (logError)
                {
                    DebugConsole.ThrowError($"Could not find NPC sets with the identifier \"{setIdentifier}\".", contentPackage: contentPackageToLogInError);
                }
                return null;
            }

            var prefabs = sets.SelectMany(npcSet => npcSet.Humans);
            return prefabs.GetRandomByWeight(npc => npc.Commonness, randSync);
        }

        public override void Dispose() { }
    }
}