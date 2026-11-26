using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Globalization;

namespace HomeAndHarbor;

partial class SingularityReactor : ReactorBase
{
    //the rate at which the reactor is being run on (higher rate -> higher temperature)
    private float feedingRate;
    
    private float containmentRate;
    
    private float? signalControlledTargetFeedingRate, signalControlledTargetContainmentRate;
    private double lastReceivedFeedingRateSignalTime, lastReceivedContainmentRateSignalTime;

    private SingularityComponent? singularity;
    private SingularityComponent? Singularity
    {
        get => singularity;
        set
        {
            if (singularity == value) { return; }
            //remove previous singularity from linked
            if (singularity != null) { item.linkedTo.Remove(singularity.Item); }

            singularity = value;
            if (singularity != null)
            {
                item.linkedTo.Add(singularity.Item);
            }
            RegisterUnsentChanges();
        }
    }

    private float createSingularityDelay;

    private float containmentFieldUpdateTimer;

    const float MinContainmentFieldAmplitude = 0.0f;
    const float MaxContainmentFieldAmplitude = 1.0f;
    /// <summary>
    /// The amplitude the player tries to match on the containment field graph.
    /// </summary>
    private float containmentFieldTargetAmplitude = MinContainmentFieldAmplitude;
    /// <summary>
    /// Current amplitude set by the player on the containment field graph.
    /// </summary>
    private float containmentFieldAmplitude = (MinContainmentFieldAmplitude + MaxContainmentFieldAmplitude) / 2;

    const float MinContainmentFieldOffset = -1.0f;
    const float MaxContainmentFieldOffset = 1.0f;
    /// <summary>
    /// The offset the player tries to match on the containment field graph.
    /// </summary>
    private float containmentFieldTargetOffset = (MinContainmentFieldOffset + MaxContainmentFieldOffset) / 2;
    /// <summary>
    /// Current offset set by the player on the containment field graph.
    /// </summary>
    private float containmentFieldOffset = (MinContainmentFieldOffset + MaxContainmentFieldOffset) / 2;

    private float containmentFieldMatchError;

    private LightComponent? feedingRateLight, containmentRateLight;
    private Color feedingRateLightColor, containmentRateLightColor;

    private float currentInstability;

    private float escapeContainmentTimer;

    /// <summary>
    /// How high the containment rate can be set. Limited by the amount and condition of the stabilizers.
    /// </summary>
    private float maxContainmentRate = 100.0f;

    [Editable, Serialize(20.0f, IsPropertySaveable.Yes, description: $"How many seconds it takes for the singularity to escape when the instability is above {nameof(SingularityEscapeMinInstability)}")]

    public float EscapeContainmentDelay { get; private set; }

    [Editable, Serialize(80.0f, IsPropertySaveable.Yes, description: "If the instability is above this value, the timer for the singularity escaping starts ticking up.")]
    public float SingularityEscapeMinInstability { get; private set; }

    [Editable, Serialize(80.0f, IsPropertySaveable.Yes, description: "If the strength of the singularity is above this value, the reactor starts taking damage (at a rate of strength / 100 per second).")]

    public float SingularityStrengthDamageThreshold { get; private set; }

    [Editable, Serialize(1.0f, IsPropertySaveable.Yes, description: $"The amount of damage the stabilizers take per second when not submerged at the lowest containment rate.")]

    public float MinStabilizerDamage { get; private set; }

    [Editable, Serialize(10.0f, IsPropertySaveable.Yes, description: $"The amount of damage the stabilizers take per second when not submerged at the highest containment rate.")]

    public float MaxStabilizerDamage { get; private set; }

    public bool InCriticalCondition => item.ConditionPercentage < criticalConditionThreshold;
    private float criticalConditionThreshold = 20.0f;

    private readonly List<Pump> stabilizers = [];

    public bool SingularityUnstable => currentInstability > SingularityEscapeMinInstability / 100.0f;
                    
