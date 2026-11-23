using Intera.Common.Models.Query;

namespace Intera.Common.Infrastructure.Query;

/// <summary>
/// Resolves a <see cref="RelativeDateValue"/>, <see cref="NowExpression"/>, or <see cref="DateDiffExpression"/>
/// from the query DSL in the requested zone (the process's local zone when none is given).
/// </summary>
public static class RelativeDateResolver
{
    public static DateTime Resolve(RelativeDateValue value, TimeZoneInfo? timeZone = null, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        return Resolve(value, DateTimeOffset.UtcNow, timeZone, weekStart);
    }

    public static DateTime Resolve(
        RelativeDateValue value,
        DateTimeOffset nowUtc,
        TimeZoneInfo? timeZone = null,
        DayOfWeek weekStart = DayOfWeek.Monday)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!string.Equals(value.Type, "relativeDate", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported filter value type '{value.Type}'.");
        }

        if (!string.Equals(value.From, "now", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value.From, "today", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("relativeDate supports only from = 'now' or from = 'today'.");
        }

        var now = ToZoneWallClock(nowUtc, timeZone);

        var baseDate = string.Equals(value.From, "today", StringComparison.OrdinalIgnoreCase)
            ? now.Date
            : now;

        var shifted = AddUnit(baseDate, NormalizeUnit(value.Unit), value.Amount);

        var snapped = shifted;
        var toEnd = false;
        if (!string.IsNullOrWhiteSpace(value.SnapTo))
        {
            var snapUnit = NormalizeUnit(value.SnapTo, "snapTo");
            toEnd = ParseSnapEdgeEnd(value.SnapEdge);
            snapped = SnapToStart(shifted, snapUnit, weekStart);
            if (toEnd)
            {
                snapped = AddUnit(snapped, snapUnit, 1);
            }
        }
        else if (!string.IsNullOrWhiteSpace(value.SnapEdge))
        {
            throw new ArgumentException("relativeDate snapEdge requires snapTo.");
        }

        if (value.Offset is { } offset)
        {
            var offsetUnit = NormalizeUnit(offset.Unit, "offset.unit");
            if (offset.Amount != 0)
            {
                snapped = AddUnit(snapped, offsetUnit, offset.Amount);
            }
        }

        return toEnd ? snapped.AddMilliseconds(-1) : snapped;
    }

    public static DateTime ResolveNow(string? anchor, DateTimeOffset nowUtc, TimeZoneInfo? timeZone = null)
    {
        var effectiveAnchor = string.IsNullOrWhiteSpace(anchor) ? "now" : anchor.Trim();
        if (!string.Equals(effectiveAnchor, "now", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(effectiveAnchor, "today", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported now anchor '{anchor}'. Expected 'now' or 'today'.");
        }

        var now = ToZoneWallClock(nowUtc, timeZone);
        return string.Equals(effectiveAnchor, "today", StringComparison.OrdinalIgnoreCase)
            ? now.Date
            : now;
    }

    /// <summary>
    /// Computes SQL-compatible calendar boundary <c>DATEDIFF(unit, start, end)</c> (<c>end - start</c>),
    /// aligning <c>week</c> to <paramref name="weekStart"/>.
    /// </summary>
    public static long ComputeDateDiff(string unit, DateTime start, DateTime end, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        return NormalizeUnit(unit, "dateDiff unit") switch
        {
            "year" => end.Year - start.Year,
            "quarter" => ((end.Year * 4L) + ((end.Month - 1) / 3)) - ((start.Year * 4L) + ((start.Month - 1) / 3)),
            "month" => ((end.Year * 12L) + end.Month) - ((start.Year * 12L) + start.Month),
            "week" => (long)Math.Round((StartOfWeek(end, weekStart) - StartOfWeek(start, weekStart)).TotalDays / 7.0),
            "day" => (long)Math.Round((end.Date - start.Date).TotalDays),
            "hour" => (long)(TruncateToHour(end) - TruncateToHour(start)).TotalHours,
            "minute" => (long)(TruncateToMinute(end) - TruncateToMinute(start)).TotalMinutes,
            "second" => (long)(TruncateToSecond(end) - TruncateToSecond(start)).TotalSeconds,
            _ => throw new ArgumentException($"Unsupported dateDiff unit '{unit}'.")
        };
    }

    /// <summary>
    /// Resolves a Windows or IANA zone id. An unknown id is an error rather than a silent fallback
    /// to local time.
    /// </summary>
    public static TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException($"Unknown time zone '{id}'.", exception);
        }
    }

