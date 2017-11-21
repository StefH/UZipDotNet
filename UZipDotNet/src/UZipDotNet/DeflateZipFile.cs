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
using UZipDotNet.Extensions;
using UZipDotNet.Support;

namespace UZipDotNet
{
    public class DeflateZipFile : DeflateMethod, IDeflateFile
    {
        public static bool IsOpen(DeflateZipFile deflate)
        {
            return deflate != null && deflate._writeFile != null;
        }

        public string ArchiveName
        {
            get
            {
                return _writeFileName;
            }
        }

        public bool IsEmpty
        {
            get
            {
                return _writeFile == null || ZipDir == null || ZipDir.Count == 0;
            }
        }

        public List<FileHeader> ZipDir;
        private long _zipDirPosition;
        private bool _deleteMode;

        private FileStream _readStream;
        private BinaryReader _readFile;
        private uint _readRemain;
        private uint _readCrc32;

        private string _writeFileName;
        private FileStream _writeStream;
        private BinaryWriter _writeFile;
        private BinaryReader _writeFileReader;
        private long _writeStartPosition;
        private long _writeRewindPosition;

        private FileHeader _fileHeaderInfo;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateZipFile"/> class.
        /// </summary>
        /// <param name="fileName">Name of the zip-file.</param>
        /// <param name="compLevel">The comp level.</param>
        public DeflateZipFile(string fileName, int compLevel = DefaultCompression)
            : base(compLevel)
        {
            if (!File.Exists(fileName))
            {
                CreateArchive(fileName);
            }
            else
            {
                OpenArchive(fileName);
            }
        }

        /// <summary>
        /// Create ZIP Archive
        /// </summary>
        /// <param name="fileName">Name of the write file.</param>
        private void CreateArchive(string fileName)
        {
            // save name
            _writeFileName = fileName;

            // create destination file
            _writeStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            // convert stream to binary writer
            _writeFile = new BinaryWriter(_writeStream, Encoding.UTF8);

            // convert stream to binary reader
            _writeFileReader = new BinaryReader(_writeStream, Encoding.UTF8);

            // create empty zip file directory
            ZipDir = new List<FileHeader>();

            // reset zip directory position
            _zipDirPosition = 0;

            // reset delete mode
            _deleteMode = false;
        }