    [Serialize(0.0f, IsPropertySaveable.Yes, description: "Current feeding rate of the reactor (0% - 100%). Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
    public float FeedingRate
    {
        get { return feedingRate; }
        set 
        {
            if (!MathUtils.IsValid(value)) return;
            feedingRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
        }
    }

    private float NormalizedFeedingRate => FeedingRate / 100.0f;

    [Serialize(20.0f, IsPropertySaveable.Yes, description: "")]
    public float ContainmentRate
    {
        get { return containmentRate; }
        set
        {
            if (!MathUtils.IsValid(value)) { return; }
            containmentRate = MathHelper.Clamp(value, 0.0f, 100.0f);
        }
    }

    private float NormalizedContainmentRate => ContainmentRate / 100.0f;

    private float targetFeedingRate;

    [Serialize(0.0f, IsPropertySaveable.Yes)]
    public float TargetFeedingRate
    {
        get { return targetFeedingRate; }
        set
        {
            if (!MathUtils.IsValid(value)) { return; }
            targetFeedingRate = MathHelper.Clamp(value, 0.0f, 100.0f);
        }
    }

    private float targetContainmentRate;

    [Serialize(0.0f, IsPropertySaveable.Yes)]
    public float TargetContainmentRate
    {
        get { return targetContainmentRate; }
        set
        {
            if (!MathUtils.IsValid(value)) { return; }
            targetContainmentRate = MathHelper.Clamp(value, 0.0f, 100.0f);
        }
    }

    [Editable, Serialize(true, IsPropertySaveable.Yes)]
    public bool ExplosionDamagesOtherSubs
    {
        get;
        set;
    }

    [Editable, Serialize("0,0", IsPropertySaveable.Yes, description: "Offset from the center of the reactor to the position of the singularity.")]
    public Point SingularityOffset
    {
        get;
        set;
    }

    [Serialize("singularity", IsPropertySaveable.Yes, description: "Identifier of the item that spawns inside the reactor.")]
    public Identifier SingularityPrefab
    {
        get;
        set;
    }

    public bool IsSingularityEscaping =>
        Singularity != null &&
        currentInstability > SingularityEscapeMinInstability / 100.0f;

    protected override float PowerGenerationRate { get => FeedingRate; set => FeedingRate = value; }
    protected override float TargetPowerGenerationRate { get => TargetFeedingRate; set => TargetFeedingRate = value; }

    protected override float OutputControlRate { get => 100.0f - ContainmentRate; set => ContainmentRate = 100.0f - value; }
    protected override float TargetOutputControlRate { get => 100.0f - TargetContainmentRate; set => TargetContainmentRate = 100.0f - value; }

    public float SingularityStrength => Singularity?.Strength ?? 0.0f;

    /// <summary>
    /// Maximum output rate = containment field at 0
    /// </summary>
    protected override float OutputRateWhenPoweredDown => 100.0f;

    public SingularityReactor(Item item, ContentXElement element)
        : base(item, element)
    {
        InitProjSpecific(element);
    }

    partial void InitProjSpecific(ContentXElement element);

    public override void OnItemLoaded()
    {
        base.OnItemLoaded();

        var childLightComponents = item.GetComponents<LightComponent>().Where(lc => lc.Parent == this).ToArray();
        if (childLightComponents.Length > 0)
        {
            feedingRateLight = childLightComponents[0];
            feedingRateLightColor = feedingRateLight.LightColor;
        }
        if (childLightComponents.Length > 1)
        {
            containmentRateLight = childLightComponents[1];
            containmentRateLightColor = containmentRateLight.LightColor;
        }

        OnItemLoadedProjSpecific();
    }

    partial void OnItemLoadedProjSpecific();

    public override void Update(float deltaTime, Camera? cam)
    {
#if SERVER
        if (GameMain.Server != null && nextServerLogWriteTime != null)
        {
            if (Timing.TotalTime >= (float)nextServerLogWriteTime)
            {
                GameServer.Log(GameServer.CharacterLogName(lastUser) + " adjusted reactor settings: " +
                        "Singularity strength: " + (int)((Singularity?.NormalizedStrength ?? 0.0f) * 100.0f) +
                        ", Singularity instability: " + (int)(Singularity == null ? 0 : currentInstability) + "%" +
                        ", Feeding rate: " + (int)TargetFeedingRate +
                        ", Containment rate: " + (int)ContainmentRate +
                        (AutoTemp ? ", Autotemp ON" : ", Autotemp OFF"),
                        ServerLog.MessageType.ItemInteraction);

                nextServerLogWriteTime = null;
                lastServerLogWriteTime = (float)Timing.TotalTime;
            }
        }
#endif

#if CLIENT
        if (PowerOn && AvailableFuel < 1)
        {
            HintManager.OnReactorOutOfFuel(Item, AvailableFuel);
        }
#endif

        UpdateSingularityReactorSounds();

        base.Update(deltaTime, cam);

        if (feedingRateLight != null)
        {
            feedingRateLight.IsOn = feedingRate > 0.0f;
            feedingRateLight.LightColor = feedingRateLightColor * NormalizedFeedingRate;
#if CLIENT
            feedingRateLight.Light.OverrideLightSpriteAlpha = NormalizedFeedingRate;
#endif
        }
        if (containmentRateLight != null)
        {
            containmentRateLight.IsOn = containmentRate > 0.0f;
            containmentRateLight.LightColor = containmentRateLightColor * NormalizedContainmentRate;
#if CLIENT
            containmentRateLight.Light.OverrideLightSpriteAlpha = NormalizedContainmentRate;
#endif
        }

        var nonBrokenStabilizers = stabilizers.Where(s => s.Item.Condition > 0.0f);

        maxContainmentRate = 0.0f;
        foreach (var stabilizer in stabilizers)
        {
            if (stabilizer.Item.Condition <= 0.0f) { continue; }
            //divide the containment rate evenly between the functional stabilizers
            stabilizer.FlowPercentage = -TargetContainmentRate / nonBrokenStabilizers.Count();
            stabilizer.IsOn = PowerOn;
            //the better condition the stabilizer is in, the more effective it is at containing the singularity
            maxContainmentRate += MathHelper.Lerp(5.0f, 50.0f, stabilizer.Item.ConditionPercentage / 100.0f);

            if (!stabilizer.Item.InWater && stabilizer.IsOn && stabilizer.FlowPercentage < 0.0f)
            {
                stabilizer.Item.Condition -= MathHelper.Lerp(MinStabilizerDamage, MaxStabilizerDamage, TargetContainmentRate) * deltaTime;
            }
        }
        
        TargetContainmentRate = Math.Min(maxContainmentRate, TargetContainmentRate);

        float maxPowerOut = GetMaxOutput();

        if (signalControlledTargetFeedingRate.HasValue && lastReceivedFeedingRateSignalTime > Timing.TotalTime - 1)
        {
            TargetFeedingRate = AdjustValueWithoutOverShooting(TargetFeedingRate, signalControlledTargetFeedingRate.Value, deltaTime * SignalControlledAdjustmentSpeed);
#if CLIENT
            FeedingRateScrollBar.BarScroll = TargetFeedingRate / 100.0f;                
#endif
        }
        else
        {
            signalControlledTargetFeedingRate = null;
        }
        if (signalControlledTargetContainmentRate.HasValue && lastReceivedContainmentRateSignalTime > Timing.TotalTime - 1)
        {
            TargetContainmentRate = AdjustValueWithoutOverShooting(TargetContainmentRate, signalControlledTargetContainmentRate.Value, deltaTime * SignalControlledAdjustmentSpeed);
#if CLIENT
            ContainmentRateScrollBar.BarScroll = TargetContainmentRate / 100.0f;
#endif
        }
        else
        {
            signalControlledTargetContainmentRate = null;
        }

        UpdateSingularity(deltaTime);

        UpdateFailures(deltaTime);
#if CLIENT
        UpdateGraphs(deltaTime);
#endif
        AvailableFuel = 0.0f;
    }

    partial void UpdateSingularityReactorSounds();

    private void UpdateSingularity(float deltaTime)
    {
        if (Singularity != null && GameMain.NetworkMember is not { IsClient: true })
        {
            if (IsSingularityEscaping)
            {
                escapeContainmentTimer += deltaTime;
                if (escapeContainmentTimer > EscapeContainmentDelay)
                {
                    item.Condition = 0.0f;
                    SingularityEscape();
                }
            }
            else
            {
                escapeContainmentTimer = Math.Max(0.0f, escapeContainmentTimer - deltaTime);
            }

            //start taking damage when singularity is very strong
            if (Singularity != null && Singularity.Strength > SingularityStrengthDamageThreshold)
            {
                item.Condition -= Singularity.Strength / 100.0f * deltaTime;
            }
        }

        if (!PowerOn) 
        { 
            //can't turn the reactor off when in critical condition
            if (Singularity != null && InCriticalCondition)
            {
                PowerOn = true;
            }
            return; 
        }

        if (Level.Loaded is not { Generating: true } &&
            Singularity?.Item is { IdFreed: true } &&
            Entity.Spawner != null)
        {
            //remove the temporary singularity we created to power up the outpost during loading
            Entity.Spawner.AddItemToRemoveQueue(Singularity.Item);
            Singularity = null;
        }

        if (Singularity == null)
        {
            if (FeedingRate > 1.0f)
            {
                createSingularityDelay -= deltaTime;
                if (createSingularityDelay <= 0.0f && Entity.Spawner != null && GameMain.NetworkMember is not { IsClient: true })
                {
                    if (!ItemPrefab.Prefabs.TryGet(SingularityPrefab, out ItemPrefab? singularityPrefab))
                    {
                        DebugConsole.ThrowError($"Failed to find the singularity prefab ({SingularityPrefab}) to spawn in {nameof(SingularityReactor)}");
                        return;
                    }
                    //a bit hacky: spawn the singularity above the top of the level, so it doesn't appear to be uncontained at the position of the reactor
                    //for the brief moment between spawning and the client receiving an update about it being contained
                    Vector2 spawnPos = new Vector2(0, (Level.Loaded?.Size.Y ?? 0) * 1.5f);
                    if (Level.Loaded is { Generating: true })
                    {
                        //can't use EntitySpawner yet when we're still generating, but we need a temporary singularity for OutpostGenerator.PowerUpOutpost regardless
                        Singularity = new Item(singularityPrefab, spawnPos, submarine: null).GetComponent<SingularityComponent>();
                        Singularity.Strength = 5.0f;
                        //free the ID so this temporary singularity doesn't mess up IDs of anything else that gets spawned during loading
                        Singularity.Item.FreeID();
                    }
                    else
                    {
                        Entity.Spawner?.AddItemToSpawnQueue(singularityPrefab, spawnPos,
                            onSpawned: (Item item) =>
                            {
                                Singularity = item.GetComponent<SingularityComponent>();
                                Singularity.Strength = 5.0f;
                            });
                    }
                    createSingularityDelay = 1.0f;
                }
            }
        }
        else
        {
            Singularity.IsContained = true;

            float targetStrength = FeedingRate
                //Low containment rates make the singularity larger, high values make it smaller-
                //Math.Sqrt is intended to make ramping up the containment rate start restricting the singularity more noticeably even at low-to-mid rates.
                //It also makes autotemp less capable of just cranking both the feeding and the containment rate very high (the high containment rate would restrict output too much)
                * MathHelper.Lerp(2.0f, 0.5f, MathF.Sqrt(NormalizedContainmentRate));
            const float MinStrengthAdjustmentSpeed = 10.0f;
            const float MaxStrengthAdjustmentSpeed = 100.0f;
            Singularity.Strength = AdjustValueWithoutOverShooting(Singularity.Strength, targetStrength, 
                speed: MathHelper.Lerp(MinStrengthAdjustmentSpeed, MaxStrengthAdjustmentSpeed, NormalizedFeedingRate) * deltaTime);
            
            if (Singularity.Item.body is { } body)
            {
                Vector2 singularityTargetPos = item.WorldPosition;
                Vector2 transformedOffset = SingularityOffset.ToVector2() * item.Scale;
                if (item.FlippedX) { transformedOffset.X = -transformedOffset.X; }
                if (item.FlippedY) { transformedOffset.Y = -transformedOffset.Y; }
                singularityTargetPos += transformedOffset;

                Vector2 diff = singularityTargetPos - Singularity.Item.WorldPosition;
                if (diff.LengthSquared() > 10.0f || item.Submarine != Singularity.Item.Submarine)
                {
                    Singularity.Item.SetTransform(item.SimPosition + ConvertUnits.ToSimUnits(transformedOffset), rotation: 0.0f);
                    Singularity.Item.CurrentHull = item.CurrentHull;
                    Singularity.Item.Submarine = body.Submarine =  item.Submarine;
                    body.LinearVelocity = Vector2.Zero;
                }
                else
                {
                    body.LinearVelocity = Vector2.Lerp(body.LinearVelocity, diff, 0.5f);
                }
            }

            Singularity.Strength += GetSingularityStrengthFluctuation(deltaTime);

            if (Singularity.Item.Removed)
            {
                createSingularityDelay = 5.0f;
                containmentFieldTargetAmplitude = MinContainmentFieldAmplitude;
                containmentFieldTargetOffset = (MinContainmentFieldOffset + MaxContainmentFieldOffset) / 2;
                TargetContainmentRate = 0.0f;
                TargetFeedingRate = 0.0f;
                PowerOn = false;
                Singularity = null;
                escapeContainmentTimer = 0.0f;
            }

            UpdateContainmentField(deltaTime);
        }
    }

    public override void OnItemBroken()
    {
        base.OnItemBroken();
        if (Singularity != null)
        {
            Collapse();
        }
    }

    public override void UpdateBroken(float deltaTime, Camera cam)
    {
        base.UpdateBroken(deltaTime, cam);
        UpdateSingularity(deltaTime);
        UpdateSingularityReactorSounds();
    }

    private void UpdateContainmentField(float deltaTime)
    {
        currentInstability = GetInstability();

        //server handles updating the containment field
        if (GameMain.NetworkMember is { IsClient: true } || Singularity == null) { return; }

        containmentFieldUpdateTimer += deltaTime;

        float graphInstability = currentInstability + Singularity.NormalizedStrength;

        //update interval drops from 10 to ~1.4, dropping very fast as instability approaches the maximum
        float containmentFieldUpdateInterval = MathF.Sqrt(Math.Max(1.0f, MathHelper.Lerp(50.0f, 1.0f, graphInstability)));
        if (containmentFieldUpdateTimer > containmentFieldUpdateInterval)
        {
            bool changeOffset;
            bool changeAmplitude;
            if (SingularityUnstable)
            {
                //at high instability, change both the offset and the amplitude
                changeOffset = true;
                changeAmplitude = true;
            }
            else
            {
                //low instability, just change one
                changeOffset = Rand.Range(0.0f, 1.0f) < 0.5f;
                changeAmplitude = !changeOffset;
            }

            if (changeOffset)
            { 
                float maxChange = MathHelper.Lerp(0.0f, 2.0f, graphInstability);
                containmentFieldTargetOffset = MathHelper.Clamp(containmentFieldTargetOffset + Rand.Range(-maxChange, maxChange), MinContainmentFieldOffset, MaxContainmentFieldOffset);
            }
            if (changeAmplitude)
            {
                float maxAmplitude = MathHelper.Lerp(graphInstability, 1.0f, graphInstability);
                containmentFieldTargetAmplitude = Rand.Range(graphInstability / 2, maxAmplitude);
            }
            containmentFieldTargetAmplitude = Math.Max(graphInstability / 2, containmentFieldTargetAmplitude);
            containmentFieldUpdateTimer = 0.0f;
            RegisterUnsentChanges();
        }
    }

    /// <summary>
    /// Min and Max power output of the reactor based on tolerance
    /// </summary>
    public override PowerRange MinMaxPowerOut(Connection conn, float load)
    {
        const float tolerance = 0.1f;

        float generatedPower = GetGeneratedPower();
        minUpdatePowerOut = Math.Max(0, generatedPower * (1.0f - tolerance));
        maxUpdatePowerOut = Math.Max(minUpdatePowerOut, generatedPower * (1.0f + tolerance));

        float maxPowerOut = GetMaxOutput();
        return new PowerRange(0.0f, GetGeneratedPower(), maxPowerOut);
    }

    private float GetSingularityStrengthFluctuation(float deltaTime)
    {
        if (Singularity == null) { return 0.0f; }

        //random noise value between -0.5 and 0.5
        float noise = PerlinNoise.GetPerlin((float)Timing.TotalTimeUnpaused * 0.25f, (float)Timing.TotalTimeUnpaused * 0.5f) - 0.5f;
        float fluctuation =
            noise
            //exponentially increasing fluctuation based on instability
            * currentInstability * currentInstability
            //larger singularity -> more fluctuation
            * Singularity.NormalizedStrength
            * 5000.0f
            //multiplied by inverse containment rate (lower containment -> more fluctuation)
            * (100.0f - containmentRate) / 100.0f;

        fluctuation = MathHelper.Clamp(fluctuation, -50.0f, 50.0f);
        if (Singularity.Strength + fluctuation * deltaTime < 1.0f)
        {
            fluctuation = 0.0f;
        }
        return fluctuation * deltaTime;
    }

    private float GetInstability()
    {
        if (Singularity == null) { return 0.0f; }

        //strength of the singularity increases instability (ad-hoc formula, exponential increase after strength goes above 10%)
        float strengthBasedInstability = Math.Max((MathF.Pow(Singularity.NormalizedStrength, 2)) * 1.5f, 0.0f);
        float baseInstability = 0.1f + Math.Max(strengthBasedInstability, 0.0f);

        const float LowContainmentThreshold = 20.0f;
        const float HighContainmentThreshold = 90.0f;
        if (ContainmentRate < LowContainmentThreshold)
        {
            //very low containment rate causes instability to be much higher (making stabilizers breaking more of a threat)
            baseInstability += MathHelper.Lerp(0.5f, 0.0f, ContainmentRate / LowContainmentThreshold);
        }
        else if (ContainmentRate > HighContainmentThreshold)
        {
            //very high containment rate causes instability to reduce significantly, making it easier to kill an out-of-control singularity
            const float MaxInstabilityReduction = 0.5f;
            baseInstability += MathHelper.Lerp(0.0f, -MaxInstabilityReduction, MathUtils.InverseLerp(HighContainmentThreshold, 100.0f, ContainmentRate));
        }
        const float InstabilityAtMinContainmentRate = 0.5f;
        const float InstabilityAtMaxContainmentRate = 0.15f;
        baseInstability += MathHelper.Lerp(InstabilityAtMinContainmentRate, InstabilityAtMaxContainmentRate, MathF.Pow(NormalizedContainmentRate, 2));

        //becomes more instable when getting closer to escaping
        float instability = baseInstability + (escapeContainmentTimer / EscapeContainmentDelay);

        //matching containment field manually reduces instability
        const float InstabilityAtBestGraphMatch = 0.05f;
        const float InstabilityAtWorstGraphMatch = 2.5f;
        instability *= MathHelper.Lerp(InstabilityAtBestGraphMatch, InstabilityAtWorstGraphMatch, MathF.Pow(containmentFieldMatchError, 2.0f));
        instability = MathHelper.Max(0.0f, instability);

        return instability;
    }

    private float GetGeneratedPower()
    {
        const float ContainmentPowerCost = 0.2f;
        if (Singularity == null)
        {
            return 0.0f;
        }
        return Math.Max(0, (Singularity.NormalizedStrength - (NormalizedContainmentRate * ContainmentPowerCost)) * GetMaxOutput());
    }

    /// <summary>
    /// Do we need more fuel to generate enough power to match the current load.
    /// </summary>
    /// <param name="minimumOutputRatio">How low we allow the output/load ratio to go before loading more fuel. 
    /// 1.0 = always load more fuel when maximum output is too low, 0.5 = load more if max output is 50% of the load</param>
    protected override bool NeedMoreFuel(float minimumOutputRatio, float minCondition = 0)
    {
        float remainingFuel = item.ContainedItems.Sum(i => i.Condition);
        if (remainingFuel <= minCondition && Load > 0.0f)
        {
            return true;
        }

        return false;
    }

    public override bool TooMuchFuel()
    {
        return false;
    }

    private void UpdateFailures(float deltaTime)
    {
        item.SendSignal(InCriticalCondition ? "1" : "0", "collapse_warning");
        item.SendSignal(IsSingularityEscaping ? "1" : "0", "escape_warning");        
    }

    public override void UpdateAutoTemp(float speed, float deltaTime, Character operatingCharacter)
    {
        float generatedPower = GetGeneratedPower();

        float desiredContainmentRate = 70.0f;

        //almost at the instability threshold, reduce feeding rate and increase containment
        bool closeToEscapeThreshold = 
            currentInstability > SingularityEscapeMinInstability / 100.0f * 0.95f ||
            escapeContainmentTimer > EscapeContainmentDelay / 2.0f;
        if (closeToEscapeThreshold)
        {
            TargetFeedingRate -= speed * 5 * deltaTime;
        }
        else if (Math.Abs(Load - generatedPower) > 1.0f)
        {
            if (Singularity == null)
            {
                //no singularity yet -> no point in cranking the feeding rate up in hopes of more power
                //that'll just lead to overloading the grid when the singularity appears
                targetFeedingRate = 5.0f;
            }
            else
            {
                float loadDiff = Load - generatedPower;
                TargetFeedingRate += loadDiff * 0.1f * speed * deltaTime;
                desiredContainmentRate = MathHelper.Lerp(70.0f, 25.0f, MathHelper.Clamp(loadDiff / GetMaxOutput(), 0.0f, 1.0f));
            }
            //don't push the target too far from the current feeding rate
            //otherwise we may "overshoot", cranking the target feeding rate all the way up because it takes a while
            //for the actual feeding rate and output to follow
            TargetFeedingRate = MathHelper.Clamp(TargetFeedingRate, FeedingRate - 5, FeedingRate + 5);
        }

        float containmentRateDiff = desiredContainmentRate - TargetContainmentRate;
        TargetContainmentRate += containmentRateDiff * 0.1f * speed * deltaTime; 
        if (closeToEscapeThreshold)
        {
            TargetContainmentRate += speed * 5 * deltaTime;
        }
        if (operatingCharacter != null)
        {
            //higher level characters can also adjust the containment field
            if (degreeOfSuccess > 0.25f)
            {
                containmentFieldAmplitude = adjustValueWithoutOverShooting(containmentFieldAmplitude, containmentFieldTargetAmplitude, speed * degreeOfSuccess * deltaTime);
                containmentFieldOffset = adjustValueWithoutOverShooting(containmentFieldOffset, containmentFieldTargetOffset, speed * degreeOfSuccess * deltaTime);
#if CLIENT
                if (ContainmentFieldAmplitudeScrollBar != null)
                {
                    ContainmentFieldAmplitudeScrollBar.BarScrollValue = containmentFieldAmplitude;
                    ContainmentFieldOffsetScrollBar.BarScrollValue = -containmentFieldOffset;
                }
#endif
            }
        }

#if CLIENT
        FeedingRateScrollBar.BarScroll = TargetFeedingRate / 100.0f;
        ContainmentRateScrollBar.BarScroll = TargetContainmentRate / 100.0f;
#endif

        static float adjustValueWithoutOverShooting(float current, float target, float speed)
        {
            return target < current ? Math.Max(target, current - speed) : Math.Min(target, current + speed);
        }
    }

    private void SingularityEscape()
    {
        PowerOn = false;
        if (Singularity == null) { return; }
        escapeContainmentTimer = 0.0f;
        currentInstability = 0.0f;
        createSingularityDelay = 5.0f;
        containmentFieldTargetAmplitude = MinContainmentFieldAmplitude;
        containmentFieldTargetOffset = (MinContainmentFieldOffset + MaxContainmentFieldOffset) / 2;
        Singularity.IsContained = false;
        Singularity = null;
        BreakContainedItems();

        ApplyStatusEffects(ActionType.OnFailure, deltaTime: 1.0f);
    }

    /// <summary>
    /// Called when the reactor breaks.
    /// </summary>
    private void Collapse()
    {
        if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

        escapeContainmentTimer = 0.0f;
        currentInstability = 0.0f;
        PowerOn = false;

        if (Singularity != null)
        {
            SingularityEscape();
        }

        if (!ExplosionDamagesOtherSubs)
        {
            foreach (var statusEffect in GetStatusEffectsOfType(ActionType.OnBroken))
            {
                foreach (Explosion explosion in statusEffect.Explosions)
                {
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub != item.Submarine) { explosion.IgnoredSubmarines.Add(sub); }
                    }
                }
            }
        }        

        item.Condition = 0.0f; 
        BreakContainedItems();
#if SERVER
        GameServer.Log("Singularity reactor collapse!", ServerLog.MessageType.ItemInteraction);
        GameMain.Server?.KarmaManager.OnReactorMeltdown(item, blameOnBroken?.Character);
#endif
    }



