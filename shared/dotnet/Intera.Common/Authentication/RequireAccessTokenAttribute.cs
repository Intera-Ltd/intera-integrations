using Microsoft.AspNetCore.Authorization;

namespace Intera.Common.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireAccessTokenAttribute : AuthorizeAttribute
{
    public RequireAccessTokenAttribute()
    {
        AuthenticationSchemes = AccessTokenAuthenticationDefaults.Scheme;
    }
}
