#nullable enable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Barotrauma
{
    /// <summary>
    /// Class that patches plugins so their types are automatically added to GCHelper.
    /// </summary>
    internal static class GCHelperAssemblyPatcher
    {
        private static readonly Harmony GCHarmony = new Harmony("Barotrauma.GCPatch");

        private static readonly MethodInfo PostFixMethod =
            typeof(GCHelperAssemblyPatcher).GetMethod(nameof(CtorPostfix), BindingFlags.Public | BindingFlags.Static)!;

        public static void Patch(Assembly assembly)
        {
            const BindingFlags ctorFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (Type type in assembly.GetTypes())
            {
                if (IsProblematicType(type)) { continue; }
                if (type.IsStatic() || type.IsEnum) { continue; }
                if (type.IsStruct()) { continue; }
                if (type.GetCustomAttribute<CompilerGeneratedAttribute>() != null) { continue; }
                ConstructorInfo?    parameterlessCtor = type.GetConstructor(ctorFlags, binder: null, types: [], modifiers: null);

                if (parameterlessCtor is null)
                {
                    ConstructorInfo[] allConstructors = type.GetConstructors(ctorFlags);
                    if (allConstructors.Length is 0)
                    {
                        DebugConsole.AddWarning($"Unable to patch {type}: No constructor");
                        continue;
                    }

                    foreach (ConstructorInfo info in allConstructors)
                    {
                        PatchCtor(GCHarmony, info);
                    }
                }
                else
                {
                    PatchCtor(GCHarmony, parameterlessCtor);
                }
            }
            return;

            static void PatchCtor(Harmony harmony, ConstructorInfo info)
            {
                try
                {
                    harmony.Patch(info, postfix: new HarmonyMethod(PostFixMethod));
                    DebugConsole.NewMessage($"GCHelper: Patched {info.DeclaringType} constructor.");
                }
                catch (Exception e)
                {
                    // warning because throwing an error here will make the content file not load, also this isn't critical
                    DebugConsole.AddWarning($"Unable to patch {info.DeclaringType} constructor.\n{e}");
                }
            }
        }

        private static readonly string[] ProblematicTypes = ["__StaticArrayInitTypeSize", "ReadOnlySingleElementList"];

        private static bool IsProblematicType(Type type)
        {
            if (type.Namespace != null && type.Namespace.StartsWith("System.")) { return true; }

            string typeName = type.FullName ?? type.Name;
            foreach (string s in ProblematicTypes)
            {
                if (typeName.Contains(s, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ReSharper disable once InconsistentNaming
        public static void CtorPostfix(object __instance)
            => GCHelper.RegisterForWatch(__instance, $"{__instance.GetType().Name}: Tracked automatically");
    }
}