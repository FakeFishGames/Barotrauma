using System;

namespace Barotrauma
{
    /// <summary>
    /// This attribute is used to indicate that a class was previously known by a different name.
    /// This is used for backwards compatibility when we have types that are loaded from XML using reflection.
    /// 
    /// Only works in cases where we use <see cref="ReflectionUtils.GetTypeWithBackwardsCompatibility"/> to load the type.
    /// 
    /// If you wish to use this, you will need to replace the call to Type.GetType() in the load method with
    /// ReflectionUtils.GetTypeWithBackwardsCompatibility().
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TypePreviouslyKnownAs : Attribute
    {
        public string PreviousName { get; }

        public TypePreviouslyKnownAs(string previousName)
        {
            PreviousName = previousName;
        }
    }
}