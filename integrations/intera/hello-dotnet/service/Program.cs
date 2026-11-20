// Hello .NET: a tiny service that an INTERA integration bundle calls over HTTP.
//
//   GET /health     -> 200 {"status":"ok"}   (polled by the installation agent)
//   GET /api/items  -> a few sample records  (read by the bundle's data source)
//
// The port comes from ASPNETCORE_HTTP_PORTS (8080 in the image). The service
// keeps no state and needs no secrets.

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();

var items = new[]
{
    new Item("item-001", "Starter plan", 49.90m, new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero)),
    new Item("item-002", "Team plan", 199.00m, new DateTimeOffset(2026, 2, 3, 14, 5, 0, TimeSpan.Zero)),
    new Item("item-003", "Enterprise plan", 1250.50m, new DateTimeOffset(2026, 3, 21, 8, 0, 0, TimeSpan.Zero)),
    new Item("item-004", "Support add-on", 15.00m, new DateTimeOffset(2026, 4, 10, 17, 45, 0, TimeSpan.Zero)),
};

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/items", () => Results.Ok(items));

app.Run();

/// <summary>One sample record. Serialized as camelCase JSON.</summary>
internal sealed record Item(string Id, string Name, decimal Amount, DateTimeOffset UpdatedAt);
