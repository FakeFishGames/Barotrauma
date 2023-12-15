using System;
using Barotrauma.Items.Components;

namespace Barotrauma;

[AttributeUsage(AttributeTargets.Property)]
sealed class ConditionallyEditable : Editable
{
    public ConditionallyEditable(ConditionType conditionType, bool onlyInEditors = true)
    {
        this.conditionType = conditionType;
        this.onlyInEditors = onlyInEditors;
    }
    private readonly ConditionType conditionType;

    private readonly bool onlyInEditors;

    public enum ConditionType
    {
        //These need to exist at compile time, so it is a little awkward
        //I would love to see a better way to do this
        AllowLinkingWifiToChat,
        IsSwappableItem,
        AllowRotating,
        Attachable,
        HasBody,
        Pickable,
        OnlyByStatusEffectsAndNetwork,
        HasIntegratedButtons,
        IsToggleableController,
        HasConnectionPanel
    }

    public bool IsEditable(ISerializableEntity entity)
    {
        if (onlyInEditors && Screen.Selected is { IsEditor: false }) { return false; }

        return conditionType switch
        {
            ConditionType.AllowLinkingWifiToChat
                => GameMain.NetworkMember is not { ServerSettings.AllowLinkingWifiToChat: false },
            ConditionType.IsSwappableItem
                => entity is Item item && item.Prefab.SwappableItem != null,
            ConditionType.AllowRotating
                => (entity is Item { body: null } item && item.Prefab.AllowRotatingInEditor)
                   || (entity is Structure structure && structure.Prefab.AllowRotatingInEditor),
            ConditionType.Attachable
                => entity is Holdable { Attachable: true },
            ConditionType.HasBody
                => entity is Structure { HasBody: true } or Item { body: not null },
            ConditionType.Pickable
                => entity is Item item && item.GetComponent<Pickable>() != null,
            ConditionType.OnlyByStatusEffectsAndNetwork
                => GameMain.NetworkMember is { IsServer: true },
            ConditionType.HasIntegratedButtons
                => entity is Door { HasIntegratedButtons: true },
            ConditionType.IsToggleableController
                => entity is Controller { IsToggle: true } controller && controller.Item.GetComponent<ConnectionPanel>() != null,
            ConditionType.HasConnectionPanel
                => (entity is Item item && item.GetComponent<ConnectionPanel>() != null)
                   || (entity is ItemComponent ic && ic.Item.GetComponent<ConnectionPanel>() != null),
            _
                => false
        };
    }
}
