using System;
using System.IO;

namespace UZipDotNet.Extensions
{
    public static class FileInfoExtensions
    {
        public static FileTimeDetails GetFileTimeDetails(this FileInfo fi)
        {
            return new FileTimeDetails
            {
                LastWriteTime = fi.LastWriteTime,
                LastWriteTimeUtc = fi.LastWriteTimeUtc,

                LastAccessTime = fi.LastAccessTime,
                LastAccessTimeUtc = fi.LastWriteTime,

                // Workaround for Linux : just take LastWriteTime
                CreationTime = fi.CreationTime != DateTime.MinValue ? fi.CreationTime : fi.LastWriteTime,
                CreationTimeUtc = fi.CreationTimeUtc != DateTime.MinValue ? fi.CreationTimeUtc : fi.LastWriteTimeUtc
            };
        }
    }
}