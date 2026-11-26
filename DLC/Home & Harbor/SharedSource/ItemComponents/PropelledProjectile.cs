using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

internal partial class PropelledProjectile(Item item, ContentXElement element) : Projectile(item, element)
{
    protected override void DoLaunch(Vector2 impulse)
    {
        base.DoLaunch(impulse);

        InitTracking();
        InitOscillation();
    }

    public override void Update(float deltaTime, Camera cam)
    {
        base.Update(deltaTime, cam);

        if (Hitscan || !IsActive || IsStuckToTarget) { return; }

        if (DeactivationTime > 0)
        {
            if (deactivationTimer < 0)
            {
                return;
            }
        }

        UpdateTracking(deltaTime);
        UpdateOscillation(deltaTime);
        UpdateAcceleration(deltaTime);
    }

    #region Tracking

    private float trackingSpeedRad;
    [Serialize(0f, IsPropertySaveable.No, "How fast the missile can rotate to follow its target, in degrees/sec.")]
    public float TrackingSpeed
    {
        get => MathHelper.ToDegrees(trackingSpeedRad);
        set => trackingSpeedRad = Math.Max(MathHelper.ToRadians(value), 0f);
    }

    private Turret? turret;
    /// <summary>
    /// The point the projectile is currently tracking, in world coordinates.
    /// </summary>
    private Vector2? lastTrackedPoint;
    private readonly List<Fixture> ignoredFixtures = [];

    private void InitTracking()
    {
        turret = null;
        lastTrackedPoint = null;
        ignoredFixtures.Clear();

        if (Launcher != null)
        {
            turret = Launcher.GetComponent<Turret>();

            ignoredFixtures.AddRange(Launcher.StaticFixtures);
            if (Launcher.body != null) { ignoredFixtures.AddRange(Launcher.body.FarseerBody.FixtureList); }
        }

#if SERVER
        item.CreateServerEvent(this, new PropelledProjectileEventData(EventType.InitTracking));
#endif
    }

    private void UpdateTracking(float deltaTime)
    {
        if (trackingSpeedRad <= 0f) { return; }

        if (turret != null && (turret.User != null || turret.AutoOperate))
        {
            if (turret.Item.GetComponent<LineDrawer>() is LineDrawer lineDrawer)
            {
                lastTrackedPoint = lineDrawer.CurrentTargetPosition;
#if CLIENT
                lineDrawer.RegisterBeingUsedForTargeting();
#endif
            }
        }

        if (!lastTrackedPoint.HasValue) { return; }
        float targetRotation = MathUtils.VectorToAngle(lastTrackedPoint.Value - item.WorldPosition);
        float newRotationRad = MathUtils.CurveAngle(item.body.Rotation, targetRotation, Math.Min(trackingSpeedRad * deltaTime, 1.0f));
        item.body.SmoothRotate(newRotationRad);
    }
#endregion Tracking

    #region Oscillation

    [Serialize(0f, IsPropertySaveable.No, "How much the missile's rotation oscillates. Does not correspond to any real unit (the actual \"waviness\" of the trajectory is affected by the oscillation frequency and the acceleration) - experiment with different values to see what feels good!")]
    public float Oscillation
    {
        get;
        set;
    }

    private float oscillationFrequency;
    [Serialize(1f, IsPropertySaveable.No, "How fast the missile's rotation oscillates, in Hertz.")]
    public float OscillationFrequency
    {
        get => oscillationFrequency;
        set => oscillationFrequency = Math.Max(value, 0f);
    }

    private float oscillationTimer;

    private void InitOscillation()
    {
        //initialize to a random value so not every missile oscillates in sync
        //clients should not do this, they set the correct value in ClientEventRead!
        if (GameMain.NetworkMember is not { IsClient: true})
        {
            oscillationTimer = Rand.Range(0.0f, 100.0f);
        }
#if SERVER
        item.CreateServerEvent(this, new PropelledProjectileEventData(EventType.InitOscillation));
#endif
    }

    private void UpdateOscillation(float deltaTime)
    {
        if (Oscillation <= 0f || oscillationFrequency <= 0f) { return; }

        oscillationTimer += deltaTime;

        float phase = oscillationTimer * MathHelper.TwoPi * oscillationFrequency;
        float currOscillation = MathF.Sin(phase) * Oscillation * 0.005f;

        item.body.SmoothRotate(item.body.Rotation + currOscillation);
    }
    #endregion

    #region Acceleration
    private float acceleration;
    [Serialize(0f, IsPropertySaveable.No, "How fast the missile accelerates, in meters/sec².")]
    public float Acceleration
    {
        get => acceleration;
        set => acceleration = Math.Clamp(value, 0f, NetConfig.MaxPhysicsBodyVelocity * Timing.FixedUpdateRate);
    }

    private float maxSpeed;
    [Serialize(NetConfig.MaxPhysicsBodyVelocity, IsPropertySaveable.No, "The maximum speed of the missile, in meters/sec.")]
    public float MaxSpeed
    {
        get => maxSpeed;
        set => maxSpeed = Math.Clamp(value, 0f, NetConfig.MaxPhysicsBodyVelocity);
    }

    private void UpdateAcceleration(float deltaTime)
    {
        if (acceleration <= 0f) { return; }

        Vector2 dir = MathUtils.RotatedUnitXRadians(item.body.Rotation);
        item.body.LinearVelocity = dir * item.body.LinearVelocity.Length().MoveTowards(maxSpeed, acceleration * deltaTime);
    }
    #endregion

    #region Networking
    private enum EventType { InitTracking, InitOscillation }
    #endregion
}