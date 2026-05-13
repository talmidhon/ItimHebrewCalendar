using System;
using System.Collections.Generic;
using ItimHebrewCalendar.Models;

namespace ItimHebrewCalendar.Services
{
    public readonly record struct EventOccurrence(UserEvent Event, DateTime Date);

    public static class EventOccurrenceExpander
    {
        // Expands a single event into concrete occurrences whose dates fall in [fromInclusive, toInclusive].
        // Date-only; time-of-day is taken from ev.StartTime when scheduling reminders.
        public static IEnumerable<EventOccurrence> Expand(UserEvent ev, DateTime fromInclusive, DateTime toInclusive)
        {
            if (ev.StartGregorian == null) yield break;
            var start = ev.StartGregorian.Value.Date;
            var from = fromInclusive.Date;
            var to = toInclusive.Date;
            if (to < from) yield break;

            var rec = ev.Recurrence;
            if (rec == null || rec.Kind == RecurrenceKind.None)
            {
                if (start >= from && start <= to)
                    yield return new EventOccurrence(ev, start);
                yield break;
            }

            var until = rec.Until?.Date ?? DateTime.MaxValue.Date;
            var maxCount = rec.Count ?? int.MaxValue;
            var interval = Math.Max(1, rec.Interval);
            var clipTo = to < until ? to : until;

            switch (rec.Kind)
            {
                case RecurrenceKind.DailyGregorian:
                    foreach (var occ in EnumerateDaily(ev, start, from, clipTo, interval, maxCount))
                        yield return occ;
                    break;

                case RecurrenceKind.WeeklyGregorian:
                    foreach (var occ in EnumerateWeekly(ev, start, from, clipTo, interval, maxCount, rec.Weekdays))
                        yield return occ;
                    break;

                case RecurrenceKind.MonthlyGregorian:
                    foreach (var occ in EnumerateMonthlyGreg(ev, start, from, clipTo, interval, maxCount))
                        yield return occ;
                    break;

                case RecurrenceKind.YearlyGregorian:
                    foreach (var occ in EnumerateYearlyGreg(ev, start, from, clipTo, interval, maxCount))
                        yield return occ;
                    break;

                case RecurrenceKind.MonthlyHebrew:
                    foreach (var occ in EnumerateMonthlyHebrew(ev, from, clipTo, interval, maxCount, rec.LeapPolicy))
                        yield return occ;
                    break;

                case RecurrenceKind.YearlyHebrew:
                    foreach (var occ in EnumerateYearlyHebrew(ev, from, clipTo, interval, maxCount, rec.LeapPolicy))
                        yield return occ;
                    break;
            }
        }

        public static IEnumerable<EventOccurrence> ExpandMany(
            IEnumerable<UserEvent> events, DateTime fromInclusive, DateTime toInclusive)
        {
            foreach (var ev in events)
                foreach (var occ in Expand(ev, fromInclusive, toInclusive))
                    yield return occ;
        }

        // ─── Gregorian enumerators ─────────────────────────────────────────────────

        private static IEnumerable<EventOccurrence> EnumerateDaily(
            UserEvent ev, DateTime start, DateTime from, DateTime to, int interval, int maxCount)
        {
            int produced = 0;
            var d = start;
            while (d <= to && produced < maxCount)
            {
                if (d >= from) yield return new EventOccurrence(ev, d);
                produced++;
                d = d.AddDays(interval);
            }
        }

        private static IEnumerable<EventOccurrence> EnumerateWeekly(
            UserEvent ev, DateTime start, DateTime from, DateTime to,
            int interval, int maxCount, DaysOfWeek? mask)
        {
            int produced = 0;
            var weekStart = start;
            // Default mask: just the start's weekday.
            var effective = mask ?? DayOfWeekToFlag(start.DayOfWeek);

            while (weekStart <= to && produced < maxCount)
            {
                for (int i = 0; i < 7 && produced < maxCount; i++)
                {
                    var candidate = weekStart.AddDays(i);
                    if (candidate < start) continue;
                    if (candidate > to) break;
                    var flag = DayOfWeekToFlag(candidate.DayOfWeek);
                    if ((effective & flag) == 0) continue;
                    if (candidate >= from) yield return new EventOccurrence(ev, candidate);
                    produced++;
                }
                weekStart = weekStart.AddDays(7 * interval);
            }
        }

        private static IEnumerable<EventOccurrence> EnumerateMonthlyGreg(
            UserEvent ev, DateTime start, DateTime from, DateTime to, int interval, int maxCount)
        {
            int produced = 0;
            int day = start.Day;
            int year = start.Year;
            int month = start.Month;
            while (produced < maxCount)
            {
                int daysInMonth = DateTime.DaysInMonth(year, month);
                if (day <= daysInMonth)
                {
                    var candidate = new DateTime(year, month, day);
                    if (candidate > to) yield break;
                    if (candidate >= from) yield return new EventOccurrence(ev, candidate);
                    produced++;
                }
                // Advance interval months
                int totalMonths = year * 12 + (month - 1) + interval;
                year = totalMonths / 12;
                month = totalMonths % 12 + 1;
                if (year > to.Year + 1) yield break;
            }
        }

