using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Immutable;

namespace Barotrauma
{
    class NPCConversationCollection : Prefab
    {
        public static readonly Dictionary<LanguageIdentifier, PrefabCollection<NPCConversationCollection>> Collections = new Dictionary<LanguageIdentifier, PrefabCollection<NPCConversationCollection>>();

        public readonly LanguageIdentifier Language;

        public readonly List<NPCConversation> Conversations;
        public readonly Dictionary<Identifier, NPCPersonalityTrait> PersonalityTraits;

        public NPCConversationCollection(NPCConversationsFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Language = element.GetAttributeIdentifier("language", "English").ToLanguageIdentifier();
            Conversations = new List<NPCConversation>();
            PersonalityTraits = new Dictionary<Identifier, NPCPersonalityTrait>();
            foreach (var subElement in element.Elements())
            {
                Identifier elemName = new Identifier(subElement.Name.LocalName);
                if (elemName == "Conversation")
                {
                    Conversations.Add(new NPCConversation(subElement));
                }
                else if (elemName == "PersonalityTrait")
                {
                    var personalityTrait = new NPCPersonalityTrait(subElement);
                    PersonalityTraits.Add(personalityTrait.Name, personalityTrait);
                }
            }
        }

        public override void Dispose() { }
    }

    class NPCConversation
    {
        const int MaxPreviousConversations = 20;

        public readonly string Line;

        public readonly ImmutableHashSet<Identifier> AllowedJobs;

        public readonly ImmutableHashSet<Identifier> Flags;

        //The line can only be selected when eventmanager intensity is between these values
        //null = no restriction
        public readonly float? maxIntensity, minIntensity;

        public readonly ImmutableArray<NPCConversation> Responses;
        private readonly int speakerIndex;
        private readonly ImmutableHashSet<Identifier> allowedSpeakerTags;
        private readonly bool requireNextLine;

        public NPCConversation(XElement element)
        {
            Line = element.GetAttributeString("line", "");

            speakerIndex = element.GetAttributeInt("speaker", 0);

            AllowedJobs = element.GetAttributeIdentifierArray("allowedjobs", Array.Empty<Identifier>()).ToImmutableHashSet();
            Flags = element.GetAttributeIdentifierArray("flags", Array.Empty<Identifier>()).ToImmutableHashSet();
            allowedSpeakerTags =  element.GetAttributeIdentifierArray("speakertags", Array.Empty<Identifier>()).ToImmutableHashSet();

            if (element.Attribute("minintensity") != null) minIntensity = element.GetAttributeFloat("minintensity", 0.0f);
            if (element.Attribute("maxintensity") != null) maxIntensity = element.GetAttributeFloat("maxintensity", 1.0f);

            Responses = element.Elements().Select(s => new NPCConversation(s)).ToImmutableArray();
            requireNextLine = element.GetAttributeBool("requirenextline", false);
        }

