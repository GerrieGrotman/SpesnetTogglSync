using System.Globalization;
using System.Text;
using SpesnetTogglSync.TogglApi;

namespace SpesnetTogglSync.Services;

/// <summary>
/// Open billing cycle compared with an 8-hour weekday target.
/// Actual hours are stopped Toggl time (raw seconds), the same basis as earnings.
/// </summary>
internal static class CycleTracking
{
    public const decimal HoursPerWorkDay = 8m;
    private const long SecondsPerWorkDay = 8 * 3600;
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    public static bool IsWorkDay(DateOnly date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    public static BillingPeriod FullCycle(DateOnly today, int startDay)
    {
        var start = BillingCycle.StartContaining(today, startDay);
        return new BillingPeriod(start, NextCycleStart(start, startDay).AddDays(-1));
    }

    public static async Task<CycleTrackingSnapshot> LoadAsync(
        ITogglClient toggl,
        int startDay,
        decimal hourlyRate,
        CancellationToken cancellationToken)
    {
        var today = SouthAfricaClock.Today();
        var cycle = FullCycle(today, startDay);
        var entries = await toggl.GetTimeEntriesBetweenAsync(
            SouthAfricaClock.StartOfDayUtc(cycle.Start),
            SouthAfricaClock.StartOfDayUtc(today.AddDays(1)),
            cancellationToken);

        var secondsByDay = new Dictionary<DateOnly, long>();
        foreach (var entry in entries)
        {
            if (entry.Duration <= 0)
            {
                continue;
            }

            var date = SouthAfricaClock.Date(entry.StartUtc);
            if (date < cycle.Start || date > today)
            {
                continue;
            }

            secondsByDay[date] = secondsByDay.GetValueOrDefault(date) + entry.Duration;
        }

        return Build(today, startDay, hourlyRate, secondsByDay);
    }

    public static CycleTrackingSnapshot Build(
        DateOnly today,
        int startDay,
        decimal hourlyRate,
        IReadOnlyDictionary<DateOnly, long> secondsByDay)
    {
        var cycle = FullCycle(today, startDay);
        var days = new List<CycleDayPoint>();
        long actualSeconds = 0;
        long targetSeconds = 0;
        var workDaysToDate = 0;
        var workDaysFull = 0;

        for (var date = cycle.Start; date <= cycle.End; date = date.AddDays(1))
        {
            if (IsWorkDay(date))
            {
                targetSeconds += SecondsPerWorkDay;
                workDaysFull++;
                if (date <= today)
                {
                    workDaysToDate++;
                }
            }

            var hasActual = date <= today;
            if (hasActual)
            {
                actualSeconds += secondsByDay.GetValueOrDefault(date);
            }

            days.Add(new CycleDayPoint(
                date,
                hasActual ? Hours(actualSeconds) : null,
                Hours(targetSeconds)));
        }

        var targetToDate = Hours(workDaysToDate * SecondsPerWorkDay);
        var actualHours = Hours(actualSeconds);
        return new CycleTrackingSnapshot
        {
            Cycle = cycle,
            Today = today,
            HourlyRate = hourlyRate,
            ActualHours = actualHours,
            TargetHoursToDate = targetToDate,
            TargetHoursFull = Hours(workDaysFull * SecondsPerWorkDay),
            WorkDaysToDate = workDaysToDate,
            WorkDaysFull = workDaysFull,
            ActualMoney = Money(actualHours, hourlyRate),
            TargetMoneyToDate = Money(targetToDate, hourlyRate),
            TargetMoneyFull = Money(Hours(workDaysFull * SecondsPerWorkDay), hourlyRate),
            Days = days,
            FetchedUtc = DateTime.UtcNow
        };
    }

    public static decimal Hours(long seconds) =>
        decimal.Round(seconds / 3600m, 2, MidpointRounding.AwayFromZero);

    public static decimal Money(decimal hours, decimal hourlyRate) =>
        decimal.Round(hours * hourlyRate, 2, MidpointRounding.AwayFromZero);

    public static string FormatHours(decimal hours) =>
        hours.ToString("0.00", CultureInfo.InvariantCulture) + "h";

    public static string FormatMoney(decimal amount) =>
        "R" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static DateOnly NextCycleStart(DateOnly cycleStart, int startDay)
    {
        var cursor = cycleStart.AddDays(1);
        for (var i = 0; i < 62; i++)
        {
            var containing = BillingCycle.StartContaining(cursor, startDay);
            if (containing != cycleStart)
            {
                return containing;
            }

            cursor = cursor.AddDays(1);
        }

        return cycleStart.AddDays(1);
    }
}

internal sealed class CycleTrackingSnapshot
{
    public required BillingPeriod Cycle { get; init; }
    public required DateOnly Today { get; init; }
    public required decimal HourlyRate { get; init; }
    public required decimal ActualHours { get; init; }
    public required decimal TargetHoursToDate { get; init; }
    public required decimal TargetHoursFull { get; init; }
    public required int WorkDaysToDate { get; init; }
    public required int WorkDaysFull { get; init; }
    public required decimal ActualMoney { get; init; }
    public required decimal TargetMoneyToDate { get; init; }
    public required decimal TargetMoneyFull { get; init; }
    public required IReadOnlyList<CycleDayPoint> Days { get; init; }
    public required DateTime FetchedUtc { get; init; }

