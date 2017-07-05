using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemComponent : IPropertyObject
    {
        private bool LoadElemProjSpecific(XElement subElement)
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "guiframe":
                    break;
                case "sound":
                    break;
                default:
                    return false; //unknown element
            }
            return true; //element processed
        }
    }

}
