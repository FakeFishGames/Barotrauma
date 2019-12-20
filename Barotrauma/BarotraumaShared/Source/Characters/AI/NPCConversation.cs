using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCConversation
    {
        const int MaxPreviousConversations = 20;

        private class ConversationCollection
        {
            public readonly string Identifier;

            public readonly Dictionary<string, List<NPCConversation>> Conversations;

            public ConversationCollection(string identifier)
            {
                Identifier = identifier;
                Conversations = new Dictionary<string, List<NPCConversation>>();
            }

            public void Add(string language, string filePath, XElement subElement)
            {
                if (!Conversations.ContainsKey(language))
                {
                    Conversations.Add(language, new List<NPCConversation>());
                }
                Conversations[language].Add(new NPCConversation(subElement, filePath));
            }

            public void RemoveByFile(string filePath)
            {
                List<string> keysToRemove = new List<string>();
                foreach (var kpv in Conversations)
                {
                    kpv.Value.RemoveAll(c => c.FilePath == filePath);
                    if (kpv.Value.Count == 0) { keysToRemove.Add(kpv.Key); }
                }

                foreach (var key in keysToRemove)
                {
                    Conversations.Remove(key);
                }
            }
        }

        private static Dictionary<string, ConversationCollection> allConversations = new Dictionary<string, ConversationCollection>();

        public readonly string FilePath;

        public readonly string Line;

        public readonly List<JobPrefab> AllowedJobs;

        public readonly List<string> Flags;

        //The line can only be selected when eventmanager intensity is between these values
        //null = no restriction
        public float? maxIntensity, minIntensity;

        public readonly List<NPCConversation> Responses;
        private readonly int speakerIndex;
        private readonly List<string> allowedSpeakerTags;
        public static void LoadAll(IEnumerable<ContentFile> files)
        {
            foreach (var file in files)
            {
                if (Path.GetExtension(file.Path) == ".csv") continue; // .csv files are not supported
                LoadFromFile(file);
            }
        }

        public static void LoadFromFile(ContentFile file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
            if (doc == null) { return; }

            string language = doc.Root.GetAttributeString("Language", "English");
            string identifier = doc.Root.GetAttributeString("identifier", null);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                DebugConsole.ThrowError($"Conversations file '{file.Path}' has no identifier!");
                return;
            }

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conversation":
                        if (!allConversations.ContainsKey(identifier))
                        {
                            allConversations.Add(identifier, new ConversationCollection(identifier));
                        }
                        allConversations[identifier].Add(language, file.Path, subElement);
                        break;
                    case "personalitytrait":
                        new NPCPersonalityTrait(subElement, file.Path);
                        break;
                }
            }
        }

        public static void RemoveByFile(string filePath)
        {
            List<string> keysToRemove = new List<string>();
            foreach (var kpv in allConversations)
            {
                kpv.Value.RemoveByFile(filePath);
                if (!kpv.Value.Conversations.Any())
                {
                    keysToRemove.Add(kpv.Key);
                }
            }

            foreach (string key in keysToRemove)
            {
                allConversations.Remove(key);
            }

            NPCPersonalityTrait.List.RemoveAll(npt => npt.FilePath == filePath);
        }

        public NPCConversation(XElement element, string filePath)
        {
            FilePath = filePath;

            Line = element.GetAttributeString("line", "");

            speakerIndex = element.GetAttributeInt("speaker", 0);

            AllowedJobs = new List<JobPrefab>();
            string allowedJobsStr = element.GetAttributeString("allowedjobs", "");
            foreach (string allowedJobIdentifier in allowedJobsStr.Split(','))
            {
                if (JobPrefab.Prefabs.ContainsKey(allowedJobIdentifier.ToLowerInvariant()))
                {
                    AllowedJobs.Add(JobPrefab.Prefabs[allowedJobIdentifier.ToLowerInvariant()]);
                }
            }

            Flags = new List<string>(element.GetAttributeStringArray("flags", new string[0]));

            allowedSpeakerTags = new List<string>();
            string allowedSpeakerTagsStr = element.GetAttributeString("speakertags", "");
            foreach (string tag in allowedSpeakerTagsStr.Split(','))
            {
                if (string.IsNullOrEmpty(tag)) continue;
                allowedSpeakerTags.Add(tag.Trim().ToLowerInvariant());                
            }

            if (element.Attribute("minintensity") != null) minIntensity = element.GetAttributeFloat("minintensity", 0.0f);
            if (element.Attribute("maxintensity") != null) maxIntensity = element.GetAttributeFloat("maxintensity", 1.0f);

            Responses = new List<NPCConversation>();
            foreach (XElement subElement in element.Elements())
            {
                Responses.Add(new NPCConversation(subElement, filePath));
            }
        }

        private static List<string> GetCurrentFlags(Character speaker)
        {
            var currentFlags = new List<string>();
            if (Submarine.MainSub != null && Submarine.MainSub.AtDamageDepth) currentFlags.Add("SubmarineDeep");
            if (GameMain.GameSession != null && Timing.TotalTime < GameMain.GameSession.RoundStartTime + 30.0f) currentFlags.Add("Initial");
            if (speaker != null)
            {
                if (speaker.AnimController.InWater) currentFlags.Add("Underwater");
                currentFlags.Add(speaker.CurrentHull == null ? "Outside" : "Inside");

                if (Character.Controlled != null)
                {
                    if (Character.Controlled.CharacterHealth.GetAffliction("psychosis") != null)
                    {
                        currentFlags.Add(speaker != Character.Controlled ? "Psychosis" : "PsychosisSelf");
                    }
                }

                var afflictions = speaker.CharacterHealth.GetAllAfflictions();
                foreach (Affliction affliction in afflictions)
                {
                    var currentEffect = affliction.Prefab.GetActiveEffect(affliction.Strength);
                    if (currentEffect != null && !string.IsNullOrEmpty(currentEffect.DialogFlag) && !currentFlags.Contains(currentEffect.DialogFlag))
                    {
                        currentFlags.Add(currentEffect.DialogFlag);
                    }
                }
            }

            return currentFlags;
        }

        private static List<NPCConversation> previousConversations = new List<NPCConversation>();
        
        public static List<Pair<Character, string>> CreateRandom(List<Character> availableSpeakers)
        {
            Dictionary<int, Character> assignedSpeakers = new Dictionary<int, Character>();
            List<Pair<Character, string>> lines = new List<Pair<Character, string>>();

            CreateConversation(availableSpeakers, assignedSpeakers, null, lines,
                availableConversations: allConversations.Values.SelectMany(cc => cc.Conversations.Where(kpv => kpv.Key == TextManager.Language).SelectMany(kpv => kpv.Value)).ToList());
            return lines;
        }

        public static List<Pair<Character, string>> CreateRandom(List<Character> availableSpeakers, List<string> requiredFlags)
        {
            Dictionary<int, Character> assignedSpeakers = new Dictionary<int, Character>();
            List<Pair<Character, string>> lines = new List<Pair<Character, string>>();
            var availableConversations = allConversations.Values.SelectMany(cc => cc.Conversations.SelectMany(
                    kpv => kpv.Value.Where(conversation => kpv.Key == TextManager.Language && requiredFlags.All(f => conversation.Flags.Contains(f))))).ToList();
            if (availableConversations.Count > 0)
            {
                CreateConversation(availableSpeakers, assignedSpeakers, null, lines, availableConversations: availableConversations, ignoreFlags: true);
            }
            return lines;
        }

        private static void CreateConversation(
            List<Character> availableSpeakers, 
            Dictionary<int, Character> assignedSpeakers, 
            NPCConversation baseConversation, 
            List<Pair<Character, string>> lineList,
            List<NPCConversation> availableConversations,
            bool ignoreFlags = false)
        {
            List<NPCConversation> conversations = baseConversation == null ? availableConversations : baseConversation.Responses;
            if (conversations.Count == 0) return;

            int conversationIndex = Rand.Int(conversations.Count);
            NPCConversation selectedConversation = conversations[conversationIndex];
            if (string.IsNullOrEmpty(selectedConversation.Line)) return;
            
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
                        if ((potentialSpeaker.Info?.Job != null && potentialSpeaker.Info.Job.Prefab.OnlyJobSpecificDialog) ||
                            selectedConversation.AllowedJobs.Count > 0)
                        {
                            if (!selectedConversation.AllowedJobs.Contains(potentialSpeaker.Info?.Job.Prefab)) continue;
                        }

                        //check if the character has all required flags to say the line
                        if (!ignoreFlags)
                        {
                            var characterFlags = GetCurrentFlags(potentialSpeaker);
                            if (!selectedConversation.Flags.All(flag => characterFlags.Contains(flag))) continue;
                        }

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
            CreateConversation(availableSpeakers, assignedSpeakers, selectedConversation, lineList, availableConversations);
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

            foreach (string key in allConversations.Keys)
            {
                foreach (string lang in allConversations[key].Conversations.Keys)
                {
                    if (lang != TextManager.Language) { continue; }
                    foreach (var current in allConversations[key].Conversations[lang])
                    {
                        WriteConversation(sb, current, 0);
                        WriteSubConversations(sb, current.Responses, 1);
                        WriteEmptyRow(sb);
                    }
                }
            }

            StreamWriter file = new StreamWriter(@"NPCConversations.csv");
            file.WriteLine(sb.ToString());
            file.Close();
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

            for (int i = 0; i < conv.AllowedJobs.Count; i++)        // Jobs
            {
                sb.Append(conv.AllowedJobs[i].Identifier);

                if (i < conv.AllowedJobs.Count - 1)
                {
                    sb.Append(",");
                }
            }

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

        private static void WriteSubConversations(System.Text.StringBuilder sb, List<NPCConversation> responses, int depthIndex)
        {
            for (int i = 0; i < responses.Count; i++)
            {
                WriteConversation(sb, responses[i], depthIndex);

                if (responses[i].Responses != null && responses[i].Responses.Count > 0)
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
