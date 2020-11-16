namespace Barotrauma
{
    public enum InputType
    {
        Select,
        Use,
        Aim,
        Up, Down, Left, Right,
        Attack, Reload,
        Run, Crouch,
        InfoTab, Chat, RadioChat, CrewOrders,
        Ragdoll, Health, Grab,
        SelectNextCharacter,
        SelectPreviousCharacter,
        Voice,
        LocalVoice,
        Deselect,
        Shoot,
        Command,
        ToggleInventory
#if DEBUG
        , 
        NextFireMode,
        PreviousFireMode
#endif
    }
}
