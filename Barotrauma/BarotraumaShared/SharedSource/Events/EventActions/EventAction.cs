using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    public abstract class EventAction
    {
        public class SubactionGroup
        {
            public List<EventAction> Actions;
            private int currentSubAction = 0;

            public EventAction CurrentSubAction
            {
                get
                {
                    if (currentSubAction >= 0 && Actions.Count > currentSubAction)
                    {
                        return Actions[currentSubAction];
                    }
                    return null;
                }
            }

            public SubactionGroup(ScriptedEvent scriptedEvent, ContentXElement element)
            {
                SerializableProperty.DeserializeProperties(this, element);

                Actions = new List<EventAction>();
                foreach (var e in element.Elements())
                {
                    if (e.NameAsIdentifier().Equals("statuseffect"))
                    {
                        DebugConsole.ThrowError($"Error in event prefab \"{scriptedEvent.Prefab.Identifier}\". Status effect configured as a sub action. Please configure status effects as child elements of a StatusEffectAction.",
                            contentPackage: element.ContentPackage);
                        continue;
                    }
                    else if (e.NameAsIdentifier().Equals(nameof(OnRoundEndAction)))
                    {
                        DebugConsole.ThrowError($"Error in event prefab \"{scriptedEvent.Prefab.Identifier}\". {nameof(OnRoundEndAction)} configured as a sub action. Please configure it as an action at the end of the event.",
                            contentPackage: element.ContentPackage);
                    }
                    var action = Instantiate(scriptedEvent, e);
                    if (action != null) { Actions.Add(action); }
                }
            }

            public bool IsFinished(ref string goTo)
            {
                if (currentSubAction < Actions.Count)
                {
                    string innerGoTo = null;
                    if (Actions[currentSubAction].IsFinished(ref innerGoTo))
                    {
                        if (string.IsNullOrEmpty(innerGoTo))
                        {
                            currentSubAction++;
                        }
                        else
                        {
                            goTo = innerGoTo;
                            return true;
                        }
                    }
                }
                if (currentSubAction >= Actions.Count)
                {
                    return true;
                }
                return false;
            }

            public bool SetGoToTarget(string goTo)
            {
                currentSubAction = 0;
                for (int i = 0; i < Actions.Count; i++)
                {
                    if (Actions[i].SetGoToTarget(goTo))
                    {
                        currentSubAction = i;
                        return true;
                    }
                }
                return false;
            }

            public void Reset(bool shouldEndConversations = true)
            {
                Actions.ForEach(a => a.Reset(shouldEndConversations));
                currentSubAction = 0;
            }

            public void Update(float deltaTime)
            {
                if (currentSubAction < Actions.Count)
                {
                    Actions[currentSubAction].Update(deltaTime);
                }
            }
        }

        public readonly ScriptedEvent ParentEvent;

        public virtual Mission ParentMission { get; set; }

        public EventAction(ScriptedEvent parentEvent, ContentXElement element)
        {
            ParentEvent = parentEvent;
            SerializableProperty.DeserializeProperties(this, element);
        }

        /// <summary>
        /// Has the action finished.
        /// </summary>
        /// <param name="goToLabel">If null or empty, the event moves to the next action. Otherwise it moves to the specified label.</param>
        /// <returns></returns>
        public abstract bool IsFinished(ref string goToLabel);

        public virtual bool SetGoToTarget(string goTo)
        {
            return false;
        }

        public abstract void Reset(bool shouldEndConversations = true);

        public virtual bool CanBeFinished()
        {
            return true;
        }

        public virtual IEnumerable<EventAction> GetSubActions()
        {
            return Enumerable.Empty<EventAction>();
        }

        public virtual void Update(float deltaTime) { }

        public static EventAction Instantiate(ScriptedEvent scriptedEvent, ContentXElement element)
        {
            Type actionType;
            try
            {
                Identifier typeName = element.Name.ToString().ToIdentifier();
                if (typeName == "TutorialSegmentAction")
                {
                    typeName = nameof(EventObjectiveAction).ToIdentifier();
                }
                else if (typeName == "TutorialHighlightAction")
                {
                    typeName = nameof(HighlightAction).ToIdentifier();
                }

                actionType = ReflectionUtils
                    .GetTypeWithBackwardsCompatibility(ToolBox.BarotraumaAssembly, "Barotrauma", typeName.Value, throwOnError: false, ignoreCase: true)
                    ?? PluginData.LoadedPluginData
                                 .SelectMany(static lpd => lpd.EventActions)
                                 .FirstOrDefault(t => t.Name == typeName.Value);

                if (actionType == null) { throw new NullReferenceException(); }
            }
            catch
            {
                DebugConsole.ThrowError($"Could not find an {nameof(EventAction)} class of the type \"{element.Name}\".",
                    contentPackage: element.ContentPackage);
                return null;
            }

            ConstructorInfo constructor = actionType.GetConstructor([typeof(ScriptedEvent), typeof(ContentXElement)]);
            try
            {
                if (constructor == null)
                {
                    throw new Exception($"Error in scripted event \"{scriptedEvent.Prefab.Identifier}\" - could not find a constructor for the EventAction \"{actionType}\".");
                }
                return constructor.Invoke([scriptedEvent, element]) as EventAction;
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString(),
                    contentPackage: element.ContentPackage);
                return null;
            }
        }

        protected void ApplyTagsToHulls(Entity entity, Identifier hullTag, Identifier linkedHullTag)
        {
            var currentHull = entity switch
            {
                Item item => item.CurrentHull,
                Character character => character.CurrentHull,
                _ => null,
            };
            if (currentHull == null) { return; }

            if (!hullTag.IsEmpty)
            {
                ParentEvent.AddTarget(hullTag, currentHull);
            }
            if (!linkedHullTag.IsEmpty)
            {
                ParentEvent.AddTarget(linkedHullTag, currentHull);
                foreach (var linkedHull in currentHull.GetLinkedEntities<Hull>())
                {
                    ParentEvent.AddTarget(linkedHullTag, linkedHull);
                }
            }            
        }

        protected string GetEventDebugName()
        {
            if (ParentEvent != null)
            {
                return $"the event \"{ParentEvent.Prefab.Identifier}\"";
            }
            else if (ParentMission != null)
            {
                return $"the mission \"{ParentMission.Prefab.Identifier.Value}\"";
            }
            return "an unknown event";
        }

        /// <summary>
        /// Rich test to display in debugdraw
        /// </summary>
        /// <example>
        /// <code>
        /// public override string ToDebugString()
        /// {
        ///     return $"{ToolBox.GetDebugSymbol(isFinished)} SomeAction -> "(someInfo: {info.ColorizeObject()})";
        /// }
        /// </code>
        /// </example>
        /// <returns></returns>
        public virtual string ToDebugString()
        {
            return $"[?] {GetType().Name}";
        }
    }
}
