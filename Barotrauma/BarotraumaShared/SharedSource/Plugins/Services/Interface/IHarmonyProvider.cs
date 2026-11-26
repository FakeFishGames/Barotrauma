#nullable enable
using HarmonyLib;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Provides a <see href="https://github.com/BepInEx/HarmonyX">HarmonyX</see> instance that plugins can use to patch
    /// game methods at runtime, i.e. run their own code before (prefix), after (postfix) or instead of existing methods.
    /// </summary>
    /// <remarks>
    /// Each plugin gets its own Harmony instance.
    /// When the plugin is unloaded, every patch made through that instance is removed automatically
    /// (<see cref="Harmony.UnpatchSelf"/>), so you do not need to unpatch manually.
    /// <br/><br/>
    /// Prefer the plugin services (hooks, registrars) when they cover what you need, and use Harmony when they don't.
    /// Patches are applied to compiled code, so they may break when the patched method changes in a game update,
    /// and methods that get inlined by the JIT cannot be reliably patched.
    /// <br/><br/>
    /// The methods are instance methods. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly IHarmonyProvider HarmonyProvider = PluginServiceProvider.GetService&lt;IHarmonyProvider&gt;();
    /// </code>
    /// Further reading:
    /// <list type="bullet">
    /// <item><see href="https://harmony.pardeike.net/articles/intro.html">Harmony documentation</see>: patching concepts, prefixes, postfixes, transpilers and injected parameters.</item>
    /// <item><see href="https://github.com/BepInEx/HarmonyX/wiki">HarmonyX wiki</see>: differences and additions in HarmonyX compared to Harmony.</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// Patch with attributes and apply all patches in the plugin assembly with <see cref="PatchAll"/>:
    /// <code>
    /// [HarmonyPatch(typeof(Character), nameof(Character.Kill))]
    /// public static class CharacterKillPatch
    /// {
    ///     // Runs after Character.Kill. "__instance" is the character being killed.
    ///     public static void Postfix(Character __instance, CauseOfDeathType causeOfDeath)
    ///     {
    ///         DebugConsole.NewMessage($"{__instance.Name} died ({causeOfDeath})");
    ///     }
    /// }
    /// </code>
    /// Plugin init:
    /// <code>
    /// HarmonyProvider.PatchAll();
    /// </code>
    /// </example>
    public interface IHarmonyProvider : IPluginService
    {
        /// <summary>
        /// Gets the plugin's Harmony instance, creating it on the first call.
        /// Subsequent calls return the same instance.
        /// </summary>
        /// <remarks>
        /// The instance is created with a unique id (the plugin assembly's full name), and all patches made
        /// through it are removed automatically when the plugin is unloaded.
        /// </remarks>
        /// <example>
        /// Patch a method manually, without attributes:
        /// <code>
        /// Harmony harmony = HarmonyProvider.GetHarmony();
        /// harmony.Patch(
        ///     original: AccessTools.Method(typeof(Character), nameof(Character.Kill)),
        ///     postfix: new HarmonyMethod(typeof(MyPatches), nameof(MyPatches.KillPostfix)));
        /// </code>
        /// <code>
        /// public static class MyPatches
        /// {
        ///     public static void KillPostfix(Character __instance) 
        ///     { 
        ///         /* Do something */ 
        ///     }
        /// }
        /// </code>
        /// </example>
        public Harmony GetHarmony();

        /// <summary>
        /// Searches the plugin assembly for classes annotated with <see cref="HarmonyPatch"/> attributes and applies them
        /// using the plugin's Harmony instance (see <see cref="GetHarmony"/>).
        /// </summary>
        /// <remarks>
        /// Equivalent to <c>GetHarmony().PatchAll(pluginAssembly)</c>
        /// </remarks>
        public void PatchAll();
    }
}