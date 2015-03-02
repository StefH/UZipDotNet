/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	DeflateTree.cs
//	A support class designed to build a translation tree between
//	codes and variable length bit codes. It is based on Huffman
//	coding. Frequently used codes get short bit strings codes and
//	infrequently used codes get longer bit strings codes. The
//	DeflateMethod class uses three instants of this class for the
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
public delegate void WriteBits(Int32 Bits, Int32 Count);

/////////////////////////////////////////////////////////////////////
// Frequency Node
// This class is used to calculate the Huffman coding
// base on frequency
/////////////////////////////////////////////////////////////////////

public struct FreqNode : IComparable<FreqNode>
	{
	public Int32	Code;
	public Int32	Freq;
	public Int32	Child;	// left child is Child - 1

	public Int32 CompareTo
			(
			FreqNode Other
			)
		{
		return(this.Freq - Other.Freq);
		}
	}

/////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////

public class DeflateTree
	{
	private const Int32		RepeatSymbol_3_6 = 16;
	private const Int32		RepeatSymbol_3_10 = 17;
	private const Int32		RepeatSymbol_11_138 = 18;

	private static Int32[]	BitLengthOrder = {RepeatSymbol_3_6, RepeatSymbol_3_10, RepeatSymbol_11_138,
											  0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15};

	public  Int32[]			CodeFreq;			// number of times a given code was used
	public  Int32			MaxUsedCodes;		// the number of highest code used plus one

	private FreqNode[]		FreqTree;			// frequency tree
	private Int32			FreqTreeEnd;		// number of nodes in the frequency tree
	private Int32			MaxUsedBitLen;		// maximum used bit length

	private UInt16[]		Codes;				// dynamic or static variable length bit code
	private Byte[]			CodeLength;			// dynamic or static code length
	private Int32[]			BitLengthFreq;		// frequency of code length (how many times code length is used)
	private Int32[]			FirstCode;			// first code of a group of equal code length
	private UInt16[]		SaveCodes;			// dynamic variable length bit code
	private Byte[]			SaveCodeLength;		// dynamic code length

	private WriteBits		WriteBits;			// write bits method (void WriteBits(Int32 Bits, Int32 Count))
	private Int32			MinCodes;			// minimum number of codes
	private Int32			MaxCodes;			// maximum number of codes
	private Int32			MaxBitLength;		// maximum length in bits of a code

	////////////////////////////////////////////////////////////////////
	// Constructor
	////////////////////////////////////////////////////////////////////
	//					Literal Tree		MinCodes = 257, MaxCodes = 286, MaxBitLength = 15
	//					Distance Tree		MinCodes =   2, MaxCodes =  30, MaxBitLength = 15
	//					Bit Length Tree		MinCodes =   4, MaxCodes =  19, MaxBitLength = 7
	public DeflateTree(WriteBits WriteBits, Int32 MinCodes, Int32 MaxCodes, Int32 MaxBitLength)
		{
		// write bits method
		this.WriteBits = WriteBits;

		// save minimum codes, maximum codes, and maximum bit length
		this.MinCodes = MinCodes;
		this.MaxCodes = MaxCodes;
		this.MaxBitLength = MaxBitLength;

		// allocate arrays
		CodeFreq = new Int32[MaxCodes];
		SaveCodeLength = new Byte[MaxCodes];
		SaveCodes = new UInt16[MaxCodes];
		FreqTree  = new FreqNode[2 * MaxCodes - 1];
		BitLengthFreq = new Int32[MaxBitLength];
		FirstCode = new Int32[MaxBitLength];

		// clear arrays
		Reset();
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Reset class after each block
	////////////////////////////////////////////////////////////////////

	public void Reset()
		{
		// The deflate method will overwrite the CodeLength and Codes arrays
		// if static block was selected. We restore it to the dynamic arrays
		CodeLength = SaveCodeLength;
		Codes = SaveCodes;

		// clear code frequency array
		Array.Clear(CodeFreq, 0, CodeFreq.Length);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Write symbol to output stream
	////////////////////////////////////////////////////////////////////

	public void WriteSymbol(Int32 code)
		{
		WriteBits(Codes[code], CodeLength[code]);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// For static trees overwrite the Codes and CodeLength arrays
	// the reset function will restore the dynamic arrays
	////////////////////////////////////////////////////////////////////

	public void SetStaticCodes
			(
			UInt16[]	StaticCodes,
			Byte[]		StaticLength
			)
		{
		Codes = StaticCodes;
		CodeLength = StaticLength;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Build Huffman tree
	////////////////////////////////////////////////////////////////////

	public void BuildTree()
		{
		// find the highest code in use
		for(MaxUsedCodes = MaxCodes - 1; MaxUsedCodes >= MinCodes && CodeFreq[MaxUsedCodes] == 0; MaxUsedCodes--);
		MaxUsedCodes++;

		// clear frequency tree
		Array.Clear(FreqTree, 0, FreqTree.Length);

		// initialize Huffman frequency tree with leaf nodes
		FreqTreeEnd = 0;
		for(Int32 Code = 0; Code < MaxUsedCodes; Code++)
			{
			// include used codes. ignore codes with zero frequency
			if(CodeFreq[Code] != 0)
				{
				FreqTree[FreqTreeEnd].Code = Code;
				FreqTree[FreqTreeEnd++].Freq = CodeFreq[Code];
				}
			}

		// there are less than 2 codes in use
		if(FreqTreeEnd < 2)
			{
			// zero codes in use
			if(FreqTreeEnd == 0)
				{
				// create artificial node of code 0 with zero frequency
				FreqTree[0].Code = 0;
				FreqTree[0].Freq = 0;
				FreqTreeEnd++;
				}
			// one code in use
			// create artificial second node
			// if the first code is 0 the second is 1, if the first is non zero make the second zero
			// the frequency is zero (not used)
			FreqTree[1].Code = FreqTree[0].Code == 0 ? 1 : 0;
			FreqTree[1].Freq = 0;
			FreqTreeEnd++;
			}

		// sort it by frequency low to high
		Array.Sort(FreqTree, 0, FreqTreeEnd);

		// clear code length
		Array.Clear(CodeLength, 0, CodeLength.Length);

		// loop in case of bit length exceeding the maximum
		// for literals and distance it is 15 and for bit length it is 7
		for(;;)
			{
			// build Huffman tree by combining pairs of nodes
			CombinePairs();

			// calculate length in bits of each code
			MaxUsedBitLen = 0;
			BuildCodeTree(FreqTreeEnd - 1, 0);
			if(MaxUsedBitLen <= MaxBitLength) break;

			// adjust the lowest frequency nodes such that we get a flater tree
			AdjustNodes();
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Combine pairs of lower frequency node to a parent node
	////////////////////////////////////////////////////////////////////

	private void CombinePairs()
		{
		// combine pairs of nodes
		for(Int32 Ptr = 2; Ptr < FreqTreeEnd; Ptr += 2)
			{
			// combined frequency of current pair
			Int32 CombFreq = FreqTree[Ptr - 2].Freq + FreqTree[Ptr - 1].Freq;

			// right pointer
			Int32 RightPtr = FreqTreeEnd;

			// set left pointer to starting point
			Int32 LeftPtr = Ptr;

			// perform binary search
			Int32 InsertPtr;
			Int32 Cmp;
			for(;;)
				{
				// middle pointer
				InsertPtr = (LeftPtr + RightPtr) / 2;

				// compare
				Cmp = CombFreq - FreqTree[InsertPtr].Freq;

				// range is one or exact match
				if(InsertPtr == LeftPtr || Cmp == 0) break;

				// move search to the right
				if(Cmp > 0) LeftPtr = InsertPtr;

				// move search to the left
				else RightPtr = InsertPtr;
				}

			// exact compare (point to end of a group of equal frequencies)
			// positive compare (move to the right as long as it is equal)
			if(Cmp >= 0)
				{
				for(InsertPtr++; InsertPtr < FreqTreeEnd && FreqTree[InsertPtr].Freq == CombFreq; InsertPtr++);
				}

			// open a hole at insert point
			Array.Copy(FreqTree, InsertPtr, FreqTree, InsertPtr + 1, FreqTreeEnd - InsertPtr);
			FreqTreeEnd++;

			// add node with combined freqency
			FreqTree[InsertPtr].Code = -1;
			FreqTree[InsertPtr].Freq = CombFreq;
			FreqTree[InsertPtr].Child = Ptr - 1;
			}

		// add final root node with combined freqency
		FreqTree[FreqTreeEnd].Code = -1;
		FreqTree[FreqTreeEnd].Freq = FreqTree[FreqTreeEnd - 2].Freq + FreqTree[FreqTreeEnd - 1].Freq;
		FreqTree[FreqTreeEnd].Child = FreqTreeEnd - 1;
		FreqTreeEnd++;

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Scan the tree using recursive routine and set the number
	// of bits to represent the code
	////////////////////////////////////////////////////////////////////

	private void BuildCodeTree
			(
			Int32		Ptr,
			Int32		BitCount
			)
		{
		FreqNode FN = FreqTree[Ptr];

		// we are at a parent node
		if(FN.Code < 0)
			{
			// go to children on the left side of this node
			BuildCodeTree(FN.Child - 1, BitCount + 1);

			// go to children on the right side of this node
			BuildCodeTree(FN.Child, BitCount + 1);
			}

		// we are at child node
		else
			{
			if(BitCount > MaxUsedBitLen) MaxUsedBitLen = BitCount;
			CodeLength[FN.Code] = (Byte) BitCount;
			}
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Adjust low frequancy nodes to make the tree more symetric
	////////////////////////////////////////////////////////////////////

	public void AdjustNodes()
		{
		// remove all non leaf nodes
		Int32 Ptr;
		for(Ptr = 0; Ptr < FreqTreeEnd; Ptr++)
			{
			if(FreqTree[Ptr].Code >= 0) continue;
			FreqTreeEnd--;
			Array.Copy(FreqTree, Ptr + 1, FreqTree, Ptr, FreqTreeEnd - Ptr);
			Ptr--;
			}

		// look for first change in frequency
		Int32 Freq = FreqTree[0].Freq;
		for(Ptr = 1; Ptr < FreqTreeEnd && FreqTree[Ptr].Freq == Freq; Ptr++);
		if(Ptr == FreqTreeEnd) throw new ApplicationException("Adjust nodes failed");

		// adjust the frequency of least frequent nodes
		Freq = FreqTree[Ptr].Freq;
		for(Ptr--; Ptr >= 0; Ptr--) FreqTree[Ptr].Freq = Freq;
		return; 
		}

	////////////////////////////////////////////////////////////////////
	// Build code array from bit length frequency distribution
	////////////////////////////////////////////////////////////////////

	public void BuildCodes()
		{
		// build bit length frequency array
		Array.Clear(BitLengthFreq, 0, BitLengthFreq.Length);
		for(Int32 Code = 0; Code < MaxUsedCodes; Code++)
			{
			if(CodeLength[Code] != 0) BitLengthFreq[CodeLength[Code] - 1]++;
			}

		// build array of initial code for each block of codes
		Int32 InitCode = 0;
		for(Int32 BitNo = 0; BitNo < MaxBitLength; BitNo++)
			{
			// save the initial code of the block
			FirstCode[BitNo] = InitCode;

			// advance the code by the frequancy times 2 to the power of 15 less bit length
			InitCode += BitLengthFreq[BitNo] << (15 - BitNo);
			}

		// it must add up to 2 ** 16
		if(InitCode != 65536) throw new ApplicationException("Inconsistent bl_counts!");

		// now fill up the entire code array
		Array.Clear(Codes, 0, Codes.Length);
		for(Int32 Index = 0; Index < MaxUsedCodes; Index++)
			{
			Int32 Bits = CodeLength[Index];
			if(Bits > 0)
				{
				Codes[Index] = BitReverse.Reverse16Bits(FirstCode[Bits - 1]);
				FirstCode[Bits - 1] += 1 << (16 - Bits);
				}
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// calculate encoded data block length
	////////////////////////////////////////////////////////////////////

	public Int32 GetEncodedLength()
		{
		Int32 Len = 0;
		for(Int32 Index = 0; Index < MaxUsedCodes; Index++) Len += CodeFreq[Index] * CodeLength[Index];
		return(Len);
		}

	////////////////////////////////////////////////////////////////////
	// Bit length tree only. Calculate total length
	////////////////////////////////////////////////////////////////////

	public Int32 MaxUsedCodesBitLength()
		{
		// calculate length in bits of bit length tree
		for(Int32 Index = 18; Index >= 4; Index--) if(CodeLength[BitLengthOrder[Index]] > 0) return(Index + 1);
		return(4);
		}

	////////////////////////////////////////////////////////////////////
	// Bit length tree only. Calculate total length
	////////////////////////////////////////////////////////////////////

	public void WriteBitLengthCodeLength
			(
			Int32	blTreeCodes
			)
		{
		// send to output stream the bit length array
		for(Int32 Rank = 0; Rank < blTreeCodes; Rank++) WriteBits(CodeLength[BitLengthOrder[Rank]], 3);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// calculate Bit Length frequency
	///	0 - 15: Represent code lengths of 0 - 15
	//	16: Copy the previous code length 3 - 6 times.
	//		The next 2 bits indicate repeat length
	//		(0 = 3, ... , 3 = 6)
	//		Example:  Codes 8, 16 (+2 bits 11), 16 (+2 bits 10) will expand to
	//		12 code lengths of 8 (1 + 6 + 5)
	//	17: Repeat a code length of 0 for 3 - 10 times. (3 bits of length)
	//	18: Repeat a code length of 0 for 11 - 138 times. (7 bits of length)
	////////////////////////////////////////////////////////////////////

	public Int32 CalcBLFreq
			(
			DeflateTree blTree
			)
		{
		Int32 ExtraBits = 0;
		for(Int32 Code = 0; Code < MaxUsedCodes;)
			{
			// current code length
			Int32 CodeLen = CodeLength[Code];

			// scan the code array for equal code lengths
			Int32 Count = 1;
			for(Code++; Code < MaxUsedCodes && CodeLen == CodeLength[Code]; Code++) Count++;

			// less than three equal code length
			if(Count < 3)
				{
				blTree.CodeFreq[CodeLen] += (Int16) Count;
				continue;
				}

			// code length is other than zero
			if(CodeLen != 0)
				{
				// add one to the frequency of the code itself
				blTree.CodeFreq[CodeLen]++;

				// reduce the count by one
				Count--;

				// for every full block of 6 repeats add one REP_3_6 code
				blTree.CodeFreq[RepeatSymbol_3_6] += Count / 6;
				ExtraBits += 2 * (Count / 6);

				// get the remainder
				if((Count = Count % 6) == 0) continue;

				// remainder is less than 3
				if(Count < 3)
					{
					blTree.CodeFreq[CodeLen] += Count;
					continue;
					}

				// remainder is beween 3 to 5
				blTree.CodeFreq[RepeatSymbol_3_6]++;
				ExtraBits += 2;
				continue;
				}

			// code length is zero and count is 3 or more
			// for every full block of 138 repeats add one REP_11_138 code
			blTree.CodeFreq[RepeatSymbol_11_138] += Count / 138;
			ExtraBits += 7 * (Count / 138);

			// get the remainder
			if((Count = Count % 138) == 0) continue;

			// remainder is less than 3
			if(Count < 3)
				{
				blTree.CodeFreq[CodeLen] += Count;
				continue;
				}

			// remainder is beween 3 to 10
			if(Count <= 10)
				{
				blTree.CodeFreq[RepeatSymbol_3_10]++;
				ExtraBits += 3;
				continue;
				}

			// remainder is beween 11 to 137
			blTree.CodeFreq[RepeatSymbol_11_138]++;
			ExtraBits += 7;
			}
		return(ExtraBits);
		}

	////////////////////////////////////////////////////////////////////
	// Write the tree to output file
	////////////////////////////////////////////////////////////////////

	public void WriteTree
			(
			DeflateTree blTree
			)
		{
		for(Int32 Code = 0; Code < MaxUsedCodes;)
			{
			// current code length
			Int32 CodeLen = CodeLength[Code];

			// scan the code array for equal code lengths
			Int32 Count = 1;
			for(Code++; Code < MaxUsedCodes && CodeLen == CodeLength[Code]; Code++) Count++;

			// less than three equal code length
			if(Count < 3)
				{
				// write the first code length to output file
				blTree.WriteSymbol(CodeLen);

				// write the second code length to output file
				if(Count > 1) blTree.WriteSymbol(CodeLen);
				continue;
				}

			// Used code. Code length is other than zero.
			if(CodeLen != 0)
				{
				// write the code length to output file
				blTree.WriteSymbol(CodeLen);

				// reduce the count by one
				Count--;

				// for every full block of 6 repeats add one REP_3_6 code and two bits of 1 (11)
				for(Int32 Index = Count / 6; Index > 0; Index--)
					{
					blTree.WriteSymbol(RepeatSymbol_3_6);
					WriteBits(6 - 3, 2);
					}

				// get the remainder
				if((Count = Count % 6) == 0) continue;

				// remainder is less than 3
				if(Count < 3)
					{
					// write the code length to output file
					blTree.WriteSymbol(CodeLen);
					if(Count > 1) blTree.WriteSymbol(CodeLen);
					continue;
					}

				// remainder is beween 3 to 5
				blTree.WriteSymbol(RepeatSymbol_3_6);
				WriteBits(Count - 3, 2);
				continue;
				}

			// code length is zero and count is 3 or more
			// for every full block of 138 repeats add one REP_11_138 code plus 7 bits of 1
			for(Int32 Index = Count / 138; Index > 0; Index--)
				{
				blTree.WriteSymbol(RepeatSymbol_11_138);
				WriteBits(138 - 11, 7);
				}

			// get the remainder
			if((Count = Count % 138) == 0) continue;

			// remainder is less than 3
			if(Count < 3)
				{
				// write the code length to output file
				blTree.WriteSymbol(CodeLen);
				if(Count > 1) blTree.WriteSymbol(CodeLen);
				continue;
				}

			// remainder is beween 3 to 10
			if(Count <= 10)
				{
				blTree.WriteSymbol(RepeatSymbol_3_10);
				WriteBits(Count - 3, 3);
				continue;
				}

			// remainder is beween 11 to 137
			blTree.WriteSymbol(RepeatSymbol_11_138);
			WriteBits(Count - 11, 7);
			}
		return;
		}
	}
}
