#nullable enable

namespace Barotrauma.Plugins;

public interface IOrderObjectiveFactory
{
    /// <summary>
    /// Creates a new AIObjective based on the passed order
    /// </summary>
    /// <remarks>
    /// If the return value is null, the result is ignored
    /// </remarks>
    public AIObjective? CreateObjectiveFromOrder(AIObjectiveManager objectiveManager, Order order, Character character, float priorityModifier);
}

/// <summary>
/// Allows plugins to configure what AIObjective is created for an order identifier.
/// </summary>
/// <example>
/// Create a factory by implementing <see cref="IOrderObjectiveFactory"/>. Return an objective for the orders you want to handle, and null for everything else:
/// <code>
/// public class FooObjectiveFactory : IOrderObjectiveFactory
/// {
///     public AIObjective? CreateObjectiveFromOrder(AIObjectiveManager objectiveManager, Order order, Character character, float priorityModifier)
///     {
///         if (order.Identifier == "foo")
///         {
///             return new AIObjectiveFoo(character, objectiveManager, priorityModifier);
///         }
///         return null;
///     }
/// }
/// </code>
/// The objective itself is a regular <see cref="AIObjective"/>:
/// <code>
/// public class AIObjectiveFoo : AIObjective
/// {
///     public override Identifier Identifier { get; set; } = "foo".ToIdentifier();
///
///     public AIObjectiveFoo(Character character, AIObjectiveManager objectiveManager, float priorityModifier)
///         : base(character, objectiveManager, priorityModifier) { }
///
///     protected override void Act(float deltaTime)
///     {
///         // Make the bot do something
///     }
///
///     protected override bool CheckObjectiveState() => false;
/// }
/// </code>
/// Then register the factory, e.g. when the plugin is initialized:
/// <code>
/// AIObjectiveRegistrar.RegisterObjectiveFactory(new FooObjectiveFactory());
/// </code>
/// Now any order with the identifier "foo" will make the bot pursue <c>AIObjectiveFoo</c>.
/// </example>
/// <remarks>
/// Factories are queried before the built-in objectives, so returning an objective for a vanilla order identifier overrides the vanilla behavior.
/// </remarks>
public interface IAIObjectiveRegistrar : IPluginService
{
    public void RegisterObjectiveFactory(IOrderObjectiveFactory factory);
}