        private static List<Identifier> GetCurrentFlags(Character speaker)
        {
            var currentFlags = new List<Identifier>();
            if (Submarine.MainSub != null && Submarine.MainSub.AtDamageDepth) { currentFlags.Add("SubmarineDeep".ToIdentifier()); }

            if (GameMain.GameSession != null && Level.Loaded != null)
            {
                if (Level.Loaded.Type == LevelData.LevelType.LocationConnection)
                {
                    if (Timing.TotalTime < GameMain.GameSession.RoundStartTime + 30.0f) { currentFlags.Add("Initial".ToIdentifier()); }
                }
                else if (Level.Loaded.Type == LevelData.LevelType.Outpost)
                {
                    if (Timing.TotalTime < GameMain.GameSession.RoundStartTime + 120.0f && 
                        speaker?.CurrentHull != null && 
                        (speaker.TeamID == CharacterTeamType.FriendlyNPC || speaker.TeamID == CharacterTeamType.None) && 
                        Character.CharacterList.Any(c => c.TeamID != speaker.TeamID && c.CurrentHull == speaker.CurrentHull)) 
                    {
                        currentFlags.Add("EnterOutpost".ToIdentifier()); 
                    }
                }
                if (GameMain.GameSession.EventManager.CurrentIntensity <= 0.2f)
                {
                    currentFlags.Add("Casual".ToIdentifier());
                }

                if (GameMain.GameSession.IsCurrentLocationRadiated())
                {
                    currentFlags.Add("InRadiation".ToIdentifier());
                }
            }

            if (speaker != null)
            {
                if (speaker.AnimController.InWater) { currentFlags.Add("Underwater".ToIdentifier()); }
                currentFlags.Add((speaker.CurrentHull == null ? "Outside" : "Inside").ToIdentifier());

                if (Character.Controlled != null)
                {
                    if (Character.Controlled.CharacterHealth.GetAffliction("psychosis") != null)
                    {
                        currentFlags.Add((speaker != Character.Controlled ? "Psychosis" : "PsychosisSelf").ToIdentifier());
                    }
                }

                var afflictions = speaker.CharacterHealth.GetAllAfflictions();
                foreach (Affliction affliction in afflictions)
                {
                    var currentEffect = affliction.GetActiveEffect();
                    if (currentEffect != null && !string.IsNullOrEmpty(currentEffect.DialogFlag.Value) && !currentFlags.Contains(currentEffect.DialogFlag))
                    {
                        currentFlags.Add(currentEffect.DialogFlag);
                    }
                }

                if (speaker.TeamID == CharacterTeamType.FriendlyNPC && speaker.Submarine != null && speaker.Submarine.Info.IsOutpost)
                {
                    currentFlags.Add("OutpostNPC".ToIdentifier());
                }
                if (speaker.CampaignInteractionType != CampaignMode.InteractionType.None)
                {
                    currentFlags.Add($"CampaignNPC.{speaker.CampaignInteractionType}".ToIdentifier());
                }
                if (GameMain.GameSession?.GameMode is CampaignMode campaignMode && 
                    (campaignMode.Map?.CurrentLocation?.Type?.Identifier == "abandoned"))
                {
                    if (speaker.TeamID == CharacterTeamType.None)
                    {
                        currentFlags.Add("Bandit".ToIdentifier());
                    }
                    else if (speaker.TeamID == CharacterTeamType.FriendlyNPC)
                    {
                        currentFlags.Add("Hostage".ToIdentifier());
                    }
                }
                if (speaker.IsEscorted)
                {
                    currentFlags.Add("escort".ToIdentifier());
                }
            }

            return currentFlags;
        }

        private static readonly List<NPCConversation> previousConversations = new List<NPCConversation>();
        
        public static List<(Character speaker, string line)> CreateRandom(List<Character> availableSpeakers)
        {
            Dictionary<int, Character> assignedSpeakers = new Dictionary<int, Character>();
            List<(Character speaker, string line)> lines = new List<(Character speaker, string line)>();

            var language = GameSettings.CurrentConfig.Language;
            if (language != TextManager.DefaultLanguage && !NPCConversationCollection.Collections.ContainsKey(language))
            {
                DebugConsole.AddWarning($"Could not find NPC conversations for the language \"{language}\". Using \"{TextManager.DefaultLanguage}\" instead..");
                language = TextManager.DefaultLanguage;
            }

            CreateConversation(availableSpeakers, assignedSpeakers, null, lines,
                availableConversations: NPCConversationCollection.Collections[language].SelectMany(cc => cc.Conversations).ToList());
            return lines;
        }

        public static List<(Character speaker, string line)> CreateRandom(List<Character> availableSpeakers, IEnumerable<Identifier> requiredFlags)
        {
            Dictionary<int, Character> assignedSpeakers = new Dictionary<int, Character>();
            List<(Character speaker, string line)> lines = new List<(Character speaker, string line)>();

            var language = GameSettings.CurrentConfig.Language;
            if (language != TextManager.DefaultLanguage && !NPCConversationCollection.Collections.ContainsKey(language))
            {
                DebugConsole.AddWarning($"Could not find NPC conversations for the language \"{language}\". Using \"{TextManager.DefaultLanguage}\" instead..");
                language = TextManager.DefaultLanguage;
            }

            var availableConversations = NPCConversationCollection.Collections[language]
                .SelectMany(cc => cc.Conversations.Where(c => requiredFlags.All(f => c.Flags.Contains(f)))).ToList();
            if (availableConversations.Count > 0)
            {
                CreateConversation(availableSpeakers, assignedSpeakers, null, lines, availableConversations: availableConversations, ignoreFlags: false);
            }
            return lines;
        }

