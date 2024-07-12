
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma;
using Barotrauma.Items.Components;
using FluentAssertions;
using FsCheck;
using Microsoft.Xna.Framework;
using Xunit;

namespace TestProject;

public sealed class FabricatorQualityRollTests
{
    [Fact]
    public void TestPercentageChance()
    {
        Prop.ForAll(
            Arb.Generate<int>().Where(static i => i is <= 3 and >= 0).ToArbitrary(),
            Arb.Generate<float>().Where(static i => i is <= 100 and >= 50).ToArbitrary(),
            Arb.Generate<float>().Where(static i => i is <= 100 and >= 0).ToArbitrary(), (startingQuality, skillLevel, targetLevel) =>
            {
                float plusOneProbability = 0f,
                      plusTwoProbability = 0f;

                if (skillLevel >= Fabricator.PlusOneQualityBonusThreshold)
                {
                    var bonusChance1 = MathHelper.Lerp(targetLevel, Fabricator.PlusOneTarget, Fabricator.PlusOneLerp);
                    plusOneProbability = Fabricator.CalculateBonusRollPercentage(skillLevel, bonusChance1);

                    if (skillLevel >= Fabricator.PlusTwoQualityBonusThreshold)
                    {
                        var bonusChance2 = MathHelper.Lerp(targetLevel, Fabricator.PlusTwoTarget, Fabricator.PlusTwoLerp);
                        plusTwoProbability = Fabricator.CalculateBonusRollPercentage(skillLevel, bonusChance2);
                    }
                }

                var result = new Fabricator.QualityResult(startingQuality, HasRandomQuality: true, plusOneProbability, plusTwoProbability);

                // iterate to confirm that the percentage chance is correct
                const int iterations = 100000;
                var plusOneCount = 0;
                var plusTwoCount = 0;
                for (int i = 0; i < iterations; i++)
                {
                    int quality = result.RollQuality();
                    if (quality == startingQuality + 1)
                    {
                        plusOneCount++;
                    }
                    else if (quality == startingQuality + 2)
                    {
                        plusTwoCount++;
                    }
                }

                var iteratedPlusOneChance = plusOneCount / (float)iterations * 100f;
                var iteratedPlusTwoChance = plusTwoCount / (float)iterations * 100f;

                // check that the percentage chance is within 3% of the expected value
                result.TotalPlusOnePercentage.Should().BeApproximately(iteratedPlusOneChance, 3f);
                result.TotalPlusTwoPercentage.Should().BeApproximately(iteratedPlusTwoChance, 3f);
            }).QuickCheckThrowOnFailure();
    }
}