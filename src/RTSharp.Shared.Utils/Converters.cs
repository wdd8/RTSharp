using System;
using System.Globalization;

namespace RTSharp.Shared.Utils
{
    public static class Converters
    {
        public const string ZeroBytes = "0 B";
        public const string ZeroBytesPerSec = "0 B/s";

        public static string GetSIDataSize(ulong In) => FormatSI(In, ZeroBytes, [ " B", " KiB", " MiB", " GiB", " TiB", " PiB", " EiB" ]);

        public static string GetSIDataSpeed(ulong In) => FormatSI(In, ZeroBytesPerSec, [ " B/s", " KiB/s", " MiB/s", " GiB/s", " TiB/s", " PiB/s", " EiB/s" ]);

        private static string FormatSI(ulong In, string zero, ReadOnlySpan<string> suffixes)
        {
            if (In == 0)
                return zero;

            if (In < 1024) {
                Span<char> buf = stackalloc char[16];
                In.TryFormat(buf, out int w, provider: CultureInfo.CurrentCulture);
                return string.Concat(buf[..w], suffixes[0]);
            }

            float value = In;
            int unitIdx = 0;
            while (value >= 1024f && unitIdx < suffixes.Length - 1) {
                value /= 1024f;
                unitIdx++;
            }

            double rounded = Math.Round((double)value, 3);
            string suffix = suffixes[unitIdx];

            Span<char> numBuf = stackalloc char[16];
            rounded.TryFormat(numBuf, out int written, provider: CultureInfo.CurrentCulture);
            return string.Concat(numBuf[..written], suffix);
        }

        public static bool TryParseSISpeed(string In, bool TreatAs1024, out ulong Ret)
        {
            Ret = 0;

            var src = In.AsSpan();
            int units = 0;
            while (Char.IsDigit(src[units]) || src[units] == '.')
                units++;

            if (!Double.TryParse(src[..units], CultureInfo.InvariantCulture, out var num))
                return false;

            var unit = src[units..].TrimStart();
            if (!unit.EndsWith("/s", StringComparison.Ordinal))
                return false;

            unit = unit[..^2];

            var mult = TreatAs1024 ? 1024 : 1000;

            Ret = (ulong)(unit switch {
                "B" => num,
                "kB" or "KB" => num * mult,
                "kiB" or "KiB" => num * 1024,
                "MB" => num * mult * mult,
                "MiB" => num * 1024 * 1024,
                "GB" => num * mult * mult * mult,
                "GiB" => num * 1024 * 1024 * 1024,
                "TB" => num * mult * mult * mult * mult,
                "TiB" => num * 1024 * 1024 * 1024 * 1024,
                "PB" => num * mult * mult * mult * mult * mult,
                "PiB" => num * 1024 * 1024 * 1024 * 1024 * 1024,
                "EB" => num * mult * mult * mult * mult * mult * mult,
                "EiB" => num * 1024 * 1024 * 1024 * 1024 * 1024 * 1024,
                _ => throw new ArgumentOutOfRangeException(nameof(In))
            });

            if (Ret == 0 && num != 0)
                return false;

            return true;
        }

        public static ulong ParseSISpeed(string In, bool TreatAs1024)
        {
            var src = In.AsSpan();
            int units = 0;
            while (Char.IsDigit(src[units]) || src[units] == '.')
                units++;

            var num = Double.Parse(src[..units], CultureInfo.InvariantCulture);

            var unit = src[units..].TrimStart();
            if (!unit.EndsWith("/s", StringComparison.Ordinal))
                throw new NotImplementedException("non-second time not supported");

            unit = unit[..^2];

            var mult = TreatAs1024 ? 1024 : 1000;

            return (ulong)(unit switch {
                "B" => num,
                "kB" or "KB" => num * mult,
                "kiB" or "KiB" => num * 1024,
                "MB" => num * mult * mult,
                "MiB" => num * 1024 * 1024,
                "GB" => num * mult * mult * mult,
                "GiB" => num * 1024 * 1024 * 1024,
                "TB" => num * mult * mult * mult * mult,
                "TiB" => num * 1024 * 1024 * 1024 * 1024,
                "PB" => num * mult * mult * mult * mult * mult,
                "PiB" => num * 1024 * 1024 * 1024 * 1024 * 1024,
                "EB" => num * mult * mult * mult * mult * mult * mult,
                "EiB" => num * 1024 * 1024 * 1024 * 1024 * 1024 * 1024,
                _ => throw new NotImplementedException($"Unit {unit} not supported")
            });
        }

