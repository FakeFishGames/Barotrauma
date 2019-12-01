using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    public interface IPrefab
    {
        string OriginalName { get; }
        string Identifier { get; }
        string FilePath { get; }
        ContentPackage ContentPackage { get; }
    }
}
