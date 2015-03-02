/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateTree.cs
//	Support class designed to build a translation tree between
//	variable length bit strings and codes. It is based on Huffman
//	coding. Frequently used codes get short bit strings codes and
//	infrequently used codes get longer bit strings codes. The
//	InflateMethod class uses three instants of this class for the
//	three trees used in the program.
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
using System.Collections.Generic;

namespace UZipDotNet
{
public enum TreeType
	{
	Literal,
	Distance,
	BitLen
	}

/////////////////////////////////////////////////////////////////////
// Inflate Tree
/////////////////////////////////////////////////////////////////////

public class InflateTree
	{
	// maximum number of codes for the three Huffman trees
	private const	Int32		MaxLiteralCodes = 286;
	private const	Int32		MaxDistanceCodes = 30;
	private const	Int32		MaxBitLengthCodes = 19;

	// assume that no code will be longer than 15 bit
	private const	Int32		MAX_BITLEN = 15;

	public			Int32[]		ActiveTree;
	public			Int32		ActiveBitMask;
	private			Int32[]		StaticTree;
	private			Int32		StaticBitMask;
	private			Int32[]		DynamicTree;
	private			Int32[]		BitLengthCount = new Int32[MAX_BITLEN + 1];
	private			Int32[]		InitialCode = new Int32[MAX_BITLEN + 1];
			
	////////////////////////////////////////////////////////////////////
	// Constructs static tree
	////////////////////////////////////////////////////////////////////

