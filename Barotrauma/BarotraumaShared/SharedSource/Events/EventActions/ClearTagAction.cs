using System.Xml.Linq;
using NLog.Targets;

namespace Barotrauma
{
    class ClearTagAction : EventAction
    {
        [Serialize("", true)]
        public string Tag { get; set; }

        private bool isFinished;

        public ClearTagAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        public override bool IsFinished(ref string goToLabel) => isFinished;

        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (!string.IsNullOrWhiteSpace(Tag) && ParentEvent.Targets.ContainsKey(Tag))
            {
                ParentEvent.Targets.Remove(Tag);
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ClearTagAction)} -> (Tag: {Tag.ColorizeObject()})";
        }
    }
}