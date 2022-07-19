#nullable enable
namespace Barotrauma
{
    public class RawLString : LocalizedString
    {
        public RawLString(string value) { cachedValue = value; }

        protected override bool MustRetrieveValue() => false;

        public override bool Loaded => true;
        public override void RetrieveValue() { }
    }
}