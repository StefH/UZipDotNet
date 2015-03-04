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
using UZipDotNet.Support;


namespace UZipDotNet
{
    public delegate void WriteBits(int bits, int count);

    /// <summary>
    /// This class is used to calculate the Huffman coding based on frequency
    /// </summary>
    public struct FreqNode : IComparable<FreqNode>
    {
        public int Code;
        public int Freq;
        public int Child;	// left child is Child - 1

        public int CompareTo(FreqNode other)
        {
            return (Freq - other.Freq);
        }
    }

    /////////////////////////////////////////////////////////////////////
    /////////////////////////////////////////////////////////////////////

    public class DeflateTree
    {
        private const int RepeatSymbol_3_6 = 16;
        private const int RepeatSymbol_3_10 = 17;
        private const int RepeatSymbol_11_138 = 18;

        private static readonly int[] BitLengthOrder =
        {
            RepeatSymbol_3_6, RepeatSymbol_3_10, RepeatSymbol_11_138, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
        };

        public int[] CodeFreq;			// number of times a given code was used
        public int MaxUsedCodes;		// the number of highest code used plus one

        private readonly FreqNode[] _freqTree;  // frequency tree
        private int _freqTreeEnd;		// number of nodes in the frequency tree
        private int _maxUsedBitLen;		// maximum used bit length

        private ushort[] _codes;				// dynamic or static variable length bit code
        private byte[] _codeLength;			    // dynamic or static code length
        private readonly int[] _bitLengthFreq;  // frequency of code length (how many times code length is used)
        private readonly int[] _firstCode;      // first code of a group of equal code length
        private readonly ushort[] _saveCodes;   // dynamic variable length bit code
        private readonly byte[] _saveCodeLength;    // dynamic code length

        private readonly WriteBits _writeBits;  // write bits method (void WriteBits(Int32 Bits, Int32 Count))
        private readonly int _minCodes;			// minimum number of codes
        private readonly int _maxCodes;			// maximum number of codes
        private readonly int _maxBitLength;		// maximum length in bits of a code

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateTree"/> class.
        /// Literal Tree		MinCodes = 257, MaxCodes = 286, MaxBitLength = 15
        /// Distance Tree		MinCodes =   2, MaxCodes =  30, MaxBitLength = 15
        /// Bit Length Tree		MinCodes =   4, MaxCodes =  19, MaxBitLength = 7
        /// </summary>
        /// <param name="writeBits">The write bits.</param>
        /// <param name="minCodes">The minimum codes.</param>
        /// <param name="maxCodes">The maximum codes.</param>
        /// <param name="maxBitLength">Maximum length of the bit.</param>
        public DeflateTree(WriteBits writeBits, int minCodes, int maxCodes, int maxBitLength)
        {
            // write bits method
            _writeBits = writeBits;

            // save minimum codes, maximum codes, and maximum bit length
            _minCodes = minCodes;
            _maxCodes = maxCodes;
            _maxBitLength = maxBitLength;

            // allocate arrays
            CodeFreq = new int[maxCodes];
            _saveCodeLength = new byte[maxCodes];
            _saveCodes = new ushort[maxCodes];
            _freqTree = new FreqNode[2 * maxCodes - 1];
            _bitLengthFreq = new int[maxBitLength];
            _firstCode = new int[maxBitLength];

            // clear arrays
            Reset();
        }

        /// <summary>
        ///  Reset class after each block
        /// </summary>
        public void Reset()
        {
            // The deflate method will overwrite the CodeLength and Codes arrays
            // if static block was selected. We restore it to the dynamic arrays
            _codeLength = _saveCodeLength;
            _codes = _saveCodes;

            // clear code frequency array
            Array.Clear(CodeFreq, 0, CodeFreq.Length);
        }

        /// <summary>
        /// Write symbol to output stream
        /// </summary>
        /// <param name="code">The code.</param>
        public void WriteSymbol(int code)
        {
            _writeBits(_codes[code], _codeLength[code]);
        }

        ////////////////////////////////////////////////////////////////////
        // For static trees overwrite the Codes and CodeLength arrays
        // the reset function will restore the dynamic arrays
        ////////////////////////////////////////////////////////////////////

        public void SetStaticCodes(ushort[] staticCodes, byte[] staticLength)
        {
            _codes = staticCodes;
            _codeLength = staticLength;
        }

