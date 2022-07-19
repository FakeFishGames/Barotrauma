using System.Collections.Immutable;

namespace Barotrauma
{
    partial class UpgradePrefab
    {
        public readonly ImmutableArray<DecorativeSprite> DecorativeSprites = new ImmutableArray<DecorativeSprite>();
        public Sprite Sprite { get; private set; }
    }
}