        private static IEnumerable<EventOccurrence> EnumerateYearlyGreg(
            UserEvent ev, DateTime start, DateTime from, DateTime to, int interval, int maxCount)
        {
            int produced = 0;
            int year = start.Year;
            while (produced < maxCount)
            {
                int month = start.Month;
                int day = start.Day;
                int dim = DateTime.DaysInMonth(year, month);
                if (day <= dim)
                {
                    var candidate = new DateTime(year, month, day);
                    if (candidate > to) yield break;
                    if (candidate >= from) yield return new EventOccurrence(ev, candidate);
                    produced++;
                }
                year += interval;
                if (year > to.Year + 1) yield break;
            }
        }

        // ─── Hebrew enumerators ────────────────────────────────────────────────────

        private static IEnumerable<EventOccurrence> EnumerateYearlyHebrew(
            UserEvent ev, DateTime from, DateTime to, int interval, int maxCount, LeapMonthPolicy policy)
        {
            if (ev.StartHebrew == null) yield break;
            int produced = 0;
            int hYear = ev.StartHebrew.Year;
            int hMonth = ev.StartHebrew.Month;
            int hDay = ev.StartHebrew.Day;

            // Bound number of iterations safely (cover up to 200 Hebrew years).
            int safetyMax = 200;
            int iter = 0;
            while (produced < maxCount && iter++ < safetyMax)
            {
                var greg = TryHebrewToGregorian(hYear, hMonth, hDay);
                if (greg.HasValue)
                {
                    if (greg.Value > to) yield break;
                    if (greg.Value >= from) yield return new EventOccurrence(ev, greg.Value);
                    produced++;
                }
                else if (policy == LeapMonthPolicy.ShiftToNextAvailable)
                {
                    // Try the previous day until we find a valid one (handles 30 -> 29 deficient months)
                    var shifted = TryShiftToNextAvailable(hYear, hMonth, hDay);
                    if (shifted.HasValue)
                    {
                        if (shifted.Value > to) yield break;
                        if (shifted.Value >= from) yield return new EventOccurrence(ev, shifted.Value);
                        produced++;
                    }
                }
                hYear += interval;
            }
        }

        private static IEnumerable<EventOccurrence> EnumerateMonthlyHebrew(
            UserEvent ev, DateTime from, DateTime to, int interval, int maxCount, LeapMonthPolicy policy)
        {
            if (ev.StartHebrew == null) yield break;
            int produced = 0;
            int hYear = ev.StartHebrew.Year;
            int hMonth = ev.StartHebrew.Month;
            int hDay = ev.StartHebrew.Day;

            int safetyMax = 24 * 200; // up to ~200 years of months
            int iter = 0;
            while (produced < maxCount && iter++ < safetyMax)
            {
                var greg = TryHebrewToGregorian(hYear, hMonth, hDay);
                if (greg.HasValue)
                {
                    if (greg.Value > to) yield break;
                    if (greg.Value >= from) yield return new EventOccurrence(ev, greg.Value);
                    produced++;
                }
                // else: Skip policy = just advance
                for (int i = 0; i < interval; i++)
                    AdvanceHebrewMonth(ref hYear, ref hMonth);
            }
        }

        // ─── Hebrew helpers ────────────────────────────────────────────────────────

        private static DateTime? TryHebrewToGregorian(int hYear, int hMonth, int hDay)
        {
            try
            {
                var g = HebcalBridge.ConvertFromHebrew(hYear, hMonth, hDay);
                if (g == null || g.Year == 0) return null;
                var d = new DateTime(g.Year, g.Month, g.Day);
                // Sanity round-trip to detect "invented" dates from Go
                var back = HebcalBridge.Convert(d);
                if (back == null) return null;
                if (back.HebYear != hYear || back.HebMonth != hMonth || back.HebDay != hDay)
                    return null;
                return d;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? TryShiftToNextAvailable(int hYear, int hMonth, int startDay)
        {
            for (int d = startDay - 1; d >= 1; d--)
            {
                var x = TryHebrewToGregorian(hYear, hMonth, d);
                if (x.HasValue) return x;
            }
            return null;
        }

        // Hebcal month numbering: 1=Nisan ... 6=Elul ... 7=Tishrei (new year), 8=Cheshvan ...
        // 12=Adar (or Adar I in leap), 13=Adar II (leap only).
        // Civil year boundary is Elul (6) -> Tishrei (7) of next year.
        private static void AdvanceHebrewMonth(ref int hYear, ref int hMonth)
        {
            if (hMonth == 6)
            {
                hYear += 1;
                hMonth = 7;
                return;
            }
            if (hMonth == 12)
            {
                if (IsHebrewLeapYear(hYear)) hMonth = 13;
                else hMonth = 1;
                return;
            }
            if (hMonth == 13)
            {
                hMonth = 1;
                return;
            }
            hMonth += 1;
        }

        private static bool IsHebrewLeapYear(int hYear)
        {
            // Leap years repeat every 19 years on positions 3,6,8,11,14,17,19 of the cycle.
            int pos = ((hYear - 1) % 19) + 1;
            return pos == 3 || pos == 6 || pos == 8 || pos == 11 || pos == 14 || pos == 17 || pos == 19;
        }

        private static DaysOfWeek DayOfWeekToFlag(DayOfWeek d) => d switch
        {
            DayOfWeek.Sunday    => DaysOfWeek.Sunday,
            DayOfWeek.Monday    => DaysOfWeek.Monday,
            DayOfWeek.Tuesday   => DaysOfWeek.Tuesday,
            DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
            DayOfWeek.Thursday  => DaysOfWeek.Thursday,
            DayOfWeek.Friday    => DaysOfWeek.Friday,
            DayOfWeek.Saturday  => DaysOfWeek.Saturday,
            _ => DaysOfWeek.None
        };
    }
}
