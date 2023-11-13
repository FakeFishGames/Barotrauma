using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Barotrauma;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Xunit;

namespace TestProject;

public sealed class INetSerializableStructImplementationChecks
{
    private delegate bool TryFindBehaviorDelegate(Type type, out NetSerializableProperties.IReadWriteBehavior behavior);

    private Type FillGenericParameters(Type type)
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
            var baseTypeConstraints = constraints.Where(c => !c.IsGenericParameter);

            bool hasGenericConstraint(GenericParameterAttributes flag)
                => constraints.Any(c
                    => c.IsGenericParameter && c.GenericParameterAttributes.HasFlag(flag));

            bool refTypeConstraint = hasGenericConstraint(GenericParameterAttributes.ReferenceTypeConstraint);
            bool valueTypeConstraint = baseTypeConstraints.Contains(typeof(ValueType));

            if (refTypeConstraint && valueTypeConstraint)
            {
                throw new Exception($"Type \"{type.Name}\" has invalid generic constraints");
            }

            var viableArguments = new List<Type>();
            if (!refTypeConstraint)
            {
                // Value types are viable
                viableArguments.AddRange(new[]
                {
                    typeof(Vector2),
                    typeof(float),
                    typeof(int)
                });
            }
            if (!valueTypeConstraint)
            {
                // Reference types are viable
                viableArguments.AddRange(new[]
                {
                    typeof(string),
                    typeof(float[]),
                    typeof(int[])
                });
            }
            
            chosenArgs[i] = viableArguments.GetRandomUnsynced();
        }
        return type.MakeGenericType(chosenArgs);
    }
    
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
            var concreteType = type.IsGenericType
                ? FillGenericParameters(type)
                : type;

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
