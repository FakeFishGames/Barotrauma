using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Conversation action that lets the player collect the money accumulated to the outpost wallet, transferring it into their wallet or the crew's bank.
/// </summary>
internal partial class CollectOutpostMoneyAction(ScriptedEvent parentEvent, ContentXElement element) : OutpostConversationActionBase(parentEvent, element)
{
    protected override LocalizedString BuildMessageAndApply(PersistentOutpostData outpostData, Location location)
    {
        int amount = outpostData.OutpostWalletBalance;
        if (amount <= 0)
        {
            return TextManager.Get("outpostmanagermenu.collectmoney.nothing");
        }

        outpostData.OutpostWalletBalance = 0;
        GiveToCrewBank(outpostData, amount);

        return TextManager.GetWithVariable("outpostmanagermenu.collectmoney.collected",
            "[amount]", TextManager.FormatCurrency(amount, includeCurrencySymbol: false).Value);
    }

    /// <summary>
    /// Give the collected money to the crew's shared bank (server) or the player's wallet (singleplayer), syncing as needed.
    /// Note that we deliberately don't require any permissions in MP for this, the money always goes into the bank anyway.
    /// </summary>
    private partial void GiveToCrewBank(PersistentOutpostData outpostData, int amount);
}