        /// <summary>
        /// BBuild Huffman tree
        /// </summary>
        public void BuildTree()
        {
            // find the highest code in use
            for (MaxUsedCodes = _maxCodes - 1; MaxUsedCodes >= _minCodes && CodeFreq[MaxUsedCodes] == 0; MaxUsedCodes--)
            {
            }

            MaxUsedCodes++;

            // clear frequency tree
            Array.Clear(_freqTree, 0, _freqTree.Length);

            // initialize Huffman frequency tree with leaf nodes
            _freqTreeEnd = 0;
            for (int code = 0; code < MaxUsedCodes; code++)
            {
                // include used codes. ignore codes with zero frequency
                if (CodeFreq[code] != 0)
                {
                    _freqTree[_freqTreeEnd].Code = code;
                    _freqTree[_freqTreeEnd++].Freq = CodeFreq[code];
                }
            }

            // there are less than 2 codes in use
            if (_freqTreeEnd < 2)
            {
                // zero codes in use
                if (_freqTreeEnd == 0)
                {
                    // create artificial node of code 0 with zero frequency
                    _freqTree[0].Code = 0;
                    _freqTree[0].Freq = 0;
                    _freqTreeEnd++;
                }
                // one code in use
                // create artificial second node
                // if the first code is 0 the second is 1, if the first is non zero make the second zero
                // the frequency is zero (not used)
                _freqTree[1].Code = _freqTree[0].Code == 0 ? 1 : 0;
                _freqTree[1].Freq = 0;
                _freqTreeEnd++;
            }

            // sort it by frequency low to high
            Array.Sort(_freqTree, 0, _freqTreeEnd);

            // clear code length
            Array.Clear(_codeLength, 0, _codeLength.Length);

            // loop in case of bit length exceeding the maximum
            // for literals and distance it is 15 and for bit length it is 7
            for (; ; )
            {
                // build Huffman tree by combining pairs of nodes
                CombinePairs();

                // calculate length in bits of each code
                _maxUsedBitLen = 0;
                BuildCodeTree(_freqTreeEnd - 1, 0);
                if (_maxUsedBitLen <= _maxBitLength) break;

                // adjust the lowest frequency nodes such that we get a flater tree
                AdjustNodes();
            }
        }

        /// <summary>
        /// Combine pairs of lower frequency node to a parent nodeCombines the pairs.
        /// </summary>
        private void CombinePairs()
        {
            // combine pairs of nodes
            for (int ptr = 2; ptr < _freqTreeEnd; ptr += 2)
            {
                // combined frequency of current pair
                int combFreq = _freqTree[ptr - 2].Freq + _freqTree[ptr - 1].Freq;

                // right pointer
                int rightPtr = _freqTreeEnd;

                // set left pointer to starting point
                int leftPtr = ptr;

                // perform binary search
                int insertPtr;
                int cmp;
                for (; ; )
                {
                    // middle pointer
                    insertPtr = (leftPtr + rightPtr) / 2;

                    // compare
                    cmp = combFreq - _freqTree[insertPtr].Freq;

                    // range is one or exact match
                    if (insertPtr == leftPtr || cmp == 0) break;

                    // move search to the right
                    if (cmp > 0) leftPtr = insertPtr;

                    // move search to the left
                    else rightPtr = insertPtr;
                }

                // exact compare (point to end of a group of equal frequencies)
                // positive compare (move to the right as long as it is equal)
                if (cmp >= 0)
                {
                    for (insertPtr++; insertPtr < _freqTreeEnd && _freqTree[insertPtr].Freq == combFreq; insertPtr++)
                    {
                    }
                }

                // open a hole at insert point
                Array.Copy(_freqTree, insertPtr, _freqTree, insertPtr + 1, _freqTreeEnd - insertPtr);
                _freqTreeEnd++;

                // add node with combined freqency
                _freqTree[insertPtr].Code = -1;
                _freqTree[insertPtr].Freq = combFreq;
                _freqTree[insertPtr].Child = ptr - 1;
            }

            // add final root node with combined freqency
            _freqTree[_freqTreeEnd].Code = -1;
            _freqTree[_freqTreeEnd].Freq = _freqTree[_freqTreeEnd - 2].Freq + _freqTree[_freqTreeEnd - 1].Freq;
            _freqTree[_freqTreeEnd].Child = _freqTreeEnd - 1;
            _freqTreeEnd++;
        }

