using System;

namespace UZipDotNet.ConsoleApp
{
    public class Program
    {
        public void Main(string[] args)
        {
            Console.WriteLine("Hello World");

            Trace.Open(@"c:\temp\trace.txt");

            DeflateZipFile Def = new DeflateZipFile();

            // create empty zip file
            if (Def.CreateArchive(@"c:\temp\uziptest.zip"))
            {
                // save exception stack on error
                Console.WriteLine(string.Join("\r\n", Def.ExceptionStack));
                return;
            }

            // compress file
            if (Def.Compress("C:\\Tools\\test.msi", "test.msi"))
            {
                Console.WriteLine(string.Join("\r\n", Def.ExceptionStack));
                return;
            }
            // save archive
            if (Def.SaveArchive())
            {
                // save exception stack on error
                Console.WriteLine(string.Join("\r\n", Def.ExceptionStack));
                return;
            }

            /*
            // create decompression object
            InflateZipFile Inf = new InflateZipFile();

            // open the zip file
            if (Inf.OpenZipFile(CompFileName))
            {
                // save exception stack on error
                ExceptionStack = Inf.ExceptionStack;
                return (2);
            }

            // decompress the file
            if (Inf.DecompressZipFile(Inf.ZipDir[0], null, DecompFileName, false, true))
            {
                // save exception stack on error
                ExceptionStack = Inf.ExceptionStack;
                return (2);
            }

            // save decompression elapse time
            DecompTime = Environment.TickCount - StartTime;

            // save restored file length
            DecompFileLen = Inf.WriteTotal;
            */
            // close the zip file
            //Inf.CloseZipFile();
        }
    }
}
