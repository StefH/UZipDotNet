/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateZLib.cs
//	Class designed to decompress a file to another file. The
//	decompressed file will have ZLIB header and Adler32 checksum
//	at the end of the file. This class is derived from InflateMethod
//	class. This class is used by the application to test the
//	decompression software.
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
using UZipDotNet.Support;

namespace UZipDotNet
{
    public class InflateZLib : InflateMethod
    {
        public String[] ExceptionStack;
        public uint ReadTotal;
        public uint WriteTotal;

        private String _readFileName;
        private FileStream _readStream;
        private BinaryReader _readFile;
        private uint _readRemain;

        private String _writeFileName;
        private FileStream _writeStream;
        private BinaryWriter _writeFile;
        private uint _writeAdler32;

        /// <summary>
        /// Decompress one file
        /// </summary>
        /// <param name="readFileName">Name of the read file.</param>
        /// <param name="writeFileName">Name of the write file.</param>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">
        /// No support for files over 4GB
        /// or
        /// ZLIB file header is in error
        /// or
        /// ZLIB file Adler32 test failed
        /// </exception>
        public void DecompressFile(string readFileName, string writeFileName)
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

                // compressed part of the file
                // we subtract 2 bytes for header and 4 bytes for adler32 checksum
                _readRemain = (uint)_readStream.Length - 6;
                ReadTotal = _readRemain;

                // get ZLib header
                int header = (_readFile.ReadByte() << 8) | _readFile.ReadByte();

                // test header: chksum, compression method must be deflated, no support for external dictionary
                if (header % 31 != 0 || (header & 0xf00) != 0x800 && (header & 0xf00) != 0 || (header & 0x20) != 0)
                    throw new Exception("ZLIB file header is in error");

                // save name
                _writeFileName = writeFileName;

                // create destination file
                _writeStream = new FileStream(writeFileName, FileMode.Create, FileAccess.Write, FileShare.None);

                // convert stream to binary writer
                _writeFile = new BinaryWriter(_writeStream, Encoding.UTF8);

                // reset adler32 checksum
                _writeAdler32 = 1;

                // decompress the file
                if ((header & 0xf00) == 0x800)
                {
                    Decompress();
                }
                else
                {
                    NoCompression();
                }

                // ZLib checksum is Adler32
                if ((((uint)_readFile.ReadByte() << 24) | ((uint)_readFile.ReadByte() << 16) |
                    ((uint)_readFile.ReadByte() << 8) | (_readFile.ReadByte())) != _writeAdler32)
                    throw new Exception("ZLIB file Adler32 test failed");

                // close read file
                _readFile.Dispose();
                _readFile = null;

                // save file length
                WriteTotal = (uint)_writeStream.Length;

                // close write file
                _writeFile.Dispose();
                _writeFile = null;
            }
            catch (Exception)
            {
                Dispose();

                throw;
            }
        }

        /// <summary>
        /// Read Bytes Routine
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
            endOfFile = _readRemain == 0;

            return (_readFile.Read(buffer, pos, len));
        }

        /// <summary>
        /// Write Bytes Routine
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        protected override void WriteBytes(byte[] buffer, int pos, int len)
        {
            _writeAdler32 = Adler32.Checksum(_writeAdler32, buffer, pos, len);
            _writeFile.Write(buffer, pos, len);
        }

        public override void Dispose()
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
        }
    }
}