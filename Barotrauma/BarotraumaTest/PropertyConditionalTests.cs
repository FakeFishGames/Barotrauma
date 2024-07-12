using Barotrauma;
using FluentAssertions;
using FsCheck;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace TestProject;

public sealed class PropertyConditionalTests
{
    private readonly record struct OperatorStr(string Str);
    private readonly record struct ConditionStr(string Str);

    private class CustomGenerators
    {
        public static Arbitrary<OperatorStr> OperatorStrGeneratorOverride()
        {
            return Gen.Choose(0, operators.Length-1)
                .Select(i => operators[i])
                .ToArbitrary();
        }

        public static Arbitrary<ConditionStr> ConditionStrGeneratorOverride()
        {
            return Arb.Generate<string>()
                .Where(s => s != null && !s.Any(char.IsWhiteSpace) && !s.Contains(','))
                .Select(s => new ConditionStr(s)).ToArbitrary();
        }
    }

    public PropertyConditionalTests()
    {
        Arb.Register<TestProject.CustomGenerators>();
        Arb.Register<CustomGenerators>();
    }
    
    static ImmutableArray<OperatorStr> operators
        = new[]
        {
            "eq", "neq", "gt",
            "gte", "lt", "lte"
        }.Select(s => new OperatorStr(s)).ToImmutableArray();

    [Fact]
    public void TestExtractComparisonOperatorFromConditionString()
    {
        Prop.ForAll(
            Arb.Generate<OperatorStr>().ToArbitrary(),
            Arb.Generate<ConditionStr>().ToArbitrary(),
            ExtractComparisonOperatorFromConditionStringCase)
            .QuickCheckThrowOnFailure();
    }

    private static void ExtractComparisonOperatorFromConditionStringCase(OperatorStr operatorStr, ConditionStr conditionStr)
    {
        var op = PropertyConditional.GetComparisonOperatorType(operatorStr.Str);

        var (op2, condStr) = PropertyConditional.ExtractComparisonOperatorFromConditionString(operatorStr.Str + " " + conditionStr.Str);
        op2.Should().Be(op);
        condStr.Should().Be(conditionStr.Str);

        var (op3, condStr2) = PropertyConditional.ExtractComparisonOperatorFromConditionString(conditionStr.Str);
        op3.Should().Be(PropertyConditional.ComparisonOperatorType.Equals);
        condStr2.Should().Be(conditionStr.Str);
    }
}
