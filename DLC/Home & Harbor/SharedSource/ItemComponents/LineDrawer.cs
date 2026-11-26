using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;

namespace HomeAndHarbor;

/// <summary>
/// Draws a line from the item's origin to the set distance, based on the rotation of the item. The line can optionally be blocked by walls and/or characters.<br/>
/// If the LineDrawer is a child of a Turret component: The origin will be the barrel firing point, the rotation will be the barrel's rotation, and the line will only be drawn if the turret is being operated.
/// </summary>
internal partial class LineDrawer : Powered, IClientSerializable, IServerSerializable
{
    #region Collision
    private const Category CharacterCategory = Physics.CollisionCharacter | Physics.CollisionLagCompensationBody;
    private const Category WallCategory = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking;

    private Category collisionCategory;
    private ImmutableList<Fixture>? ignoredFixtures;

    [Editable, Serialize(false, IsPropertySaveable.Yes)]
    public bool BlockedByCharacters
    {
        get => collisionCategory.HasFlag(CharacterCategory);
        set => collisionCategory = value
            ? collisionCategory | CharacterCategory
            : collisionCategory & ~CharacterCategory;
    }

    [Editable, Serialize(true, IsPropertySaveable.Yes)]
    public bool BlockedByWalls
    {
        get => collisionCategory.HasFlag(WallCategory);
        set => collisionCategory = value
            ? collisionCategory | WallCategory
            : collisionCategory & ~WallCategory;
    }
    #endregion

    [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Is the line being drawn.", alwaysUseInstanceValues: true)]
    public bool IsOn
    {
        get => IsActive && Parent is not Turret { User: null, AutoOperate: false };
        set => IsActive = value;
    }

    const float MaxRange = 50000.0f;

    private float range;
    [Serialize(100f, IsPropertySaveable.No, "The maximum range of the line.")]
    public float Range
    {
        get => range;
        set
        {
            range = currentRange = Math.Clamp(value, 0f, MaxRange);
#if CLIENT
            drawSize = new Vector2(range);
#endif
        }
    }

    [Serialize(false, IsPropertySaveable.No, description: "If set, the line will stop at the position of the cursor of the character operating the turret.")]
    public bool StopAtCursor
    {
        get;
        set;
    }

    [Serialize(false, IsPropertySaveable.No, description: "If set to true, the line will disappear when there's currently no-one operating the turret.")]
    public bool HideWhenNotUsed
    {
        get;
        set;
    }

    private float width;
    [Serialize(3f, IsPropertySaveable.No, "The thickness of the line.")]
    public float Width
    {
        get => width;
        set => width = Math.Clamp(value, 0f, 100);
    }

    [Serialize("0,0", IsPropertySaveable.No, "The offset of the line's origin.")]
    public Vector2 Offset { get; set; }

    private float rotationRad;
    [Serialize(0f, IsPropertySaveable.No, "Additional rotation of the laser, in degrees.")]
    public float Rotation
    {
        get => MathHelper.ToDegrees(rotationRad);
        set => rotationRad = MathHelper.ToRadians(value);
    }

    /// <summary>
    /// The position the line ends at, in world coordinates.
    /// </summary>
    public Vector2 CurrentTargetPosition;

    private float alphaMultiplier;

    private float currentRange;

    private Turret? turretComponent;

    private float lastSentRange;
    private double lastSentRangeTime;

    /// <summary>
    /// Used by the clients and the server to determine how much the range must've changed for the value to be synced
    /// </summary>
    const float SendRangeThreshold = 5.0f;
    /// <summary>
    /// Used by the clients and the server to determine how often updates about the range can be sent
    /// </summary>
    const float SendRangeInterval = 0.5f;

    [Editable(FallBackTextTag = "lightcomponent.lightcolor"), Serialize("255,255,255,255", IsPropertySaveable.Yes, "The color of the line (R,G,B,A).", alwaysUseInstanceValues: true)]
    public Color Color { get; set; }

    [Serialize("", IsPropertySaveable.No, description: "If set, the sprite of this particle prefab is used to render the line.")]
    public Identifier LineParticle { get; set; }

    public override bool ShouldCauseDeterioration => false;

    #region Initialization
    public LineDrawer(Item item, ContentXElement element) : base(item, element) => InitProjSpecific(item, element);
    partial void InitProjSpecific(Item item, ContentXElement element);

    public override void OnItemLoaded()
    {
        base.OnItemLoaded();
        turretComponent = item.GetComponent<Turret>();
    }

    public override void OnMapLoaded()
    {
        base.OnMapLoaded();

        List<Fixture> itemFixtures = [.. item.StaticFixtures];
        if (item.body != null) { itemFixtures.AddRange(item.body.FarseerBody.FixtureList); }
        ignoredFixtures = [.. itemFixtures];
    }
    #endregion

    public override void Update(float deltaTime, Camera cam)
    {
        base.Update(deltaTime, cam);
        alphaMultiplier = PowerConsumption > 0f ? Math.Min(Voltage, 1f) : 1f;
        UpdateProjSpecific(deltaTime, cam);

        if (turretComponent?.User is Character user &&
            turretComponent.IsBeingActivelyUsed)
        {
            float rotation = item.RotationRadWithFlipping;
            Vector2 firingPos = item.WorldPosition;
            if (Parent is Turret turret)
            {
                rotation = turret.BarrelRotation;
                firingPos = turret.GetBarrelFiringPosition();
            }

            if (StopAtCursor)
            {
                //we can always use the cursor position directly if operated by the controlled character,
                //or if we're in single player (bots set the cursor position to the target)
                if (user == Character.Controlled || GameMain.NetworkMember == null ||
                    (GameMain.NetworkMember is { IsServer: true } && user is { IsBot: true }))
                {
                    currentRange = Math.Min(Vector2.Distance(user.CursorWorldPosition, firingPos), range);
                }
            }
            else
            {
                currentRange = range;
            }

            rotation += rotationRad;

            if (Physics.Raycast(ConvertUnits.ToSimUnits(firingPos), -rotation, ConvertUnits.ToSimUnits(currentRange), out RaycastHit? hit, collisionCategory, ignoredFixtures: ignoredFixtures))
            {
                if (hit.Value.Fixture.Body.UserData is Submarine sub)
                {
                    //hit a sub -> do a raycast in the sub's coordinate space too
                    if (Physics.Raycast(ConvertUnits.ToSimUnits(firingPos) - sub.SimPosition, -rotation, ConvertUnits.ToSimUnits(currentRange), out RaycastHit? inSubHit, collisionCategory, ignoredFixtures: ignoredFixtures))
                    {
                        //hit something inside the sub
                        currentRange = ConvertUnits.ToDisplayUnits(inSubHit.Value.Distance);
                    }
                    else
                    {
                        //didn't hit anything, the ray must've gone through the sub
                        //don't modify the range, just allow the ray to travel all the way to the end
                    }
                }
                else
                {
                    currentRange = ConvertUnits.ToDisplayUnits(hit.Value.Distance);
                }
            }
            Vector2 dir = MathUtils.RotatedUnitXRadians(rotation).FlipY();
            CurrentTargetPosition = firingPos + dir * currentRange;
        }
    }

    partial void UpdateProjSpecific(float deltaTime, Camera cam);

}
