#nullable enable
using System;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    public readonly struct SerializableTimeZone
    {
        /// <summary>
        /// Diff from UTC
        /// </summary>
        public readonly TimeSpan Value;

        private readonly int hours;
        private readonly int minutes;
        private readonly char sign;

        public SerializableTimeZone(TimeSpan value)
        {
            Value = new TimeSpan(
                hours: value.Hours,
                minutes: value.Minutes,
                seconds: 0);

            hours = Math.Abs(value.Hours);
            minutes = Math.Abs(value.Minutes);
            sign = Value.Ticks < 0 ? '-' : '+';
        }
        
        public override string ToString()
            => (hours, minutes) switch
            {
                (0, 0) => "UTC",
                (_, 0) => $"UTC{sign}{hours}",
                (_, < 10) => $"UTC{sign}{hours}:0{minutes}",
                _ => $"UTC{sign}{hours}:{minutes}"
            };

        public override int GetHashCode()
            => HashCode.Combine(Value.Ticks < 0, hours, minutes);

        public static SerializableTimeZone FromDateTime(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Unspecified)
            {
                throw new InvalidOperationException(
                    $"Cannot determine timezone for {nameof(DateTime)} " +
                    $"of unspecified kind");
            }
            var utcDateTime = dateTime.ToUniversalTime();
            return new SerializableTimeZone(dateTime - utcDateTime);
        }

        public static SerializableTimeZone LocalTimeZone
            => FromDateTime(DateTime.Now);

        public static Option<SerializableTimeZone> Parse(string str)
        {
            if (!str.StartsWith("UTC", StringComparison.OrdinalIgnoreCase))
            {
                return Option<SerializableTimeZone>.None();
            }
            string timeZoneStr = str[3..];
            bool negative = timeZoneStr.StartsWith("-");
            bool valid = negative || timeZoneStr.StartsWith("+");
            
            if (!valid) { return Option<SerializableTimeZone>.None(); }
            
            timeZoneStr = str[4..];

            TimeSpan makeTimeSpan(int hours, int minutes)
                => new TimeSpan(
                    ticks: (hours * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute)
                    * (negative ? -1L : 1L));
            
            if (timeZoneStr.IndexOf(':') is var hrMinSeparator && hrMinSeparator > 0)
            {
                if (int.TryParse(timeZoneStr[..hrMinSeparator], out int timeZoneHours)
                    && int.TryParse(timeZoneStr[(hrMinSeparator + 1)..], out int timeZoneMinutes))
                {
                    return Option<SerializableTimeZone>.Some(
                        new SerializableTimeZone(makeTimeSpan(timeZoneHours, timeZoneMinutes)));
                }
            }
            else if (int.TryParse(timeZoneStr, out int timeZoneHours))
            {
                return Option<SerializableTimeZone>.Some(
                    new SerializableTimeZone(makeTimeSpan(timeZoneHours, 0)));
            }
            return Option<SerializableTimeZone>.None();
        }
    }
    
    /// <summary>
    /// DateTime wrapper that tries to offer a reliable
    /// string representation that's also human-friendly
    /// </summary>
    public readonly struct SerializableDateTime : IComparable<SerializableDateTime>
    {
        public bool Equals(SerializableDateTime other)
            => ToUtc().value.Equals(other.ToUtc().value);

        public override bool Equals(object? obj)
            => obj is SerializableDateTime other && Equals(other);

        private static DateTime UnixEpoch(DateTimeKind kind)
            => new DateTime(1970, 1, 1, 0, 0, 0, kind);

        private readonly DateTime value;
        public readonly SerializableTimeZone TimeZone;

        public SerializableDateTime(DateTime value) : this(value, default)
        {
            if (value.Kind == DateTimeKind.Unspecified)
            {
                throw new Exception($"Timezone required when constructing {nameof(SerializableDateTime)} " +
                                    $"from {nameof(DateTime)} of unspecified kind");
            }
            TimeZone = SerializableTimeZone.FromDateTime(value);
        }
        
        public SerializableDateTime(DateTime value, SerializableTimeZone timeZone)
        {
            this.value = new DateTime(
                value.Year, value.Month, value.Day,
                value.Hour, value.Minute, value.Second,
                DateTimeKind.Unspecified);
            TimeZone = timeZone;
        }

        public static SerializableDateTime LocalNow
            => new SerializableDateTime(DateTime.Now);

        public static SerializableDateTime UtcNow
            => new SerializableDateTime(DateTime.UtcNow);
        
        public SerializableDateTime ToUtc()
            => new SerializableDateTime(
                DateTime.SpecifyKind(value - TimeZone.Value, DateTimeKind.Utc));

        public SerializableDateTime ToLocal()
            => new SerializableDateTime(
                new DateTime(ticks: value.Ticks) - TimeZone.Value + SerializableTimeZone.LocalTimeZone.Value,
                SerializableTimeZone.LocalTimeZone);

        public long Ticks => value.Ticks;
        
        public DateTime ToUtcValue() => ToUtc().value;
        public DateTime ToLocalValue() => ToLocal().value;

        public static SerializableDateTime FromLocalUnixTime(long unixTime)
            => new SerializableDateTime(UnixEpoch(DateTimeKind.Local) + TimeSpan.FromSeconds(unixTime));
        
        public static SerializableDateTime FromUtcUnixTime(long unixTime)
            => new SerializableDateTime(UnixEpoch(DateTimeKind.Utc) + TimeSpan.FromSeconds(unixTime));

        public long ToUnixTime()
            => (value - UnixEpoch(value.Kind)).Ticks / TimeSpan.TicksPerSecond;

        private static string MakeString(params (long Value, string Suffix)[] parts)
            => string.Join(' ',
                parts.Select(p => $"{p.Value.ToString().PadLeft(2, '0')}{p.Suffix}"));

        public override string ToString()
            => MakeString(
                // Let's go out of our way to tag
                // the year, month and day so nobody
                // gets confused about the meaning of
                // each number
                (value.Year, "Y"),
                (value.Month, "M"),
                (value.Day, "D"),

                (value.Hour, "HR"),
                (value.Minute, "MIN"),
                (value.Second, "SEC"))
            + $" {TimeZone}";

        public string ToLocalUserString()
            => ToLocalValue().ToString(CultureInfo.InvariantCulture);
        
        public override int GetHashCode()
            => HashCode.Combine(
                value.Year, value.Month, value.Day,
                value.Hour, value.Minute, value.Second,
                TimeZone.GetHashCode());

        public static Option<SerializableDateTime> Parse(string str)
        {
            if (long.TryParse(str, out long unixTime)
                && unixTime > 0
                && unixTime < (DateTime.MaxValue - UnixEpoch(DateTimeKind.Utc)).TotalSeconds)
            {
                return Option<SerializableDateTime>.Some(FromUtcUnixTime(unixTime));
            }

            string[] split = str.Split(' ');

            int year = 0; int month = 0; int day = 0;
            int hour = 0; int minute = 0; int second = 0;
            SerializableTimeZone timeZone = default;
            foreach (var part in split)
            {
                if (SerializableTimeZone.Parse(part).TryUnwrap(out var parsedTimeZone))
                {
                    timeZone = parsedTimeZone;
                    continue;
                }
                
                Identifier suffix = string.Join("", part.Where(char.IsLetter)).ToIdentifier();
                if (!part.EndsWith(suffix.Value)) { continue; }
                if (!int.TryParse(
                        part[..^suffix.Value.Length],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int value))
                {
                    continue;
                }
                
                if (suffix == "Y") { year = value; }
                else if (suffix == "M") { month = value; }
                else if (suffix == "D") { day = value; }
                else if (suffix == "HR") { hour = value; }
                else if (suffix == "MIN") { minute = value; }
                else if (suffix == "SEC") { second = value; }
            }

            if (year > 0 && month > 0 && day > 0)
            {
                return Option<SerializableDateTime>.Some(
                    new SerializableDateTime(
                        new DateTime(year, month, day, hour, minute, second),
                        timeZone));
            }
            
            return Option<SerializableDateTime>.None();
        }

        public int CompareTo(SerializableDateTime other)
            => ToUtc().value.CompareTo(other.ToUtc().value);

        public static bool operator <(in SerializableDateTime a, in SerializableDateTime b)
            => a.CompareTo(b) < 0;

        public static bool operator >(in SerializableDateTime a, in SerializableDateTime b)
            => a.CompareTo(b) > 0;

        public static bool operator ==(in SerializableDateTime a, in SerializableDateTime b)
            => a.CompareTo(b) == 0;

        public static bool operator !=(in SerializableDateTime a, in SerializableDateTime b)
            => !(a == b);

        public static SerializableDateTime operator +(in SerializableDateTime dt, in TimeSpan ts)
            => new SerializableDateTime(dt.value + ts, dt.TimeZone);

        public static SerializableDateTime operator -(in SerializableDateTime dt, in TimeSpan ts)
            => new SerializableDateTime(dt.value - ts, dt.TimeZone);

        public static TimeSpan operator -(in SerializableDateTime a, in SerializableDateTime b)
            => a.ToUtc().value - b.ToUtc().value;
    }
}