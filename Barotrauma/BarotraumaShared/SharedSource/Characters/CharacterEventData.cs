using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

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
            
            MinValue = 0,
            MaxValue = 14
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

        public struct AddToCrewEventData : IEventData
        {
            public EventType EventType => EventType.AddToCrew;
            public readonly CharacterTeamType TeamType;
            public readonly ImmutableArray<Item> InventoryItems;
            
            public AddToCrewEventData(CharacterTeamType teamType, IEnumerable<Item> inventoryItems)
            {
                TeamType = teamType;
                InventoryItems = inventoryItems.ToImmutableArray();
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