        public static string ToAgoString(TimeSpan In)
        {
            if (In == TimeSpan.MaxValue)
                return "∞";
            if (In == TimeSpan.Zero)
                return "0 ms";

            var days = (int)In.TotalDays;

            if (In < TimeSpan.FromMilliseconds(1)) {
                var ns = In.Nanoseconds;
                return string.Create(CountDigits(ns) + 2, ns, static (span, n) => {
                    n.TryFormat(span, out int w);
                    span[w] = 'n';
                    span[w + 1] = 's';
                });
            }

            var hours = In.Hours;
            var minutes = In.Minutes;
            var seconds = In.Seconds;
            var ms = In.Milliseconds;
            var showMs = ms != 0 && (days == 0 && hours == 0 && minutes == 0 && seconds == 0);

            int shownCount = 0;
            int len = 0;
            if (days != 0) {
                len += CountDigits(days) + 1;
                shownCount++;
            }
            if (hours != 0) {
                len += CountDigits(hours) + 1;
                shownCount++;
            }
            if (minutes != 0) {
                len += CountDigits(minutes) + 1;
                shownCount++;
            }
            if (seconds != 0) {
                len += CountDigits(seconds) + 1;
                shownCount++;
            }
            if (showMs) {
                len += CountDigits(ms) + 2;
                shownCount++;
            }
            len += shownCount - 1;

            return string.Create(len, (days, hours, minutes, seconds, ms, showMs), static (span, t) => {
                int pos = 0;
                bool needSpace = false;
                if (t.days != 0) {
                    t.days.TryFormat(span[pos..], out int written);
                    pos += written;
                    span[pos++] = 'd';
                    needSpace = true;
                }
                if (t.hours != 0) {
                    if (needSpace)
                        span[pos++] = ' ';
                    t.hours.TryFormat(span[pos..], out int written);
                    pos += written;
                    span[pos++] = 'h';
                    needSpace = true;
                }
                if (t.minutes != 0) {
                    if (needSpace)
                        span[pos++] = ' ';
                    t.minutes.TryFormat(span[pos..], out int written);
                    pos += written;
                    span[pos++] = 'm';
                    needSpace = true;
                }
                if (t.seconds != 0) {
                    if (needSpace)
                        span[pos++] = ' ';
                    t.seconds.TryFormat(span[pos..], out int written);
                    pos += written;
                    span[pos++] = 's';
                    needSpace = true;
                }
                if (t.showMs) {
                    if (needSpace)
                        span[pos++] = ' ';
                    t.ms.TryFormat(span[pos..], out int written);
                    pos += written;
                    span[pos++] = 'm';
                    span[pos] = 's';
                }
            });
        }

        private static int CountDigits(int n) => n == 0 ? 1 : (int)Math.Log10(n) + 1;

        /// <summary>
        /// Accepts hours greater than 23
        /// </summary>
        public static bool TryParseTimeSpan(string In, out TimeSpan Ret)
        {
            Ret = default;

            var src = In.AsSpan();
            Span<Range> parts = stackalloc Range[4]; // 4 in case of excessive splits
            if (src.Split(parts, ':') != 3)
                return false;

            if (!Int32.TryParse(src[parts[0]], out var hours))
                return false;
            if (!Int32.TryParse(src[parts[1]], out var minutes))
                return false;
            if (!Int32.TryParse(src[parts[2]], out var seconds))
                return false;

            Ret = new TimeSpan(days: hours / 24, hours % 24, minutes, seconds);
            return true;
        }

        public static string ToRFC3339String(DateTime In)
            => In.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
