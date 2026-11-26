#nullable enable

using Barotrauma;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

public class OutpostCreatureInfo
{
    private const string CharacterElementName = "Character";

    public XElement CharacterElement { get; private set; }
    public Character? Character { get; set; }

    public int ModuleIndex { get; private set; } = -1;
    public Vector2 PositionRelativeToModule { get; private set; }
    public int WorldStepsInOutpost { get; set; } = 0;
    public int LastReproductionWorldStep { get; set; } = -1;

    public OutpostCreatureInfo(XElement element)
    {
        XElement? characterElement = element.Element(CharacterElementName);
        if (characterElement == null)
        {
            throw new Exception("Failed to load OutpostCreatureInfo: the Character element is missing");
        }
        ModuleIndex = element.GetAttributeInt(nameof(ModuleIndex), -1);
        PositionRelativeToModule = element.GetAttributeVector2(nameof(PositionRelativeToModule), Vector2.Zero);
        WorldStepsInOutpost = element.GetAttributeInt(nameof(WorldStepsInOutpost), WorldStepsInOutpost);
        LastReproductionWorldStep = element.GetAttributeInt(nameof(LastReproductionWorldStep), -1);
        CharacterElement = characterElement;
    }

    public OutpostCreatureInfo(Character character)
    {
        Character = character;

        CharacterElement = new XElement(CharacterElementName);
        Character.SaveToElement(CharacterElement);
    }

    public void Save(XElement element)
    {
        if (Character != null && !Character.Removed)
        {
            CharacterElement = new XElement(CharacterElementName);
            Character.SaveToElement(CharacterElement);

            element.Add(new XAttribute(nameof(WorldStepsInOutpost), WorldStepsInOutpost));
            element.Add(new XAttribute(nameof(LastReproductionWorldStep), LastReproductionWorldStep));

            if (Level.Loaded?.StartOutpost is { } outpost && Character.CurrentHull != null)
            {
                var module = outpost.OutpostModules.FirstOrDefault(module => module.Entities.Contains(Character.CurrentHull));
                if (module != null)
                {
                    ModuleIndex = module.Index;
                    element.Add(new XAttribute(nameof(ModuleIndex), ModuleIndex));
                    Vector2 moduleWorldPosition = outpost.WorldPosition + module.PositionRelativeToSub;
                    PositionRelativeToModule = Character.WorldPosition - moduleWorldPosition;
                    element.Add(new XAttribute(nameof(PositionRelativeToModule), XMLExtensions.Vector2ToString(PositionRelativeToModule)));
                }
            }
        }

        element.Add(CharacterElement);
    }
}