/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	InflateZipFile.cs
//	Class designed to read an existing ZIP archive. The class has
//	a method to extract individual files from the ZIP archive.
//	This class is derived from InflateMethod.
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
using System.Collections.Generic;

namespace UZipDotNet
{
public class InflateZipFile : InflateMethod
	{
	public  String			ArchiveName{get{return(ReadFileName);}}
	public  static Boolean	IsOpen(InflateZipFile Inflate) {return(Inflate != null && Inflate.ReadFile != null);}
	public  Boolean			IsEmpty{get{return(ReadFile == null || ZipDir == null || ZipDir.Count == 0);}}
	public  String[]		ExceptionStack;
	public  List<FileHeader> ZipDir;
	public  Int64			ZipDirPosition;
	public  UInt32			ReadTotal;
	public  UInt32			WriteTotal;

	private String			ReadFileName;
	private FileStream		ReadStream;
	private BinaryReader	ReadFile;
	private UInt32			ReadRemain;

	private String			WriteFileName;
	private FileStream		WriteStream;
	private BinaryWriter	WriteFile;
	private UInt32			WriteCRC32;

	// active ZIP file info
	private FileHeader		FileHeaderInfo;
	private Boolean			FileTimeAvailable;
	private DateTime		FileModifyTime;
	private DateTime		FileAccessTime;
	private DateTime		FileCreateTime;

	/////////////////////////////////////////////////////////////////////
	// Extract All
	/////////////////////////////////////////////////////////////////////

	public Boolean ExtractAll
			(
			String		ReadFileName,
			String		RootPathName,
			Boolean		OverWrite
			)
		{
		// open the zip archive
		if(OpenZipFile(ReadFileName)) return(true);

		// if root path is null, change to empty
		if(RootPathName == null) RootPathName = String.Empty;

		// append backslash
		else if(RootPathName.Length > 0 && !RootPathName.EndsWith("\\")) RootPathName += "\\";

		// decompress all files
		foreach(FileHeader FH in ZipDir)
			{
			// if overwrite is false, test if file exists
			if(!OverWrite && File.Exists(RootPathName + FH.FileName)) continue;

			// decompress one file
			if(DecompressZipFile(FH, RootPathName, null, true, OverWrite)) return(true);
			}

		// successful exit
		return(false);
		}

	/////////////////////////////////////////////////////////////////////
	// Open ZIP file and read file directory
	/////////////////////////////////////////////////////////////////////

	public Boolean OpenZipFile
			(
			String	ReadFileName
			)
		{
		// trap errors
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

			// read zip directory
			ReadZipFileDirectory();

			// successful exit
			return(false);
			}

		// make sure read file is closed
		catch(Exception Ex)
			{
			// close the read file if it is open
			CloseZipFile();

			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}
		}

	/////////////////////////////////////////////////////////////////////
	// Close ZIP file
	/////////////////////////////////////////////////////////////////////

