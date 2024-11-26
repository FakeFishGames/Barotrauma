using System.Collections.Immutable;

namespace Barotrauma
{
    public static class GUIStyle
    {
        public readonly static ImmutableDictionary<Identifier, GUIFont> Fonts;
    }

    public class GUIFont
    {
        public readonly Identifier Identifier;
    }
}
