using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static float SkillIncreaseMultiplier = 0.8f;

        private string name;
        
        private float lastSentProgress;

        public bool Fixed
        {
            get { return repairProgress >= 1.0f; }
        }

        private float fixDurationLowSkill, fixDurationHighSkill;

        private float repairProgress;
        public float RepairProgress
        {
            get { return repairProgress; }
            set
            {
                repairProgress = MathHelper.Clamp(value, 0.0f, 1.0f);
                if (repairProgress >= 1.0f && currentFixer != null) currentFixer.AnimController.Anim = AnimController.Animation.None;
            }
        }
        
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
            name = element.GetAttributeString("name", "");
            fixDurationLowSkill = element.GetAttributeFloat("fixdurationlowskill", 100.0f);
            fixDurationHighSkill = element.GetAttributeFloat("fixdurationhighskill", 5.0f);

            InitProjSpecific(element);
        }
        
        partial void InitProjSpecific(XElement element);
        
        public void StartFixing(Character character)
        {
            CurrentFixer = character;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            repairProgress = Math.Max(item.Condition / 100.0f, repairProgress);
            if (CurrentFixer == null) return;

            if (CurrentFixer.SelectedConstruction != item || !currentFixer.CanInteractWith(item))
            {
                currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = null;
                return;
            }

            UpdateFixAnimation(CurrentFixer);

            if (GameMain.Client != null) return;

            float successFactor = requiredSkills.Count == 0 ? 1.0f : 0.0f;
            foreach (Skill skill in requiredSkills)
            {
                float characterSkillLevel = CurrentFixer.GetSkillLevel(skill.Name);
                if (characterSkillLevel >= skill.Level) successFactor += 1.0f / requiredSkills.Count;
                CurrentFixer.Info.IncreaseSkillLevel(skill.Name,
                    SkillIncreaseMultiplier * deltaTime / Math.Max(characterSkillLevel, 1.0f),
                     CurrentFixer.WorldPosition + Vector2.UnitY * 100.0f);
            }

            float fixDuration = MathHelper.Lerp(fixDurationLowSkill, fixDurationHighSkill, successFactor);
            if (fixDuration <= 0.0f)
            {
                repairProgress = 1.0f;
            }
            else
            {
                RepairProgress += deltaTime / fixDuration;
            }

            if (item.Repairables.All(r => r.Fixed))
            {
                item.Condition = 100.0f;
            }

            if (GameMain.Server != null && Math.Abs(RepairProgress - lastSentProgress) > 0.01f)
            {
                lastSentProgress = RepairProgress;
                item.CreateServerEvent(this);
            }

            if (Fixed)
            {
                SteamAchievementManager.OnItemRepaired(item, currentFixer);
            }
        }


        private void UpdateFixAnimation(Character character)
        {
            character.AnimController.UpdateUseItem(false, item.SimPosition + Vector2.UnitY * (repairProgress % 0.1f));
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(repairProgress);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            repairProgress = msg.ReadSingle();
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {

        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
        }
    }
}
