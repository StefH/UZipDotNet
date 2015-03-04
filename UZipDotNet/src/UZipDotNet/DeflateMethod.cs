/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	DeflateMethod.cs
//	Class designed to compress one file using the Deflate method
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
using UZipDotNet.Support;

namespace UZipDotNet
{
    public class ReadBytesResult
    {
        /// <summary>
        /// Defines if end of file has been reached
        /// </summary>
        public bool EndOfFile;

        /// <summary>
        /// The number of bytes read
        /// </summary>
        public int BytesRead;
    }

    public abstract class DeflateMethod : IDisposable
    {
        // Compression functions
        public enum CompFunc
        {
            Stored,		// no compression
            Fast,		// Deflate fast compression
            Slow		// Deflate slow but better compression (default)
        }

        // User compression level choices. The range is 0 to 9.
        // From no compression level=0, to best compression level 9.
        // No compression is the fastest, best compression is the slowest.
        // The default compression level is a optimum selection between speed and compression ratio.
        // No compression level=0 will result in CompFunc.Stored
        // Compression level=1 to 3 will result in CompFunc.Fast
        // Compression level=4 to 9 will result in CompFunc.Slow
        protected const int NoCompression = 0;
        protected const int BestCompression = 9;
        protected const int DefaultCompression = 6;
        private int _compLevel = DefaultCompression;

        // These 5 arrays define match process constants one for each of the 10 levels.
        // The constants control the amount of time the program will spend trying to find a better match
        private static readonly int[] GoodLengthTable;
        private static readonly int[] MaxLazyTable;
        private static readonly int[] NiceLengthTable;
        private static readonly int[] MaxChainTable;
        private static readonly CompFunc[] CompFuncTable;
        private int _goodLength;
        private int _maxLazyLength;
        private int _niceLength;
        private int _maxChainLength;
        public CompFunc CompFunction;

        // repeated string length range is from minimum 3 to maximum 258
        private const int MinMatch = 3;
        private const int MaxMatch = 258;

        // read buffer minimum look ahead (LookAhead = BufferEnd - CurrentPointer)
        private const int MinLookahead = MaxMatch + MinMatch + 1;

        // Window size is the maximum distance the repeated string matching process will look for a match
        private const int WindowSize = 32768;			// 0x8000

        // minimum match (3) should not be further than that
        private const int MatchIsTooFar = 4096;

        // Hash table is used to speed up the matching process.
        // The program calculate a hash value at the current pointer.
        // It converts 3 bytes (MinMatch) to a 16 Bits value.
        // The hash value is used as an index to the hash table to obtain the first possible match
        private const int WindowMask = WindowSize - 1; // 0x7FFF
        private const int HashTableSize = 65536; // 0x10000
        private readonly int[] _hashTable; // Hash table
        private readonly int[] _hashPrevious; // Linked chains of equal Hash Values
        private static readonly ushort[] HashXlate; // Translation table for the 3rd byte of hash value

        // Compression block type
        // Compressed file is divided into these type of blocks
        // At the end of each block the program will select the shortest block
        private enum BlockType
        {
            // no compression block
            StoredBlock,

            // compress with static trees
            StaticTrees,

            // compress with dynamic trees
            DynamicTrees
        }

        private const int BlockBufSize = 16384;		// Block buffer size 0x4000
        private const int EndOfBlockSymbol = 256;   // End of block marker 0x100

        // Block buffer. Each element is either (Literal) or (Distance << 8) | (Length - 3)
        private readonly int[] _blockBuf;

        // Current end of block buffer
        private int _blockBufEnd;

        // Number of extra bits associated with Distance or Length
        private int _blockBufExtraBits;

        // The literal tree is a combination of literals, lengths and end of block marker
        private readonly DeflateTree _literalTree;

        // Minimum literal codes
        private const int MinLiteralCodes = 257;

        // Maximum literal codes
        private const int MaxLiteralCodes = 286;

        // Maximum number of bits in literal codes
        private const int MaxLiteralBitLen = 15;

        // translation between Length-3 and LengthCode
        private static readonly ushort[] LengthCode;
        private static readonly ushort[] StaticLiteralCodes;
        private static readonly byte[] StaticLiteralLength;

        // The distance tree. The distance to previous occurance of matched string
        private readonly DeflateTree _distanceTree;

        // Minimum distance codes
        private const int MinDistanceCodes = 2;

        // Maximum distance codes
        private const int MaxDistanceCodes = 30;

        // Maximum number of bits in distance codes
        private const int MaxDistanceBitLen = 15;

        // Translation between Distance-1 and DistanceCode
        private static readonly byte[] DistanceCode;
        private static readonly ushort[] StaticDistanceCodes;
        private static readonly byte[] StaticDistanceLength;

        // Bit Length tree is the tree encoding the other two trees in a dynamic block
        private readonly DeflateTree _bitLengthTree;

        // Minimum bit length codes
        private const int MinBitLengthCodes = 4;

        // Maximum bit length codes
        private const int MaxBitLengthCodes = 19;

        // Maximum number of bites in bit length codes
        private const int MaxBitLengthBitLen = 7;

        public uint ReadTotal;					    // Total number of bytes read from input stream
        private readonly byte[] _readBuffer;		// Input stream buffer
        private int _readBufferFilePosition;		// Start of ReadBuffer in relation to the input file
        private int _readBlockStart;				// Start of current compression block
        private int _readPtr;					    // Current read pointer
        private int _readBufEnd;					// Read buffer logical end
        private int _readAvailableBytes;			// Available bytes for compression matching (=ReadBufEnd - ReadPtr)
        private bool _readEndOfFile;				// The current read buffer is the last buffer of the file
        private int _matchStart;					// Pointer to matched string in prior text
        private int _matchLen;					    // Length of matched string
        private const int ReadBufSize = 0x100000;   // Read buffer size