    /// <summary>
    /// Parses a first-day-of-week name; an unknown value is an error rather than a silent Monday.
    /// </summary>
    public static DayOfWeek ParseWeekStart(string? weekStart)
    {
        if (string.IsNullOrWhiteSpace(weekStart))
        {
            return DayOfWeek.Monday;
        }

        return Enum.TryParse<DayOfWeek>(weekStart.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown first day of week '{weekStart}'.");
    }

    public static string NormalizeUnit(string unit, string propertyName = "unit")
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException($"Unsupported relativeDate {propertyName} '{unit}'.");
        }

        return unit.Trim().ToLowerInvariant() switch
        {
            "year" or "yy" or "yyyy" => "year",
            "quarter" or "qq" or "q" => "quarter",
            "month" or "mm" or "m" => "month",
            "week" or "wk" or "ww" => "week",
            "day" or "dd" or "d" => "day",
            "hour" or "hh" => "hour",
            "minute" or "mi" or "n" => "minute",
            "second" or "ss" or "s" => "second",
            _ => throw new ArgumentException($"Unsupported relativeDate {propertyName} '{unit}'.")
        };
    }

    private static DateTime ToZoneWallClock(DateTimeOffset instantUtc, TimeZoneInfo? timeZone)
    {
        return timeZone is null
            ? instantUtc.ToLocalTime().DateTime
            : TimeZoneInfo.ConvertTime(instantUtc, timeZone).DateTime;
    }

    private static DateTime AddUnit(DateTime value, string canonicalUnit, int amount)
    {
        return canonicalUnit switch
        {
            "year" => value.AddYears(amount),
            "quarter" => value.AddMonths(amount * 3),
            "month" => value.AddMonths(amount),
            "week" => value.AddDays(amount * 7),
            "day" => value.AddDays(amount),
            "hour" => value.AddHours(amount),
            "minute" => value.AddMinutes(amount),
            "second" => value.AddSeconds(amount),
            _ => throw new ArgumentException($"Unsupported relativeDate unit '{canonicalUnit}'.")
        };
    }

    private static DateTime SnapToStart(DateTime shifted, string canonicalUnit, DayOfWeek weekStart)
    {
        return canonicalUnit switch
        {
            "year" => new DateTime(shifted.Year, 1, 1),
            "quarter" => new DateTime(shifted.Year, ((shifted.Month - 1) / 3 * 3) + 1, 1),
            "month" => new DateTime(shifted.Year, shifted.Month, 1),
            "week" => StartOfWeek(shifted, weekStart),
            "day" => shifted.Date,
            "hour" => TruncateToHour(shifted),
            "minute" => TruncateToMinute(shifted),
            "second" => TruncateToSecond(shifted),
            _ => throw new ArgumentException($"Unsupported relativeDate snapTo '{canonicalUnit}'.")
        };
    }

    private static bool ParseSnapEdgeEnd(string? snapEdge)
    {
        if (string.IsNullOrWhiteSpace(snapEdge)
            || string.Equals(snapEdge.Trim(), "start", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(snapEdge.Trim(), "end", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new ArgumentException($"Unsupported relativeDate snapEdge '{snapEdge}'. Expected 'start' or 'end'.");
    }

    private static DateTime StartOfWeek(DateTime value, DayOfWeek weekStart)
    {
        var diff = (7 + (value.DayOfWeek - weekStart)) % 7;
        return value.Date.AddDays(-diff);
    }

    private static DateTime TruncateToHour(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0);

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);

    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
}
