namespace PointOfSale.API.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowAnonymousTenantAttribute : Attribute
{
}
