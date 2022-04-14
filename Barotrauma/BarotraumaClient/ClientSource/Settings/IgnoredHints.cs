#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public class IgnoredHints
    {
        private readonly HashSet<Identifier> identifiers = new HashSet<Identifier>();

        private IgnoredHints() { }
        
        private IgnoredHints(XElement element)
        {
            identifiers = element.GetAttributeIdentifierArray("identifiers", Array.Empty<Identifier>())
                .ToHashSet();
        }
        
        public static void Init(XElement? element)
        {
            if (element is null) { return; }
            
            Instance = new IgnoredHints(element);
        }

        public void SaveTo(XElement element)
        {
            element.SetAttributeValue("identifiers", string.Join(",", identifiers));
        }

        public bool Contains(Identifier identifier) => identifiers.Contains(identifier);

        public void Add(Identifier identifier) => identifiers.Add(identifier);

        public void Remove(Identifier identifier) => identifiers.Remove(identifier);

        public void Clear() => identifiers.Clear();

        public static IgnoredHints Instance { get; private set; } = new IgnoredHints();
    }
}