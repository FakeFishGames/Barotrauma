using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class OutpostTerminal : ItemComponent
    {
        public OutpostTerminal(Item item, ContentXElement element) : base(item, element)
        {
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);
    }
}
