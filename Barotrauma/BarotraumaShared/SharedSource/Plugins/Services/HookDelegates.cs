#nullable enable

using Barotrauma.Networking;
using System.Collections.Generic;
using System.Xml.Linq;
#if CLIENT
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma
{
    /// <summary>
    /// Called in the main game loop before the game logic is updated.
    /// </summary>
    public delegate void PluginOnPreUpdateDelegate(float deltaTime);

    /// <summary>
    /// Called in the main game loop after the game logic is updated.
    /// </summary>
    public delegate void PluginOnPostUpdateDelegate(float deltaTime);

    /// <summary>
    /// Called after a round has finished loading and starts.
    /// </summary>
    public delegate void PluginRoundStartedDelegate(GameSession gameSession);

    /// <summary>
    /// Called when a round ends.
    /// </summary>
    public delegate void PluginRoundEndedDelegate(GameSession gameSession);

    /// <summary>
    /// Called when a new campaign is started or loaded.
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    /// <param name="loadElement">The XML element that was loaded from the save file or None if a new campaign was started.</param>
    /// <param name="settings">The campaign settings of the campaign.</param>
    public delegate void PluginCampaignLoadDelegate(CampaignMode mode, Option<XElement> loadElement, CampaignSettings settings);

    /// <summary>
    /// Called when the campaign is saved.
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    /// <param name="save">
    /// The XML element that gets written to the save file.
    /// If you want to save custom data add it as a child to this element.
    /// </param>
    public delegate void PluginCampaignSaveDelegate(CampaignMode mode, XElement save);

    /// <summary>
    /// Called when the end of the campaign is reached, before the campaign map and metadata are reset for the next loop.
    /// Called both server-side and client-side in multiplayer.
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    public delegate void PluginCampaignEndDelegate(CampaignMode mode);

    /// <summary>
    /// Called when the game mode is updated.
    /// Only called when the game mode is running and not paused.
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    public delegate void PluginGameModeUpdateDelegate(GameMode mode, float deltaTime);

    public delegate bool PluginShouldDisableJovianRadiation(out RichString disabledTooltip);

    /// <summary>
    /// Called when adding game mode specific GUI elements to the update list. 
    /// Can be used to add custom GUI elements to the list.
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    public delegate void PluginGameModeAddToGUIUpdateListDelegate(GameMode mode);

    public delegate void PluginCampaignMapGenerationFinishedDelegate(CampaignMode campaign, Map map, List<Location> locations);

    public delegate void PluginLocationSaveDelegate(Location location, Map map, XElement locationElement);
    public delegate void PluginLocationLoadDelegate(Location location, CampaignMode campaign, XElement locationElement);

    /// <summary>
    /// Called when the player leaves an outpost in the campaign. Not invoked in outpost levels without an actual outpost "submarine".
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    public delegate void PluginCampaignOnLeaveOutpostDelegate(CampaignMode mode);

    /// <summary>
    /// Called when the player enters an outpost in the campaign. Not invoked in outpost levels without an actual outpost "submarine".
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    /// <param name="location">The location that the player is entering.</param>
    /// <param name="outpost">The outpost that the player is entering.</param>
    public delegate void PluginCampaignOnEnterOutpostDelegate(CampaignMode mode, Location location, Submarine outpost);

    /// <summary>
    /// Called when the outpost NPCs change during a round (a new NPC appears or another one disappears)
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    /// <param name="location">The location that the player is entering.</param>
    /// <param name="outpost">The outpost that the player is entering.</param>
    public delegate void PluginCampaignOnOutpostNPCsChangedDelegate(CampaignMode mode, Location location, Submarine outpost);

    /// <summary>
    /// Called when a multiplayer round has started and fully initialized.
    /// </summary>
    public delegate void PluginMultiplayerRoundStartedDelegate(GameSession gameSession);

    /// <summary>
    /// Called when the campaign world is progressed. Affects things such as daily deals, store refreshes and radiation.
    /// </summary>
    /// <param name="map">The campaign map instance.</param>
    /// <param name="campaign">The campaign mode instance.</param>
    public delegate void PluginOnProgressWorldDelegate(Map map, CampaignMode campaign);

    /// <summary>
    /// Called when a location changes its type.
    /// </summary>
    /// <param name="location">The location whose type changed.</param>
    /// <param name="oldType">The old type of the location.</param>
    public delegate void PluginOnLocationTypeChangedDelegate(Location location, LocationType oldType);

    /// <summary>
    /// Called when the player spends money at the current location for any purpose (merchants, submarines and upgrades, clinic, HR) except events.
    /// </summary>
    public delegate void PluginCampaignMoneySpentAtLocationDelegate(int price, Location? location);

    /// <summary>
    /// Called when calculating the item buy price in a location, can be used to add a custom multiplier to the price.
    /// </summary>
    public delegate float PluginCampaignItemBuyPriceMultiplier(Location.StoreInfo storeInfo, ItemPrefab itemPrefab, PriceInfo? priceInfo = null);

    /// <summary>
    /// Called when calculating the item sell price in a location, can be used to add a custom multiplier to the price.
    /// </summary>
    public delegate float PluginCampaignItemSellPriceMultiplier(Location.StoreInfo storeInfo, ItemPrefab itemPrefab, PriceInfo? priceInfo = null);

    /// <summary>
    /// Called when determining the item availability in a location, can be used to modify the item availability.
    /// </summary>
    public delegate int PluginCampaignItemModifyAvailability(Location location, ItemPrefab itemPrefab, PriceInfo priceInfo, int currentQuantity);

    /// <summary>
    /// Called when loading a store from a save. Can be used to load a store with custom behavior, see <see cref="Location.StoreInfo.CustomStoreType"/>.
    /// Return null to let the store load normally.
    /// </summary>
    public delegate Location.StoreInfo? PluginLoadStoreDelegate(Location location, XElement savedStoreElement);

    /// <summary>
    /// Called when an item is replaced with a new item.
    /// </summary>
    /// <param name="oldItem">The item that was replaced and removed.</param>
    /// <param name="newItem">The new item that was created.</param>
    /// <remarks>
    /// Avoid performing any operations that would modify the old item, as it is removed and might cause errors.
    /// </remarks>
    public delegate void PluginOnItemReplacedDelegate(Item oldItem, Item newItem);

    /// <summary>
    /// Called when checking if a submarine item can be swapped to. Return false to disallow the swap.
    /// Not called for swaps that have already been purchased, or for swapping back to the item's current prefab.
    /// </summary>
    /// <param name="item">The item that would be swapped.</param>
    /// <param name="replacement">The item the swap would replace the item with.</param>
    /// <param name="location">The location where the swap is done.</param>
    public delegate bool PluginCanSwapItemDelegate(Item item, ItemPrefab replacement, Location? location);

    /// <summary>
    /// Called after the constructor of the character has ran.
    /// </summary>
    /// <param name="character">The created character</param>
    public delegate void PluginOnCharacterCreated(Character character);

    /// <summary>
    /// Can be used to modify the mandatory conversation flags for the given situation. Used to e.g. enforce the EnterOutpost NPC conversations when entering an outpost.
    /// </summary>
    public delegate void PluginModifyRequiredNPCConversationFlags(List<Identifier> flags);

    /// <summary>
    /// Can be used to modify the conversation flags assigned to a specific NPC, to add support for custom conversations in specific situations.
    /// </summary>
    public delegate void PluginModifyCharacterSpecificNPCConversationFlags(Character character, List<Identifier> flags);

    /// <summary>
    /// Overrides the maximum submarine tier available for a location type.
    /// Return a value between 1 and 3 to set the highest available tier,
    /// or return -1 to use the default tier selection logic.
    /// </summary>
    public delegate int PluginSelectHighestSubmarineTierAvailable(SubmarineClass subClass, Identifier locationType);

    /// <summary>
    /// Called when a human successfully performs CPR on another character.
    /// </summary>
    public delegate void PluginHumanCPRSuccessDelegate(HumanoidAnimController humanAnimController, Character target);
    /// <summary>
    /// Called when a human fails to perform CPR on another character.
    /// </summary>
    public delegate void PluginHumanCPRFailedDelegate(HumanoidAnimController humanAnimController, Character target);

#if CLIENT
    /// <summary>
    /// Called when a character is added to or removed from the crew on the client side.
    /// This is called after the crew change network event is processed.
    /// </summary>
    /// <param name="character">The character that was added or removed.</param>
    /// <param name="isAdded">True if the character was added to crew, false if removed.</param>
    public delegate void PluginCrewChangedDelegate(Character character, bool isAdded);

    /// <summary>
    /// Called when the ESC key is pressed, before the default ESC handling logic.
    /// Return true to prevent the default ESC handling from running.
    /// </summary>
    /// <returns>True to consume the ESC key and prevent default handling, false to allow default handling.</returns>
    public delegate bool PluginHandleEscapeKeyDelegate();

    /// <summary>
    /// Called when the game mode overlays are drawn in <see cref="GameMode.Draw(SpriteBatch)"/>.
    /// </summary>
    /// <param name="mode">The game mode instance.</param>
    /// <param name="spriteBatch">The sprite batch that can be used to draw custom content.</param>
    public delegate void PluginGameModeDrawDelegate(GameMode mode, SpriteBatch spriteBatch);

    /// <summary>
    /// Called when the campaign map is updated in <see cref="Map.Update(CampaignMode,float,GUICustomComponent)"/>.
    /// </summary>
    /// <param name="mode">The campaign mode instance.</param>
    /// <param name="map">The campaign map instance.</param>
    /// <remarks>
    /// This is different from <see cref="PluginGameModeUpdateDelegate"/> as it is only called when the map
    /// is visible and in single player the game is paused when the map is open.
    /// </remarks>
    public delegate void PluginCampaignMapUpdateDelegate(CampaignMode mode, Map map, float deltaTime);
    /// <summary>
    /// Called at the beginning of the campaign draw method <see cref="Map.Draw(CampaignMode, SpriteBatch, GUICustomComponent)"/>, right after rendering the map background.
    /// Can be used to draw custom content on the map before the locations and other content on the map are drawn.
    /// </summary>
    public delegate void PluginCampaignMapBeforeDrawDelegate(CampaignMode campaign, Map map, SpriteBatch spriteBatch, GUICustomComponent mapContainer, float zoom, MapGenerationParams generationParams);
    /// <summary>
    /// Called at the end of the campaign draw method <see cref="Map.Draw(CampaignMode, SpriteBatch, GUICustomComponent)"/>, after everything on the map has been drawn.
    /// </summary>
    public delegate void PluginCampaignMapAfterDrawDelegate(CampaignMode campaign, Map map, SpriteBatch spriteBatch, GUICustomComponent mapContainer, float zoom, MapGenerationParams generationParams);
    public delegate void PluginCampaignMapDrawLocationDelegate(Location location, CampaignMode campaign, Map map, GUICustomComponent mapContainer, SpriteBatch spriteBatch, Vector2 mapIconPosition, float zoom, MapGenerationParams generationParams);

    public delegate void PluginCampaignSelectTabDelegate(CampaignUI campaignUI, CampaignMode.InteractionType selectedTab);
    public delegate void PluginCampaignDrawUIDelegate(CampaignMode campaign, CampaignUI campaignUI);

    /// <summary>
    /// Called when a map tooltip is created for a location on the campaign map in <see cref="Map.CreateLocationInfoOverlay(Location)"/>.
    /// </summary>
    /// <param name="location">The location that the tooltip is for.</param>
    /// <param name="parent">
    /// The GUI component that the tooltip is created in.
    /// If you want to add custom content to the tooltip, add it as a child to this component.
    /// </param>
    public delegate void PluginCreateLocationInfoOverlayDelegate(Location location, GUIComponent parent);

    /// <summary>
    /// Called when the background for an item in inventory slot is drawn in <see cref="Inventory.DrawSlot(SpriteBatch,Inventory,VisualSlot,Item,int,bool,InvSlotType)"/>.
    /// Useful for drawing custom backgrounds for items in inventory slots.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch that can be used to draw custom content.</param>
    /// <param name="inventory">The inventory that the item is in.</param>
    /// <param name="slot">The slot that the item is in.</param>
    /// <param name="item">The item that is in the slot.</param>
    /// <param name="slotIndex">The index of the slot.</param>
    /// <param name="drawItem">Whether the item should be drawn.</param>
    /// <param name="type">The type of the inventory slot.</param>
    public delegate void PluginDrawSlotBackgroundDelegate(SpriteBatch spriteBatch, Inventory inventory, VisualSlot slot, Item item, int slotIndex, bool drawItem, InvSlotType type);

    /// <summary>
    /// Called when the foreground for an item in inventory slot is drawn in <see cref="Inventory.DrawSlot(SpriteBatch,Inventory,VisualSlot,Item,int,bool,InvSlotType)"/>.
    /// Useful for drawing custom content on top of the item.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch that can be used to draw custom content.</param>
    /// <param name="inventory">The inventory that the item is in.</param>
    /// <param name="slot">The slot that the item is in.</param>
    /// <param name="item">The item that is in the slot.</param>
    /// <param name="slotIndex">The index of the slot.</param>
    /// <param name="drawItem">Whether the item should be drawn.</param>
    /// <param name="type">The type of the inventory slot.</param>
    public delegate void PluginDrawSlotForegroundDelegate(SpriteBatch spriteBatch, Inventory inventory, VisualSlot slot, Item item, int slotIndex, bool drawItem, InvSlotType type);

    /// <summary>
    /// Called when the tooltip for an item in inventory slot is created in <see cref="Inventory.SlotReference.GetTooltip(Item,System.Collections.Generic.IEnumerable{Barotrauma.Item},Character)"/>.
    /// </summary>
    /// <param name="existingTooltip">The existing tooltip that was created by vanilla game.</param>
    /// <param name="item">The item that the tooltip is for.</param>
    /// <param name="itemsInSlot">The items that are in the slot.</param>
    /// <param name="character">The currently controlled character.</param>
    public delegate void PluginItemSlotGetTooltipDelegate(ref LocalizedString existingTooltip, Item item, IEnumerable<Item> itemsInSlot, Character character);
    
    /// <summary>
    /// Called when the client starts receiving midround synchronization data from the server.
    /// </summary>
    public delegate void PluginMidRoundSyncReceiveStartedDelegate();
    
    /// <summary>
    /// Called when the client has completed receiving midround synchronization data from the server.
    /// </summary>
    public delegate void PluginMidRoundSyncReceiveCompletedDelegate();

#elif SERVER

    /// <summary>
    /// Called after a client has joined the round (enters the game screen, either mid-round or at the start of the round),
    /// and has been gotten in sync with the server.
    /// </summary>
    public delegate void PluginClientJoinedRoundDelegate(Client client);

    /// <summary>
    /// Called when a client has disconnected from the server.
    /// </summary>
    public delegate void PluginClientDisconnectedDelegate(Client client);
#endif
}
