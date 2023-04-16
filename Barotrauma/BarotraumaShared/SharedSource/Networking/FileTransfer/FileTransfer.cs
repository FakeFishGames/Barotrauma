namespace Barotrauma.Networking
{
    enum FileTransferStatus
    {
        NotStarted, Sending, Receiving, Finished, Canceled, Error
    }

    enum FileTransferMessageType
    {
        Unknown, Initiate, Data, TransferOnSameMachine, Cancel
    }

    enum FileTransferType
    {
        Submarine, CampaignSave, Mod
    }
}
