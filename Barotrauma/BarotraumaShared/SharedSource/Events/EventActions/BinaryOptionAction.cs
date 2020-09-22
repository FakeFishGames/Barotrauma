using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class BinaryOptionAction : EventAction
    {
        public SubactionGroup Success = null;
        public SubactionGroup Failure = null;
        protected bool? succeeded = null;

        public BinaryOptionAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element)
        {
            foreach (XElement elem in element.Elements())
            {
                string elemName = elem.Name.LocalName;
                if (elemName.Equals("success", StringComparison.InvariantCultureIgnoreCase))
                {
                    Success ??= new SubactionGroup(ParentEvent, elem);
                }
                else if (elemName.Equals("failure", StringComparison.InvariantCultureIgnoreCase))
                {
                    Failure ??= new SubactionGroup(ParentEvent, elem);
                }
            }
        }
        
        public override IEnumerable<EventAction> GetSubActions()
        {
            IEnumerable<EventAction> actions = Success?.Actions ?? Enumerable.Empty<EventAction>();
            actions = actions.Concat(Failure?.Actions ?? Enumerable.Empty<EventAction>());
            return actions;
        }

        public override bool IsFinished(ref string goTo)
        {
            return DetermineFinished(ref goTo);
        }

        protected bool DetermineFinished()
        {
            string throwaway = null;
            return DetermineFinished(ref throwaway);
        }

        protected bool DetermineFinished(ref string goTo)
        {
            if (succeeded.HasValue)
            {
                if (succeeded.Value)
                {
                    if (Success == null || Success.IsFinished(ref goTo))
                    {
                        return true;
                    }
                }
                else
                {
                    if (Failure == null || Failure.IsFinished(ref goTo))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        protected bool HasBeenDetermined()
        {
            return succeeded.HasValue;
        }

        public override bool SetGoToTarget(string goTo)
        {
            if (Success != null && Success.SetGoToTarget(goTo))
            {
                succeeded = true;
                return true;
            }
            else if (Failure != null && Failure.SetGoToTarget(goTo))
            {
                succeeded = false;
                return true;
            }
            return false;
        }

        public override void Reset()
        {
            Success?.Reset();
            Failure?.Reset(); 
            succeeded = null;
        }

        public override void Update(float deltaTime)
        {
            if (succeeded.HasValue)
            {
                if (succeeded.Value)
                {
                    Success?.Update(deltaTime);
                }
                else
                {
                    Failure?.Update(deltaTime);
                }
            }
            else
            {
                succeeded = DetermineSuccess();
            }
        }

        protected abstract bool? DetermineSuccess();
    }
}