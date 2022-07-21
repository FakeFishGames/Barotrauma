#nullable enable
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    internal class StartItem
    {
        public Identifier Item;
        public int Amount;
        
        public StartItem(XElement element)
        {
            Item = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            Amount = element.GetAttributeInt("amount", 1);
        }
    }

    /// <summary>
    /// Additive sets of items spawned only at the start of the game.
    /// </summary>
    internal class StartItemSet : PrefabWithUintIdentifier
    {
        public readonly static PrefabCollection<StartItemSet> Sets = new PrefabCollection<StartItemSet>();

        public readonly ImmutableArray<StartItem> Items;

        /// <summary>
        /// The order in which the sets are displayed in menus
        /// </summary>
        public readonly int Order;

        public StartItemSet(ContentXElement element, StartItemsFile file) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        {
            Items = element.Elements().Select(e => new StartItem(e!)).ToImmutableArray();
            Order = element.GetAttributeInt("order", 0);
        }

        public override void Dispose() { }
    }
}