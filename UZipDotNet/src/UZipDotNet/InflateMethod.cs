/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateMethod.cs
//	Class designed to decompress one file using the Deflate method
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
public class InflateMethod
	{
	private enum BlockType
		{
		StoredBlock,
		StaticTrees,
		DynamicTrees
		}
	// Window size is the maximum distance the repeated string matching process will look for a match
	private const Int32		WindowSize = 32768;			// 0x8000

	// maximum number of codes for the three Huffman trees
	private const Int32		MaxLiteralCodes = 286;
	private const Int32		MaxDistanceCodes = 30;
	private const Int32		MaxBitLengthCodes = 19;

	// bit length repeast symbols
	private const Int32		RepeatSymbol_3_6 = 16;
	private const Int32		RepeatSymbol_3_10 = 17;
	private const Int32		RepeatSymbol_11_138 = 18;

	// Bit length symbols are transmitted in a coded way.
	// This array translate real codes to transmitted codes.
	// It is done to with the hope that most likely codes are at the begining
	// and the least likely will be at the end and if not used will not be transmitted.
	private static readonly Int32[] BitLengthOrder;

	// Base lengths for literal codes 257..285
	private static readonly Int32[] BaseLength;
	
	// Extra bits for literal codes 257..285
	private static readonly Int32[] ExtraLengthBits;
	
	// Base offsets for distance codes 0..29
	private static readonly Int32[] BaseDistance;
	
	// Extra bits for distance codes
	private static readonly Int32[] ExtraDistanceBits;

	private Byte[]			ReadBuffer;				// compressed data input buffer
	private Int32			ReadPtr;				// current pointer to compressed data item
	private Int32			ReadBufEnd;				// end of compressed data in the buffer
	private Boolean			ReadEndOfFile;			// end of file flag. If true, the current read buffer is the last buffer
	private UInt32			BitBuffer;				// 32 bit buffer for reading for reading variable length bits codes
	private Int32			BitCount;				// bit buffer active bit count
	private const Int32		ReadBufSize = 0x100000;	// read buffer length 1MB

	private Byte[]			WriteBuffer;			// decompressed data buffer
	private Int32			WritePtr;				// current pointer to end of the write data in the buffer
	private const Int32		WriteBufSize = 0x100000; // write buffer length 1MB

	// allocate arrays
	private Byte[]			BitLengthArray;
	private Byte[]			LiteralDistanceArray;

	private InflateTree		BitLengthTree;
	private InflateTree		LiteralTree;
	private InflateTree		DistanceTree;

	////////////////////////////////////////////////////////////////////
	// Inflate static constructor
	// We use the static constructor to build all static read only arrays
	////////////////////////////////////////////////////////////////////

	static InflateMethod()
		{
		// Bit length symbols are transmitted in a coded way.
		// This array translate real codes to transmitted codes.
		// It is done to with the hope that most likely codes are at the begining
		// and the least likely will be at the end and if not used will not be transmitted.
		BitLengthOrder = new Int32[]
					{RepeatSymbol_3_6, RepeatSymbol_3_10, RepeatSymbol_11_138,
					0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15};

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
		BaseLength = new Int32[]
					{
					3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
					35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
					};
		
		// Extra bits for literal codes 257..285
		ExtraLengthBits = new Int32[]
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
		BaseDistance = new Int32[]
					{
					1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385,
					513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577
					};
		
		// Extra bits for distance codes
		ExtraDistanceBits = new Int32[]
					{
					0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
					7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
					};
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Constructor
	////////////////////////////////////////////////////////////////////
	
	public InflateMethod()
		{
		// allocate buffers
		ReadBuffer = new Byte[ReadBufSize];
		WriteBuffer = new Byte[WriteBufSize];
		BitLengthArray = new Byte[MaxBitLengthCodes];
		LiteralDistanceArray = new Byte[MaxLiteralCodes + MaxDistanceCodes];
		BitLengthTree = new InflateTree(TreeType.BitLen);
		LiteralTree = new InflateTree(TreeType.Literal);
		DistanceTree = new InflateTree(TreeType.Distance);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Decompress the input file
	////////////////////////////////////////////////////////////////////

	public void Decompress()
		{
		// reset read process
		ReadPtr = 0;
		BitBuffer = 0;
		BitCount = 0;

		// read first block
		ReadBufEnd = ReadBytes(ReadBuffer, 0, ReadBufSize, out ReadEndOfFile);

		// reset write process
		WritePtr = 0;

		// reset last block flag
		Boolean LastBlock = false;

		// loop for blocks
		while(!LastBlock)
			{
			// get next block header
			Int32 BlockHeader = GetBits(3);

			// set last block flag				
			if((BlockHeader & 1) != 0) LastBlock = true;

			// switch based on type of block
			switch((BlockType) (BlockHeader >> 1)) 
				{
				// copy uncompressed block from read buffer to wrire buffer
				case BlockType.StoredBlock:
					CopyStoredBlock();
					break;

				// decode compressed block using static trees
				case BlockType.StaticTrees:
					LiteralTree.SetStatic();
					DistanceTree.SetStatic();
					DecodeCompressedBlock(true);
					break;

				// decode compressed block using dynamic trees
				case BlockType.DynamicTrees:
					DecodeDynamicHuffamTrees();
					LiteralTree.SetDynamic();
					DistanceTree.SetDynamic();
					DecodeCompressedBlock(false);
					break;

				default:
					throw new ApplicationException("Unknown block type");
				}
			}

		// flush write buffer
		WriteToFile(true);
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Copy stored file
	// this routine is called for zip file archived
	// when the compression method is no compression (code of zero)
	/////////////////////////////////////////////////////////////////////

	public void NoCompression()
		{
		// loop writing directly from read buffer
		ReadEndOfFile = false;
		while(!ReadEndOfFile)
			{
			// read one block
			ReadBufEnd = ReadBytes(ReadBuffer, 0, ReadBufSize, out ReadEndOfFile);

			// write read buffer content
			WriteBytes(ReadBuffer, 0, ReadBufEnd);
			}
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Copy uncompresses block from input stream to output stream
	/////////////////////////////////////////////////////////////////////

	private void CopyStoredBlock()
		{
		// move read buffer pointer to next multiple of 8 bits
		// drop bits that are not multiple of 8
		BitBuffer >>= BitCount & 7;

		// effectively subtract the dropped bits from the count
		BitCount &= ~7;

		// get block length
		// note: the Get16Bits routine will empty the read bit buffer
		// after reading 2 blocks of 16 bits the bit buffer is guarantied to be empty
		Int32 BlockLength = Get16Bits();

		// get the inverted block length and compare it to the block length
		if(Get16Bits() != (BlockLength ^ 0xffff)) throw new ApplicationException("Stored block length in error");

		// make sure read buffer has enough bytes for full block transfer
		// not enough bytes available
		if(ReadPtr + BlockLength > ReadBufEnd) TestReadBuffer(BlockLength);

		// make sure write buffer has enough space to receive the block in one transfer
		if(WritePtr + BlockLength > WriteBuffer.Length) WriteToFile(false);

		// write to output buffer
		Array.Copy(ReadBuffer, ReadPtr, WriteBuffer, WritePtr, BlockLength);

		// update pointers and total
		WritePtr += BlockLength;
		ReadPtr += BlockLength;
		return;
		}
		
	/////////////////////////////////////////////////////////////////////
	// Get next 16 bits
	// Assume that bit buffer is aligned on 8 bit boundry.
	// Empty the bit buffer first. After two calles to this routine
	// the bit buffer is guarantied to be empty.
	// Throw exception if end of input is reached
	/////////////////////////////////////////////////////////////////////

	private Int32 Get16Bits()
		{
		Int32 Token;

		// bit buffer has 16 or 24 bits
		if(BitCount >= 16)
			{
			Token = (Int32) BitBuffer & 0xffff;
			BitBuffer >>= 16;
			BitCount -= 16;
			return(Token);
			}

		// bit buffer has 8 bits
		if(BitCount >= 8)
			{
			// get high bits from bit buffer
			Token = (Int32) BitBuffer;
			BitBuffer = 0;
			BitCount = 0;
			}
		else
			{
			// get high bits from read buffer
			Token = ReadByte();

			// we are at the end of file case
			if(Token < 0) throw new ApplicationException("Unexpected end of file (Get16Bits)");
			}

		// get low bits from read buffer
		Int32 NextByte = ReadByte();

		// we are at the end of file case
		if(NextByte < 0) throw new ApplicationException("Unexpected end of file (Get16Bits)");

		// return 16 bits
		return((NextByte << 8) | Token);
		}
		
	/////////////////////////////////////////////////////////////////////
	// Test read buffer for at least Len bytes availability
	/////////////////////////////////////////////////////////////////////

	private void TestReadBuffer
			(
			Int32	Len
			)
		{
		// end of file flag is on
		if(ReadEndOfFile) throw new ApplicationException("Premature end of file reading zip header");

		// move the top part of the file to the start of the buffer (round to 8 bytes)
		Int32 StartOfMovePtr = ReadPtr & ~7;
		Int32 MoveSize = ReadBufEnd - StartOfMovePtr;
		Array.Copy(ReadBuffer, StartOfMovePtr, ReadBuffer, 0, MoveSize);

		// adjust read pointer
		ReadPtr &= 7;

		// read one block
		ReadBufEnd = MoveSize + ReadBytes(ReadBuffer, MoveSize, ReadBufSize - MoveSize, out ReadEndOfFile);

		// test again for sufficient look ahead buffer
		if(ReadPtr + Len > ReadBufEnd)  throw new ApplicationException("Premature end of file reading zip header");

		// get next byte
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Decode the input stream and produce the output stream
	////////////////////////////////////////////////////////////////////

	private void DecodeCompressedBlock(Boolean Static)
		{
		// loop for all symbols of one block
		for(;;)
			{
			// Loop while symbols are less than 256.
			// In other words input literals go unchanged to output stream
			Int32 Symbol;
			while((Symbol = ReadSymbol(LiteralTree)) < 256)
				{
				// test for write buffer full
				if(WritePtr == WriteBufSize) WriteToFile(false);

				// write to output buffer
				WriteBuffer[WritePtr++] = (Byte) Symbol;
				}

			// end of block
			if(Symbol == 256) return;

			// translate symbol into copy length
			Symbol -= 257;					
			Int32 StrLength = BaseLength[Symbol];
			Int32 ReqBits = ExtraLengthBits[Symbol];
			if(ReqBits > 0) StrLength += GetBits(ReqBits);

			// get next symbol
			Symbol = ReadSymbol(DistanceTree);

			// translate into copy distance
			Int32 StrDist = BaseDistance[Symbol];
			ReqBits = ExtraDistanceBits[Symbol];
			if(ReqBits > 0) StrDist += GetBits(ReqBits);

			// test for write buffer full
			if(WritePtr + StrLength > WriteBufSize) WriteToFile(false);

			// test for overlap
			Int32 Len = StrLength > StrDist ? StrDist : StrLength;

			// write to output buffer
			Array.Copy(WriteBuffer, WritePtr - StrDist, WriteBuffer, WritePtr, Len);

			// update pointer and length
			WritePtr += Len;

			// special case of repeating strings
			if(StrLength > StrDist)
				{
				// copy one byte at a time
				Int32 WriteEnd = WritePtr + StrLength - StrDist;
				for(; WritePtr < WriteEnd; WritePtr++) WriteBuffer[WritePtr] = WriteBuffer[WritePtr - StrDist];
				}
			}
		}

	/////////////////////////////////////////////////////////////////////
	// Write To File
	/////////////////////////////////////////////////////////////////////

	private void WriteToFile
			(
			Boolean Flush
			)
		{
		// write buffer keeping one window size (make sure it is multiple of 8)
		Int32 Len = Flush ? WritePtr : (WritePtr - WindowSize) & ~(7);

		// write to file
		WriteBytes(WriteBuffer, 0, Len);

		// move leftover to start of buffer (except for flush)
		WritePtr -= Len;
		Array.Copy(WriteBuffer, Len, WriteBuffer, 0, WritePtr);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Decode dynamic Huffman header
	////////////////////////////////////////////////////////////////////

	private void DecodeDynamicHuffamTrees()
		{
		// length of length/literal tree		
		Int32 LiteralLength = GetBits(5) + 257;

		// length of distance tree
		Int32 DistanceLength = GetBits(5) + 1;

		// length of bit length tree
		Int32 BitLengthReceived = GetBits(4) + 4;

		// get bit length info from input stream
		// note: array length must be 19 and not length received
		Array.Clear(BitLengthArray, 0, BitLengthArray.Length);
		for(Int32 Index = 0; Index < BitLengthReceived; Index++)
			BitLengthArray[BitLengthOrder[Index]] = (Byte) GetBits(3);

		// create bit length hauffman tree
		BitLengthTree.BuildTree(BitLengthArray, 0, BitLengthArray.Length);

		// create a combined array of length/literal and distance
		Int32 TotalLength = LiteralLength + DistanceLength;
		Array.Clear(LiteralDistanceArray, 0, LiteralDistanceArray.Length);
		Byte LastCode = 0;
		for(Int32 Ptr = 0; Ptr < TotalLength; )
			{
			// get next symbol from input stream
			Int32 Symbol = ReadSymbol(BitLengthTree);

			// switch based on symbol
			switch(Symbol)
				{
				// symbol is less than 16 it is a literal
				default:
					LiteralDistanceArray[Ptr++] = LastCode = (Byte) Symbol;
					continue;

				case RepeatSymbol_3_6:
					for(Int32 Count = GetBits(2) + 3; Count > 0; Count--) LiteralDistanceArray[Ptr++] = LastCode;
					continue;

				case RepeatSymbol_3_10:
					for(Int32 Count = GetBits(3) + 3; Count > 0; Count--) LiteralDistanceArray[Ptr++] = 0;
					continue;

				case RepeatSymbol_11_138:
					for(Int32 Count = GetBits(7) + 11; Count > 0; Count--) LiteralDistanceArray[Ptr++] = 0;
					continue;
				}
			}

		// create the literal array and distance array
		LiteralTree.SetDynamic();
		LiteralTree.BuildTree(LiteralDistanceArray, 0, LiteralLength);
		DistanceTree.SetDynamic();
		DistanceTree.BuildTree(LiteralDistanceArray, LiteralLength, DistanceLength);
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Read Next Byte From Read Buffer
	/////////////////////////////////////////////////////////////////////

	private Int32 ReadByte()
		{
		// test for end of read buffer
		if(ReadPtr == ReadBufEnd)
			{
			// end of file flag was set during last read operation
			if(ReadEndOfFile) return(-1);

			// read one block
			ReadBufEnd = ReadBytes(ReadBuffer, 0, ReadBufSize, out ReadEndOfFile);

			// test again for sufficient look ahead buffer
			if(ReadBufEnd == 0)  throw new ApplicationException("Premature end of file reading zip header");

			// reset read pointer
			ReadPtr = 0;
			}

		// get next byte
		return((Int32) ReadBuffer[ReadPtr++]);
		}

	/////////////////////////////////////////////////////////////////////
	// Get next n bits
	// Throw exception if end of input is reached
	/////////////////////////////////////////////////////////////////////

	private Int32 GetBits
			(
			Int32 Bits
			)
		{
		// fill the buffer to a maximum of 32 bits
		for(; BitCount <= 24; BitCount += 8) 
			{
			Int32 OneByte = ReadByte();
			if(OneByte < 0) break;
			BitBuffer |= (UInt32) (OneByte << BitCount);
			}

		// error: the program should not ask for bits beyond end of file
		if(Bits > BitCount) throw new ApplicationException("Peek Bits: Premature end of file");

		Int32 Token = (Int32) BitBuffer & ((1 << Bits) - 1);
		BitBuffer >>= Bits;
		BitCount -= Bits;
		return(Token);
		}
		
	/////////////////////////////////////////////////////////////////////
	// Reads the next symbol from input.  The symbol is encoded using the huffman tree.
	/////////////////////////////////////////////////////////////////////

	private Int32 ReadSymbol
			(
			InflateTree		Tree
			)
		{
		// fill the buffer to a maximum of 32 bits
		for(; BitCount <= 24; BitCount += 8) 
			{
			// read next byte from read buffer
			Int32 OneByte = ReadByte();

			// end of file
			if(OneByte < 0) break;

			// append to the bit buffer
			BitBuffer |= (UInt32) (OneByte << BitCount);
			}

		// loop through the decode tree
		Int32 Next, Mask;
		for(Mask = Tree.ActiveBitMask, Next = Tree.ActiveTree[(Int32) BitBuffer & (Mask - 1)];
			Next > 0 && Mask < 0x10000; Next = (BitBuffer & Mask) == 0 ? Tree.ActiveTree[Next] : Tree.ActiveTree[Next + 1], Mask <<= 1);

		// error
		if(Next >= 0) throw new ApplicationException("Error decoding the compressed bit stream (decoding tree error)");

		// invert the symbol plus bit count
		Next = ~Next;

		// extract the number of bits
		Int32 Bits = Next & 15;

		// remove the bits from the bit buffer
		BitBuffer >>= Bits;
		BitCount -= Bits;

		// error
		if(BitCount < 0) throw new ApplicationException("Error decoding the compressed bit stream (premature end of file)");

		// exit with symbol
		return(Next >> 4);
		}

	/////////////////////////////////////////////////////////////////////
	// Read bytes from input stream
	/////////////////////////////////////////////////////////////////////

	public virtual Int32 ReadBytes(Byte[] buffer, Int32 Pos, Int32 Len, out Boolean EndOfFile)
		{
		throw new ApplicationException("ReadBytes routine is missing");
		}

	/////////////////////////////////////////////////////////////////////
	// Write bytes to output stream
	/////////////////////////////////////////////////////////////////////

	public virtual void WriteBytes(Byte[] buffer, Int32 Pos, Int32 Len)
		{
		throw new ApplicationException("WriteBytes routine is missing");
		}
	}
}
