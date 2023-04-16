using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml.Linq;
using Barotrauma;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace TestProject
{
    public class TestExample
    {
        // By default FsCheck has generators for basic types like floats ints and strings
        // Anything custom like Rectangle or Vector2 requires writing a custom generator for it
        private class CustomExampleGenerators
        {
            // We override the float generator to exclude NaNs and infinites
            public static Arbitrary<float> FloatGeneratorOverride() => Arb.Default.Float32().Generator.Where(MathUtils.IsValid).ToArbitrary();

            // We override the String generator to exclude null and empty strings
            public static Arbitrary<string> StringGeneratorOverride() => Arb.Default.String().Generator.Where(s => !string.IsNullOrWhiteSpace(s)).ToArbitrary();

            // Generator for the Rectangle type
            public static Arbitrary<Rectangle> RectangleGenerator()
            {
                return Arb.From(from int x in Arb.Generate<int>()
                                from int y in Arb.Generate<int>()
                                from int w in Arb.Generate<int>().Where(i => i > 0)
                                from int h in Arb.Generate<int>().Where(i => i > 0)
                                select new Rectangle(x, y, w, h));
            }
        }

        // Used to output text into the test output
        private readonly ITestOutputHelper testOutputHelper;

        public TestExample(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
            Arb.Register<CustomExampleGenerators>(); // Register our custom generators
        }

        [Fact] // Create a public function and add the [Fact] attribute on it to make a test function
        public void TestXORAlgorithm()
        {
            Prop.ForAll<string, string>((text, key) => // generates a pair of random strings
            {
                string encrypted = XOREncryptDecrypt(text, key);
                string decrypted = XOREncryptDecrypt(encrypted, key);

                decrypted.Should().BeEquivalentTo(text); // FluentAssertions provides clear and verbose assertions with the Should() method
            }).VerboseCheckThrowOnFailure(testOutputHelper);
            // VerboseCheck performs 100 tests and outputs the generated values into the test output
            // ThrowOnFailure will additionally throw an exception if any of the functions fail which will make the test fail

            // We will see that this fails the test with the following exception:

            /*
             * System.Exception
             * Falsifiable, after 1 test (0 shrinks) (StdGen (2118948508,297004609)):
             * Original:
             * ("Jl", "m")
             * with exception:
             * Xunit.Sdk.XunitException: Expected decrypted to be equivalent to "Jl" with a length of 2, but "948492" has a length of 6, differs near "948" (index 0).
             */

            // We can see that the reason it is failing is because the original text was "Jl" but when encrypted and decrypted the string becomes "948492"
            // This is of course because we are not casting the XOR'd value to a char and instead appending the integer to the string builder
        }

        // Erroneous XOR encryption algorithm
        private static string XOREncryptDecrypt(string text, string key)
        {
            var result = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                result.Append(text[i] ^ (uint)key[i % key.Length]);
            }

            return result.ToString();
        }

        private class ExampleEntity : ISerializableEntity
        {
            [Serialize(0.0f, IsPropertySaveable.Yes)]
            public float ExampleValue { get; set; }

            public string Name => nameof(ExampleEntity);
            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

            public ExampleEntity()
            {
                SerializableProperties = SerializableProperty.GetProperties(this);
            }
        }

        [Fact]
        public void TestPropertyConditionalEqualsOperator()
        {
            // Test if the PropertyConditional equals operator is working correctly
            Prop.ForAll<float>(value =>
            {
                XAttribute xmlAttribute = new XAttribute("examplevalue", $"equals {value}");

                PropertyConditional conditional = new PropertyConditional(xmlAttribute);

                ExampleEntity entity = new ExampleEntity
                {
                    ExampleValue = value
                };

                conditional.Matches(entity).Should().BeTrue();
            }).VerboseCheckThrowOnFailure(testOutputHelper); // Remember to pass testOutputHelper so we actually get output
        }
    }
}