        /// <summary>
        /// Scan the tree using recursive routine and set the number of bits to represent the code
        /// </summary>
        /// <param name="ptr">The PTR.</param>
        /// <param name="bitCount">The bit count.</param>
        private void BuildCodeTree(int ptr, int bitCount)
        {
            FreqNode fn = _freqTree[ptr];

            // we are at a parent node
            if (fn.Code < 0)
            {
                // go to children on the left side of this node
                BuildCodeTree(fn.Child - 1, bitCount + 1);

                // go to children on the right side of this node
                BuildCodeTree(fn.Child, bitCount + 1);
            }

            // we are at child node
            else
            {
                if (bitCount > _maxUsedBitLen) _maxUsedBitLen = bitCount;
                _codeLength[fn.Code] = (byte)bitCount;
            }
        }

        /// <summary>
        /// AAdjust low frequency nodes to make the tree more symetric
        /// </summary>
        /// <exception cref="System.Exception">Adjust nodes failed</exception>
        public void AdjustNodes()
        {
            // remove all non leaf nodes
            int ptr;
            for (ptr = 0; ptr < _freqTreeEnd; ptr++)
            {
                if (_freqTree[ptr].Code >= 0) continue;
                _freqTreeEnd--;
                Array.Copy(_freqTree, ptr + 1, _freqTree, ptr, _freqTreeEnd - ptr);
                ptr--;
            }

            // look for first change in frequency
            int freq = _freqTree[0].Freq;
            for (ptr = 1; ptr < _freqTreeEnd && _freqTree[ptr].Freq == freq; ptr++)
            {
            }
            if (ptr == _freqTreeEnd) throw new Exception("Adjust nodes failed");

            // adjust the frequency of least frequent nodes
            freq = _freqTree[ptr].Freq;
            for (ptr--; ptr >= 0; ptr--) _freqTree[ptr].Freq = freq;
        }

        /// <summary>
        /// Build code array from bit length frequency distribution
        /// </summary>
        /// <exception cref="System.Exception">Inconsistent bl_counts!</exception>
        public void BuildCodes()
        {
            // build bit length frequency array
            Array.Clear(_bitLengthFreq, 0, _bitLengthFreq.Length);
            for (int code = 0; code < MaxUsedCodes; code++)
            {
                if (_codeLength[code] != 0) _bitLengthFreq[_codeLength[code] - 1]++;
            }

            // build array of initial code for each block of codes
            int initCode = 0;
            for (int bitNo = 0; bitNo < _maxBitLength; bitNo++)
            {
                // save the initial code of the block
                _firstCode[bitNo] = initCode;

                // advance the code by the frequancy times 2 to the power of 15 less bit length
                initCode += _bitLengthFreq[bitNo] << (15 - bitNo);
            }

            // it must add up to 2 ** 16
            if (initCode != 65536) throw new Exception("Inconsistent bl_counts!");

            // now fill up the entire code array
            Array.Clear(_codes, 0, _codes.Length);
            for (int index = 0; index < MaxUsedCodes; index++)
            {
                int bits = _codeLength[index];
                if (bits > 0)
                {
                    _codes[index] = BitReverse.Reverse16Bits(_firstCode[bits - 1]);
                    _firstCode[bits - 1] += 1 << (16 - bits);
                }
            }
        }

        /// <summary>
        /// Calculate encoded data block lengthGets the length of the encoded.
        /// </summary>
        /// <returns></returns>
        public int GetEncodedLength()
        {
            int len = 0;
            for (int index = 0; index < MaxUsedCodes; index++) len += CodeFreq[index] * _codeLength[index];
            return (len);
        }

        /// <summary>
        /// Bit length tree only. Calculate total length
        /// </summary>
        /// <returns></returns>
        public int MaxUsedCodesBitLength()
        {
            // calculate length in bits of bit length tree
            for (int index = 18; index >= 4; index--)
            {
                if (_codeLength[BitLengthOrder[index]] > 0)
                {
                    return (index + 1);
                }
            }
            return 4;
        }