        /// <summary>
        /// Open existing ZIP archive for updating
        /// </summary>
        /// <param name="writeFileName">Name of the write file.</param>
        private void OpenArchive(string writeFileName)
        {
            try
            {
                // save name
                _writeFileName = writeFileName;

                // create destination file
                _writeStream = new FileStream(writeFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                // convert stream to binary writer
                _writeFile = new BinaryWriter(_writeStream, Encoding.UTF8);

                // convert stream to binary reader
                _writeFileReader = new BinaryReader(_writeStream, Encoding.UTF8);

                // read zip file directory
                ReadZipFileDirectory();

                // reset delete mode
                _deleteMode = false;
            }
            catch (Exception)
            {
                // close the write file if it is open
                Dispose();

                throw;
            }
        }

        /// <summary>
        /// Compress one file
        /// </summary>
        /// <param name="fullFileName">Full name of the file.</param>
        /// <param name="archiveFileName">Name of the archive file.</param>
        public void Compress(string fullFileName, string archiveFileName)
        {
            try
            {
                // write zip file header
                WriteFileHeader(fullFileName, archiveFileName);

                // open source file for reading
                _readStream = new FileStream(fullFileName, FileMode.Open, FileAccess.Read, FileShare.Read);

                // convert stream to binary reader
                _readFile = new BinaryReader(_readStream, Encoding.UTF8);

                // uncompressed file length
                _readRemain = (UInt32)_readStream.Length;

                // reset CRC32 checksum
                _readCrc32 = 0;

                // compress the file
                Compress();

                // close read file
                _readFile.Dispose();
                _readFile = null;

                // update zip file header with input file crc and compressed file length
                UpdateZipFileHeader(_readCrc32);
            }
            catch (Exception)
            {
                // close the read file if it is open
                if (_readFile != null)
                {
                    _readFile.Dispose();
                    _readFile = null;
                }

                // remove any information written to the file
                _writeStream.SetLength(_writeStartPosition);

                throw;
            }
        }

        /// <summary>
        /// Save directory path in the ZIP directory
        /// </summary>
        /// <param name="fullFileName">Full name of the file.</param>
        /// <param name="archiveFileName">Name of the archive file.</param>
        public void SaveDirectoryPath(string fullFileName, string archiveFileName)
        {
            try
            {
                // write zip file header for directory path
                WriteFileHeader(fullFileName, archiveFileName);

                // save file header
                ZipDir.Insert(~ZipDir.BinarySearch(_fileHeaderInfo), _fileHeaderInfo);
            }
            catch (Exception)
            {
                // remove any information written to the file
                _writeStream.SetLength(_writeStartPosition);

                throw;
            }
        }

        /// <summary>
        /// Delete central directory entry
        /// </summary>
        /// <param name="zipDirIndex">Index of the zip dir.</param>
        public void Delete(int zipDirIndex)
        {
            // delete the entry
            ZipDir.RemoveAt(zipDirIndex);

            // set delete mode flag for save archive
            _deleteMode = true;
        }

        /// <summary>
        /// Save ZIP Archive
        /// </summary>
        public void Save()
        {
            try
            {
                // zip directory is empty
                if (ZipDir.Count == 0)
                {
                    // close and delete the file
                    _writeFile.Dispose();
                    _writeFile = null;
                    File.Delete(_writeFileName);
                }
                else
                {
                    // one or more files were deleted
                    if (_deleteMode) DeleteFiles();

                    // write zip file directory
                    ZipFileDirectory();

                    // close file
                    _writeFile.Dispose();
                    _writeFile = null;

                    // clear directory
                    ZipDir.Clear();
                }
            }
            catch (Exception)
            {
                // close the write file if it is open
                Dispose();

                throw;
            }
        }

        /// <summary>
        /// Clear ZIP Archive
        /// </summary>
        public void Clear()
        {
            try
            {
                // close and delete the file
                Dispose();
                File.Delete(_writeFileName);
            }
            catch
            {
                // ignored
            }

            // clear directory
            ZipDir.Clear();
        }

        /// <summary>
        /// Delete files
        /// </summary>
        /// <exception cref="UZipDotNet.Exception">
        /// Delete file: Read file header error
        /// or
        /// Delete file: ZIP signature in error
        /// or
        /// Delete file: Read first block error
        /// or
        /// Delete file: read file block error
        /// </exception>
        private void DeleteFiles()
        {
            // allocate 64kb array
            var buffer = new byte[1024 * 64];

            // sort directory by position
            ZipDir.Sort(FileHeader.CompareByPosition);

            // new file position
            long writePosition = 0;

            // loop for all files
            for (int index = 0; index < ZipDir.Count; index++)
            {
                // short cut
                FileHeader fileHeader = ZipDir[index];

                // read position
                long readPosition = fileHeader.FilePos;

                // set file position
                _writeStream.Position = readPosition;

                // read local file header base part
                int len = _writeFileReader.Read(buffer, 0, 30);
                if (len != 30) throw new Exception("Delete file: Read file header error");

                // local file header signature
                if (BitConverter.ToUInt32(buffer, 0) != 0x04034b50) throw new Exception("Delete file: ZIP signature in error");

                // file name length
                uint fileNameLen = BitConverter.ToUInt16(buffer, 26);

                // extra fields length
                uint extraFieldLen = BitConverter.ToUInt16(buffer, 28);

                // total length of compressed file plus local file header
                uint totalLength = 30 + fileNameLen + extraFieldLen + fileHeader.CompSize;

                // test if move is required
                if (readPosition == writePosition)
                {
                    // no move required
                    writePosition += totalLength;
                    continue;
                }

                // change file position in the directory
                fileHeader.FilePos = (uint)writePosition;

                // fill the first buffer
                // we have already the first 30 bytes
                int blockLen = (totalLength > (uint)buffer.Length ? buffer.Length : (int)totalLength) - 30;
                len = _writeFileReader.Read(buffer, 30, blockLen);
                if (len != blockLen) throw new Exception("Delete file: Read first block error");
                blockLen += 30;

                // update read position
                readPosition += blockLen;

                // copy loop
                for (; ; )
                {
                    // set write position
                    _writeStream.Position = writePosition;

                    // write the block
                    _writeFile.Write(buffer, 0, blockLen);

                    // update write position
                    writePosition += blockLen;

                    // update total length
                    totalLength -= (uint)blockLen;
                    if (totalLength == 0) break;

                    // set position to next block
                    _writeStream.Position = readPosition;

                    // read next block
                    blockLen = totalLength > (uint)buffer.Length ? buffer.Length : (int)totalLength;
                    len = _writeFileReader.Read(buffer, 0, blockLen);

                    if (len != blockLen) throw new Exception("Delete file: read file block error");

                    // update read position
                    readPosition += blockLen;
                }
            }

            // truncate file to current write position
            _writeStream.Position = writePosition;
            _writeStream.SetLength(writePosition);
        }

        /// <summary>
        /// ZIP local file header
        /// Write file header to output file with CRC and compressed length set to zero
        /// </summary>
        /// <param name="fullFileName">Full name of the file.</param>
        /// <param name="archiveFileName">Name of the archive file.</param>
        private void WriteFileHeader(string fullFileName, string archiveFileName)
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
            _writeStartPosition = _writeStream.Length;

            // file info
            var fi = new FileInfo(fullFileName);

            // Directory flag
            bool dirFlag = (fi.Attributes & FileAttributes.Directory) != 0;

            // create file header structure (name, time, attributes and version)
            _fileHeaderInfo = new FileHeader(archiveFileName, fi.LastWriteTime, fi.Attributes, _writeStream.Length, dirFlag ? 0 : fi.Length);

            // compression method deflate
            if (!dirFlag) _fileHeaderInfo.CompMethod = 8;

            // test for duplicate file name
            if (ZipDir.BinarySearch(_fileHeaderInfo) >= 0) throw new Exception("Duplicate file name");

            // local file header signature
            _writeFile.Write((uint)0x04034b50);

            // version needed to extract 2.0 (file system is dos)
            _writeFile.Write(_fileHeaderInfo.Version);

            // general purpose bit flag
            _writeFile.Write(_fileHeaderInfo.BitFlags);

            // compression method deflate
            _writeFile.Write(_fileHeaderInfo.CompMethod);

            // last mod file time
            _writeFile.Write(_fileHeaderInfo.FileTime);

            // last mod file date
            _writeFile.Write(_fileHeaderInfo.FileDate);

            // CRC-32 is set to zero until CRC will be calculated
            _writeFile.Write(_fileHeaderInfo.FileCRC32);

            // compressed size is set to zero until the compressed size is known
            _writeFile.Write(_fileHeaderInfo.CompSize);

            // uncompressed size
            _writeFile.Write(_fileHeaderInfo.FileSize);

            // convert filename string to array of bytes using DOS (IBM OEM code page 437) encoding
            byte[] zipFileName = Utils.EncodeFilename(_fileHeaderInfo.FileName);

            // file name length
            _writeFile.Write((Int16)zipFileName.Length);

            // extra field length (for file times)
            _writeFile.Write((Int16)(dirFlag ? 0 : 36));

            // file name
            _writeFile.Write(zipFileName);

            // extra field
            if (!dirFlag)
            {
                // NTFS tag of length 32, reserved area of zero, File times tag of 1 with length 24
                _writeFile.Write((UInt32)0x0020000a);
                _writeFile.Write((UInt32)0);
                _writeFile.Write((UInt32)0x00180001);

                var details = fi.GetFileTimeDetails();

                _writeFile.Write(details.LastWriteTimeAsFileTime);
                _writeFile.Write(details.LastAccessTimeAsFileTime);
                _writeFile.Write(details.CreationTimeAsFileTime);
            }

            // save write file length for possible rewind
            _writeRewindPosition = _writeStream.Length;
        }

