using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma;
using FluentAssertions;
using FsCheck;
using Xunit;

namespace TestProject;

public sealed class SplitEscapedTest
{
    private const char Joiner = ',';
    private readonly record struct PathologicalString(string Value);
    
    private class CustomGenerators
    {
        public static Arbitrary<PathologicalString> SplittableStringGenerator()
        {
            var rng = new System.Random();
            return Arb.Generate<string>()
                .Where(s => s != null)
                .Select(s => new PathologicalString(
                    // Generate a string that only contains backslashes and commas
                    string.Join("", s.Select(_ => rng.Next() % 100 < 50 ? '\\' : Joiner))))
                .ToArbitrary();
        }
    }

    public SplitEscapedTest()
    {
        Arb.Register<CustomGenerators>();
    }

    [Fact]
    public void EqualityTest()
    {
        Prop.ForAll<PathologicalString>(EqualityCheck).QuickCheckThrowOnFailure();
        Prop.ForAll<string>(EqualityCheck).QuickCheckThrowOnFailure();
    }

    private static void EqualityCheck(PathologicalString pathologicalString)
    {
        EqualityCheck(pathologicalString.Value);
    }
    
    private static void EqualityCheck(string? str)
    {
        if (str is null) { return; }
        IReadOnlyList<string> splitted;
        try
        {
            splitted = str.SplitEscaped(Joiner);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Didn't fail the test, the input just had bad escapes
            return;
        }
        string recombined = splitted.JoinEscaped(Joiner);
        recombined.Should().BeEquivalentTo(str);
    }
}