using System.IO;

namespace UZipDotNet.ConsoleApp.VS2013
{
    class Program
    {
        static void Main(string[] args)
        {
            File.Delete(@"c:\temp\uziptest.zip");

            using (var def = new DeflateZipFile(@"c:\temp\uziptest.zip"))
            {
                // compress file
                def.Compress("C:\\Tools\\test.msi", "test1.msi");
                def.Compress("C:\\Tools\\test.msi", "test2.msi");

                // save archive
                def.Save();
            }


            using (var inf = new InflateZipFile(@"c:\temp\uziptest.zip"))
            {
                inf.Decompress(inf.ZipDir[0], @"c:\temp\unzip", null, true, true);
                inf.Decompress(inf.ZipDir[1], @"c:\temp\unzip", null, true, true);
            }
        }
    }
}