namespace Barotrauma.Networking;

public readonly record struct ServerListContentPackageInfo(
    string Name, string Hash, Option<ContentPackageId> Id)
{
    public ServerListContentPackageInfo(ContentPackage pkg)
        : this(pkg.Name, pkg.Hash.StringRepresentation, pkg.UgcId) {}
            
    public static Option<ServerListContentPackageInfo> ParseSingleEntry(string singleEntry)
    {
        if (singleEntry.SplitEscaped(',') is not { Count: 3 } split) { return Option.None; }

        return Option.Some(
            new ServerListContentPackageInfo(
                split[0],
                split[1],
                ContentPackageId.Parse(split[2])));
    }

    public override string ToString()
        => new[] { Name, Hash, Id.Select(id => id.StringRepresentation).Fallback("") }
            .JoinEscaped(',');
}
