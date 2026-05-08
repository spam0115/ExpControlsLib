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
    }
}
