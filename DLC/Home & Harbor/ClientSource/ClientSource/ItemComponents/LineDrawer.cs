using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor;

internal partial class LineDrawer : Powered, IDrawableComponent
{
    private Vector2 drawSize;
    public Vector2 DrawSize => drawSize;

    private Sprite? lineSprite, hitSprite;

    private RoundSound? targetedSound, notTargetedSound;
    private SoundChannel? soundChannel;

    private float? lastReceivedRange;
    private float prevRange;
    private float rangeInterpolationTimer;

    private double lastBeingUsedForTargetingTime;
    private double lastTargetFoundTime;

    private ParticlePrefab? lineParticlePrefab;

    partial void InitProjSpecific(Item item, ContentXElement element)
    {
        foreach (var subElement in element.Elements())
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "targetedsound":
                    targetedSound = RoundSound.Load(subElement);
                    break;
                case "nottargetedsound":
                    notTargetedSound = RoundSound.Load(subElement);
                    break;
                case "hitsprite":
                    hitSprite = new Sprite(subElement);
                    break;
                case "linesprite":
                    lineSprite = new Sprite(subElement);
                    break;
            }
        }
        _drawInLightMap.Add(this);
    }

    public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth, Color? overrideColor)
    {
        if (!IsOn || !HasPower) { return; }

        if (item.Submarine is { IsAboveLevel: true }) { return; }

        if (HideWhenNotUsed && turretComponent is { IsBeingActivelyUsed: false }) { return; }

        float rotation = item.RotationRadWithFlipping;
        Vector2 drawPos = item.DrawPosition;
        if (Parent is Turret turret)
        {
            rotation = turret.BarrelRotation;
            drawPos = turret.GetBarrelFiringPosition();
        }

        rotation += rotationRad;
        drawPos += MathUtils.RotatePoint(Offset, rotation);
        drawPos = drawPos.FlipY();

        //the ammo loaded in the turret can change how the line looks
        TurretAmmo? turretAmmo = turretComponent?.GetActiveTurretAmmo();

        Color color = (turretAmmo?.LineColor ?? Color) * alphaMultiplier;

        Vector2 lineEndPos = drawPos + MathUtils.RotatedUnitXRadians(rotation) * currentRange;
        drawSize = lineEndPos - drawPos;

        //ensure the line never goes below 5 pixels on screen,
        //that will cause visual artifacts / the line becoming invisible as the visual parts are shunk down below 1 pixel
        float minDrawWidth = 5.0f / Screen.Selected.Cam.Zoom;
        float drawWidth = Math.Max(width * (turretAmmo?.LineWidthMultiplier ?? 1.0f), minDrawWidth);

        Identifier lineParticle = turretAmmo is { LineParticle.IsEmpty: false } ? turretAmmo.LineParticle : LineParticle;
        if (!lineParticle.IsEmpty)
        {
            if (lineParticlePrefab == null || lineParticlePrefab.Identifier != lineParticle)
            {
                if (!ParticlePrefab.Prefabs.TryGet(lineParticle, out lineParticlePrefab))
                {
                    DebugConsole.ThrowError($"LineDrawer component in item \"{item.Name}\" references particle prefab \"{lineParticle}\" that couldn't be found.");
                }
            }
            if (lineParticlePrefab != null)
            {
                GUI.DrawLine(spriteBatch, lineParticlePrefab.Sprites.First(), drawPos, lineEndPos, color, width: (int)drawWidth);
            }
        }
        else if (lineSprite != null)
        {
            GUI.DrawLine(spriteBatch, lineSprite, drawPos, lineEndPos, color, width: (int)drawWidth);
        }
        else
        {
            GUI.DrawLine(spriteBatch, drawPos, lineEndPos, color, width: drawWidth);
        }
        hitSprite?.Draw(spriteBatch, lineEndPos, color, rotation);
    }
    
    partial void UpdateProjSpecific(float deltaTime, Camera cam)
    {
        if (turretComponent?.User is Character user && user == Character.Controlled)
        {
            if (Math.Abs(lastSentRange - currentRange) > SendRangeThreshold && 
                Timing.TotalTimeUnpaused > lastSentRangeTime + SendRangeInterval)
            {
                lastSentRange = currentRange;
                lastSentRangeTime = Timing.TotalTimeUnpaused;
                item.CreateClientEvent(this);
            }
        }

        if (lastReceivedRange.HasValue)
        {
            rangeInterpolationTimer += deltaTime;
            //quick and dirty interpolation of the range to make the movement of the line a bit less janky when other clients move it
            currentRange = MathHelper.Lerp(prevRange, lastReceivedRange.Value, rangeInterpolationTimer / SendRangeInterval);
            if (Math.Abs(currentRange - lastReceivedRange.Value) < 10.0f)
            {
                currentRange = lastReceivedRange.Value;
                lastReceivedRange = null;
                rangeInterpolationTimer = 0.0f;
            }
        }

        Entity soundSourceEntity = item;
        //if the item has a turret component, play the sounds based on whether the player is using it
        if (turretComponent != null)
        {
            if (turretComponent.User == null || 
                Character.Controlled != turretComponent.User ||
                Character.Controlled.ViewTarget != turretComponent.Item)
            {
                soundChannel?.FadeOutAndDispose();
                return; 
            }
            soundSourceEntity = turretComponent.User.ViewTarget ?? turretComponent.User;
        }
        //stop playing once nothing is using the item for targeting
        if (Timing.TotalTimeUnpaused > lastBeingUsedForTargetingTime + 0.1)
        {
            soundChannel?.FadeOutAndDispose();
            return;
        }

        RoundSound? sound = Timing.TotalTimeUnpaused > lastTargetFoundTime + 0.1 ? notTargetedSound : targetedSound;
        if (sound == null) { return; }
        if (sound != null)
        {
            if (soundChannel == null || !soundChannel.IsPlaying || sound.Sound != soundChannel.Sound)
            {
                soundChannel?.FadeOutAndDispose();
                soundChannel = SoundPlayer.PlaySound(sound, soundSourceEntity.WorldPosition);
                if (soundChannel != null) { soundChannel.Looping = true; }
            }
            else
            {
                soundChannel.Position = new Vector3(soundSourceEntity.WorldPosition, 0.0f);
            }
        }
    }

    public void RegisterBeingUsedForTargeting()
    {
        lastBeingUsedForTargetingTime = Timing.TotalTimeUnpaused;
    }

    protected override void RemoveComponentSpecific()
    {
        base.RemoveComponentSpecific();
        lineSprite?.Remove();
        lineSprite = null;
        hitSprite?.Remove();
        hitSprite = null;
        _drawInLightMap.Remove(this);
    }

    public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteSingle(currentRange);
    }

    public void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        float newRange = msg.ReadSingle();
        //if currently operated by this player, don't update the range based on the server's value to avoid conflicts with the player's input
        if (turretComponent?.User != Character.Controlled)
        {
            prevRange = currentRange;
            lastReceivedRange = newRange;
            rangeInterpolationTimer = 0.0f;
        }
    }
}