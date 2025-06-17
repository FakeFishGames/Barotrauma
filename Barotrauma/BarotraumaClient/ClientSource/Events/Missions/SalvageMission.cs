using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;
using FarseerPhysics;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        public override bool DisplayAsCompleted => false;
        public override bool DisplayAsFailed => false;
        
        private void TryShowPickedUpMessage() => HandleMessage(ref pickedUpMessage);

        private void TryShowRetrievedMessage()
        {
            if (DetermineCompleted())
            {
                HandleMessage(ref allRetrievedMessage);
            }
            else
            {
                HandleMessage(ref partiallyRetrievedMessage);
            }
        }
        
        private void HandleMessage(ref LocalizedString message)
        {
            if (!message.IsNullOrEmpty()) { CreateMessageBox(string.Empty, message); }
            //no need to show this again, clear it
            message = string.Empty;
        }

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            
            byte characterCount = msg.ReadByte();
            for (int i = 0; i < characterCount; i++)
            {
                Character character = Character.ReadSpawnData(msg);
                characters.Add(character);
                ushort itemCount = msg.ReadUInt16();
                for (int j = 0; j < itemCount; j++)
                {
                    Item.ReadSpawnData(msg);
                }
            }
            if (characters.Contains(null))
            {
                throw new System.Exception("Error in SalvageMission.ClientReadInitial: character list contains null (mission: " + Prefab.Identifier + ")");
            }
            if (characters.Count != characterCount)
            {
                throw new System.Exception("Error in SalvageMission.ClientReadInitial: character count does not match the server count (" + characters + " != " + characters.Count + "mission: " + Prefab.Identifier + ")");
            }

            foreach (var target in targets)
            {
                bool targetFound = msg.ReadBoolean();
                if (!targetFound) { continue; }

                bool usedExistingItem = msg.ReadBoolean();
                if (usedExistingItem)
                {
                    ushort id = msg.ReadUInt16();
                    target.Item = Entity.FindEntityByID(id) as Item;
                    if (target.Item == null)
                    {
                        throw new System.Exception("Error in SalvageMission.ClientReadInitial: failed to find item " + id + " (mission: " + Prefab.Identifier + ")");
                    }
                }
                else
                {
                    target.Item = Item.ReadSpawnData(msg);
                    target.Item.HighlightColor = GUIStyle.Orange;
                    target.Item.ExternalHighlight = true;

                    ushort parentTargetId = msg.ReadUInt16();
                    if (parentTargetId != Entity.NullEntityID)
                    {
                        target.OriginalContainer = Entity.FindEntityByID(parentTargetId) as Item;
                    }

                    if (target.Item == null)
                    {
                        throw new System.Exception("Error in SalvageMission.ClientReadInitial: spawned item was null (mission: " + Prefab.Identifier + ")");
                    }
                }

                int executedEffectCount = msg.ReadByte();
                for (int i = 0; i < executedEffectCount; i++)
                {
                    int listIndex = msg.ReadByte();
                    int effectIndex = msg.ReadByte();
                    var selectedEffect = target.StatusEffects[listIndex][effectIndex];
                    target.Item.ApplyStatusEffect(selectedEffect, selectedEffect.type, deltaTime: 1.0f, worldPosition: target.Item.Position);
                }

                if (target.Item.body != null && target.Item.CurrentHull == null)
                {
                    target.Item.body.FarseerBody.BodyType = BodyType.Kinematic;
                }
            }
        }

        public override void ClientRead(IReadMessage msg)
        {
            base.ClientRead(msg);
            bool atLeastOneTargetWasRetrieved = false;
            bool showPickedUpMsg = false;
            int targetCount = msg.ReadByte();
            for (int i = 0; i < targetCount; i++)
            {
                var state = (Target.RetrievalState)msg.ReadByte();
                if (i < targets.Count)
                {
                    Target target = targets[i];
                    bool wasRetrieved = target.Retrieved;
                    bool wasPickedUp = target.State == Target.RetrievalState.PickedUp;
                    targets[i].State = state;
                    if (!wasRetrieved && target.Retrieved)
                    {
                        atLeastOneTargetWasRetrieved = true;
                    }
                    else if (!wasPickedUp && target.State == Target.RetrievalState.PickedUp)
                    {
                        showPickedUpMsg = true;
                    }
                }
            }
            if (atLeastOneTargetWasRetrieved)
            {
                TryShowRetrievedMessage();
            }
            if (showPickedUpMsg)
            {
                TryShowPickedUpMessage();
            }
        }

        public override IEnumerable<Entity> HudIconTargets => targets.Where(static t => !t.Retrieved && t.Item?.GetRootInventoryOwner() is not Character { IsLocalPlayer: true }).Select(static t => t.Item);
    }
}
