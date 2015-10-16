using System.Collections.Generic;

namespace Barotrauma
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
