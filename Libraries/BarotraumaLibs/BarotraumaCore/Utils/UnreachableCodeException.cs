using System;

namespace Barotrauma;

public sealed class UnreachableCodeException : Exception
{
    public UnreachableCodeException() : base(message: "Code that was supposed to be unreachable was executed.") { }
}