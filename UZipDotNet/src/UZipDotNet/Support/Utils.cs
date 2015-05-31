using System.Text;

namespace UZipDotNet.Support
{
    internal static class Utils
    {
#if DNXCORE50
        private static Encoding Encoding = Encoding.UTF8;
#else
        private static Encoding Encoding = Encoding.GetEncoding(437);
#endif

        /// <summary>
        /// Convert filename string to array of bytes using DOS (IBM OEM code page 437) encoding
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <returns></returns>
        public static byte[] EncodeFilename(string filename)
        {
            return Encoding.GetBytes(filename.Replace('\\', '/'));
        }

        /// <summary>
        /// Extract a string from the byte array using DOS (IBM OEM code page 437)
        /// Replace the unix forward slash with microsoft back slash.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns></returns>
        public static string DecodeFilename(byte[] bytes)
        {
            return Encoding.GetString(bytes).Replace('/', '\\');
        }
    }
}