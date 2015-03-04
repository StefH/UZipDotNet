using System;

namespace UZipDotNet
{
    public interface IInflateFile
    {
        void Decompress(FileHeader fh, string rootPathName, string newFileName, bool createPath, bool overWrite);
    }
}