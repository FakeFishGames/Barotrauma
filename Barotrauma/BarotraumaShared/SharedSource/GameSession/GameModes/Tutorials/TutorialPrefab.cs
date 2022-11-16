using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    class TutorialPrefab : Prefab
    {

        public static readonly PrefabCollection<TutorialPrefab> Prefabs =
#if CLIENT
            new PrefabCollection<TutorialPrefab>(onSort: MainMenuScreen.UpdateInstanceTutorialButtons);
#else
            new PrefabCollection<TutorialPrefab>();
#endif

        public readonly int Order;
        public readonly bool DisableBotConversations;
        public readonly bool AllowCharacterSwitch;

        public readonly ContentPath SubmarinePath = ContentPath.FromRaw("Content/Tutorials/Dugong_Tutorial.sub");
        public readonly ContentPath OutpostPath = ContentPath.FromRaw("Content/Tutorials/TutorialOutpost.sub");
        public readonly string LevelSeed;
        public readonly string LevelParams;

        private readonly ContentXElement tutorialCharacterElement;
        public readonly ImmutableArray<Identifier> StartingItemTags;

        public readonly Identifier EventIdentifier;

        public readonly Sprite Banner;

        public TutorialPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Order = element.GetAttributeInt("order", int.MaxValue);
            DisableBotConversations = element.GetAttributeBool("disablebotconversations", true);
            AllowCharacterSwitch = element.GetAttributeBool("allowcharacterswitch", false);

            SubmarinePath = element.GetAttributeContentPath("submarinepath") ?? SubmarinePath;
            OutpostPath = element.GetAttributeContentPath("outpostpath") ?? OutpostPath;
            LevelSeed = element.GetAttributeString("levelseed", "nLoZLLtza");
            LevelParams = element.GetAttributeString("levelparams", "ColdCavernsTutorial");

            tutorialCharacterElement = element.GetChildElement("characterinfo");
            if (tutorialCharacterElement != null)
            {
                StartingItemTags = tutorialCharacterElement
                    .GetAttributeIdentifierArray("startingitemtags", new Identifier[0])
                    .ToImmutableArray();
            }
            else
            {
                StartingItemTags = ImmutableArray<Identifier>.Empty;
            }

            var bannerElement = element.GetChildElement("banner");
            if (bannerElement != null)
            {
                Banner = new Sprite(bannerElement, lazyLoad: true);
            }

            EventIdentifier = element.GetChildElement("scriptedevent")?.GetAttributeIdentifier("identifier", "") ?? Identifier.Empty;
        }

        public CharacterInfo GetTutorialCharacterInfo()
        {
            if (tutorialCharacterElement == null)
            {
                return null;
            }
            Identifier speciesName = tutorialCharacterElement.GetAttributeIdentifier("speciesname", CharacterPrefab.HumanSpeciesName);
            Identifier jobPrefabIdentifier = tutorialCharacterElement.GetAttributeIdentifier("jobidentifier", "assistant");
            if (!JobPrefab.Prefabs.TryGet(jobPrefabIdentifier, out var jobPrefab))
            {
                jobPrefab = JobPrefab.Prefabs.First();
            }
            int jobVariant = tutorialCharacterElement.GetAttributeInt("variant", 0);
            var characterInfo = new CharacterInfo(speciesName, jobOrJobPrefab: jobPrefab, variant: jobVariant);
            foreach (var skillElement in tutorialCharacterElement.GetChildElements("skill"))
            {
                Identifier skillIdentifier = skillElement.GetAttributeIdentifier("identifier", "");
                if (skillIdentifier.IsEmpty) { continue; }
                float level = skillElement.GetAttributeFloat("level", 0.0f);
                characterInfo.SetSkillLevel(skillIdentifier, level);
            }
            return characterInfo;
        }

        public override void Dispose() { }
    }
}