        /// <summary>
        /// Bit length tree only. Calculate total length
        /// </summary>
        /// <param name="blTreeCodes">The bl tree codes.</param>
        public void WriteBitLengthCodeLength(Int32 blTreeCodes)
        {
            // send to output stream the bit length array
            for (int rank = 0; rank < blTreeCodes; rank++)
                _writeBits(_codeLength[BitLengthOrder[rank]], 3);
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
        public Int32 CalcBLFreq(DeflateTree blTree)
        {
            int extraBits = 0;
            for (int code = 0; code < MaxUsedCodes; )
            {
                // current code length
                int codeLen = _codeLength[code];

                // scan the code array for equal code lengths
                int count = 1;
                for (code++; code < MaxUsedCodes && codeLen == _codeLength[code]; code++) count++;

                // less than three equal code length
                if (count < 3)
                {
                    blTree.CodeFreq[codeLen] += (short)count;
                    continue;
                }

                // code length is other than zero
                if (codeLen != 0)
                {
                    // add one to the frequency of the code itself
                    blTree.CodeFreq[codeLen]++;

                    // reduce the count by one
                    count--;

                    // for every full block of 6 repeats add one REP_3_6 code
                    blTree.CodeFreq[RepeatSymbol_3_6] += count / 6;
                    extraBits += 2 * (count / 6);

                    // get the remainder
                    if ((count = count % 6) == 0) continue;

                    // remainder is less than 3
                    if (count < 3)
                    {
                        blTree.CodeFreq[codeLen] += count;
                        continue;
                    }

                    // remainder is beween 3 to 5
                    blTree.CodeFreq[RepeatSymbol_3_6]++;
                    extraBits += 2;
                    continue;
                }

                // code length is zero and count is 3 or more
                // for every full block of 138 repeats add one REP_11_138 code
                blTree.CodeFreq[RepeatSymbol_11_138] += count / 138;
                extraBits += 7 * (count / 138);

                // get the remainder
                if ((count = count % 138) == 0) continue;

                // remainder is less than 3
                if (count < 3)
                {
                    blTree.CodeFreq[codeLen] += count;
                    continue;
                }

                // remainder is beween 3 to 10
                if (count <= 10)
                {
                    blTree.CodeFreq[RepeatSymbol_3_10]++;
                    extraBits += 3;
                    continue;
                }

                // remainder is beween 11 to 137
                blTree.CodeFreq[RepeatSymbol_11_138]++;
                extraBits += 7;
            }
            return (extraBits);
        }

        /// <summary>
        /// Write the tree to output file
        /// </summary>
        /// <param name="blTree">The bl tree.</param>
        public void WriteTree(DeflateTree blTree)
        {
            for (int code = 0; code < MaxUsedCodes; )
            {
                // current code length
                int codeLen = _codeLength[code];

                // scan the code array for equal code lengths
                int count = 1;
                for (code++; code < MaxUsedCodes && codeLen == _codeLength[code]; code++) count++;

                // less than three equal code length
                if (count < 3)
                {
                    // write the first code length to output file
                    blTree.WriteSymbol(codeLen);

                    // write the second code length to output file
                    if (count > 1) blTree.WriteSymbol(codeLen);
                    continue;
                }

                // Used code. Code length is other than zero.
                if (codeLen != 0)
                {
                    // write the code length to output file
                    blTree.WriteSymbol(codeLen);

                    // reduce the count by one
                    count--;

                    // for every full block of 6 repeats add one REP_3_6 code and two bits of 1 (11)
                    for (int index = count / 6; index > 0; index--)
                    {
                        blTree.WriteSymbol(RepeatSymbol_3_6);
                        _writeBits(6 - 3, 2);
                    }

                    // get the remainder
                    if ((count = count % 6) == 0) continue;

                    // remainder is less than 3
                    if (count < 3)
                    {
                        // write the code length to output file
                        blTree.WriteSymbol(codeLen);
                        if (count > 1) blTree.WriteSymbol(codeLen);
                        continue;
                    }

                    // remainder is beween 3 to 5
                    blTree.WriteSymbol(RepeatSymbol_3_6);
                    _writeBits(count - 3, 2);
                    continue;
                }

                // code length is zero and count is 3 or more
                // for every full block of 138 repeats add one REP_11_138 code plus 7 bits of 1
                for (int index = count / 138; index > 0; index--)
                {
                    blTree.WriteSymbol(RepeatSymbol_11_138);
                    _writeBits(138 - 11, 7);
                }

                // get the remainder
                if ((count = count % 138) == 0) continue;

                // remainder is less than 3
                if (count < 3)
                {
                    // write the code length to output file
                    blTree.WriteSymbol(codeLen);
                    if (count > 1) blTree.WriteSymbol(codeLen);
                    continue;
                }

                // remainder is beween 3 to 10
                if (count <= 10)
                {
                    blTree.WriteSymbol(RepeatSymbol_3_10);
                    _writeBits(count - 3, 3);
                    continue;
                }

                // remainder is beween 11 to 137
                blTree.WriteSymbol(RepeatSymbol_11_138);
                _writeBits(count - 11, 7);
            }
        }
    }
}