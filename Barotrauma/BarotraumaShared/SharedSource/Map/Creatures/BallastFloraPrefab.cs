using System;
using System.Collections;
using System.Xml.Linq;

namespace Barotrauma
{
    class BallastFloraPrefab : Prefab
    {
        public string OriginalName { get; }
        public LocalizedString DisplayName { get; }
        public ContentXElement Element { get; }

        public bool Disposed;

        public static readonly PrefabCollection<BallastFloraPrefab> Prefabs = new PrefabCollection<BallastFloraPrefab>();

        public BallastFloraPrefab(ContentXElement element, BallastFloraFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            OriginalName = element.GetAttributeString("name", "");
            DisplayName = TextManager.Get(Identifier).Fallback(OriginalName);
            Element = element;
        }

        public static BallastFloraPrefab Find(Identifier identifier)
        {
            return Prefabs.ContainsKey(identifier) ? Prefabs[identifier] : null;
        }

        public override void Dispose() { }
    }
}