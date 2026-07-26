namespace PointOfSale.API.Attributes;

/// <summary>
/// Marks a controller as Testing-environment-only.
/// The <see cref="TestingOnlyControllerFeatureProvider"/> removes all controllers
/// decorated with this attribute when ASPNETCORE_ENVIRONMENT is NOT "Testing".
///
/// SECURITY: Any controller decorated with this attribute MUST NOT be registered
/// in Production. The feature provider enforces this at startup.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TestingOnlyControllerAttribute : Attribute
{
}
