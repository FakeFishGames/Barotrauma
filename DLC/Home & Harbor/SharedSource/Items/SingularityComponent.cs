using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

public partial class SingularityComponent : ItemComponent, IServerSerializable
{
    private PositionalDistortionComponent? distortionComponent;
    private float distortionComponentInitialRadius;

    /// <summary>
    /// Used for sampling noise values for movement. Kept in sync between the clients and the server to make the movement stay in sync.
    /// </summary>
    private float movementTimer;

    private float strength;

    const float MinStrength = 0.0f, MaxStrength = 100.0f;

    [Serialize(defaultValue: 50f, IsPropertySaveable.Yes, description: "Represent the size/mass of the singularity. A value between 0 and 100."), Editable]
    public float Strength
    {
        get => strength;
        set
        {
            strength = MathHelper.Clamp(value, MinStrength, MaxStrength);
        }
    }

    public float NormalizedStrength => strength / MaxStrength;

    [Serialize(defaultValue: 0.01f, IsPropertySaveable.Yes, description: "Scale of the item at minimum strength."), Editable]
    public float MinScale { get; set; }

    [Serialize(defaultValue: 3.0f, IsPropertySaveable.Yes, description: "Scale of the item at maximum strength."), Editable]
    public float MaxScale { get; set; }

    [Serialize(defaultValue: 1.0f, IsPropertySaveable.Yes, description: "The speed at which the singularity wanders around when at minimum strength."), Editable]
    public float MinWanderSpeed { get; set; }

    [Serialize(defaultValue: 100.0f, IsPropertySaveable.Yes, description: "The speed at which the singularity wanders around when at maximum strength."), Editable]
    public float MaxWanderSpeed { get; set; }

    [Serialize(defaultValue: 10.0f, IsPropertySaveable.Yes, description: "How fast the singularity changes the direction it's wandering towards when at minimum strength."), Editable]
    public float MinChangeDirectionSpeed { get; set; }

    [Serialize(defaultValue: 0.2f, IsPropertySaveable.Yes, description: "How fast the singularity changes the direction it's wandering towards when at maximum strength."), Editable]
    public float MaxChangeDirectionSpeed { get; set; }

    [Serialize(defaultValue: 10.0f, IsPropertySaveable.Yes, description: "How close to the boundaries of the hull the singularity must be for wall avoidance to kick in."), Editable]
    public float WallAvoidanceDistance { get; set; }

    [Serialize(defaultValue: 10.0f, IsPropertySaveable.Yes, description: "How much the singularity accelerates away from the hulls boundaries when at minimum strength."), Editable]
    public float MinWallAvoidanceStrength { get; set; }

    [Serialize(defaultValue: 0.2f, IsPropertySaveable.Yes, description: "How much the singularity accelerates away from the hulls boundaries when at maximum strength."), Editable]
    public float MaxWallAvoidanceStrength { get; set; }

    private bool isContained = false;
    public bool IsContained 
    {
        get { return isContained; }
        set
        {
            if (isContained == value) { return; }
            isContained = value;
            item.IsDangerous = !isContained;
            if (!isContained)
            {
                item.ApplyStatusEffects(ActionType.OnFailure, deltaTime: 1.0f);
            }
        }
    }

    [Serialize(defaultValue: 0.7f, IsPropertySaveable.Yes)]
    public float SpriteDepthWhenContained 
    { 
        get; 
        set;
    }

    [Serialize(defaultValue: 0.0f, IsPropertySaveable.Yes)]
    public float SpriteDepthWhenEscaped
    {
        get;
        set;
    }

    private Vector2 currentVelocity;

    public SingularityComponent(Item item, ContentXElement element) : base(item, element)
    {
        IsActive = true;
        InitProjSpecific(element);

        if (item.body == null)
        {
            DebugConsole.ThrowError($"Item {item.Prefab.Identifier} has a {nameof(SingularityComponent)} but no physics body.");
        }
    }

    public override void OnItemLoaded()
    {
        distortionComponent = item.GetComponent<PositionalDistortionComponent>();
        if (distortionComponent != null)
        {
            distortionComponentInitialRadius = distortionComponent.Radius;
        }
    }

    partial void InitProjSpecific(ContentXElement element);

    public override void Update(float deltaTime, Camera cam)
    {
        base.Update(deltaTime, cam);

        item.Scale = MathHelper.Lerp(MinScale, MaxScale, NormalizedStrength);
        if (!IsContained)
        {
            item.Sprite.Depth = SpriteDepthWhenEscaped;
            UpdateMovement(deltaTime);
            if (distortionComponent != null)
            {
                distortionComponent.IsActive = true;
                distortionComponent.Radius = distortionComponentInitialRadius * NormalizedStrength;
            }
        }
        else if (distortionComponent != null)
        {
            item.Sprite.Depth = SpriteDepthWhenContained;
            distortionComponent.IsActive = false;
        }
        UpdateProjSpecific(deltaTime);
    }

    private void UpdateMovement(float deltaTime)
    {
        float wanderSpeed = MathHelper.Lerp(MinWanderSpeed, MaxWanderSpeed, NormalizedStrength);
        float changeDirectionSpeed = MathHelper.Lerp(MinChangeDirectionSpeed, MaxChangeDirectionSpeed, NormalizedStrength)
            //a magic number to make the speed values make more sense - otherwise a value of 1 would be already quite fast
            * 0.1f;

        movementTimer += deltaTime;

        float t1 = movementTimer * changeDirectionSpeed;
        float t2 = movementTimer * changeDirectionSpeed * 0.25f;
        Vector2 velocityChangeDir = new Vector2(
            PerlinNoise.GetPerlin(t1, t2) - 0.5f,
            PerlinNoise.GetPerlin(t2, t1) - 0.5f);

        currentVelocity += velocityChangeDir * wanderSpeed;

        if (item.CurrentHull != null)
        {
            float wallAvoidanceStrength = MathHelper.Lerp(MinWallAvoidanceStrength, MaxWallAvoidanceStrength, NormalizedStrength);

            if (item.Position.X < item.CurrentHull.Rect.X + WallAvoidanceDistance)
            {
                currentVelocity += Vector2.UnitX * wallAvoidanceStrength;
            }
            else if (item.Position.X > item.CurrentHull.Rect.Right - WallAvoidanceDistance)
            {
                currentVelocity -= Vector2.UnitX * wallAvoidanceStrength;
            }
            if (item.Position.Y > item.CurrentHull.Rect.Y - WallAvoidanceDistance)
            {
                currentVelocity -= Vector2.UnitY * wallAvoidanceStrength;
            }
            else if (item.Position.Y < item.CurrentHull.Rect.Y - item.CurrentHull.Rect.Height + WallAvoidanceDistance)
            {
                currentVelocity += Vector2.UnitY * wallAvoidanceStrength;
            }
        }

        currentVelocity = Vector2.Clamp(currentVelocity, -Vector2.One * wanderSpeed, Vector2.One * wanderSpeed);
        item.body.LinearVelocity = ConvertUnits.ToSimUnits(currentVelocity);
    }

    partial void UpdateProjSpecific(float deltaTime);
}