#nullable enable

using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components;

/// <summary>
/// Creates a collider that can selectively block items or characters based on the configuration.
/// The collider is automatically enabled when the Holdable component is attached.
/// It can also be manually controlled using <see cref="ColliderEnabled"/>.
/// </summary>
public class SelectiveColliderComponent : ItemComponent
{
    [Serialize("", IsPropertySaveable.No, "Allows the specified species to pass through this collider.")]
    public Identifier[] SpeciesAllowedToPass { get; private set; } = [];

    [Serialize("", IsPropertySaveable.No, "Allows items with the specified tags to pass through this collider.")]
    public Identifier[] ItemTagsAllowedToPass { get; private set; } = [];

    [Serialize(false, IsPropertySaveable.No, "Allows characters to pass through the collider if they are selected by another character (e.g. grabbing a pet)")]
    public bool AllowPassingIfSelectedByAnotherCharacter { get; private set; }

    [Serialize(false, IsPropertySaveable.No, "Disables the selective collider if the item is broken.")]
    public bool DisableColliderIfBroken { get; private set; }

    [Serialize(true, IsPropertySaveable.No, "Disables the selective collider if the item is not attached to a wall through a holdable component.")]
    public bool DisableColliderIfNotAttached { get; private set; }

    [Serialize(false, IsPropertySaveable.No, "If set to true, blocks the closest waypoint to this item.")]
    public bool BlockClosestWayPoint { get; private set; }

    [Serialize(300f, IsPropertySaveable.No, $"The maximum squared distance of a waypoint to be considered close enough for {nameof(BlockClosestWayPoint)}.")]
    public float ClosestWayPointDistance { get; private set; }


    private bool colliderEnabled = true;
    /// <summary>
    /// Can be used by status effects to enable/disable the collider. 
    /// </summary>
    /// <remarks>
    /// If this item has a Holdable component, the collider must be attached to a wall to be enabled.
    /// </remarks>
    public bool ColliderEnabled
    {
        get
        {
            if (DisableColliderIfBroken && item.Condition <= 0f)
            {
                return false;
            }

            if (DisableColliderIfNotAttached && item.GetComponent<Holdable>()?.IsAttached != true)
            {
                return false;
            }

            return colliderEnabled;
        }
        set
        {
            colliderEnabled = value;
        }
    }

    private PhysicsBody? collider;

    private Vector2 lastSimPosition;
    private float lastRotation;

    private WayPoint? currentWayPoint;
    private Hull? currentHull;

    public SelectiveColliderComponent(Item item, ContentXElement element) : base(item, element)
    {
        IsActive = true;

        foreach (XElement subElement in element.Elements())
        {
            if (subElement.Name.LocalName.ToLowerInvariant() == "body")
            {
                collider = new PhysicsBody(subElement, item.SimPosition, 1f, null, Physics.CollisionWall, Physics.CollisionCharacter)
                {
                    BodyType = BodyType.Kinematic,
                    Enabled = false,
                    UserData = item
                };

                collider.FarseerBody.OnCollision += OnCollision;
            }
        }

        if (collider == null)
        {
            DebugConsole.ThrowError($"body sub element not defined for {nameof(SelectiveColliderComponent)}");
        }
    }

    public bool ShouldBlockCharacter(Character character)
    {
        if (AllowPassingIfSelectedByAnotherCharacter && character.SelectedBy != null)
        {
            return false;
        }

        if (SpeciesAllowedToPass.Any(species => species == character.SpeciesName))
        {
            return false;
        }

        return true;
    }

    public bool ShouldBlockItem(Item item)
    {
        if (ItemTagsAllowedToPass.Any(item.HasTag))
        {
            return false;
        }

        return true;
    }

    private bool OnCollision(Fixture sender, Fixture other, Contact contact)
    {
        if (other.Body.UserData is Limb limb)
        {
            if (!ShouldBlockCharacter(limb.character)) { return false; }
        }
        else if (other.Body.UserData is Character character)
        {
            if (!ShouldBlockCharacter(character)) { return false; }
        }
        else if (other.Body.UserData is Item item)
        {
            if (!ShouldBlockItem(item)) { return false; }
        }

        return true;
    }

    public override void Update(float deltaTime, Camera cam)
    {
        if (collider == null) { return; }

        if (!lastSimPosition.NearlyEquals(item.SimPosition) || !MathUtils.NearlyEqual(lastRotation, item.Rotation, 0.1f))
        {
            collider.SetTransformIgnoreContacts(item.SimPosition, item.Rotation);

            lastSimPosition = item.SimPosition;
            lastRotation = item.Rotation;
        }

        Holdable holdable = item.GetComponent<Holdable>();

        bool prevState = collider.Enabled;

        collider.Enabled = ColliderEnabled;

        if (prevState != collider.Enabled && BlockClosestWayPoint)
        {
            if (currentWayPoint != null) 
            {
                currentWayPoint.BlockingItems.Remove(item);
            }

            if (collider.Enabled)
            {
                WayPoint? wayPoint = item.Submarine.GetWaypoints(alsoFromConnectedSubs: false)
                    .MinBy(waypoint => Vector2.DistanceSquared(Item.WorldPosition, waypoint.WorldPosition));
            
                if (wayPoint != null && Vector2.DistanceSquared(Item.WorldPosition, wayPoint.WorldPosition) < ClosestWayPointDistance * ClosestWayPointDistance)
                {
                    wayPoint.BlockingItems.Add(Item);
                    currentWayPoint = wayPoint;
                }
                else
                {
                    DebugConsole.AddWarning($"Failed to find nearby waypoint to link {Item.Name} to");
                }
            }
        }
    }

    public override void UpdateBroken(float deltaTime, Camera cam)
    {
        Update(deltaTime, cam);
    }

    protected override void RemoveComponentSpecific()
    {
        if (collider != null)
        {
            collider.Remove();
            collider = null;
        }
    }
}