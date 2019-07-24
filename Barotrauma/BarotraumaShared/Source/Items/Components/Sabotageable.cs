using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Sabotageable : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static float SkillIncreasePerSabotage = 5.0f;

        private string header;
        private Repairable repairable;

        private Character currentFixer;
        public Character CurrentFixer
        {
            get => repairable.CurrentFixer;
            set
            {
                if (repairable.CurrentFixer == value) return;
                repairable.CurrentFixer = value;
                if (value != null) value.AnimController.Anim = AnimController.Animation.None;
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

        public void ResetDeterioration()
        {
            // TODO(xxx): repairable.deteriorationTimer = 0.0f; 
            item.Condition = 0.1f * item.Prefab.Health;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime);

            if (CurrentFixer == null)
            {
                return;
            }

            if (CurrentFixer.SelectedConstruction != item || !currentFixer.CanInteractWith(item))
            {
                currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = null;
                return;
            }

            UpdateFixAnimation(CurrentFixer);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            float successFactor = requiredSkills.Count == 0 ? 1.0f : 0.0f;
            
            float fixDuration = MathHelper.Lerp(repairable.FixDurationLowSkill, repairable.FixDurationHighSkill, successFactor);
            if (fixDuration <= 0.0f)
            {
                item.Condition = 0.0f;
            }
            else
            {
                item.Condition -= deltaTime / (fixDuration / item.MaxCondition);
            }
            /*
            if (wasBroken && item.IsFullCondition)
            {
                foreach (Skill skill in requiredSkills)
                {
                    float characterSkillLevel = CurrentFixer.GetSkillLevel(skill.Identifier);
                    CurrentFixer.Info.IncreaseSkillLevel(skill.Identifier,
                        SkillIncreasePerRepair / Math.Max(characterSkillLevel, 1.0f),
                        CurrentFixer.WorldPosition + Vector2.UnitY * 100.0f);
                }
                SteamAchievementManager.OnItemRepaired(item, currentFixer);
                deteriorationTimer = Rand.Range(MinDeteriorationDelay, MaxDeteriorationDelay);
                wasBroken = false;
#if SERVER
                item.CreateServerEvent(this);
#endif
            }*/
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
