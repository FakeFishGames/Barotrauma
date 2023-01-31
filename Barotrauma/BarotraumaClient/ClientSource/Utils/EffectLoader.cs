using Microsoft.Xna.Framework.Graphics;
namespace Barotrauma;

static class EffectLoader
{
    public static Effect Load(string path)
        => GameMain.Instance.Content.Load<Effect>(path
#if LINUX || OSX
                        +"_opengl"
#endif
        );
}
