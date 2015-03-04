namespace UZipDotNet
{
    public interface IDeflateFile
    {
        void Compress(string fullFileName, string archiveFileName);
    }
}