using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Reactor : Powered, IServerSerializable, IClientSerializable
    {
        const float NetworkUpdateIntervalHigh = 0.5f;

        const float TemperatureBoostAmount = 20;

        //the rate at which the reactor is being run on (higher rate -> higher temperature)
        private float fissionRate;
        
        //how much of the generated steam is used to spin the turbines and generate power
        private float turbineOutput;
        
        private float temperature;
        
        //is automatic temperature control on
        //(adjusts the fission rate and turbine output automatically to keep the
        //amount of power generated balanced with the load)
        private bool autoTemp;
        
        //automatical adjustment to the power output when 
        //turbine output and temperature are in the optimal range
        private float autoAdjustAmount;
        
        private float fuelConsumptionRate;

        private float meltDownTimer, meltDownDelay;
        private float fireTimer, fireDelay;

        private float maxPowerOutput;
        private float minUpdatePowerOut;
        private float maxUpdatePowerOut;

        private bool unsentChanges;
        private float sendUpdateTimer;

        private float degreeOfSuccess;

        private Vector2 optimalTemperature, allowedTemperature;
        private Vector2 optimalFissionRate, allowedFissionRate;
        private Vector2 optimalTurbineOutput, allowedTurbineOutput;

        private float? signalControlledTargetFissionRate, signalControlledTargetTurbineOutput;
        private double lastReceivedFissionRateSignalTime, lastReceivedTurbineOutputSignalTime;

        private float temperatureBoost;

        private bool _powerOn;

        [Serialize(defaultValue: false, isSaveable: IsPropertySaveable.Yes)]
        public bool PowerOn
        {
            get { return _powerOn; }
            set
            {
                _powerOn = value;
#if CLIENT
                UpdateUIElementStates();
#endif
            }
        }

        protected override PowerPriority Priority { get { return PowerPriority.Reactor; } }

        public Character LastAIUser { get; private set; }

        [Serialize(defaultValue: false, isSaveable: IsPropertySaveable.Yes)]
        public bool LastUserWasPlayer { get; private set; }

        private Character lastUser;
        public Character LastUser
        {
            get { return lastUser; }
            private set
            {
                if (lastUser == value) { return; }
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
        
        [Editable(0.0f, float.MaxValue), Serialize(120.0f, IsPropertySaveable.Yes, description: "How long the temperature has to stay critical until a meltdown occurs.")]
        public float MeltdownDelay
        {
            get { return meltDownDelay; }
            set { meltDownDelay = Math.Max(value, 0.0f); }
        }

        [Editable(0.0f, float.MaxValue), Serialize(30.0f, IsPropertySaveable.Yes, description: "How long the temperature has to stay critical until the reactor catches fire.")]
        public float FireDelay
        {
            get { return fireDelay; }
            set { fireDelay = Math.Max(value, 0.0f); }
        }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Current temperature of the reactor (0% - 100%). Indended to be used by StatusEffect conditionals.")]
        public float Temperature
        {
            get { return temperature; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                temperature = MathHelper.Clamp(value, 0.0f, 100.0f);
            }
        }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Current fission rate of the reactor (0% - 100%). Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public float FissionRate
        {
            get { return fissionRate; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                fissionRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
        }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Current turbine output of the reactor (0% - 100%). Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public float TurbineOutput
        {
            get { return turbineOutput; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                turbineOutput = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
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

        [Serialize(false, IsPropertySaveable.Yes, description: "Is the temperature currently critical. Intended to be used by StatusEffect conditionals (setting the value from XML has no effect).")]
        public bool TemperatureCritical
        {
            get { return temperature > allowedTemperature.Y; }
            set { /*do nothing*/ }
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
        
        private float prevAvailableFuel;

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float AvailableFuel { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public new float Load { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float TargetFissionRate { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float TargetTurbineOutput { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float CorrectTurbineOutput { get; set; }

        [Editable, Serialize(true, IsPropertySaveable.Yes)]
        public bool ExplosionDamagesOtherSubs
        {
            get;
            set;
        }

        public Reactor(Item item, ContentXElement element)
            : base(item, element)
        {         
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);
                
        public override void Update(float deltaTime, Camera cam)
        {
#if SERVER
            if (GameMain.Server != null && nextServerLogWriteTime != null)
            {
                if (Timing.TotalTime >= (float)nextServerLogWriteTime)
                {
                    GameServer.Log(GameServer.CharacterLogName(lastUser) + " adjusted reactor settings: " +
                            "Temperature: " + (int)(temperature * 100.0f) +
                            ", Fission rate: " + (int)TargetFissionRate +
                            ", Turbine output: " + (int)TargetTurbineOutput +
                            (autoTemp ? ", Autotemp ON" : ", Autotemp OFF"),
                            ServerLog.MessageType.ItemInteraction);

                    nextServerLogWriteTime = null;
                    lastServerLogWriteTime = (float)Timing.TotalTime;
                }
            }
#endif

            //if an AI character was using the item on the previous frame but not anymore, turn autotemp on
            // (= bots turn autotemp back on when leaving the reactor)
            if (LastAIUser != null)
            {
                if (LastAIUser.SelectedItem != item && LastAIUser.CanInteractWith(item))
                {
                    AutoTemp = true;
                    if (GameMain.NetworkMember?.IsServer ?? false) { unsentChanges = true; }
                    LastAIUser = null;
                }
            }

#if CLIENT
            if (PowerOn && AvailableFuel < 1)
            {
                HintManager.OnReactorOutOfFuel(this);
            }
#endif

            float maxPowerOut = GetMaxOutput();

            if (signalControlledTargetFissionRate.HasValue && lastReceivedFissionRateSignalTime > Timing.TotalTime - 1)
            {
                TargetFissionRate = adjustValueWithoutOverShooting(TargetFissionRate, signalControlledTargetFissionRate.Value, deltaTime * 5.0f);
#if CLIENT
                FissionRateScrollBar.BarScroll = TargetFissionRate / 100.0f;
#endif
            }
            else
            {
                signalControlledTargetFissionRate = null;
            }
            if (signalControlledTargetTurbineOutput.HasValue && lastReceivedTurbineOutputSignalTime > Timing.TotalTime - 1)
            {
                TargetTurbineOutput = adjustValueWithoutOverShooting(TargetTurbineOutput, signalControlledTargetTurbineOutput.Value, deltaTime * 5.0f);                
#if CLIENT
                TurbineOutputScrollBar.BarScroll = TargetTurbineOutput / 100.0f;
#endif
            }
            else
            {
                signalControlledTargetTurbineOutput = null;
            }

            static float adjustValueWithoutOverShooting(float current, float target, float speed)
            {
                return target < current ? Math.Max(target, current - speed) : Math.Min(target, current + speed);      
            }

            prevAvailableFuel = AvailableFuel;
            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            //use a smoothed "correct output" instead of the actual correct output based on the load
            //so the player doesn't have to keep adjusting the rate impossibly fast when the load fluctuates heavily
            if (!MathUtils.NearlyEqual(maxPowerOut, 0.0f))
            {
                CorrectTurbineOutput += MathHelper.Clamp((Load / maxPowerOut * 100.0f) - CorrectTurbineOutput, -20.0f, 20.0f) * deltaTime;
            }

            //calculate tolerances of the meters based on the skills of the user
            //more skilled characters have larger "sweet spots", making it easier to keep the power output at a suitable level
            float tolerance = MathHelper.Lerp(2.5f, 10.0f, degreeOfSuccess);
            optimalTurbineOutput = new Vector2(CorrectTurbineOutput - tolerance, CorrectTurbineOutput + tolerance);
            tolerance = MathHelper.Lerp(5.0f, 20.0f, degreeOfSuccess);
            allowedTurbineOutput = new Vector2(CorrectTurbineOutput - tolerance, CorrectTurbineOutput + tolerance);

            optimalTemperature = Vector2.Lerp(new Vector2(40.0f, 60.0f), new Vector2(30.0f, 70.0f), degreeOfSuccess);
            allowedTemperature = Vector2.Lerp(new Vector2(30.0f, 70.0f), new Vector2(10.0f, 90.0f), degreeOfSuccess);
            
            optimalFissionRate = Vector2.Lerp(new Vector2(30, AvailableFuel - 20), new Vector2(20, AvailableFuel - 10), degreeOfSuccess);
            optimalFissionRate.X = Math.Min(optimalFissionRate.X, optimalFissionRate.Y - 10);
            allowedFissionRate = Vector2.Lerp(new Vector2(20, AvailableFuel), new Vector2(10, AvailableFuel), degreeOfSuccess);
            allowedFissionRate.X = Math.Min(allowedFissionRate.X, allowedFissionRate.Y - 10);

            float heatAmount = GetGeneratedHeat(fissionRate);

            float temperatureDiff = (heatAmount - turbineOutput) - Temperature;
            Temperature += MathHelper.Clamp(Math.Sign(temperatureDiff) * 10.0f * deltaTime, -Math.Abs(temperatureDiff), Math.Abs(temperatureDiff));
            temperatureBoost = adjustValueWithoutOverShooting(temperatureBoost, 0.0f, deltaTime);
#if CLIENT
            temperatureBoostUpButton.Enabled = temperatureBoostDownButton.Enabled = Math.Abs(temperatureBoost) < TemperatureBoostAmount * 0.9f;
#endif

            FissionRate = MathHelper.Lerp(fissionRate, Math.Min(TargetFissionRate, AvailableFuel), deltaTime);

            TurbineOutput = MathHelper.Lerp(turbineOutput, TargetTurbineOutput, deltaTime);

            float temperatureFactor = Math.Min(temperature / 50.0f, 1.0f);

            if (!PowerOn)
            {
                TargetFissionRate = 0.0f;
                TargetTurbineOutput = 0.0f;
            }
            else if (autoTemp)
            {
                UpdateAutoTemp(2.0f, deltaTime);
            }


            float fuelLeft = 0.0f;
            var containedItems = item.OwnInventory?.AllItems;
            if (containedItems != null)
            {
                foreach (Item item in containedItems)
                {
                    if (!item.HasTag("reactorfuel")) { continue; }
                    if (fissionRate > 0.0f)
                    {
                        bool isConnectedToFriendlyOutpost = Level.IsLoadedOutpost && 
                            Item.Submarine?.TeamID == CharacterTeamType.Team1 && 
                            Item.Submarine.GetConnectedSubs().Any(s => s.Info.IsOutpost && s.TeamID == CharacterTeamType.FriendlyNPC);

                        if (!isConnectedToFriendlyOutpost)
                        {
                            item.Condition -= fissionRate / 100.0f * GetFuelConsumption() * deltaTime;
                        }
                    }
                    fuelLeft += item.ConditionPercentage;
                }
            }

            if (fissionRate > 0.0f)
            {
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

            item.SendSignal(((int)(temperature * 100.0f)).ToString(), "temperature_out");
            item.SendSignal(((int)-CurrPowerConsumption).ToString(), "power_value_out");
            item.SendSignal(((int)Load).ToString(), "load_value_out");
            item.SendSignal(((int)AvailableFuel).ToString(), "fuel_out");
            item.SendSignal(((int)fuelLeft).ToString(), "fuel_percentage_left");

            UpdateFailures(deltaTime);
#if CLIENT
            UpdateGraph(deltaTime);
#endif
            AvailableFuel = 0.0f;


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

        /// <summary>
        /// Returns a negative value (indicating the reactor generates power) when querying the power output connection.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection connection = null)
        {
            return connection != null && connection.IsPower && connection.IsOutput ? -1 : 0;
        }

        /// <summary>
        /// Min and Max power output of the reactor based on tolerance
        /// </summary>
        public override PowerRange MinMaxPowerOut(Connection conn, float load)
        {
            float tolerance = 1f;

            //If within the optimal output allow for slight output adjustments
            if (turbineOutput > optimalTurbineOutput.X && turbineOutput < optimalTurbineOutput.Y &&
                temperature > optimalTemperature.X && temperature < optimalTemperature.Y)
            {
                tolerance = 3f;
            }

            float maxPowerOut = GetMaxOutput();

            float temperatureFactor = Math.Min(temperature / 50.0f, 1.0f);
            float minOutput = maxPowerOut * Math.Clamp(Math.Min((turbineOutput - tolerance) / 100.0f, temperatureFactor), 0, 1);
            float maxOutput = maxPowerOut * Math.Min((turbineOutput + tolerance) / 100.0f, temperatureFactor);

            minUpdatePowerOut = minOutput;
            maxUpdatePowerOut = maxOutput;

            float reactorMax = PowerOn ? maxPowerOut : maxUpdatePowerOut;

            return new PowerRange(minOutput, maxOutput, reactorMax);
        }

        /// <summary>
        /// Determine how much power to output based on the load. The load is divided between reactors according to their maximum output in multi-reactor setups.
        /// </summary>
        public override float GetConnectionPowerOut(Connection conn, float power, PowerRange minMaxPower, float load)
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
                float loadAdjust = MathHelper.Clamp((ratio - 0.5f) * 25 + idealLoad - (turbineOutput / 100 * maxOutput), -maxOutput / 100, maxOutput / 100);
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

        private float GetGeneratedHeat(float fissionRate)
        {
            return fissionRate * (prevAvailableFuel / 100.0f) * 2.0f + temperatureBoost;
        }

        /// <summary>
        /// Do we need more fuel to generate enough power to match the current load.
        /// </summary>
        /// <param name="minimumOutputRatio">How low we allow the output/load ratio to go before loading more fuel. 
        /// 1.0 = always load more fuel when maximum output is too low, 0.5 = load more if max output is 50% of the load</param>
        private bool NeedMoreFuel(float minimumOutputRatio, float minCondition = 0)
        {
            float remainingFuel = item.ContainedItems.Sum(i => i.Condition);
            if (remainingFuel <= minCondition && Load > 0.0f)
            {
                return true;
            }

            //fission rate is clamped to the amount of available fuel
            float maxFissionRate = Math.Min(prevAvailableFuel, 100.0f);
            if (maxFissionRate >= 100.0f) { return false; }

            float maxTurbineOutput = 100.0f;

            //calculate the maximum output if the fission rate is cranked as high as it goes and turbine output is at max
            float theoreticalMaxHeat = GetGeneratedHeat(fissionRate: maxFissionRate);             
            float temperatureFactor = Math.Min(theoreticalMaxHeat / 50.0f, 1.0f);
            float theoreticalMaxOutput = Math.Min(maxTurbineOutput / 100.0f, temperatureFactor) * GetMaxOutput();

            //maximum output not enough, we need more fuel
            return theoreticalMaxOutput < Load * minimumOutputRatio;
        }

        private bool TooMuchFuel()
        {
            var containedItems = item.OwnInventory?.AllItems;
            if (containedItems != null && containedItems.Count() <= 1) { return false; }

            //get the amount of heat we'd generate if the fission rate was at the low end of the optimal range
            float minimumHeat = GetGeneratedHeat(optimalFissionRate.X);

            //if we need a very high turbine output to keep the engine from overheating, there's too much fuel
            return minimumHeat > Math.Min(CorrectTurbineOutput * 1.5f, 90);
        }

        private void UpdateFailures(float deltaTime)
        {
            if (temperature > allowedTemperature.Y)
            {
                item.SendSignal("1", "meltdown_warning");
                if (!item.InvulnerableToDamage)
                {
                    //faster meltdown if the item is in a bad condition
                    meltDownTimer += MathHelper.Lerp(deltaTime * 2.0f, deltaTime, item.Condition / item.MaxCondition);
                    if (meltDownTimer > MeltdownDelay)
                    {
                        MeltDown();
                        return;
                    }
                }
            }
            else
            {
                item.SendSignal("0", "meltdown_warning");
                meltDownTimer = Math.Max(0.0f, meltDownTimer - deltaTime);
            }

            if (temperature > optimalTemperature.Y)
            {
                fireTimer += MathHelper.Lerp(deltaTime * 2.0f, deltaTime, item.Condition / item.MaxCondition);
#if SERVER
                if (fireTimer > Math.Min(5.0f, FireDelay / 2) && blameOnBroken?.Character?.SelectedItem == item)
                {
                    GameMain.Server.KarmaManager.OnReactorOverHeating(item, blameOnBroken.Character, deltaTime);
                }
#endif
                if (fireTimer >= FireDelay)
                {
                    new FireSource(item.WorldPosition);
                    fireTimer = 0.0f;
                }
            }
            else
            {
                fireTimer = Math.Max(0.0f, fireTimer - deltaTime);
            }
        }

        public void UpdateAutoTemp(float speed, float deltaTime)
        {
            float desiredTurbineOutput = (optimalTurbineOutput.X + optimalTurbineOutput.Y) / 2.0f;
            TargetTurbineOutput += MathHelper.Clamp(desiredTurbineOutput - TargetTurbineOutput, -speed, speed) * deltaTime;
            TargetTurbineOutput = MathHelper.Clamp(TargetTurbineOutput, 0.0f, 100.0f);

            float desiredFissionRate = (optimalFissionRate.X + optimalFissionRate.Y) / 2.0f;
            TargetFissionRate += MathHelper.Clamp(desiredFissionRate - TargetFissionRate, -speed, speed) * deltaTime;

            if (temperature > (optimalTemperature.X + optimalTemperature.Y) / 2.0f)
            {
                TargetFissionRate = Math.Min(TargetFissionRate - speed * 2 * deltaTime, allowedFissionRate.Y);
            }
            else if (-currPowerConsumption < Load)
            {
                TargetFissionRate = Math.Min(TargetFissionRate + speed * 2 * deltaTime, 100.0f);
            }
            TargetFissionRate = MathHelper.Clamp(TargetFissionRate, 0.0f, 100.0f);

            //don't push the target too far from the current fission rate
            //otherwise we may "overshoot", cranking the target fission rate all the way up because it takes a while
            //for the actual fission rate and temperature to follow
            TargetFissionRate = MathHelper.Clamp(TargetFissionRate, FissionRate - 5, FissionRate + 5);
        }

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

            item.SendSignal(((int)(temperature * 100.0f)).ToString(), "temperature_out");

            currPowerConsumption = 0.0f;
            Temperature -= deltaTime * 1000.0f;
            TargetFissionRate = Math.Max(TargetFissionRate - deltaTime * 10.0f, 0.0f);
            TargetTurbineOutput = Math.Max(TargetTurbineOutput - deltaTime * 10.0f, 0.0f);
#if CLIENT
            FissionRateScrollBar.BarScroll = 1.0f - FissionRate / 100.0f;
            TurbineOutputScrollBar.BarScroll = 1.0f - TurbineOutput / 100.0f;
            UpdateGraph(deltaTime);
#endif
        }

        private void MeltDown()
        {
            if (item.Condition <= 0.0f) { return; }
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (!ExplosionDamagesOtherSubs && (statusEffectLists?.ContainsKey(ActionType.OnBroken) ?? false))
            {
                foreach (var statusEffect in statusEffectLists[ActionType.OnBroken])
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
            fireTimer = 0.0f;
            meltDownTimer = 0.0f;

            var containedItems = item.OwnInventory?.AllItems;
            if (containedItems != null)
            {
                foreach (Item containedItem in containedItems)
                {
                    containedItem.Condition = 0.0f;
                }
            }
#if SERVER
            GameServer.Log("Reactor meltdown!", ServerLog.MessageType.ItemInteraction);
            if (GameMain.Server != null)
            {
                GameMain.Server.KarmaManager.OnReactorMeltdown(item, blameOnBroken?.Character);
            }
#endif
        }

        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }
            character.AIController.SteeringManager.Reset();
            bool shutDown = objective.Option == "shutdown";

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
                            var containObjective = AIContainItems<Reactor>(container, character, objective, itemCount: 1, equip: true, removeEmpty: true, spawnItemIfNotFound: !character.IsOnPlayerTeam, dropItemOnDeselected: true);
                            containObjective.Completed += ReportFuelRodCount;
                            containObjective.Abandoned += ReportFuelRodCount;
                            character.Speak(TextManager.Get("DialogReactorFuel").Value, null, 0.0f, "reactorfuel".ToIdentifier(), 30.0f);

                            void ReportFuelRodCount()
                            {
                                if (!character.IsOnPlayerTeam) { return; }
                                if (character.Submarine != Submarine.MainSub) { return; }
                                int remainingFuelRods = Submarine.MainSub.GetItems(false).Count(i => i.HasTag("reactorfuel") && i.Condition > 1);
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
                        if (Item.ConditionPercentage <= 0 && AIObjectiveRepairItems.IsValidTarget(Item, character))
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

            bool prevAutoTemp = autoTemp;
            bool prevPowerOn = _powerOn;
            float prevFissionRate = TargetFissionRate;
            float prevTurbineOutput = TargetTurbineOutput;

            if (shutDown)
            {
                PowerOn = false;
                AutoTemp = false;
                TargetFissionRate = 0.0f;
                TargetTurbineOutput = 0.0f;
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
                        UpdateAutoTemp(MathHelper.Lerp(0.5f, 2.0f, degreeOfSuccess), 1.0f);
                    }
                }
#if CLIENT
                FissionRateScrollBar.BarScroll = FissionRate / 100.0f;
                TurbineOutputScrollBar.BarScroll = TurbineOutput / 100.0f;
#endif
                if (autoTemp != prevAutoTemp ||
                    prevPowerOn != _powerOn ||
                    Math.Abs(prevFissionRate - TargetFissionRate) > 1.0f ||
                    Math.Abs(prevTurbineOutput - TargetTurbineOutput) > 1.0f)
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
                            break;
                        }
                    }
                }
            }
        }

        public override void OnMapLoaded()
        {
            prevAvailableFuel = AvailableFuel;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "shutdown":
                    if (TargetFissionRate > 0.0f || TargetTurbineOutput > 0.0f)
                    {
                        PowerOn = false;
                        AutoTemp = false;
                        TargetFissionRate = 0.0f;
                        TargetTurbineOutput = 0.0f;
                        registerUnsentChanges();
                    }
                    break;
                case "set_fissionrate":
                    if (PowerOn && float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float newFissionRate))
                    {
                        signalControlledTargetFissionRate = MathHelper.Clamp(newFissionRate, 0.0f, 100.0f);
                        lastReceivedFissionRateSignalTime = Timing.TotalTime;
                        registerUnsentChanges();
                    }
                    break;
                case "set_turbineoutput":
                    if (PowerOn && float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float newTurbineOutput))
                    {
                        signalControlledTargetTurbineOutput = MathHelper.Clamp(newTurbineOutput, 0.0f, 100.0f);
                        lastReceivedTurbineOutputSignalTime = Timing.TotalTime;
                        registerUnsentChanges();
                    }
                    break;
            }

            void registerUnsentChanges()
            {
                if (GameMain.NetworkMember is { IsServer: true }) { unsentChanges = true; }
            }
        }

        private float GetMaxOutput() => item.StatManager.GetAdjustedValue(ItemTalentStats.ReactorMaxOutput, MaxPowerOutput);
        private float GetFuelConsumption() => item.StatManager.GetAdjustedValue(ItemTalentStats.ReactorFuelEfficiency, fuelConsumptionRate);
    }
}
