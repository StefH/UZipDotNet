/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	Adler32.cs
//	Adler 32 bit checksum.
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

namespace UZipDotNet.Support
{
    /// <summary>
    /// Accumulate Adler Checksum
    /// </summary>
    internal static class Adler32
    {
        const uint Adler32Base = 65521;

        public static uint Checksum(uint adlerValue, byte[] buffer, int pos, int len)
        {
            // split current Adler chksum into two 
            uint adlerLow = adlerValue & 0xFFFF;
            uint adlerHigh = adlerValue >> 16;

            while (len > 0)
            {
                // We can defer the modulo operation:
                // Under worst case the starting value of the two halves is 65520 = (AdlerBase - 1)
                // each new byte is maximum 255
                // The low half grows AdlerLow(n) = AdlerBase - 1 + n * 255
                // The high half grows AdlerHigh(n) = (n + 1)*(AdlerBase - 1) + n * (n + 1) * 255 / 2
                // The maximum n before overflow of 32 bit unsigned integer is 5552
                // it is the solution of the following quadratic equation
                // 255 * n * n + (2 * (AdlerBase - 1) + 255) * n + 2 * (AdlerBase - 1 - UInt32.MaxValue) = 0
                int n = len < 5552 ? len : 5552;
                len -= n;
                while (--n >= 0)
                {
                    adlerLow += buffer[pos++];
                    adlerHigh += adlerLow;
                }

                adlerLow %= Adler32Base;
                adlerHigh %= Adler32Base;
            }

            return ((adlerHigh << 16) | adlerLow);
        }
    }
}