	public InflateTree
			(
			TreeType	Type
			)
		{
		Byte[] CodeLength;

		// switch based on type
		switch(Type)
			{
			// literal tree
			case TreeType.Literal:
				// literal tree is made of 286 codes alphabet however inorder to balance the tree it is made of 288
				CodeLength = new Byte[288];

				// build code length for static literal tree
				Int32 Index = 0;

				// the first 144 codes are 8 bits long
				while(Index < 144) CodeLength[Index++] = 8;

				// the next 112 codes are 9 bits long
				while(Index < 256) CodeLength[Index++] = 9;

				// the next 24 codes are 7 bits long
				while(Index < 280) CodeLength[Index++] = 7;

				// the last 8 codes are 8 bits long
				while(Index < 288) CodeLength[Index++] = 8;

				// build the static tree
				ActiveTree = new Int32[512];
				BuildTree(CodeLength, 0, CodeLength.Length);
				StaticTree = ActiveTree;
				StaticBitMask = ActiveBitMask;

				// alocate the space for dynamic tree
				DynamicTree = new Int32[1024];
				break;

			// distance tree
			case TreeType.Distance:
				// distance tree is made of 32 codes alphabet
				CodeLength = new Byte[32];

				// all codes are 5 bits long
				for(Index = 0; Index < 32; Index++) CodeLength[Index] = 5;

				// build the static tree
				ActiveTree = new Int32[32];
				BuildTree(CodeLength, 0, CodeLength.Length);
				StaticTree = ActiveTree;
				StaticBitMask = ActiveBitMask;

				// alocate the dynamic tree
				DynamicTree = new Int32[575];
				break;

			// bit length tree
			case TreeType.BitLen:
				ActiveTree = new Int32[128];
				break;
			}
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Set Static Table
	////////////////////////////////////////////////////////////////////

	public void SetStatic()
		{
		ActiveTree = StaticTree;
		ActiveBitMask = StaticBitMask;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Set Dynamic Table
	////////////////////////////////////////////////////////////////////

	public void SetDynamic()
		{
		ActiveTree = DynamicTree;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Constructs a Huffman tree from the array of code lengths.
	////////////////////////////////////////////////////////////////////

	public void BuildTree
			(
			Byte[]	CodeLengths,
			Int32	Offset,
			Int32	Length
			)
		{
		// Build frequency array for bit length 1 to 15
		// Note: BitLengthCount[0] will never be used.
		// All elements of CodeLength are greater than zero.
		// In other words no code has zero length
		Array.Clear(BitLengthCount, 0, BitLengthCount.Length);
		for(Int32 Index = 0; Index < Length; Index++) BitLengthCount[CodeLengths[Offset + Index]]++;

		// build array of inital codes for each group of equal code length
		Int32 InitCode = 0;
		for(Int32 Bits = 1; Bits <= MAX_BITLEN; Bits++)
			{
			InitialCode[Bits] = InitCode;
			InitCode += BitLengthCount[Bits] << (16 - Bits);
			}

		// Warning
		// If BitLengthCount array was constructed properly InitCode should be equal to 65536.
		// During all my initial testing of decompressing ZIP archives coming from other programs
		// that was the case. I finally run into one file that was compressed by old version
		// of PKZIP version 2.50 4-15-1998 that the following commented statement threw exception.
		// I would strongly recomment to anyone making modification to the compression/decompression
		// software to activate this statement during the testing.
		//
		//if(InitCode != 65536) throw new ApplicationException("Code lengths don't add up properly.");
		//

		// longest code bit count
		Int32 MaxBits;
		for(MaxBits = MAX_BITLEN; BitLengthCount[MaxBits] == 0; MaxBits--);

		// number of hash bits at the root of the decode tree
		// the longest code but no more than 9 bits
		Int32 ActiveHashBits = Math.Min(9, MaxBits);

		// the decoding process is using this bit mask
		ActiveBitMask = 1 << ActiveHashBits;

		// hash table at the begining of the tree
		Array.Clear(ActiveTree, 0, ActiveTree.Length);

		// pointer to the area above the hash table for codes longer than hash bits
		Int32 EndPtr = ActiveBitMask;

		// fill the tree
		for(Int32 Index = 0; Index < Length; Index++)
			{
			// code length in bits
			Int32 Bits = CodeLengths[Offset + Index];

			// code not in use
			if(Bits == 0) continue;

			// reverse the code from the most significant part to the least significant part of the integer
			Int32 Code = BitReverse.Reverse16Bits(InitialCode[Bits]);

			// the number of bits is less than the hash bits
			// we need to add artificial entries for the missing bits
			if(Bits < ActiveHashBits)
				{
				Int32 NextInc = (1 << Bits);
				for(Int32 Next = Code; Next < ActiveBitMask; Next += NextInc)
					{
					ActiveTree[Next] = ~(Index << 4 | Bits);
					}
				}

			// the number of bits is equal to the hash bits
			else if(Bits == ActiveHashBits)
				{
				ActiveTree[Code] = ~(Index << 4 | Bits);
				}

			// the number of bits is greater than the hash bits
			else
				{
				// hash pointer to the start of the tree table
				Int32 HashPtr = Code & (ActiveBitMask - 1);

				// get the value at this location
				Int32 Next = ActiveTree[HashPtr];

				// the location is not initialized
				if(Next == 0)
					{
					// get a free node at the area above the hash area and link it to the tree
					ActiveTree[HashPtr] = EndPtr;
					Next = EndPtr;
					EndPtr += 2;
					}

				// navigate through the tree above the hash area
				// add empty nodes as required
				Int32 BitMaskEnd = 1 << (Bits - 1);
				for(Int32 BitMask = ActiveBitMask; BitMask != BitMaskEnd; BitMask <<= 1)
					{
					// current bit is one, adjust the next pointer
					if((Code & BitMask) != 0) Next++;

					// get the value at this location
					Int32 Next1 = ActiveTree[Next];

					// the location was initialized before, continue to follow the path
					if(Next1 > 0)
						{
						Next = Next1;
						continue;
						}

					// add free node from the area above the hash table and link it to the tree
					ActiveTree[Next] = EndPtr;
					Next = EndPtr;
					EndPtr += 2;
					}

				// we are now at a leaf point, add the symbol and the number of bits
				if((Code & BitMaskEnd) != 0) Next++;
				ActiveTree[Next] = ~(Index << 4 | Bits);
				}

			// update initial code for the next code with the same number of bits
			InitialCode[Bits] += 1 << (16 - Bits);
			}

		// exit
		return;
		}
	}
}