        private static void CreateConversation(
            List<Character> availableSpeakers, 
            Dictionary<int, Character> assignedSpeakers, 
            NPCConversation baseConversation, 
            IList<(Character speaker, string line)> lineList,
            IList<NPCConversation> availableConversations,
            bool ignoreFlags = false)
        {
            IList<NPCConversation> conversations = baseConversation == null ? availableConversations : baseConversation.Responses;
            if (conversations.Count == 0) { return; }

            int conversationIndex = Rand.Int(conversations.Count);
            NPCConversation selectedConversation = conversations[conversationIndex];
            if (string.IsNullOrEmpty(selectedConversation.Line)) { return; }
            
            Character speaker = null;
            //speaker already assigned for this line
            if (assignedSpeakers.ContainsKey(selectedConversation.speakerIndex))
            {
                //check if the character has all required flags to say the line
                var characterFlags = GetCurrentFlags(assignedSpeakers[selectedConversation.speakerIndex]);
                if (selectedConversation.Flags.All(flag => characterFlags.Contains(flag)))
                {
                    speaker = assignedSpeakers[selectedConversation.speakerIndex];
                }
            }
            if (speaker == null)
            {
                var allowedSpeakers = new List<Character>();

                List<NPCConversation> potentialLines = new List<NPCConversation>(conversations);

                //remove lines that are not appropriate for the intensity of the current situation
                if (GameMain.GameSession?.EventManager != null)
                {
                    potentialLines.RemoveAll(l => 
                        (l.minIntensity.HasValue && GameMain.GameSession.EventManager.CurrentIntensity < l.minIntensity) ||
                        (l.maxIntensity.HasValue && GameMain.GameSession.EventManager.CurrentIntensity > l.maxIntensity));
                }

                while (potentialLines.Count > 0)
                {
                    //select a random line and attempt to find a speaker for it
                    // and if no valid speaker is found, choose another random line
                    selectedConversation = GetRandomConversation(potentialLines, baseConversation == null);
                    if (selectedConversation == null || string.IsNullOrEmpty(selectedConversation.Line)) { return; }

                    //speaker already assigned for this line
                    if (assignedSpeakers.ContainsKey(selectedConversation.speakerIndex))
                    {
                        speaker = assignedSpeakers[selectedConversation.speakerIndex];
                        break;
                    }

                    foreach (Character potentialSpeaker in availableSpeakers)
                    {
                        if (CheckSpeakerViability(potentialSpeaker, selectedConversation, assignedSpeakers.Values.ToList(), ignoreFlags))
                        {
                            allowedSpeakers.Add(potentialSpeaker);
                        }
                    }

                    if (allowedSpeakers.Count == 0 || NextLineFailure(selectedConversation, availableSpeakers, allowedSpeakers, ignoreFlags))
                    {
                        allowedSpeakers.Clear();
                        potentialLines.Remove(selectedConversation);
                    }
                    else
                    {
                        break;
                    }
                }

                if (allowedSpeakers.Count == 0) { return; }
                speaker = allowedSpeakers[Rand.Int(allowedSpeakers.Count)];
                availableSpeakers.Remove(speaker);
                assignedSpeakers.Add(selectedConversation.speakerIndex, speaker);
            }

            if (baseConversation == null)
            {
                previousConversations.Insert(0, selectedConversation);
                if (previousConversations.Count > MaxPreviousConversations) previousConversations.RemoveAt(MaxPreviousConversations);
            }
            lineList.Add((speaker, selectedConversation.Line));
            CreateConversation(availableSpeakers, assignedSpeakers, selectedConversation, lineList, availableConversations);
        }

