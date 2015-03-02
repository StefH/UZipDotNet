/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateZLib.cs
//	Class designed to decompress a file to another file. The
//	decompressed file will have ZLIB header and Adler32 checksum
//	at the end of the file. This class is derived from InflateMethod
//	class. This class is used by the application to test the
//	decompression software.
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
public class InflateZLib : InflateMethod
	{
	public  String[]		ExceptionStack;
	public  UInt32			ReadTotal;
	public  UInt32			WriteTotal;

	private String			ReadFileName;
	private FileStream		ReadStream;
	private BinaryReader	ReadFile;
	private UInt32			ReadRemain;

	private String			WriteFileName;
	private FileStream		WriteStream;
	private BinaryWriter	WriteFile;
	private UInt32			WriteAdler32;

	////////////////////////////////////////////////////////////////////
	// Decompress one file
	////////////////////////////////////////////////////////////////////
	
	public Boolean Decompress
			(
			String		ReadFileName,
			String		WriteFileName
			)
		{
		try
			{
			// save name
			this.ReadFileName = ReadFileName;

			// open source file for reading
			ReadStream = new FileStream(ReadFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
		
			// convert stream to binary reader
			ReadFile = new BinaryReader(ReadStream, Encoding.UTF8);

			// file is too long
			if(ReadStream.Length > (Int64) 0xffffffff) throw new ApplicationException("No support for files over 4GB");

			// compressed part of the file
			// we subtract 2 bytes for header and 4 bytes for adler32 checksum
			ReadRemain = (UInt32) ReadStream.Length - 6;
			ReadTotal = ReadRemain;

			// get ZLib header
			Int32 Header = (ReadFile.ReadByte() << 8) | ReadFile.ReadByte();

			// test header: chksum, compression method must be deflated, no support for external dictionary
			if(Header % 31 != 0 || (Header & 0xf00) != 0x800 && (Header & 0xf00) != 0 || (Header & 0x20) != 0)
				throw new ApplicationException("ZLIB file header is in error");

			// save name
			this.WriteFileName = WriteFileName;

			// create destination file
			WriteStream = new FileStream(WriteFileName, FileMode.Create, FileAccess.Write, FileShare.None);

			// convert stream to binary writer
			WriteFile = new BinaryWriter(WriteStream, Encoding.UTF8);

			// reset adler32 checksum
			WriteAdler32 = 1;

			// decompress the file
			if((Header & 0xf00) == 0x800)
				{
				Decompress();
				}
			else
				{
				NoCompression();
				}

			// ZLib checksum is Adler32
			if((((UInt32) ReadFile.ReadByte() << 24) | ((UInt32) ReadFile.ReadByte() << 16) |
				((UInt32) ReadFile.ReadByte() << 8) | ((UInt32) ReadFile.ReadByte())) != WriteAdler32)
					throw new ApplicationException("ZLIB file Adler32 test failed");

			// close read file
			ReadFile.Dispose();
			ReadFile = null;

			// save file length
			WriteTotal = (UInt32) WriteStream.Length;

			// close write file
			WriteFile.Dispose();
			WriteFile = null;

			// successful exit
			return(false);
			}

		// make sure read and write files are closed
		catch(Exception Ex)
			{
			// close the read file if it is open
			if(ReadFile != null)
				{
				ReadFile.Dispose();
				ReadFile = null;
				}

			// close the write file if it is open
			if(WriteFile != null)
				{
				WriteFile.Dispose();
				WriteFile = null;
				}

			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}
		}

	////////////////////////////////////////////////////////////////////
	// Read Bytes Routine
	////////////////////////////////////////////////////////////////////

	public override Int32 ReadBytes
			(
			Byte[]			Buffer,
			Int32			Pos,
			Int32			Len,
			out Boolean		EndOfFile
			)
		{
		Len = Len > ReadRemain ? (Int32) ReadRemain : Len;
		ReadRemain -= (UInt32) Len;
		EndOfFile = ReadRemain == 0;
		return(ReadFile.Read(Buffer, Pos, Len));
		}
	
	////////////////////////////////////////////////////////////////////
	// Write Bytes Routine
	////////////////////////////////////////////////////////////////////

	public override void WriteBytes
			(
			Byte[]		Buffer,
			Int32		Pos,
			Int32		Len
			)
		{
		WriteAdler32 = Adler32.Checksum(WriteAdler32, Buffer, Pos,  Len);
		WriteFile.Write(Buffer, Pos, Len);
		return;
		}
	}
}
