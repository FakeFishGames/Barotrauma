using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public interface IMessage
    {
        bool CanWrite { get; }
        bool CanRead { get; }
    }
}
