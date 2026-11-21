using Intera.Common.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Intera.Xero.Api.Controllers;

public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<object>> Get()
    {
        return OkResponse<object>(new
        {
            status = "ok",
            api = "Intera.Xero.Api",
            utcNow = DateTimeOffset.UtcNow
        });
    }
}
