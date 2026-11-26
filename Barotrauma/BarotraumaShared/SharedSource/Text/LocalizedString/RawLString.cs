#nullable enable
namespace Barotrauma
{
    public class RawLString : LocalizedString
    {
        public override bool UsingDefaultLanguageAsFallback => false;

        public RawLString(string value) 
        { 
            cachedValue = TextPack.PerformXmlSymbolReplacements(value); 
        }

        protected override bool MustRetrieveValue() => false;

        public override bool Loaded => true;
        public override void RetrieveValue() { }
    }
}