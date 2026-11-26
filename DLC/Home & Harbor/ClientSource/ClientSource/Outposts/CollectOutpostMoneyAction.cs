using Barotrauma;

namespace HomeAndHarbor;

internal partial class CollectOutpostMoneyAction
{
    // NOTE: The client-side code for this action only runs in singleplayer, so the money goes straight into the player's own wallet.
    private partial void GiveToCrewBank(PersistentOutpostData outpostData, int amount)
    {
        if (GameMain.GameSession?.Campaign is { } campaign)
        {
            campaign.Wallet.Give(amount);
        }
    }
}
