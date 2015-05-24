using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    interface IPropertyObject
    {
        Dictionary<string, ObjectProperty> ObjectProperties
        {
            get;
        }
    }
}
