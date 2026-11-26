using Barotrauma;

namespace HomeAndHarbor;

internal partial class CollectOutpostMoneyAction
{
    private partial void GiveToCrewBank(PersistentOutpostData outpostData, int amount)
    {
        if (GameMain.GameSession?.Campaign is { } campaign)
        {
            campaign.Bank.Give(amount);
            outpostData.ServerWriteState(); // Sync the new wallet state to clients
        }
    }
}
