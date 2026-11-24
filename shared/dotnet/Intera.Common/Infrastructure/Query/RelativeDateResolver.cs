using Intera.Common.Models.Query;

namespace Intera.Common.Infrastructure.Query;

/// <summary>
/// Half-open window <c>[Start, EndExclusive)</c> when a <see cref="RelativeDateValue"/> names a
/// calendar day (<c>from: 'today'</c> without sub-day units) or a whole period
/// (<c>snapEdge: 'whole'</c>), or a point instant (<see cref="EndExclusive"/> is <see langword="null"/>)
/// when anchored on <c>now</c> or sub-day units.
/// </summary>
public readonly record struct RelativeDateRange(DateTime Start, DateTime? EndExclusive);

/// <summary>
/// Resolves a <see cref="RelativeDateValue"/>, <see cref="NowExpression"/>, or <see cref="DateDiffExpression"/>
/// from the query DSL in the requested zone (the process's local zone when none is given).
/// </summary>
public static class RelativeDateResolver
{
    private enum SnapEdgeKind
    {
        Start,
        End,
        Whole
    }

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
        var (start, _, edge) = ComputeCore(value, nowUtc, timeZone, weekStart);
        return edge == SnapEdgeKind.End ? start.AddDays(1).AddMilliseconds(-1) : start;
    }

    public static RelativeDateRange ResolveRange(
        RelativeDateValue value,
        DateTimeOffset nowUtc,
        TimeZoneInfo? timeZone = null,
        DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var (start, endExclusive, _) = ComputeCore(value, nowUtc, timeZone, weekStart);
        return new RelativeDateRange(start, endExclusive);
    }

    private static (DateTime Start, DateTime? EndExclusive, SnapEdgeKind Edge) ComputeCore(
        RelativeDateValue value,
        DateTimeOffset nowUtc,
        TimeZoneInfo? timeZone,
        DayOfWeek weekStart)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!string.Equals(value.Type, "relativeDate", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported filter value type '{value.Type}'.");
        }

        var isFromToday = string.Equals(value.From, "today", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(value.From, "now", StringComparison.OrdinalIgnoreCase) && !isFromToday)
        {
            throw new ArgumentException("relativeDate supports only from = 'now' or from = 'today'.");
        }

        var now = ToZoneWallClock(nowUtc, timeZone);
        var baseDate = isFromToday ? now.Date : now;

        var canonicalUnit = NormalizeUnit(value.Unit);
        var shifted = AddUnit(baseDate, canonicalUnit, value.Amount);

        string? canonicalSnapUnit = null;
        var edge = SnapEdgeKind.Start;
        var snapped = shifted;

        if (!string.IsNullOrWhiteSpace(value.SnapTo))
        {
            canonicalSnapUnit = NormalizeUnit(value.SnapTo, "snapTo");
            edge = ParseSnapEdge(value.SnapEdge);
            snapped = SnapToStart(shifted, canonicalSnapUnit, weekStart);
            if (edge == SnapEdgeKind.End)
            {
                snapped = AddUnit(snapped, canonicalSnapUnit, 1);
            }
        }
        else if (!string.IsNullOrWhiteSpace(value.SnapEdge))
        {
            throw new ArgumentException("relativeDate snapEdge requires snapTo.");
        }

        string? canonicalOffsetUnit = null;
        if (value.Offset is { } offset)
        {
            canonicalOffsetUnit = NormalizeUnit(offset.Unit, "offset.unit");
            if (offset.Amount != 0)
            {
                snapped = AddUnit(snapped, canonicalOffsetUnit, offset.Amount);
            }
        }

        if (edge == SnapEdgeKind.Whole)
        {
            // Period start was shifted by any post-snap offset; period end is one `snapUnit` after
            // the un-offset period start plus the same offset (preserving month-end clamping when
            // offsetting by months).
            var unoffsetStart = SnapToStart(shifted, canonicalSnapUnit!, weekStart);
            var endBound = AddUnit(unoffsetStart, canonicalSnapUnit!, 1);
            if (value.Offset is { Amount: not 0 } wholeOffset)
            {
                endBound = AddUnit(endBound, canonicalOffsetUnit!, wholeOffset.Amount);
            }

            return (snapped, endBound, edge);
        }

        var hasSubDay = IsSubDayUnit(canonicalUnit)
            || (canonicalSnapUnit is not null && IsSubDayUnit(canonicalSnapUnit))
            || (canonicalOffsetUnit is not null && value.Offset!.Amount != 0 && IsSubDayUnit(canonicalOffsetUnit));

        if (isFromToday && !hasSubDay)
        {
            if (edge == SnapEdgeKind.End)
            {
                // `snapped` is already midnight of the day after the target calendar day.
                var dayStart = snapped.AddDays(-1);
                return (dayStart, snapped, edge);
            }

            return (snapped, snapped.AddDays(1), edge);
        }

        if (edge == SnapEdgeKind.End)
        {
            var endInstant = snapped.AddMilliseconds(-1);
            return (endInstant, null, SnapEdgeKind.Start);
        }

        return (snapped, null, edge);
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

    private static bool IsSubDayUnit(string canonicalUnit) =>
        canonicalUnit is "hour" or "minute" or "second";

    private static SnapEdgeKind ParseSnapEdge(string? snapEdge)
    {
        if (string.IsNullOrWhiteSpace(snapEdge)
            || string.Equals(snapEdge.Trim(), "start", StringComparison.OrdinalIgnoreCase))
        {
            return SnapEdgeKind.Start;
        }

        if (string.Equals(snapEdge.Trim(), "end", StringComparison.OrdinalIgnoreCase))
        {
            return SnapEdgeKind.End;
        }

        if (string.Equals(snapEdge.Trim(), "whole", StringComparison.OrdinalIgnoreCase))
        {
            return SnapEdgeKind.Whole;
        }

        throw new ArgumentException($"Unsupported relativeDate snapEdge '{snapEdge}'. Expected 'start', 'end', or 'whole'.");
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
