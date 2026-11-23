using Intera.Common.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Intera.ZohoInventory.Api.Controllers;

public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<object>> Get()
    {
        return OkResponse<object>(new
        {
            status = "ok",
            api = "Intera.ZohoInventory.Api",
            utcNow = DateTimeOffset.UtcNow
        });
    }
}
