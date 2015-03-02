/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	Trace.cs
//	Class designed to save tracing information to
//	UZipDotNetTrace.txt file.
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
/////////////////////////////////////////////////////////////////////
// Trace Class
/////////////////////////////////////////////////////////////////////

static public class Trace
	{
	// variables
	static private String FileName;
	static private Int32 FileSize = 1024*1024;

	/////////////////////////////////////////////////////////////////////
	// Open trace file
	/////////////////////////////////////////////////////////////////////

	static public void Open
			(
			String	argFileName
			)
		{
		// find file full name
		FileInfo TraceFileInfo = new FileInfo(argFileName);

		// save full file name
		FileName = TraceFileInfo.FullName;

		// exit
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// write to trace file
	/////////////////////////////////////////////////////////////////////

	static public void Write
			(
			String Message
			)
		{
		// test file length
		TestSize();

		// open or create the trace file
		StreamWriter TraceFile = new StreamWriter(FileName, true);

		// get date and time
		String DateTimeStr = String.Format("{0:yyyy}/{0:MM}/{0:dd} {0:HH}:{0:mm}:{0:ss} ", DateTime.Now);

		// write date and time
		TraceFile.Write(DateTimeStr);

		// write message
		TraceFile.WriteLine(Message);

		// close the file
		TraceFile.Close();

		// exit
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// test file size
	/////////////////////////////////////////////////////////////////////

	static private void TestSize()
		{
		Int32
			j,
			NewFileLength;

		Int64
			FilePtr;

		// create file info class
		FileInfo TraceFileInfo = new FileInfo(FileName);

		// test length
		if(TraceFileInfo.Exists == false || TraceFileInfo.Length <= FileSize) return;

		// create file info class
		FileStream TraceFile = new FileStream(FileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		// set file pointer to 25% of file length
		FilePtr = TraceFile.Length / 4;

		// new file length
		NewFileLength = (int) (TraceFile.Length - FilePtr);

		// get a buffer
		byte [] Buffer = new byte[NewFileLength];

		// seek to 25% length
		TraceFile.Seek(FilePtr, SeekOrigin.Begin);

		// read file to the end
		TraceFile.Read(Buffer, 0, NewFileLength);

		// search for end of line
		for(j = 0; j < NewFileLength && Buffer[j] != '\n'; j++);
		j++;
		if(j >= NewFileLength) j = 0;

		// seek to start of file
		TraceFile.Seek(0, SeekOrigin.Begin);

		// write top part of file
		TraceFile.Write(Buffer, j, NewFileLength - j);

		// truncate the file
		TraceFile.SetLength(NewFileLength - j);

		// close the file
		TraceFile.Close();

		// exit
		return;
		}
	}
}