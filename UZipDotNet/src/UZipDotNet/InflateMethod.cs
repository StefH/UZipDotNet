/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateMethod.cs
//	Class designed to decompress one file using the Inflate method
//	of compression.
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

namespace UZipDotNet
{
    public abstract class InflateMethod : IDisposable
    {
        private enum BlockType
        {
            StoredBlock,
            StaticTrees,
            DynamicTrees
        }

        // Window size is the maximum distance the repeated string matching process will look for a match
        private const int WindowSize = 32768;			// 0x8000

        // maximum number of codes for the three Huffman trees
        private const int MaxLiteralCodes = 286;
        private const int MaxDistanceCodes = 30;
        private const int MaxBitLengthCodes = 19;

        // bit length repeast symbols
        private const int RepeatSymbol_3_6 = 16;
        private const int RepeatSymbol_3_10 = 17;
        private const int RepeatSymbol_11_138 = 18;

        // Bit length symbols are transmitted in a coded way.
        // This array translate real codes to transmitted codes.
        // It is done to with the hope that most likely codes are at the begining
        // and the least likely will be at the end and if not used will not be transmitted.
        private static readonly int[] BitLengthOrder;

        // Base lengths for literal codes 257..285
        private static readonly int[] BaseLength;

        // Extra bits for literal codes 257..285
        private static readonly int[] ExtraLengthBits;

        // Base offsets for distance codes 0..29
        private static readonly int[] BaseDistance;

        // Extra bits for distance codes
        private static readonly int[] ExtraDistanceBits;

        private readonly byte[] _readBuffer;        // compressed data input buffer
        private int _readPtr;				        // current pointer to compressed data item
        private int _readBufEnd;				    // end of compressed data in the buffer
        private bool _readEndOfFile;			    // end of file flag. If true, the current read buffer is the last buffer
        private uint _bitBuffer;				    // 32 bit buffer for reading for reading variable length bits codes
        private int _bitCount;				        // bit buffer active bit count
        private const int ReadBufSize = 0x100000;	// read buffer length 1MB

        private readonly byte[] _writeBuffer;       // decompressed data buffer
        private int _writePtr;                      // current pointer to end of the write data in the buffer
        private const int WriteBufSize = 0x100000; // write buffer length 1MB

        // allocate arrays
        private readonly byte[] _bitLengthArray;
        private readonly byte[] _literalDistanceArray;

        private readonly InflateTree _bitLengthTree;
        private readonly InflateTree _literalTree;
        private readonly InflateTree _distanceTree;