    public override void OnMapLoaded()
    {
        base.OnMapLoaded();
        stabilizers.Clear();

        foreach (var linked in item.linkedTo.ToList())
        {
            if (linked is not Item linkedItem) { continue; }
            if (linkedItem.HasTag("singularityreactorstabilizer".ToIdentifier()) && linkedItem.GetComponent<Pump>() is { } stabilizer)
            {
                stabilizers.Add(stabilizer);
            }
            else if (linkedItem.GetComponent<SingularityComponent>() is { } linkedSingularity)
            {
                Singularity = linkedSingularity;
            }
        }
    }

    public override void ReceiveSignal(Signal signal, Connection connection)
    {
        base.ReceiveSignal(signal, connection);

        switch (connection.Name)
        {
            case "set_feedingrate":
                if (PowerOn && float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float newFeedingRate))
                {
                    signalControlledTargetFeedingRate = newFeedingRate;
                    lastReceivedFeedingRateSignalTime = Timing.TotalTime;
                    RegisterUnsentChanges();
                }
                break;
            case "set_containmentrate":
                if (PowerOn && float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float newContainmentRate))
                {
                    signalControlledTargetContainmentRate = newContainmentRate;
                    lastReceivedContainmentRateSignalTime = Timing.TotalTime;
                    RegisterUnsentChanges();
                }
                break;
        }
    }

    private void RegisterUnsentChanges()
    {
        if (GameMain.NetworkMember is { IsServer: true }) { unsentChanges = true; }
    }

    protected override void RemoveComponentSpecific()
    {
        base.RemoveComponentSpecific();

        if (Singularity != null)
        {
            if (Entity.Spawner == null)
            {
                Singularity.Item.Remove();
            }
            else
            {
                Entity.Spawner.AddItemToRemoveQueue(Singularity.Item);
            }
        }
#if CLIENT
        graphLine?.Remove();
        meterBar?.Remove();
#endif
    }

}
