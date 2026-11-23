namespace Intera.Common.Infrastructure.Query;

public sealed record InMemoryQueryField(string Name, Func<IReadOnlyDictionary<string, object?>, object?> Accessor);

public static class InMemoryQueryFieldsBuilder
{
    public static IReadOnlyDictionary<string, InMemoryQueryField> Build(params ReadOnlySpan<string> fieldNames)
    {
        var result = new Dictionary<string, InMemoryQueryField>(fieldNames.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var name in fieldNames)
        {
            var capturedName = name;
            result[name] = new InMemoryQueryField(name, row => row.TryGetValue(capturedName, out var value) ? value : null);
        }

        return result;
    }
}
