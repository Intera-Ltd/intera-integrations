using Intera.Common.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Intera.Jira.Api.Controllers;

public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<object>> Get()
    {
        return OkResponse<object>(new
        {
            status = "ok",
            api = "Intera.Jira.Api",
            utcNow = DateTimeOffset.UtcNow
        });
    }
}
