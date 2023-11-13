#nullable enable
using System;
using System.Globalization;

namespace Barotrauma
{
    sealed class SteamWorkshopId : ContentPackageId
    {
        public readonly UInt64 Value;

        public SteamWorkshopId(UInt64 value)
        {
            Value = value;
        }

        private const string Prefix = "STEAM_WORKSHOP_";
        
        public override string StringRepresentation => Value.ToString(CultureInfo.InvariantCulture);

        public override bool Equals(object? obj)
            => obj is SteamWorkshopId otherWorkshopId && otherWorkshopId.Value == Value;

        public override int GetHashCode() => Value.GetHashCode();

        public new static Option<SteamWorkshopId> Parse(string s)
        {
            if (s.StartsWith(Prefix)) { s = s[Prefix.Length..]; }
            if (!UInt64.TryParse(s, out var id) || id == 0) { return Option<SteamWorkshopId>.None(); }
            return Option<SteamWorkshopId>.Some(new SteamWorkshopId(id));
        }
    }
}