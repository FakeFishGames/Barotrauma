using System;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public sealed class EosConnectContinuanceToken
    {
        // Got this number by checking a decoded continuance token, may be subject to change
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

        public readonly AccountId ExternalAccountId;
        public readonly DateTime ExpiryTime;
        public bool IsValid => value != IntPtr.Zero && DateTime.Now < ExpiryTime;

        private IntPtr value;

        public EosConnectContinuanceToken(IntPtr value, AccountId externalAccountId, DateTime expiryTime)
        {
            this.value = value;
            this.ExternalAccountId = externalAccountId;
            ExpiryTime = expiryTime;
        }

        public IntPtr Spend()
        {
            var retVal = IsValid ? value : IntPtr.Zero;
            value = IntPtr.Zero;
            return retVal;
        }

        public override string ToString()
            => $"{(IsValid ? "Valid" : "Invalid")} {ExternalAccountId} ContinuanceToken"
               + (IsValid ? $" (expires on {ExpiryTime})" : "");
    }
}