using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal readonly record struct OpenOutpostManagementUIData() : INetSerializableStruct
{
    public void Serialize(IWriteMessage msg) { }

    public void Deserialize(IReadMessage msg) { }
}

[NetworkSerialize]
internal readonly record struct ShowOutpostMessageData(string MessageText, ushort SpeakerId) : INetSerializableStruct;

[NetworkSerialize]
internal readonly record struct OpenCampaignInteractionData(CampaignMode.InteractionType InteractionType, ushort SpeakerId) : INetSerializableStruct;
