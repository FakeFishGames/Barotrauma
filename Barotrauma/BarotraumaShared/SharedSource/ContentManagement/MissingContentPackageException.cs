#nullable enable
using System;

namespace Barotrauma
{
    public sealed class MissingContentPackageException : Exception
    {
        public override string Message { get; }
        
        public MissingContentPackageException(ContentPackage? whoAsked, string? missingPackage)
        {
            Message = $"\"{whoAsked?.Name ?? "[NULL]"}\" depends on a package " +
                      $"with name or ID \"{missingPackage ?? "[NULL]"}\" " +
                      $"that is not currently enabled.";
        }
    }
}
