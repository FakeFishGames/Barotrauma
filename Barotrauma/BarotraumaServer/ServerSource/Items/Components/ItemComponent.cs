using System;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class ItemComponent : ISerializableEntity
    {
        private bool LoadElemProjSpecific(ContentXElement subElement)
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

        public virtual IEventData ServerGetEventData() => null;
    }

}
