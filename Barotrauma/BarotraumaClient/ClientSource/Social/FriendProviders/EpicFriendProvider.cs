using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace Barotrauma;

sealed class EpicFriendProvider : FriendProvider
{
    private FriendInfo EgsFriendToFriendInfo(EosInterface.EgsFriend egsFriend)
    {
        return new FriendInfo(
            name: egsFriend.DisplayName,
            id: egsFriend.EpicAccountId,
            status: egsFriend.Status,
            serverName: egsFriend.ServerName,
            connectCommand: ConnectCommand.Parse(egsFriend.ConnectCommand),
            provider: this);
    }

    public override async Task<Option<FriendInfo>> RetrieveFriend(AccountId id)
    {
        if (id is not EpicAccountId friendEaid) { return Option.None; }
        var selfEaidOption = Eos.EosAccount.SelfAccountIds.OfType<EpicAccountId>().FirstOrNone();
        if (!selfEaidOption.TryUnwrap(out var selfEaid)) { return Option.None; }

        var friendResult = await EosInterface.Friends.GetFriend(selfEaid, friendEaid);
        if (!friendResult.TryUnwrapSuccess(out var f)) { return Option.None; }

        return Option.Some(EgsFriendToFriendInfo(f));
    }

    public override async Task<ImmutableArray<FriendInfo>> RetrieveFriends()
    {
        var epicAccountIdOption = Eos.EosAccount.SelfAccountIds.OfType<EpicAccountId>().FirstOrNone();
        if (!epicAccountIdOption.TryUnwrap(out var epicAccountId)) { return ImmutableArray<FriendInfo>.Empty; }

        var friendsResult = await EosInterface.Friends.GetFriends(epicAccountId);
        if (!friendsResult.TryUnwrapSuccess(out var friends)) { return ImmutableArray<FriendInfo>.Empty; }

        return friends.Select(EgsFriendToFriendInfo).ToImmutableArray();
    }

    private static readonly ImmutableArray<Color> egsProfileColors = new[]
    {
        // Cyan
        new Color(0xfff0a950),

        // Dark green
        new Color(0xff2b9850),

        // Yellow-green
        new Color(0xff2ba08e),

        // Purple
        new Color(0xff951249),

        // Purple-red
        new Color(0xff9a0c71),

        // Red
        new Color(0xff3e29c6),

        // Orange
        new Color(0xff3875ed),

        // Yellow-orange
        new Color(0xff1ea5ed)
    }.ToImmutableArray();

    public override Task<Option<Sprite>> RetrieveAvatar(FriendInfo friend, int avatarSize)
    {
        if (friend.Id is not EpicAccountId epicAccount) { return Task.FromResult<Option<Sprite>>(Option.None); }

        // EGS doesn't have profile pictures yet.
        // Instead, each player gets a color based on their account ID.
        // This is an educated guess of how Epic picks that color, and is likely incorrect for IDs nearing the boundaries of ranges:
        Color color = Color.Black;
        if (ulong.TryParse(epicAccount.EosStringRepresentation[..16], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mostSignificant64Bits)
            && ulong.TryParse(epicAccount.EosStringRepresentation[16..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var leastSignificant64Bits))
        {
            BigInteger fullId = mostSignificant64Bits;
            fullId <<= 64;
            fullId |= leastSignificant64Bits;

            BigInteger idMaxValue = ulong.MaxValue;
            idMaxValue <<= 64;
            idMaxValue |= ulong.MaxValue;

            BigInteger middleRangeSize = idMaxValue / 7;
            BigInteger firstRangeSize = middleRangeSize / 2;
            if (fullId <= firstRangeSize)
            {
                color = egsProfileColors[0];
            }
            else
            {
                color = egsProfileColors[(int)((fullId - firstRangeSize) / middleRangeSize) + 1];
            }
        }

        char glyphChar = friend.Name.FallbackNullOrEmpty("?")[0];
        var font = GUIStyle.UnscaledSmallFont.GetFontForStr(glyphChar.ToString());

        Texture2D tex = null;
        if (font != null)
        {
            var (glyphData, glyphTexture) = font.GetGlyphDataAndTextureForChar(glyphChar);
            var glyphSize = new Vector2(glyphData.TexCoords.Width, glyphData.TexCoords.Height);
            int texSize = (int)Math.Max(
                MathUtils.RoundUpToPowerOfTwo((uint)(font.LineHeight * 1.5f)),
                MathUtils.RoundUpToPowerOfTwo((uint)(font.LineHeight * 1.5f)));

            if (glyphTexture is not null)
            {
                var glyphTextureData = new Color[(int)glyphSize.X * (int)glyphSize.Y];
                glyphTexture.GetData(
                    level: 0,
                    rect: glyphData.TexCoords,
                    data: glyphTextureData,
                    startIndex: 0,
                    elementCount: glyphTextureData.Length);
                
                var texData = Enumerable.Range(0, texSize * texSize).Select(_ => color).ToArray();
                var start = (new Vector2(texSize, texSize) / 2 - glyphSize / 2).ToPoint();
                var end = start + glyphSize.ToPoint();

                for (int x = start.X; x < end.X; x++)
                {
                    for (int y = start.Y; y < end.Y; y++)
                    {
                        texData[x + y * texSize] =
                            Color.Lerp(
                                color,
                                Color.White,
                                glyphTextureData[(x - start.X) + (y - start.Y) * (int)glyphSize.X].A / 255f);
                    }
                }

                tex = new Texture2D(GameMain.GraphicsDeviceManager.GraphicsDevice, texSize, texSize);
                tex.SetData(texData);
            }
        }

        if (tex is null)
        {
            tex = new Texture2D(GameMain.GraphicsDeviceManager.GraphicsDevice, 2, 2);
            tex.SetData(new[] { color, color, color, color });
        }
        
        
        var sprite = new Sprite(tex, null, null);
        return Task.FromResult(Option.Some(sprite));
    }

    public override async Task<string> GetSelfUserName()
    {
        var epicAccountIdOption = Eos.EosAccount.SelfAccountIds.OfType<EpicAccountId>().FirstOrNone();
        if (!epicAccountIdOption.TryUnwrap(out var epicAccountId)) { return ""; }

        var selfInfoResult = await EosInterface.Friends.GetSelfUserInfo(epicAccountId);
        if (!selfInfoResult.TryUnwrapSuccess(out var selfInfo)) { return ""; }

        return selfInfo.DisplayName;
    }
}