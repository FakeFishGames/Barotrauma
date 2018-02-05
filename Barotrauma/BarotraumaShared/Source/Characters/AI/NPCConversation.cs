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
        private static List<NPCConversation> list;

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

        static NPCConversation()
        {
            Load(Path.Combine("Content", "NpcConversations.xml"));
        }

        private static void Load(string file)
        {
            list = new List<NPCConversation>();

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            foreach (XElement subElement in doc.Root.Elements())
            {
                list.Add(new NPCConversation(subElement));
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
                
                //attempt to find speakers for the line, and if it fails, select the next conversation in the list
                int i = 0;
                while (allowedSpeakers.Count == 0 && i < conversations.Count)
                {
                    selectedConversation = conversations[(conversationIndex + i) % conversations.Count];
                    if (string.IsNullOrEmpty(selectedConversation.Line)) return;

                    foreach (Character potentialSpeaker in availableSpeakers)
                    {
                        //check if the character has an appropriate job to say the line
                        if (selectedConversation.AllowedJobs.Count > 0 && !selectedConversation.AllowedJobs.Contains(potentialSpeaker.Info?.Job.Prefab)) continue;
                        //check if the character has all required flags to say the line
                        var characterFlags = GetCurrentFlags(potentialSpeaker);
                        if (!selectedConversation.Flags.All(flag => characterFlags.Contains(flag))) continue;

                        allowedSpeakers.Add(potentialSpeaker);
                    }
                    i++;
                }

                if (allowedSpeakers.Count == 0) return;
                speaker = allowedSpeakers[Rand.Int(allowedSpeakers.Count)];
                availableSpeakers.Remove(speaker);
                assignedSpeakers.Add(selectedConversation.speakerIndex, speaker);
            }


            lineList.Add(Pair<Character, string>.Create(speaker, selectedConversation.Line));
            CreateConversation(availableSpeakers,assignedSpeakers, selectedConversation, lineList);
        }
    }
}
