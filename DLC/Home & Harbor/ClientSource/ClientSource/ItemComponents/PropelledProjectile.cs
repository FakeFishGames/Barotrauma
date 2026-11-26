using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor;

internal partial class PropelledProjectile : Projectile, IDrawableComponent
{
    #region Drawing
    public Vector2 DrawSize => Vector2.Zero;

    public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1f, Color? overrideColor = null)
    {
        if (!GameMain.DebugDraw) { return; }
        Vector2 itemDrawPos = item.DrawPosition.FlipY();

        bool drawTracking = trackingSpeedRad > 0f && lastTrackedPoint.HasValue;
        bool drawOscillation = Oscillation > 0f && oscillationFrequency > 0f;
        bool drawAcceleration = acceleration > 0f;

        if (drawTracking || drawOscillation || drawAcceleration)
        {
            GUI.DrawLine(spriteBatch, itemDrawPos, itemDrawPos + Physics.SimPosToDrawPos(MathUtils.RotatedUnitXRadians(item.body.Rotation) * maxSpeed), Color.Yellow, width: 3f);
            GUI.DrawLine(spriteBatch, itemDrawPos, itemDrawPos + Physics.SimPosToDrawPos(item.body.LinearVelocity), Color.Red, width: 3f);
        }

        if (drawTracking) { GUI.DrawLine(spriteBatch, itemDrawPos, lastTrackedPoint!.Value.FlipY(), Color.Cyan, width: 3f); }
    }
    #endregion

    #region Networking
    public override void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        base.ClientEventRead(msg, sendingTime);

        EventType type = (EventType)msg.ReadRangedInteger(0, Enum.GetValues<EventType>().Length - 1);
        switch (type)
        {
            case EventType.InitTracking:
                turret = (Entity.FindEntityByID(msg.ReadUInt16()) as Item)?.GetComponent<Turret>();
                break;
            case EventType.InitOscillation:
                oscillationTimer = msg.ReadSingle();
                break;
        }
    }
    #endregion
}
