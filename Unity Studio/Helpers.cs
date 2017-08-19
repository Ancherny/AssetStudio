using System.Text.RegularExpressions;

namespace UnityStudio
{
    internal class TexEnv
    {
        public string name;
        public PPtr m_Texture;
        public float[] m_Scale;
        public float[] m_Offset;
    }

    internal class StrFloatPair
    {
        public string first;
        public float second;
    }

    internal class StrColorPair
    {
        public string first;
        public float[] second;
    }

    public static class StringExtensions
    {
        /// <summary>
        /// Compares the string against a given pattern.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="pattern">The pattern to match, where "*" means any sequence of characters, and "?" means any single character.</param>
        /// <returns><c>true</c> if the string matches the given pattern; otherwise <c>false</c>.</returns>
        public static bool Like(this string str, string pattern)
        {
            return new Regex(
                "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            ).IsMatch(str);
        }
    }

    internal static class Helpers
    {
        public static string FixMayaName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";

            if (char.IsDigit(name[0]))
                return "_" + name;

            return name;
        }
    }
}
