using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsApiLib
{
    public class Utils
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
                var di = new DriveInfo(drivePath.Substring(0, 2));
                return di.TotalSize;
            }
            catch
            {
                return 0;
            }
        }

        public static string EnsureTrailingSlash(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return Path.DirectorySeparatorChar.ToString();

            char separator = Path.DirectorySeparatorChar;

            if (path[^1] == Path.DirectorySeparatorChar ||
                path[^1] == Path.AltDirectorySeparatorChar)
            {
                return path;
            }

            return path + separator;
        }

        public static string RemoveTrailingDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            char separator = Path.DirectorySeparatorChar;

            return path.Trim().TrimEnd(Path.DirectorySeparatorChar).TrimEnd(Path.AltDirectorySeparatorChar);
        }

        public static (string, string) SplitPathAndFileName(string fullFileName)
        {
            int split = fullFileName.LastIndexOf('\\');

            var fileName = fullFileName.Substring(split + 1);
            var path = Utils.EnsureTrailingSlash(fullFileName.Substring(0, split + 1));

            return (path, fileName);
        }
    }
}
