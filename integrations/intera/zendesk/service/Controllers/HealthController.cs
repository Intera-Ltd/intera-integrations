using Intera.Common.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Intera.Zendesk.Api.Controllers;

public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<object>> Get()
    {
        return OkResponse<object>(new
        {
            status = "ok",
            api = "Intera.Zendesk.Api",
            utcNow = DateTimeOffset.UtcNow
        });
    }
}
