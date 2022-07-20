#nullable enable
namespace Barotrauma
{
    public class CapitalizeLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;

        public CapitalizeLString(LocalizedString nStr) { nestedStr = nStr; }
        
        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            string str = nestedStr.Value;
            if (!string.IsNullOrEmpty(str))
            {
                cachedValue = char.ToUpper(str[0]) + str[1..];
            }
            else
            {
                cachedValue = "";
            }
            UpdateLanguage();
        }
    }
}