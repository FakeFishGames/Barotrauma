using System.Collections.Generic;

namespace Barotrauma
{
    interface ISerializableEntity
    {
        string Name
        {
            get;
        }

        Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
        }
    }
}
