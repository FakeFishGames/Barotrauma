using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class Objective
        {
            public Traitor Traitor { get; private set; }

            private readonly List<Goal> allGoals = new List<Goal>();
            private readonly List<Goal> pendingGoals = new List<Goal>();
            private readonly List<Goal> completedGoals = new List<Goal>();

            public bool IsCompleted => pendingGoals.Count <= 0;
            public bool IsPartiallyCompleted => completedGoals.Count > 0;
            public bool IsStarted { get; private set; } = false;

            public bool IsEnemy(Character character) => pendingGoals.Any(goal => goal.IsEnemy(character));

            public string InfoText { get; private set; }

            public virtual string GoalInfoFormatId { get; set; } = "TraitorObjectiveGoalInfoFormat";
            public string GoalInfos => string.Join("", allGoals.ConvertAll(goal => TextManager.GetWithVariables(GoalInfoFormatId, new string[] {
                "[statustext]"
            }, new string[] {
                goal.StatusText
            })));

            public virtual string StartMessageTextId { get; set; } = "TraitorObjectiveStartMessage";
            public virtual IEnumerable<string> StartMessageKeys => new string[] { "[traitorgoalinfos]" };
            public virtual IEnumerable<string> StartMessageValues => new string[] { GoalInfos };

            public virtual string StartMessageText => TextManager.GetWithVariables(StartMessageTextId, StartMessageKeys.ToArray(), StartMessageValues.ToArray());

            public virtual string StartMessageServerTextId { get; set; } = "TraitorObjectiveStartMessageServer";
            public virtual IEnumerable<string> StartMessageServerKeys => StartMessageKeys.Concat(new string[] { "[traitorname]" });
            public virtual IEnumerable<string> StartMessageServerValues => StartMessageValues.Concat(new string[] { Traitor?.Character?.Name ?? "(unknown)" });

            public virtual string StartMessageServerText => TextManager.GetWithVariables(StartMessageServerTextId, StartMessageServerKeys.ToArray(), StartMessageServerValues.ToArray());

            public virtual string EndMessageSuccessTextId { get; set; } = "TraitorObjectiveEndMessageSuccess";
            public virtual string EndMessageSuccessDeadTextId { get; set; } = "TraitorObjectiveEndMessageSuccessDead";
            public virtual string EndMessageSuccessDetainedTextId { get; set; } = "TraitorObjectiveEndMessageSuccessDetained";
            public virtual string EndMessageFailureTextId { get; set; } = "TraitorObjectiveEndMessageFailure";
            public virtual string EndMessageFailureDeadTextId { get; set; } = "TraitorObjectiveEndMessageFailureDead";
            public virtual string EndMessageFailureDetainedTextId { get; set; } = "TraitorObjectiveEndMessageFailureDetained";

            public virtual IEnumerable<string> EndMessageKeys => new string[] { "[traitorname]", "[traitorgoalinfos]" };
            public virtual IEnumerable<string> EndMessageValues => new string[] { Traitor?.Character?.Name ?? "(unknown)", GoalInfos };
            public virtual string EndMessageText
            {
                get
                {
                    var traitorIsDead = Traitor.Character.IsDead;
                    var traitorIsDetained = Traitor.Character.LockHands;
                    var messageId = IsCompleted
                        ? (traitorIsDead ? EndMessageSuccessDeadTextId : traitorIsDetained ? EndMessageSuccessDetainedTextId : EndMessageSuccessTextId)
                        : (traitorIsDead ? EndMessageFailureDeadTextId : traitorIsDetained ? EndMessageFailureDetainedTextId : EndMessageFailureTextId);
                    return TextManager.ReplaceGenderPronouns(TextManager.GetWithVariables(messageId, EndMessageKeys.ToArray(), EndMessageValues.ToArray()), Traitor.Character.Info.Gender);
                }
            }

            public bool Start(GameServer server, Traitor traitor)
            {
                Traitor = traitor;
                for (var i = 0; i < pendingGoals.Count;)
                {
                    var goal = pendingGoals[i];
                    if (goal.Start(server, traitor))
                    {
                        ++i;
                    }
                    else
                    {
                        completedGoals.Add(goal);
                        pendingGoals.RemoveAt(i);
                    }
                }
                if (pendingGoals.Count <= 0)
                {
                    return false;
                }
                IsStarted = true;
                Client traitorClient = server.ConnectedClients.Find(c => c.Character == traitor.Character);
                server.SendDirectChatMessage(StartMessageText, traitorClient);
                server.SendDirectChatMessage(ChatMessage.Create(null, StartMessageText, ChatMessageType.MessageBox, null), traitorClient);

                Traitor.Character.TraitorCurrentObjective = GoalInfos;
                server.SendTraitorCurrentObjective(traitorClient, Traitor.Character.TraitorCurrentObjective);

                return true;
            }

            public void End(GameServer server)
            {
                Client traitorClient = server.ConnectedClients.Find(c => c.Character == Traitor.Character);
                GameMain.Server.SendDirectChatMessage(EndMessageText, traitorClient);
                GameMain.Server.SendDirectChatMessage(ChatMessage.Create(null, EndMessageText, ChatMessageType.MessageBox, null), traitorClient);

                // Traitor.Character.TraitorCurrentObjective = "";
                // server.SendTraitorCurrentObjective(traitorClient, Traitor.Character.TraitorCurrentObjective);
            }

            public void Update(float deltaTime)
            {
                if (!IsStarted)
                {
                    return;
                }
                int completedCount = completedGoals.Count;
                for (int i = 0; i < pendingGoals.Count;)
                {
                    var goal = pendingGoals[i];
                    goal.Update(deltaTime);
                    if (!goal.IsCompleted)
                    {
                        ++i;
                    }
                    else
                    {
                        completedGoals.Add(goal);
                        pendingGoals.RemoveAt(i);
                        if (GameMain.Server != null)
                        {
                            Client traitorClient = GameMain.Server.ConnectedClients?.Find(c => c.Character == goal.Traitor.Character);
                            if (traitorClient != null)
                            {
                                GameMain.Server.SendDirectChatMessage(goal.CompletedText, traitorClient);
                                GameMain.Server.SendDirectChatMessage(ChatMessage.Create(null, goal.CompletedText, ChatMessageType.MessageBox, null), traitorClient);

                                Traitor.Character.TraitorCurrentObjective = GoalInfos;
                                GameMain.Server.SendTraitorCurrentObjective(traitorClient, Traitor.Character.TraitorCurrentObjective);
                            }
                        }
                    }
                }
            }

            public Objective(string infoText, params Goal[] goals)
            {
                InfoText = infoText;
                allGoals.AddRange(goals);
                pendingGoals.AddRange(goals);
            }
        }
    }
}
