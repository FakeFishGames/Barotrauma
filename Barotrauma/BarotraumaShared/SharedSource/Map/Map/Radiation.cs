#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal partial class Radiation : ISerializableEntity
    {
        public string Name => nameof(Radiation);

        [Serialize(defaultValue: 0f, isSaveable: IsPropertySaveable.Yes)]
        public float Amount { get; set; }

        [Serialize(defaultValue: true, isSaveable: IsPropertySaveable.Yes)]
        public bool Enabled { get; set; }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        public readonly Map Map;
        public readonly RadiationParams Params;

        private float radiationTimer;

        private float increasedAmount;
        private float lastIncrease;

        public Radiation(Map map, RadiationParams radiationParams, XElement? element = null)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            Map = map;
            Params = radiationParams;
            radiationTimer = Params.RadiationDamageDelay;
            if (element == null)
            {
                Amount = Params.StartingRadiation;
            }
        }

        /// <summary>
        /// Advances the progress  of the radiation.
        /// </summary>
        /// <param name="steps"></param>
        public void OnStep(float steps = 1)
        {
            if (!Enabled) { return; }
            if (steps <= 0) { return; }

            float increaseAmount = Params.RadiationStep * steps;

            if (Params.MaxRadiation > 0 && Params.MaxRadiation < Amount + increaseAmount)
            {
                increaseAmount = Params.MaxRadiation - Amount;
            }

            IncreaseRadiation(increaseAmount);

            int amountOfOutposts = Map.Locations.Count(location => location.Type.HasOutpost && !location.IsCriticallyRadiated());

            foreach (Location location in Map.Locations.Where(l => DepthInRadiation(l) > 0))
            {
                if (location.IsGateBetweenBiomes)
                {
                    location.Connections.ForEach(c => c.Locked = false);
                    continue;
                }

                if (amountOfOutposts <= Params.MinimumOutpostAmount) { break; }

                if (Map.CurrentLocation is { } currLocation)
                {
                    // Don't advance on nearby locations to avoid buggy behavior
                    if (currLocation == location || currLocation.Connections.Any(lc => lc.OtherLocation(currLocation) == location)) { continue; }
                }

                bool wasCritical = location.IsCriticallyRadiated();

                location.TurnsInRadiation++;

                if (location.Type.HasOutpost && !wasCritical && location.IsCriticallyRadiated())
                {
                    location.ClearMissions();
                    amountOfOutposts--;
                }
            }
        }

        public void IncreaseRadiation(float amount)
        {
            Amount += amount;
            increasedAmount = lastIncrease = amount;
        }

        public void UpdateRadiation(float deltaTime)
        {
            if (!(GameMain.GameSession?.IsCurrentLocationRadiated() ?? false)) { return; }

            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            if (radiationTimer > 0)
            {
                radiationTimer -= deltaTime;
                return;
            }

            radiationTimer = Params.RadiationDamageDelay;

            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead || character.Removed || !(character.CharacterHealth is { } health)) { continue; }
                
                float depthInRadiation = DepthInRadiation(character);
                if (depthInRadiation > 0)
                {
                    AfflictionPrefab afflictionPrefab;
                    // Get the related affliction (if necessary, fall back to the traditional radiation sickness for slightly better backwards compatibility)
                    afflictionPrefab = AfflictionPrefab.JovianRadiation ?? AfflictionPrefab.RadiationSickness;
                    float currentAfflictionStrength = character.CharacterHealth.GetAfflictionStrengthByIdentifier(afflictionPrefab.Identifier);
                    
                    // Get Jovian radiation strength, and cancel out the affliction's strength change (meant for decaying it)
                    // (for simplicity, let's assume each Effect of the Affliction has the same strengthchange)
                    float addedStrength = Params.RadiationDamageAmount - afflictionPrefab.Effects.FirstOrDefault()?.StrengthChange ?? 0.0f;
                    
                    // Damage is applied periodically, so we must apply the total damage for the full period at once (after deducting strengthchange)
                    addedStrength *= Params.RadiationDamageDelay;
                    
                    // The JovianRadiation affliction has brackets of 25 strength determined by the multiplier (1x = 0-25, 2x = 25-50 etc.)
                    int multiplier = (int)Math.Ceiling(depthInRadiation / Params.RadiationEffectMultipliedPerPixelDistance);
                    float growthPotentialInBracket = (multiplier * 25) - currentAfflictionStrength;
                    if (growthPotentialInBracket > 0)
                    {
                        addedStrength = Math.Min(addedStrength, growthPotentialInBracket);
                        character.CharacterHealth.ApplyAffliction(
                            character.AnimController?.MainLimb,
                            afflictionPrefab.Instantiate(addedStrength));
                    }
                }
            }
        }

        public float DepthInRadiation(Location location)
        {
            return DepthInRadiation(location.MapPosition);
        }
        
        private float DepthInRadiation(Vector2 pos)
        {
            return Amount - pos.X;
        }

        public float DepthInRadiation(Entity entity)
        {
            if (!Enabled) { return 0; }
            if (Level.Loaded is { Type: LevelData.LevelType.LocationConnection, StartLocation: { } startLocation, EndLocation: { } endLocation } level)
            {
                // Approximate how far between the level start and end points the entity is on the map
                float distanceNormalized = MathHelper.Clamp((entity.WorldPosition.X - level.StartPosition.X) / (level.EndPosition.X - level.StartPosition.X), 0.0f, 1.0f);
                var (startX, startY) = startLocation.MapPosition;
                var (endX, endY) = endLocation.MapPosition;
                Vector2 mapPos = new Vector2(startX, startY) + (new Vector2(endX - startX, endY - startY) * distanceNormalized);

                return DepthInRadiation(mapPos);
            }

            return 0;
        }

        public XElement Save()
        {
            XElement element = new XElement(nameof(Radiation));
            SerializableProperty.SerializeProperties(this, element, saveIfDefault: true);
            return element;
        }
    }
}