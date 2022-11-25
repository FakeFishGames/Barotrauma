using System;
using System.Linq;
using System.Reflection;
using Barotrauma;
using Xunit;

namespace TestProject;

public class INetSerializableStructImplementationChecks
{
    private delegate bool TryFindBehaviorDelegate(Type type, out NetSerializableProperties.IReadWriteBehavior behavior);
    
    [Fact]
    public void CheckStructMemberTypes()
    {
        var interfaceType = typeof(INetSerializableStruct);
        var types = interfaceType.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.IsAssignableTo(interfaceType));
        
        //private static bool TryFindBehavior(Type type, out IReadWriteBehavior behavior)
        TryFindBehaviorDelegate tryFindBehavior
            = typeof(NetSerializableProperties)
                .GetMethod("TryFindBehavior", BindingFlags.NonPublic | BindingFlags.Static,
                    typeof(TryFindBehaviorDelegate).GetMethod("Invoke")!
                        .GetParameters().Select(p => p.ParameterType).ToArray())!
                .CreateDelegate<TryFindBehaviorDelegate>();

        foreach (var type in types)
        {
            var members = NetSerializableProperties.GetPropertiesAndFields(type);
            foreach (var member in members)
            {
                void checkType(Type typeBeingChecked)
                {
                    Assert.True(tryFindBehavior(typeBeingChecked, out _), $"{type}.{member.Name} of type {member.Type} is unsupported in {nameof(INetSerializableStruct)}");
                    Type? nestedType = null;
                    if (typeBeingChecked.IsGenericType)
                    {
                        nestedType = typeBeingChecked.GetGenericArguments()[0];
                    }
                    else if (typeBeingChecked.IsArray)
                    {
                        nestedType = typeBeingChecked.GetElementType();
                    }

                    if (nestedType != null)
                    {
                        checkType(nestedType);
                    }
                }
                checkType(member.Type);
            }
        }
    }
}
