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
            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[catalystitem]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { catalystItemName });

            private bool isCompleted;
            public override bool IsCompleted => isCompleted;

            private string catalystItemIdentifier, catalystItemName;

            private Vector2 activeEntitySavedPosition;
            private Entity activeEntity;
            private int activeEntityIndex;
            private const float gracePeriod = 1f;
            private const float graceDistance = 200f;
            private float graceTimer;
            private double transformationTime;

            private enum EntityTypes { Character, Item }

            private string[] entities;
            private EntityTypes[] entityTypes;

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
                if (activeEntity != null && !activeEntity.Removed)
                {
                    activeEntitySavedPosition = activeEntity.WorldPosition;
                }
                else
                {
                    if (transformationTime == 0)
                    {
                        graceTimer = 0.0f;
                        activeEntityIndex++;
                        transformationTime = Timing.TotalTime;
                    }
                    graceTimer += deltaTime;

                    switch (entityTypes[activeEntityIndex])
                    {
                        case EntityTypes.Character:
                            foreach (Character character in Character.CharacterList)
                            {
                                if (character.Submarine == null || Traitors.All(t => character.Submarine.TeamID != t.Character.TeamID) || character.SpawnTime + gracePeriod < transformationTime)
                                {
                                    continue;
                                }
                                if (character.SpeciesName.ToLowerInvariant() == entities[activeEntityIndex] && Vector2.Distance(activeEntitySavedPosition, character.WorldPosition) < graceDistance)
                                {
                                    activeEntity = character;
                                    transformationTime = 0.0;
                                    return activeEntityIndex == entities.Length - 1;
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
                                if (item.prefab.Identifier == entities[activeEntityIndex] && Vector2.Distance(activeEntitySavedPosition, item.WorldPosition) < graceDistance)
                                {
                                    activeEntity = item;
                                    transformationTime = 0.0;
                                    return activeEntityIndex == entities.Length - 1;
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

                catalystItemName = TextManager.FormatServerMessage($"entityname.{catalystItemIdentifier}");

                activeEntity = null;
                activeEntityIndex = 0;

                switch (entityTypes[activeEntityIndex])
                {
                    case EntityTypes.Character:
                        foreach (Character character in Character.CharacterList)
                        {
                            if (character.Submarine == null || Traitors.All(t => character.Submarine.TeamID != t.Character.TeamID))
                            {
                                continue;
                            }
                            if (character.SpeciesName.ToLowerInvariant() == entities[activeEntityIndex].ToLowerInvariant())
                            {
                                activeEntity = character;
                                break;
                            }
                        }
                        break;
                    case EntityTypes.Item:
                        foreach (Item item in Item.ItemList)
                        {
                            if (item.Submarine == null || Traitors.All(t => item.Submarine.TeamID != t.Character.TeamID))
                            {
                                continue;
                            }
                            if (item.prefab.Identifier.ToLowerInvariant() == entities[0].ToLowerInvariant())
                            {
                                activeEntity = item;
                                break;
                            }
                        }
                        break;
                }           

                graceTimer = 0.0f;
                return activeEntity != null;
            }        

            public GoalEntityTransformation(string[] entities, string[] entityTypes, string catalystItemIdentifier) : base()
            {
                this.entities = entities;

                this.entityTypes = new EntityTypes[entityTypes.Length];

                for (int i = 0; i < this.entityTypes.Length; i++)
                {
                    this.entityTypes[i] = (EntityTypes)Enum.Parse(typeof(EntityTypes), entityTypes[i], true);
                }

                this.catalystItemIdentifier = catalystItemIdentifier;
            }
        }
    }
}
