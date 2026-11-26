using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    public abstract class ReactorBase : Powered, IServerSerializable, IClientSerializable
    {
        const float NetworkUpdateIntervalHigh = 0.5f;
        const float NetworkUpdateIntervalLow = 10.0f;

        /// <summary>
        /// How fast the reactor controls move towards the desired value when controlled by signals.
        /// </summary>
        protected const float SignalControlledAdjustmentSpeed = 5.0f;

        //is automatic temperature control on
        //(adjusts the fission rate and turbine output automatically to keep the
        //amount of power generated balanced with the load)
        private bool autoTemp;
        
        private float fuelConsumptionRate;

        private float maxPowerOutput;
        protected float minUpdatePowerOut;
        protected float maxUpdatePowerOut;

        protected bool unsentChanges;
        protected float sendUpdateTimer;

        protected float degreeOfSuccess;

        /// <summary>
        /// A value between 0-100, represents how much heat the engine is generating (in normal reactors, the fission rate)
        /// </summary>
        protected abstract float PowerGenerationRate { get; set; }

        /// <summary>
        /// A value between 0-100, represents how much power the generator is generating from the heat (in normal reactors, the turbine output)
        /// </summary>
        protected abstract float OutputControlRate { get; set; }

        protected abstract float TargetPowerGenerationRate { get; set; }

        protected abstract float TargetOutputControlRate { get; set; }

        /// <summary>
        /// What should the power generation rate be set to when the reactor is powered down?
        /// </summary>
        protected virtual float PowerGenerationRateWhenPoweredDown { get => 0; }

        /// <summary>
        /// What should the output rate be set to when the reactor is powered down?
        /// </summary>
        protected virtual float OutputRateWhenPoweredDown { get => 0; }

        private bool _powerOn;

        [Serialize(defaultValue: false, isSaveable: IsPropertySaveable.Yes)]
        public bool PowerOn
        {
            get { return _powerOn; }
            set
            {
                if (_powerOn == value) { return; }
                _powerOn = value;
#if CLIENT
                UpdateUIElementStates();
#endif
            }
        }

        protected override PowerPriority Priority { get { return PowerPriority.Reactor; } }

        private bool lastUserWasTryingToBlowUpReactor;

        public Character LastAIUser { get; protected set; }

        [Serialize(defaultValue: false, isSaveable: IsPropertySaveable.Yes)]
        public bool LastUserWasPlayer { get; private set; }

        protected Character lastUser;
        public Character LastUser
        {
            get { return lastUser; }
            protected set
            {
                if (lastUser == value) { return; }
                if (Screen.Selected.IsEditor) { return; }
                lastUser = value;
                if (lastUser == null)
                {
                    degreeOfSuccess = 0.0f;
                    LastUserWasPlayer = false;
                }
                else
                {
                    degreeOfSuccess = Math.Min(DegreeOfSuccess(lastUser), 1.0f);
                    LastUserWasPlayer = lastUser.IsPlayer;
                }
            }
        }

        [Editable(0.0f, float.MaxValue), Serialize(10000.0f, IsPropertySaveable.Yes, description: "How much power (kW) the reactor generates when operating at full capacity.", alwaysUseInstanceValues: true)]
        public float MaxPowerOutput
        {
            get => maxPowerOutput;
            set => maxPowerOutput = Math.Max(0.0f, value);
        }
        
        [Serialize(0.2f, IsPropertySaveable.Yes, description: "How fast the condition of the contained fuel rods deteriorates per second."), Editable(0.0f, 1000.0f, decimals: 3)]
        public float FuelConsumptionRate
        {
            get => fuelConsumptionRate;
            set
            {
                if (!MathUtils.IsValid(value)) return;
                fuelConsumptionRate = Math.Max(value, 0.0f);
            }
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Is the automatic temperature control currently on. Indended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public bool AutoTemp
        {
            get { return autoTemp; }
            set 
            { 
                autoTemp = value;
#if CLIENT
                UpdateUIElementStates();
#endif
            }
        }
        
        protected float prevAvailableFuel;

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float AvailableFuel { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public new float Load { get; private set; }

        public virtual bool CurrentlyControlledBySignals { get; }

        public ReactorBase(Item item, ContentXElement element)
            : base(item, element)
        {         
            IsActive = true;
        }

#if CLIENT
        protected abstract void UpdateUIElementStates();

        protected abstract void UpdateGraphs(float deltaTime);

        protected abstract GUIScrollBar PowerGenerationRateScrollBar { get; }
        protected abstract GUIScrollBar OutputControlRateScrollBar { get; }
#endif

        public override void Update(float deltaTime, Camera cam)
        {
            prevAvailableFuel = AvailableFuel;
            //if an AI character was using the item on the previous frame but not anymore, turn autotemp on
            // (= bots turn autotemp back on when leaving the reactor)
            if (LastAIUser != null)
            {
                if (LastAIUser.SelectedItem != item && LastAIUser.CanInteractWith(item))
                {
                    if (!lastUserWasTryingToBlowUpReactor) { AutoTemp = true; }                   
                    if (GameMain.NetworkMember?.IsServer ?? false) { unsentChanges = true; }
                    lastUserWasTryingToBlowUpReactor = false;
                    LastAIUser = null;
                }
            }

            //rapidly adjust the reactor in the first few seconds of the round to prevent overvoltages if the load changed between rounds
            //(unless the reactor is being operated by a player)
            if (GameMain.GameSession is { RoundDuration: < 5 } && lastUser is not { IsPlayer: true } && PowerOn && AutoTemp &&
                !CurrentlyControlledBySignals)
            {
                UpdateAutoTemp(100.0f, (float)(Timing.Step * 10.0f));
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime);

            if (!PowerOn)
            {
                TargetPowerGenerationRate = PowerGenerationRateWhenPoweredDown;
                TargetOutputControlRate = OutputRateWhenPoweredDown;
            }
            else if (autoTemp)
            {
                UpdateAutoTemp(speed: 2.0f, deltaTime, operatingCharacter: LastAIUser);
            }

            PowerGenerationRate = MathHelper.Lerp(PowerGenerationRate, Math.Min(TargetPowerGenerationRate, AvailableFuel), deltaTime);
            OutputControlRate = MathHelper.Lerp(OutputControlRate, TargetOutputControlRate, deltaTime);

            float fuelLeft = 0.0f;
            var containedItems = item.OwnInventory?.AllItems;
            if (containedItems != null)
            {
                foreach (Item item in containedItems)
                {
                    if (!item.HasTag(Tags.ReactorFuel)) { continue; }
                    if (PowerGenerationRate > 0.0f)
                    {
                        bool isConnectedToFriendlyOutpost = Level.IsLoadedOutpost && 
                            Item.Submarine?.TeamID == CharacterTeamType.Team1 && 
                            Item.Submarine.GetConnectedSubs().Any(s => s.Info.IsOutpost && s.TeamID == CharacterTeamType.FriendlyNPC);

                        if (!isConnectedToFriendlyOutpost)
                        {
                            item.Condition -= GetCurrentFuelConsumption(deltaTime);
                        }
                    }
                    fuelLeft += item.ConditionPercentage;
                }
            }

            if (PowerGenerationRate > 0.0f)
            {
                float maxPowerOut = GetMaxOutput();
                if (item.AiTarget != null && maxPowerOut > 0)
                {
                    var aiTarget = item.AiTarget;
                    float range = Math.Abs(currPowerConsumption) / maxPowerOut;
                    aiTarget.SoundRange = MathHelper.Lerp(aiTarget.MinSoundRange, aiTarget.MaxSoundRange, range);
                    if (item.CurrentHull != null)
                    {
                        var hullAITarget = item.CurrentHull.AiTarget;
                        if (hullAITarget != null)
                        {
                            hullAITarget.SoundRange = Math.Max(hullAITarget.SoundRange, aiTarget.SoundRange);
                        }
                    }
                }
            }

            item.SendSignal(((int)-CurrPowerConsumption).ToString(), "power_value_out");
            item.SendSignal(((int)Load).ToString(), "load_value_out");
            item.SendSignal(((int)AvailableFuel).ToString(), "fuel_out");
            item.SendSignal(((int)fuelLeft).ToString(), "fuel_percentage_left");

#if CLIENT
            UpdateGraphs(deltaTime);
#endif
            UpdateNetworkEvents(deltaTime);
        }

        /// <summary>
        /// Creates a network event if there are unsent changes (or periodically on the server).
        /// Called from both <see cref="Update"/> and <see cref="UpdateBroken"/>, so changes made when the reactor breaks
        /// (e.g. powering down after a meltdown or the singularity escaping) still get synced.
        /// </summary>
        private void UpdateNetworkEvents(float deltaTime)
        {
            sendUpdateTimer -= deltaTime;
#if CLIENT
            if (unsentChanges && sendUpdateTimer <= 0.0f)
#else
            if (sendUpdateTimer < -NetworkUpdateIntervalLow || (unsentChanges && sendUpdateTimer <= 0.0f))
#endif
            {
#if SERVER
                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                }
#elif CLIENT
                if (GameMain.Client != null)
                {
                    item.CreateClientEvent(this);
                }
#endif
                sendUpdateTimer = NetworkUpdateIntervalHigh;
                unsentChanges = false;
            }
        }

        protected float GetCurrentFuelConsumption(float deltaTime)
        {
            return PowerGenerationRate / 100.0f * GetFuelConsumption() * deltaTime;
        }

        protected static float AdjustValueWithoutOverShooting(float current, float target, float speed)
        {
            return target < current ? Math.Max(target, current - speed) : Math.Min(target, current + speed);
        }

        /// <summary>
        /// Returns a negative value (indicating the reactor generates power) when querying the power output connection.
        /// </summary>
        public override sealed float GetCurrentPowerConsumption(Connection connection = null)
        {
            return connection != null && connection.IsPower && connection.IsOutput ? -1 : 0;
        }

        /// <summary>
        /// Determine how much power to output based on the load. The load is divided between reactors according to their maximum output in multi-reactor setups.
        /// </summary>
        public override sealed float GetConnectionPowerOut(Connection conn, float power, PowerRange minMaxPower, float load)
        {
            //Load must be calculated at this stage instead of at gridResolved to remove influence of lower priority devices
            float loadLeft = MathHelper.Max(load - power,0);
            float expectedPower = MathHelper.Clamp(loadLeft, minMaxPower.Min, minMaxPower.Max);

            //Delta ratio of Min and Max power output capability of the grid
            float ratio = MathHelper.Max((loadLeft - minMaxPower.Min) / (minMaxPower.Max - minMaxPower.Min), 0);
            if (float.IsInfinity(ratio))
            {
                ratio = 0;
            }

            float output = MathHelper.Clamp(ratio * (maxUpdatePowerOut - minUpdatePowerOut) + minUpdatePowerOut, minUpdatePowerOut, maxUpdatePowerOut);
            float newLoad = loadLeft;

            float maxOutput = GetMaxOutput();

            //Adjust behaviour for multi reactor setup
            if (maxOutput != minMaxPower.ReactorMaxOutput)
            {
                float idealLoad = maxOutput / minMaxPower.ReactorMaxOutput * loadLeft;
                float loadAdjust = MathHelper.Clamp((ratio - 0.5f) * 25 + idealLoad - (OutputControlRate / 100 * maxOutput), -maxOutput / 100, maxOutput / 100);
                newLoad = MathHelper.Clamp(loadLeft - (expectedPower - output) + loadAdjust, 0, loadLeft);
            }

            if (float.IsNegative(newLoad))
            {
                newLoad = 0.0f;
            }
            
            Load = newLoad;
            currPowerConsumption = -output;
            return output;
        }

        public abstract void UpdateAutoTemp(float speed, float deltaTime, Character operatingCharacter = null);

        /// <summary>
        /// Do we need more fuel to generate enough power to match the current load.
        /// </summary>
        protected abstract bool NeedMoreFuel(float minimumOutputRatio, float minCondition = 0);

        public abstract bool TooMuchFuel();

        public void PowerUpImmediately()
        {
            PowerOn = true;
            AutoTemp = true;
            prevAvailableFuel = AvailableFuel;
            for (int i = 0; i < 100; i++)
            {
                Update((float)(Timing.Step * 10.0f), cam: null);
                UpdateAutoTemp(100.0f, (float)(Timing.Step * 10.0f));
                AvailableFuel = prevAvailableFuel;
            }
        }
        
        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);

            currPowerConsumption = 0.0f;
            TargetPowerGenerationRate = Math.Max(TargetPowerGenerationRate - deltaTime * 10.0f, 0.0f);
            TargetOutputControlRate = Math.Max(TargetOutputControlRate - deltaTime * 10.0f, 0.0f);
#if CLIENT
            SetScrollBarsToMatchRates();
            UpdateGraphs(deltaTime);
#endif
            UpdateNetworkEvents(deltaTime);
        }

        /// <summary>
        /// Sets the conditions of all contained items to 0
        /// </summary>
        protected void BreakContainedItems()
        {
            var containedItems = item.OwnInventory?.AllItems;
            if (containedItems != null)
            {
                foreach (Item containedItem in containedItems)
                {
                    containedItem.Condition = 0.0f;
                }
            }
        }

        public override sealed bool Pick(Character picker)
        {
            return picker != null;
        }

        public override sealed bool CrewAIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }
            character.AIController.SteeringManager.Reset();
            bool shutDown = objective.Option == "shutdown";
            bool blowup = objective.Option == "blowup";

            IsActive = true;

            if (!shutDown)
            {
                float degreeOfSuccess = Math.Min(DegreeOfSuccess(character), 1.0f);
                float refuelLimit = 0.3f;
                //characters with insufficient skill levels don't refuel the reactor
                if (degreeOfSuccess > refuelLimit)
                {
                    if (aiUpdateTimer > 0.0f)
                    {
                        aiUpdateTimer -= deltaTime;
                        return false;
                    }
                    aiUpdateTimer = AIUpdateInterval;
                    // load more fuel if the current maximum output is only 50% of the current load
                    // or if the fuel rod is (almost) deplenished 
                    float minCondition = GetFuelConsumption() * MathUtils.Pow2((degreeOfSuccess - refuelLimit) * 2);
                    if (NeedMoreFuel(minimumOutputRatio: 0.5f, minCondition: minCondition))
                    {
                        bool outOfFuel = false;
                        var container = item.GetComponent<ItemContainer>();
                        if (objective.SubObjectives.None())
                        {
                            var containObjective = AIContainItems<ReactorBase>(container, character, objective, itemCount: 1, equip: true, removeEmpty: true, spawnItemIfNotFound: character.CanSpawnMissingItems, dropItemOnDeselected: true);
                            containObjective.StoreRemovedItems = true;
                            containObjective.Completed += ReportFuelRodCount;
                            containObjective.Abandoned += ReportFuelRodCount;
                            character.Speak(TextManager.Get("DialogReactorFuel").Value, null, 0.0f, Tags.ReactorFuel, 30.0f);

                            void ReportFuelRodCount()
                            {
                                if (!character.IsOnPlayerTeam) { return; }
                                if (character.Submarine != Submarine.MainSub) { return; }
                                int remainingFuelRods = Submarine.MainSub.GetItems(false).Count(i => i.HasTag(Tags.ReactorFuel) && i.Condition > 1);
                                if (remainingFuelRods == 0)
                                {
                                    character.Speak(TextManager.Get("DialogOutOfFuelRods").Value, null, 0.0f, "outoffuelrods".ToIdentifier(), 30.0f);
                                    outOfFuel = true;
                                }
                                else if (remainingFuelRods < 3)
                                {
                                    character.Speak(TextManager.Get("DialogLowOnFuelRods").Value, null, 0.0f, "lowonfuelrods".ToIdentifier(), 30.0f);
                                }
                            }
                        }
                        return outOfFuel;
                    }
                    else
                    {
                        if (Item.ConditionPercentage <= 0 && AIObjectiveRepairItems.IsValidTarget(Item, character, allowInAnySub: true))
                        {
                            if (Item.Repairables.Average(r => r.DegreeOfSuccess(character)) > 0.4f)
                            {
                                objective.AddSubObjective(new AIObjectiveRepairItem(character, Item, objective.objectiveManager, isPriority: true));
                                return false;
                            }
                            else
                            {
                                character.Speak(TextManager.Get("DialogReactorIsBroken").Value, identifier: "reactorisbroken".ToIdentifier(), minDurationBetweenSimilar: 30.0f);
                            }
                        }
                        if (TooMuchFuel())
                        {
                            DropFuel(minCondition: 0.1f, maxCondition: 100);
                        }
                        else
                        {
                            DropFuel(minCondition: 0, maxCondition: 0);
                        }
                    }
                }
            }

            if (objective.Override)
            {
                if (lastUser != null && lastUser != character && lastUser != LastAIUser)
                {
                    if (lastUser.SelectedItem == item && character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.Get("DialogReactorTaken").Value, null, 0.0f, "reactortaken".ToIdentifier(), 10.0f);
                    }
                }
            }
            else if (LastUserWasPlayer && lastUser != null && lastUser.TeamID == character.TeamID)
            {
                return true;
            }

            LastUser = LastAIUser = character;
            lastUserWasTryingToBlowUpReactor = blowup;

            bool prevAutoTemp = autoTemp;
            bool prevPowerOn = _powerOn;
            float prevHeatGenerationRate = TargetPowerGenerationRate;
            float prevOutputControlRate = TargetOutputControlRate;

            if (blowup)
            {
                PowerOn = true;
                TargetPowerGenerationRate = 100.0f;
                TargetOutputControlRate = 0.0f;
                AutoTemp = false;
                unsentChanges = true;
                //not completed
                return false;
            }
            else if (shutDown)
            {
                PowerOn = false;
                TargetPowerGenerationRate = 0.0f;
                TargetOutputControlRate = 0.0f;
                unsentChanges = true;
                return true;
            }
            else
            {
                PowerOn = true;
                if (objective.Override || !autoTemp)
                {
                    //characters with insufficient skill levels simply set the autotemp on instead of trying to adjust the temperature manually
                    if (degreeOfSuccess < 0.5f)
                    {
                        AutoTemp = true;
                    }
                    else
                    {
                        AutoTemp = false;
                        UpdateAutoTemp(speed: 1.0f, deltaTime: 1.0f, operatingCharacter: character);
                    }
                }
#if CLIENT
                SetScrollBarsToMatchRates();
#endif
                if (autoTemp != prevAutoTemp ||
                    prevPowerOn != _powerOn ||
                    Math.Abs(prevHeatGenerationRate - TargetPowerGenerationRate) > 1.0f ||
                    Math.Abs(prevOutputControlRate - TargetOutputControlRate) > 1.0f)
                {
                    unsentChanges = true;
                }
                aiUpdateTimer = AIUpdateInterval;
                return false;
            }


            void DropFuel(float minCondition, float maxCondition)
            {
                if (item.OwnInventory?.AllItems != null)
                {
                    var container = item.GetComponent<ItemContainer>();
                    foreach (Item item in item.OwnInventory.AllItemsMod)
                    {
                        if (item.ConditionPercentage <= maxCondition && item.ConditionPercentage >= minCondition)
                        {
                            item.Drop(character);
                            (character.AIController as HumanAIController)?.StoreItem(item);
                            break;
                        }
                    }
                }
            }
        }

