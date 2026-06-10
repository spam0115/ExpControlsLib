using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsApiLib
{
    internal class Utils
    {
        public static bool WildcardLike(string input, string pattern)
        {
            if (input is null || pattern is null) return false;

            // Supports * and ? wildcard patterns (typical filter usage: *.doc, *.txt, file??.log)
            string regex = "^" +
                           Regex.Escape(pattern)
                                .Replace(@"\*", ".*")
                                .Replace(@"\?", ".") +
                           "$";

            return Regex.IsMatch(input, regex, RegexOptions.CultureInvariant);
        }

        public static long GetDiskSize(string drivePath)
        {
            try
            {
                var deviceId = drivePath.Substring(0, 2);
                var disk = new System.Management.ManagementObject("win32_logicaldisk.deviceid=\"" + deviceId + "\"");
                return Convert.ToInt64(disk["Size"]);
            }
            catch
            {
                return 0;
            }
        }
    }
}
