using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;

namespace HomeAndHarbor;

public partial class InfestableVentComponent : ItemComponent, IDrawableComponent, IRepairToolDamageable, IServerSerializable
{
    [Serialize(defaultValue: false, IsPropertySaveable.Yes)]
    public bool IsInfested { get; private set; }

    [Serialize(defaultValue: 0f, IsPropertySaveable.Yes)]
    public float InfestationProgress { get; set; }

    [Serialize(defaultValue: 0.01f, IsPropertySaveable.Yes)]
    public float InfestationDamageMultiplier { get; set; }

    [Serialize(defaultValue: 60f, IsPropertySaveable.Yes, description: "How long it takes for the infestation to fully develop once the vent is infested.")]
    public float InfestationDelay { get; set; }

    [Serialize(defaultValue: 30f, IsPropertySaveable.No, description: "Delay in seconds between pest spawns once the the vent is fully infested.")]
    public float PestSpawnDelay { get; set; }

    [Serialize(defaultValue: 10, IsPropertySaveable.No, description: "Stops this vent from spawning new pests while it has this many alive pests.")]
    public int MaxPests { get; set; }

    [Serialize(defaultValue: 30, IsPropertySaveable.No, description: "Stops this vent from spawning new pests while the total number of alive pests spawned by all vents is at this limit.")]
    public int MaxPestsGlobal { get; set; }

    public bool CanBeInfested => !item.HasTag(Tags.DisallowInfesting) && !item.NonInteractable;

    private const float InfestationSendDelay = 5f;

    private PhysicsBody? collider;
    private float pestSpawnTimer;
    private Sprite? infestationSprite;

    private readonly ImmutableHashSet<CharacterPrefab> pestPrefabs = [];
    private readonly List<Character> spawnedPests = [];

    public InfestableVentComponent(Item item, ContentXElement element) : base(item, element)
    {
        foreach (ContentXElement subElement in element.Elements())
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "infestationsprite":
                    infestationSprite = new Sprite(subElement);
                    break;
#if CLIENT
                case "damageparticle":
                    damageParticles.Add(new Barotrauma.Particles.ParticleEmitter(subElement));
                    break;
                case "deathparticle":
                    deathParticles.Add(new Barotrauma.Particles.ParticleEmitter(subElement));
                    break;
#endif
            }
        }

        List<CharacterPrefab> validPrefabs = [];
        foreach (var characterPrefab in CharacterPrefab.Prefabs)
        {
            var tags = characterPrefab.ConfigElement.GetAttributeIdentifierImmutableHashSet("Tags", def: ImmutableHashSet<Identifier>.Empty);
            if (tags.Contains("outpostpest"))
            {
                validPrefabs.Add(characterPrefab);
            }
        }
        if (validPrefabs.None())
        {
            DebugConsole.AddWarning("Could not find any character prefabs with the tag \"outpostpest\".");
        }

        pestPrefabs = validPrefabs.ToImmutableHashSet();

        IsActive = false;
    }

    public override void OnMapLoaded()
    {
        base.OnMapLoaded();

        if (IsInfested)
        {
            IsActive = true;
        }
    }

    public void Infest()
    {
        if (!CanBeInfested) { return; }

#if CLIENT
        TryDisplayInfestationHint();
#endif

        IsInfested = true;
        IsActive = true;

#if SERVER
        Item.CreateServerEvent(this);
#endif
    }

    public void RemoveInfestation()
    {
        IsInfested = false;
        InfestationProgress = 0f;
        IsActive = false;

        collider?.Remove();
        collider = null;

#if SERVER
        Item.CreateServerEvent(this);
#elif CLIENT
        foreach (var particleEmitter in deathParticles)
        {
            particleEmitter.Emit(1f, Item.WorldPosition, Item.CurrentHull);
        }
#endif
    }

    public override void Update(float deltaTime, Camera cam)
    {
        if (!IsInfested) { return; }

        if (Item.CurrentHull is { } hull)
        {
            // Make bots avoid staying in hulls with an infestation
            hull.TemporaryAvoidStayingTime = 1f;
        }

#if CLIENT
        damageShakeOffset = Vector2.Max(Vector2.Zero, damageShakeOffset - Vector2.One * deltaTime);
#endif

        if (InfestationProgress < 0f)
        {
            RemoveInfestation();
            return;
        }

        InfestationProgress = MathF.Min(InfestationProgress + deltaTime / InfestationDelay, 1f);

#if SERVER
        sentTimeInfestationProgress -= deltaTime;

        if (!MathUtils.NearlyEqual(InfestationProgress, prevSentInfestationProgress) &&
            sentTimeInfestationProgress <= 0f)
        {
            Item.CreateServerEvent(this);

            sentTimeInfestationProgress = InfestationSendDelay;
            prevSentInfestationProgress = InfestationProgress;
        }
#endif

        if (collider == null)
        {
            // Collider that can be hit by repair tools
            collider = new PhysicsBody(ConvertUnits.ToSimUnits(item.Rect.Width), ConvertUnits.ToSimUnits(item.Rect.Height), 0f, 1f, BodyType.Kinematic, Physics.CollisionWall, Physics.CollisionNone)
            {
                Enabled = true,
                UserData = item
            };

            collider.SetTransform(Item.SimPosition, Item.Rotation);
        }

        spawnedPests.RemoveAll(c => c.Removed || c.IsDead);

        if (GameMain.NetworkMember is not { IsClient: true } &&
            InfestationProgress > 0.95f && 
            spawnedPests.Count < MaxPests && 
            Character.CharacterList.Count(c => pestPrefabs.Contains(c.Prefab)) < MaxPestsGlobal)
        {
            pestSpawnTimer -= deltaTime;

            if (pestSpawnTimer <= 0f)
            {
                var chosenPest = pestPrefabs.GetRandomUnsynced();
                if (chosenPest != null)
                {
                    Entity.Spawner.AddCharacterToSpawnQueue(chosenPest.Identifier, Item.WorldPosition, (Character spawnedCharacter) =>
                    {
                        //do not save, that'd lead to pests accumulating each round
                        spawnedCharacter.DisableCreatureSaving = true;

                        spawnedPests.Add(spawnedCharacter);
                    });
                }

                pestSpawnTimer = PestSpawnDelay;
            }
        }

        base.Update(deltaTime, cam);
    }

    public void OnDamageRepairTool(RepairTool repairTool, Character user, Vector2 hitPosition, float damage, float deltaTime, float degreeOfSuccess, Body targetBody)
    {
        if (!IsInfested) { return; }

        InfestationProgress -= repairTool.FireDamage * deltaTime * InfestationDamageMultiplier;

#if CLIENT
        damageShakeOffset = new Vector2(Rand.Range(-2f, 2f), Rand.Range(-2f, 2f));
        user?.UpdateHUDProgressBar(this, Item.WorldPosition, InfestationProgress, GUIStyle.Red, GUIStyle.Green, "infestedvent");

        foreach (var particleEmitter in damageParticles)
        {
            particleEmitter.Emit(deltaTime, Item.WorldPosition, Item.CurrentHull);
        }
#endif
    }

    protected override void RemoveComponentSpecific()
    {
        base.RemoveComponentSpecific();

        collider?.Remove();
        collider = null;
    }
}