        /// <summary>
        /// Update ZIP file header
        /// Overwrite CRC and compressed length and save in zip directory
        /// </summary>
        /// <param name="crc32">The read CRC32.</param>
        private void UpdateZipFileHeader(uint crc32)
        {
            // compress function was stored
            if (CompFunction == CompFunc.Stored)
            {
                // set file position to compression method flag
                _writeStream.Position = _fileHeaderInfo.FilePos + 8;

                // change compression method from Deflate(8) to Stored(0)
                _fileHeaderInfo.CompMethod = 0;
                _writeFile.Write(_fileHeaderInfo.CompMethod);
            }

            // set file position to update position within file header
            _writeStream.Position = _fileHeaderInfo.FilePos + 14;

            // crc-32 of the input file
            _fileHeaderInfo.FileCRC32 = crc32;
            _writeFile.Write(crc32);

            // compressed file size
            _fileHeaderInfo.CompSize = WriteTotal;
            _writeFile.Write(WriteTotal);

            // restore file position to end of file
            _writeStream.Position = _writeStream.Length;

            // save file header
            ZipDir.Insert(~ZipDir.BinarySearch(_fileHeaderInfo), _fileHeaderInfo);
        }

        /// <summary>
        /// Write ZIP file directory
        /// </summary>
        /// <exception cref="UZipDotNet.Exception">No support for files over 4GB</exception>
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
            if (_writeStream.Length > 0xffffffff) throw new Exception("No support for files over 4GB");