        static bool NextLineFailure(NPCConversation selectedConversation, List<Character> availableSpeakers, List<Character> allowedSpeakers, bool ignoreFlags)
        {
            if (selectedConversation.requireNextLine)
            {
                foreach (NPCConversation nextConversation in selectedConversation.Responses)
                {
                    foreach (Character potentialNextSpeaker in availableSpeakers)
                    {
                        if (CheckSpeakerViability(potentialNextSpeaker, nextConversation, allowedSpeakers, ignoreFlags))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            return false;
        }

        static bool CheckSpeakerViability(Character potentialSpeaker, NPCConversation selectedConversation, List<Character> checkedSpeakers, bool ignoreFlags)
        {
            //check if the character has an appropriate job to say the line
            if ((potentialSpeaker.Info?.Job != null && potentialSpeaker.Info.Job.Prefab.OnlyJobSpecificDialog) || selectedConversation.AllowedJobs.Count > 0)
            {
                if (!(potentialSpeaker.Info?.Job?.Prefab is { } speakerJobPrefab)
                    || !selectedConversation.AllowedJobs.Contains(speakerJobPrefab.Identifier)) { return false; }
            }

            //check if the character has all required flags to say the line
            if (!ignoreFlags)
            {
                var characterFlags = GetCurrentFlags(potentialSpeaker);
                if (!selectedConversation.Flags.All(flag => characterFlags.Contains(flag))) { return false; }
            }

            //check if the character is close enough to hear the rest of the speakers
            if (checkedSpeakers.Any(s => !potentialSpeaker.CanHearCharacter(s))) { return false; }

            //check if the character is close enough to see the rest of the speakers (this should be replaced with a more performant method)
            if (checkedSpeakers.Any(s => !potentialSpeaker.CanSeeCharacter(s))) { return false; }

            //check if the character has an appropriate personality
            if (selectedConversation.allowedSpeakerTags.Count > 0)
            {
                if (potentialSpeaker.Info?.PersonalityTrait == null) { return false; }
                if (!selectedConversation.allowedSpeakerTags.Any(t => potentialSpeaker.Info.PersonalityTrait.AllowedDialogTags.Any(t2 => t2 == t))) { return false; }
            }
            else
            {
                if (potentialSpeaker.Info?.PersonalityTrait != null &&
                    !potentialSpeaker.Info.PersonalityTrait.AllowedDialogTags.Contains("none"))
                {
                    return false;
                }
            }
            return true;
        }
        private static NPCConversation GetRandomConversation(List<NPCConversation> conversations, bool avoidPreviouslyUsed)
        {
            if (!avoidPreviouslyUsed)
            {
                return conversations.Count == 0 ? null : conversations[Rand.Int(conversations.Count)];
            }

            List<float> probabilities = new List<float>();
            foreach (NPCConversation conversation in conversations)
            {
                probabilities.Add(GetConversationProbability(conversation));
            }
            return ToolBox.SelectWeightedRandom(conversations, probabilities, Rand.RandSync.Unsynced);
        }

        private static float GetConversationProbability(NPCConversation conversation)
        {
            int index = previousConversations.IndexOf(conversation);
            if (index < 0) return 10.0f;

            return 1.0f - 1.0f / (index + 1);
        }

#if DEBUG
        public static void WriteToCSV()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (Identifier identifier in NPCConversationCollection.Collections[GameSettings.CurrentConfig.Language].Keys)
            {
                foreach (var current in NPCConversationCollection.Collections[GameSettings.CurrentConfig.Language][identifier].Conversations)
                {
                    WriteConversation(sb, current, 0);
                    WriteSubConversations(sb, current.Responses, 1);
                    WriteEmptyRow(sb);
                }
            }

            File.WriteAllText("NPCConversations.csv", sb.ToString());
        }

        private static void WriteConversation(System.Text.StringBuilder sb, NPCConversation conv, int depthIndex)
        {
            sb.Append(conv.speakerIndex);                           // Speaker index
            sb.Append('*');
            sb.Append(depthIndex);                                  // Depth index
            sb.Append('*');
            sb.Append(conv.Line);                                   // Original
            sb.Append('*');
            // Translated
            sb.Append('*');
            sb.Append(string.Join(",", conv.Flags));                // Flags
            sb.Append('*');

            sb.Append(string.Join(',', conv.AllowedJobs));

            sb.Append('*');
            sb.Append(string.Join(",", conv.allowedSpeakerTags));   // Traits
            sb.Append('*');
            sb.Append(conv.minIntensity);                           // Minimum intensity
            sb.Append('*');
            sb.Append(conv.maxIntensity);                           // Maximum intensity
            sb.Append('*');
            // Comments
            sb.AppendLine();
        }

        private static void WriteSubConversations(System.Text.StringBuilder sb, IList<NPCConversation> responses, int depthIndex)
        {
            for (int i = 0; i < responses.Count; i++)
            {
                WriteConversation(sb, responses[i], depthIndex);

                if (responses[i].Responses != null && responses[i].Responses.Length > 0)
                {
                    WriteSubConversations(sb, responses[i].Responses, depthIndex + 1);
                }
            }
        }

        private static void WriteEmptyRow(System.Text.StringBuilder sb)
        {
            for (int i = 0; i < 8; i++)
            {
                sb.Append('*');
            }
            sb.AppendLine();
        }
#endif
    }

}
