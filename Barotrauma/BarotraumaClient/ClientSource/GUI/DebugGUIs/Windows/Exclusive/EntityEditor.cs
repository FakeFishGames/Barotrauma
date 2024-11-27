using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal sealed class EntityEditor : ExclusiveDebugWindow<Entity>
    {
        private readonly GUIListBox editorList;

        private EntityEditor(Entity entity) : base(entity, createRefreshButton: true)
        {
            editorList = new(new(Vector2.One, Content.RectTransform));

            Refresh();
        }

        public static void TryOpenNew(Entity entity)
        {
            if (!WindowExists(entity))
            {
                new EntityEditor(entity);
            }
        }

        protected override void Refresh()
        {
            editorList.ClearChildren();

            switch (FocusedObject)
            {
                case ISerializableEntity sEntity:
                    new SerializableEntityEditor(editorList.Content.RectTransform, sEntity, false, true);
                    switch (sEntity)
                    {
                        case Item item:
                            item.Components.ForEach(component => new SerializableEntityEditor(editorList.Content.RectTransform, component, false, true));
                            break;
                        case Character character:
                            new SerializableEntityEditor(editorList.Content.RectTransform, character.Params, false, true);
                            new SerializableEntityEditor(editorList.Content.RectTransform, character.AnimController.RagdollParams, false, true);
                            character.AnimController.Limbs.ForEach(limb =>
                            {
                                new SerializableEntityEditor(editorList.Content.RectTransform, limb, false, true);
                                limb.DamageModifiers.ForEach(mod => new SerializableEntityEditor(editorList.Content.RectTransform, mod, false, true));
                            });
                            break;
                    }
                    break;
                case Submarine sub:
                    SubmarineInfo info = sub.Info;
                    if (info.OutpostGenerationParams != null)
                    {
                        new SerializableEntityEditor(editorList.Content.RectTransform, info.OutpostGenerationParams, false, true);
                    }
                    if (info.OutpostModuleInfo != null)
                    {
                        new SerializableEntityEditor(editorList.Content.RectTransform, info.OutpostModuleInfo, false, true);
                    }
                    if (info.GetExtraSubmarineInfo != null)
                    {
                        new SerializableEntityEditor(editorList.Content.RectTransform, info.GetExtraSubmarineInfo, false, true);
                    }
                    break;
            }
        }

        protected override void Update()
        {
            if (!Entity.GetEntities().Contains(FocusedObject))
            {
                Close();
                return;
            }

            base.Update();
        }
    }
}