            // save directory position
            uint dirPos = (uint)_writeStream.Length;

            // sort by position
            ZipDir.Sort(FileHeader.CompareByPosition);

            // write central directory at the end of the file
            for (int entryNo = 0; entryNo < ZipDir.Count; entryNo++)
            {
                // shortcut
                FileHeader fh = ZipDir[entryNo];

                // Central directory file header signature = 0x02014b50
                _writeFile.Write((UInt32)0x02014b50);

                // Version made by 2.0
                _writeFile.Write(fh.Version);

                // Low byte is version needed to extract (the low byte should be 20 for version 2.0).
                // High byte is a computer system code that define the extrenal file attribute.
                // If high byte is zero it is DOS compatible.
                _writeFile.Write(fh.Version);

                //	General purpose bit flag (bit 0 encripted)
                _writeFile.Write(fh.BitFlags);

                //	Compression method must be deflate or no compression
                _writeFile.Write(fh.CompMethod);

                //	File last modification time
                _writeFile.Write(fh.FileTime);

                //	File last modification date
                _writeFile.Write(fh.FileDate);

                // CRC-32
                _writeFile.Write(fh.FileCRC32);

                // Compressed size
                _writeFile.Write(fh.CompSize);

                // Uncompressed size
                _writeFile.Write(fh.FileSize);

                // convert filename string to array of bytes using DOS (IBM OEM code page 437) encoding
                byte[] fileName = Utils.EncodeFilename(fh.FileName);

                // File name length
                _writeFile.Write((UInt16)fileName.Length);

                // Extra field length
                _writeFile.Write((UInt16)0);

                // File comment length
                _writeFile.Write((UInt16)0);

                // Disk number where file starts
                _writeFile.Write((UInt16)0);

                // internal file attributes
                _writeFile.Write((UInt16)0);

                // external file attributes
                _writeFile.Write((UInt32)fh.FileAttr);

                // file position
                _writeFile.Write(fh.FilePos);

                // file name (Byte array)
                _writeFile.Write(fileName);
            }

            // directory size
            uint dirSize = (uint)_writeStream.Length - dirPos;

            // Central directory signature = 0x06054b50
            _writeFile.Write((UInt32)0x06054b50);

