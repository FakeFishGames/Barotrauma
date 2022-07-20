using System.Collections.Generic;

namespace Barotrauma
{
    public interface ISerializableEntity
    {
        string Name
        {
            get;
        }

        Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
        }
    }
}
