using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    partial class Character
    {
        public enum EventType
        {
            InventoryState = 0,
            Control = 1,
            Status = 2,
            Treatment = 3,
            SetAttackTarget = 4,
            ExecuteAttack = 5,
            AssignCampaignInteraction = 6,
            ObjectiveManagerState = 7,
            TeamChange = 8,
            AddToCrew = 9,
            UpdateExperience = 10,
            UpdateTalents = 11,
            UpdateSkills = 12,
            UpdateMoney = 13,
            UpdatePermanentStats = 14,
            RemoveFromCrew = 15,
            
            MinValue = 0,
            MaxValue = 15
        }
        
        private interface IEventData : NetEntityEvent.IData
        {
            public EventType EventType { get; }
        }

        public struct InventoryStateEventData : IEventData
        {
            public EventType EventType => EventType.InventoryState;
        }
        
        public struct ControlEventData : IEventData
        {
            public EventType EventType => EventType.Control;
            public readonly Client Owner;
            
            public ControlEventData(Client owner)
            {
                Owner = owner;
            }
        }

        public struct CharacterStatusEventData : IEventData
        {
            public EventType EventType => EventType.Status;

#if SERVER
            public bool ForceAfflictionData;

            public CharacterStatusEventData(bool forceAfflictionData)
            {
                ForceAfflictionData = forceAfflictionData;
            }
#endif
        }

        public struct TreatmentEventData : IEventData
        {
            public EventType EventType => EventType.Treatment;
        }

        private interface IAttackEventData : IEventData
        {
            public Limb AttackLimb { get; }
            public IDamageable TargetEntity { get; }
            public Limb TargetLimb { get; }
            public Vector2 TargetSimPos { get; }
        }

        public struct SetAttackTargetEventData : IAttackEventData
        {
            public EventType EventType => EventType.SetAttackTarget;
            public Limb AttackLimb { get; }
            public IDamageable TargetEntity { get; }
            public Limb TargetLimb { get; }
            public Vector2 TargetSimPos { get; }
            
            public SetAttackTargetEventData(Limb attackLimb, IDamageable targetEntity, Limb targetLimb, Vector2 targetSimPos)
            {
                AttackLimb = attackLimb;
                TargetEntity = targetEntity;
                TargetLimb = targetLimb;
                TargetSimPos = targetSimPos;
            }
        }

        public struct ExecuteAttackEventData : IAttackEventData
        {
            public EventType EventType => EventType.ExecuteAttack;
            public Limb AttackLimb { get; }
            public IDamageable TargetEntity { get; }
            public Limb TargetLimb { get; }
            public Vector2 TargetSimPos { get; }
            
            public ExecuteAttackEventData(Limb attackLimb, IDamageable targetEntity, Limb targetLimb, Vector2 targetSimPos)
            {
                AttackLimb = attackLimb;
                TargetEntity = targetEntity;
                TargetLimb = targetLimb;
                TargetSimPos = targetSimPos;
            }
        }

        public struct AssignCampaignInteractionEventData : IEventData
        {
            public EventType EventType => EventType.AssignCampaignInteraction;
        }

        public struct ObjectiveManagerStateEventData : IEventData
        {
            public EventType EventType => EventType.ObjectiveManagerState;
            public readonly AIObjectiveManager.ObjectiveType ObjectiveType;
            
            public ObjectiveManagerStateEventData(AIObjectiveManager.ObjectiveType objectiveType)
            {
                ObjectiveType = objectiveType;
            }
        }
        
        private struct TeamChangeEventData : IEventData
        {
            public EventType EventType => EventType.TeamChange;
        }

        [NetworkSerialize]
        public readonly record struct ItemTeamChange(CharacterTeamType TeamId, ImmutableArray<UInt16> ItemIds) : INetSerializableStruct;


        public struct AddToCrewEventData : IEventData
        {
            public EventType EventType => EventType.AddToCrew;
            public readonly ItemTeamChange ItemTeamChange;
            
            public AddToCrewEventData(CharacterTeamType teamType, IEnumerable<Item> inventoryItems)
            {
                ItemTeamChange = new ItemTeamChange(teamType, inventoryItems.Select(it => it.ID).ToImmutableArray());
            }            
        }

        public struct RemoveFromCrewEventData : IEventData
        {
            public EventType EventType => EventType.RemoveFromCrew;
            public readonly ItemTeamChange ItemTeamChange;

            public RemoveFromCrewEventData(CharacterTeamType teamType, IEnumerable<Item> inventoryItems)
            {
                ItemTeamChange = new ItemTeamChange(teamType, inventoryItems.Select(it => it.ID).ToImmutableArray());
            }
        }

        public struct UpdateExperienceEventData : IEventData
        {
            public EventType EventType => EventType.UpdateExperience;
        }

        public struct UpdateTalentsEventData : IEventData
        {
            public EventType EventType => EventType.UpdateTalents;
        }

        public struct UpdateSkillsEventData : IEventData
        {
            public EventType EventType => EventType.UpdateSkills;
        }
        
        private struct UpdateMoneyEventData : IEventData
        {
            public EventType EventType => EventType.UpdateMoney;
        }

        public struct UpdatePermanentStatsEventData : IEventData
        {
            public EventType EventType => EventType.UpdatePermanentStats;
            public readonly StatTypes StatType;
            
            public UpdatePermanentStatsEventData(StatTypes statType)
            {
                StatType = statType;
            }
        }
    }
}