            // number of this disk should be zero
            _writeFile.Write((UInt16)0);

            // disk where central directory starts should be zero
            _writeFile.Write((UInt16)0);

            // number of central directory records on this disk
            _writeFile.Write((UInt16)ZipDir.Count);

            // Total number of central directory records
            _writeFile.Write((UInt16)ZipDir.Count);

            // Size of central directory (bytes)
            _writeFile.Write(dirSize);

            // Offset of start of central directory, relative to start of archive
            _writeFile.Write(dirPos);

            // directory comment length should be zero
            _writeFile.Write((UInt16)0);

            // sort by file name
            ZipDir.Sort();
        }

        /// <summary>
        ///  Read ZIP file directory
        /// </summary>
        /// <exception cref="UZipDotNet.Exception">
        /// ZIP file is too short
        /// or
        /// Invalid ZIP file (No central directory)
        /// or
        /// No support for multi-disk ZIP file
        /// or
        /// Central directory is empty or in error
        /// or
        /// Central directory empty
        /// or
        /// File directory signature error
        /// or
        /// Directory name must have a slash
        /// </exception>
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
            long fileLen = _writeStream.Length;
            if (fileLen < 98) throw new Exception("ZIP file is too short");

            // read last 512 byte block at the end of the file
            if (fileLen > 512) _writeStream.Position = fileLen - 512;
            byte[] dirSig = _writeFileReader.ReadBytes(512);

            // look for signature
            int ptr;
            for (ptr = dirSig.Length - 20; ptr >= 0 && BitConverter.ToInt32(dirSig, ptr) != 0x06054b50; ptr--)
            {
            }
            if (ptr < 0) throw new Exception("Invalid ZIP file (No central directory)");
            ptr += 4;

            // number of this disk should be zero
            short diskNo = BitConverter.ToInt16(dirSig, ptr);
            ptr += 2;
            if (diskNo != 0) throw new Exception("No support for multi-disk ZIP file");

            // disk where central directory starts should be zero
            short dirDiskNo = BitConverter.ToInt16(dirSig, ptr);
            ptr += 2;
            if (dirDiskNo != 0) throw new Exception("No support for multi-disk ZIP file");

            // Number of central directory records on this disk
            short dirEntries = BitConverter.ToInt16(dirSig, ptr);
            ptr += 2;

            // Total number of central directory records
            short dirEntriesTotal = BitConverter.ToInt16(dirSig, ptr);
            ptr += 2;
            if (dirEntriesTotal == 0 || dirEntriesTotal != dirEntries) throw new Exception("Central directory is empty or in error");

            // Size of central directory (bytes)
            int dirSize = BitConverter.ToInt32(dirSig, ptr);
            ptr += 4;
            if (dirSize == 0) throw new Exception("Central directory empty");

            // Offset of start of central directory, relative to start of archive
            _zipDirPosition = BitConverter.ToInt32(dirSig, ptr);
            if (_zipDirPosition == 0) throw new Exception("Central directory empty");

            // create new zip directory array
            ZipDir = new List<FileHeader>(dirEntriesTotal);

            // position file to central directory
            _writeStream.Position = _zipDirPosition;

