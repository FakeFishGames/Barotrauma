namespace Barotrauma.Networking
{
    public enum FileTransferStatus
    {
        NotStarted, Sending, Receiving, Finished, Canceled, Error
    }

    enum FileTransferMessageType
    {
        Unknown, Initiate, Data, TransferOnSameMachine, Cancel
    }

    public enum FileTransferType
    {
        Submarine, CampaignSave, Mod
    }
}
