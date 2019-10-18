using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalEntityTransformation : Goal
        {
            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[startingentity]", "[transformedentity]", "[catalystitem]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { startingEntityName, transformedEntityName, catalystItemName });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private string startingEntityIdentifier, transformedEntityIdentifier, catalystItemIdentifier;
            private string startingEntityName, transformedEntityName, catalystItemName;
            private EntityTypes startingEntityType, transformedEntityType;

            private Entity startingEntity;
            private Vector2 startingEntitySavedPosition;
            private const float gracePeriod = 1f;
            private const float graceDistance = 200f;
            private float graceTimer;
            private double transformationTime = 0.0;

            private enum EntityTypes { Character, Item }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = HasTransformed(deltaTime);
            }

            public override bool CanBeCompleted(ICollection<Traitor> traitors)
            {
                return graceTimer <= gracePeriod;
            }

            private bool HasTransformed(float deltaTime)
            {
                if (startingEntity != null && !startingEntity.Removed)
                {
                    startingEntitySavedPosition = startingEntity.WorldPosition;
                }
                else
                {
                    if (transformationTime == 0.0)
                    {
                        transformationTime = Timing.TotalTime;
                    }
                    graceTimer += deltaTime;

                    switch (transformedEntityType)
                    {
                        case EntityTypes.Character:
                            foreach (Character character in Character.CharacterList)
                            {
                                if (character.Submarine == null || Traitors.All(t => character.Submarine.TeamID != t.Character.TeamID) || character.SpawnTime + gracePeriod < transformationTime)
                                {
                                    continue;
                                }
                                if (character.SpeciesName.ToLowerInvariant() == transformedEntityIdentifier && Vector2.Distance(startingEntitySavedPosition, character.WorldPosition) < graceDistance)
                                {
                                    return true;
                                }
                            }
                            break;
                        case EntityTypes.Item:
                            foreach (Item item in Item.ItemList)
                            {
                                if (item.Submarine == null || Traitors.All(t => item.Submarine.TeamID != t.Character.TeamID) || item.SpawnTime + gracePeriod < transformationTime)
                                {
                                    continue;
                                }
                                if (item.prefab.Identifier == transformedEntityIdentifier && Vector2.Distance(startingEntitySavedPosition, item.WorldPosition) < graceDistance)
                                {
                                    return true;
                                }
                            }
                            break;
                    }
                }

                return false;
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }

                startingEntityName = TextManager.FormatServerMessage(GetTextId(startingEntityType, startingEntityIdentifier)) ?? startingEntityIdentifier;
                transformedEntityName = TextManager.FormatServerMessage(GetTextId(transformedEntityType, transformedEntityIdentifier)) ?? transformedEntityIdentifier;
                catalystItemName = TextManager.FormatServerMessage(GetTextId(EntityTypes.Item, catalystItemIdentifier)) ?? catalystItemIdentifier;

                startingEntity = null;

                switch (startingEntityType)
                {
                    case EntityTypes.Character:
                        // Not used
                        break;
                    case EntityTypes.Item:
                        foreach (Item item in Item.ItemList)
                        {
                            if (item.Submarine == null || Traitors.All(t => item.Submarine.TeamID != t.Character.TeamID))
                            {
                                continue;
                            }
                            if (item.prefab.Identifier == startingEntityIdentifier)
                            {
                                startingEntity = item;
                                break;
                            }
                        }
                        break;
                }           

                graceTimer = 0.0f;
                return startingEntity != null;
            }

            private string GetTextId(EntityTypes type, string entityId)
            {
                string textId = null;
                switch (type)
                {
                    case EntityTypes.Character:
                        textId = $"character.{entityId}";
                        break;
                    case EntityTypes.Item:
                        textId = $"entityname.{entityId}";
                        break;
                }

                return textId;
            }

            public GoalEntityTransformation(string startingEntityIdentifier, string transformedEntityIdentifier, string startingEntityType, string transformedEntityType, string catalystItemIdentifier) : base()
            {
                this.startingEntityIdentifier = startingEntityIdentifier;
                this.startingEntityType = (EntityTypes)Enum.Parse(typeof(EntityTypes), startingEntityType, true);

                this.transformedEntityIdentifier = transformedEntityIdentifier.ToLowerInvariant();
                this.transformedEntityType = (EntityTypes)Enum.Parse(typeof(EntityTypes), transformedEntityType, true);

                this.catalystItemIdentifier = catalystItemIdentifier;
            }
        }
    }
}
