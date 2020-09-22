using Microsoft.Xna.Framework;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class TriggerAction : EventAction
    {
        [Serialize("", true, description: "Tag of the first entity that will be used for trigger checks.")]
        public string Target1Tag { get; set; }

        [Serialize("", true, description: "Tag of the second entity that will be used for trigger checks.")]
        public string Target2Tag { get; set; }

        [Serialize("", true, description: "If set, the first target has to be within an outpost module of this type.")]
        public string TargetModuleType { get; set; }

        [Serialize("", true, description: "Tag to apply to the first entity when the trigger check succeeds.")]
        public string ApplyToTarget1 { get; set; }

        [Serialize("", true, description: "Tag to apply to the second entity when the trigger check succeeds.")]
        public string ApplyToTarget2 { get; set; }

        [Serialize(0.0f, true, description: "Range both entities must be within to activate the trigger.")]
        public float Radius { get; set; }

        [Serialize(true, true, description: "If true, characters who are being targeted by some enemy cannot trigger the event.")]
        public bool DisableInCombat { get; set; }

        private float distance;
        
        public TriggerAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) 
        {
            TargetModuleType = TargetModuleType?.ToLowerInvariant();
        }

        private bool isFinished = false;
        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isRunning = false;
            isFinished = false;
        }

        public bool isRunning = false;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            isRunning = true;

            var targets1 = ParentEvent.GetTargets(Target1Tag);
            if (!targets1.Any()) { return; }
            
            foreach (Entity e1 in targets1)
            {
                if (DisableInCombat && IsInCombat(e1)) { continue; }
                if (!string.IsNullOrEmpty(TargetModuleType))
                {
                    if (IsCloseEnoughToHull(e1, out Hull hull))
                    {
                        Trigger(e1, hull);
                        return;
                    }
                    continue;
                }

                var targets2 = ParentEvent.GetTargets(Target2Tag);
                
                foreach (Entity e2 in targets2)
                {
                    if (e1 == e2) { continue; }
                    if (DisableInCombat && IsInCombat(e2)) { continue; }

                    Vector2 pos1 = e1.WorldPosition;
                    Vector2 pos2 = e2.WorldPosition;
                    distance = Vector2.Distance(pos1, pos2);
                    if (((e1 is MapEntity m1) && Submarine.RectContains(m1.WorldRect, pos2)) ||
                        ((e2 is MapEntity m2) && Submarine.RectContains(m2.WorldRect, pos1)) ||
                        Vector2.DistanceSquared(pos1, pos2) < Radius * Radius)
                    {
                        Trigger(e1, e2);
                        return;
                    }
                }
            }            
        }

        private bool IsCloseEnoughToHull(Entity e, out Hull hull)
        {
            hull = null;
            if (Radius <= 0)
            {
                if (e is Character character && character.CurrentHull != null && character.CurrentHull.OutpostModuleTags.Contains(TargetModuleType))
                {
                    hull = character.CurrentHull;
                    return true;
                }
                else if (e is Item item && item.CurrentHull != null && item.CurrentHull.OutpostModuleTags.Contains(TargetModuleType))
                {
                    hull = item.CurrentHull;
                    return true;
                }
                return false;
            }
            else
            {
                foreach (Hull potentialHull in Hull.hullList)
                {
                    if (!potentialHull.OutpostModuleTags.Contains(TargetModuleType)) { continue; }

                    Rectangle hullRect = potentialHull.WorldRect;
                    hullRect.Inflate(Radius, Radius);
                    if (Submarine.RectContains(hullRect, e.WorldPosition))
                    {
                        hull = potentialHull;
                        return true;
                    }
                }
                return false;
            }
        }

        private bool IsInCombat(Entity entity)
        {
            if (!(entity is Character character)) { return false; }
            foreach (Character c in Character.CharacterList)
            {
                if (c.IsDead || c.Removed || c.IsIncapacitated || !c.Enabled) { continue; }
                if (c.IsBot && c.AIController is HumanAIController humanAi)
                {
                    if (humanAi.ObjectiveManager.CurrentObjective is AIObjectiveCombat combatObjective &&
                        combatObjective.Enemy == character)
                    {
                        return true;
                    }
                }
                else if (c.AIController is EnemyAIController enemyAI && (enemyAI.State == AIState.Aggressive || enemyAI.State == AIState.Attack))
                {
                    if (enemyAI.SelectedAiTarget?.Entity == character || c.CurrentHull == character.CurrentHull)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void Trigger(Entity entity1, Entity entity2)
        {
            if (!string.IsNullOrEmpty(ApplyToTarget1))
            {
                ParentEvent.AddTarget(ApplyToTarget1, entity1);
            }
            if (!string.IsNullOrEmpty(ApplyToTarget2))
            {
                ParentEvent.AddTarget(ApplyToTarget2, entity2);
            }

            isRunning = false;
            isFinished = true;
        }

        public override string ToDebugString()
        {
            if (string.IsNullOrEmpty(TargetModuleType))
            {
                return $"{ToolBox.GetDebugSymbol(isFinished, isRunning)} {nameof(TriggerAction)} -> (Distance: {((int)distance).ColorizeObject()}, Radius: {Radius.ColorizeObject()}, TargetTags: {Target1Tag.ColorizeObject()}, {Target2Tag.ColorizeObject()})";
            }
            else
            {
                return $"{ToolBox.GetDebugSymbol(isFinished, isRunning)} {nameof(TriggerAction)} -> (TargetTags: {Target1Tag.ColorizeObject()}, {TargetModuleType.ColorizeObject()})";
            }
        }
    }
}