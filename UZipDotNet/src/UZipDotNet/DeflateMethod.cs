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
using System.IO;
using System.Text;

namespace UZipDotNet
{
public class DeflateMethod
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
	protected const			Int32		NoCompression = 0;
	protected const			Int32		BestCompression = 9;
	protected const			Int32		DefaultCompression = 6;
	private					Int32		CompLevel = DefaultCompression;

	// These 5 arrays define match process constants one for each of the 10 levels.
	// The constants control the amount of time the program will spend trying to find a better match
	private static readonly Int32[]		GoodLengthTable;
	private static readonly Int32[]		MaxLazyTable;
	private static readonly Int32[]		NiceLengthTable;
	private static readonly Int32[]		MaxChainTable;
	private static readonly CompFunc[]	CompFuncTable;
	private					Int32		GoodLength;
	private					Int32		MaxLazyLength;
	private					Int32		NiceLength;
	private					Int32		MaxChainLength;
	public					CompFunc	CompFunction;

	// repeated string length range is from minimum 3 to maximum 258
	private const			Int32		MinMatch = 3;
	private const			Int32		MaxMatch = 258;

	// read buffer minimum look ahead (LookAhead = BufferEnd - CurrentPointer)
	private const			Int32		MinLookahead = MaxMatch + MinMatch + 1;

	// Window size is the maximum distance the repeated string matching process will look for a match
	private const			Int32		WindowSize = 32768;			// 0x8000
	private const			Int32		MatchIsTooFar = 4096;		// minimum match (3) should not be further than that

	// Hash table is used to speed up the matching process.
	// The program calculate a hash value at the current pointer.
	// It converts 3 bytes (MinMatch) to a 16 Bits value.
	// The hash value is used as an index to the hash table to obtain the first possible match
	private const			Int32		WindowMask = WindowSize - 1;	// 0x7FFF
	private const			Int32		HashTableSize = 65536;			// 0x10000
	private					Int32[]		HashTable;						// Hash table
	private					Int32[]		HashPrevious;					// Linked chains of equal Hash Values
	private static readonly UInt16[]	HashXlate;						// Translation table for the 3rd byte of hash value

	// Compression block type
	// Compressed file is divided into these type of blocks
	// At the end of each block the program will select the shortest block
	private enum BlockType
		{
		StoredBlock,	// no compression block
		StaticTrees,	// compress with static trees
		DynamicTrees	// compress with dynamic trees
		}

	private const			Int32		MaxStoredBlockSize = 65535;	// Stored block maximum length 0xFFFF
	private const			Int32		BlockBufSize = 16384;		// Block buffer size 0x4000
	private const			Int32		EndOfBlockSymbol = 256;		// End of block marker 0x100
	private					Int32[]		BlockBuf;					// Block buffer. Each elemet is either
																	// (Literal) or (Distance << 8) | (Length - 3)
	private					Int32		BlockBufEnd;				// Current end of block buffer
	private					Int32		BlockBufExtraBits;			// Number of extra bits associated with Distance or Length

	// The literal tree is a combination of literals, lengths and end of block marker
	private					DeflateTree	LiteralTree;
	private const			Int32		MinLiteralCodes = 257;		// Minimum literal codes
	private const			Int32		MaxLiteralCodes = 286;		// Maximum literal codes
	private const			Int32		MaxLiteralBitLen = 15;		// Maximum number of bits in literal codes
	private static readonly UInt16[]	LengthCode;					// translation between Length-3 and LengthCode
	private static readonly UInt16[]	StaticLiteralCodes;
	private static readonly Byte[]		StaticLiteralLength;

	// The distance tree. The distance to previous occurance of matched string
	private					DeflateTree	DistanceTree;
	private const			Int32		MinDistanceCodes = 2;		// Minimum distance codes
	private const			Int32		MaxDistanceCodes = 30;		// Maximum distance codes
	private const			Int32		MaxDistanceBitLen = 15;		// Maximum number of bits in distance codes
	private static readonly Byte[]		DistanceCode;				// Translation between Distance-1 and DistanceCode
	private static readonly UInt16[]	StaticDistanceCodes;
	private static readonly Byte[]		StaticDistanceLength;

	// Bit Length tree is the tree encoding the other two trees in a dynamic block
	private					DeflateTree	BitLengthTree;
	private const			Int32		MinBitLengthCodes = 4;		// Minimum bit length codes
	private const			Int32		MaxBitLengthCodes = 19;		// Maximum bit length codes
	private const			Int32		MaxBitLengthBitLen = 7;		// Maximum number of bites in bit length codes

	public					UInt32		ReadTotal;					// Total number of bytes read from input stream
	private					Byte[]		ReadBuffer;					// Input stream buffer
	private					Int32		ReadBufferFilePosition;		// Start of ReadBuffer in relation to the input file
	private					Int32		ReadBlockStart;				// Start of current compression block
	private					Int32		ReadPtr;					// Current read pointer
	private					Int32		ReadBufEnd;					// Read buffer logical end
	private					Int32		ReadAvailableBytes;			// Available bytes for compression matching (=ReadBufEnd - ReadPtr)
	private					Boolean		ReadEndOfFile;				// The current read buffer is the last buffer of the file
	private					Int32		MatchStart;					// Pointer to matched string in prior text
	private					Int32		MatchLen;					// Length of matched string
	private const			Int32		ReadBufSize = 0x100000;		// Read buffer size