        public uint WriteTotal;					    // Total number of bytes written to output stream
        private readonly byte[] _writeBuffer;		// Output stream buffer
        private int _writePtr;					    // Current pointer to output stream buffer
        private uint _bitBuffer;					    // 32 bit buffer
        private int _bitCount;					    // number of active bits in bit buffer
        private bool _writeFirstBlock;			    // First compressed block is not out yet
        private const int WriteBufSize = 0x100000;	// Write buffer size

        /// <summary>
        /// Initializes the <see cref="DeflateMethod"/> class.
        /// We use the static constructor to build all static read only arrays
        /// </summary>
        static DeflateMethod()
        {
            // compression level translation tables
            // Level 0 Stored, Level 1-3 Fast, Level 4-9 Slow
            GoodLengthTable = new[] { 0, 4, 4, 4, 4, 8, 8, 8, 32, 32 };
            MaxLazyTable = new[] { 0, 4, 5, 6, 4, 16, 16, 32, 128, 258 };
            NiceLengthTable = new[] { 0, 8, 16, 32, 16, 32, 128, 128, 258, 258 };
            MaxChainTable = new[] { 0, 4, 8, 32, 16, 32, 128, 256, 1024, 4096 };
            CompFuncTable = new[] { CompFunc.Stored, CompFunc.Fast, CompFunc.Fast, CompFunc.Fast, CompFunc.Slow, CompFunc.Slow, CompFunc.Slow, CompFunc.Slow, CompFunc.Slow, CompFunc.Slow };

            // translation table for the third byte of Hash calculations
            // Bit 0 to 15, Bit 2 to 14, Bit 4 to 13, Bit 6 to 12
            // Bit 1 to 7,  Bit 3 to 6,  Bit 5 to 5,  Bit 7 to 4
            HashXlate = new ushort[]
            {
                0x0000, 0x8000, 0x0080, 0x8080, 0x4000, 0xC000, 0x4080, 0xC080,
                0x0040, 0x8040, 0x00C0, 0x80C0, 0x4040, 0xC040, 0x40C0, 0xC0C0,
                0x2000, 0xA000, 0x2080, 0xA080, 0x6000, 0xE000, 0x6080, 0xE080,
                0x2040, 0xA040, 0x20C0, 0xA0C0, 0x6040, 0xE040, 0x60C0, 0xE0C0,
                0x0020, 0x8020, 0x00A0, 0x80A0, 0x4020, 0xC020, 0x40A0, 0xC0A0,
                0x0060, 0x8060, 0x00E0, 0x80E0, 0x4060, 0xC060, 0x40E0, 0xC0E0,
                0x2020, 0xA020, 0x20A0, 0xA0A0, 0x6020, 0xE020, 0x60A0, 0xE0A0,
                0x2060, 0xA060, 0x20E0, 0xA0E0, 0x6060, 0xE060, 0x60E0, 0xE0E0,
                0x1000, 0x9000, 0x1080, 0x9080, 0x5000, 0xD000, 0x5080, 0xD080,
                0x1040, 0x9040, 0x10C0, 0x90C0, 0x5040, 0xD040, 0x50C0, 0xD0C0,
                0x3000, 0xB000, 0x3080, 0xB080, 0x7000, 0xF000, 0x7080, 0xF080,
                0x3040, 0xB040, 0x30C0, 0xB0C0, 0x7040, 0xF040, 0x70C0, 0xF0C0,
                0x1020, 0x9020, 0x10A0, 0x90A0, 0x5020, 0xD020, 0x50A0, 0xD0A0,
                0x1060, 0x9060, 0x10E0, 0x90E0, 0x5060, 0xD060, 0x50E0, 0xD0E0,
                0x3020, 0xB020, 0x30A0, 0xB0A0, 0x7020, 0xF020, 0x70A0, 0xF0A0,
                0x3060, 0xB060, 0x30E0, 0xB0E0, 0x7060, 0xF060, 0x70E0, 0xF0E0,
                0x0010, 0x8010, 0x0090, 0x8090, 0x4010, 0xC010, 0x4090, 0xC090,
                0x0050, 0x8050, 0x00D0, 0x80D0, 0x4050, 0xC050, 0x40D0, 0xC0D0,
                0x2010, 0xA010, 0x2090, 0xA090, 0x6010, 0xE010, 0x6090, 0xE090,
                0x2050, 0xA050, 0x20D0, 0xA0D0, 0x6050, 0xE050, 0x60D0, 0xE0D0,
                0x0030, 0x8030, 0x00B0, 0x80B0, 0x4030, 0xC030, 0x40B0, 0xC0B0,
                0x0070, 0x8070, 0x00F0, 0x80F0, 0x4070, 0xC070, 0x40F0, 0xC0F0,
                0x2030, 0xA030, 0x20B0, 0xA0B0, 0x6030, 0xE030, 0x60B0, 0xE0B0,
                0x2070, 0xA070, 0x20F0, 0xA0F0, 0x6070, 0xE070, 0x60F0, 0xE0F0,
                0x1010, 0x9010, 0x1090, 0x9090, 0x5010, 0xD010, 0x5090, 0xD090,
                0x1050, 0x9050, 0x10D0, 0x90D0, 0x5050, 0xD050, 0x50D0, 0xD0D0,
                0x3010, 0xB010, 0x3090, 0xB090, 0x7010, 0xF010, 0x7090, 0xF090,
                0x3050, 0xB050, 0x30D0, 0xB0D0, 0x7050, 0xF050, 0x70D0, 0xF0D0,
                0x1030, 0x9030, 0x10B0, 0x90B0, 0x5030, 0xD030, 0x50B0, 0xD0B0,
                0x1070, 0x9070, 0x10F0, 0x90F0, 0x5070, 0xD070, 0x50F0, 0xD0F0,
                0x3030, 0xB030, 0x30B0, 0xB0B0, 0x7030, 0xF030, 0x70B0, 0xF0B0,
                0x3070, 0xB070, 0x30F0, 0xB0F0, 0x7070, 0xF070, 0x70F0, 0xF0F0
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
            // build translation table between length and the code representing the length
            // length range is 3 to 258 (index 0 represent length 3)
            // codes will be in the range 257 to 285
            LengthCode = new ushort[256];
            int @base = 257;
            int divider = 1;
            int next = 8;
            for (uint len = 0; len < 255; len++)
            {
                if (len == next)
                {
                    @base += 4;
                    divider <<= 1;
                    next <<= 1;
                }
                LengthCode[len] = (ushort)(@base + len / divider);
            }
            LengthCode[255] = 285;

            // Distance Codes (See RFC 1951 3.2.5)
            //		 Extra           Extra                Extra
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
            // build translation table between distance and distance code
            // distance range is 1 to 32768 (index 0 represent distance 1)
            // distance codes will be in the range of 0 to 29
            DistanceCode = new Byte[WindowSize];
            @base = 0;
            divider = 1;
            next = 4;
            for (uint dist = 0; dist < WindowSize; dist++)
            {
                if (dist == next)
                {
                    @base += 2;
                    divider <<= 1;
                    next <<= 1;
                }
                DistanceCode[dist] = (Byte)(@base + dist / divider);
            }

            // static literal codes and length codes  (See RFC 1951 3.2.6)
            //	Lit Value    Bits        Codes
            //	---------    ----        -----
            //	  0 - 143     8          00110000 through 10111111
            //	144 - 255     9          110010000 through 111111111
            //	256 - 279     7          0000000 through 0010111
            //	280 - 287     8          11000000 through 11000111
            //
            StaticLiteralCodes = new ushort[288];
            StaticLiteralLength = new byte[288];
            int code = 0;
            for (int index = 256; index <= 279; index++)
            {
                StaticLiteralCodes[index] = BitReverse.Reverse16Bits(code);
                StaticLiteralLength[index] = 7;
                code += 1 << (16 - 7);
            }
            for (int index = 0; index <= 143; index++)
            {
                StaticLiteralCodes[index] = BitReverse.Reverse16Bits(code);
                StaticLiteralLength[index] = 8;
                code += 1 << (16 - 8);
            }
            for (int index = 280; index <= 287; index++)
            {
                StaticLiteralCodes[index] = BitReverse.Reverse16Bits(code);
                StaticLiteralLength[index] = 8;
                code += 1 << (16 - 8);
            }
            for (int index = 144; index <= 255; index++)
            {
                StaticLiteralCodes[index] = BitReverse.Reverse16Bits(code);
                StaticLiteralLength[index] = 9;
                code += 1 << (16 - 9);
            }

            //	Static distance codes (See RFC 1951 3.2.6)
            //	Distance codes 0-31 are represented by (fixed-length) 5-bit
            //	codes, with possible additional bits as shown in the table
            //	shown in Paragraph 3.2.5, above.  Note that distance codes 30-
            //	31 will never actually occur in the compressed data.
            StaticDistanceCodes = new ushort[MaxDistanceCodes];
            StaticDistanceLength = new byte[MaxDistanceCodes];
            for (int index = 0; index < MaxDistanceCodes; index++)
            {
                StaticDistanceCodes[index] = BitReverse.Reverse16Bits(index << 11);
                StaticDistanceLength[index] = 5;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateMethod"/> class.
        /// The constructor is used to allocate buffers.
        /// </summary>
        /// <param name="compLevel">The comp level.</param>
        protected DeflateMethod(int compLevel)
        {
            // compress level
            _compLevel = compLevel < NoCompression || compLevel > BestCompression ? DefaultCompression : compLevel;

            // create the three trees that contol the compression process
            _literalTree = new DeflateTree(WriteBits, MinLiteralCodes, MaxLiteralCodes, MaxLiteralBitLen);
            _distanceTree = new DeflateTree(WriteBits, MinDistanceCodes, MaxDistanceCodes, MaxDistanceBitLen);
            _bitLengthTree = new DeflateTree(WriteBits, MinBitLengthCodes, MaxBitLengthCodes, MaxBitLengthBitLen);

            // allocate compression block buffer
            _blockBuf = new int[BlockBufSize];

            // allocate read buffer
            _readBuffer = new byte[ReadBufSize];

            // allocate write buffer
            _writeBuffer = new byte[WriteBufSize];

            // hash tables initialization
            _hashTable = new int[HashTableSize];
            _hashPrevious = new int[WindowSize];
        }

        /// <summary>
        /// Gets or sets the compression level.
        /// </summary>
        /// <value>
        /// The compression level (0 to 9).
        /// </value>
        public int CompressionLevel
        {
            set
            {
                // define compression control values based on the user compression level 0 to 9
                _compLevel = value < NoCompression || value > BestCompression ? DefaultCompression : value;
            }
            get
            {
                return (_compLevel);
            }
        }

        /// <summary>
        /// Compress read stream to write stream
        /// This is the main function of the DefaultMethod class
        /// </summary>
        protected void Compress()
        {
            // define compression control values based on the user compression level 0 to 9
            CompFunction = CompFuncTable[_compLevel];
            _goodLength = GoodLengthTable[_compLevel];
            _maxLazyLength = MaxLazyTable[_compLevel];
            _niceLength = NiceLengthTable[_compLevel];
            _maxChainLength = MaxChainTable[_compLevel];

            // read process initialization
            _readBufferFilePosition = 0;
            _readBlockStart = 0;
            _readPtr = 0;
            _readEndOfFile = false;
            _matchStart = 0;
            _matchLen = MinMatch - 1;

            // read first block (the derived class will supply this routine)
            _readBufEnd = ReadBytes(_readBuffer, 0, ReadBufSize, out _readEndOfFile);

            ReadTotal = (uint)_readBufEnd;

            // available bytes in the buffer
            _readAvailableBytes = _readBufEnd;

            // if file is less than 8 bytes, just store it
            if (_readAvailableBytes < 8) CompFunction = CompFunc.Stored;

            // write process initialization
            WriteTotal = 0;
            _writePtr = 0;
            _bitBuffer = 0;
            _bitCount = 0;
            _writeFirstBlock = true;

            // hash tables initialization
            for (int hashPtr = _hashTable.Length - 1; hashPtr >= 0; hashPtr--) _hashTable[hashPtr] = -1;
            for (int hashPtr = _hashPrevious.Length - 1; hashPtr >= 0; hashPtr--) _hashPrevious[hashPtr] = -1;

            // switch based on the type of compression
            switch (CompFunction)
            {
                case CompFunc.Stored:
                    DeflateStored();
                    break;

                case CompFunc.Fast:
                    DeflateFast();
                    break;

                case CompFunc.Slow:
                    DeflateSlow();
                    break;
            }

            // flush any left over bits in bit buffer
            WriteAlignToByte();
            if (_writePtr != 0)
            {
                WriteBytes(_writeBuffer, 0, _writePtr);
                WriteTotal += (uint)_writePtr;
            }
        }

        /// <summary>
        /// No compression
        /// Move blocks of data from read stream to write stream
        /// </summary>
        private void DeflateStored()
        {
            while (_readAvailableBytes > 0)
            {
                // write the content of the read buffer
                WriteBytes(_readBuffer, 0, _readAvailableBytes);
                WriteTotal += (uint)_readAvailableBytes;

                // end of input file
                if (_readEndOfFile) break;

                // read next block
                _readAvailableBytes = ReadBytes(_readBuffer, 0, ReadBufSize, out _readEndOfFile);
                ReadTotal += (uint)_readAvailableBytes;
            }
        }

        /// <summary>
        /// Fast compression
        /// The program will use this compression method if user compression level was 1, 2 or 3
        /// </summary>
        private void DeflateFast()
        {
            // Set block buffer to empty
            BlockReset();

            // This is the main compression loop for compression level 1 to 3.
            // The program will scan the whole file for matching strings.
            // If no match was found, the current literal is saved in block buffer.
            // If match is found, the distance and length are saved in block buffer.
            // When the block buffer is full, the block is compressed into the write buffer.
            for (; ; )
            {
                // End of file. The buffer has 0 to 2 bytes left.
                if (_readAvailableBytes < MinMatch)
                {
                    while (_readAvailableBytes > 0)
                    {
                        // Block buffer is full. Compress the buffer.
                        if (_blockBufEnd == BlockBufSize) CompressBlockBuf(false);

                        // save the last one or two characters
                        SaveLiteralInBlockBuf(_readBuffer[_readPtr]);

                        // update pointer
                        _readPtr++;
                        _readAvailableBytes--;
                    }

                    // Compress the content of the last buffer and signal end of file.
                    CompressBlockBuf(true);
                    return;
                }

                // look for a match to the current string in the previous text.
                _matchLen = MinMatch - 1;
                FindLongestMatch();

                // if Find Longest Match is successful MatchStart and MatchLength will be set
                if (_matchLen >= MinMatch)
                {
                    // add the distance and length to the distance and length arrays
                    SaveDistanceInBlockBuf(_readPtr - _matchStart, _matchLen);

                    // move the current string pointer after the matched string
                    // if the match length is less than the max_lazy, add hash values
                    // otherwise do not include the matched area for future searches
                    if (_matchLen <= _maxLazyLength)
                    {
                        while (--_matchLen > 0)
                        {
                            _readPtr++;
                            HashInsertString();
                        }
                        _readPtr++;
                    }
                    else
                    {
                        _readPtr += _matchLen;
                    }
                }
                else
                {
                    // No match found
                    SaveLiteralInBlockBuf(_readBuffer[_readPtr]);
                    _readPtr++;
                }

                // update available bytes after read pointer move
                _readAvailableBytes = _readBufEnd - _readPtr;

                // end of one block
                if (_blockBufEnd == BlockBufSize) CompressBlockBuf(false);

                // fill buffer if lookahead area is below minimum
                if (_readAvailableBytes < MinLookahead && !_readEndOfFile)
                {
                    // We need to keep block start within the buffer in case the compression
                    // algorithm needs to output an uncompress block.
                    // if read block start is small, the read routine will not be able to add more bytes
                    // in that case we will flush the block even if it is not full
                    // if the read buffer size is more that 1MB all of this will not happen
                    if (_readBlockStart < WindowSize) CompressBlockBuf(false);

                    // fill the buffer
                    ReadFillBuffer();
                }
            }
        }

        /// <summary>
        /// Normal compression
        /// The program will use this compression method if user compression level was 4 to 9
        /// </summary>
        private void DeflateSlow()
        {
            // Set block buffer to empty
            BlockReset();

            // This is the main compression loop for compression level 4 to 9.
            // The program will scan the whole file for matching strings.
            // If no match was found, the current literal is saved in block buffer.
            // If match is found, the program will try the next location for a better match.
            // If the first match is better, the distance and length are saved in block buffer.
            // If the second match is better, one literal is saved.
            // When the block buffer is full, the block is compressed into the write buffer.
            bool prevLiteralNotSaved = false;
            for (; ; )
            {
                // End of file. Number of literals left is 0 to 2.
                if (_readAvailableBytes < MinMatch)
                {
                    // save the last character if it is available
                    if (prevLiteralNotSaved) SaveLiteralInBlockBuf(_readBuffer[_readPtr - 1]);

                    // One or two characters are still in the read buffer
                    while (_readAvailableBytes > 0)
                    {
                        // Block is full. Compress the block buffer
                        if (_blockBufEnd == BlockBufSize) CompressBlockBuf(false);

                        // save the literal in the block buffer
                        SaveLiteralInBlockBuf(_readBuffer[_readPtr]);

                        // update pointer
                        _readPtr++;
                        _readAvailableBytes--;
                    }

                    // Compress the last block of data.
                    CompressBlockBuf(true);
                    return;
                }

                // save current match
                int prevMatch = _matchStart;
                int prevLen = _matchLen;

                // find the longest match for current ReadPtr
                if (_matchLen < _niceLength && _matchLen < _readAvailableBytes) FindLongestMatch();

                // previous match was better
                if (prevLen >= MinMatch && prevLen >= _matchLen)
                {
                    // save the previuos match
                    SaveDistanceInBlockBuf(_readPtr - 1 - prevMatch, prevLen);

                    // move the pointer to the last literal of the current matched block
                    for (prevLen -= 2; prevLen > 0; prevLen--)
                    {
                        // update pointer
                        _readPtr++;
                        _readAvailableBytes--;

                        // update the hash table for each literal of the current matched block
                        HashInsertString();
                    }

                    // previuos literal is not available.
                    prevLiteralNotSaved = false;

                    // reset previous match.
                    _matchLen = MinMatch - 1;
                }

                // current match is better
                else
                {
                    // save the previous single literal
                    if (prevLiteralNotSaved) SaveLiteralInBlockBuf(_readBuffer[_readPtr - 1]);

                    // try again for a better match
                    prevLiteralNotSaved = true;
                }

                // update pointer to next literal
                _readPtr++;
                _readAvailableBytes--;

                // compress block buffer
                if (_blockBufEnd == BlockBufSize)
                {
                    if (prevLiteralNotSaved)
                    {
                        _readPtr--;
                        CompressBlockBuf(false);
                        _readPtr++;
                    }
                    else
                    {
                        // if read pointer is at the end of the buffer it is end of file situation
                        CompressBlockBuf(_readPtr == _readBufEnd);
                        if (_readPtr == _readBufEnd) return;
                    }
                }

                // fill buffer if lookahead area is below minimum
                if (_readAvailableBytes < MinLookahead && !_readEndOfFile)
                {
                    // we need to keep block start within the buffer in case the compression algorithm needs to output an uncompress block
                    // if read block start is small, the read routine will not be able to add more bytes
                    // in that case we will flush the block even if it is not full
                    // if the read buffer size is more that 1MB all of this will not happen
                    if (_readBlockStart < WindowSize)
                    {
                        if (prevLiteralNotSaved) _readPtr--;
                        CompressBlockBuf(false);
                        if (prevLiteralNotSaved) _readPtr++;
                    }

                    // fill the buffer
                    ReadFillBuffer();
                }
            }
        }

        /// <summary>
        /// Finds the longest match.
        /// </summary>
        private void FindLongestMatch()
        {
            // stop the search if the scan pointer is greater than MaxMatch beyond current pointer
            int maxScanPtr = _readPtr + Math.Min(MaxMatch, _readAvailableBytes);

            // initially MatchLen is MinMatch - 1
            // for slow deflate MatchLen will be the best match of the previous byte (ReadStringPtr -1)
            // in that case the current match must be better than the previous one
            int scanEnd = _readPtr + _matchLen;

            // byte value at scan end and one before
            Byte scanEndValue = _readBuffer[scanEnd];

            // Pointer to maximum distance backward in the read buffer
            int maxDistanceLimit = Math.Max(_readPtr - WindowSize, 0);

            // reset current match to no-match
            int curMatch = -1;

            // HashPrevious array has a chained pointers to all locations in the current window
            // that have equal hash values to the ReadStringPtr
            // maximum number of tries along the chain pointers
            for (int chainLength = _matchLen < _goodLength ? _maxChainLength : _maxChainLength / 4; chainLength > 0; chainLength--)
            {
                // get the first possible matched string based on hash code
                // or get the next possible matched string from the linked chain
                curMatch = curMatch < 0 ? HashInsertString() :
                    _hashPrevious[(_readBufferFilePosition + curMatch) & WindowMask] - _readBufferFilePosition;

                // exit if hash entry is empty or it is too far back
                if (curMatch < maxDistanceLimit) break;

                // if we have a previous match test the end characters for possible progress
                if (_readBuffer[curMatch + _matchLen] != scanEndValue) continue;

                // the distance beween current pointer and previous possible match
                int matchDelta = _readPtr - curMatch;

                // find the length of the match				
                int scanPtr;
                for (scanPtr = _readPtr; scanPtr < maxScanPtr && _readBuffer[scanPtr] == _readBuffer[scanPtr - matchDelta]; scanPtr++)
                {
                }

                // we have a longer match
                if (scanPtr > scanEnd)
                {
                    // replace current match in a global area
                    _matchStart = curMatch;
                    _matchLen = scanPtr - _readPtr;

                    // break if this is good enough
                    if (_matchLen >= _niceLength) break;

                    // end of current match and length
                    scanEnd = scanPtr;

                    // we cannot do any better
                    if (scanPtr == maxScanPtr) break;

                    // replace the byte values at the end of this scan
                    scanEndValue = _readBuffer[scanEnd];
                }
            }

            // Discard match if too small and too far away
            if (_matchLen == MinMatch && _readPtr - _matchStart > MatchIsTooFar) _matchLen = MinMatch - 1;
        }

        /// <summary>
        /// Add one byte to the literal list
        /// </summary>
        /// <param name="literal">The literal.</param>
        private void SaveLiteralInBlockBuf(int literal)
        {
            // save literal in block buffer
            _blockBuf[_blockBufEnd++] = literal;

            // update frequency array
            _literalTree.CodeFreq[literal]++;
        }

        /// <summary>
        /// Add one distance/length pair to the literal list
        /// </summary>
        /// <param name="distance">The distance.</param>
        /// <param name="length">The length.</param>
        private void SaveDistanceInBlockBuf(int distance, int length)
        {
            // adjust length (real length range is 3 to 258, saved length range is 0 to 255)
            length -= MinMatch;

            // save distance and length in one integer
            _blockBuf[_blockBufEnd++] = (distance << 8) | length;

            // build frequency array for length code
            int lenCode = LengthCode[length];
            _literalTree.CodeFreq[lenCode]++;

            // accumulate number of extra bits for length codes
            if (lenCode >= 265 && lenCode < 285) _blockBufExtraBits += (lenCode - 261) / 4;

            // build frequency array for distance codes
            int distCode = DistanceCode[distance - 1];
            _distanceTree.CodeFreq[distCode]++;

            // accumulate extra bits for distance codes
            if (distCode >= 4) _blockBufExtraBits += distCode / 2 - 1;
        }

        /// <summary>
        /// Compress the block buffer when it is full
        /// The block buffer is made of literals and distance length pairs
        /// </summary>
        /// <param name="lastBlock">if set to <c>true</c> [last block].</param>
        private void CompressBlockBuf(bool lastBlock)
        {
            // add end of block to code frequency array
            _literalTree.CodeFreq[EndOfBlockSymbol]++;

            // Build trees
            _literalTree.BuildTree();
            _distanceTree.BuildTree();

            // Calculate bitlen frequency
            int bitLengthExtraBits = _literalTree.CalcBLFreq(_bitLengthTree);
            bitLengthExtraBits += _distanceTree.CalcBLFreq(_bitLengthTree);

            // Build bitlen tree
            _bitLengthTree.BuildTree();

            // calculate length in bits of bit length tree
            int blTreeCodes = _bitLengthTree.MaxUsedCodesBitLength();

            // calculate total block length for dynamic coding
            // The 17 is made of: 3 bits block header, 5 bits literal codes, 5 bits distance codes, 4 bits bit-length codes
            int compressedLen = 17 + blTreeCodes * 3 + _bitLengthTree.GetEncodedLength() + bitLengthExtraBits +
                    _literalTree.GetEncodedLength() + _distanceTree.GetEncodedLength() + _blockBufExtraBits;

            // compressed block length in bytes for dynamic coding
            compressedLen = (compressedLen + 7) / 8;

            // calculate total block length for static coding
            int staticLen = 3 + _blockBufExtraBits;
            for (int i = 0; i < MaxLiteralCodes; i++) staticLen += _literalTree.CodeFreq[i] * StaticLiteralLength[i];
            for (int i = 0; i < MaxDistanceCodes; i++) staticLen += _distanceTree.CodeFreq[i] * StaticDistanceLength[i];

            // static block length in bytes
            staticLen = (staticLen + 7) / 8;

            // static trees look better
            if (staticLen <= compressedLen) compressedLen = staticLen;

            // uncompressed read block length in bytes
            int storedBlockLen = _readPtr - _readBlockStart;

            // This is the last compressed block
            if (lastBlock)
            {
                // If this this is the first block and the last block at the same time (relatively small file)
                // and the uncompressed block is better than compressed block, change compression function from deflate to stored
                if (_writeFirstBlock && storedBlockLen <= compressedLen)
                {
                    CompFunction = CompFunc.Stored;
                    _readAvailableBytes = storedBlockLen;
                    DeflateStored();
                    return;
                }

                // Test compressed overall file length.
                // If overall compressed length is more than the original uncompressed size
                // the derived class will rewind the read and write stream.
                if (WriteTotal + _writePtr + (_bitCount + 7) / 8 + Math.Min(compressedLen, storedBlockLen + 5) > ReadTotal)
                {
                    // rewind both read and write streams
                    RewindStreams();

                    // read first block (the derived class will supply this routine)
                    _readAvailableBytes = ReadBytes(_readBuffer, 0, ReadBufSize, out _readEndOfFile);

                    // reset uncompressed and compressed totals
                    ReadTotal = (uint)_readAvailableBytes;
                    WriteTotal = 0;

                    // reset write ptr
                    _writePtr = 0;
                    _bitCount = 0;

                    // change compress function from deflate to stored
                    CompFunction = CompFunc.Stored;

                    // move input stream to output stream
                    DeflateStored();
                    return;
                }
            }

            // Uncompressed block length is better than compressed length.
            // Uncompressed block has 5 bytes overhead.
            // Stored block header plus 2 bytes length and 2 bytes length complament.
            if (storedBlockLen + 5 < compressedLen)
            {
                // loop in case block length is larger tan max allowed
                while (storedBlockLen > 0)
                {
                    // block length (max 65535)
                    int len = Math.Min(storedBlockLen, 0xffff);

                    // adjust remaing length
                    storedBlockLen -= len;

                    // Write the block on even byte boundry, Signal if this is the last block of the file
                    WriteStoredBlock(len, lastBlock && storedBlockLen == 0);

                    // adjust block start pointer
                    _readBlockStart += len;
                }
            }

            // Encode with static tree
            else if (compressedLen == staticLen)
            {
                // write static block header to output file
                WriteBits(((int)BlockType.StaticTrees << 1) + (lastBlock ? 1 : 0), 3);

                // replace the dynamic codes with static codes
                _literalTree.SetStaticCodes(StaticLiteralCodes, StaticLiteralLength);
                _distanceTree.SetStaticCodes(StaticDistanceCodes, StaticDistanceLength);

                // Compress the block and send it to the output buffer
                // This process converts the block buffer values into variable length sequence of bits.
                CompressBlock();

                // adjust block pointer
                _readBlockStart += storedBlockLen;
            }

            // Encode with dynamic tree
            else
            {
                // write dynamic block header to output file
                WriteBits(((int)BlockType.DynamicTrees << 1) + (lastBlock ? 1 : 0), 3);

                // write the dynamic tree to the output stream
                SendAllTrees(blTreeCodes);

                // Compress the block and send it to the output buffer
                // This process converts the block buffer values into variable length sequence of bits.
                CompressBlock();

                // adjust block pointer
                _readBlockStart += storedBlockLen;
            }

            // Set block buffer to empty
            BlockReset();

            // Reset write first block
            _writeFirstBlock = false;
        }

        /// <summary>
        /// Block Reset
        /// </summary>
        private void BlockReset()
        {
            // Set block buffer to empty
            _blockBufEnd = 0;
            _blockBufExtraBits = 0;

            // Reset literal, distance and bit-length trees
            _literalTree.Reset();
            _distanceTree.Reset();
            _bitLengthTree.Reset();
        }

        /// <summary>
        /// At the start of each dynamic block transmit all trees
        /// </summary>
        /// <param name="blTreeCodes">The bl tree codes.</param>
        private void SendAllTrees(int blTreeCodes)
        {
            // Calculate the Huffman codes for all used literals and lengths
            _literalTree.BuildCodes();

            // write to output buffer the number of used literal/length codes
            WriteBits(_literalTree.MaxUsedCodes - 257, 5);

            // Calculate the Huffman codes for all used distances
            _distanceTree.BuildCodes();

            // write to output buffer the number of used distance codes
            WriteBits(_distanceTree.MaxUsedCodes - 1, 5);

            // Calculate the Huffman codes for transmitting the first two trees
            _bitLengthTree.BuildCodes();

            // write to output buffer the number of used bit-length codes
            WriteBits(blTreeCodes - 4, 4);

            // In the next three statements we send the Huffman codes assoicated with each code used.
            // The decompressor will used this information to build a decoder.
            // The decoder will translate incoming codes into original literals.
            // Send to output stream the bit-length tree codes. 
            _bitLengthTree.WriteBitLengthCodeLength(blTreeCodes);

            // Send to output stream the literal/length tree codes.
            _literalTree.WriteTree(_bitLengthTree);

            // Send to output stream the distance tree codes.
            _distanceTree.WriteTree(_bitLengthTree);
        }

        /// <summary>
        /// Compress the static or dynamic block
        /// </summary>
        private void CompressBlock()
        {
            // loop for all entries in the scan buffer
            for (int blockBufPtr = 0; blockBufPtr < _blockBufEnd; blockBufPtr++)
            {
                // length and distance pair
                int distance = _blockBuf[blockBufPtr] >> 8;
                int length = (Byte)_blockBuf[blockBufPtr];

                // literal
                if (distance == 0)
                {
                    //if(WritePtr >= 33138)
                    //	{
                    //	Trace.Write(String.Format("WritePtr: {0}, Literal: {1}", WritePtr, Length));
                    //	}
                    // WriteSymbol translates the literal code to variable length Huffman code and write it to the output stream
                    _literalTree.WriteSymbol(length);
                    continue;
                }

                //if(WritePtr >= 33138)
                //	{
                //	Trace.Write(String.Format("WritePtr: {0}, Length: {1}, Distance {2}", WritePtr, Length + 3, Distance));
                //	}
                // Translate length to length code
                // The LengthCode translation array is defined above in the DeflateMethod static constructor.
                int lenCode = LengthCode[length];

                // WriteSymbol translates the length code to variable length Huffman code and write it to the output stream
                _literalTree.WriteSymbol(lenCode);

                // send extra bits
                // note: LenCode=285 is the highest code and it has no extra bits
                if (lenCode >= 265 && lenCode < 285)
                {
                    int lenBits = (lenCode - 261) / 4;
                    WriteBits(length & ((1 << lenBits) - 1), lenBits);
                }

                // translate distance to distance code
                // The DistanceCode translation array is defined above in the DeflateMethod static constructor.
                int distCode = DistanceCode[--distance];

                // WriteSymbol translates the distance code to variable length Huffman code and write it to the output stream
                _distanceTree.WriteSymbol(distCode);

                // send extra bits
                if (distCode >= 4)
                {
                    int distBits = distCode / 2 - 1;
                    WriteBits(distance & ((1 << distBits) - 1), distBits);
                }
            }

            // write the end of block symbol to output file
            _literalTree.WriteSymbol(EndOfBlockSymbol);
        }

        /// <summary>
        /// Fill read buffer
        /// </summary>
        private void ReadFillBuffer()
        {
            // move the remaining part at the top of the buffer to the start of the buffer (round to 8 bytes)
            // we need to keep at least window size for the best match algorithm
            int startOfMovePtr = Math.Min((_readPtr - WindowSize) & ~7, _readBlockStart & ~7);
            Array.Copy(_readBuffer, startOfMovePtr, _readBuffer, 0, _readBufEnd - startOfMovePtr);

            // the move delta is
            int delta = startOfMovePtr;

            // adjust pointers
            _readBufferFilePosition += delta;
            _readBlockStart -= delta;
            _matchStart -= delta;
            _readPtr -= delta;
            _readBufEnd -= delta;

            // read next block
            int len = ReadBytes(_readBuffer, _readBufEnd, _readBuffer.Length - _readBufEnd, out _readEndOfFile);
            _readBufEnd += len;
            ReadTotal += (uint)len;

            // available bytes in the buffer
            _readAvailableBytes = _readBufEnd - _readPtr;
        }

        /// <summary>
        /// Write bits
        /// </summary>
        /// <param name="bits">The bits.</param>
        /// <param name="count">The count.</param>
        private void WriteBits(int bits, int count)
        {
            // add bits to bits buffer
            _bitBuffer |= (uint)(bits << _bitCount);
            _bitCount += count;

            // we have more than 16 bits in the buffer
            if (_bitCount >= 16)
            {
                // test for room in the buffer
                if (WriteBufSize - _writePtr < 2) WriteToFile();

                // move two bytes from bit buffer to write buffer
                _writeBuffer[_writePtr++] = (byte)_bitBuffer;
                _writeBuffer[_writePtr++] = (byte)(_bitBuffer >> 8);

                // adjust bit buffer
                _bitBuffer >>= 16;
                _bitCount -= 16;
            }
        }

        /// <summary>
        /// Allign bit buffer to byte boundary
        /// </summary>
        private void WriteAlignToByte()
        {
            if (_bitCount > 0)
            {
                // test for room in the buffer
                if (WriteBufSize - _writePtr < 2) WriteToFile();

                // write first byte
                _writeBuffer[_writePtr++] = (byte)_bitBuffer;

                // write second byte if needed
                if (_bitCount > 8) _writeBuffer[_writePtr++] = (byte)(_bitBuffer >> 8);

                // clear bit buffer
                _bitBuffer = 0;
                _bitCount = 0;
            }
        }

        /// <summary>
        /// Flush Stored Block to Write Buffer
        /// </summary>
        /// <param name="length">The length.</param>
        /// <param name="lastBlock">if set to <c>true</c> [last block].</param>
        private void WriteStoredBlock(int length, Boolean lastBlock)
        {
            // block header of stored block (3 bits)
            WriteBits(((int)BlockType.StoredBlock << 1) + (lastBlock ? 1 : 0), 3);

            // flush the bit buffer
            WriteAlignToByte();

            // test for room in the buffer
            if (WriteBufSize - _writePtr < length + 4) WriteToFile();

            // write block length (16 bits)
            _writeBuffer[_writePtr++] = (byte)length;
            _writeBuffer[_writePtr++] = (byte)(length >> 8);

            // write inverted block length (16 bits)
            _writeBuffer[_writePtr++] = (byte)(~length);
            _writeBuffer[_writePtr++] = (byte)((~length) >> 8);

            Array.Copy(_readBuffer, _readBlockStart, _writeBuffer, _writePtr, length);
            _writePtr += length;
        }

        /// <summary>
        /// Writes to file.
        /// </summary>
        private void WriteToFile()
        {
            // write to stream even multiple of 8 bytes
            int writeLength = _writePtr & ~7;
            WriteBytes(_writeBuffer, 0, writeLength);

            // update write total
            WriteTotal += (uint)writeLength;

            // move left over to the start of buffer
            int moveLength = _writePtr - writeLength;
            Array.Copy(_writeBuffer, writeLength, _writeBuffer, 0, moveLength);
            _writePtr = moveLength;
        }

        /// <summary>
        /// Hashes the insert string.
        /// </summary>
        /// <returns></returns>
        private int HashInsertString()
        {
            // end of file test
            if (_readAvailableBytes < MinMatch) return (-1);

            // hash value
            // NOTE: the original hash table was 15 bits (32768)
            // the hash value was calculated as
            // HashValue = (ReadBuffer[ReadPtr] << 10 ^ ReadBuffer[ReadPtr + 1] << 5 | ReadBuffer[ReadPtr + 2]) & 0x7FFF;
            // The method used here is faster and produces less collisions 
            int hashValue = BitConverter.ToUInt16(_readBuffer, _readPtr) ^ HashXlate[_readBuffer[_readPtr + 2]];

            // get the previous pointer at the hash value position
            int previousPtr = _hashTable[hashValue];

            // read pointer file position
            int filePtr = _readBufferFilePosition + _readPtr;

            // save current file position in the hash table
            _hashTable[hashValue] = filePtr;

            // save the previous pointer in a cicular buffer
            _hashPrevious[filePtr & WindowMask] = previousPtr;

            // return with a pointer to read buffer position with the same hash value as the current pointer
            // if there was no previous match, the return value is -1
            return (previousPtr < 0 ? previousPtr : previousPtr - _readBufferFilePosition);
        }

        /// <summary>
        /// Read bytes from input stream
        /// To be implemented by derived class
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        /// <param name="endOfFile">if set to <c>true</c> [end of file].</param>
        /// <returns></returns>
        /// <exception cref="System.NotImplementedException">ReadBytes routine is missing</exception>
        protected abstract int ReadBytes(byte[] buffer, int pos, int len, out bool endOfFile);

        /// <summary>
        /// Write bytes to output stream
        /// To be implemented by derived class
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        /// <exception cref="System.NotImplementedException">WriteBytes routine is missing</exception>
        protected abstract void WriteBytes(byte[] buffer, int pos, int len);

        /// <summary>
        /// Rewind input and output stream
        /// To be implemented by derived class
        /// </summary>
        /// <exception cref="System.Exception">RewindStream routine is missing</exception>
        protected abstract void RewindStreams();

        public abstract void Dispose();
    }
}