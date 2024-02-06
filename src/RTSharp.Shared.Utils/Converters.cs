using System;
using System.Globalization;

namespace RTSharp.Shared.Utils
{
    public static class Converters
    {
        public static string GetSIDataSize(ulong In)
        {
            if (In < 1024)
                return In + " B";
            if (In < 1024 * 1024)
                return Math.Round((float)In / 1024, 3) + " KiB";
            if (In < 1024 * 1024 * 1024)
                return Math.Round((float)In / 1024 / 1024, 3) + " MiB";
            if (In < (ulong)1024 * 1024 * 1024 * 1024)
                return Math.Round((float)In / 1024 / 1024 / 1024, 3) + " GiB";
            if (In < (ulong)1024 * 1024 * 1024 * 1024 * 1024)
                return Math.Round((float)In / 1024 / 1024 / 1024 / 1024, 3) + " TiB";
            if (In < (ulong)1024 * 1024 * 1024 * 1024 * 1024 * 1024)
                return Math.Round((float)In / 1024 / 1024 / 1024 / 1024 / 1024, 3) + " PiB";
            return Math.Round((float)In / 1024 / 1024 / 1024 / 1024 / 1024 / 1024, 3) + " EiB";
        }

        public static bool TryParseSISpeed(string In, bool TreatAs1024, out ulong Ret)
        {
            Ret = 0;

            int units = 0;
            while (Char.IsDigit(In[units]) || In[units] == '.')
                units++;

            if (!Double.TryParse(In[..units], CultureInfo.InvariantCulture, out var num))
                return false;

            var unit = In[units..].TrimStart();
            if (!unit.EndsWith("/s"))
                return false;

            unit = unit[..^2];

            var mult = TreatAs1024 ? 1024 : 1000;

            bool good = true;

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
                "EiB" => num * 1024 * 1024 * 1024 * 1024 * 1024 * 1024
            });

            if (Ret == 0 && num != 0)
                return false;

            return good;
        }

        public static ulong ParseSISpeed(string In, bool TreatAs1024)
        {
            int units = 0;
            while (Char.IsDigit(In[units]) || In[units] == '.')
                units++;

            var num = Double.Parse(In[..units], CultureInfo.InvariantCulture);

            var unit = In[units..].TrimStart();
            if (!unit.EndsWith("/s"))
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
            var days = (int)In.TotalDays;
            return (days != 0 ? (days + "d ") : "") + (In.Hours != 0 ? (In.Hours + "h ") : "") + (In.Minutes != 0 ? (In.Minutes + "m ") : "") + (In.Seconds != 0 ? (In.Seconds + "s") : "");
        }

        /// <summary>
        /// Accepts hours greater than 23
        /// </summary>
        public static bool TryParseTimeSpan(string In, out TimeSpan Ret)
        {
            Ret = default;

            var parts = In.Split(':');

            if (parts.Length != 3)
                return false;

            if (!Int32.TryParse(parts[0], out var hours))
                return false;
            if (!Int32.TryParse(parts[1], out var minutes))
                return false;
            if (!Int32.TryParse(parts[2], out var seconds))
                return false;

            Ret = new TimeSpan(days: hours / 24, hours % 24, minutes, seconds);
            return true;
        }

        public static string ToRFC3339String(DateTime In)
            => In.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
