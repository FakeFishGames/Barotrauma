/*using System.Collections.Generic;
using System.Xml.Linq;
using System;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma.Tutorials
{
    class ContextualTutorial : Tutorial
    {
        public ContextualTutorial(XElement element) : base(element)
        {
            //Name = "ContextualTutorial";
        }

        public static bool Selected = false;

        private Steering navConsole;
        private Reactor reactor;
        private Sonar sonar;
        private Vector2 subStartingPosition;
        private List<Character> crew;
        private Character mechanic;
        private Character engineer;
        private Character injuredMember = null;

        private List<Pair<Character, float>> characterTimeOnSonar;
        private float requiredTimeOnSonar = 5f;

        private float tutorialTimer;

        private bool disableTutorialOnDeficiencyFound = true;

        private float floodTutorialTimer = 0.0f;
        private const float floodTutorialDelay = 2.0f;
        private float medicalTutorialTimer = 0.0f;
        private const float medicalTutorialDelay = 2.0f;

        public override void Initialize()
        {
            base.Initialize();

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].IsTriggered = false;
            }

            characterTimeOnSonar = new List<Pair<Character, float>>();
        }

        public void LoadPartiallyComplete(XElement element)
        {
            int[] completedSegments = element.GetAttributeIntArray("completedsegments", null);

            if (completedSegments == null || completedSegments.Length == 0)
            {
                return;
            }

            if (completedSegments.Length == segments.Count) // Completed all segments
            {
                Stop();
                return;
            }

            for (int i = 0; i < completedSegments.Length; i++)
            {
                segments[completedSegments[i]].IsTriggered = true;
            }
        }

        public void SavePartiallyComplete(XElement element)
        {
            XElement tutorialElement = new XElement("contextualtutorial");
            tutorialElement.Add(new XAttribute("completedsegments", GetCompletedSegments()));
            element.Add(tutorialElement);
        }

        private string GetCompletedSegments()
        {
            string completedSegments = string.Empty;

            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsTriggered)
                {
                    completedSegments += i + ",";
                }
            }

            if (completedSegments.Length > 0)
            {
                completedSegments = completedSegments.TrimEnd(',');
            }

            return completedSegments;
        }

        public override void Start()
        {
            if (!Initialized) return;

            base.Start();
            injuredMember = null;
            activeContentSegment = null;
            tutorialTimer = floodTutorialTimer = medicalTutorialTimer = 0.0f;
            subStartingPosition = Vector2.Zero;
            characterTimeOnSonar.Clear();

            subStartingPosition = Submarine.MainSub.WorldPosition;
            navConsole = Item.ItemList.Find(i => i.HasTag("command"))?.GetComponent<Steering>();
            sonar = navConsole?.Item.GetComponent<Sonar>();
            reactor = Item.ItemList.Find(i => i.HasTag("reactor"))?.GetComponent<Reactor>();

#if DEBUG
            if (reactor == null || navConsole == null || sonar == null)
            {
                infoBox = CreateInfoFrame("Error", "Submarine not compatible with the tutorial:"
                    + "\nReactor - " + (reactor != null ? "OK" : "Tag 'reactor' not found")
                    + "\nNavigation Console - " + (navConsole != null ? "OK" : "Tag 'command' not found")
                    + "\nSonar - " + (sonar != null ? "OK" : "Not found under Navigation Console"), hasButton: true);
                CoroutineManager.StartCoroutine(WaitForErrorClosed());
                return;
            }
#endif
            if (disableTutorialOnDeficiencyFound)
            {
                if (reactor == null || navConsole == null || sonar == null)
                {
                    Stop();
                    return;
                }
            }
            else
            {
                if (navConsole == null) segments[2].IsTriggered = true; // Disable navigation console usage tutorial
                if (reactor == null) segments[5].IsTriggered = true; // Disable reactor usage tutorial
                if (sonar == null) segments[6].IsTriggered = true; // Disable enemy on sonar tutorial
            }

            crew = GameMain.GameSession.CrewManager.GetCharacters().ToList();
            mechanic = CrewMemberWithJob("mechanic");
            engineer = CrewMemberWithJob("engineer");

            Completed = true; // Trigger completed at start to prevent the contextual tutorial from automatically activating on starting new campaigns after this one
            started = true;
        }

#if DEBUG
        private IEnumerable<object> WaitForErrorClosed()
        {
            while (infoBox != null) yield return null;
            Stop();
        }
#endif

        public override void Stop()
        {
            base.Stop();
            characterTimeOnSonar = null;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!started || ContentRunning) return;

            deltaTime *= 0.5f;
                       
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsTriggered || HasObjective(segments[i])) continue;
                if (CheckContextualTutorials(i, deltaTime)) // Found a relevant tutorial, halt finding new ones
                {
                    break;
                }
            }
        }

        private bool CheckContextualTutorials(int index, float deltaTime)
        {
            switch (index)
            {
                case 0: // Welcome: Game Start [Text]
                    if (tutorialTimer < 1.0f)
                    {
                        tutorialTimer += deltaTime;
                        return false;
                    }
                    break;
                case 1: // Command Reactor: 2 seconds after 'Welcome' dismissed and only if no command given to start reactor [Video]
                    if (!segments[0].IsTriggered) return false;
                    if (tutorialTimer < 3.0f)
                    {
                        tutorialTimer += deltaTime;

                        if (HasOrder("operatereactor"))
                        {
                            segments[index].IsTriggered = true;
                            tutorialTimer = 2.5f;
                        }
                        return false;
                    }
                    break;
                case 2: // Nav Console: 2 seconds after 'Command Reactor' dismissed or if nav console is activated [Video]
                    if (!IsReactorPoweredUp()) return false; // Do not advance tutorial based on this segment if reactor has not been powered up
                    if (Character.Controlled?.SelectedConstruction != navConsole.Item)
                    {                       
                        if (tutorialTimer < 4.5f)
                        {
                            tutorialTimer += deltaTime;
                            return false;
                        }
                    }
                    else
                    {
                        tutorialTimer = 4.5f;
                    }

                    TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                    return true;
                case 3: // Objective: Travel ~150 meters and while sub is not flooding [Text]
                    if (Vector2.Distance(subStartingPosition, Submarine.MainSub.WorldPosition) < 8000f || IsFlooding())
                    {
                        return false;
                    }
                    else // Called earlier than others due to requiring specific args
                    {
                        TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                        return true;
                    }
                case 4: // Flood: Hull is breached and sub is taking on water [Video]
                    if (!IsFlooding())
                    {
                        return false;
                    }
                    else if (floodTutorialTimer < floodTutorialDelay)
                    {
                        floodTutorialTimer += deltaTime;
                        return false;
                    }
                    break;
                case 5: // Reactor: Player uses reactor for the first time [Video]
                    if (Character.Controlled?.SelectedConstruction != reactor.Item)
                    {
                        return false;
                    }
                    break;
                case 6: // Enemy on Sonar:  Player witnesses creature signal on sonar for 5 seconds [Video]
                    if (!HasEnemyOnSonarForDuration(deltaTime))
                    {
                        return false;
                    }
                    break;
                case 7: // Degrading1: Any equipment degrades to 50% health or less and player has not assigned any crew to perform maintenance [Text]
                    if ((mechanic == null || mechanic.IsDead) && (engineer == null || engineer.IsDead)) // Both engineer and mechanic are dead or do not exist -> do not display
                    {
                        return false;
                    }

                    bool degradedEquipmentFound = false;

                    foreach (Item item in Item.ItemList)
                    {
                        if (!item.Repairables.Any() || item.Condition > 50.0f) continue;
                        degradedEquipmentFound = true;
                        break;
                    }

                    if (degradedEquipmentFound)
                    {
                        if (HasOrder("repairsystems", "jobspecific"))
                        {
                            segments[index].IsTriggered = true;
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                    break;
                case 8: // Medical: Crewmember is injured but not killed [Video]

                    if (injuredMember == null)
                    {
                        for (int i = 0; i < crew.Count; i++)
                        {
                            Character member = crew[i];
                            if (member.Vitality < member.MaxVitality && !member.IsDead)
                            {
                                injuredMember = member;
                                break;
                            }
                        }

                        return false;
                    }
                    else if (medicalTutorialTimer < medicalTutorialDelay)
                    {
                        medicalTutorialTimer += deltaTime;
                        return false;
                    }
                    else
                    {
                        TriggerTutorialSegment(index, new string[] { injuredMember.Info.DisplayName,
                                (injuredMember.Info.Gender == Gender.Male) ? TextManager.Get("PronounPossessiveMale").ToLower() : TextManager.Get("PronounPossessiveFemale").ToLower() });
                        return true;
                    }
                case 9: // Approach1: Destination is within ~100m [Video]
                    if (Vector2.Distance(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) > 8000f)
                    {
                        return false;
                    }
                    else
                    {
                        TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                        return true;
                    }
                case 10: // Approach2: Sub is docked [Text]
                    if (!Submarine.MainSub.AtEndPosition || Submarine.MainSub.DockedTo.Count == 0)
                    {
                        return false;
                    }
                    break;
            }

            TriggerTutorialSegment(index);
            return true;
        }

        protected override void CheckActiveObjectives(TutorialSegment objective, float deltaTime)
        {
            switch(objective.Id)
            {
                case "ReactorCommand": // Reactor commanded
                    if (!IsReactorPoweredUp())
                    {
                        if (!HasOrder("operatereactor")) return;
                    }
                    break;
                case "NavConsole": // traveled 50 meters
                    if (Vector2.Distance(subStartingPosition, Submarine.MainSub.WorldPosition) < 4000f)
                    {
                        return;
                    }
                    break;
                case "Flood": // Hull breaches repaired
                    if (IsFlooding()) return;
                    break;
                case "Medical":
                    if (injuredMember != null && !injuredMember.IsDead)
                    {
                        if (injuredMember.CharacterHealth.DroppedItem == null) return;
                    }
                    break;
                case "EnemyOnSonar": // Enemy dispatched
                    if (HasEnemyOnSonarForDuration(deltaTime))
                    {
                        return;
                    }
                    break;
                case "Degrading": // Fixed
                    if (mechanic != null && !mechanic.IsDead)
                    {
                        HumanAIController humanAI = mechanic.AIController as HumanAIController;
                        if (mechanic.CurrentOrder?.AITag != "repairsystems" || humanAI.CurrentOrderOption != "jobspecific")
                        {
                            return;
                        }
                    }

                    if (engineer != null && !engineer.IsDead)
                    {
                        HumanAIController humanAI = engineer.AIController as HumanAIController;
                        if (engineer.CurrentOrder?.AITag != "repairsystems" || humanAI.CurrentOrderOption != "jobspecific")
                        {
                            return;
                        }
                    }

                    break;
                case "Approach1": // Wait until docked
                    if (!Submarine.MainSub.AtEndPosition || Submarine.MainSub.DockedTo.Count == 0)
                    {
                        return;
                    }
                    break;
            }

            RemoveCompletedObjective(objective);
        }

        private bool IsReactorPoweredUp()
        {
            float load = 0.0f;
            List<Connection> connections = reactor.Item.Connections;
            if (connections != null && connections.Count > 0)
            {
                foreach (Connection connection in connections)
                {
                    if (!connection.IsPower) continue;
                    foreach (Connection recipient in connection.Recipients)
                    {
                        if (!(recipient.Item is Item it)) continue;

                        PowerTransfer pt = it.GetComponent<PowerTransfer>();
                        if (pt == null) continue;

                        load = Math.Max(load, pt.PowerLoad);
                    }
                }
            }

            return Math.Abs(load + reactor.CurrPowerConsumption) < 10;
        }

        private Character CrewMemberWithJob(string job)
        {
            job = job.ToLowerInvariant();
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i].Info.Job.Prefab.Identifier.ToLowerInvariant() == job) return crew[i];
            }

            return null;
        }

        private bool HasOrder(string aiTag, string option = null)
        {
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i].CurrentOrder?.AITag == aiTag)
                {
                    if (option == null)
                    {
                        return true;
                    }
                    else
                    {
                        HumanAIController humanAI = crew[i].AIController as HumanAIController;
                        return humanAI.CurrentOrderOption == option;
                    }
                }
            }

            return false;
        }

        private bool IsFlooding()
        {
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.ConnectedWall == null || gap.IsRoomToRoom) continue;
                if (gap.ConnectedDoor != null || gap.Open <= 0.0f) continue;
                if (gap.Submarine == null) continue;
                if (gap.Submarine.IsOutpost) continue;
                if (gap.Submarine != Submarine.MainSub) continue;
                if (gap.FlowTargetHull == null || gap.FlowTargetHull.WaterPercentage <= 0.0f) continue;
                return true;
            }

            return false;
        }

        private bool HasEnemyOnSonarForDuration(float deltaTime)
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null || !c.Enabled || !(c.AIController is EnemyAIController)) continue;
                if (sonar.DetectSubmarineWalls && c.AnimController.CurrentHull == null && sonar.Item.CurrentHull != null) continue;
                if (Vector2.DistanceSquared(c.WorldPosition, sonar.Item.WorldPosition) > sonar.Range * sonar.Range)
                {
                    for (int i = 0; i < characterTimeOnSonar.Count; i++)
                    {
                        if (characterTimeOnSonar[i].First == c)
                        {
                            characterTimeOnSonar.RemoveAt(i);
                            break;
                        }
                    }

                    continue;
                }

                Pair<Character, float> pair = characterTimeOnSonar.Find(ct => ct.First == c);
                if (pair != null)
                {
                    pair.Second += deltaTime;
                }
                else
                {
                    characterTimeOnSonar.Add(new Pair<Character, float>(c, deltaTime));
                }
            }

            return characterTimeOnSonar.Find(ct => ct.Second >= requiredTimeOnSonar && !ct.First.IsDead) != null;
        }

        protected override void TriggerTutorialSegment(int index, params object[] args)
        {
            base.TriggerTutorialSegment(index, args);

            for (int i = 0; i < segments.Count; i++)
            {
                if (!segments[i].IsTriggered) return;
            }

            CoroutineManager.StartCoroutine(WaitToStop()); // Completed
        }

        private IEnumerable<object> WaitToStop()
        {
            while (ContentRunning) yield return null;
            Stop();
        }
    }
}*/