    public decimal HoursVariance => ActualHours - TargetHoursToDate;

    public decimal MoneyVariance => ActualMoney - TargetMoneyToDate;

    public bool IsFresh => DateTime.UtcNow - FetchedUtc < CycleTracking.CacheLifetime;

    public string HoverText()
    {
        var range = FormatRange(Cycle.Start, Cycle.End);
        var builder = new StringBuilder();
        builder.AppendLine(range);
        builder.Append("Actual  ").Append(CycleTracking.FormatHours(ActualHours));
        AppendMoney(builder, ActualMoney);
        builder.AppendLine();
        builder.Append("Target  ").Append(CycleTracking.FormatHours(TargetHoursToDate));
        AppendMoney(builder, TargetMoneyToDate);
        builder.AppendLine();
        AppendVariance(builder);
        builder.AppendLine();
        builder.Append("Click to open the tracking chart");
        return builder.ToString();
    }

    public string DetailText()
    {
        var builder = new StringBuilder();
        builder.Append("Cycle ").Append(FormatRange(Cycle.Start, Cycle.End));
        builder.AppendLine();
        builder.Append("Actual  ").Append(CycleTracking.FormatHours(ActualHours));
        AppendMoney(builder, ActualMoney);
        builder.AppendLine();
        builder.Append("Target through today  ").Append(CycleTracking.FormatHours(TargetHoursToDate));
        AppendMoney(builder, TargetMoneyToDate);
        builder.Append("  (").Append(WorkDaysToDate).Append(" weekdays × 8h)");
        builder.AppendLine();
        AppendVariance(builder);
        builder.AppendLine();
        builder.Append("Full cycle target  ").Append(CycleTracking.FormatHours(TargetHoursFull));
        AppendMoney(builder, TargetMoneyFull);
        builder.Append("  (").Append(WorkDaysFull).Append(" weekdays)");
        if (HourlyRate <= 0)
        {
            builder.AppendLine();
            builder.Append("Set an hourly rate to see rands.");
        }

        builder.AppendLine();
        builder.Append("Target counts 8 hours for each weekday (Monday–Friday), including today. Actual hours are tracked Toggl time.");
        return builder.ToString();
    }

    private string VarianceLabel() => HoursVariance switch
    {
        > 0 => "Ahead",
        < 0 => "Behind",
        _ => "On target"
    };

    private void AppendVariance(StringBuilder builder)
    {
        if (HoursVariance == 0)
        {
            builder.Append("On target");
            return;
        }

        builder.Append(VarianceLabel()).Append(" by ").Append(CycleTracking.FormatHours(Math.Abs(HoursVariance)));
        if (HourlyRate > 0)
        {
            builder.Append(" (").Append(CycleTracking.FormatMoney(Math.Abs(MoneyVariance))).Append(')');
        }
    }

    private void AppendMoney(StringBuilder builder, decimal amount)
    {
        if (HourlyRate > 0)
        {
            builder.Append("  (").Append(CycleTracking.FormatMoney(amount)).Append(')');
        }
    }

    private static string FormatRange(DateOnly start, DateOnly end)
    {
        if (start.Year == end.Year)
        {
            return string.Concat(
                start.ToString("d MMM", CultureInfo.InvariantCulture),
                " – ",
                end.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
        }

        return string.Concat(
            start.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
            " – ",
            end.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
    }
}

internal readonly record struct CycleDayPoint(DateOnly Date, decimal? CumulativeActualHours, decimal CumulativeTargetHours);
