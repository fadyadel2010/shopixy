using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PointOfSale.API.Infrastructure.Swagger;

/// <summary>
/// Swagger operation filter that documents the Idempotency-Key header requirement
/// for checkout endpoints. The header must be a valid GUID (format: uuid).
/// </summary>
public class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Only apply to CheckoutController endpoints
        var controllerName = context.MethodInfo.DeclaringType?.Name;
        if (controllerName != "CheckoutController")
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();

        // Add Idempotency-Key header parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Unique identifier for idempotency protection. Must be a valid GUID (UUID format). " +
                          "The same key will return the cached response if the checkout was already processed. " +
                          "Missing or invalid GUID will result in IDEMPOTENCY_KEY_MISSING or IDEMPOTENCY_KEY_INVALID error.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Format = "uuid",
                Example = new Microsoft.OpenApi.Any.OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6")
            }
        });

        // Add X-Request-Id header parameter (optional compatibility header)
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Request-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional compatibility header. If provided, must be a valid GUID matching the Idempotency-Key value. " +
                          "Invalid GUID will result in REQUEST_ID_INVALID error.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Format = "uuid"
            }
        });

        // Add X-Tenant-Id header parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Tenant-Id",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Tenant identifier. Must be a valid GUID.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Format = "uuid"
            }
        });

        // Add X-Session-Id header parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Session-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Session identifier for guest checkout. Required when X-Customer-Id is not provided.",
            Schema = new OpenApiSchema
            {
                Type = "string"
            }
        });

        // Add X-Customer-Id header parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Customer-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Customer identifier for authenticated checkout. Must be a valid GUID if provided.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Format = "uuid"
            }
        });
    }
}
