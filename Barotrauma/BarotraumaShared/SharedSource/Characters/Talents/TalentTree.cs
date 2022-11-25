using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal sealed class TalentTree : Prefab
    {
        public enum TalentStages
        {
            Invalid,
            Locked,
            Unlocked,
            Available,
            Highlighted
        }

        public static readonly PrefabCollection<TalentTree> JobTalentTrees = new PrefabCollection<TalentTree>();

        public readonly ImmutableArray<TalentSubTree> TalentSubTrees;

        /// <summary>
        /// Talent identifiers of all the talents in this tree
        /// </summary>
        public readonly ImmutableHashSet<Identifier> AllTalentIdentifiers;

        public ContentXElement ConfigElement
        {
            get;
            private set;
        }

        public TalentTree(ContentXElement element, TalentTreesFile file) : base(file, element.GetAttributeIdentifier("jobIdentifier", ""))
        {
            ConfigElement = element;

            if (Identifier.IsEmpty)
            {
                DebugConsole.ThrowError($"No job defined for talent tree in \"{file.Path}\"!");
                return;
            }

            List<TalentSubTree> subTrees = new List<TalentSubTree>();
            foreach (var subTreeElement in element.GetChildElements("subtree"))
            {
                subTrees.Add(new TalentSubTree(subTreeElement));
            }

            TalentSubTrees = subTrees.ToImmutableArray();
            AllTalentIdentifiers = TalentSubTrees.SelectMany(t => t.AllTalentIdentifiers).ToImmutableHashSet();
        }

        public bool TalentIsInTree(Identifier talentIdentifier)
        {
            return AllTalentIdentifiers.Contains(talentIdentifier);
        }

        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier)
        {
            return IsViableTalentForCharacter(character, talentIdentifier, character?.Info?.UnlockedTalents ?? (IReadOnlyCollection<Identifier>)Array.Empty<Identifier>());
        }

        public static bool TalentTreeMeetsRequirements(TalentTree tree, TalentSubTree targetTree, IReadOnlyCollection<Identifier> selectedTalents)
        {
            IEnumerable<TalentSubTree> blockingSubTrees = tree.TalentSubTrees.Where(tst => tst.BlockedTrees.Contains(targetTree.Identifier)),
                                       requiredSubTrees = tree.TalentSubTrees.Where(tst => targetTree.RequiredTrees.Contains(tst.Identifier));

            return requiredSubTrees.All(tst => tst.HasEnoughTalents(selectedTalents)) &&  // check if we meet requirements
                   !blockingSubTrees.Any(tst => tst.HasAnyTalent(selectedTalents) && !tst.HasMaxTalents(selectedTalents)); // check if any other talent trees are blocking this one
        }

        // i hate this function - markus
        // me too - joonas
        public static TalentStages GetTalentOptionStageState(Character character, Identifier subTreeIdentifier, int index, IReadOnlyCollection<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab is null) { return TalentStages.Invalid; }

            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return TalentStages.Invalid; }

            TalentSubTree subTree = talentTree!.TalentSubTrees.FirstOrDefault(tst => tst.Identifier == subTreeIdentifier);
            if (subTree is null) { return TalentStages.Invalid; }

            if (!TalentTreeMeetsRequirements(talentTree, subTree, selectedTalents))
            {
                return TalentStages.Locked;
            }

            TalentOption targetTalentOption = subTree.TalentOptionStages[index];

            if (targetTalentOption.HasEnoughTalents(character.Info))
            {
                return TalentStages.Unlocked;
            }

            if (targetTalentOption.HasSelectedTalent(selectedTalents))
            {
                return TalentStages.Highlighted;
            }

            bool hasTalentInLastTier = true;
            bool isLastTalentPurchased = true;

            int lastindex = index - 1;
            if (lastindex >= 0)
            {
                TalentOption lastLatentOption = subTree.TalentOptionStages[lastindex];
                hasTalentInLastTier = lastLatentOption.HasEnoughTalents(selectedTalents);
                isLastTalentPurchased = lastLatentOption.HasEnoughTalents(character.Info);
            }

            if (!hasTalentInLastTier)
            {
                return TalentStages.Locked;
            }

            bool hasPointsForNewTalent = character.Info.GetTotalTalentPoints() - selectedTalents.Count > 0;

            if (hasPointsForNewTalent)
            {
                return isLastTalentPurchased ? TalentStages.Highlighted : TalentStages.Available;
            }

            return TalentStages.Locked;
        }


        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier, IReadOnlyCollection<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab == null) { return false; }
            if (character.Info.GetTotalTalentPoints() - selectedTalents.Count <= 0) { return false; }
            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return false; }

            foreach (var subTree in talentTree!.TalentSubTrees)
            {
                if (subTree.AllTalentIdentifiers.Contains(talentIdentifier) && subTree.HasMaxTalents(selectedTalents)) { return false; }

                foreach (var talentOptionStage in subTree.TalentOptionStages)
                {
                    if (talentOptionStage.TalentIdentifiers.Contains(talentIdentifier))
                    {
                        return !talentOptionStage.HasMaxTalents(selectedTalents) && TalentTreeMeetsRequirements(talentTree, subTree, selectedTalents);
                    }
                    bool optionStageCompleted = talentOptionStage.HasEnoughTalents(selectedTalents);
                    if (!optionStageCompleted)
                    {
                        break;
                    }
                }
            }

            return false;
        }

        public static List<Identifier> CheckTalentSelection(Character controlledCharacter, IEnumerable<Identifier> selectedTalents)
        {
            List<Identifier> viableTalents = new List<Identifier>();
            bool canStillUnlock = true;
            // keep trying to unlock talents until none of the talents are unlockable
            while (canStillUnlock && selectedTalents.Any())
            {
                canStillUnlock = false;
                foreach (Identifier talent in selectedTalents)
                {
                    if (!viableTalents.Contains(talent) && IsViableTalentForCharacter(controlledCharacter, talent, viableTalents))
                    {
                        viableTalents.Add(talent);
                        canStillUnlock = true;
                    }
                }
            }

            return viableTalents;
        }

        public override void Dispose() { }
    }

    internal enum TalentTreeType
    {
        Specialization,
        Primary
    }

    internal sealed class TalentSubTree
    {
        public Identifier Identifier { get; }

        public LocalizedString DisplayName { get; }

        public readonly ImmutableArray<TalentOption> TalentOptionStages;

        public readonly ImmutableHashSet<Identifier> AllTalentIdentifiers;

        public readonly TalentTreeType Type;
        public readonly ImmutableHashSet<Identifier> RequiredTrees;
        public readonly ImmutableHashSet<Identifier> BlockedTrees;

        public bool HasEnoughTalents(IReadOnlyCollection<Identifier> talents) => TalentOptionStages.All(option => option.HasEnoughTalents(talents));
        public bool HasMaxTalents(IReadOnlyCollection<Identifier> talents) => TalentOptionStages.All(option => option.HasMaxTalents(talents));
        public bool HasAnyTalent(IReadOnlyCollection<Identifier> talents) => TalentOptionStages.Any(option => option.HasSelectedTalent(talents));

        public TalentSubTree(ContentXElement subTreeElement)
        {
            Identifier = subTreeElement.GetAttributeIdentifier("identifier", "");
            string nameIdentifier = subTreeElement.GetAttributeString("nameidentifier", string.Empty);
            if (string.IsNullOrWhiteSpace(nameIdentifier))
            {
                nameIdentifier = $"talenttree.{Identifier}";
            }
            DisplayName = TextManager.Get(nameIdentifier).Fallback(Identifier.Value);
            Type = subTreeElement.GetAttributeEnum("type", TalentTreeType.Specialization);
            RequiredTrees = subTreeElement.GetAttributeIdentifierImmutableHashSet("requires", ImmutableHashSet<Identifier>.Empty);
            BlockedTrees = subTreeElement.GetAttributeIdentifierImmutableHashSet("blocks", ImmutableHashSet<Identifier>.Empty);
            List<TalentOption> talentOptionStages = new List<TalentOption>();
            foreach (var talentOptionsElement in subTreeElement.GetChildElements("talentoptions"))
            {
                talentOptionStages.Add(new TalentOption(talentOptionsElement, Identifier));
            }

            TalentOptionStages = talentOptionStages.ToImmutableArray();
            AllTalentIdentifiers = TalentOptionStages.SelectMany(t => t.TalentIdentifiers).ToImmutableHashSet();
        }
    }

    internal readonly struct TalentOption
    {
        private readonly ImmutableHashSet<Identifier> talentIdentifiers;

        public IEnumerable<Identifier> TalentIdentifiers => talentIdentifiers;

        /// <summary>
        /// How many talents need to be unlocked to consider this tree completed
        /// </summary>
        public readonly int RequiredTalents;
        /// <summary>
        /// How many talents can be unlocked in total
        /// </summary>
        public readonly int MaxChosenTalents;

        /// <summary>
        /// When specified the talent option will show talent with this identifier
        /// and clicking on it will expand the talent option to show the talents
        /// </summary>
        public readonly Dictionary<Identifier, ImmutableHashSet<Identifier>> ShowCaseTalents = new Dictionary<Identifier, ImmutableHashSet<Identifier>>();

        public bool HasEnoughTalents(CharacterInfo character) => CountMatchingTalents(character.UnlockedTalents) >= RequiredTalents;
        public bool HasEnoughTalents(IReadOnlyCollection<Identifier> selectedTalents) => CountMatchingTalents(selectedTalents) >= RequiredTalents;
        public bool HasMaxTalents(IReadOnlyCollection<Identifier> selectedTalents) => CountMatchingTalents(selectedTalents) >= MaxChosenTalents;

        // No LINQ
        public bool HasSelectedTalent(IReadOnlyCollection<Identifier> selectedTalents)
        {
            foreach (Identifier talent in selectedTalents)
            {
                if (talentIdentifiers.Contains(talent))
                {
                    return true;
                }
            }
            return false;
        }

        public int CountMatchingTalents(IReadOnlyCollection<Identifier> talents)
        {
            int i = 0;
            foreach (Identifier talent in talents)
            {
                if (talentIdentifiers.Contains(talent))
                {
                    i++;
                }
            }
            return i;
        }

        public TalentOption(ContentXElement talentOptionsElement, Identifier debugIdentifier)
        {
            MaxChosenTalents = talentOptionsElement.GetAttributeInt(nameof(MaxChosenTalents), 1);
            RequiredTalents = talentOptionsElement.GetAttributeInt(nameof(RequiredTalents), MaxChosenTalents);

            if (RequiredTalents > MaxChosenTalents)
            {
                DebugConsole.ThrowError($"Error in talent tree {debugIdentifier} - MaxChosenTalents is larger than RequiredTalents.");
            }

            HashSet<Identifier> identifiers = new HashSet<Identifier>();
            foreach (ContentXElement talentOptionElement in talentOptionsElement.Elements())
            {
                Identifier elementName = talentOptionElement.Name.ToIdentifier();
                if (elementName == "talentoption")
                {
                    identifiers.Add(talentOptionElement.GetAttributeIdentifier("identifier", Identifier.Empty));
                }
                else if (elementName == "showcasetalent")
                {
                    Identifier showCaseIdentifier = talentOptionElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    HashSet<Identifier> showCaseTalentIdentifiers = new HashSet<Identifier>();
                    foreach (ContentXElement subElement in talentOptionElement.Elements())
                    {
                        Identifier identifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        showCaseTalentIdentifiers.Add(identifier);
                        identifiers.Add(identifier);
                    }
                    ShowCaseTalents.Add(showCaseIdentifier, showCaseTalentIdentifiers.ToImmutableHashSet());
                }
            }

            talentIdentifiers = identifiers.ToImmutableHashSet();

            if (RequiredTalents > talentIdentifiers.Count)
            {
                DebugConsole.ThrowError($"Error in talent tree {debugIdentifier} - completing a stage of the tree requires more talents than there are in the stage.");
            }
            if (MaxChosenTalents > talentIdentifiers.Count)
            {
                DebugConsole.ThrowError($"Error in talent tree {debugIdentifier} - maximum number of talents to choose is larger than the number of talents.");
            }
        }
    }
}