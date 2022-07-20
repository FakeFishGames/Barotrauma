#nullable enable
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class CompletedTutorials
    {
        private readonly HashSet<Identifier> identifiers = new HashSet<Identifier>();

        private CompletedTutorials() { }
        
        private CompletedTutorials(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                identifiers.Add(subElement.GetAttributeIdentifier("name", Identifier.Empty));
            }
        }
        
        public static void Init(XElement? element)
        {
            if (element is null) { return; }
            
            Instance = new CompletedTutorials(element);
        }

        public void SaveTo(XElement element)
        {
            foreach (var id in identifiers)
            {
                element.Add(new XElement("Tutorial", new XAttribute("name", id.Value)));
            }
        }

        public bool Contains(Identifier identifier) => identifiers.Contains(identifier);

        public void Add(Identifier identifier) => identifiers.Add(identifier);
        
        public void Remove(Identifier identifier) => identifiers.Remove(identifier);

        public static CompletedTutorials Instance { get; private set; } = new CompletedTutorials();
    }
}
