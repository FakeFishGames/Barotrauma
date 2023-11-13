namespace Barotrauma;

partial class CharacterHUD
{
    static partial void RecreateHudTextsIfControllingProjSpecific(Character character);

    static partial void RecreateHudTextsIfFocusedProjSpecific(params Item[] items);

    public static void RecreateHudTextsIfControlling(Character character) => RecreateHudTextsIfControllingProjSpecific(character);

    public static void RecreateHudTextsIfFocused(params Item[] items) => RecreateHudTextsIfFocusedProjSpecific(items);
}