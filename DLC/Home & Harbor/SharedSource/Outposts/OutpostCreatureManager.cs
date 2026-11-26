using Barotrauma;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace HomeAndHarbor;

public class OutpostCreatureManager
{
    public IReadOnlyList<OutpostCreatureInfo> CreatureInfos => creatureInfos;

    private const string SavedCreaturesElementName = "SavedCreatures";

    private readonly List<OutpostCreatureInfo> creatureInfos = [];

    private int lastWorldStepCount = 0;

    public void OnEnterOutpost(CampaignMode mode, Location location, Submarine outpost)
    {
        if (outpost == null) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        outpostData.CreatureManager?.SpawnCreatures(outpost);
    }

    public void OnLeaveOutpost(CampaignMode campaignMode)
    {
        if (GameMain.GameSession?.StartLocation == null) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(GameMain.GameSession.StartLocation);
        outpostData.CreatureManager?.RefreshCreatures();
    }

    /// <summary>
    /// Gets the OutpostCreatureInfo of a character, or if there isn't one, creates one and adds it to the list of creatures.
    /// </summary>
    /// <remarks>
    /// The character must be a valid creature, see <see cref="ShouldSaveCreature(Character)"/>
    /// </remarks>
    public OutpostCreatureInfo GetOrAddCreatureInfo(Character character)
    {
        if (!ShouldSaveCreature(character))
        {
            throw new Exception("Tried to get or add a creature info of an invalid character");
        }

        OutpostCreatureInfo? outpostCreatureInfo = creatureInfos.FirstOrDefault(info => info.Character == character);
        if (outpostCreatureInfo != null) 
        { 
            return outpostCreatureInfo; 
        }

        outpostCreatureInfo = new OutpostCreatureInfo(character);

        creatureInfos.Add(outpostCreatureInfo);

        return outpostCreatureInfo;
    }

    private void SpawnCreatures(Submarine outpost)
    {
        if (GameMain.NetworkMember is { IsClient: true }) { return; }

        foreach (OutpostCreatureInfo creatureInfo in creatureInfos.ToList())
        {
            SpawnCreature(creatureInfo, outpost);
        }

        if (GameMain.GameSession.Campaign is { Map: Map map })
        {
            int progressedWorldSteps = map.WorldStepCount - lastWorldStepCount;
            creatureInfos.ForEach(creatureInfo => creatureInfo.WorldStepsInOutpost += progressedWorldSteps);
            lastWorldStepCount = map.WorldStepCount;
        }
    }

    public static bool ShouldSaveCreature(Character character)
    {
        if (character.Removed) { return false; }
        if (character.DisableCreatureSaving) { return false; }
        if (character.IsDead) { return false; }
        if (character.IsHuman) { return false; }

        if (!character.ForceSaveInOutpost)
        {
            if (character.Submarine == null) { return false; }
            if (character.Submarine.Info is not { Type: SubmarineType.Outpost }) { return false; }
        }

        return true;
    }

    private static void SpawnCreature(OutpostCreatureInfo info, Submarine outpost)
    {
        if (info.Character != null && !info.Character.Removed)
        {
            DebugConsole.ThrowError($"Failed to spawn an outpost creature: OutpostCreatureInfo already has a valid character ({info.Character}) assigned to it.");
            return;
        }

        Character character = Character.LoadFromElement(info.CharacterElement, out Vector2 savePosition, out string _, out int controllerId);

        Vector2 spawnPosition = outpost.WorldPosition + savePosition;
        if (info.ModuleIndex > -1)
        {
            var module = outpost.OutpostModules.FirstOrDefault(m => m.Index == info.ModuleIndex);
            if (module != null)
            {
                spawnPosition = outpost.WorldPosition + module.PositionRelativeToSub + info.PositionRelativeToModule;
            }
        }

        character.TeleportTo(spawnPosition);

        if (character.CurrentHull == null)
        {
            DebugConsole.NewMessage($"Creature {character} spawned outside hulls, finding a new spawn position...");
            WayPoint? spawnPoint = WayPoint.WayPointList.FirstOrDefault(wp => wp.Submarine == outpost && wp.SpawnType == SpawnType.Human);
            if (spawnPoint == null)
            {
                DebugConsole.ThrowError($"Creature {character} spawned outside hulls, could not find an outpost spawnpoint to reposition it to.");            
            }
            else
            {
                character.TeleportTo(spawnPoint.WorldPosition);
            }
        }

        character.TrySelectControllerBySaveId(outpost, controllerId);

        info.Character = character;
    }

    /// <summary>
    /// Builds a list of valid characters to be saved in this outpost, removes old 
    /// outpost creature infos that do not contain these characters and creates new 
    /// ones for new characters.
    /// </summary>
    private void RefreshCreatures()
    {
        List<Character> validCreatures = Character.CharacterList.Where(ShouldSaveCreature).ToList();

        foreach (OutpostCreatureInfo info in creatureInfos.ToList())
        {
            if (info.Character == null || 
                info.Character.Removed || 
                !validCreatures.Contains(info.Character))
            {
                // Creature died, escaped or is in the player sub, remove
                creatureInfos.Remove(info);

                DebugConsole.NewMessage($"[{nameof(OutpostCreatureManager)}] Removed {nameof(OutpostCreatureInfo)} ({info.Character?.Name ?? "\"character was null\""})");
            }
        }

        // Create infos for new creatures in the outpost
        foreach (Character creature in validCreatures)
        {
            if (creatureInfos.Any(info => info.Character == creature))
            {
                continue;
            }

            GetOrAddCreatureInfo(creature);
        }
    }

    public void Load(XElement element)
    {
        creatureInfos.Clear();

        var charactersElement = element.Element(SavedCreaturesElementName);
        if (charactersElement != null)
        {
            foreach (var infoElement in charactersElement.Elements())
            {
                OutpostCreatureInfo creatureInfo = new OutpostCreatureInfo(infoElement);
                creatureInfos.Add(creatureInfo);
            }
        }

        if (GameMain.GameSession.Campaign is { Map: Map map })
        {
            lastWorldStepCount = element.GetAttributeInt(nameof(lastWorldStepCount), map.WorldStepCount);
        }
    }

    public void Save(XElement element)
    {
        var saveElement = new XElement(nameof(OutpostCreatureManager));

        if (GameMain.GameSession.Campaign is { Map: Map map })
        {
            saveElement.SetAttribute(nameof(lastWorldStepCount), map.WorldStepCount);
        }

        var savedCreaturesElement = new XElement(SavedCreaturesElementName);
        foreach (OutpostCreatureInfo info in creatureInfos)
        {
            XElement creatureInfoElement = new XElement(nameof(OutpostCreatureInfo));
            info.Save(creatureInfoElement);

            savedCreaturesElement.Add(creatureInfoElement);
        }

        saveElement.Add(savedCreaturesElement);

        element.Add(saveElement);
    }
}