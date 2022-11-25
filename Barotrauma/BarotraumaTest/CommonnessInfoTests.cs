using Barotrauma;
using FluentAssertions;
using FsCheck;
using Xunit;
using static Barotrauma.ItemPrefab;

namespace TestProject
{
    public class CommonnessInfoTests
    {
        private class CustomGenerators
        {

            public static Arbitrary<CommonnessInfo> CommonnessInfoGeneratorOverride()
            {
                return Arb.From(from float commonness in Arb.Generate<float>().Where(IsValid)
                                from float? abyssCommonness in Arb.Generate<float?>().Where(IsNullableValid)
                                from float? caveCommonness in Arb.Generate<float?>().Where(IsNullableValid)
                                select new CommonnessInfo(commonness, abyssCommonness, caveCommonness));

                static bool IsValid(float commonness) => !float.IsNaN(commonness) && commonness > float.MinValue && commonness < float.MaxValue;
                static bool IsNullableValid(float? commonness) => !commonness.HasValue || IsValid(commonness.Value);
            }
        }

        public CommonnessInfoTests()
        {
            Arb.Register<TestProject.CustomGenerators>();
            Arb.Register<CustomGenerators>();
        }

        [Fact]
        public void TestInheritedCommonness()
        {
            Prop.ForAll<CommonnessInfo, CommonnessInfo>((child, parent) =>
            {
                var info = child.WithInheritedCommonness(parent);

                info.Commonness.Should().Be(child.commonness);

                if (child.abyssCommonness.HasValue)
                {
                    info.abyssCommonness.Should().HaveValue();
                    info.abyssCommonness.Should().Be(child.abyssCommonness);
                }
                else if (parent.abyssCommonness.HasValue)
                {
                    info.abyssCommonness.Should().HaveValue();
                    info.abyssCommonness.Should().Be(parent.abyssCommonness);
                }
                else
                {
                    info.abyssCommonness.Should().NotHaveValue();
                }

                if (child.caveCommonness.HasValue)
                {
                    info.caveCommonness.Should().HaveValue();
                    info.caveCommonness.Should().Be(child.caveCommonness);
                }
                else if (parent.caveCommonness.HasValue)
                {
                    info.caveCommonness.Should().HaveValue();
                    info.caveCommonness.Should().Be(parent.caveCommonness);
                }
                else
                {
                    info.caveCommonness.Should().NotHaveValue();
                }
            }).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestPathCommonness()
        {
            Prop.ForAll<CommonnessInfo>(info =>
            {
                info.GetCommonness(Level.TunnelType.MainPath).Should().Be(info.Commonness);
                info.GetCommonness(Level.TunnelType.SidePath).Should().Be(info.Commonness);
                info.GetCommonness(Level.TunnelType.Cave).Should().Be(info.CaveCommonness);
            }).QuickCheckThrowOnFailure();
        }
    }
}
