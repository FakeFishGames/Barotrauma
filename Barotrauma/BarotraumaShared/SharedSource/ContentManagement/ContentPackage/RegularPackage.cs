using System.Xml.Linq;

namespace Barotrauma
{
    public class RegularPackage : ContentPackage
    {
        public RegularPackage(XDocument doc, string path) : base(doc, path)
        {
            AssertCondition(!doc.Root.GetAttributeBool("corepackage", false), "Expected a regular package, got a core package");
        }
    }
}