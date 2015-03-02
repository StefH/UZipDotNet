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

using System;

namespace UZipDotNet
{
public static class Adler32
	{
	/////////////////////////////////////////////////////////////////////
	// Accumulate Adler Checksum
	/////////////////////////////////////////////////////////////////////

	public static UInt32 Checksum
			(
			UInt32		AdlerValue,
			Byte[]		Buffer,
			Int32		Pos,
			Int32		Len
			)
		{
		const UInt32 Adler32Base = 65521;

		// split current Adler chksum into two 
		UInt32 AdlerLow = AdlerValue & 0xFFFF;
		UInt32 AdlerHigh = AdlerValue >> 16;

		while(Len > 0) 
			{
			// We can defer the modulo operation:
			// Under worst case the starting value of the two halves is 65520 = (AdlerBase - 1)
			// each new byte is maximum 255
			// The low half grows AdlerLow(n) = AdlerBase - 1 + n * 255
			// The high half grows AdlerHigh(n) = (n + 1)*(AdlerBase - 1) + n * (n + 1) * 255 / 2
			// The maximum n before overflow of 32 bit unsigned integer is 5552
			// it is the solution of the following quadratic equation
			// 255 * n * n + (2 * (AdlerBase - 1) + 255) * n + 2 * (AdlerBase - 1 - UInt32.MaxValue) = 0
			Int32 n = Len < 5552 ? Len : 5552;
			Len -= n;
			while(--n >= 0) 
				{
				AdlerLow += (UInt32) Buffer[Pos++];
				AdlerHigh += AdlerLow;
				}
			AdlerLow %= Adler32Base;
			AdlerHigh %= Adler32Base;
			}
		return((AdlerHigh << 16) | AdlerLow);
		}

	}
}
