/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	DeflateZipFile.cs
//	Class designed to create a new ZIP archive or to update existing
//	ZIP archive. The class has methods to add and delete files and
//	directory paths. This class is derived from DeflateMethod.
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
public class DeflateZipFile : DeflateMethod
	{
	public  String			ArchiveName{get{return(WriteFileName);}}
	public  static Boolean	IsOpen(DeflateZipFile Deflate) {return(Deflate != null && Deflate.WriteFile != null);}
	public  Boolean			IsEmpty{get{return(WriteFile == null || ZipDir == null || ZipDir.Count == 0);}}
	public  String[]		ExceptionStack;
	public  List<FileHeader> ZipDir;
	private Int64			ZipDirPosition;
	private Boolean			DeleteMode;

	private String			ReadFileName;
	private FileStream		ReadStream;
	private BinaryReader	ReadFile;
	private UInt32			ReadRemain;
	private UInt32			ReadCRC32;

	private String			WriteFileName;
	private FileStream		WriteStream;
	private BinaryWriter	WriteFile;
	private BinaryReader	WriteFileReader;
	private Int64			WriteStartPosition;
	private Int64			WriteRewindPosition;

	private FileHeader		FileHeaderInfo;

	////////////////////////////////////////////////////////////////////
	// Constructor
	////////////////////////////////////////////////////////////////////

	public DeflateZipFile() : base(DefaultCompression) {}
	public DeflateZipFile(Int32 CompLevel) : base(CompLevel) {}
		
	////////////////////////////////////////////////////////////////////
	// Create ZIP Archive
	////////////////////////////////////////////////////////////////////
	
	public Boolean CreateArchive
			(
			String		WriteFileName
			)
		{
		try
			{
			// save name
			this.WriteFileName = WriteFileName;

			// create destination file
			WriteStream = new FileStream(WriteFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

			// convert stream to binary writer
			WriteFile = new BinaryWriter(WriteStream, Encoding.UTF8);

			// convert stream to binary reader
			WriteFileReader = new BinaryReader(WriteStream, Encoding.UTF8);

			// create empty zip file directory
			ZipDir = new List<FileHeader>();

			// reset zip directory position
			ZipDirPosition = 0;

			// reset delete mode
			DeleteMode = false;

			// successful exit
			return(false);
			}

		// catch exceptions and save the exception error message and stack
		catch(Exception Ex)
			{
			// close the write file if it is open
			CloseZipFile();

			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}
		}

	////////////////////////////////////////////////////////////////////
	// Open existing ZIP archive for updating
	////////////////////////////////////////////////////////////////////
	
	public Boolean OpenArchive
			(
			String	WriteFileName
			)
		{
		try
			{
			// save name
			this.WriteFileName = WriteFileName;

			// create destination file
			WriteStream = new FileStream(WriteFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

			// convert stream to binary writer
			WriteFile = new BinaryWriter(WriteStream, Encoding.UTF8);

			// convert stream to binary reader
			WriteFileReader = new BinaryReader(WriteStream, Encoding.UTF8);

			// read zip file directory
			ReadZipFileDirectory();

			// reset delete mode
			DeleteMode = false;

			// successful exit
			return(false);
			}

		// catch exceptions and save the exception error message and stack
		catch(Exception Ex)
			{
			// close the write file if it is open
			CloseZipFile();

			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}
		}

	////////////////////////////////////////////////////////////////////
	// Decompress one file
	////////////////////////////////////////////////////////////////////
	
	public Boolean Compress
			(
			String		FullFileName,
			String		ArchiveFileName
			)
		{
		try
			{
			// write zip file header
			WriteFileHeader(FullFileName, ArchiveFileName);

			// open source file for reading
			ReadStream = new FileStream(ReadFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
		
			// convert stream to binary reader
			ReadFile = new BinaryReader(ReadStream, Encoding.UTF8);

			// uncompressed file length
			ReadRemain = (UInt32) ReadStream.Length;

			// reset CRC32 checksum
			ReadCRC32 = 0;

			// compress the file
			Compress();

			// close read file
			ReadFile.Dispose();
			ReadFile = null;

			// update zip file header with input file crc and compressed file length
			UpdateZipFileHeader();

			// successful exit
			return(false);
			}

		// catch exceptions and save the exception error message and stack
		catch(Exception Ex)
			{
			// close the read file if it is open
			if(ReadFile != null)
				{
				ReadFile.Dispose();
				ReadFile = null;
				}

			// remove any information written to the file
			WriteStream.SetLength(WriteStartPosition);

			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}
		}

	////////////////////////////////////////////////////////////////////
	// Save directory path in the ZIP directory
	////////////////////////////////////////////////////////////////////
	
	public Boolean SaveDirectoryPath
			(
			String		FullFileName,
			String		ArchiveFileName
			)
		{
		try
			{
			// write zip file header for directory path
			WriteFileHeader(FullFileName, ArchiveFileName);

			// save file header
			ZipDir.Insert(~ZipDir.BinarySearch(FileHeaderInfo), FileHeaderInfo);

			// successful exit
			return(false);
			}

		// catch exceptions and save the exception error message and stack
		catch(Exception Ex)
			{
			// remove any information written to the file
			WriteStream.SetLength(WriteStartPosition);

			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}
		}

	////////////////////////////////////////////////////////////////////
	// Delete central directory entry
	////////////////////////////////////////////////////////////////////
	
	public Boolean Delete
			(
			Int32	ZipDirIndex
			)
		{
		try
			{
			// delete the entry
			ZipDir.RemoveAt(ZipDirIndex);
			}

		// catch exceptions and save the exception error message and stack
		catch(Exception Ex)
			{
			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}

		// set delete mode flag for save archive
		DeleteMode = true;

		// successful exit
		return(false);
		}

	////////////////////////////////////////////////////////////////////
	// Save ZIP Archive
	////////////////////////////////////////////////////////////////////
	
	public Boolean SaveArchive()
		{
		try
			{
			// zip directory is empty
			if(ZipDir.Count == 0)
				{
				// close and delete the file
				WriteFile.Dispose();
				WriteFile = null;
				File.Delete(WriteFileName);
				}
			else
				{
				// one or more files were deleted
				if(DeleteMode) DeleteFiles();

				// write zip file directory
				ZipFileDirectory();

				// close file
				WriteFile.Dispose();
				WriteFile = null;

				// clear directory
				ZipDir.Clear();
				}

			// successful exit
			return(false);
			}

		// catch exceptions and save the exception error message and stack
		catch(Exception Ex)
			{
			// close the write file if it is open
			CloseZipFile();

			// error exit
			ExceptionStack = ExceptionReport.GetMessageAndStack(this, Ex);
			return(true);
			}
		}

	////////////////////////////////////////////////////////////////////
	// Clear ZIP Archive
	////////////////////////////////////////////////////////////////////
	
	public void ClearArchive()
		{
		try
			{
			// close and delete the file
			WriteFile.Dispose();
			WriteFile = null;
			File.Delete(WriteFileName);
			}
		catch {}

		// clear directory
		ZipDir.Clear();

		// exit
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Close ZIP file
	/////////////////////////////////////////////////////////////////////

	private void CloseZipFile()
		{
		// close the write file if it is open
		if(WriteFile != null)
			{
			WriteFile.Dispose();
			WriteFile = null;
			}
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Delete files
	////////////////////////////////////////////////////////////////////

	private void DeleteFiles()
		{
		// allocate 64kb array
		Byte[] Buffer = new Byte[1024*64];

		// sort directory by position
		ZipDir.Sort(FileHeader.CompareByPosition);

		// new file position
		Int64 WritePosition = 0;

		// loop for all files
		for(Int32 Index = 0; Index < ZipDir.Count; Index++)
			{
			// short cut
			FileHeader FH = ZipDir[Index];

			// read position
			Int64 ReadPosition = FH.FilePos;

			// set file position
			WriteStream.Position = ReadPosition;

			// read local file header base part
			Int32 Len = WriteFileReader.Read(Buffer, 0, 30);
			if(Len != 30) throw new ApplicationException("Delete file: Read file header error");

			// local file header signature
			if(BitConverter.ToUInt32(Buffer, 0) != 0x04034b50) throw new ApplicationException("Delete file: ZIP signature in error");

			// file name length
			UInt32 FileNameLen = BitConverter.ToUInt16(Buffer, 26);

			// extra fields length
			UInt32 ExtraFieldLen = BitConverter.ToUInt16(Buffer, 28);

			// total length of compressed file plus local file header
			UInt32 TotalLength = 30 + FileNameLen + ExtraFieldLen + FH.CompSize;

			// test if move is required
			if(ReadPosition == WritePosition)
				{
				// no move required
				WritePosition += TotalLength;
				continue;
				}

			// change file position in the directory
			FH.FilePos = (UInt32) WritePosition;

			// fill the first buffer
			// we have already the first 30 bytes
			Int32 BlockLen = (TotalLength > (UInt32) Buffer.Length ? Buffer.Length : (Int32) TotalLength) - 30;
			Len = WriteFileReader.Read(Buffer, 30, BlockLen);
			if(Len != BlockLen) throw new ApplicationException("Delete file: Read first block error");
			BlockLen += 30;

			// update read position
			ReadPosition += BlockLen;

			// copy loop
			for(;;)
				{
				// set write position
				WriteStream.Position = WritePosition;

				// write the block
				WriteFile.Write(Buffer, 0, BlockLen);

				// update write position
				WritePosition += BlockLen;

				// update total length
				TotalLength -= (UInt32) BlockLen;
				if(TotalLength == 0) break;

				// set position to next block
				WriteStream.Position = ReadPosition;

				// read next block
				BlockLen = TotalLength > (UInt32) Buffer.Length ? Buffer.Length : (Int32) TotalLength;
				Len = WriteFileReader.Read(Buffer, 0, BlockLen);
				if(Len != BlockLen) throw new ApplicationException("Delete file: read file block error");		

				// update read position
				ReadPosition += BlockLen;
				}
			}

		// truncate file to current write position
		WriteStream.Position = WritePosition;
		WriteStream.SetLength(WritePosition);
		return;
		}

	////////////////////////////////////////////////////////////////////
	// ZIP local file header
	// Write file header to output file with CRC and compressed length set to zero
	////////////////////////////////////////////////////////////////////

	private void WriteFileHeader
			(
			String		FullFileName,
			String		ArchiveFileName
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

		// save write file length for possible error
		WriteStartPosition = WriteStream.Length;

		// save read file name
		ReadFileName = FullFileName;

		// file info
		FileInfo FI = new FileInfo(ReadFileName);

		// Directory flag
		Boolean DirFlag = (FI.Attributes & FileAttributes.Directory) != 0;

		// create file header structure (name, time, attributes and version)
		FileHeaderInfo = new FileHeader(ArchiveFileName, FI.LastWriteTime, FI.Attributes, WriteStream.Length, DirFlag ? 0 : FI.Length);

		// compression method deflate
		if(!DirFlag) FileHeaderInfo.CompMethod = 8;

		// test for duplicate file name
		if(ZipDir.BinarySearch(FileHeaderInfo) >= 0) throw new ApplicationException("Duplicate file name");

		// local file header signature
		WriteFile.Write((UInt32) 0x04034b50);

		// version needed to extract 2.0 (file system is dos)
		WriteFile.Write((UInt16) FileHeaderInfo.Version);

		// general purpose bit flag
		WriteFile.Write((UInt16) FileHeaderInfo.BitFlags);

		// compression method deflate
		WriteFile.Write((UInt16) FileHeaderInfo.CompMethod);

		// last mod file time
		WriteFile.Write(FileHeaderInfo.FileTime);

		// last mod file date
		WriteFile.Write(FileHeaderInfo.FileDate);

		// CRC-32 is set to zero until CRC will be calculated
		WriteFile.Write((UInt32) FileHeaderInfo.FileCRC32);

		// compressed size is set to zero until the compressed size is known
		WriteFile.Write((UInt32) FileHeaderInfo.CompSize);

		// uncompressed size
		WriteFile.Write((UInt32) FileHeaderInfo.FileSize);

		// convert filename string to array of bytes using DOS (IBM OEM code page 437) encoding
		Byte[] ZipFileName = Encoding.GetEncoding(437).GetBytes(FileHeaderInfo.FileName.Replace('\\', '/'));

		// file name length
		WriteFile.Write((Int16) ZipFileName.Length);

		// extra field length (for file times)
		WriteFile.Write((Int16) (DirFlag ? 0 : 36));

		// file name
		WriteFile.Write(ZipFileName);

		// extra field
		if(!DirFlag)
			{
			// NTFS tag of length 32, reserved area of zero, File times tag of 1 with length 24
			WriteFile.Write((UInt32) 0x0020000a);
			WriteFile.Write((UInt32) 0);
			WriteFile.Write((UInt32) 0x00180001);
			WriteFile.Write((Int64) FI.LastWriteTime.ToFileTime());
			WriteFile.Write((Int64) (File.GetLastAccessTime(ReadFileName).ToFileTime()));
			WriteFile.Write((Int64) (File.GetCreationTime(ReadFileName).ToFileTime()));
			}

		// save write file length for possible rewind
		WriteRewindPosition = WriteStream.Length;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Update ZIP file header
	// Overwrite CRC and compressed length and save in zip directory
	////////////////////////////////////////////////////////////////////

	private void UpdateZipFileHeader()
		{
		// compress function was stored
		if(CompFunction == CompFunc.Stored)
			{
			// set file position to compression method flag
			WriteStream.Position = FileHeaderInfo.FilePos + 8;

			// change compression method from Deflate(8) to Stored(0)
			FileHeaderInfo.CompMethod = 0;
			WriteFile.Write((UInt16) FileHeaderInfo.CompMethod);
			}

		// set file position to update position within file header
		WriteStream.Position = FileHeaderInfo.FilePos + 14;

		// crc-32 of the input file
		FileHeaderInfo.FileCRC32 = ReadCRC32;
		WriteFile.Write((UInt32) ReadCRC32);

		// compressed file size
		FileHeaderInfo.CompSize = WriteTotal;
		WriteFile.Write((UInt32) WriteTotal);

		// restore file position to end of file
		WriteStream.Position = WriteStream.Length;

		// save file header
		ZipDir.Insert(~ZipDir.BinarySearch(FileHeaderInfo), FileHeaderInfo);

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Write ZIP file directory
	////////////////////////////////////////////////////////////////////

	private void ZipFileDirectory()
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
		//	42		4		Relative offset of local file header.
		//	46		n		File name
		//	46+n	m		Extra field
		//	46+n+m	k		File comment

		// file is too long
		if(WriteStream.Length > (Int64) 0xffffffff) throw new ApplicationException("No support for files over 4GB");

		// save directory position
		UInt32 DirPos = (UInt32) WriteStream.Length;

		// sort by position
		ZipDir.Sort(FileHeader.CompareByPosition);

		// write central directory at the end of the file
		for(Int32 EntryNo = 0; EntryNo < ZipDir.Count; EntryNo++)
			{
			// shortcut
			FileHeader FH = ZipDir[EntryNo];

			// Central directory file header signature = 0x02014b50
			WriteFile.Write((UInt32) 0x02014b50);

			// Version made by 2.0
			WriteFile.Write((UInt16) FH.Version);

			// Low byte is version needed to extract (the low byte should be 20 for version 2.0).
			// High byte is a computer system code that define the extrenal file attribute.
			// If high byte is zero it is DOS compatible.
			WriteFile.Write((UInt16) FH.Version);

			//	General purpose bit flag (bit 0 encripted)
			WriteFile.Write((UInt16) FH.BitFlags);

			//	Compression method must be deflate or no compression
			WriteFile.Write((UInt16) FH.CompMethod);

			//	File last modification time
			WriteFile.Write((UInt16) FH.FileTime);

			//	File last modification date
			WriteFile.Write((UInt16) FH.FileDate);

			// CRC-32
			WriteFile.Write((UInt32) FH.FileCRC32);

			// Compressed size
			WriteFile.Write((UInt32) FH.CompSize);

			// Uncompressed size
			WriteFile.Write((UInt32) FH.FileSize);

			// convert filename string to array of bytes using DOS (IBM OEM code page 437) encoding
			Byte[] FileName = Encoding.GetEncoding(437).GetBytes(FH.FileName.Replace('\\', '/'));

			// File name length
			WriteFile.Write((UInt16) FileName.Length);

			// Extra field length
			WriteFile.Write((UInt16) 0);

			// File comment length
			WriteFile.Write((UInt16) 0);

			// Disk number where file starts
			WriteFile.Write((UInt16) 0);

			// internal file attributes
			WriteFile.Write((UInt16) 0);

			// external file attributes
			WriteFile.Write((UInt32) FH.FileAttr);

			// file position
			WriteFile.Write((UInt32) FH.FilePos);

			// file name (Byte array)
			WriteFile.Write(FileName);
			}

		// directory size
		UInt32 DirSize = (UInt32) WriteStream.Length - DirPos;

		// Central directory signature = 0x06054b50
		WriteFile.Write((UInt32) 0x06054b50);

		// number of this disk should be zero
		WriteFile.Write((UInt16) 0);

		// disk where central directory starts should be zero
		WriteFile.Write((UInt16) 0);

		// number of central directory records on this disk
		WriteFile.Write((UInt16) ZipDir.Count);

		// Total number of central directory records
		WriteFile.Write((UInt16) ZipDir.Count);

		// Size of central directory (bytes)
		WriteFile.Write((UInt32) DirSize);

		// Offset of start of central directory, relative to start of archive
		WriteFile.Write((UInt32) DirPos);

		// directory comment length should be zero
		WriteFile.Write((UInt16) 0);

		// sort by file name
		ZipDir.Sort();
		return;
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
		Int64 FileLen = WriteStream.Length;
		if(FileLen < 98) throw new ApplicationException("ZIP file is too short");

		// read last 512 byte block at the end of the file
		if(FileLen > 512) WriteStream.Position = FileLen - 512;
		Byte[] DirSig = WriteFileReader.ReadBytes(512);

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

		// create new zip directory array
		ZipDir = new List<FileHeader>(DirEntries);

		// position file to central directory
		WriteStream.Position = ZipDirPosition;

		// read central directory
		while(DirEntries-- > 0)
			{
			// file header
			FileHeader FH = new FileHeader();

			// Central directory file header signature = 0x02014b50
			Int32 FileDirSig = WriteFileReader.ReadInt32();
			if(FileDirSig != 0x02014b50) throw new ApplicationException("File directory signature error");

			// Version made by (ignored)
			Int32 VersionMadeBy = WriteFileReader.ReadInt16();

			// Low byte is version needed to extract (the low byte should be 20 for version 2.0).
			// High byte is a computer system code that define the extrenal file attribute.
			// If high byte is zero it is DOS compatible.
			FH.Version = WriteFileReader.ReadUInt16();

			//	General purpose bit flag (bit 0 encripted)
			FH.BitFlags = WriteFileReader.ReadUInt16();

			//	Compression method must be deflate or no compression
			FH.CompMethod = WriteFileReader.ReadUInt16();

			//	File last modification time
			FH.FileTime = WriteFileReader.ReadUInt16();

			//	File last modification date
			FH.FileDate = WriteFileReader.ReadUInt16();

			// CRC-32
			FH.FileCRC32 = WriteFileReader.ReadUInt32();

			// Compressed size
			FH.CompSize = WriteFileReader.ReadUInt32();

			// Uncompressed size
			FH.FileSize = WriteFileReader.ReadUInt32();

			// File name length
			Int32 FileNameLen = WriteFileReader.ReadInt16();

			// Extra field length
			Int32 ExtraFieldLen = WriteFileReader.ReadInt16();

			// File comment length
			Int32 CommentLen = WriteFileReader.ReadInt16();

			// Disk number where file starts
			Int32 FileDiskNo = WriteFileReader.ReadInt16();
			if(FileDiskNo != 0) throw new ApplicationException("No support for multi-disk ZIP file");

			// internal file attributes (ignored)
			Int32 FileIntAttr = WriteFileReader.ReadInt16();

			// external file attributes
			FH.FileAttr = (FileAttributes) WriteFileReader.ReadUInt32();

			// if file system is not FAT or equivalent, erase the attributes
			if((FH.Version & 0xff00) != 0) FH.FileAttr = 0;

			// file position
			FH.FilePos = WriteFileReader.ReadUInt32();

			// file name
			// read all the bytes of the file name into a byte array
			// extract a string from the byte array using DOS (IBM OEM code page 437)
			// replace the unix forward slash with microsoft back slash
			FH.FileName = Encoding.GetEncoding(437).GetString(WriteFileReader.ReadBytes(FileNameLen)).Replace('/', '\\');

			// if file attribute is a directory make sure we have terminating backslash
//			if((FH.FileAttr & FileAttributes.Directory) != 0 && !FH.FileName.EndsWith("\\")) FH.FileName += "\\";

			// find if file name contains a path
			FH.Path = FH.FileName.Contains("\\");

			// if we have a directory, we must have a terminating slash
			if((FH.FileAttr & FileAttributes.Directory) != 0 && !FH.Path) throw new ApplicationException("Directory name must have a slash");

			// Skip Extra field and File comment
			WriteStream.Position += ExtraFieldLen + CommentLen;

			// add file header to zip directory
			ZipDir.Add(FH);
			}

		// position file to central directory
		WriteStream.Position = ZipDirPosition;

		// remove central directory from this file
		WriteStream.SetLength(ZipDirPosition);

		// sort array
		ZipDir.Sort();
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
		ReadFile.Read(Buffer, Pos, Len);
		ReadCRC32 = CRC32.Checksum(ReadCRC32, Buffer, Pos,  Len);
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
		ReadCRC32 = 0;

		// reposition write stream to the new end of file
		WriteStream.SetLength(WriteRewindPosition);
		WriteStream.Position = WriteRewindPosition;
		return;
		}
	}
}
