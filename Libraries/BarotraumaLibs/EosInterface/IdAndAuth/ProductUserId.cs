namespace Barotrauma;

public static partial class EosInterface
{
    /// <summary>
    /// A Product User ID is an EOS-specific ID that's linked to the SteamID or the Epic Account ID of a player.
    /// It is used to identify players in many of EOS' interfaces, most notably the P2P networking interface.
    /// <br /><br />
    /// A Product User ID used by Barotrauma is only valid for Barotrauma; other games that use EOS get their
    /// own separate set of Product User IDs.
    /// </summary>
    public readonly record struct ProductUserId(string Value);
}