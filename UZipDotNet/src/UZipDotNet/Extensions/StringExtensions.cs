
namespace UZipDotNet.Extensions
{
    public static class StringExtensions
    {
        public static bool Contains(this string source, char value)
        {
            return source != null && source.IndexOf(value) >= 0;
        }

        public static bool EndsWith(this string source, char value)
        {
            return source.EndsWith(new string(new [] { value }));
        }
    }
}