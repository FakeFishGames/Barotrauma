using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Linq;

namespace Barotrauma.Items.Components
{
    public partial class Reactor : ReactorBase, IServerSerializable, IClientSerializable
    {
        const float TemperatureBoostAmount = 25;

        //the rate at which the reactor is being run on (higher rate -> higher temperature)
        private float fissionRate;
        
        //how much of the generated steam is used to spin the turbines and generate power
        private float turbineOutput;
        
        private float temperature;
        
        private float meltDownTimer, meltDownDelay;
        private float fireTimer, fireDelay;

        private Vector2 optimalTemperature, allowedTemperature;
        private Vector2 optimalFissionRate, allowedFissionRate;
        private Vector2 optimalTurbineOutput, allowedTurbineOutput;

        private float? signalControlledTargetFissionRate, signalControlledTargetTurbineOutput;
        private double lastReceivedFissionRateSignalTime, lastReceivedTurbineOutputSignalTime;

        private float temperatureBoost;

        public bool AllowTemperatureBoost => Math.Abs(temperatureBoost) < TemperatureBoostAmount * 0.9f;

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

        [Serialize(false, IsPropertySaveable.Yes, description: "Is the temperature currently critical. Intended to be used by StatusEffect conditionals (setting the value from XML has no effect).")]
        public bool TemperatureCritical
        {
            get { return temperature > allowedTemperature.Y; }
            set { /*do nothing*/ }
        }

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

        public bool MeltedDownThisRound { get; private set; }

        protected override float TargetPowerGenerationRate { get => TargetFissionRate; set => TargetFissionRate = value; }
        protected override float TargetOutputControlRate { get => TargetTurbineOutput; set => TargetTurbineOutput = value; }

        protected override float PowerGenerationRate { get => FissionRate; set => FissionRate = value; }

        protected override float OutputControlRate { get => TurbineOutput; set => TurbineOutput = value; }

        public override bool CurrentlyControlledBySignals =>
            (signalControlledTargetFissionRate.HasValue && lastReceivedFissionRateSignalTime > Timing.TotalTime - 1) ||
            (signalControlledTargetTurbineOutput.HasValue && lastReceivedTurbineOutputSignalTime > Timing.TotalTime - 1);

        /// <summary>
        /// Reactors deteriorate when powered up (generating heat/power)
        /// </summary>
        public override bool ShouldCauseDeterioration => Temperature > 0.1f;

        public Reactor(Item item, ContentXElement element)
            : base(item, element)
        {
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
                            (AutoTemp ? ", Autotemp ON" : ", Autotemp OFF"),
                            ServerLog.MessageType.ItemInteraction);

                    nextServerLogWriteTime = null;
                    lastServerLogWriteTime = (float)Timing.TotalTime;
                }
            }
#endif

            base.Update(deltaTime, cam);

#if CLIENT
            if (PowerOn && AvailableFuel < 1)
            {
                HintManager.OnReactorOutOfFuel(Item, AvailableFuel);
            }
#endif

            float maxPowerOut = GetMaxOutput();
            bool fissionRateControlledBySignals = signalControlledTargetFissionRate.HasValue && lastReceivedFissionRateSignalTime > Timing.TotalTime - 1;
            bool turbineOutputRateControlledBySignals = signalControlledTargetTurbineOutput.HasValue && lastReceivedTurbineOutputSignalTime > Timing.TotalTime - 1;

            if (fissionRateControlledBySignals)
            {
                TargetFissionRate = AdjustValueWithoutOverShooting(TargetFissionRate, signalControlledTargetFissionRate.Value, deltaTime * SignalControlledAdjustmentSpeed);
#if CLIENT
                FissionRateScrollBar.BarScroll = TargetFissionRate / 100.0f;
#endif
            }
            else
            {
                signalControlledTargetFissionRate = null;
            }
            if (turbineOutputRateControlledBySignals)
            {
                TargetTurbineOutput = AdjustValueWithoutOverShooting(TargetTurbineOutput, signalControlledTargetTurbineOutput.Value, deltaTime * SignalControlledAdjustmentSpeed);                
#if CLIENT
                TurbineOutputScrollBar.BarScroll = TargetTurbineOutput / 100.0f;
#endif
            }
            else
            {
                signalControlledTargetTurbineOutput = null;
            }

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
            temperatureBoost = AdjustValueWithoutOverShooting(temperatureBoost, 0.0f, deltaTime);
#if CLIENT
            temperatureBoostUpButton.Enabled = temperatureBoostDownButton.Enabled = AllowTemperatureBoost;
#endif

            TurbineOutput = MathHelper.Lerp(turbineOutput, TargetTurbineOutput, deltaTime);

            item.SendSignal(((int)(temperature * 100.0f)).ToString(), "temperature_out");

            UpdateFailures(deltaTime);
            AvailableFuel = 0.0f;
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

        private float GetGeneratedHeat(float fissionRate)
        {
            float fissionHeat = fissionRate * (prevAvailableFuel / 100f) * 2f;
            float currentTemperatureBoost = Math.Min(temperatureBoost, fissionHeat);
            return fissionHeat + currentTemperatureBoost;
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

        public override bool TooMuchFuel()
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
                if (fireTimer > Math.Min(5.0f, FireDelay / 2) && blameOnBroken?.Character != null)
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

        public override void UpdateAutoTemp(float speed, float deltaTime, Character operatingCharacter = null)
        {
            if (operatingCharacter != null)
            {
                speed *= MathHelper.Lerp(0.5f, 2.0f, degreeOfSuccess);
            }

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

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);

            item.SendSignal(((int)(temperature * 100.0f)).ToString(), "temperature_out");
            Temperature -= deltaTime * 1000.0f;
        }

        private void MeltDown()
        {
            if (item.Condition <= 0.0f) { return; }
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (!ExplosionDamagesOtherSubs)
            {
                foreach (StatusEffect statusEffect in GetStatusEffectsOfType(ActionType.OnBroken))
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
            MeltedDownThisRound = true;
            BreakContainedItems();

#if SERVER
            GameServer.Log("Reactor meltdown!", ServerLog.MessageType.ItemInteraction);
            if (GameMain.Server != null)
            {
                GameMain.Server.KarmaManager.OnReactorMeltdown(item, blameOnBroken?.Character);
            }
#endif
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            base.ReceiveSignal(signal, connection);

            switch (connection.Name)
            {
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
    }
}
