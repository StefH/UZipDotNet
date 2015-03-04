/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	DeflateZLib.cs
//	Class designed to compress a file to another file. The
//	compressed file will have ZLIB header and Adler32 checksum
//	at the end of the file. This class is derived from DeflateMethod
//	class. This class is used by the application to test the
//	compression software.
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
    public class DeflateZLib : DeflateMethod, IDeflateFile
    {
        private String _readFileName;
        private FileStream _readStream;
        private BinaryReader _readFile;
        private UInt32 _readRemain;
        private UInt32 _readAdler32;

        private String _writeFileName;
        private FileStream _writeStream;
        private BinaryWriter _writeFile;

        private static readonly int[] CompLevelTable = { 0, 1, 1, 1, 2, 2, 2, 3, 3, 3 };

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateZLib"/> class.
        /// </summary>
        public DeflateZLib() : base(DefaultCompression) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateZLib"/> class.
        /// </summary>
        /// <param name="compLevel">The comp level.</param>
        public DeflateZLib(int compLevel) : base(compLevel) { }

        /// <summary>
        /// Compress one file
        /// </summary>
        /// <param name="readFileName">Name of the read file.</param>
        /// <param name="writeFileName">Name of the write file.</param>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">No support for files over 4GB</exception>
        public void Compress(string readFileName, string writeFileName)
        {
            try
            {
                // save read file name
                _readFileName = readFileName;

                // open source file for reading
                _readStream = new FileStream(readFileName, FileMode.Open, FileAccess.Read, FileShare.Read);

                // convert stream to binary reader
                _readFile = new BinaryReader(_readStream, Encoding.UTF8);

                // file is too long
                if (_readStream.Length > 0xffffffff) throw new Exception("No support for files over 4GB");

                // uncompressed file length
                _readRemain = (uint) _readStream.Length;

                // reset adler32 checksum
                _readAdler32 = 1;

                // save name
                _writeFileName = writeFileName;

                // create destination file
                _writeStream = new FileStream(writeFileName, FileMode.Create, FileAccess.Write, FileShare.None);

                // convert stream to binary writer
                _writeFile = new BinaryWriter(_writeStream, Encoding.UTF8);

                // Header is made out of 16 bits [iiiicccclldxxxxx]
                // iiii is compression information. It is WindowBit - 8 in this case 7. iiii = 0111
                // cccc is compression method. Deflate (8 dec) or Store (0 dec)
                // The first byte is 0x78 for deflate and 0x70 for store
                // ll is compression level 0 to 3 (CompLevelTable translates between user level of 0-9 to header level of 0-3)
                // d is preset dictionary. The preset dictionary is not supported by this program. d is always 0
                // xxx is 5 bit check sum
                int header = (0x78 << 8) | (CompLevelTable[CompressionLevel] << 6);
                header += 31 - (header%31);

                // write two bytes in most significant byte first
                _writeFile.Write((byte) (header >> 8));
                _writeFile.Write((byte) header);

                // compress the file
                Compress();

                // compress function was stored
                if (CompFunction == CompFunc.Stored)
                {
                    // set file position to header
                    _writeStream.Position = 0;

                    // change compression method from Deflate(8) to Stored(0)
                    header = (0x70 << 8) | (CompLevelTable[CompressionLevel] << 6);
                    header += 31 - (header%31);

                    // write two bytes in most significant byte first
                    _writeFile.Write((byte) (header >> 8));
                    _writeFile.Write((byte) header);

                    // restore file position
                    _writeStream.Position = _writeStream.Length;
                }

                // ZLib checksum is Adler32 write it big endian order, high byte first
                _writeFile.Write((byte) (_readAdler32 >> 24));
                _writeFile.Write((byte) (_readAdler32 >> 16));
                _writeFile.Write((byte) (_readAdler32 >> 8));
                _writeFile.Write((byte) _readAdler32);
            }
            finally
            {
                Dispose();
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
            _readFile.Read(buffer, pos, len);
            _readAdler32 = Adler32.Checksum(_readAdler32, buffer, pos, len);

            return (len);
        }

        /// <summary>
        ///  Write Bytes Routine
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        protected override void WriteBytes(byte[] buffer, int pos, int len)
        {
            _writeFile.Write(buffer, pos, len);
        }

        /// <summary>
        /// Rewind Streams
        /// </summary>
        protected override void RewindStreams()
        {
            // reposition stream file pointer to start of file
            _readStream.Position = 0;

            // uncompressed file length
            _readRemain = (uint)_readStream.Length;

            // reset adler32 checksum
            _readAdler32 = 1;

            // truncate file keeping the zlib header
            _writeStream.SetLength(2);

            // reposition write stream to the new end of file
            _writeStream.Position = 2;
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