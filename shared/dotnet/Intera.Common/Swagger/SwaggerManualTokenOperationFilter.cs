using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intera.Common.Swagger;

public sealed class SwaggerManualTokenOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAttribute =
            context.MethodInfo.GetCustomAttributes(typeof(SwaggerManualTokenAttribute), inherit: true).Length != 0 ||
            context.MethodInfo.DeclaringType?.GetCustomAttributes(typeof(SwaggerManualTokenAttribute), inherit: true).Length != 0;

        if (!hasAttribute)
        {
            return;
        }

        operation.Parameters ??= [];

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Access-Token",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Access token for this endpoint. You can paste with or without 'Bearer '.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String
            }
        });
    }
}