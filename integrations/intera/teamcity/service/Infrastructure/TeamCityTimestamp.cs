using System.Globalization;

namespace Intera.TeamCity.Api.Infrastructure;

/// <summary>
/// Reads TeamCity's compact timestamp, <c>20260915T071146+0000</c>.
/// </summary>
/// <remarks>
/// Neither <c>zzz</c> nor <c>K</c> in a .NET format string accepts an offset without a colon, so the
/// offset is read by hand. The instant keeps its offset (a <see cref="DateTimeOffset"/>): TeamCity prints
/// the server's own zone, and dropping it would move every build by that much.
/// </remarks>
public static class TeamCityTimestamp
{
    private const string LocalPartFormat = "yyyyMMdd'T'HHmmss";

    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        // yyyyMMddTHHmmss is 15 characters; the offset, when present, is +HHmm.
        if (text.Length < 15
            || !DateTime.TryParseExact(text[..15], LocalPartFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        var rest = text[15..];

        if (rest.Length > 0)
        {
            if (rest is "Z" or "z")
            {
                offset = TimeSpan.Zero;
            }
            else if (rest.Length == 5
                && rest[0] is '+' or '-'
                && int.TryParse(rest.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
                && int.TryParse(rest.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                offset = new TimeSpan(hours, minutes, 0);
                if (rest[0] == '-')
                {
                    offset = offset.Negate();
                }
            }
            else
            {
                return null;
            }
        }

        return new DateTimeOffset(local, offset);
    }
}
