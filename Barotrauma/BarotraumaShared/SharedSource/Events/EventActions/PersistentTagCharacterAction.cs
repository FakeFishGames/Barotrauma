using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Scripted action that assigns a persistent tag to a character, storing it in the campaign save file so it carries 
    /// over across campaign rounds. Unlike TagAction, which only marks entities for the duration of the current event, 
    /// this action writes a long-term tag that can be detected by other events, even if the tag was originally applied elsewhere.
    /// To check for a character with a persistent tag, use persistenttag:foobar for the criteria in a TagAction.
    /// </summary>
    /// <remarks>
    /// Under the hood this uses CharacterInfo.GetIdentifier to generate the key used for storing the persistent tags, 
    /// this key should be unique for each character, unless there's another character with the exact same name, job and appearance.
    /// </remarks>
    public class PersistentTagCharacterAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the persistent tag to set. Can be any arbitrary identifier.")]
        public Identifier Identifier { get; set; }
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character to target.")]
        public Identifier TargetTag { get; set; }
        [Serialize(false, IsPropertySaveable.Yes, description: "If set to true the persistent tag will be removed instead of added.")]
        public bool Remove { get; set; }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset(bool shouldEndConversations = true)
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                TagCharacters(campaign);
            }

            isFinished = true;
        }

        private void TagCharacters(CampaignMode campaign)
        {
            var tagTargets = ParentEvent.GetTargets(TargetTag).OfType<Character>();

            if (!tagTargets.Any())
            {
                DebugConsole.AddWarning($"Couldn't find any targets with the given {nameof(TargetTag)} for {GetEventDebugName()}.");
                return;
            }

            foreach (var character in tagTargets)
            {
                if (Remove)
                {
                    campaign.CampaignMetadata.RemoveValue(GetMetadataIdentifer(character, Identifier));
                }
                else
                {
                    campaign.CampaignMetadata.SetValue(GetMetadataIdentifer(character, Identifier), true);
                }
            }
        }

        public static Identifier GetMetadataIdentifer(Character character, Identifier tagIdentifier)
        {
            int characterUniqueId = character.Info.GetIdentifier();
            Identifier metadataIdentifier = $"charactertag_{characterUniqueId}_{tagIdentifier}".ToIdentifier();
            return metadataIdentifier;
        }

        public override string ToDebugString()
        {
            string target = $"Tag: {TargetTag}";
            return $"{ToolBox.GetDebugSymbol(isFinished)} {GetType().Name} -> ({target})";
        }
    }
}