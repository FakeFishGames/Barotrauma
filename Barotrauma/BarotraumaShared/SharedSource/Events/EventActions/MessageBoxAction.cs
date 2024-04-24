namespace Barotrauma
{
    /// <summary>
    /// Displays a message box, or modifies an existing one.
    /// </summary>
    partial class MessageBoxAction : EventAction
    {
        public enum ActionType { Create, ConnectObjective, Close, Clear }

        [Serialize(ActionType.Create, IsPropertySaveable.Yes, description: "What do you want to do with the message box (Create, ConnectObjective, Close, Clear)?")]
        public ActionType Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional identifier of the tutorial \"segment\" that can be referenced by other event actions.")]
        public Identifier Identifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "An arbitrary tag given to the message box. Only required if you're intending to close or clear the box with another MessageBoxAction later.")]
        public string Tag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Text displayed in the header of the message box. Can be either the text as-is, or a tag referring to a line in a text file.")]
        public Identifier Header { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Text displayed in the body of the message box. Can be either the text as-is, or a tag referring to a line in a text file.")]
        public Identifier Text { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Style of the icon displayed in the corner of the message box (optional). The style must be defined in a UIStyle file.")]
        public string IconStyle { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the button that closes the box be hidden? If it is hidden, you must close the box manually using another MessageBoxAction.")]
        public bool HideCloseButton { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) to show the message box to.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message box is automatically closed on some input (e.g. Select, Use, CrewOrders).")]
        public string CloseOnInput { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message box is automatically closed when the user selects an item that has this tag.")]
        public Identifier CloseOnSelectTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message box is automatically closed when the user picks up an item that has this tag.")]
        public Identifier CloseOnPickUpTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message box is automatically closed when the user equips an item that has this tag.")]
        public Identifier CloseOnEquipTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message box is automatically closed when the user exits a room with this name.")]
        public Identifier CloseOnExitRoomName { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message box is automatically closed when the user is in a room with this name.")]
        public Identifier CloseOnInRoomName { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional tag that will be used to get the text for the objective that is displayed on the screen.")]
        public Identifier ObjectiveTag { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool ObjectiveCanBeCompleted { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ParentObjectiveId { get; set; }

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