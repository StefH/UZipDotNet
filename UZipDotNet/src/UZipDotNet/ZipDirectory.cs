/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	ZipDirectory.cs
//	Class designed to hold the ZIP archive central directory
//	information in memory.
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

namespace UZipDotNet
{
// ZIP directory file header information
// Full description of ZIP file headers is given in:
// PKWARE Zip file format specification 
//		File:    APPNOTE.TXT - .ZIP File Format Specification
//		Version: 6.3.2 
//		Revised: September 28, 2007
//		Copyright (c) 1989 - 2007 PKWARE Inc., All Rights Reserved.
// Wikipedia
//		http://en.wikipedia.org/wiki/ZIP_(file_format)
//
public enum FileSystem
	{
	DOS,
	Amiga,
	OpenVMS,
	Unix,
	VMCMS,
	Atari,
	OS2,
	Mac,
	ZSys,
	CPM,
	NTFS,
	MVS,
	VSE,
	Acorn,
	VFAT,
	ALTMVS,
	BeOS,
	Tandem,
	OS400,
	OSX,
	Unused,
	};

public class FileHeader : IComparable<FileHeader>
	{
	public String			FileName;		// file name with or without path
											// file name will not start with drive letter or server name
											// in other words D:\ or \ or \\server-name\ are invalid
	public Boolean			Path;			// File name contains path
	public UInt32			FilePos;		// file header position within the zip file
	public UInt16			FileTime;		// file time in dos format hhhhhmmmmmmsssss (seconds are in 2 sec increments)
	public UInt16			FileDate;		// file date in dos format yyyyyyymmmmddddd (years since 1980)
	public FileAttributes	FileAttr;		// file attributes (read only=1, hidden=2, system= 4, directory=8)
	public UInt32			FileSize;		// uncompressed file size (4GB max)
	public UInt32			CompSize;		// compressed file size. This nnumber does not include the file header.
	public UInt32			FileCRC32;		// uncompressed file CRC32 checksum
	public UInt16			CompMethod;		// compression method. This program supports 0-no compression and 8-deflate method
	public UInt16			BitFlags;		// This program expects zero (see PKWARE ZIP file format specifications section "J. Explanation of fields"
	public UInt16			Version;		// low byte=version, high byte=file system
											// This program was design to read and write ZIP files compatible with
											// Microsoft's Windows explorer Send To Compressed (Zipped) folder function.
											// Windows explorer sets the version to 20 and file system to 0.
											// According to PKWARE this corresponds to version 2.0.
											// File system: MS-DOS and OS/2 (FAT / VFAT / FAT32 file systems)

	////////////////////////////////////////////////////////////////////
	// Default constructor
	////////////////////////////////////////////////////////////////////

	public FileHeader(){}

	////////////////////////////////////////////////////////////////////
	// Constructor
	////////////////////////////////////////////////////////////////////

	public FileHeader
			(
			String			FileName,
			DateTime		LastWriteTime,
			FileAttributes	FileAttr,
			Int64			FilePos,
			Int64			FileSize
			)
		{
		// test file name (error if file starts with c:\ or \ or \\name\)
		if(FileName[0] == '\\' || FileName[1] == ':') throw new ApplicationException("Invalid file name");

		// file name				
		this.FileName = FileName;

		// file name has a path component
		this.Path = this.FileName.Contains("\\");

		// last mod file time
		this.FileTime = (UInt16) ((LastWriteTime.Hour << 11) | (LastWriteTime.Minute << 5) | (LastWriteTime.Second / 2));

		// last mod file date
		this.FileDate = (UInt16) (((LastWriteTime.Year - 1980) << 9) | (LastWriteTime.Month << 5) | LastWriteTime.Day);

		// file attributes
		this.FileAttr = FileAttr & (FileAttributes.Archive | FileAttributes.Directory |
			FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);

		// file position
		if(FilePos > 0xffffffff) throw new ApplicationException("No support for files over 4GB");
		this.FilePos = (UInt32) FilePos;

		// file length
		if(FileSize > 0xffffffff) throw new ApplicationException("No support for files over 4GB");
		this.FileSize = (UInt32) FileSize;

		// version
		this.Version = 20;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Compare To for sort by name
	////////////////////////////////////////////////////////////////////

	public Int32 CompareTo
			(
			FileHeader	Other
			)
		{
		if(this.Path != Other.Path) return(this.Path ? 1 : -1);
		return(String.Compare(this.FileName, Other.FileName));
		}

	////////////////////////////////////////////////////////////////////
	// Compare To for sort by postion
	////////////////////////////////////////////////////////////////////

	public static Int32 CompareByPosition
			(
			FileHeader	One,
			FileHeader	Other
			)
		{
		// this should not happen
		if(One.FilePos == Other.FilePos) return(0);

		// FilePos is UInt32 this is the reason for not using One.FilePos - Other.FilePos
		return(One.FilePos > Other.FilePos ? 1 : -1);
		}
	}
}
