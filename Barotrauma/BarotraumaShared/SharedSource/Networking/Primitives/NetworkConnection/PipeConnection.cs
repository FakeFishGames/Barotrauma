#nullable enable
using System;

namespace Barotrauma.Networking
{
    sealed class PipeEndpoint : Endpoint
    {
        public override string StringRepresentation => "PIPE";
        
        public override LocalizedString ServerTypeString => throw new InvalidOperationException();

        public PipeEndpoint() : base(new PipeAddress()) { }

        public override bool Equals(object? obj)
            => obj is PipeEndpoint;

        public override int GetHashCode() => 1;

        public static bool operator ==(PipeEndpoint a, PipeEndpoint b)
            => true;

        public static bool operator !=(PipeEndpoint a, PipeEndpoint b)
            => !(a == b);
    }
    
    sealed class PipeConnection : NetworkConnection
    {
        public PipeConnection(AccountId accountId) : base(new PipeEndpoint())
        {
            SetAccountInfo(new AccountInfo(Option<AccountId>.Some(accountId)));
        }
    }
}