#if CLIENT
        protected virtual void SetScrollBarsToMatchRates()
        {
            PowerGenerationRateScrollBar.BarScroll = PowerGenerationRate / 100.0f;
            OutputControlRateScrollBar.BarScroll = OutputControlRate / 100.0f;
        }
#endif

        public override void OnMapLoaded()
        {
            prevAvailableFuel = AvailableFuel;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "shutdown":
                    if (TargetPowerGenerationRate > 0.0f || TargetOutputControlRate > 0.0f)
                    {
                        PowerOn = false;
                        AutoTemp = false;
                        TargetPowerGenerationRate = 0.0f;
                        TargetOutputControlRate = 0.0f;
                        registerUnsentChanges();
                    }
                    break;
            }

            void registerUnsentChanges()
            {
                if (GameMain.NetworkMember is { IsServer: true }) { unsentChanges = true; }
            }
        }

        protected float GetMaxOutput() => item.StatManager.GetAdjustedValueMultiplicative(ItemTalentStats.ReactorMaxOutput, MaxPowerOutput);
        protected float GetFuelConsumption() => item.StatManager.GetAdjustedValueMultiplicative(ItemTalentStats.ReactorFuelConsumption, fuelConsumptionRate);

#if SERVER
        public abstract void ServerEventRead(IReadMessage msg, Client c);
        public abstract void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null);
#elif CLIENT
        public abstract void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null);
        public abstract void ClientEventRead(IReadMessage msg, float sendingTime);
#endif
    }
}
