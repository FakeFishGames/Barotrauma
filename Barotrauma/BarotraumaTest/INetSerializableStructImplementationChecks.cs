using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Barotrauma;
using Microsoft.Xna.Framework;
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
            var concreteType = type;
            if (type.IsGenericType)
            {
                // Plug in some known good parameters to evaluate
                // a concrete instance of this generic type
                
                var paramsConstraints = type.GetGenericArguments()
                    .Select(p => p.GetGenericParameterConstraints())
                    .ToImmutableArray();

                var chosenArgs = new Type[paramsConstraints.Length];

                for (int i = 0; i < paramsConstraints.Length; i++)
                {
                    var constraints = paramsConstraints[i];
                    bool refTypeConstraint = constraints.Any(c
                        => c.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint));
                    bool valueTypeConstraint = constraints.Any(c
                        => c.GenericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint));
                    if (refTypeConstraint && valueTypeConstraint)
                    {
                        throw new Exception($"Type \"{type.Name}\" has invalid generic constraints");
                    }

                    int rngMin = refTypeConstraint ? 3 : 0;
                    int rngMax = valueTypeConstraint ? 3 : 6;

                    chosenArgs[i] = Rand.Range(rngMin, rngMax) switch
                    {
                        0 => typeof(Vector2),
                        1 => typeof(Point),
                        2 => typeof(int),
                        
                        3 => typeof(string),
                        4 => typeof(float[]),
                        5 => typeof(int[]),
                        
                        var invalid => throw new Exception($"Broken RNG ranges in test, got {invalid}")
                    };
                }

                concreteType = type.MakeGenericType(chosenArgs);
            }
            
            var members = NetSerializableProperties.GetPropertiesAndFields(concreteType);
            foreach (var member in members)
            {
                void checkType(Type typeBeingChecked)
                {
                    Assert.True(tryFindBehavior(typeBeingChecked, out _), $"{concreteType}.{member.Name} of type {member.Type} is unsupported in {nameof(INetSerializableStruct)}");
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
