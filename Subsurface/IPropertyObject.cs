using System.Collections.Generic;

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
