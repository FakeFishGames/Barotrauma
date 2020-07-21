using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class EventAction
    {
        public class SubactionGroup
        {
            public string Text;
            public List<EventAction> Actions;
            public bool EndConversation;

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

            public SubactionGroup(ScriptedEvent scriptedEvent, XElement elem)
            {
                Text = elem.Attribute("text")?.Value ?? "";
                Actions = new List<EventAction>();
                EndConversation = elem.GetAttributeBool("endconversation", false);
                foreach (XElement e in elem.Elements())
                {
                    if (e.Name.ToString().Equals("statuseffect", StringComparison.OrdinalIgnoreCase))
                    {
                        DebugConsole.ThrowError($"Error in event prefab \"{scriptedEvent.Prefab.Identifier}\". Status effect configured as a sub action (text: \"{Text}\"). Please configure status effects as child elements of a StatusEffectAction.");
                        continue;
                    }
                    Actions.Add(Instantiate(scriptedEvent, e));
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

            public void Reset()
            {
                Actions.ForEach(a => a.Reset());
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

        public EventAction(ScriptedEvent parentEvent, XElement element)
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

        public abstract void Reset();

        public virtual bool CanBeFinished()
        {
            return true;
        }

        public virtual IEnumerable<EventAction> GetSubActions()
        {
            return Enumerable.Empty<EventAction>();
        }

        public virtual void Update(float deltaTime) { }

        public static EventAction Instantiate(ScriptedEvent scriptedEvent, XElement element)
        {
            Type actionType = null;
            try
            {
                actionType = Type.GetType("Barotrauma." + element.Name, true, true);
                if (actionType == null) { throw new NullReferenceException(); }
            }
            catch
            {
                DebugConsole.ThrowError("Could not find an event class of the type \"" + element.Name + "\".");
                return null;
            }

            ConstructorInfo constructor = actionType.GetConstructor(new[] { typeof(ScriptedEvent), typeof(XElement) });
            try
            {
                return constructor.Invoke(new object[] { scriptedEvent, element }) as EventAction;
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
                return null;
            }
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
