#nullable enable

using Barotrauma;

namespace HomeAndHarbor;

internal partial class PersistentOutpostData : ISerializableEntity
{
    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, PersistentOutpostNetworkData>(Plugin.NetworkHeaders.UpdatePersistentOutpostData, ClientReadState);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, ResourcesNetworkData>(Plugin.NetworkHeaders.UpdateResources, ClientReadResources);
    }

    private static void ClientReadState(PersistentOutpostNetworkData data)
    {
        if (GameMain.GameSession?.Map is not { } map) 
        { 
            DebugConsole.ThrowError($"Failed to read {nameof(PersistentOutpostData)}, map is null.");
            return;
        }
        if (data.LocationIndex < 0 || data.LocationIndex >= map.Locations.Count)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(PersistentOutpostData)}, invalid location index.");
            return;
        }
        data.ApplyToPersistentOutpostData(GetPersistentOutpostData(map.Locations[data.LocationIndex]));
    }

    private static void ClientReadResources(ResourcesNetworkData data)
    {
        if (GameMain.GameSession?.Map is not { } map)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(PersistentOutpostData)}, map is null.");
            return;
        }
        if (data.LocationIndex < 0 || data.LocationIndex >= map.Locations.Count)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(PersistentOutpostData)}, invalid location index.");
            return;
        }
        data.ApplyToPersistentOutpostData(GetPersistentOutpostData(map.Locations[data.LocationIndex]));
    }
}