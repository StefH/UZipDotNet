/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateNoHeader.cs
//	Class designed to decompress a file to another file. The input
//	file has no header or trailer information. This class is derived
//	from InflateMethod class. Not used in this project.
//
//	Granotech Limited
//	Author: Uzi Granot
//	Version 1.0
//	March 30, 2012
//	Copyright (C) 2012 Granotech Limited. All Rights Reserved
//
//	UZipDotNet application is a free software.
//	It is distributed under the Code Project Open License (CPOL).
//	The document UZipDotNetReadmeAndLicense.pdf contained within
//	the distribution specify the license agreement and other
//	conditions and notes. You must read this document and agree
//	with the conditions specified in order to use this software.
//
/////////////////////////////////////////////////////////////////////

using System;
using System.IO;
using System.Text;

namespace UZipDotNet
{
    public class InflateNoHeader : InflateMethod
    {
        public uint ReadTotal;
        public uint WriteTotal;

        private String _readFileName;
        private FileStream _readStream;
        private BinaryReader _readFile;
        private uint _readRemain;

        private String _writeFileName;
        private FileStream _writeStream;
        private BinaryWriter _writeFile;

        /// <summary>
        ///  Decompress one file
        /// </summary>
        /// <param name="readFileName">Name of the read file.</param>
        /// <param name="writeFileName">Name of the write file.</param>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">No support for files over 4GB</exception>
        public bool Decompress(string readFileName, string writeFileName)
        {
            try
            {
                // save name
                _readFileName = readFileName;

                // open source file for reading
                _readStream = new FileStream(readFileName, FileMode.Open, FileAccess.Read, FileShare.Read);

                // convert stream to binary reader
                _readFile = new BinaryReader(_readStream, Encoding.UTF8);

                // file is too long
                if (_readStream.Length > 0xffffffff) throw new Exception("No support for files over 4GB");

                // save file length
                _readRemain = (uint)_readStream.Length;
                ReadTotal = _readRemain;

                // save name
                _writeFileName = writeFileName;

                // create destination file
                _writeStream = new FileStream(writeFileName, FileMode.Create, FileAccess.Write, FileShare.None);

                // convert stream to binary writer
                _writeFile = new BinaryWriter(_writeStream, Encoding.UTF8);

                // decompress the file
                Decompress();

                // close read file
                _readFile.Dispose();
                _readFile = null;

                // save file length
                WriteTotal = (uint)_writeStream.Length;

                // close write file
                _writeFile.Dispose();
                _writeFile = null;

                // successful exit
                return (false);
            }

            // make sure read and write files are closed
            catch (Exception)
            {
                // close the read file if it is open
                if (_readFile != null)
                {
                    _readFile.Dispose();
                    _readFile = null;
                }

                // close the write file if it is open
                if (_writeFile != null)
                {
                    _writeFile.Dispose();
                    _writeFile = null;
                }

                // error exit
                //ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
                return (true);
            }
        }

        /// <summary>
        ///  Read Bytes Routine
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        /// <param name="endOfFile">if set to <c>true</c> [end of file].</param>
        /// <returns></returns>
        protected override int ReadBytes(byte[] buffer, int pos, int len, out bool endOfFile)
        {
            len = len > _readRemain ? (int)_readRemain : len;
            _readRemain -= (uint)len;
            ReadTotal += (uint)len;
            endOfFile = _readRemain == 0;

            return (_readFile.Read(buffer, pos, len));
        }

        /// <summary>
        /// Writes the bytes.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        protected override void WriteBytes(byte[] buffer, int pos, int len)
        {
            _writeFile.Write(buffer, pos, len);
            WriteTotal += (uint)len;
        }

        public override void Dispose()
        {
            //
        }
    }
}