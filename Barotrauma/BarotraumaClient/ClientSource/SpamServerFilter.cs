#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Barotrauma.IO;
using Barotrauma.Networking;
using RestSharp;
using XmlWriter = Barotrauma.IO.XmlWriter;

namespace Barotrauma
{
    public enum SpamServerFilterType
    {
        Invalid,
        NameEquals,
        NameContains,
        MessageEquals,
        MessageContains,
        PlayerCountLarger,
        PlayerCountExact,
        MaxPlayersLarger,
        MaxPlayersExact,
        GameModeEquals,
        PlayStyleEquals,
        Endpoint,
        LanguageEquals
    }

    internal readonly record struct SpamFilter(ImmutableHashSet<(SpamServerFilterType Type, string Value)> Filters)
    {
        public bool IsFiltered(ServerInfo info)
        {
            if (!Filters.Any()) { return false; }

            foreach (var (type, value) in Filters)
            {
                if (!IsFiltered(info, type, value)) { return false; }
            }

            return true;
        }

        private static bool IsFiltered(ServerInfo info, SpamServerFilterType type, string value)
        {
            string desc = info.ServerMessage,
                   name = info.ServerName;

            int.TryParse(value, out int parsedInt);

            return type switch
            {
                SpamServerFilterType.NameEquals => CompareEquals(name, value),
                SpamServerFilterType.NameContains => CompareContains(name, value),

                SpamServerFilterType.MessageEquals => CompareEquals(desc, value),
                SpamServerFilterType.MessageContains => CompareContains(desc, value),

                SpamServerFilterType.Endpoint => info.Endpoints.First().StringRepresentation.Equals(value, StringComparison.OrdinalIgnoreCase),

                SpamServerFilterType.PlayerCountLarger => info.PlayerCount > parsedInt,
                SpamServerFilterType.PlayerCountExact => info.PlayerCount == parsedInt,

                SpamServerFilterType.MaxPlayersLarger => info.MaxPlayers > parsedInt,
                SpamServerFilterType.MaxPlayersExact => info.MaxPlayers == parsedInt,

                SpamServerFilterType.GameModeEquals => info.GameMode == value,
                SpamServerFilterType.PlayStyleEquals => info.PlayStyle.ToIdentifier() == value,

                SpamServerFilterType.LanguageEquals => info.Language.Value == value,
                _ => false
            };

            static bool CompareEquals(string a, string b)
                => a.Equals(b, StringComparison.OrdinalIgnoreCase) || Homoglyphs.Compare(a, b);

            static bool CompareContains(string a, string b)
                => a.Contains(b, StringComparison.OrdinalIgnoreCase);
        }

        public XElement Serialize()
        {
            var element = new XElement("Filter");

            foreach (var (type, value) in Filters)
            {
                element.Add(new XAttribute(type.ToString().ToLowerInvariant(), value));
            }

            return element;
        }

        public static bool TryParse(XElement element, out SpamFilter filter)
        {
            var builder = ImmutableHashSet.CreateBuilder<(SpamServerFilterType Type, string Value)>();
            foreach (var attribute in element.Attributes())
            {
                if (!Enum.TryParse(attribute.Name.ToString(), ignoreCase: true, out SpamServerFilterType e))
                {
                    DebugConsole.ThrowError($"Failed to parse spam filter attribute \"{attribute.Name}\"");
                    continue;
                }
                if (e is SpamServerFilterType.Invalid) { continue; }
                builder.Add((e, attribute.Value));
            }

            if (builder.Any())
            {
                filter = new SpamFilter(builder.ToImmutable());
                return true;
            }

            filter = default;
            return false;
        }

        public override string ToString()
        {
            return !Filters.Any() ? "Invalid Filter" : string.Join(", ", Filters.Select(static f => $"{f.Type}: {f.Value}"));
        }
    }

    internal sealed class SpamServerFilter
    {
        public readonly ImmutableArray<SpamFilter> Filters;

        public bool IsFiltered(ServerInfo info)
        {
            foreach (var f in Filters)
            {
                if (f.IsFiltered(info)) { return true; }
            }

            return false;
        }

        public SpamServerFilter(XElement element)
        {
            var builder = ImmutableArray.CreateBuilder<SpamFilter>();
            foreach (var subElement in element.Elements())
            {
                if (SpamFilter.TryParse(subElement, out var filter))
                {
                    builder.Add(filter);
                }
            }
            Filters = builder.ToImmutable();
        }

        public SpamServerFilter(ImmutableArray<SpamFilter> filters)
            => Filters = filters;

        public readonly static string SavePath = Path.Combine("Data", "serverblacklist.xml");

