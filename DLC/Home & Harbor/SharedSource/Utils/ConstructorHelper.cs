using System.Reflection;

namespace HomeAndHarbor
{
    /// <summary>
    /// Marks a class to have its static constructor run on initialization.
    /// Normally, static constructors are run when the class is first accessed
    /// but this attribute forces the static constructor to run (if it hasn't already)
    /// when the <see cref="ConstructorHelper.RunConstructors"/> method is called.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RunConstructorOnInitAttribute : Attribute;

    public static class ConstructorHelper
    {
        /// <summary>
        /// Runs the static constructors of all classes in the assembly that have the <see cref="RunConstructorOnInitAttribute"/>.
        /// </summary>
        public static void RunConstructors()
        {
            foreach (Type type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttribute<RunConstructorOnInitAttribute>() != null)
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
            }
        }
    }
}