	public void CloseZipFile()
		{
		// close the read file if it is open
		if(ReadFile != null)
			{
			ReadFile.Dispose();
			ReadFile = null;
			}

		// remove directory
		if(ZipDir != null)
			{
			ZipDir.Clear();
			ZipDir = null;
			}
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Decompress ZIP file
	////////////////////////////////////////////////////////////////////
	
	public Boolean DecompressZipFile
			(
			FileHeader		FH,
			String			RootPathName,		// save the decompressed file in this directory
			String			NewFileName,		// if this file name is given it overwrite the name in the FH structure
			Boolean			CreatePath,
			Boolean			OverWrite
			)
		{
		try
			{
			// save file header
			FileHeaderInfo = FH;

			// read file header for this file and compare it to the directory information
			ReadFileHeader(FileHeaderInfo.FilePos);

			// compressed length
			ReadRemain = FileHeaderInfo.CompSize;
			ReadTotal = ReadRemain;

			// build write file name
			// Root name (optional) plus either original name or a new name
			WriteFileName = (String.IsNullOrEmpty(RootPathName) ? String.Empty :
				(RootPathName.EndsWith("\\") ? RootPathName : RootPathName + "\\")) +
				(String.IsNullOrEmpty(NewFileName) ? FH.FileName : NewFileName);

			// test if write file name has a path component
			Int32 Ptr = WriteFileName.LastIndexOf('\\');
			if(Ptr >= 0)
				{
				// make sure directory exists
				if(!Directory.Exists(WriteFileName.Substring(0, Ptr)))
					{
					// make a new folder
					if(CreatePath) Directory.CreateDirectory(WriteFileName.Substring(0, Ptr));

					// error
					else throw new ApplicationException("Extract file failed. Invalid directory path");
					}
				}

			// create destination file
			WriteStream = new FileStream(WriteFileName, OverWrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);

			// convert stream to binary writer
			WriteFile = new BinaryWriter(WriteStream, Encoding.UTF8);

			// reset crc32 checksum
			WriteCRC32 = 0;

			// switch based on compression method
			switch(FileHeaderInfo.CompMethod)
				{
				// no compression
				case 0:
					NoCompression();
					break;

				// deflate compress method
				case 8:
					// decompress file
					Decompress();
					break;

				// not supported
				default:
					throw new ApplicationException("Unsupported compression method");
				}

			// Zip file checksum is CRC32
			if(FileHeaderInfo.FileCRC32 != WriteCRC32) throw new ApplicationException("ZIP file CRC test failed");

			// save file length
			WriteTotal = (UInt32) WriteStream.Length;

			// close write file
			WriteFile.Dispose();
			WriteFile = null;

			// if file times are available set the file time
			if(FileTimeAvailable)
				{
				File.SetCreationTime(WriteFileName, FileCreateTime);
				File.SetLastWriteTime(WriteFileName, FileModifyTime);
				File.SetLastAccessTime(WriteFileName, FileAccessTime);
				}
			else
				{
				// convert dos file date and time to DateTime format
				DateTime FileDosTime = new DateTime(1980 + ((FileHeaderInfo.FileDate >> 9) & 0x7f),
					(FileHeaderInfo.FileDate >> 5) & 0xf, FileHeaderInfo.FileDate & 0x1f,
					(FileHeaderInfo.FileTime >> 11) & 0x1f, (FileHeaderInfo.FileTime >> 5) & 0x3f, 2 * (FileHeaderInfo.FileTime & 0x1f));
				File.SetCreationTime(WriteFileName, FileDosTime);
				File.SetLastWriteTime(WriteFileName, FileDosTime);
				File.SetLastAccessTime(WriteFileName, FileDosTime);
				}

			// set file attribute attributes
			if(FileHeaderInfo.FileAttr != 0) File.SetAttributes(WriteFileName, FileHeaderInfo.FileAttr);

			// successful exit
			return(false);
			}

		// make sure write file is closed
		catch(Exception Ex)
			{
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

	/////////////////////////////////////////////////////////////////////
	// Read Len bytes and convert to String
	/////////////////////////////////////////////////////////////////////

	private String GetFileName
			(
			Int32	Len
			)
		{
		// empty string
		if(Len == 0) return(String.Empty);

		// convert byts to char and build a string
		StringBuilder FileName = new StringBuilder(Len);
		FileName.Length = Len;
		for(Int32 Ptr = 0; Ptr < Len; Ptr++) FileName[Ptr] = (Char) ReadFile.ReadByte();
		return(FileName.ToString().Replace('/', '\\'));
		}

	////////////////////////////////////////////////////////////////////
	// Read ZIP file directory
	////////////////////////////////////////////////////////////////////
	
	private void ReadZipFileDirectory()
		{
		//	End of central directory record:
		//	Pos		Len
		//	0		4		End of central directory signature = 0x06054b50
		//	4		2		Number of this disk
		//	6		2		Disk where central directory starts
		//	8		2		Number of central directory records on this disk
		//	10		2		Total number of central directory records
		//	12		4		Size of central directory (bytes)
		//	16		4		Offset of start of central directory, relative to start of archive
		//	20		2		ZIP file comment length (n)
		//	22		n		ZIP file comment
		//
		//	Central directory file header
		//
		//	Pos		Len
		//	0		4		Central directory file header signature = 0x02014b50
		//	4		2		Version made by
		//	6		2		Version needed to extract (minimum)
		//	8		2		General purpose bit flag
		//	10		2		Compression method
		//	12		2		File last modification time
		//	14		2		File last modification date
		//	16		4		CRC-32
		//	20		4		Compressed size
		//	24		4		Uncompressed size
		//	28		2		File name length (n)
		//	30		2		Extra field length (m)
		//	32		2		File comment length (k)
		//	34		2		Disk number where file starts
		//	36		2		Internal file attributes
		//	38		4		External file attributes
		//	42		4		Offset of local file header
		//	46		n		File name
		//	46+n	m		Extra field
		//	46+n+m	k		File comment

		// file length
		Int64 FileLen = ReadStream.Length;
		if(FileLen < 98) throw new ApplicationException("ZIP file is too short");

		// read last 512 byte block at the end of the file
		if(FileLen > 512) ReadStream.Position = FileLen - 512;
		Byte[] DirSig = ReadFile.ReadBytes(512);

		// look for signature
		Int32 Ptr;
		for(Ptr = DirSig.Length - 20; Ptr >= 0 && BitConverter.ToInt32(DirSig, Ptr) != 0x06054b50; Ptr--);
		if(Ptr < 0) throw new ApplicationException("Invalid ZIP file (No central directory)");
		Ptr += 4;

		// number of this disk should be zero
		Int16 DiskNo = BitConverter.ToInt16(DirSig, Ptr);
		Ptr += 2;
		if(DiskNo != 0) throw new ApplicationException("No support for multi-disk ZIP file");

		// disk where central directory starts should be zero
		Int16 DirDiskNo = BitConverter.ToInt16(DirSig, Ptr);
		Ptr += 2;
		if(DirDiskNo != 0) throw new ApplicationException("No support for multi-disk ZIP file");

		// number of central directory records on this disk
		Int16 DirEntries1 = BitConverter.ToInt16(DirSig, Ptr);
		Ptr += 2;

		// Total number of central directory records
		Int16 DirEntries = BitConverter.ToInt16(DirSig, Ptr);
		Ptr += 2;
		if(DirEntries == 0 || DirEntries != DirEntries1) throw new ApplicationException("Central directory is empty or in error");

		// Size of central directory (bytes)
		Int32 DirSize = BitConverter.ToInt32(DirSig, Ptr);
		Ptr += 4;
		if(DirSize == 0) throw new ApplicationException("Central directory empty");

		// Offset of start of central directory, relative to start of archive
		ZipDirPosition = BitConverter.ToInt32(DirSig, Ptr);
		if(ZipDirPosition == 0) throw new ApplicationException("Central directory empty");

		// create result array
		ZipDir = new List<FileHeader>(DirEntries);

		// position file to central directory
		ReadStream.Position = ZipDirPosition;

		// read central directory
		while(DirEntries-- > 0)
			{
			// file header
			FileHeader FH = new FileHeader();

			// Central directory file header signature = 0x02014b50
			Int32 FileDirSig = ReadFile.ReadInt32();
			if(FileDirSig != 0x02014b50) throw new ApplicationException("File directory signature error");

			// Version made by (ignored)
			Int32 VersionMadeBy = ReadFile.ReadInt16();

			// Low byte is version needed to extract (the low byte should be 20 for version 2.0).
			// High byte is a computer system code that define the extrenal file attribute.
			// If high byte is zero it is DOS compatible.
			FH.Version = ReadFile.ReadUInt16();

			//	General purpose bit flag (bit 0 encripted)
			FH.BitFlags = ReadFile.ReadUInt16();

			//	Compression method must be deflate or no compression
			FH.CompMethod = ReadFile.ReadUInt16();

			//	File last modification time
			FH.FileTime = ReadFile.ReadUInt16();

			//	File last modification date
			FH.FileDate = ReadFile.ReadUInt16();

			// CRC-32
			FH.FileCRC32 = ReadFile.ReadUInt32();

			// Compressed size
			FH.CompSize = ReadFile.ReadUInt32();

			// Uncompressed size
			FH.FileSize = ReadFile.ReadUInt32();

			// File name length
			Int32 FileNameLen = ReadFile.ReadInt16();

			// Extra field length
			Int32 ExtraFieldLen = ReadFile.ReadInt16();

			// File comment length
			Int32 CommentLen = ReadFile.ReadInt16();

			// Disk number where file starts
			Int32 FileDiskNo = ReadFile.ReadInt16();
			if(FileDiskNo != 0) throw new ApplicationException("No support for multi-disk ZIP file");

			// internal file attributes (ignored)
			Int32 FileIntAttr = ReadFile.ReadInt16();

			// external file attributes
			FH.FileAttr = (FileAttributes) ReadFile.ReadUInt32();

			// if file system is not FAT or equivalent, erase the attributes
			if((FH.Version & 0xff00) != 0) FH.FileAttr = 0;

			// file position
			FH.FilePos = ReadFile.ReadUInt32();

			// file name
			// read all the bytes of the file name into a byte array
			// extract a string from the byte array using DOS (IBM OEM code page 437)
			// replace the unix forward slash with microsoft back slash
			FH.FileName = Encoding.GetEncoding(437).GetString(ReadFile.ReadBytes(FileNameLen)).Replace('/', '\\');

			// if file attribute is a directory make sure we have terminating backslash
//			if((FH.FileAttr & FileAttributes.Directory) != 0 && !FH.FileName.EndsWith("\\")) FH.FileName += "\\";

			// find if file name contains a path
			FH.Path = FH.FileName.Contains("\\");

			// if we have a directory, we must have a terminating slash
			if((FH.FileAttr & FileAttributes.Directory) != 0 && !FH.Path) throw new ApplicationException("Directory name must have a slash");

			// Skip Extra field and File comment
			ReadStream.Position += ExtraFieldLen + CommentLen;

			// add file header to zip directory
			ZipDir.Add(FH);
			}

		// sort array
		ZipDir.Sort();
		return;
		}

	////////////////////////////////////////////////////////////////////
	// ZIP file header
	////////////////////////////////////////////////////////////////////

	private void ReadFileHeader
			(
			UInt32		HeaderPosition
			)
		{
		//
		//	file header
		//
		//	Pos		Len
		//	0		4		Local file header signature = 0x04034b50
		//	4		2		Version needed to extract (minimum)
		//	6		2		General purpose bit flag
		//	8		2		Compression method
		//	10		2		File last modification time
		//	12		2		File last modification date
		//	14		4		CRC-32
		//	18		4		Compressed size
		//	22		4		Uncompressed size
		//	26		2		File name length (n)
		//	28		2		Extra field length (m)
		//	30		n		File name
		//	30+n	m		Extra field
		//
		// reset file time information available
		FileTimeAvailable = false;

		// set initial position
		ReadStream.Position = HeaderPosition;

		// local file header signature
		if(ReadFile.ReadUInt32() != 0x04034b50) throw new ApplicationException("Zip file signature in error");

		// NOTE: The program uses the file header information from the ZIP directory
		// at the end of the file. The local file header is ignored except for file times.
		// One can skip 22 bytes instead of reading these fields.
		// ReadStream.Position += 22;

		// version needed to extract and file system for external file attributes
		UInt16 Version = ReadFile.ReadUInt16();

		// general purpose bit flag
		UInt16 BitFlags = ReadFile.ReadUInt16();

		// compression method
		UInt16 CompMethod = ReadFile.ReadUInt16();

		// last mod file time
		UInt16 FileTime = ReadFile.ReadUInt16();

		// last mod file date
		UInt16 FileDate = ReadFile.ReadUInt16();

		// crc-32
		UInt32 FileCRC32 = ReadFile.ReadUInt32();

		// test local file header against zip directory file header
		if(FileHeaderInfo.FileCRC32 != FileCRC32) throw new ApplicationException("File header error");

		// compressed size
		UInt32 CompSize = ReadFile.ReadUInt32();

		// uncompressed size
		UInt32 FileSize = ReadFile.ReadUInt32();

		// file name length
		Int32 FileNameLen = ReadFile.ReadInt16();

		// extra field length
		Int32 ExtraFieldLen = ReadFile.ReadInt16();

		// file name
		// read all the bytes of the file name into a byte array
		// extract a string from the byte array using DOS (IBM OEM code page 437)
		// replace the unix forward slash with microsoft back slash
		String FileName = Encoding.GetEncoding(437).GetString(ReadFile.ReadBytes(FileNameLen)).Replace('/', '\\');

		// if extra field length is 36 get file times
		if(ExtraFieldLen == 36)
			{
			// get the extra field
			Byte[] TimeField = ReadFile.ReadBytes(36);

			// make sure it is NTFS tag of length 32, reserved area of zero, File times tag of 1 with length 24
			if(BitConverter.ToUInt32(TimeField, 0) == 0x0020000a &&
				BitConverter.ToUInt32(TimeField, 4) == 0 &&
				BitConverter.ToUInt32(TimeField, 8) == 0x00180001)
				{
				FileModifyTime = DateTime.FromFileTime(BitConverter.ToInt64(TimeField, 12));
				FileAccessTime = DateTime.FromFileTime(BitConverter.ToInt64(TimeField, 20));
				FileCreateTime = DateTime.FromFileTime(BitConverter.ToInt64(TimeField, 28));
				FileTimeAvailable = true;
				}
			}

		// skip extra field
		else
			{
			ReadStream.Position += ExtraFieldLen;
			}
		return;
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
		WriteCRC32 = CRC32.Checksum(WriteCRC32, Buffer, Pos,  Len);
		WriteFile.Write(Buffer, Pos, Len);
		return;
		}
	}
}
