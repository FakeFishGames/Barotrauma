using System;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class RespawnManager
    {
        private DateTime lastShuttleLeavingWarningTime;

        public int PendingRespawnCount

        {
            get; private set;
        }

        public int RequiredRespawnCount
        {
            get; private set;
        }

        public bool ForceSpawnInMainSub
        {
            get; private set;
        }

        partial void UpdateTransportingProjSpecific(float deltaTime)
        {
            if (GameMain.Client?.Character == null || GameMain.Client.Character.Submarine != RespawnShuttle) { return; }
            if (!ReturnCountdownStarted) { return; }

            //show a warning when there's 20 seconds until the shuttle leaves
            if ((ReturnTime - DateTime.Now).TotalSeconds < 20.0f && 
                (DateTime.Now - lastShuttleLeavingWarningTime).TotalSeconds > 30.0f)
            {
                lastShuttleLeavingWarningTime = DateTime.Now;
                GameMain.Client.AddChatMessage("ServerMessage.ShuttleLeaving", ChatMessageType.Server);
            }
        }

        private CoroutineHandle respawnPromptCoroutine;

        public void ShowRespawnPromptIfNeeded(float delay = 5.0f)
        {
            if (!UseRespawnPrompt) { return; }
            if (CoroutineManager.IsCoroutineRunning(respawnPromptCoroutine) || GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "respawnquestionprompt")) 
            { 
                return;
            }

            respawnPromptCoroutine = CoroutineManager.Invoke(() =>
            {
                if (Character.Controlled != null || (!(GameMain.GameSession?.IsRunning ?? false))) { return; }

                LocalizedString text =
                    TextManager.GetWithVariable("respawnskillpenalty", "[percentage]", ((int)(SkillReductionOnDeath * 100)).ToString())
                    + "\n\n" + TextManager.Get("respawnquestionprompt");

                var respawnPrompt = new GUIMessageBox(
                    TextManager.Get("tutorial.tryagainheader"), text,
                    new LocalizedString[] { TextManager.Get("respawnquestionpromptrespawn"), TextManager.Get("respawnquestionpromptwait") })
                {
                    UserData = "respawnquestionprompt"
                };
                respawnPrompt.Buttons[0].OnClicked += (btn, userdata) =>
                {
                    GameMain.Client?.SendRespawnPromptResponse(waitForNextRoundRespawn: false);
                    respawnPrompt.Close();
                    return true;
                };
                respawnPrompt.Buttons[1].OnClicked += (btn, userdata) =>
                {
                    GameMain.Client?.SendRespawnPromptResponse(waitForNextRoundRespawn: true);
                    respawnPrompt.Close();
                    return true;
                };
            }, delay: delay);            
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            bool respawnPromptPending = false;
            var newState = (State)msg.ReadRangedInteger(0, Enum.GetNames(typeof(State)).Length);
            ForceSpawnInMainSub = false;
            switch (newState)
            {
                case State.Transporting:
                    ReturnCountdownStarted  = msg.ReadBoolean();
                    maxTransportTime        = msg.ReadSingle();
                    float transportTimeLeft = msg.ReadSingle();

                    ReturnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(transportTimeLeft * 1000.0f));
                    RespawnCountdownStarted = false;
                    if (CurrentState != newState)
                    {
                        CoroutineManager.StopCoroutines("forcepos");
                    }
                    break;
                case State.Waiting:
                    PendingRespawnCount = msg.ReadUInt16();
                    RequiredRespawnCount = msg.ReadUInt16();
                    respawnPromptPending = msg.ReadBoolean();
                    RespawnCountdownStarted = msg.ReadBoolean();
                    ForceSpawnInMainSub = msg.ReadBoolean();
                    ResetShuttle();
                    float newRespawnTime = msg.ReadSingle();
                    RespawnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(newRespawnTime * 1000.0f));
                    break;
                case State.Returning:
                    RespawnCountdownStarted = false;
                    break;
            }
            CurrentState = newState;

            if (respawnPromptPending)
            {
                GameMain.Client.HasSpawned = true;
                ShowRespawnPromptIfNeeded(delay: 1.0f);
            }

            msg.ReadPadBits();
        }
    }
}
