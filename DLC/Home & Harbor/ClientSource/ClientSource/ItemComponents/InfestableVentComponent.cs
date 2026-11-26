using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor;

public partial class InfestableVentComponent
{
    public Vector2 DrawSize => item.Rect.Size.ToVector2();

    private readonly List<ParticleEmitter> damageParticles = [];
    private readonly List<ParticleEmitter> deathParticles = [];

    private Vector2 damageShakeOffset;

    /// <summary>
    /// How long (in seconds) after a vent becomes infested the hint is shown.
    /// </summary>
    private const float InfestationHintDelay = 10.0f;

    public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
    {
        if (!IsInfested) { return; }
        if (infestationSprite == null) { return; }

        infestationSprite.Draw(spriteBatch,
            pos: Item.DrawPosition.FlipY() + damageShakeOffset, 
            color: Color.White * 1f, 
            rotate: 0f, 
            scale: (InfestationProgress + 0.3f) * 0.4f, 
            depth: itemDepth - 0.01f);
    }

    /// <summary>
    /// Shows a one-time hint when a vent in the player-owned outpost becomes infested.
    /// </summary>
    private void TryDisplayInfestationHint()
    {
        if (Level.Loaded?.StartOutpost is not { } outpost || Item.Submarine != outpost) { return; }
        if (GameMain.GameSession?.StartLocation is not { } location) { return; }
        if (PersistentOutpostData.GetPersistentOutpostData(location) is not { IsPlayerOwned: true }) { return; }

        //delay the hint a bit, so it doesn't pop up while the round is still starting
        //(when the infestation is rolled) or on top of other hints/messages
        CoroutineManager.Invoke(() =>
        {
            if (Item.Removed || !IsInfested) { return; }
            HintManager.OnVentInfested();
        }, delay: InfestationHintDelay);
    }

    public void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        bool newIsInfested = msg.ReadBoolean();
        InfestationProgress = msg.ReadSingle();

        if (!IsInfested && newIsInfested)
        {
            Infest();
        }
        else if (IsInfested && !newIsInfested)
        {
            RemoveInfestation();
        }
    }
}
