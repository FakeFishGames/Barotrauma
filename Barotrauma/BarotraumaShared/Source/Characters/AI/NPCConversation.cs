using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCConversation
    {
        const int MaxPreviousConversations = 20;

        private static List<NPCConversation> list = new List<NPCConversation>();
        
        public readonly string Line;

        public readonly List<JobPrefab> AllowedJobs;

        public readonly List<ConversationFlag> Flags;

        public enum ConversationFlag
        {
            Initial,
            Casual,
            Underwater,
            Inside,
            Outside,
            SubmarineDeep,
        }

        public readonly List<NPCConversation> Responses;
        private readonly int speakerIndex;
        private readonly List<string> allowedSpeakerTags;
        public static void LoadAll(List<string> filePaths)
        {
            foreach (string filePath in filePaths)
            {
                Load(filePath);
            }
        }

        public static void Load(string file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conversation":
                        list.Add(new NPCConversation(subElement));
                        break;
                    case "personalitytrait":
                        new NPCPersonalityTrait(subElement);
                        break;
                }
            }
        }

        public NPCConversation(XElement element)
        {
            Line = element.GetAttributeString("line", "");

            speakerIndex = element.GetAttributeInt("speaker", 0);

            AllowedJobs = new List<JobPrefab>();
            string allowedJobsStr = element.GetAttributeString("allowedjobs", "");
            foreach (string allowedJobName in allowedJobsStr.Split(','))
            {
                var jobPrefab = JobPrefab.List.Find(jp => jp.Name.ToLowerInvariant() == allowedJobName.ToLowerInvariant());
                if (jobPrefab != null) AllowedJobs.Add(jobPrefab);
            }

            Flags = new List<ConversationFlag>();
            string flagsStr = element.GetAttributeString("flags", "");
            foreach (string flag in flagsStr.Split(','))
            {
                ConversationFlag parsedFlag;
                if (Enum.TryParse(flag.Trim(), true, out parsedFlag))
                {
                    Flags.Add(parsedFlag);
                }
            }

            allowedSpeakerTags = new List<string>();
            string allowedSpeakerTagsStr = element.GetAttributeString("speakertags", "");
            foreach (string tag in allowedSpeakerTagsStr.Split(','))
            {
                if (string.IsNullOrEmpty(tag)) continue;
                allowedSpeakerTags.Add(tag.Trim().ToLowerInvariant());                
            }

            Responses = new List<NPCConversation>();
            foreach (XElement subElement in element.Elements())
            {
                Responses.Add(new NPCConversation(subElement));
            }
        }

        private static List<ConversationFlag> GetCurrentFlags(Character speaker)
        {
            var currentFlags = new List<ConversationFlag>();
            if (Submarine.MainSub != null && Submarine.MainSub.AtDamageDepth) currentFlags.Add(ConversationFlag.SubmarineDeep);
            if (GameMain.GameSession != null && Timing.TotalTime < GameMain.GameSession.RoundStartTime + 30.0f) currentFlags.Add(ConversationFlag.Initial);
            if (speaker != null)
            {
                if (speaker.AnimController.InWater) currentFlags.Add(ConversationFlag.Underwater);
                currentFlags.Add(speaker.CurrentHull == null ? ConversationFlag.Outside : ConversationFlag.Inside);
            }

            return currentFlags;
        }

        private static List<NPCConversation> previousConversations = new List<NPCConversation>();
        
        public static List<Pair<Character, string>> CreateRandom(List<Character> availableSpeakers)
        {
            Dictionary<int, Character> assignedSpeakers = new Dictionary<int, Character>();
            List<Pair<Character, string>> lines = new List<Pair<Character, string>>();

            CreateConversation(availableSpeakers, assignedSpeakers, null, lines);
            return lines;
        }

        private static void CreateConversation(
            List<Character> availableSpeakers, 
            Dictionary<int, Character> assignedSpeakers, 
            NPCConversation baseConversation, 
            List<Pair<Character, string>> lineList)
        {
            List<NPCConversation> conversations = baseConversation == null ? list : baseConversation.Responses;
            if (conversations.Count == 0) return;

            int conversationIndex = Rand.Int(conversations.Count);
            NPCConversation selectedConversation = conversations[conversationIndex];
            if (string.IsNullOrEmpty(selectedConversation.Line)) return;

            Character speaker = null;
            //speaker already assigned for this line
            if (assignedSpeakers.ContainsKey(selectedConversation.speakerIndex))
            {
                speaker = assignedSpeakers[selectedConversation.speakerIndex];
            }
            else
            {
                var allowedSpeakers = new List<Character>();

                List<NPCConversation> potentialLines = new List<NPCConversation>(conversations);
                while (potentialLines.Count > 0)
                {
                    //select a random line and attempt to find a speaker for it
                    // and if no valid speaker is found, choose another random line
                    selectedConversation = GetRandomConversation(potentialLines, baseConversation == null);
                    if (selectedConversation == null || string.IsNullOrEmpty(selectedConversation.Line)) return;

                    //speaker already assigned for this line
                    if (assignedSpeakers.ContainsKey(selectedConversation.speakerIndex))
                    {
                        speaker = assignedSpeakers[selectedConversation.speakerIndex];
                        break;
                    }

                    foreach (Character potentialSpeaker in availableSpeakers)
                    {
                        //check if the character has an appropriate job to say the line
                        if (selectedConversation.AllowedJobs.Count > 0 && !selectedConversation.AllowedJobs.Contains(potentialSpeaker.Info?.Job.Prefab)) continue;

                        //check if the character has all required flags to say the line
                        var characterFlags = GetCurrentFlags(potentialSpeaker);
                        if (!selectedConversation.Flags.All(flag => characterFlags.Contains(flag))) continue;

                        //check if the character has an appropriate personality
                        if (selectedConversation.allowedSpeakerTags.Count > 0)
                        {
                            if (potentialSpeaker.Info?.PersonalityTrait == null) continue;
                            if (!selectedConversation.allowedSpeakerTags.Any(t => potentialSpeaker.Info.PersonalityTrait.AllowedDialogTags.Any(t2 => t2 == t))) continue;
                        }
                        else
                        {
                            if (potentialSpeaker.Info?.PersonalityTrait != null &&
                                !potentialSpeaker.Info.PersonalityTrait.AllowedDialogTags.Contains("none"))
                            {
                                continue;
                            }
                        }

                        allowedSpeakers.Add(potentialSpeaker);
                    }

                    if (allowedSpeakers.Count == 0)
                    {
                        potentialLines.Remove(selectedConversation);
                    }
                    else
                    {
                        break;
                    }
                }

                if (allowedSpeakers.Count == 0) return;
                speaker = allowedSpeakers[Rand.Int(allowedSpeakers.Count)];
                availableSpeakers.Remove(speaker);
                assignedSpeakers.Add(selectedConversation.speakerIndex, speaker);
            }

            if (baseConversation == null)
            {
                previousConversations.Insert(0, selectedConversation);
                if (previousConversations.Count > MaxPreviousConversations) previousConversations.RemoveAt(MaxPreviousConversations);
            }
            lineList.Add(new Pair<Character, string>(speaker, selectedConversation.Line));
            CreateConversation(availableSpeakers,assignedSpeakers, selectedConversation, lineList);
        }

        private static NPCConversation GetRandomConversation(List<NPCConversation> conversations, bool avoidPreviouslyUsed)
        {
            if (!avoidPreviouslyUsed)
            {
                return conversations.Count == 0 ? null : conversations[Rand.Int(conversations.Count)];
            }

            float probabilitySum = 0.0f;
            foreach (NPCConversation conversation in conversations)
            {
                probabilitySum += GetConversationProbability(conversation);
            }
            float randomNum = Rand.Range(0.0f, probabilitySum);
            foreach (NPCConversation conversation in conversations)
            {
                float probability = GetConversationProbability(conversation);
                if (randomNum <= probability)
                {
                    return conversation;
                }
                randomNum -= probability;
            }
            return null;
        }

        private static float GetConversationProbability(NPCConversation conversation)
        {
            int index = previousConversations.IndexOf(conversation);
            if (index < 0) return 10.0f;

            return 1.0f - 1.0f / (index + 1);
        }
    }

}
