using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface : ItemComponent
    {
        public CustomInterface(Item item, XElement element)
            : base(item, element)
        {
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);      
    }
}
