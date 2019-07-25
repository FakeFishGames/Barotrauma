using System;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class Sabotageable : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static float SkillIncreasePerSabotage = 3.0f;

        private string header;
        private Repairable repairable;

        // TODO(xxx): Prevent repairable fixing while current fixer is set?
        private Character currentFixer;
        public Character CurrentFixer
        {
            get => currentFixer;
            set
            {
                if (currentFixer == value) return;
                if (currentFixer != null) currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = value;
            }
        }

        public Sabotageable(Item item, XElement element) : base(item, element)
        {
            IsActive = true;
            canBeSelected = true;

            this.item = item;
            header = 
                TextManager.Get(element.GetAttributeString("header", ""), returnNull: true) ??
                TextManager.Get(item.Prefab.ConfigElement.GetAttributeString("header", ""), returnNull: true) ??
                element.GetAttributeString("name", "");
            InitProjSpecific(element);
        }

        public override void OnItemLoaded()
        {
            repairable = item.GetComponent<Repairable>();
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
                return;
            }

            if (CurrentFixer.SelectedConstruction != item || !CurrentFixer.CanInteractWith(item))
            {
                CurrentFixer = null;
                return;
            }

            UpdateFixAnimation(CurrentFixer);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            bool itemWasFunctional = item.Condition > 0.2f;
            float successFactor = DegreeOfSuccess(CurrentFixer);
            
            float fixDuration = MathHelper.Lerp(repairable.FixDurationLowSkill, repairable.FixDurationHighSkill, successFactor);
            if (fixDuration <= 0.0f)
            {
                item.Condition = 0.1f;
            } else {
                item.Condition = Math.Max(0.1f, item.Condition - deltaTime / (fixDuration / item.MaxCondition));
            }

            if (itemWasFunctional && item.Condition <= 0.1f)
            {
                if (repairable != null) {
                    repairable.ResetDeteriorationTimerTo(0f, 0.5f);
                }
                // skill increase
                foreach (Skill skill in requiredSkills)
                {
                    float characterSkillLevel = CurrentFixer.GetSkillLevel(skill.Identifier);
                    CurrentFixer.Info.IncreaseSkillLevel(skill.Identifier, SkillIncreasePerSabotage / Math.Max(characterSkillLevel, 1.0f), CurrentFixer.WorldPosition + Vector2.UnitY * 100.0f);
                }
                // TODO(xxx): Sabotage achievements on steam?
                // SteamAchievementManager.OnItemRepaired(item, currentFixer);
            }
#if SERVER
            item.CreateServerEvent(this);
#endif
        }

        partial void UpdateProjSpecific(float deltaTime);

        private void UpdateFixAnimation(Character character)
        {
            character.AnimController.UpdateUseItem(false, item.WorldPosition + new Vector2(0.0f, 100.0f) * ((item.Condition / item.MaxCondition) % 0.1f));
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
        }
    }
}
