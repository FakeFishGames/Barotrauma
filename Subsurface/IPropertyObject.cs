using System.Collections.Generic;

namespace Subsurface
{
    interface IPropertyObject
    {
        string Name
        {
            get;
        }

        Dictionary<string, ObjectProperty> ObjectProperties
        {
            get;
        }
    }
}
