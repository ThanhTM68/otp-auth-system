using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OTPAuth.API.Swagger;

public sealed class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor actionDescriptor)
        {
            return;
        }

        var requiresAuthorization = actionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any() ||
            actionDescriptor.MethodInfo.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any() ||
            actionDescriptor.ControllerTypeInfo.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any();

        if (!requiresAuthorization)
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
            }
        ];
    }
}
