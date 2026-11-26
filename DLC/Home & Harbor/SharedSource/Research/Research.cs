using Barotrauma;
using System.Xml.Linq;

namespace HomeAndHarbor;

/// <summary>
/// Represents a currently in progress or finished research
/// </summary>
public class Research
{
    public readonly TechnologyPrefab Prefab;

    private int progress;

    /// <summary>
    /// How close the research is to being finished (finished when this value reaches <see cref="TechnologyPrefab.Cost"/>)
    /// </summary>
    public int Progress
    {
        get { return progress; } 
        set { progress = Math.Clamp(value, 0, Prefab.Cost); }
    }
    public bool IsFinished => progress >= Prefab.Cost;

    public Research(TechnologyPrefab prefab, bool unlockInstantly = false)
    {
        Prefab = prefab;

        if (unlockInstantly)
        {
            Progress = prefab.Cost;
        }
    }

    public Research(XElement element)
    {
        Identifier identifier = element.GetAttributeIdentifier("prefab", Identifier.Empty);
        if (!TechnologyPrefab.Prefabs.TryGet(identifier, out TechnologyPrefab? prefab))
        {
            throw new Exception($"Failed to find prefab with identifier {identifier}");
        }

        Prefab = prefab;
        Progress = element.GetAttributeInt("progress", Progress);

        // this might seem redundant but this avoids breaking existing campaign save files if we ever change a research cost
        if (element.GetAttributeBool("isfinished", false))
        {
            Progress = prefab.Cost;
        }
    }

    public int AddProgress(int researchPoints)
    {
        int costLeft = Prefab.Cost - Progress;

        bool wasFinished = IsFinished;

        int progressed = Math.Min(researchPoints, costLeft); ;
        Progress += progressed;
        researchPoints -= progressed;

        if (!wasFinished && IsFinished)
        {
            OnFinished();
        }

        return researchPoints;
    }

    public void OnFinished()
    {
        ResearchRecipeUnlockPrefab.UnlockRecipes(Prefab);
    }

    public XElement Save()
    {
        XElement element = new XElement("Research");
        element.SetAttributeValue("prefab", Prefab.Identifier);
        element.SetAttributeValue("progress", Progress);
        element.SetAttributeValue("isfinished", IsFinished);
        return element;
    }
}