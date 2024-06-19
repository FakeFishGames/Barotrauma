#nullable enable

using System;

namespace Barotrauma.Networking
{
    /// <summary>
    /// Represents an account ID of a client that has not been authenticated in any way. The account ID is only based on the client's name. 
    /// Only used on servers that allow joining without Steam/EOS authentication (such as when playing in a local network with no internet connection).
    /// </summary>
    public sealed class UnauthenticatedAccountId : AccountId
    {
        private const string Prefix = "UNAUTHENTICATED_";

        private readonly string clientName;

        public override string StringRepresentation => Prefix + clientName;

        public override string EosStringRepresentation => StringRepresentation;

        public UnauthenticatedAccountId(string clientName)
        {
            this.clientName = clientName;
        }

        public override bool Equals(object? obj)
            => obj is UnauthenticatedAccountId otherId
            && otherId.clientName.Equals(clientName);

        public override int GetHashCode()
        {
            return clientName.GetHashCode();
        }

        public new static Option<UnauthenticatedAccountId> Parse(string str)
        {
            if (str.IsNullOrWhiteSpace()) { return Option.None; }
            if (!str.StartsWith(Prefix, StringComparison.InvariantCultureIgnoreCase))
            {
                return Option.None;
            }
            return Option.Some(new UnauthenticatedAccountId(str[Prefix.Length..]));
        }
    }
}