        public void Save(string path)
        {
            var comment = new XComment(SpamServerFilters.LocalFilterComment);
            var doc = new XDocument(comment, new XElement("Filters"));
            foreach (var filter in Filters)
            {
                doc.Root?.Add(filter.Serialize());
            }

            try
            {
                using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true });
                doc.SaveSafe(writer);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving spam filter failed.", e);
            }
        }
    }

    internal static class SpamServerFilters
    {
        public static Option<SpamServerFilter> LocalSpamFilter;
        public static Option<SpamServerFilter> GlobalSpamFilter;

        public const string LocalFilterComment = @"
This file contains a list of filters that can be used to hide servers from the server list.
You can add filters by right-clicking a server in the server list and selecting ""Hide server"" or by reporting the server and choosing ""Report and hide server"".
The filters are saved in this file, which you can edit manually if you want to.

The available filter types are:
- NameEquals: The server name must equal the specified value. Homoglyphs are also checked.
- NameContains: The server name must contain the specified value.
- MessageEquals: The server description must equal the specified value. Homoglyphs are also checked.
- MessageContains: The server description must contain the specified value.
- PlayerCountLarger: The player count must be larger than the specified value.
- PlayerCountExact: The player count must match the specified value exactly.
- MaxPlayersLarger: The max player count must be larger than the specified value.
- MaxPlayersExact: The max player count must match the specified value exactly.
- GameModeEquals: The game mode identifier must match the specified value exactly.
- PlayStyleEquals: The play style must match the specified value exactly.
- Endpoint: The server endpoint, which is a Steam ID or an IP address, must match the specified value exactly. Steam ID is in the format of STEAM_X:Y:Z.
- LanguageEquals: The server language must match the specified value exactly.

The filter values are case-insensitive and adding multiple conditions on one filter will require all of them to be met.
Homoglyph comparison is used for NameEquals and MessageEquals filters, which means that it checks whether the words look the same, meaning you can't abuse identical-looking but different symbols to work around the filter. For example ""lmaobox"" and ""lmаobox"" (with a cyrillic a) are considered equal.

Examples:
<Filters>
  <Filter namecontains=""discord.gg"" />
  <Filter messagecontains=""discord.gg"" />
  <Filter nameequals=""get good get lmaobox"" maxplayersexact=""999"" />
</Filters>
These will hide all servers that have a discord.gg link in their name or description and servers with the name ""get good get lmaobox"" that have 999 max players.
";
        static SpamServerFilters()
        {
            XDocument? doc;
            if (!File.Exists(SpamServerFilter.SavePath))
            {
                var comment = new XComment(LocalFilterComment);

                doc = new XDocument(comment, new XElement("Filters"));

                try
                {
                    using var writer = XmlWriter.Create(SpamServerFilter.SavePath, new XmlWriterSettings { Indent = true });
                    doc.SaveSafe(writer);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Saving spam filter failed.", e);
                }
            }
            else
            {
                doc = XMLExtensions.TryLoadXml(SpamServerFilter.SavePath);
            }

            if (doc?.Root is { } root)
            {
                LocalSpamFilter = Option.Some(new SpamServerFilter(root));
            }
        }

        public static bool IsFiltered(ServerInfo info)
        {
            if (LocalSpamFilter.TryUnwrap(out var localFilter) && localFilter.IsFiltered(info)) { return true; }
            if (GlobalSpamFilter.TryUnwrap(out var globalFilter) && globalFilter.IsFiltered(info)) { return true; }
            return false;
        }

        public static void AddServerToLocalSpamList(ServerInfo info)
        {
            if (!LocalSpamFilter.TryUnwrap(out var localFilter)) { return; }
            if (localFilter.IsFiltered(info)) { return; }

            var filters = localFilter.Filters.Add(new SpamFilter(ImmutableHashSet.Create((NameExact: SpamServerFilterType.NameEquals, info.ServerName))));
            var newFilter = new SpamServerFilter(filters);
            newFilter.Save(SpamServerFilter.SavePath);
            LocalSpamFilter = Option.Some(newFilter);
        }

        public static void ClearLocalSpamFilter()
        {
            var newFilter = new SpamServerFilter(ImmutableArray<SpamFilter>.Empty);
            newFilter.Save(SpamServerFilter.SavePath);
            LocalSpamFilter = Option.Some(newFilter);
        }

        public static void RequestGlobalSpamFilter()
        {
            if (GameSettings.CurrentConfig.DisableGlobalSpamList) { return; }

            string remoteContentUrl = GameSettings.CurrentConfig.RemoteMainMenuContentUrl;
            if (string.IsNullOrEmpty(remoteContentUrl)) { return; }

            try
            {
                var client = new RestClient($"{remoteContentUrl}spamfilter")
                {
                    CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore)
                };
                client.AddDefaultHeader("Cache-Control", "no-cache");
                client.AddDefaultHeader("Pragma", "no-cache");
                var request = new RestRequest("serve_spamlist.php", Method.GET);
                TaskPool.Add("RequestGlobalSpamFilter", client.ExecuteAsync(request), RemoteContentReceived);
            }
            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("Fetching global spam list failed.", e);
#endif
                GameAnalyticsManager.AddErrorEventOnce("SpamServerFilters.RequestGlobalSpamFilter:Exception", GameAnalyticsManager.ErrorSeverity.Error,
                    "Fetching global spam list failed. " + e.Message);
            }

            static void RemoteContentReceived(Task t)
            {
                try
                {
                    if (!t.TryGetResult(out IRestResponse? remoteContentResponse)) { throw new Exception("Task did not return a valid result"); }
                    if (remoteContentResponse.StatusCode != HttpStatusCode.OK)
                    {
                        DebugConsole.AddWarning(
                            "Failed to receive global spam filter." +
                            "There may be an issue with your internet connection, or the master server might be temporarily unavailable " +
                            $"(error code: {remoteContentResponse.StatusCode})");
                        return;
                    }
                    string data = remoteContentResponse.Content;
                    if (string.IsNullOrWhiteSpace(data)) { return; }

                    if (XDocument.Parse(data).Root is { } root)
                    {
                        GlobalSpamFilter = Option.Some(new SpamServerFilter(root));
                    }
                }
                catch (Exception e)
                {
#if DEBUG
                    DebugConsole.ThrowError("Reading received global spam filter failed.", e);
#endif
                    GameAnalyticsManager.AddErrorEventOnce("SpamServerFilters.RemoteContentReceived:Exception", GameAnalyticsManager.ErrorSeverity.Error,
                        "Reading received global spam filter failed. " + e.Message);
                }
            }
        }
    }
}