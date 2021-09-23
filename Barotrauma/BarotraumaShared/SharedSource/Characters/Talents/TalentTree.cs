using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class TalentTree
    {
        public enum TalentTreeStageState
        {
            Invalid,
            Locked,
            Unlocked,
            Available,
            Highlighted
        }

        public static readonly Dictionary<string, TalentTree> JobTalentTrees = new Dictionary<string, TalentTree>();

        public readonly List<TalentSubTree> TalentSubTrees = new List<TalentSubTree>();

        public XElement ConfigElement
        {
            get;
            private set;
        }

        public TalentTree(XElement element, string filePath)
        {
            ConfigElement = element;

            string jobIdentifier = element.GetAttributeString("jobidentifier", "");

            if (string.IsNullOrEmpty(jobIdentifier))
            {
                DebugConsole.ThrowError($"No job defined for talent tree in \"{filePath}\"!");
                return;
            }

            foreach (XElement subTreeElement in element.GetChildElements("subtree"))
            {
                TalentSubTrees.Add(new TalentSubTree(subTreeElement));
            }

            // talents found and unlocked using the identifier wihin the talent tree, so no duplicates may occur
            HashSet<string> duplicateSet = new HashSet<string>();
            foreach (string talent in TalentSubTrees.SelectMany(s => s.TalentOptionStages.SelectMany(o => o.Talents.Select(t => t.Identifier))))
            {
                TalentPrefab talentPrefab = TalentPrefab.TalentPrefabs.Find(c => c.Identifier.Equals(talent, StringComparison.OrdinalIgnoreCase));
                if (talentPrefab == null)
                {
                    DebugConsole.AddWarning($"Talent tree for job {jobIdentifier} contains non-existent talent {talent}! Talent tree not added.");
                    return;
                }
                if (!duplicateSet.Add(talent))
                {
                    DebugConsole.ThrowError($"Talent tree for job {jobIdentifier} contains duplicate talent {talent}! Talent tree not added.");
                    return;
                }
            }

            if (!JobTalentTrees.TryAdd(jobIdentifier, this))
            {
                DebugConsole.ThrowError($"Could not add talent tree for job {jobIdentifier}! A talent tree for this job is already likely defined");
            }
        }

        public static void LoadFromFile(ContentFile file)
        {
            DebugConsole.Log("Loading talent tree: " + file.Path);

            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
            if (doc == null) { return; }

            var rootElement = doc.Root;
            switch (rootElement.Name.ToString().ToLowerInvariant())
            {
                case "talenttree":
                    new TalentTree(rootElement, file.Path);
                    break;
                case "talenttrees":
                    foreach (var element in rootElement.Elements())
                    {
                        if (element.IsOverride())
                        {
                            var treeElement = element.GetChildElement("talenttree");
                            if (treeElement != null)
                            {
                                new TalentTree(rootElement, file.Path);
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Cannot find a talent tree element from the children of the override element defined in {file.Path}");
                            }
                        }
                        else
                        {
                            new TalentTree(element, file.Path);
                        }
                    }
                    break;
                default:
                    DebugConsole.ThrowError($"Invalid XML root element: '{rootElement.Name}' in {file.Path}");
                    break;
            }
        }

        public static void LoadAll(IEnumerable<ContentFile> files)
        {
            DebugConsole.Log("Loading talent tree: ");

            foreach (ContentFile file in files)
            {
                LoadFromFile(file);
            }
        }

        public static bool IsViableTalentForCharacter(Character character, string talentIdentifier)
        {
            return IsViableTalentForCharacter(character, talentIdentifier, character?.Info?.UnlockedTalents ?? Enumerable.Empty<string>());
        }

        // i hate this function - markus
        public static TalentTreeStageState GetTalentOptionStageState(Character character, string subTreeIdentifier, int index, List<string> selectedTalents)
        {
            if (character?.Info?.Job.Prefab is null) { return TalentTreeStageState.Invalid; }

            if (!JobTalentTrees.TryGetValue(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return TalentTreeStageState.Invalid; }

            TalentSubTree subTree = talentTree.TalentSubTrees.FirstOrDefault(tst => tst.Identifier == subTreeIdentifier);

            if (subTree == null) { return TalentTreeStageState.Invalid; }

            TalentOption targetTalentOption = subTree.TalentOptionStages[index];

            if (targetTalentOption.Talents.Any(t => character.HasTalent(t.Identifier)))
            {
                return TalentTreeStageState.Unlocked;
            }

            if (targetTalentOption.Talents.Any(t => selectedTalents.Contains(t.Identifier)))
            {
                return TalentTreeStageState.Highlighted;
            }

            bool hasTalentInLastTier = true;
            bool isLastTalentPurchased = true;

            int lastindex = index - 1;
            if (lastindex >= 0)
            {
                TalentOption lastLatentOption = subTree.TalentOptionStages[lastindex];
                hasTalentInLastTier = lastLatentOption.Talents.Any(HasTalent);
                isLastTalentPurchased = lastLatentOption.Talents.Any(t => character.HasTalent(t.Identifier));
            }

            if (!hasTalentInLastTier)
            {
                return TalentTreeStageState.Locked;
            }

            bool hasPointsForNewTalent = character.Info.GetTotalTalentPoints() - selectedTalents.Count > 0;

            if (hasPointsForNewTalent)
            {
                return isLastTalentPurchased ? TalentTreeStageState.Highlighted : TalentTreeStageState.Available;
            }

            return TalentTreeStageState.Locked;

            bool HasTalent(TalentPrefab t)
            {
                return selectedTalents.Contains(t.Identifier);
            }
        }


        public static bool IsViableTalentForCharacter(Character character, string talentIdentifier, IEnumerable<string> selectedTalents)
        {
            if (character?.Info?.Job.Prefab == null) { return false; }
            if (character.Info.GetTotalTalentPoints() - selectedTalents.Count() <= 0) { return false; }

            if (!JobTalentTrees.TryGetValue(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return false; }

            foreach (var subTree in talentTree.TalentSubTrees)
            {
                foreach (var talentOptionStage in subTree.TalentOptionStages)
                {
                    bool hasTalentInThisTier = talentOptionStage.Talents.Any(t => selectedTalents.Contains(t.Identifier));
                    if (!hasTalentInThisTier)
                    {
                        if (talentOptionStage.Talents.Any(t => t.Identifier == talentIdentifier))
                        {
                            return true;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            return false;
        }

        public static List<string> CheckTalentSelection(Character controlledCharacter, IEnumerable<string> selectedTalents)
        {
            List<string> viableTalents = new List<string>();
            bool canStillUnlock = true;
            // keep trying to unlock talents until none of the talents are unlockable
            while (canStillUnlock && selectedTalents.Any())
            {
                canStillUnlock = false;
                foreach (string talent in selectedTalents)
                {
                    if (IsViableTalentForCharacter(controlledCharacter, talent, viableTalents))
                    {
                        viableTalents.Add(talent);
                        canStillUnlock = true;
                    }
                }
            }
            return viableTalents;
        }
    }

    class TalentSubTree
    {
        public string Identifier { get; }

        public string DisplayName { get; }

        public readonly List<TalentOption> TalentOptionStages = new List<TalentOption>();

        public TalentSubTree(XElement subTreeElement)
        {
            Identifier = subTreeElement.GetAttributeString("identifier", "");

            DisplayName = TextManager.Get("talenttree." + Identifier, returnNull: true) ?? Identifier;

            foreach (XElement talentOptionsElement in subTreeElement.GetChildElements("talentoptions"))
            {
                TalentOptionStages.Add(new TalentOption(talentOptionsElement, Identifier));
            }
        }

    }

    class TalentOption
    {
        public readonly List<TalentPrefab> Talents = new List<TalentPrefab>();

        public TalentOption(XElement talentOptionsElement, string debugIdentifier)
        {
            foreach (XElement talentOptionElement in talentOptionsElement.GetChildElements("talentoption"))
            {
                string identifier = talentOptionElement.GetAttributeString("identifier", string.Empty);

                if (!TalentPrefab.TalentPrefabs.ContainsKey(identifier))
                {
                    DebugConsole.ThrowError($"Error in talent tree \"{debugIdentifier}\" - could not find a talent with the identifier \"{identifier}\".");
                    return;
                }
                Talents.Add(TalentPrefab.TalentPrefabs[identifier]);
            }
        }
    }

}