	public					UInt32		WriteTotal;					// Total number of bytes written to output stream
	private					Byte[]		WriteBuffer;				// Output stream buffer
	private					Int32		WritePtr;					// Current pointer to output stream buffer
	private					UInt32		BitBuffer;					// 32 bit buffer
	private					Int32		BitCount;					// number of active bits in bit buffer
	private					Boolean		WriteFirstBlock;			// First compressed block is not out yet
	private const			Int32		WriteBufSize = 0x100000;	// Write buffer size

	////////////////////////////////////////////////////////////////////
	// Deflate static constructor
	// We use the static constructor to build all static read only arrays
	////////////////////////////////////////////////////////////////////

	static DeflateMethod()
		{
		// compression level translation tables
		// Level 0 Stored, Level 1-3 Fast, Level 4-9 Slow
		GoodLengthTable = new Int32[] {0, 4, 4, 4, 4, 8, 8, 8, 32, 32};
		MaxLazyTable = new Int32[] {0, 4, 5, 6, 4, 16, 16, 32, 128, 258};
		NiceLengthTable = new Int32[] {0, 8, 16, 32, 16, 32, 128, 128, 258, 258};
		MaxChainTable = new Int32[] {0, 4, 8, 32, 16, 32, 128, 256, 1024, 4096};
		CompFuncTable = new CompFunc[] {CompFunc.Stored,
										CompFunc.Fast, CompFunc.Fast, CompFunc.Fast,
										CompFunc.Slow, CompFunc.Slow, CompFunc.Slow,
										CompFunc.Slow, CompFunc.Slow, CompFunc.Slow};

		// translation table for the third byte of Hash calculations
		// Bit 0 to 15, Bit 2 to 14, Bit 4 to 13, Bit 6 to 12
		// Bit 1 to 7,  Bit 3 to 6,  Bit 5 to 5,  Bit 7 to 4
		HashXlate = new UInt16[]
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
			0x3070, 0xB070, 0x30F0, 0xB0F0, 0x7070, 0xF070, 0x70F0, 0xF0F0, 
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
		LengthCode = new UInt16[256];
		Int32 Base = 257;
		Int32 Divider = 1;
		Int32 Next = 8;
		for(UInt32 Len = 0; Len < 255; Len++)
			{
			if(Len == Next)
				{
				Base += 4;
				Divider <<= 1;
				Next <<= 1;
				}
			LengthCode[Len] = (UInt16) (Base + Len / Divider);
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
		Base = 0;
		Divider = 1;
		Next = 4;
		for(UInt32 Dist = 0; Dist < WindowSize; Dist++)
			{
			if(Dist == Next)
				{
				Base += 2;
				Divider <<= 1;
				Next <<= 1;
				}
			DistanceCode[Dist] = (Byte) (Base + Dist / Divider);
			}

		// static literal codes and length codes  (See RFC 1951 3.2.6)
		//	Lit Value    Bits        Codes
		//	---------    ----        -----
		//	  0 - 143     8          00110000 through 10111111
		//	144 - 255     9          110010000 through 111111111
		//	256 - 279     7          0000000 through 0010111
		//	280 - 287     8          11000000 through 11000111
		//
		StaticLiteralCodes = new UInt16[288];
		StaticLiteralLength = new Byte[288];
		Int32 Code = 0;
		for(Int32 Index = 256; Index <= 279; Index++)
			{
			StaticLiteralCodes[Index] = BitReverse.Reverse16Bits(Code);
			StaticLiteralLength[Index] = (Byte) 7;
			Code += 1 << (16 - 7);
			}
		for(Int32 Index = 0; Index <= 143; Index++)
			{
			StaticLiteralCodes[Index] = BitReverse.Reverse16Bits(Code);
			StaticLiteralLength[Index] = (Byte) 8;
			Code += 1 << (16 - 8);
			}
		for(Int32 Index = 280; Index <= 287; Index++)
			{
			StaticLiteralCodes[Index] = BitReverse.Reverse16Bits(Code);
			StaticLiteralLength[Index] = (Byte) 8;
			Code += 1 << (16 - 8);
			}
		for(Int32 Index = 144; Index <= 255; Index++)
			{
			StaticLiteralCodes[Index] = BitReverse.Reverse16Bits(Code);
			StaticLiteralLength[Index] = (Byte) 9;
			Code += 1 << (16 - 9);
			}

		//	Static distance codes (See RFC 1951 3.2.6)
		//	Distance codes 0-31 are represented by (fixed-length) 5-bit
		//	codes, with possible additional bits as shown in the table
		//	shown in Paragraph 3.2.5, above.  Note that distance codes 30-
		//	31 will never actually occur in the compressed data.
		//
		StaticDistanceCodes = new UInt16[MaxDistanceCodes];
		StaticDistanceLength = new Byte[MaxDistanceCodes];
		for(Int32 Index = 0; Index < MaxDistanceCodes; Index++) 
			{
			StaticDistanceCodes[Index] = BitReverse.Reverse16Bits(Index << 11);
			StaticDistanceLength[Index] = 5;
			}
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Deflate constructor
	// The constructor is used to allocate buffers.
	////////////////////////////////////////////////////////////////////

	public DeflateMethod
			(
			Int32		CompLevel
			)
		{
		// compress level
		this.CompLevel = CompLevel < NoCompression || CompLevel > BestCompression ? DefaultCompression : CompLevel;

		// create the three trees that contol the compression process
		LiteralTree = new DeflateTree(WriteBits, MinLiteralCodes, MaxLiteralCodes, MaxLiteralBitLen);
		DistanceTree = new DeflateTree(WriteBits, MinDistanceCodes, MaxDistanceCodes, MaxDistanceBitLen);
		BitLengthTree = new DeflateTree(WriteBits, MinBitLengthCodes, MaxBitLengthCodes, MaxBitLengthBitLen);

		// allocate compression block buffer
		BlockBuf = new Int32[BlockBufSize];

		// allocate read buffer
		ReadBuffer = new Byte[ReadBufSize];

		// allocate write buffer
		WriteBuffer = new Byte[WriteBufSize];

		// hash tables initialization
		HashTable = new Int32[HashTableSize];
		HashPrevious = new Int32[WindowSize];
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Set and get user compression level (0 to 9)
	////////////////////////////////////////////////////////////////////

	public Int32 CompressionLevel
		{
		set
			{
			// define compression control values based on the user compression level 0 to 9
			CompLevel = value < NoCompression || value > BestCompression ? DefaultCompression : value;
			}
		get
			{
			return(CompLevel);
			}
		}

	////////////////////////////////////////////////////////////////////
	// Compress read stream to write stream
	// This is the main function of the DefaultMethod class
	////////////////////////////////////////////////////////////////////

	public void Compress()
		{
		// define compression control values based on the user compression level 0 to 9
		CompFunction = CompFuncTable[CompLevel];
		GoodLength = GoodLengthTable[CompLevel];
		MaxLazyLength = MaxLazyTable[CompLevel];
		NiceLength = NiceLengthTable[CompLevel];
		MaxChainLength = MaxChainTable[CompLevel];

		// read process initialization
		ReadBufferFilePosition = 0;
		ReadBlockStart = 0;
		ReadPtr = 0;
		ReadEndOfFile = false;
		MatchStart = 0;
		MatchLen = MinMatch - 1;

		// read first block (the derived class will supply this routine)
		ReadBufEnd = ReadBytes(ReadBuffer, 0, ReadBufSize, out ReadEndOfFile);
		ReadTotal = (UInt32) ReadBufEnd;

		// available bytes in the buffer
		ReadAvailableBytes = ReadBufEnd;

		// if file is less than 8 bytes, just store it
		if(ReadAvailableBytes < 8) CompFunction = CompFunc.Stored;

		// write process initialization
		WriteTotal = 0;
		WritePtr = 0;
		BitBuffer = 0;
		BitCount = 0;
		WriteFirstBlock = true;

		// hash tables initialization
		for(Int32 HashPtr = HashTable.Length - 1; HashPtr >= 0; HashPtr--) HashTable[HashPtr] = -1;
		for(Int32 HashPtr = HashPrevious.Length - 1; HashPtr >= 0; HashPtr--) HashPrevious[HashPtr] = -1;

		// switch based on the type of compression
		switch(CompFunction)
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
		if(WritePtr != 0)
			{
			WriteBytes(WriteBuffer, 0, WritePtr);
			WriteTotal += (UInt32) WritePtr;
			}

		// successful return
		return;
		}

	////////////////////////////////////////////////////////////////////
	// No compression
	// Move blocks of data from read stream to write stream
	////////////////////////////////////////////////////////////////////

	private void DeflateStored()
		{
		while(ReadAvailableBytes > 0)
			{
			// write the content of the read buffer
			WriteBytes(ReadBuffer, 0, ReadAvailableBytes);
			WriteTotal += (UInt32) ReadAvailableBytes;

			// end of input file
			if(ReadEndOfFile) break;

			// read next block
			ReadAvailableBytes = ReadBytes(ReadBuffer, 0, ReadBufSize, out ReadEndOfFile);
			ReadTotal += (UInt32) ReadAvailableBytes;
			}
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Fast compression
	// The program will use this compression method if
	// user compression level was 1, 2 or 3
	////////////////////////////////////////////////////////////////////

	private void DeflateFast()
		{
		// Set block buffer to empty
		BlockReset();

		// This is the main compression loop for compression level 1 to 3.
		// The program will scan the whole file for matching strings.
		// If no match was found, the current literal is saved in block buffer.
		// If match is found, the distance and length are saved in block buffer.
		// When the block buffer is full, the block is compressed into the write buffer.
		for(;;)
			{
			// End of file. The buffer has 0 to 2 bytes left.
			if(ReadAvailableBytes < MinMatch)
				{
				while(ReadAvailableBytes > 0)
					{
					// Block buffer is full. Compress the buffer.
					if(BlockBufEnd == BlockBufSize) CompressBlockBuf(false);

					// save the last one or two characters
					SaveLiteralInBlockBuf(ReadBuffer[ReadPtr]);

					// update pointer
					ReadPtr++;
					ReadAvailableBytes--;
					}
			
				// Compress the content of the last buffer and signal end of file.
				CompressBlockBuf(true);
				return;
				}

			// look for a match to the current string in the previous text.
			MatchLen = MinMatch - 1;
			FindLongestMatch();

			// if Find Longest Match is successful MatchStart and MatchLength will be set
			if(MatchLen >= MinMatch)
				{
				// add the distance and length to the distance and length arrays
				SaveDistanceInBlockBuf(ReadPtr - MatchStart, MatchLen);

				// move the current string pointer after the matched string
				// if the match length is less than the max_lazy, add hash values
				// otherwise do not include the matched area for future searches
				if(MatchLen <= MaxLazyLength)
					{
					while(--MatchLen > 0)
						{
						ReadPtr++;
						HashInsertString();
						}
					ReadPtr++;
					}
				else
					{
					ReadPtr += MatchLen;
					}
				}
			else
				{
				// No match found
				SaveLiteralInBlockBuf(ReadBuffer[ReadPtr]);
				ReadPtr++;
				}

			// update available bytes after read pointer move
			ReadAvailableBytes = ReadBufEnd - ReadPtr;

			// end of one block
			if(BlockBufEnd == BlockBufSize) CompressBlockBuf(false);

			// fill buffer if lookahead area is below minimum
			if(ReadAvailableBytes < MinLookahead && !ReadEndOfFile)
				{
				// We need to keep block start within the buffer in case the compression
				// algorithm needs to output an uncompress block.
				// if read block start is small, the read routine will not be able to add more bytes
				// in that case we will flush the block even if it is not full
				// if the read buffer size is more that 1MB all of this will not happen
				if(ReadBlockStart < WindowSize) CompressBlockBuf(false);

				// fill the buffer
				ReadFillBuffer();
				}
			}
		}

	////////////////////////////////////////////////////////////////////
	// Normal compression
	// The program will use this compression method if
	// user compression level was 4 to 9
	////////////////////////////////////////////////////////////////////

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
		Boolean PrevLiteralNotSaved = false;
		for(;;)
			{
			// End of file. Number of literals left is 0 to 2.
			if(ReadAvailableBytes < MinMatch)
				{
				// save the last character if it is available
				if(PrevLiteralNotSaved) SaveLiteralInBlockBuf(ReadBuffer[ReadPtr - 1]);

				// One or two characters are still in the read buffer
				while(ReadAvailableBytes > 0)
					{
					// Block is full. Compress the block buffer
					if(BlockBufEnd == BlockBufSize) CompressBlockBuf(false);

					// save the literal in the block buffer
					SaveLiteralInBlockBuf(ReadBuffer[ReadPtr]);

					// update pointer
					ReadPtr++;
					ReadAvailableBytes--;
					}

				// Compress the last block of data.
				CompressBlockBuf(true);
				return;
				}

			// save current match
			Int32 PrevMatch = MatchStart;
			Int32 PrevLen = MatchLen;

			// find the longest match for current ReadPtr
			if(MatchLen < NiceLength && MatchLen < ReadAvailableBytes) FindLongestMatch();

			// previous match was better
			if(PrevLen >= MinMatch && PrevLen >= MatchLen)
				{
				// save the previuos match
				SaveDistanceInBlockBuf(ReadPtr - 1 - PrevMatch, PrevLen);

				// move the pointer to the last literal of the current matched block
				for(PrevLen -= 2; PrevLen > 0; PrevLen--)
					{
					// update pointer
					ReadPtr++;
					ReadAvailableBytes--;

					// update the hash table for each literal of the current matched block
					HashInsertString();
					}

				// previuos literal is not available.
				PrevLiteralNotSaved = false;

				// reset previous match.
				MatchLen = MinMatch - 1;
				}

			// current match is better
			else
				{
				// save the previous single literal
				if(PrevLiteralNotSaved) SaveLiteralInBlockBuf(ReadBuffer[ReadPtr - 1]);

				// try again for a better match
				PrevLiteralNotSaved = true;
				}

			// update pointer to next literal
			ReadPtr++;
			ReadAvailableBytes--;

			// compress block buffer
			if(BlockBufEnd == BlockBufSize)
				{
				if(PrevLiteralNotSaved)
					{
					ReadPtr--;
					CompressBlockBuf(false);
					ReadPtr++;
					}
				else
					{
					// if read pointer is at the end of the buffer it is end of file situation
					CompressBlockBuf(ReadPtr == ReadBufEnd);
					if(ReadPtr == ReadBufEnd) return;
					}
				}

			// fill buffer if lookahead area is below minimum
			if(ReadAvailableBytes < MinLookahead && !ReadEndOfFile)
				{
				// we need to keep block start within the buffer in case the compression algorithm needs to output an uncompress block
				// if read block start is small, the read routine will not be able to add more bytes
				// in that case we will flush the block even if it is not full
				// if the read buffer size is more that 1MB all of this will not happen
				if(ReadBlockStart < WindowSize)
					{
					if(PrevLiteralNotSaved) ReadPtr--;
					CompressBlockBuf(false);
					if(PrevLiteralNotSaved) ReadPtr++;
					}

				// fill the buffer
				ReadFillBuffer();
				}
			}
		}

	////////////////////////////////////////////////////////////////////
	// Find Longest Match
	////////////////////////////////////////////////////////////////////

		private void FindLongestMatch() 
		{
		// stop the search if the scan pointer is greater than MaxMatch beyond current pointer
		Int32 MaxScanPtr = ReadPtr + Math.Min(MaxMatch, ReadAvailableBytes);

		// initially MatchLen is MinMatch - 1
		// for slow deflate MatchLen will be the best match of the previous byte (ReadStringPtr -1)
		// in that case the current match must be better than the previous one
		Int32 ScanEnd = ReadPtr + MatchLen;

		// byte value at scan end and one before
		Byte ScanEndValue = ReadBuffer[ScanEnd];

		// Pointer to maximum distance backward in the read buffer
		Int32 MaxDistanceLimit = Math.Max(ReadPtr - WindowSize, 0);

		// reset current match to no-match
		Int32 CurMatch = -1;

		// HashPrevious array has a chained pointers to all locations in the current window
		// that have equal hash values to the ReadStringPtr
		// maximum number of tries along the chain pointers
		for(Int32 ChainLength = MatchLen < GoodLength ? MaxChainLength : MaxChainLength / 4; ChainLength > 0; ChainLength--)
			{
			// get the first possible matched string based on hash code
			// or get the next possible matched string from the linked chain
			CurMatch = CurMatch < 0 ? HashInsertString() :
				HashPrevious[(ReadBufferFilePosition + CurMatch) & WindowMask] - ReadBufferFilePosition;

			// exit if hash entry is empty or it is too far back
			if(CurMatch < MaxDistanceLimit) break;

			// if we have a previous match test the end characters for possible progress
			if(ReadBuffer[CurMatch + MatchLen] != ScanEndValue) continue;

			// the distance beween current pointer and previous possible match
			Int32 MatchDelta = ReadPtr - CurMatch;

			// find the length of the match				
			Int32 ScanPtr = 0;
			for(ScanPtr = ReadPtr; ScanPtr < MaxScanPtr && ReadBuffer[ScanPtr] == ReadBuffer[ScanPtr - MatchDelta]; ScanPtr++);

			// we have a longer match
			if(ScanPtr > ScanEnd) 
				{
				// replace current match in a global area
				MatchStart = CurMatch;
				MatchLen = ScanPtr - ReadPtr;

				// break if this is good enough
				if(MatchLen >= NiceLength) break;

				// end of current match and length
				ScanEnd = ScanPtr;

				// we cannot do any better
				if(ScanPtr == MaxScanPtr) break;

				// replace the byte values at the end of this scan
				ScanEndValue = ReadBuffer[ScanEnd];
				}
			}

		// Discard match if too small and too far away
		if(MatchLen == MinMatch && ReadPtr - MatchStart > MatchIsTooFar) MatchLen = MinMatch - 1;

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Add one byte to the literal list
	////////////////////////////////////////////////////////////////////

	private void SaveLiteralInBlockBuf
			(
			Int32 Literal
			)
		{
		// save literal in block buffer
		BlockBuf[BlockBufEnd++] = Literal;

		// update frequency array
		LiteralTree.CodeFreq[Literal]++;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Add one distance/length pair to the literal list
	////////////////////////////////////////////////////////////////////

	private void SaveDistanceInBlockBuf
			(
			Int32 Distance,
			Int32 Length
			)
		{
		// adjust length (real length range is 3 to 258, saved length range is 0 to 255)
		Length -= MinMatch;

		// save distance and length in one integer
		BlockBuf[BlockBufEnd++] = (Distance << 8) | Length;

		// build frequency array for length code
		Int32 LenCode = LengthCode[Length];
		LiteralTree.CodeFreq[LenCode]++;

		// accumulate number of extra bits for length codes
		if(LenCode >= 265 && LenCode < 285) BlockBufExtraBits += (LenCode - 261) / 4;

		// build frequency array for distance codes
		Int32 DistCode = DistanceCode[Distance - 1];
		DistanceTree.CodeFreq[DistCode]++;

		// accumulate extra bits for distance codes
		if(DistCode >= 4) BlockBufExtraBits += DistCode / 2 - 1;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Compress the block buffer when it is full
	// The block buffer is made of literals and distance length pairs
	////////////////////////////////////////////////////////////////////

	private void CompressBlockBuf
			(
			bool LastBlock
			)
		{
		// add end of block to code frequency array
		LiteralTree.CodeFreq[EndOfBlockSymbol]++;

		// Build trees
		LiteralTree.BuildTree();
		DistanceTree.BuildTree();

		// Calculate bitlen frequency
		Int32 BitLengthExtraBits = LiteralTree.CalcBLFreq(BitLengthTree);
		BitLengthExtraBits += DistanceTree.CalcBLFreq(BitLengthTree);

		// Build bitlen tree
		BitLengthTree.BuildTree();

		// calculate length in bits of bit length tree
		Int32 blTreeCodes = BitLengthTree.MaxUsedCodesBitLength();

		// calculate total block length for dynamic coding
		// The 17 is made of: 3 bits block header, 5 bits literal codes, 5 bits distance codes, 4 bits bit-length codes
		Int32 CompressedLen = 17 + blTreeCodes * 3 + BitLengthTree.GetEncodedLength() + BitLengthExtraBits +
				LiteralTree.GetEncodedLength() + DistanceTree.GetEncodedLength() + BlockBufExtraBits;

		// compressed block length in bytes for dynamic coding
		CompressedLen = (CompressedLen + 7) / 8;

		// calculate total block length for static coding
		Int32 StaticLen = 3 + BlockBufExtraBits;
		for(Int32 i = 0; i < MaxLiteralCodes; i++) StaticLen += LiteralTree.CodeFreq[i] * StaticLiteralLength[i];
		for(Int32 i = 0; i < MaxDistanceCodes; i++) StaticLen += DistanceTree.CodeFreq[i] * StaticDistanceLength[i];

		// static block length in bytes
		StaticLen = (StaticLen + 7) / 8;

		// static trees look better
		if(StaticLen <= CompressedLen) CompressedLen = StaticLen;

		// uncompressed read block length in bytes
		Int32 StoredBlockLen = ReadPtr - ReadBlockStart;

		// This is the last compressed block
		if(LastBlock)
			{
			// If this this is the first block and the last block at the same time (relatively small file)
			// and the uncompressed block is better than compressed block, change compression function from deflate to stored
			if(WriteFirstBlock && StoredBlockLen <= CompressedLen)
				{
				CompFunction = CompFunc.Stored;
				ReadAvailableBytes = StoredBlockLen;
				DeflateStored();
				return;
				}

			// Test compressed overall file length.
			// If overall compressed length is more than the original uncompressed size
			// the derived class will rewind the read and write stream.
			if(WriteTotal + WritePtr + (BitCount + 7) / 8 + Math.Min(CompressedLen, StoredBlockLen + 5) > ReadTotal)
				{
				// rewind both read and write streams
				RewindStreams();

				// read first block (the derived class will supply this routine)
				ReadAvailableBytes = ReadBytes(ReadBuffer, 0, ReadBufSize, out ReadEndOfFile);

				// reset uncompressed and compressed totals
				ReadTotal = (UInt32) ReadAvailableBytes;
				WriteTotal = 0;

				// reset write ptr
				WritePtr = 0;
				BitCount = 0;

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
		if(StoredBlockLen + 5 < CompressedLen) 
			{
			// loop in case block length is larger tan max allowed
			while(StoredBlockLen > 0)
				{
				// block length (max 65535)
				Int32 Len = Math.Min(StoredBlockLen, (Int32) 0xffff);

				// adjust remaing length
				StoredBlockLen -= Len;

				// Write the block on even byte boundry, Signal if this is the last block of the file
				WriteStoredBlock(Len, LastBlock && StoredBlockLen == 0);

				// adjust block start pointer
				ReadBlockStart += Len;
				}
			}

		// Encode with static tree
		else if(CompressedLen == StaticLen)
			{
			// write static block header to output file
			WriteBits(((Int32) BlockType.StaticTrees << 1) + (LastBlock ? 1 : 0), 3);

			// replace the dynamic codes with static codes
			LiteralTree.SetStaticCodes(StaticLiteralCodes, StaticLiteralLength);
			DistanceTree.SetStaticCodes(StaticDistanceCodes, StaticDistanceLength);

			// Compress the block and send it to the output buffer
			// This process converts the block buffer values into variable length sequence of bits.
			CompressBlock();

			// adjust block pointer
			ReadBlockStart += StoredBlockLen;
			}

		// Encode with dynamic tree
		else
			{
			// write dynamic block header to output file
			WriteBits(((Int32) BlockType.DynamicTrees << 1) + (LastBlock ? 1 : 0), 3);

			// write the dynamic tree to the output stream
			SendAllTrees(blTreeCodes);

			// Compress the block and send it to the output buffer
			// This process converts the block buffer values into variable length sequence of bits.
			CompressBlock();

			// adjust block pointer
			ReadBlockStart += StoredBlockLen;
			}

		// Set block buffer to empty
		BlockReset();

		// Reset write first block
		WriteFirstBlock = false;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Block Reset
	////////////////////////////////////////////////////////////////////

	private void BlockReset()
		{
		// Set block buffer to empty
		BlockBufEnd = 0;
		BlockBufExtraBits = 0;

		// Reset literal, distance and bit-length trees
		LiteralTree.Reset();
		DistanceTree.Reset();
		BitLengthTree.Reset();
		return;
		}

	////////////////////////////////////////////////////////////////////
	// At the start of each dynamic block transmit all trees
	////////////////////////////////////////////////////////////////////

	private void SendAllTrees
			(
			Int32 blTreeCodes
			)
		{
		// Calculate the Huffman codes for all used literals and lengths
		LiteralTree.BuildCodes();

		// write to output buffer the number of used literal/length codes
		WriteBits(LiteralTree.MaxUsedCodes - 257, 5);

		// Calculate the Huffman codes for all used distances
		DistanceTree.BuildCodes();

		// write to output buffer the number of used distance codes
		WriteBits(DistanceTree.MaxUsedCodes - 1, 5);

		// Calculate the Huffman codes for transmitting the first two trees
		BitLengthTree.BuildCodes();

		// write to output buffer the number of used bit-length codes
		WriteBits(blTreeCodes - 4, 4);

		// In the next three statements we send the Huffman codes assoicated with each code used.
		// The decompressor will used this information to build a decoder.
		// The decoder will translate incoming codes into original literals.
		// Send to output stream the bit-length tree codes. 
		BitLengthTree.WriteBitLengthCodeLength(blTreeCodes);

		// Send to output stream the literal/length tree codes.
		LiteralTree.WriteTree(BitLengthTree);

		// Send to output stream the distance tree codes.
		DistanceTree.WriteTree(BitLengthTree);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Compress the static or dynamic block
	////////////////////////////////////////////////////////////////////

	private void CompressBlock()
		{
		// loop for all entries in the scan buffer
		for(Int32 BlockBufPtr = 0; BlockBufPtr < BlockBufEnd; BlockBufPtr++)
			{
			// length and distance pair
			Int32 Distance = BlockBuf[BlockBufPtr] >> 8;
			Int32 Length = (Byte) BlockBuf[BlockBufPtr];

			// literal
			if(Distance == 0)
				{
//if(WritePtr >= 33138)
//	{
//	Trace.Write(String.Format("WritePtr: {0}, Literal: {1}", WritePtr, Length));
//	}
				// WriteSymbol translates the literal code to variable length Huffman code and write it to the output stream
				LiteralTree.WriteSymbol(Length);
				continue;
				}

//if(WritePtr >= 33138)
//	{
//	Trace.Write(String.Format("WritePtr: {0}, Length: {1}, Distance {2}", WritePtr, Length + 3, Distance));
//	}
			// Translate length to length code
			// The LengthCode translation array is defined above in the DeflateMethod static constructor.
			Int32 LenCode = LengthCode[Length];

			// WriteSymbol translates the length code to variable length Huffman code and write it to the output stream
			LiteralTree.WriteSymbol(LenCode);

			// send extra bits
			// note: LenCode=285 is the highest code and it has no extra bits
			if(LenCode >= 265 && LenCode < 285)
				{
				Int32 LenBits = (LenCode - 261) / 4;
				WriteBits(Length & ((1 << LenBits) - 1), LenBits);
				}

			// translate distance to distance code
			// The DistanceCode translation array is defined above in the DeflateMethod static constructor.
			Int32 DistCode = DistanceCode[--Distance];

			// WriteSymbol translates the distance code to variable length Huffman code and write it to the output stream
			DistanceTree.WriteSymbol(DistCode);

			// send extra bits
			if(DistCode >= 4)
				{
				Int32 DistBits = DistCode / 2 - 1;
				WriteBits(Distance & ((1 << DistBits) - 1), DistBits);
				}
			}

		// write the end of block symbol to output file
		LiteralTree.WriteSymbol(EndOfBlockSymbol);
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Fill read buffer
	/////////////////////////////////////////////////////////////////////

	private void ReadFillBuffer()
		{
		// move the remaining part at the top of the buffer to the start of the buffer (round to 8 bytes)
		// we need to keep at least window size for the best match algorithm
		Int32 StartOfMovePtr = Math.Min((ReadPtr - WindowSize) & ~7, ReadBlockStart & ~7);
		Array.Copy(ReadBuffer, StartOfMovePtr, ReadBuffer, 0, ReadBufEnd - StartOfMovePtr);

		// the move delta is
		Int32 Delta = StartOfMovePtr;

		// adjust pointers
		ReadBufferFilePosition += Delta;
		ReadBlockStart -= Delta;
		MatchStart -= Delta;
		ReadPtr -= Delta;
		ReadBufEnd -= Delta;

		// read next block
		Int32 Len = ReadBytes(ReadBuffer, ReadBufEnd, ReadBuffer.Length - ReadBufEnd, out ReadEndOfFile);
		ReadBufEnd += Len;
		ReadTotal += (UInt32) Len;

		// available bytes in the buffer
		ReadAvailableBytes = ReadBufEnd - ReadPtr;
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Write bits
	/////////////////////////////////////////////////////////////////////

	private void WriteBits
			(
			Int32		Bits,
			Int32		Count
			)
		{
		// add bits to bits buffer
		BitBuffer |= (UInt32)(Bits << BitCount);
		BitCount += Count;

		// we have more than 16 bits in the buffer
		if(BitCount >= 16) 
			{
			// test for room in the buffer
			if(WriteBufSize - WritePtr < 2) WriteToFile();

			// move two bytes from bit buffer to write buffer
			WriteBuffer[WritePtr++] = (Byte) BitBuffer;
			WriteBuffer[WritePtr++] = (Byte) (BitBuffer >> 8);

			// adjust bit buffer
			BitBuffer >>= 16;
			BitCount -= 16;
			}
		return;
		}
		
	/////////////////////////////////////////////////////////////////////
	// Allign bit buffer to byte boundry
	/////////////////////////////////////////////////////////////////////

	private void WriteAlignToByte() 
		{
		if(BitCount > 0) 
			{
			// test for room in the buffer
			if(WriteBufSize - WritePtr < 2) WriteToFile();

			// write first byte
			WriteBuffer[WritePtr++] = (Byte) BitBuffer;

			// write second byte if needed
			if(BitCount > 8) WriteBuffer[WritePtr++] = (Byte) (BitBuffer >> 8);

			// clear bit buffer
			BitBuffer = 0;
			BitCount = 0;
			}
		return;
		}
		
	/////////////////////////////////////////////////////////////////////
	// Flush Stored Block to Write Buffer
	/////////////////////////////////////////////////////////////////////

	private void WriteStoredBlock
			(
			Int32		Length,
			Boolean		LastBlock
			)
		{
		// block header of stored block (3 bits)
		WriteBits(((Int32) BlockType.StoredBlock << 1) + (LastBlock ? 1 : 0), 3);

		// flush the bit buffer
		WriteAlignToByte();

		// test for room in the buffer
		if(WriteBufSize - WritePtr < Length + 4) WriteToFile();

		// write block length (16 bits)
		WriteBuffer[WritePtr++] = (Byte) Length;
		WriteBuffer[WritePtr++] = (Byte) (Length >> 8);

		// write inverted block length (16 bits)
		WriteBuffer[WritePtr++] = (Byte) (~Length);
		WriteBuffer[WritePtr++] = (Byte) ((~Length) >> 8);

		Array.Copy(ReadBuffer, ReadBlockStart, WriteBuffer, WritePtr, Length);
		WritePtr += Length;
		return;
		}
		
	/////////////////////////////////////////////////////////////////////
	// Write To File
	/////////////////////////////////////////////////////////////////////

	private void WriteToFile()
		{
		// write to stream even multiple of 8 bytes
		Int32 WriteLength = WritePtr & ~7;
		WriteBytes(WriteBuffer, 0, WriteLength);

		// update write total
		WriteTotal += (UInt32) WriteLength;

		// move left over to the start of buffer
		Int32 MoveLength = WritePtr - WriteLength;
		Array.Copy(WriteBuffer, WriteLength, WriteBuffer, 0, MoveLength);
		WritePtr = MoveLength;
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Hash insert string
	/////////////////////////////////////////////////////////////////////

	private Int32 HashInsertString() 
		{
		// end of file test
		if(ReadAvailableBytes < MinMatch) return(-1);

		// hash value
		// NOTE: the original hash table was 15 bits (32768)
		// the hash value was calculated as
		// HashValue = (ReadBuffer[ReadPtr] << 10 ^ ReadBuffer[ReadPtr + 1] << 5 | ReadBuffer[ReadPtr + 2]) & 0x7FFF;
		// The method used here is faster and produces less collisions 
		Int32 HashValue = BitConverter.ToUInt16(ReadBuffer, ReadPtr) ^ HashXlate[ReadBuffer[ReadPtr + 2]];

		// get the previous pointer at the hash value position
		Int32 PreviousPtr = HashTable[HashValue];

		// read pointer file position
		Int32 FilePtr = ReadBufferFilePosition + ReadPtr;

		// save current file position in the hash table
		HashTable[HashValue] = FilePtr;

		// save the previous pointer in a cicular buffer
		HashPrevious[FilePtr & WindowMask] = PreviousPtr;

		// return with a pointer to read buffer position with the same hash value as the current pointer
		// if there was no previous match, the return value is -1
		return(PreviousPtr < 0 ? PreviousPtr : PreviousPtr - ReadBufferFilePosition);
		}

	/////////////////////////////////////////////////////////////////////
	// Read bytes from input stream
	// To be implemented by derived class
	/////////////////////////////////////////////////////////////////////

	public virtual Int32 ReadBytes(Byte[] buffer, Int32 Pos, Int32 Len, out Boolean EndOfFile)
		{
		throw new ApplicationException("ReadBytes routine is missing");
		}

	/////////////////////////////////////////////////////////////////////
	// Write bytes to output stream
	// To be implemented by derived class
	/////////////////////////////////////////////////////////////////////

	public virtual void WriteBytes(Byte[] buffer, Int32 Pos, Int32 Len)
		{
		throw new ApplicationException("WriteBytes routine is missing");
		}

	/////////////////////////////////////////////////////////////////////
	// Rewind input and output stream 
	// To be implemented by derived class
	/////////////////////////////////////////////////////////////////////

	public virtual void RewindStreams()
		{
		throw new ApplicationException("RewindStream routine is missing");
		}
	}
}
