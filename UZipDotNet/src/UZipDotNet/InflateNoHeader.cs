/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateNoHeader.cs
//	Class designed to decompress a file to another file. The input
//	file has no header or trailer information. This class is derived
//	from InflateMethod class. Not used in this project.
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
public class InflateNoHeader : InflateMethod
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

			// save file length
			ReadRemain = (UInt32) ReadStream.Length;
			ReadTotal = ReadRemain;

			// save name
			this.WriteFileName = WriteFileName;

			// create destination file
			WriteStream = new FileStream(WriteFileName, FileMode.Create, FileAccess.Write, FileShare.None);

			// convert stream to binary writer
			WriteFile = new BinaryWriter(WriteStream, Encoding.UTF8);

			// decompress the file
			Decompress();

			// close read file
			ReadFile.Close();
			ReadFile = null;

			// save file length
			WriteTotal = (UInt32) WriteStream.Length;

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
		ReadTotal += (UInt32) Len;
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
		WriteFile.Write(Buffer, Pos, Len);
		WriteTotal += (UInt32) Len;
		return;
		}
	}
}
