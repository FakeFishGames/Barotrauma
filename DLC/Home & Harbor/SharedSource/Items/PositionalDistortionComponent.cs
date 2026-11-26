using Barotrauma;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor;

/// <summary>
/// Creates a "positional distortion" (a circular bubble-like distortion) screen effect at the position of the item when active.
/// </summary>
public partial class PositionalDistortionComponent : ItemComponent, IDrawableComponent
{
    private float distortAmount;

    const float MinStrength = -100.0f, MaxStrength = 100.0f;

    [Serialize(defaultValue: 50f, IsPropertySaveable.Yes), Editable]
    public float DistortAmount
    {
        get => distortAmount;
        set
        {
            distortAmount = MathHelper.Clamp(value, MinStrength, MaxStrength);
        }
    }

    [Serialize(defaultValue: 1.0f, IsPropertySaveable.Yes), Editable]
    public float Radius { get; set; }

    public Vector2 DrawSize => 
        //an arbitrary magic number found by testing, as the actual extents of the distortion effect aren't that well defined 
        Vector2.One * Radius * 10;

    public PositionalDistortionComponent(Item item, ContentXElement element) : base(item, element)
    {
    }

#if CLIENT
    public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
    {
        if (!IsActive) { return; }
        if (Screen.Selected is GameScreen gameScreen)
        {
            gameScreen.AddPositionalDistortion(
                gameScreen.Cam.WorldToScreen(item.WorldPosition) / new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                DistortAmount,
                Radius);
        }
    }
#endif
}