#nullable enable

using Barotrauma;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace HomeAndHarbor;

/// <summary>
/// Used for storing persistent information about outpost NPCs.
/// </summary>
public class OutpostDwellerInfo : ISerializableEntity
{
    public readonly CharacterInfo CharacterInfo;

    private readonly Character? character;

    /// <summary>
    /// Not used for anything (yet), just an example
    /// </summary>
    [Serialize(defaultValue: 50.0f, IsPropertySaveable.Yes)]
    public float Hunger { get; private set; }

    [Serialize(defaultValue: 0, IsPropertySaveable.Yes)]
    public int AssignedWorkstationId { get; set; }

    /// <summary>
    /// How many work shifts (world steps) this dweller is unavailable for work (being assigned to workstations).
    /// 0 means they're available for work again. -1 means they're unavailable indefinitely, until this value is set
    /// to 0 to clear it or to >0 to make the unavailability finite again.
    /// </summary>
    [Serialize(defaultValue: 0, IsPropertySaveable.Yes)]
    public int WorkShiftsUnavailable { get; set; }

    /// <summary>
    /// Optional identifier for the text tag shown in the dweller UI in place of the workstation name while the dweller is unavailable for work.
    /// Set in SetDwellerUnavailableAction so each event can word it in its own way (sick, lazy, striking, etc.).
    /// If this is left unset, the generic 'dwellerui.unavailable' is used as fallback.
    /// </summary>
    [Serialize(defaultValue: "", IsPropertySaveable.Yes)]
    public Identifier UnavailabilityReason { get; set; }

    public bool IsUnavailableForWork => WorkShiftsUnavailable != 0;

    /// <summary>
    /// Which skill this character gains when assigned to a workstation. Persisted so skill gain works even when the outpost is not loaded. Not intended to be set directly, use <see cref="WorkstationComponent.SkillIdentifier"/> instead. />
    /// </summary>
    [Serialize(defaultValue: "", IsPropertySaveable.Yes)]
    public Identifier SkillToGainPerWorldStep { get; private set; }

    /// <summary>
    /// Base skill gain passed to <see cref="CharacterInfo.ApplySkillGain"/> per world step, if <see cref="SkillToGainPerWorldStep"/> has been set. Not intended to be set directly, use <see cref="WorkstationComponent.SkillIncreasePerWorldStep"/> instead. />
    /// </summary>
    [Serialize(defaultValue: 0f, IsPropertySaveable.Yes)]
    public float SkillIncreasePerWorldStep { get; private set; }

    public string Name => $"{nameof(OutpostDwellerInfo)} ({CharacterInfo.Name})";

    public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

    public OutpostDwellerInfo(XElement element)
    {
        SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

        if (element.GetChildElement("Character") is not { } characterInfoElement)
        {
            DebugConsole.ThrowError($"Error when loading {nameof(OutpostDwellerInfo)}: could not find a characterinfo element");
            CharacterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName);
        }
        else
        {
            // Passing the ids in makes the dweller re-derive its name and title from the human prefab, so those follow the language setting
            CharacterInfo = new CharacterInfo(new ContentXElement(contentPackage: null, characterInfoElement),
                humanPrefabIds: Barotrauma.CharacterInfo.GetHumanPrefabIds(characterInfoElement));
        }
    }

    public OutpostDwellerInfo(Character character)
    {
        this.character = character;
        CharacterInfo = character.Info;
        SerializableProperties = SerializableProperty.GetProperties(this);
    }

    public OutpostDwellerInfo(CharacterInfo characterInfo)
    {
        CharacterInfo = characterInfo;
        SerializableProperties = SerializableProperty.GetProperties(this);
    }

    /// <summary>
    /// Cache the skill and skill gain rate from the assigned workstation.
    /// Should only be called by the workstation system.
    /// </summary>
    public void SetWorkstationSkill(Identifier skill, float gainPerWorldStep)
    {
        SkillToGainPerWorldStep = skill;
        SkillIncreasePerWorldStep = gainPerWorldStep;
    }

    /// <summary>
    /// Clears the cached workstation skill settings on unassignment.
    /// Should only be called by the workstation system.
    /// </summary>
    public void ClearWorkstationSkill()
    {
        SkillToGainPerWorldStep = Identifier.Empty;
        SkillIncreasePerWorldStep = 0f;
    }

    /// <summary>
    /// Makes the dweller available for work again and clears everything related to them having been unavailable.
    /// </summary>
    public void ClearWorkUnavailability()
    {
        WorkShiftsUnavailable = 0;
        UnavailabilityReason = Identifier.Empty;
    }

    public XElement Save(XElement parentElement)
    {
        var outpostDwellerElement = new XElement(nameof(OutpostDwellerInfo));
        SerializableProperty.SerializeProperties(this, outpostDwellerElement);
        CharacterInfo.Save(outpostDwellerElement, includeInventoryData: true, includeHealthData: true, includeOrderData: true);
        parentElement.Add(outpostDwellerElement);
        return outpostDwellerElement;
    }

    public void OnCharacterSpawned(Character spawnedCharacter)
    {
        if (AssignedWorkstationId != Entity.NullEntityID)
        {
            if (WorkstationComponent.FindWorkstationWithID(AssignedWorkstationId) is not WorkstationComponent workstationComponent)
            {
                DebugConsole.ThrowError($"Failed to find a work station with the id {AssignedWorkstationId} for the character {spawnedCharacter.Name}.");
            }
            else
            {
                workstationComponent.AssignCharacter(spawnedCharacter);
                //don't teleport to the workstation if it's somehow outside hulls (moved, attached outside the sub?)
                if (workstationComponent.Item.CurrentHull is Hull workstationHull)
                {
                    // teleport a bit above the floor instead of the center of the workstation, which may be inside the floor or some other structure
                    float floorY = workstationHull.WorldRect.Y - workstationHull.WorldRect.Height;
                    spawnedCharacter.TeleportTo(new Vector2(workstationComponent.Item.WorldPosition.X, floorY + WayPoint.WayPointHeightFromFloor));
                }
                // Refresh from the workstation's current values in case there's ever a situation where they've changed
                SetWorkstationSkill(workstationComponent.SkillIdentifier, workstationComponent.SkillIncreasePerWorldStep);
            }
        }
    }

    public override string ToString() => $"{nameof(OutpostDwellerInfo)} ({CharacterInfo.Name})";
}