namespace Barotrauma
{
    partial class MessageBoxAction : EventAction
    {
        public enum ActionType { Create, ConnectObjective, Close, Clear }

        [Serialize(ActionType.Create, IsPropertySaveable.Yes)]
        public ActionType Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Identifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string Tag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Header { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Text { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string IconStyle { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool HideCloseButton { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string CloseOnInput { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnSelectTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnPickUpTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnEquipTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnExitRoomName { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CloseOnInRoomName { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ObjectiveTag { get; set; }

        private bool isFinished = false;

        public MessageBoxAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (Identifier.IsEmpty)
            {
                Identifier = element.GetAttributeIdentifier("id", Identifier.Empty);
            }
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            UpdateProjSpecific();
            isFinished = true;
        }

        partial void UpdateProjSpecific();

        public override bool IsFinished(ref string goToLabel) => isFinished;

        public override void Reset() => isFinished = false;

        public override string ToDebugString() => $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MessageBoxAction)}";
    }
}