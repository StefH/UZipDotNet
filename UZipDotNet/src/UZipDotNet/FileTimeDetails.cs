using System;

namespace UZipDotNet
{
    public class FileTimeDetails
    {
        public DateTime CreationTime { get; set; }
    
        public DateTime CreationTimeUtc { get; set; }

        public long CreationTimeAsFileTime
        {
            get { return CreationTime.ToFileTime(); }
        }

        public long CreationTimeUtcAsFileTime
        {
            get { return CreationTimeUtc.ToFileTime(); }
        }
       
        public DateTime LastAccessTime { get; set; }

        public DateTime LastAccessTimeUtc { get; set; }

        public long LastAccessTimeAsFileTime
        {
            get { return LastAccessTime.ToFileTime(); }
        }

        public long LastAccessTimeUtcAsFileTime
        {
            get { return LastAccessTimeUtc.ToFileTime(); }
        }

        public DateTime LastWriteTime { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }

        public long LastWriteTimeAsFileTime
        {
            get { return LastWriteTime.ToFileTime(); }
        }

        public long LastWriteTimeUtcAsFileTime
        {
            get { return LastWriteTimeUtc.ToFileTime(); }
        }
    }
}
