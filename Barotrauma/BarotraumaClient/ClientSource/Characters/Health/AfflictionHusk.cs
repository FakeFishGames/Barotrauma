namespace Barotrauma
{
    partial class AfflictionHusk : Affliction
    {
        private InfectionState? prevDisplayedMessage;
        partial void UpdateMessages()
        {
            if (Prefab is AfflictionPrefabHusk { SendMessages: false }) { return; }
            if (prevDisplayedMessage.HasValue && prevDisplayedMessage.Value == State) { return; }

            switch (State)
            {
                case InfectionState.Dormant:
                    if (Strength < DormantThreshold * 0.5f)
                    {
                        return;
                    }
                    GUI.AddMessage(TextManager.Get("HuskDormant"), GUI.Style.Red);
                    break;
                case InfectionState.Transition:
                    GUI.AddMessage(TextManager.Get("HuskCantSpeak"), GUI.Style.Red);
                    break;
                case InfectionState.Active:
                    if (character.Params.UseHuskAppendage)
                    {
                        GUI.AddMessage(TextManager.GetWithVariable("HuskActivate", "[Attack]", GameMain.Config.KeyBindText(InputType.Attack)), GUI.Style.Red);
                    }
                    break;
                case InfectionState.Final:
                default:
                    break;
            }
            prevDisplayedMessage = State;
        }
    }
}
