using System;
using System.Collections.Generic;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class Item
    {
        public enum EventType
        {
            ComponentState = 0,
            InventoryState = 1,
            Treatment = 2,
            ChangeProperty = 3,
            Combine = 4,
            Status = 5,
            AssignCampaignInteraction = 6,
            ApplyStatusEffect = 7,
            Upgrade = 8,
            ItemStat = 9,
            
            MinValue = 0,
            MaxValue = 9
        }

        public interface IEventData : NetEntityEvent.IData
        {
            public EventType EventType { get; }
        }

        public struct ComponentStateEventData : IEventData
        {
            public EventType EventType => EventType.ComponentState;
            public readonly ItemComponent Component;
            public readonly ItemComponent.IEventData ComponentData;

            public ComponentStateEventData(ItemComponent component, ItemComponent.IEventData componentData)
            {
                Component = component;
                ComponentData = componentData;
            }
        }

        public readonly struct InventoryStateEventData : IEventData
        {
            public EventType EventType => EventType.InventoryState;
            public readonly ItemContainer Component;
            
            public InventoryStateEventData(ItemContainer component)
            {
                Component = component;
            }
        }

        public readonly struct ChangePropertyEventData : IEventData
        {
            public EventType EventType => EventType.ChangeProperty;
            public readonly SerializableProperty SerializableProperty;
            public readonly ISerializableEntity Entity;

            public ChangePropertyEventData(SerializableProperty serializableProperty, ISerializableEntity entity)
            {
                SerializableProperty = serializableProperty;
                Entity = entity;
            }
        }

        public readonly struct SetItemStatEventData : IEventData
        {
            public EventType EventType => EventType.ItemStat;

            public readonly Dictionary<ItemStatManager.TalentStatIdentifier, float> Stats;

            public SetItemStatEventData(Dictionary<ItemStatManager.TalentStatIdentifier, float> stats)
            {
                Stats = stats;
            }
        }

        private readonly struct ItemStatusEventData : IEventData
        {
            public EventType EventType => EventType.Status;
        }
        
        private readonly struct AssignCampaignInteractionEventData : IEventData
        {
            public EventType EventType => EventType.AssignCampaignInteraction;
        }

        public readonly struct ApplyStatusEffectEventData : IEventData
        {
            public EventType EventType => EventType.ApplyStatusEffect;
            public readonly ActionType ActionType;
            public readonly ItemComponent TargetItemComponent;
            public readonly Character TargetCharacter;
            public readonly Limb TargetLimb;
            public readonly Entity UseTarget;
            public readonly Vector2? WorldPosition;

            public ApplyStatusEffectEventData(
                ActionType actionType,
                ItemComponent targetItemComponent = null,
                Character targetCharacter = null,
                Limb targetLimb = null,
                Entity useTarget = null,
                Vector2? worldPosition = null)
            {
                ActionType = actionType;
                TargetItemComponent = targetItemComponent;
                TargetCharacter = targetCharacter;
                TargetLimb = targetLimb;
                UseTarget = useTarget;
                WorldPosition = worldPosition;
            }
        }

        private readonly struct UpgradeEventData : IEventData
        {
            public EventType EventType => EventType.Upgrade;
            public readonly Upgrade Upgrade;

            public UpgradeEventData(Upgrade upgrade)
            {
                Upgrade = upgrade;
            }
        }
    }
}
