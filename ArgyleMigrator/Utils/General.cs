using System;

namespace ArgyleMigrator.Utils
{
    public class General
    {
        public static DateTime ConvertUnixTimestampToDateTime(double unixTimestamp)
        {
            // The Unix epoch starts at 1970-01-01T00:00:00Z
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Add the total seconds to the epoch and return the DateTime object
            DateTime dateTime = epoch.AddSeconds(Math.Floor(unixTimestamp));

            // Add the milliseconds/microseconds part
            double fractionalPart = unixTimestamp - Math.Floor(unixTimestamp);
            dateTime = dateTime.AddSeconds(fractionalPart);

            return dateTime.ToLocalTime(); // Convert to local time if necessary
        }
    }
}
