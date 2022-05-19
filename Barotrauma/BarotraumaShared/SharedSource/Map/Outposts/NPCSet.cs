#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal class NPCSet : Prefab
    {
        public readonly static PrefabCollection<NPCSet> Sets = new PrefabCollection<NPCSet>();


        private readonly ImmutableArray<HumanPrefab> Humans;

        private bool Disposed { get; set; }

        public NPCSet(ContentXElement element, NPCSetsFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Humans = element.Elements().Select(npcElement => new HumanPrefab(npcElement, file)).ToImmutableArray();
        }

        public static HumanPrefab? Get(Identifier setIdentifier, Identifier npcidentifier)
        {
            HumanPrefab? prefab = Sets.Where(set => set.Identifier == setIdentifier).SelectMany(npcSet => npcSet.Humans.Where(npcSetHuman => npcSetHuman.Identifier == npcidentifier)).FirstOrDefault();

            if (prefab == null)
            {
                DebugConsole.ThrowError($"Could not find human prefab \"{npcidentifier}\" from \"{setIdentifier}\".");
                return null;
            }
            return prefab;
        }

        public override void Dispose() { }
    }
}