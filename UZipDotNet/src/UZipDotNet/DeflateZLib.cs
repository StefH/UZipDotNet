/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	DeflateZLib.cs
//	Class designed to compress a file to another file. The
//	compressed file will have ZLIB header and Adler32 checksum
//	at the end of the file. This class is derived from DeflateMethod
//	class. This class is used by the application to test the
//	compression software.
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
public class DeflateZLib : DeflateMethod
	{
	public  String[]		ExceptionStack;

	private String			ReadFileName;
	private FileStream		ReadStream;
	private BinaryReader	ReadFile;
	private UInt32			ReadRemain;
	private UInt32			ReadAdler32;

	private String			WriteFileName;
	private FileStream		WriteStream;
	private BinaryWriter	WriteFile;

	private static Int32[]	CompLevelTable  = { 0, 1, 1, 1, 2, 2, 2, 3, 3, 3 };

	////////////////////////////////////////////////////////////////////
	// Constructor
	////////////////////////////////////////////////////////////////////

	public DeflateZLib() : base(DefaultCompression) {}
	public DeflateZLib(Int32 CompLevel) : base(CompLevel) {}
		
	////////////////////////////////////////////////////////////////////
	// Decompress one file
	////////////////////////////////////////////////////////////////////
	
	public Boolean Compress
			(
			String		ReadFileName,
			String		WriteFileName
			)
		{
		try
			{
			// save read file name
			this.ReadFileName = ReadFileName;

			// open source file for reading
			ReadStream = new FileStream(ReadFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
		
			// convert stream to binary reader
			ReadFile = new BinaryReader(ReadStream, Encoding.UTF8);

			// file is too long
			if(ReadStream.Length > (Int64) 0xffffffff) throw new ApplicationException("No support for files over 4GB");

			// uncompressed file length
			ReadRemain = (UInt32) ReadStream.Length;

			// reset adler32 checksum
			ReadAdler32 = 1;

			// save name
			this.WriteFileName = WriteFileName;

			// create destination file
			WriteStream = new FileStream(WriteFileName, FileMode.Create, FileAccess.Write, FileShare.None);

			// convert stream to binary writer
			WriteFile = new BinaryWriter(WriteStream, Encoding.UTF8);

			// Header is made out of 16 bits [iiiicccclldxxxxx]
			// iiii is compression information. It is WindowBit - 8 in this case 7. iiii = 0111
			// cccc is compression method. Deflate (8 dec) or Store (0 dec)
			// The first byte is 0x78 for deflate and 0x70 for store
			// ll is compression level 0 to 3 (CompLevelTable translates between user level of 0-9 to header level of 0-3)
			// d is preset dictionary. The preset dictionary is not supported by this program. d is always 0
			// xxx is 5 bit check sum
			Int32 Header = (0x78 << 8) | (CompLevelTable[CompressionLevel] << 6);
			Header += 31 - (Header % 31);

			// write two bytes in most significant byte first
			WriteFile.Write((Byte) (Header >> 8));
			WriteFile.Write((Byte) Header);

			// compress the file
			Compress();

			// compress function was stored
			if(CompFunction == CompFunc.Stored)
				{
				// set file position to header
				WriteStream.Position = 0;

				// change compression method from Deflate(8) to Stored(0)
				Header = (0x70 << 8) | (CompLevelTable[CompressionLevel] << 6);
				Header += 31 - (Header % 31);

				// write two bytes in most significant byte first
				WriteFile.Write((Byte) (Header >> 8));
				WriteFile.Write((Byte) Header);

				// restore file position
				WriteStream.Position = WriteStream.Length;
				}

			// ZLib checksum is Adler32 write it big endian order, high byte first
			WriteFile.Write((Byte) (ReadAdler32 >> 24));
			WriteFile.Write((Byte) (ReadAdler32 >> 16));
			WriteFile.Write((Byte) (ReadAdler32 >> 8));
			WriteFile.Write((Byte) ReadAdler32);

			// close read file
			ReadFile.Close();
			ReadFile = null;

			// close write file
			WriteFile.Close();
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
				ReadFile.Close();
				ReadFile = null;
				}

			// close the write file if it is open
			if(WriteFile != null)
				{
				WriteFile.Close();
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
		ReadFile.Read(Buffer, Pos, Len);
		ReadAdler32 = Adler32.Checksum(ReadAdler32, Buffer, Pos,  Len);
		return(Len);
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
		WriteFile.Write(Buffer, Pos, Len);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Rewind Streams
	////////////////////////////////////////////////////////////////////

	public override void RewindStreams()
		{
		// reposition stream file pointer to start of file
		ReadStream.Position = 0;

		// uncompressed file length
		ReadRemain = (UInt32) ReadStream.Length;

		// reset adler32 checksum
		ReadAdler32 = 1;

		// truncate file keeping the zlib header
		WriteStream.SetLength(2);

		// reposition write stream to the new end of file
		WriteStream.Position = 2;
		return;
		}
	}
}