        /// <summary>
        /// Initializes the <see cref="InflateMethod"/> class.
        /// Inflate static constructor
        /// We use the static constructor to build all static read only arrays
        /// </summary>
        static InflateMethod()
        {
            // Bit length symbols are transmitted in a coded way.
            // This array translate real codes to transmitted codes.
            // It is done to with the hope that most likely codes are at the begining
            // and the least likely will be at the end and if not used will not be transmitted.
            BitLengthOrder = new[]
            {
                RepeatSymbol_3_6, RepeatSymbol_3_10, RepeatSymbol_11_138, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
            };

            // Length Code (See RFC 1951 3.2.5)
            //		 Extra               Extra               Extra
            //	Code Bits Length(s) Code Bits Lengths   Code Bits Length(s)
            //	---- ---- ------     ---- ---- -------   ---- ---- -------
            //	 257   0     3       267   1   15,16     277   4   67-82
            //	 258   0     4       268   1   17,18     278   4   83-98
            //	 259   0     5       269   2   19-22     279   4   99-114
            //	 260   0     6       270   2   23-26     280   4  115-130
            //	 261   0     7       271   2   27-30     281   5  131-162
            //	 262   0     8       272   2   31-34     282   5  163-194
            //	 263   0     9       273   3   35-42     283   5  195-226
            //	 264   0    10       274   3   43-50     284   5  227-257
            //	 265   1  11,12      275   3   51-58     285   0    258
            //	 266   1  13,14      276   3   59-66
            //
            // Base lengths for literal codes 257..285
            BaseLength = new[]
            {
                3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
                35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
            };

            // Extra bits for literal codes 257..285
            ExtraLengthBits = new[]
            {
                0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
                3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
            };

            // Distance Codes (See RFC 1951 3.2.5)
            //		 Extra           Extra               Extra
            //	Code Bits Dist  Code Bits   Dist     Code Bits Distance
            //	---- ---- ----  ---- ----  ------    ---- ---- --------
            //	  0   0    1     10   4     33-48    20    9   1025-1536
            //	  1   0    2     11   4     49-64    21    9   1537-2048
            //	  2   0    3     12   5     65-96    22   10   2049-3072
            //	  3   0    4     13   5     97-128   23   10   3073-4096
            //	  4   1   5,6    14   6    129-192   24   11   4097-6144
            //	  5   1   7,8    15   6    193-256   25   11   6145-8192
            //	  6   2   9-12   16   7    257-384   26   12  8193-12288
            //	  7   2  13-16   17   7    385-512   27   12 12289-16384
            //	  8   3  17-24   18   8    513-768   28   13 16385-24576
            //	  9   3  25-32   19   8   769-1024   29   13 24577-32768
            //
            // Base offsets for distance codes 0..29
            BaseDistance = new[]
            {
                1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385,
                513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577
            };

            // Extra bits for distance codes
            ExtraDistanceBits = new[]
            {
                0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
                7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InflateMethod"/> class.
        /// </summary>
        protected InflateMethod()
        {
            // allocate buffers
            _readBuffer = new byte[ReadBufSize];
            _writeBuffer = new byte[WriteBufSize];
            _bitLengthArray = new byte[MaxBitLengthCodes];
            _literalDistanceArray = new byte[MaxLiteralCodes + MaxDistanceCodes];
            _bitLengthTree = new InflateTree(TreeType.BitLen);
            _literalTree = new InflateTree(TreeType.Literal);
            _distanceTree = new InflateTree(TreeType.Distance);
        }

        /// <summary>
        /// Decompress the input file
        /// </summary>
        /// <exception cref="UZipDotNet.Exception">Unknown block type</exception>
        public void Decompress()
        {
            // reset read process
            _readPtr = 0;
            _bitBuffer = 0;
            _bitCount = 0;

            // read first block
            _readBufEnd = ReadBytes(_readBuffer, 0, ReadBufSize, out _readEndOfFile);

            // reset write process
            _writePtr = 0;

            // reset last block flag
            bool lastBlock = false;

            // loop for blocks
            while (!lastBlock)
            {
                // get next block header
                int blockHeader = GetBits(3);

                // set last block flag				
                if ((blockHeader & 1) != 0) lastBlock = true;

                // switch based on type of block
                switch ((BlockType)(blockHeader >> 1))
                {
                    // copy uncompressed block from read buffer to wrire buffer
                    case BlockType.StoredBlock:
                        CopyStoredBlock();
                        break;

                    // decode compressed block using static trees
                    case BlockType.StaticTrees:
                        _literalTree.SetStatic();
                        _distanceTree.SetStatic();
                        DecodeCompressedBlock(true);
                        break;

                    // decode compressed block using dynamic trees
                    case BlockType.DynamicTrees:
                        DecodeDynamicHuffamTrees();
                        _literalTree.SetDynamic();
                        _distanceTree.SetDynamic();
                        DecodeCompressedBlock(false);
                        break;

                    default:
                        throw new Exception("Unknown block type");
                }
            }

            // flush write buffer
            WriteToFile(true);
        }

        /// <summary>
        /// Copy stored file
        /// this routine is called for zip file archived
        /// when the compression method is no compression (code of zero)
        /// </summary>
        public void NoCompression()
        {
            // loop writing directly from read buffer
            _readEndOfFile = false;
            while (!_readEndOfFile)
            {
                // read one block
                _readBufEnd = ReadBytes(_readBuffer, 0, ReadBufSize, out _readEndOfFile);

                // write read buffer content
                WriteBytes(_readBuffer, 0, _readBufEnd);
            }
        }

        /// <summary>
        /// Copy uncompresses block from input stream to output stream
        /// </summary>
        /// <exception cref="UZipDotNet.Exception">Stored block length in error</exception>
        private void CopyStoredBlock()
        {
            // move read buffer pointer to next multiple of 8 bits
            // drop bits that are not multiple of 8
            _bitBuffer >>= _bitCount & 7;

            // effectively subtract the dropped bits from the count
            _bitCount &= ~7;

            // get block length
            // note: the Get16Bits routine will empty the read bit buffer
            // after reading 2 blocks of 16 bits the bit buffer is guarantied to be empty
            int blockLength = Get16Bits();

            // get the inverted block length and compare it to the block length
            if (Get16Bits() != (blockLength ^ 0xffff)) throw new Exception("Stored block length in error");

            // make sure read buffer has enough bytes for full block transfer
            // not enough bytes available
            if (_readPtr + blockLength > _readBufEnd) TestReadBuffer(blockLength);

            // make sure write buffer has enough space to receive the block in one transfer
            if (_writePtr + blockLength > _writeBuffer.Length) WriteToFile(false);

            // write to output buffer
            Array.Copy(_readBuffer, _readPtr, _writeBuffer, _writePtr, blockLength);

            // update pointers and total
            _writePtr += blockLength;
            _readPtr += blockLength;
        }

        /// <summary>
        /// Get next 16 bits
        /// Assume that bit buffer is aligned on 8 bit boundary.
        /// Empty the bit buffer first. After two calls to this routine the bit buffer is guarantied to be empty.
        /// Throw exception if end of input is reached
        /// </summary>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">
        /// Unexpected end of file (Get16Bits)
        /// or
        /// Unexpected end of file (Get16Bits)
        /// </exception>
        private int Get16Bits()
        {
            int token;

            // bit buffer has 16 or 24 bits
            if (_bitCount >= 16)
            {
                token = (int)_bitBuffer & 0xffff;
                _bitBuffer >>= 16;
                _bitCount -= 16;
                return (token);
            }

            // bit buffer has 8 bits
            if (_bitCount >= 8)
            {
                // get high bits from bit buffer
                token = (int)_bitBuffer;
                _bitBuffer = 0;
                _bitCount = 0;
            }
            else
            {
                // get high bits from read buffer
                token = ReadByte();

                // we are at the end of file case
                if (token < 0) throw new Exception("Unexpected end of file (Get16Bits)");
            }

            // get low bits from read buffer
            int nextByte = ReadByte();

            // we are at the end of file case
            if (nextByte < 0) throw new Exception("Unexpected end of file (Get16Bits)");

            // return 16 bits
            return ((nextByte << 8) | token);
        }

        /// <summary>
        /// Test read buffer for at least Len bytes availability
        /// </summary>
        /// <param name="len">The length.</param>
        /// <exception cref="UZipDotNet.Exception">
        /// Premature end of file reading zip header
        /// or
        /// Premature end of file reading zip header
        /// </exception>
        // ReSharper disable once UnusedParameter.Local
        private void TestReadBuffer(int len)
        {
            // end of file flag is on
            if (_readEndOfFile) throw new Exception("Premature end of file reading zip header");

            // move the top part of the file to the start of the buffer (round to 8 bytes)
            int startOfMovePtr = _readPtr & ~7;
            int moveSize = _readBufEnd - startOfMovePtr;
            Array.Copy(_readBuffer, startOfMovePtr, _readBuffer, 0, moveSize);

            // adjust read pointer
            _readPtr &= 7;

            // read one block
            _readBufEnd = moveSize + ReadBytes(_readBuffer, moveSize, ReadBufSize - moveSize, out _readEndOfFile);

            // test again for sufficient look ahead buffer
            if (_readPtr + len > _readBufEnd) throw new Exception("Premature end of file reading zip header");
        }

        /// <summary>
        /// Decode the input stream and produce the output stream
        /// </summary>
        /// <param name="static">if set to <c>true</c> [static].</param>
        private void DecodeCompressedBlock(bool @static)
        {
            // loop for all symbols of one block
            for (; ; )
            {
                // Loop while symbols are less than 256.
                // In other words input literals go unchanged to output stream
                int symbol;
                while ((symbol = ReadSymbol(_literalTree)) < 256)
                {
                    // test for write buffer full
                    if (_writePtr == WriteBufSize) WriteToFile(false);

                    // write to output buffer
                    _writeBuffer[_writePtr++] = (byte)symbol;
                }

                // end of block
                if (symbol == 256) return;

                // translate symbol into copy length
                symbol -= 257;
                int strLength = BaseLength[symbol];
                int reqBits = ExtraLengthBits[symbol];
                if (reqBits > 0) strLength += GetBits(reqBits);

                // get next symbol
                symbol = ReadSymbol(_distanceTree);

                // translate into copy distance
                int strDist = BaseDistance[symbol];
                reqBits = ExtraDistanceBits[symbol];
                if (reqBits > 0) strDist += GetBits(reqBits);

                // test for write buffer full
                if (_writePtr + strLength > WriteBufSize) WriteToFile(false);

                // test for overlap
                int len = strLength > strDist ? strDist : strLength;

                // write to output buffer
                Array.Copy(_writeBuffer, _writePtr - strDist, _writeBuffer, _writePtr, len);

                // update pointer and length
                _writePtr += len;

                // special case of repeating strings
                if (strLength > strDist)
                {
                    // copy one byte at a time
                    int writeEnd = _writePtr + strLength - strDist;
                    for (; _writePtr < writeEnd; _writePtr++) _writeBuffer[_writePtr] = _writeBuffer[_writePtr - strDist];
                }
            }
        }

        /// <summary>
        /// Writes to file.
        /// </summary>
        /// <param name="flush">if set to <c>true</c> [flush].</param>
        private void WriteToFile(bool flush)
        {
            // write buffer keeping one window size (make sure it is multiple of 8)
            int len = flush ? _writePtr : (_writePtr - WindowSize) & ~(7);

            // write to file
            WriteBytes(_writeBuffer, 0, len);

            // move leftover to start of buffer (except for flush)
            _writePtr -= len;
            Array.Copy(_writeBuffer, len, _writeBuffer, 0, _writePtr);
        }

        /// <summary>
        /// Decode dynamic Huffman header
        /// </summary>
        private void DecodeDynamicHuffamTrees()
        {
            // length of length/literal tree		
            int literalLength = GetBits(5) + 257;

            // length of distance tree
            int distanceLength = GetBits(5) + 1;

            // length of bit length tree
            int bitLengthReceived = GetBits(4) + 4;

            // get bit length info from input stream
            // note: array length must be 19 and not length received
            Array.Clear(_bitLengthArray, 0, _bitLengthArray.Length);
            for (int index = 0; index < bitLengthReceived; index++)
                _bitLengthArray[BitLengthOrder[index]] = (byte)GetBits(3);

            // create bit length hauffman tree
            _bitLengthTree.BuildTree(_bitLengthArray, 0, _bitLengthArray.Length);

            // create a combined array of length/literal and distance
            int totalLength = literalLength + distanceLength;
            Array.Clear(_literalDistanceArray, 0, _literalDistanceArray.Length);
            byte lastCode = 0;
            for (int ptr = 0; ptr < totalLength; )
            {
                // get next symbol from input stream
                int symbol = ReadSymbol(_bitLengthTree);

                // switch based on symbol
                switch (symbol)
                {
                    // symbol is less than 16 it is a literal
                    default:
                        _literalDistanceArray[ptr++] = lastCode = (byte)symbol;
                        continue;

                    case RepeatSymbol_3_6:
                        for (int count = GetBits(2) + 3; count > 0; count--) _literalDistanceArray[ptr++] = lastCode;
                        continue;

                    case RepeatSymbol_3_10:
                        for (int count = GetBits(3) + 3; count > 0; count--) _literalDistanceArray[ptr++] = 0;
                        continue;

                    case RepeatSymbol_11_138:
                        for (int count = GetBits(7) + 11; count > 0; count--) _literalDistanceArray[ptr++] = 0;
                        continue;
                }
            }

            // create the literal array and distance array
            _literalTree.SetDynamic();
            _literalTree.BuildTree(_literalDistanceArray, 0, literalLength);
            _distanceTree.SetDynamic();
            _distanceTree.BuildTree(_literalDistanceArray, literalLength, distanceLength);
        }

        /// <summary>
        /// Read Next Byte From Read Buffer
        /// </summary>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">Premature end of file reading zip header</exception>
        private int ReadByte()
        {
            // test for end of read buffer
            if (_readPtr == _readBufEnd)
            {
                // end of file flag was set during last read operation
                if (_readEndOfFile) return (-1);

                // read one block
                _readBufEnd = ReadBytes(_readBuffer, 0, ReadBufSize, out _readEndOfFile);

                // test again for sufficient look ahead buffer
                if (_readBufEnd == 0) throw new Exception("Premature end of file reading zip header");

                // reset read pointer
                _readPtr = 0;
            }

            // get next byte
            return (_readBuffer[_readPtr++]);
        }

        /// <summary>
        /// Get next n bits
        /// Throw exception if end of input is reached
        /// </summary>
        /// <param name="bits">The bits.</param>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">Peek Bits: Premature end of file</exception>
        private int GetBits(int bits)
        {
            // fill the buffer to a maximum of 32 bits
            for (; _bitCount <= 24; _bitCount += 8)
            {
                int oneByte = ReadByte();
                if (oneByte < 0) break;
                _bitBuffer |= (uint)(oneByte << _bitCount);
            }

            // error: the program should not ask for bits beyond end of file
            if (bits > _bitCount) throw new Exception("Peek Bits: Premature end of file");

            int token = (int)_bitBuffer & ((1 << bits) - 1);
            _bitBuffer >>= bits;
            _bitCount -= bits;

            return token;
        }

        /// <summary>
        /// Reads the next symbol from input.  The symbol is encoded using the huffman tree.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">
        /// Error decoding the compressed bit stream (decoding tree error)
        /// or
        /// Error decoding the compressed bit stream (premature end of file)
        /// </exception>
        private int ReadSymbol(InflateTree tree)
        {
            // fill the buffer to a maximum of 32 bits
            for (; _bitCount <= 24; _bitCount += 8)
            {
                // read next byte from read buffer
                int oneByte = ReadByte();

                // end of file
                if (oneByte < 0) break;

                // append to the bit buffer
                _bitBuffer |= (uint)(oneByte << _bitCount);
            }

            // loop through the decode tree
            int next, mask;
            for (mask = tree.ActiveBitMask, next = tree.ActiveTree[(int) _bitBuffer & (mask - 1)];
                next > 0 && mask < 0x10000;
                next = (_bitBuffer & mask) == 0 ? tree.ActiveTree[next] : tree.ActiveTree[next + 1], mask <<= 1)
            {
            }

            // error
            if (next >= 0) throw new Exception("Error decoding the compressed bit stream (decoding tree error)");

            // invert the symbol plus bit count
            next = ~next;

            // extract the number of bits
            int bits = next & 15;

            // remove the bits from the bit buffer
            _bitBuffer >>= bits;
            _bitCount -= bits;

            // error
            if (_bitCount < 0) throw new Exception("Error decoding the compressed bit stream (premature end of file)");

            // exit with symbol
            return (next >> 4);
        }

        /// <summary>
        /// Read bytes from input stream
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        /// <param name="endOfFile">if set to <c>true</c> [end of file].</param>
        /// <returns></returns>
        protected abstract int ReadBytes(byte[] buffer, int pos, int len, out bool endOfFile);

        /// <summary>
        /// Write bytes to output stream
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        protected abstract void WriteBytes(byte[] buffer, int pos, int len);

        public abstract void Dispose();
    }
}