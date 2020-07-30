using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class MissionAction : EventAction
    {
        [Serialize("", true)]
        public string MissionIdentifier { get; set; }

        [Serialize("", true)]
        public string MissionTag { get; set; }

        private bool isFinished;

        public MissionAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element)
        {
            //TODO: use event identifier in the error messages
            if (string.IsNullOrEmpty(MissionIdentifier) && string.IsNullOrEmpty(MissionTag))
            {
                DebugConsole.ThrowError($"Error in event \"{"event identifier goes here"}\": neither MissionIdentifier or MissionTag has been configured.");
            }
            if (!string.IsNullOrEmpty(MissionIdentifier) && !string.IsNullOrEmpty(MissionTag))
            {
                DebugConsole.ThrowError($"Error in event \"{"event identifier goes here"}\": both MissionIdentifier or MissionTag have been configured. The tag will be ignored.");
            }
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession.GameMode is CampaignMode campaign)
            {
                MissionPrefab prefab = null;
                if (!string.IsNullOrEmpty(MissionIdentifier))
                {
                    prefab = campaign.Map.CurrentLocation.UnlockMissionByIdentifier(MissionIdentifier);                    
                }
                else if (!string.IsNullOrEmpty(MissionTag))
                {
                    prefab = campaign.Map.CurrentLocation.UnlockMissionByTag(MissionTag);
                }
                if (campaign is MultiPlayerCampaign mpCampaign)
                {
                    mpCampaign.LastUpdateID++;
                }

                if (prefab != null)
                {
#if CLIENT
                    new GUIMessageBox(string.Empty, TextManager.GetWithVariable("missionunlocked", "[missionname]", prefab.Name), 
                        new string[0], type: GUIMessageBox.Type.InGame, icon: prefab.Icon, relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128))
                    {
                        IconColor = prefab.IconColor
                    };
#else
                    NotifyMissionUnlock(prefab);
#endif
                }
            }
            isFinished = true;            
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MissionAction)} -> ({(string.IsNullOrEmpty(MissionIdentifier) ? MissionTag : MissionIdentifier)})";
        }
        
#if SERVER
        private void NotifyMissionUnlock(MissionPrefab prefab)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                IWriteMessage outmsg = new WriteOnlyMessage();
                outmsg.Write((byte) ServerPacketHeader.EVENTACTION);
                outmsg.Write((byte) EventManager.NetworkEventType.MISSION);
                outmsg.Write(prefab.Identifier);
                GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
            }
        }
#endif
    }
}