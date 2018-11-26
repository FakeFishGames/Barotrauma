using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static float SkillIncreaseMultiplier = 0.4f;

        private string header;
        
        private float lastSentProgress;
        
        private float fixDurationLowSkill, fixDurationHighSkill;

        private float deteriorationTimer;

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 2, ToolTip = "How fast the condition of the item deteriorates per second.")]
        public float DeteriorationSpeed
        {
            get;
            set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f, DecimalCount = 2, ToolTip = "Minimum initial delay before the item starts to deteriorate.")]
        public float MinDeteriorationDelay
        {
            get;
            set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f, DecimalCount = 2, ToolTip = "Maximum initial delay before the item starts to deteriorate.")]
        public float MaxDeteriorationDelay
        {
            get;
            set;
        }

        [Serialize(50.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, ToolTip = "The item won't deteriorate spontaneously if the condition is below this value. For example, if set to 10, the condition will spontaneously drop to 10 and then stop dropping (unless the item is damaged further by external factors).")]
        public float MinDeteriorationCondition
        {
            get;
            set;
        }

        [Serialize(80.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, ToolTip = "The condition of the item has to be below this before the repair UI becomes usable.")]
        public float ShowRepairUIThreshold
        {
            get;
            set;
        }

        /*private float repairProgress;
        public float RepairProgress
        {
            get { return repairProgress; }
            set
            {
                repairProgress = MathHelper.Clamp(value, 0.0f, 1.0f);
                if (repairProgress >= 1.0f && currentFixer != null) currentFixer.AnimController.Anim = AnimController.Animation.None;
            }
        }*/
        
        private Character currentFixer;
        public Character CurrentFixer
        {
            get { return currentFixer; }
            set
            {
                if (currentFixer == value || item.Condition >= 100.0f) return;
                if (currentFixer != null) currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = value;
            }
        }
        
        public Repairable(Item item, XElement element) 
            : base(item, element)
        {
            IsActive = true;
            canBeSelected = true;

            this.item = item;
            header = element.GetAttributeString("name", "");
            fixDurationLowSkill = element.GetAttributeFloat("fixdurationlowskill", 100.0f);
            fixDurationHighSkill = element.GetAttributeFloat("fixdurationhighskill", 5.0f);
            
            deteriorationTimer = Rand.Range(MinDeteriorationDelay, MaxDeteriorationDelay);
            
            InitProjSpecific(element);
        }
        
        partial void InitProjSpecific(XElement element);
        
        public void StartRepairing(Character character)
        {
            CurrentFixer = character;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime);

            if (CurrentFixer == null)
            {
                if (item.Condition > 0.0f)
                {
                    if (deteriorationTimer > 0.0f)
                    {
                        if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                        {
                            deteriorationTimer -= deltaTime;
#if SERVER
                            if (deteriorationTimer <= 0.0f) { item.CreateServerEvent(this); }
#endif
                        }
                        return;
                    }

                    if (item.Condition > MinDeteriorationCondition)
                    {
                        item.Condition -= DeteriorationSpeed * deltaTime;
                    }
                }
                return;
            }

            if (CurrentFixer.SelectedConstruction != item || !currentFixer.CanInteractWith(item))
            {
                currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = null;
                return;
            }

            UpdateFixAnimation(CurrentFixer);

#if CLIENT
            if (GameMain.Client != null) return;
#endif

            float successFactor = requiredSkills.Count == 0 ? 1.0f : 0.0f;
            foreach (Skill skill in requiredSkills)
            {
                float characterSkillLevel = CurrentFixer.GetSkillLevel(skill.Identifier);
                if (characterSkillLevel >= skill.Level) successFactor += 1.0f / requiredSkills.Count;
                CurrentFixer.Info.IncreaseSkillLevel(skill.Identifier,
                    SkillIncreaseMultiplier * deltaTime / Math.Max(characterSkillLevel, 1.0f),
                     CurrentFixer.WorldPosition + Vector2.UnitY * 100.0f);
            }

            bool wasBroken = item.Condition < item.Prefab.Health;
            float fixDuration = MathHelper.Lerp(fixDurationLowSkill, fixDurationHighSkill, successFactor);
            if (fixDuration <= 0.0f)
            {
                item.Condition = item.Prefab.Health;
            }
            else
            {
                item.Condition += deltaTime / (fixDuration / item.Prefab.Health);
            }

            if (wasBroken && item.Condition >= item.Prefab.Health)
            {
                SteamAchievementManager.OnItemRepaired(item, currentFixer);
            }
        }

        partial void UpdateProjSpecific(float deltaTime);

        private void UpdateFixAnimation(Character character)
        {
            character.AnimController.UpdateUseItem(false, item.WorldPosition + new Vector2(0.0f, 100.0f) * ((item.Condition / 100.0f) % 0.1f));
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(deteriorationTimer);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            deteriorationTimer = msg.ReadSingle();
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            //no need to write anything, just letting the server know we started repairing
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            if (c.Character == null) return;
            StartRepairing(c.Character);
        }
    }
}
