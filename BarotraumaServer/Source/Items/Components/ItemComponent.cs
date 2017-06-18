using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using System.IO;

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
