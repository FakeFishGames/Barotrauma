#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    [NetworkSerialize]
    internal readonly record struct NetCircuitBoxCursorInfo(Vector2[] RecordedPositions, Option<Vector2> DragStart, Option<Identifier> HeldItem, ushort CharacterID = 0) : INetSerializableStruct;

    internal sealed class CircuitBoxCursor
    {
        public NetCircuitBoxCursorInfo Info;

        public CircuitBoxCursor(NetCircuitBoxCursorInfo info)
        {
            if (Entity.FindEntityByID(info.CharacterID) is Character c)
            {
                Color = GenerateColor(c.Name);
            }

            UpdateInfo(info);
        }

        public void UpdateInfo(NetCircuitBoxCursorInfo newInfo)
        {
            Info = newInfo;

            newInfo.HeldItem.Match(
                some: newIdentifier =>
                    HeldPrefab.Match(
                        some: oldPrefab =>
                        {
                            if (oldPrefab.Identifier == newIdentifier) { return; }
                            SetHeldPrefab(newIdentifier);
                        },
                        none: () => SetHeldPrefab(newIdentifier)
                    ),
                none: () => HeldPrefab = Option.None);

            prevPosition = DrawPosition;

            void SetHeldPrefab(Identifier identifier)
            {
                ItemPrefab? prefab = ItemPrefab.Prefabs.Find(prefab => prefab.Identifier.Equals(identifier));
                HeldPrefab = prefab is null ? Option.None : Option.Some(prefab);
            }
        }

        public Option<ItemPrefab> HeldPrefab { get; private set; } = Option.None;

        public Color Color = Color.White;

        public static Color GenerateColor(string name)
        {
            Random random = new Random(ToolBox.StringToInt(name));
            return ToolBoxCore.HSVToRGB(random.NextSingle() * 360f, 1f, 1f);
        }

        private const float UpdateTimeout = 5f;

        private float updateTimer;
        private float positionTimer;
        private Vector2 prevPosition;
        public Vector2 DrawPosition;

        public bool IsActive => updateTimer < UpdateTimeout;

        public void Update(float deltaTime)
        {
            updateTimer += deltaTime;

            Vector2 finalPosition = Info.RecordedPositions[^1];

            if (positionTimer > 1f)
            {
                DrawPosition = finalPosition;
                prevPosition = Vector2.Zero;
            }
            else
            {
                positionTimer += deltaTime;

                float stepTimer = positionTimer * 10f;
                int targetPositonIndex = (int)MathF.Floor(stepTimer);
                int prevPosIndex = targetPositonIndex - 1;

                Vector2 targetPosition = IsInRange(targetPositonIndex, Info.RecordedPositions.Length)
                    ? Info.RecordedPositions[targetPositonIndex]
                    : finalPosition;

                Vector2 prevTargetPosition = IsInRange(prevPosIndex, Info.RecordedPositions.Length)
                    ? Info.RecordedPositions[prevPosIndex]
                    : prevPosition;

                DrawPosition = Vector2.Lerp(prevTargetPosition, targetPosition, MathHelper.Clamp(stepTimer % 1f, 0f, 1f));
            }

            static bool IsInRange(int index, int length)
                => index >= 0 && index < length;
        }

        public void ResetTimers()
        {
            positionTimer = 0f;
            updateTimer = 0f;
        }
    }
}