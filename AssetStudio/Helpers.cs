using System.Text;
using System.Text.RegularExpressions;

namespace AssetStudio
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

            string ascii = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(name));
            StringBuilder newName = new StringBuilder();
            foreach (char c in ascii)
            {
                if (c == '?')
                {
                    newName.Append('Q');
                    continue;
                }

                if (!char.IsDigit(c) && !char.IsLetter(c))
                {
                    newName.Append('_');
                    continue;
                }

                if (newName.Length <= 0)
                {
                    if (char.IsDigit(c))
                        newName.Append('_');
                }
                newName.Append(c);
            }

            return newName.ToString();
        }
    }
}
