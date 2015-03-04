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
using UZipDotNet.Support;

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
        // private const int MaxLiteralCodes = 286;
        // private const int MaxDistanceCodes = 30;
        // private const int MaxBitLengthCodes = 19;

        // assume that no code will be longer than 15 bit
        private const int MaxBitlen = 15;

        public int[] ActiveTree;
        public int ActiveBitMask;
        private readonly int[] _staticTree;
        private readonly int _staticBitMask;
        private readonly int[] _dynamicTree;
        private readonly int[] _bitLengthCount = new int[MaxBitlen + 1];
        private readonly int[] _initialCode = new int[MaxBitlen + 1];

        /// <summary>
        /// Initializes a new instance of the <see cref="InflateTree"/> class.
        /// </summary>
        /// <param name="Type">The type.</param>
        public InflateTree(TreeType Type)
        {
            byte[] codeLength;

            // switch based on type
            switch (Type)
            {
                // literal tree
                case TreeType.Literal:
                    // literal tree is made of 286 codes alphabet however inorder to balance the tree it is made of 288
                    codeLength = new Byte[288];

                    // build code length for static literal tree
                    int index = 0;

                    // the first 144 codes are 8 bits long
                    while (index < 144) codeLength[index++] = 8;

                    // the next 112 codes are 9 bits long
                    while (index < 256) codeLength[index++] = 9;

                    // the next 24 codes are 7 bits long
                    while (index < 280) codeLength[index++] = 7;

                    // the last 8 codes are 8 bits long
                    while (index < 288) codeLength[index++] = 8;

                    // build the static tree
                    ActiveTree = new int[512];
                    BuildTree(codeLength, 0, codeLength.Length);
                    _staticTree = ActiveTree;
                    _staticBitMask = ActiveBitMask;

                    // alocate the space for dynamic tree
                    _dynamicTree = new int[1024];
                    break;

                // distance tree
                case TreeType.Distance:
                    // distance tree is made of 32 codes alphabet
                    codeLength = new Byte[32];

                    // all codes are 5 bits long
                    for (index = 0; index < 32; index++) codeLength[index] = 5;

                    // build the static tree
                    ActiveTree = new int[32];
                    BuildTree(codeLength, 0, codeLength.Length);
                    _staticTree = ActiveTree;
                    _staticBitMask = ActiveBitMask;

                    // alocate the dynamic tree
                    _dynamicTree = new int[575];
                    break;

                // bit length tree
                case TreeType.BitLen:
                    ActiveTree = new int[128];
                    break;
            }
        }

        /// <summary>
        ///  Set Static Table
        /// </summary>
        public void SetStatic()
        {
            ActiveTree = _staticTree;
            ActiveBitMask = _staticBitMask;
        }

        /// <summary>
        /// Set Dynamic Table
        /// </summary>
        public void SetDynamic()
        {
            ActiveTree = _dynamicTree;
        }

        /// <summary>
        /// Constructs a Huffman tree from the array of code lengths.
        /// </summary>
        /// <param name="codeLengths">The code lengths.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        public void BuildTree(byte[] codeLengths, int offset, int length)
        {
            // Build frequency array for bit length 1 to 15
            // Note: BitLengthCount[0] will never be used.
            // All elements of CodeLength are greater than zero.
            // In other words no code has zero length
            Array.Clear(_bitLengthCount, 0, _bitLengthCount.Length);
            for (int index = 0; index < length; index++) _bitLengthCount[codeLengths[offset + index]]++;

            // build array of inital codes for each group of equal code length
            int initCode = 0;
            for (int bits = 1; bits <= MaxBitlen; bits++)
            {
                _initialCode[bits] = initCode;
                initCode += _bitLengthCount[bits] << (16 - bits);
            }

            // Warning
            // If BitLengthCount array was constructed properly InitCode should be equal to 65536.
            // During all my initial testing of decompressing ZIP archives coming from other programs
            // that was the case. I finally run into one file that was compressed by old version
            // of PKZIP version 2.50 4-15-1998 that the following commented statement threw exception.
            // I would strongly recomment to anyone making modification to the compression/decompression
            // software to activate this statement during the testing.
            //
            //if(InitCode != 65536) throw new Exception("Code lengths don't add up properly.");
            //

            // longest code bit count
            int maxBits;
            for (maxBits = MaxBitlen; _bitLengthCount[maxBits] == 0; maxBits--)
            {
            }

            // number of hash bits at the root of the decode tree
            // the longest code but no more than 9 bits
            int activeHashBits = Math.Min(9, maxBits);

            // the decoding process is using this bit mask
            ActiveBitMask = 1 << activeHashBits;

            // hash table at the begining of the tree
            Array.Clear(ActiveTree, 0, ActiveTree.Length);

            // pointer to the area above the hash table for codes longer than hash bits
            int endPtr = ActiveBitMask;

            // fill the tree
            for (int index = 0; index < length; index++)
            {
                // code length in bits
                int Bits = codeLengths[offset + index];

                // code not in use
                if (Bits == 0) continue;

                // reverse the code from the most significant part to the least significant part of the integer
                int code = BitReverse.Reverse16Bits(_initialCode[Bits]);

                // the number of bits is less than the hash bits
                // we need to add artificial entries for the missing bits
                if (Bits < activeHashBits)
                {
                    int nextInc = (1 << Bits);
                    for (int next = code; next < ActiveBitMask; next += nextInc)
                    {
                        ActiveTree[next] = ~(index << 4 | Bits);
                    }
                }

                // the number of bits is equal to the hash bits
                else if (Bits == activeHashBits)
                {
                    ActiveTree[code] = ~(index << 4 | Bits);
                }

                // the number of bits is greater than the hash bits
                else
                {
                    // hash pointer to the start of the tree table
                    int hashPtr = code & (ActiveBitMask - 1);

                    // get the value at this location
                    int next = ActiveTree[hashPtr];

                    // the location is not initialized
                    if (next == 0)
                    {
                        // get a free node at the area above the hash area and link it to the tree
                        ActiveTree[hashPtr] = endPtr;
                        next = endPtr;
                        endPtr += 2;
                    }

                    // navigate through the tree above the hash area
                    // add empty nodes as required
                    int bitMaskEnd = 1 << (Bits - 1);
                    for (int bitMask = ActiveBitMask; bitMask != bitMaskEnd; bitMask <<= 1)
                    {
                        // current bit is one, adjust the next pointer
                        if ((code & bitMask) != 0) next++;

                        // get the value at this location
                        int next1 = ActiveTree[next];

                        // the location was initialized before, continue to follow the path
                        if (next1 > 0)
                        {
                            next = next1;
                            continue;
                        }

                        // add free node from the area above the hash table and link it to the tree
                        ActiveTree[next] = endPtr;
                        next = endPtr;
                        endPtr += 2;
                    }

                    // we are now at a leaf point, add the symbol and the number of bits
                    if ((code & bitMaskEnd) != 0) next++;
                    ActiveTree[next] = ~(index << 4 | Bits);
                }

                // update initial code for the next code with the same number of bits
                _initialCode[Bits] += 1 << (16 - Bits);
            }
        }
    }
}