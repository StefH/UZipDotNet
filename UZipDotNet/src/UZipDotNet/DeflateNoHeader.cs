/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	DeflateNoHeader.cs
//	Class designed to compress a file to another file without
//	adding any header or trailer information. This class is
//	derived from DeflateMethod class. Not used in this project.
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
    public class DeflateNoHeader : DeflateMethod
    {
        public string[] ExceptionStack;

        private string _readFileName;
        private FileStream _readStream;
        private BinaryReader _readFile;
        private UInt32 _readRemain;

        private string _writeFileName;
        private FileStream _writeStream;
        private BinaryWriter _writeFile;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateNoHeader"/> class.
        /// </summary>
        public DeflateNoHeader() : base(DefaultCompression) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateNoHeader"/> class.
        /// </summary>
        /// <param name="compLevel">The comp level.</param>
        public DeflateNoHeader(int compLevel) : base(compLevel) { }

        /// <summary>
        /// Compress one file
        /// </summary>
        /// <param name="readFileName">Name of the read file.</param>
        /// <param name="writeFileName">Name of the write file.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">No support for files over 4GB</exception>
        public void Compress(string readFileName, string writeFileName)
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
                if (_readStream.Length > 0xffffffff) throw new Exception("No support for files over 4GB"); // TODO throw IOException ?

                // uncompressed file length
                _readRemain = (UInt32)_readStream.Length;

                // save name
                _writeFileName = writeFileName;

                // create destination file
                _writeStream = new FileStream(writeFileName, FileMode.Create, FileAccess.Write, FileShare.None);

                // convert stream to binary writer
                _writeFile = new BinaryWriter(_writeStream, Encoding.UTF8);

                // compress the file (function defind in DeflateMethod the base class)
                Compress();

                // close read file
                _readFile.Dispose();
                _readFile = null;

                // close write file
                _writeFile.Dispose();
                _writeFile = null;
            }
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
            _writeFile.Write(buffer, pos, len);
        }

        /// <summary>
        /// Rewind input and output stream
        /// </summary>
        protected override void RewindStreams()
        {
            // reposition stream file pointer to start of file
            _readStream.Position = 0;

            // uncompressed file length
            _readRemain = (uint)_readStream.Length;

            // truncate file
            _writeStream.SetLength(0);

            // reposition write stream to the new end of file
            _writeStream.Position = 0;
        }

        public override void Dispose()
        {
        }
    }
}