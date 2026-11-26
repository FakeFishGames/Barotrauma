using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor;

public partial class LinkToolComponent : IDrawableComponent
{
    public Vector2 DrawSize => item.Rect.Size.ToVector2();
    
    private Item? currentlyHighlightedItem;
    private Item? selectedItem;

    private readonly List<Item> cachedLinkItems = new List<Item>();
    private double lastLinkedItemsRefreshTimer = 0f;
    private const float LinkedItemsRefreshDelay = 1f;
    private const float UseDelay = 0.2f;

    private float useTimer;
    private Character? user;
    private Submarine? lastUserSubmarine;

    public override IEnumerable<Item> GetEquippedItemHighlights(Character character)
    {
        return cachedLinkItems.Where(CanCurrentlyLink);
    }

    public void FindLinkItems()
    {
        cachedLinkItems.Clear();
        if (user?.Submarine != null)
        {
            cachedLinkItems.AddRange(user.Submarine.GetItems(false).Where(CanLinkToAny));
        }
    }

    public bool CanCurrentlyLink(Item item)
    {
        if (!validLinks.Any(link => item.HasIdentifierOrTags(link.First) || item.HasIdentifierOrTags(link.Second)))
        {
            return false;
        }

        if (selectedItem != null && !TryGetLinkFromItems(selectedItem, item, out ValidLink _))
        {
            return false;
        }

        return true;
    }

    public override bool Use(float deltaTime, Character? character = null)
    {
        if (useTimer > 0f) { return false; }

        useTimer = UseDelay;

        if (currentlyHighlightedItem == null)
        {
            // Reset selected link if the user just clicked outside
            selectedItem = null;
            FindLinkItems();
            if (character == Character.Controlled)
            {
                SoundPlayer.PlayUISound(GUISoundType.Decrease);
            }
            return false;
        }

        if (selectedItem != null && selectedItem != currentlyHighlightedItem)
        {
            // End the link
            bool unlinking = selectedItem.linkedTo.Contains(currentlyHighlightedItem);

            if (CheckLinkLimits(selectedItem, currentlyHighlightedItem, unlinking, out LocalizedString errorMessage))
            {
                LinkOrUnlinkItems(
                    first: selectedItem, 
                    second: currentlyHighlightedItem, 
                    unlink: unlinking, 
                    createNetworkEvent: true);

                SoundPlayer.PlayUISound(GUISoundType.Increase);
            }
            else
            {
                GUI.AddMessage(errorMessage, Color.Red, lifeTime: 5);
            }

            selectedItem = null;
        }
        else
        {
            // Begin the link
            selectedItem = currentlyHighlightedItem;

            if (character == Character.Controlled)
            {
                SoundPlayer.PlayUISound(GUISoundType.Select);
            }
        }

        FindLinkItems();

        return false;
    }

    public override void Equip(Character character)
    {
        user = character;
        FindLinkItems();
    }

    public override void Update(float deltaTime, Camera cam)
    {
        useTimer -= deltaTime;

        if (Item.GetRootInventoryOwner() is not Character character || character.Submarine == null || !character.HasEquippedItem(Item))
        {
            user = null;
            selectedItem = null;
            return;
        }

        user = character;

        // Reset selected link if the user switches submarines
        if (user.Submarine != lastUserSubmarine)
        {
            selectedItem = null;
            lastUserSubmarine = user.Submarine;
        }

        base.Update(deltaTime, cam);

        lastLinkedItemsRefreshTimer -= deltaTime;
        if (lastLinkedItemsRefreshTimer <= 0f)
        {
            FindLinkItems();
            lastLinkedItemsRefreshTimer = LinkedItemsRefreshDelay;
        }
    }

    public override void DrawHUD(SpriteBatch spriteBatch, Character character)
    {
        if (character == null) { return; }
        if (character.Submarine == null) { return; }

        base.DrawHUD(spriteBatch, character);

        if (selectedItem != null)
        {
            Vector2 selectedItemScreenPosition = GameMain.GameScreen.Cam.WorldToScreen(selectedItem.DrawPosition);

            GUI.DrawLine(spriteBatch, 
                start: selectedItemScreenPosition, 
                end: PlayerInput.MousePosition, 
                clr: GUIStyle.Green, 
                width: 3);

            GUI.DrawString(spriteBatch,
                pos: PlayerInput.MousePosition + Vector2.UnitX * GUI.IntScale(20),
                text: TextManager.GetWithVariable("CurrentlyLinkingItem", "[item]", selectedItem.Name),
                color: GUIStyle.TextColorNormal,
                backgroundColor: Color.Black * 0.7f,
                backgroundPadding: 2,
                font: GUIStyle.SmallFont,
                forceUpperCase: ForceUpperCase.No);
        }
        else
        {
            GUI.DrawString(spriteBatch,
                pos: PlayerInput.MousePosition + Vector2.UnitX * GUI.IntScale(20),
                text: TextManager.ParseInputTypes(TextManager.Get("LinkTip")),
                color: GUIStyle.TextColorNormal,
                backgroundColor: Color.Black * 0.7f,
                backgroundPadding: 2,
                font: GUIStyle.SmallFont,
                forceUpperCase: ForceUpperCase.No);
        }
    }

    public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
    {
        if (user == null || user != Character.Controlled)
        {
            return;
        }

        currentlyHighlightedItem = null;

        foreach (Item item in cachedLinkItems)
        {
            foreach (Item linkedTo in item.linkedTo.OfType<Item>())
            {
                Vector2 rectSize = Vector2.One * GUI.IntScale(10);

                GUI.DrawLine(spriteBatch, item.DrawPosition.FlipY(), linkedTo.DrawPosition.FlipY(), GUIStyle.Green, width: 3);
                GUI.DrawFilledRectangle(spriteBatch, new RectangleF(item.DrawPosition.FlipY() - rectSize / 2f, rectSize), Color.Red);
                GUI.DrawFilledRectangle(spriteBatch, new RectangleF(linkedTo.DrawPosition.FlipY() - rectSize / 2f, rectSize), Color.Red);
            }

            Rectangle rect = item.WorldRect;
            rect.Y -= rect.Height;

            if (rect.Contains(user.CursorWorldPosition))
            {
                float size = MathF.Max(rect.Width, rect.Height);

                if (CanCurrentlyLink(item))
                {
                    item.Draw(spriteBatch, editing: false, overrideColor: GUIStyle.Blue * 0.7f, overrideDepth: item.GetDrawDepth() - 0.01f);
                }
                else
                {
                    item.Draw(spriteBatch, editing: false, overrideColor: GUIStyle.Red * 0.7f, overrideDepth: item.GetDrawDepth() - 0.01f);
                }

                currentlyHighlightedItem = item;
            }
        }
    }

    public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
    {
        var linkEventData = ExtractEventData<LinkEventData>(extraData);

        msg.WriteUInt16(linkEventData.First.ID);
        msg.WriteUInt16(linkEventData.Second.ID);
        msg.WriteBoolean(linkEventData.Unlink);
    }

    public void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        UInt16 firstId = msg.ReadUInt16();
        UInt16 secondId = msg.ReadUInt16();
        bool unlink = msg.ReadBoolean();

        if (Entity.FindEntityByID(firstId) is not Item first)
        {
            DebugConsole.ThrowError($"Failed to find item with id {firstId} while performing a link.");
            return;
        }

        if (Entity.FindEntityByID(secondId) is not Item second)
        {
            DebugConsole.ThrowError($"Failed to find item with id {secondId} while performing a link.");
            return;
        }

        LinkOrUnlinkItems(first, second, unlink);
    }
}