            // read central directory
            while (dirEntriesTotal-- > 0)
            {
                // file header
                var fh = new FileHeader();

                // Central directory file header signature = 0x02014b50
                int fileDirSig = _writeFileReader.ReadInt32();
                if (fileDirSig != 0x02014b50) throw new Exception("File directory signature error");

                // ReSharper disable once UnusedVariable : Version made by (ignored)
                int versionMadeBy = _writeFileReader.ReadInt16();

                // Low byte is version needed to extract (the low byte should be 20 for version 2.0).
                // High byte is a computer system code that define the extrenal file attribute.
                // If high byte is zero it is DOS compatible.
                fh.Version = _writeFileReader.ReadUInt16();

                //	General purpose bit flag (bit 0 encripted)
                fh.BitFlags = _writeFileReader.ReadUInt16();

                //	Compression method must be deflate or no compression
                fh.CompMethod = _writeFileReader.ReadUInt16();

                //	File last modification time
                fh.FileTime = _writeFileReader.ReadUInt16();

                //	File last modification date
                fh.FileDate = _writeFileReader.ReadUInt16();

                // CRC-32
                fh.FileCRC32 = _writeFileReader.ReadUInt32();

                // Compressed size
                fh.CompSize = _writeFileReader.ReadUInt32();

                // Uncompressed size
                fh.FileSize = _writeFileReader.ReadUInt32();

                // File name length
                int fileNameLen = _writeFileReader.ReadInt16();

                // Extra field length
                int extraFieldLen = _writeFileReader.ReadInt16();

                // File comment length
                int commentLen = _writeFileReader.ReadInt16();

                // Disk number where file starts
                int fileDiskNo = _writeFileReader.ReadInt16();
                if (fileDiskNo != 0) throw new Exception("No support for multi-disk ZIP file");

                // ReSharper disable once UnusedVariable : internal file attributes (ignored)
                int fileIntAttr = _writeFileReader.ReadInt16();

                // external file attributes
                fh.FileAttr = (FileAttributes)_writeFileReader.ReadUInt32();

                // if file system is not FAT or equivalent, erase the attributes
                if ((fh.Version & 0xff00) != 0) fh.FileAttr = 0;

                // file position
                fh.FilePos = _writeFileReader.ReadUInt32();

                // file name
                // read all the bytes of the file name into a byte array
                // extract a string from the byte array using DOS (IBM OEM code page 437)
                // replace the unix forward slash with microsoft back slash
                fh.FileName = Utils.DecodeFilename(_writeFileReader.ReadBytes(fileNameLen));

                // if file attribute is a directory make sure we have terminating backslash
                //			if((FH.FileAttr & FileAttributes.Directory) != 0 && !FH.FileName.EndsWith("\\")) FH.FileName += "\\";

                // find if file name contains a path
                fh.Path = fh.FileName.Contains(Path.DirectorySeparatorChar);

                // if we have a directory, we must have a terminating slash
                if ((fh.FileAttr & FileAttributes.Directory) != 0 && !fh.Path) throw new Exception("Directory name must have a slash");

                // Skip Extra field and File comment
                _writeStream.Position += extraFieldLen + commentLen;

                // add file header to zip directory
                ZipDir.Add(fh);
            }

            // position file to central directory
            _writeStream.Position = _zipDirPosition;

            // remove central directory from this file
            _writeStream.SetLength(_zipDirPosition);

            // sort array
            ZipDir.Sort();
        }

        /// <summary>
        /// Read Bytes Routine
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        /// <param name="endOfFile">if set to <c>true</c> [end of file].</param>
        /// <returns></returns>
        protected override int ReadBytes(byte[] buffer, int pos, int len, out bool endOfFile)
        {
            len = len > _readRemain ? (int)_readRemain : len;
            _readRemain -= (uint)len;
            endOfFile = _readRemain == 0;
            _readFile.Read(buffer, pos, len);
            _readCrc32 = CRC32.Checksum(_readCrc32, buffer, pos, len);

            return len;
        }

        /// <summary>
        /// Write Bytes Routine
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        protected override void WriteBytes(byte[] buffer, int pos, int len)
        {
            _writeFile.Write(buffer, pos, len);
        }

        /// <summary>
        /// Rewind Streams
        /// </summary>
        protected override void RewindStreams()
        {
            // reposition stream file pointer to start of file
            _readStream.Position = 0;

            // uncompressed file length
            _readRemain = (uint)_readStream.Length;

            // reset adler32 checksum
            _readCrc32 = 0;

            // reposition write stream to the new end of file
            _writeStream.SetLength(_writeRewindPosition);
            _writeStream.Position = _writeRewindPosition;
        }

        public override void Dispose()
        {
            // close the write file if it is open
            if (_writeFile != null)
            {
                _writeFile.Dispose();
                _writeFile = null;
            }
        }
    }
}