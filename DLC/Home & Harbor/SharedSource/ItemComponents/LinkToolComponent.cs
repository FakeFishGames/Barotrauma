using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

public partial class LinkToolComponent : ItemComponent, IServerSerializable, IClientSerializable
{
    private readonly record struct LinkEventData(Item First, Item Second, bool Unlink) : IEventData;

    [Serialize(1000f, IsPropertySaveable.No, description: "How far two items being linked are allowed to be?")]
    public float LinkMaximumDistance { get; set; }

    [Serialize(false, IsPropertySaveable.No, description: "Should characters that don't have the same team as the submarine be allowed to make links?")]
    public bool AllowLinkingDifferentTeam { get; set; }

    private readonly record struct ValidLink(Identifier[] First, Identifier[] Second, bool DisplaySideBySide, int MaxLinks);
    private readonly List<ValidLink> validLinks = [];

    public LinkToolComponent(Item item, ContentXElement element) : base(item, element)
    {
        IsActive = true;

        foreach (var subElement in element.Elements())
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "validlink":
                    validLinks.Add(new ValidLink()
                    {
                        First = subElement.GetAttributeIdentifierArray(nameof(ValidLink.First), []),
                        Second = subElement.GetAttributeIdentifierArray(nameof(ValidLink.Second), []),
                        DisplaySideBySide = subElement.GetAttributeBool(nameof(ValidLink.DisplaySideBySide), false),
                        MaxLinks = subElement.GetAttributeInt(nameof(ValidLink.MaxLinks), 3)
                    });
                    break;
            }
        }
    }

    private bool CanLinkToAny(Item item) 
    { 
        if (validLinks.None(link => item.HasIdentifierOrTags(link.First) || item.HasIdentifierOrTags(link.Second)))
        {
            return false;
        }

        if (!AllowLinkingDifferentTeam &&
            Character.Controlled != null &&
            (Character.Controlled.TeamID != item.Submarine.TeamID))
        {
            return false;
        }

        return true;
    }

    private bool TryGetLinkFromItems(Item first, Item second, out ValidLink validLink)
    {
        foreach (var link in validLinks)
        {
            bool normal = first.HasIdentifierOrTags(link.First) && second.HasIdentifierOrTags(link.Second);
            bool reversed = first.HasIdentifierOrTags(link.Second) && second.HasIdentifierOrTags(link.First);

            if (normal || reversed)
            {
                validLink = link;
                return true;
            }
        }

        validLink = default;
        return false;
    }

    /// <summary>
    /// Checks if two items can be linked, taking into account limits and outputting an error message.
    /// </summary>
    private bool CheckLinkLimits(Item first, Item second, bool unlink, out LocalizedString errorMessage, Character? usingCharacter = null)
    {
#if CLIENT
        usingCharacter = usingCharacter ?? user;
#endif

        if (!AllowLinkingDifferentTeam &&
            usingCharacter != null && 
            (usingCharacter.TeamID != first.Submarine.TeamID || usingCharacter.TeamID != second.Submarine.TeamID))
        {
            errorMessage = TextManager.Get("LinkFailHostileSub");
            return false;
        }

        if (!TryGetLinkFromItems(first, second, out ValidLink link))
        {
            errorMessage = TextManager.GetWithVariables("LinkFailInvalid", ("[item1]", first.Name), ("[item2]", second.Name));
            return false;
        }

        if (Vector2.Distance(first.WorldPosition, second.WorldPosition) > LinkMaximumDistance)
        {
            errorMessage = TextManager.GetWithVariables("LinkTooFar", ("[item1]", first.Name), ("[item2]", second.Name));
            return false;
        }

        // Only check if we are adding a new link
        if (!unlink)
        {
            if (first.linkedTo.Count >= link.MaxLinks)
            {
                errorMessage = TextManager.GetWithVariables("LinkLimitReached", ("[item]", first.Name), ("[limit]", link.MaxLinks.ToString()));
                return false;
            }

            if (second.linkedTo.Count >= link.MaxLinks)
            {
                errorMessage = TextManager.GetWithVariables("LinkLimitReached", ("[item]", second.Name), ("[limit]", link.MaxLinks.ToString()));
                return false;
            }
        }

        errorMessage = "";
        return true;
    }

    /// <summary>
    /// Links (or unlinks) two items, assumes that they form a valid link.
    /// </summary>
    public void LinkOrUnlinkItems(Item first, Item second, bool unlink, bool createNetworkEvent = false)
    {
        if (!TryGetLinkFromItems(first, second, out ValidLink link))
        {
            DebugConsole.ThrowError($"Tried to link items {first} {second}, but they don't belong to any valid link");
            return;
        }

        if (unlink)
        {
            first.linkedTo.RemoveAll(x => x == second);
            second.linkedTo.RemoveAll(x => x == first);
        }
        else
        {
            if (!first.linkedTo.Contains(second))
            {
                first.linkedTo.Add(second);
            }

            if (!second.linkedTo.Contains(first))
            {
                second.linkedTo.Add(first);
            }

            if (link.DisplaySideBySide)
            {
                first.DisplaySideBySideWhenLinked = true;
                second.DisplaySideBySideWhenLinked = true;
            }
        }

#if CLIENT
        if (createNetworkEvent)
        {
            Item.CreateClientEvent(this, new LinkEventData(first, second, unlink));
        }
#endif
    }
}