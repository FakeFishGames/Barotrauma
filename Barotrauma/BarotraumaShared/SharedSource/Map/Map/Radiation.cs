#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal partial class Radiation : ISerializableEntity
    {
        public string Name => nameof(Radiation);

        [Serialize(defaultValue: 0f, isSaveable: true)]
        public float Amount { get; set; }

        [Serialize(defaultValue: true, isSaveable: true)]
        public bool Enabled { get; set; }

        public Dictionary<string, SerializableProperty> SerializableProperties { get; }

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

            foreach (Location location in Map.Locations.Where(Contains))
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

                if (IsEntityRadiated(character))
                {
                    health.ApplyAffliction(null, new Affliction(AfflictionPrefab.RadiationSickness, Params.RadiationDamageAmount));
                }
            }
        }

        public bool Contains(Location location)
        {
            return Contains(location.MapPosition);
        }

        public bool Contains(Vector2 pos)
        {
            return pos.X < Amount;
        }

        public bool IsEntityRadiated(Entity entity)
        {
            if (!Enabled) { return false; }
            if (Level.Loaded is { Type: LevelData.LevelType.LocationConnection, StartLocation: { } startLocation, EndLocation: { } endLocation } level)
            {
                if (Contains(startLocation) && Contains(endLocation)) { return true; }

                float distance = MathHelper.Clamp((entity.WorldPosition.X - level.StartPosition.X) / (level.EndPosition.X - level.StartPosition.X), 0.0f, 1.0f);
                var (startX, startY) = startLocation.MapPosition;
                var (endX, endY) = endLocation.MapPosition;
                Vector2 mapPos = new Vector2(startX + (endX - startX), startY + (endY - startY)) * distance;

                return Contains(mapPos);
            }

            return false;
        }

        public XElement Save()
        {
            XElement element = new XElement(nameof(Radiation));
            SerializableProperty.SerializeProperties(this, element, saveIfDefault: true);
            return element;
        }
    }
}