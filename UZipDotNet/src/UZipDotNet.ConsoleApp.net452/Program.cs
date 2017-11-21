using System;
using System.IO;

namespace UZipDotNet.ConsoleApp.net452
{
    class Program
    {
        static void Main(string[] args)
        {
            string temp = args.Length > 0 ? args[0] : @"c:\temp";
            string tempunzip = Path.Combine(temp, "unzip");
            string filepath = Path.Combine(temp, "uziptest.zip");

            Console.WriteLine("start");
            try
            {
                File.Delete(filepath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ignore : " + ex);
            }

            using (var def = new DeflateZipFile(filepath))
            {
                // compress files
                def.Compress("../UZipDotNet/DeflateZipFile.cs", "NewFileName.txt");
                def.Compress("../UZipDotNet/DeflateMethod.cs", "DeflateMethod.cs");

                // save archive
                def.Save();
            }

            // un-compress files
            using (var inf = new InflateZipFile(filepath))
            {
                inf.Decompress(inf.ZipDir[0], tempunzip, null, true, true);
                inf.Decompress(inf.ZipDir[1], tempunzip, null, true, true);
            }
            Console.WriteLine("end");
        }
    }
}