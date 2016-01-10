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
using UZipDotNet.Support;
using UZipDotNet.Extensions;

namespace UZipDotNet
{
    public class InflateZipFile : InflateMethod
    {
        public static bool IsOpen(InflateZipFile inflate)
        {
            return inflate != null && inflate._readFile != null;
        }

        public String ArchiveName
        {
            get
            {
                return (_readFileName);
            }
        }

        public bool IsEmpty
        {
            get
            {
                return _readFile == null || ZipDir == null || ZipDir.Count == 0;
            }
        }
        public List<FileHeader> ZipDir;
        public long ZipDirPosition;
        public uint ReadTotal;
        public uint WriteTotal;

        private string _readFileName;
        private FileStream _readStream;
        private BinaryReader _readFile;
        private uint _readRemain;

        private string _writeFileName;
        private FileStream _writeStream;
        private BinaryWriter _writeFile;
        private uint _writeCrc32;

        // active ZIP file info
        private FileHeader _fileHeaderInfo;
        private bool _fileTimeAvailable;
        private DateTime _fileModifyTime;
        private DateTime _fileAccessTime;
        private DateTime _fileCreateTime;

        /// <summary>
        /// Initializes a new instance of the <see cref="InflateZipFile"/> class.
        /// </summary>
        /// <param name="readFileName">Name of the zip-file.</param>
        public InflateZipFile(string readFileName)
        {
            OpenZipFile(readFileName);
        }

        /// <summary>
        /// Open ZIP file and read file directory
        /// </summary>
        /// <param name="readFileName">Name of the read file.</param>
        /// <returns></returns>
        /// <exception cref="UZipDotNet.Exception">No support for files over 4GB</exception>
        private void OpenZipFile(string readFileName)
        {
            try
            {
                // save name
                _readFileName = readFileName;

                // open source file for reading
                _readStream = new FileStream(readFileName, FileMode.Open, FileAccess.Read, FileShare.Read);

                // convert stream to binary reader
                _readFile = new BinaryReader(_readStream, Encoding.UTF8);

                // file is too long
                if (_readStream.Length > (Int64)0xffffffff) throw new Exception("No support for files over 4GB");

                // read zip directory
                ReadZipFileDirectory();
            }
            catch (Exception)
            {
                // close the read file if it is open
                Dispose();

                throw;
            }
        }

        /// <summary>
        /// Extract All
        /// </summary>
        /// <param name="rootPathName">Name of the root path.</param>
        /// <param name="overWrite">if set to <c>true</c> [over write].</param>
        /// <returns></returns>
        public void DecompressAll(string rootPathName, bool overWrite)
        {
            // if root path is null, change to empty
            if (rootPathName == null)
                rootPathName = string.Empty;
            // append backslash
            else if (rootPathName.Length > 0 && !rootPathName.EndsWith(Path.DirectorySeparatorChar))
                rootPathName += Path.DirectorySeparatorChar;

            // decompress all files
            foreach (FileHeader fh in ZipDir)
            {
                // if overwrite is false, test if file exists
                if (!overWrite && File.Exists(rootPathName + fh.FileName)) continue;

                // decompress one file
                Decompress(fh, rootPathName, null, true, overWrite);
            }
        }

        /// <summary>
        /// Decompress ZIP file
        /// </summary>
        /// <param name="fileHeader">The fh.</param>
        /// <param name="rootPathName">Name of the root path, save the decompressed file in this directory.</param>
        /// <param name="newFileName">New name of the file, if this file name is given it overwrite the name in the FH structure</param>
        /// <param name="createPath">if set to <c>true</c> [create path].</param>
        /// <param name="overWrite">if set to <c>true</c> [over write].</param>
        /// <exception cref="UZipDotNet.Exception">
        /// Extract file failed. Invalid directory path
        /// or
        /// Unsupported compression method
        /// or
        /// ZIP file CRC test failed
        /// </exception>
        public void Decompress(FileHeader fileHeader, string rootPathName, string newFileName, bool createPath, bool overWrite)
        {
            try
            {
                // save file header
                _fileHeaderInfo = fileHeader;

                // read file header for this file and compare it to the directory information
                ReadFileHeader(_fileHeaderInfo.FilePos);

                // compressed length
                _readRemain = _fileHeaderInfo.CompSize;
                ReadTotal = _readRemain;

                // build write file name
                // Root name (optional) plus either original name or a new name
                _writeFileName = (string.IsNullOrEmpty(rootPathName) ? string.Empty :
                    (rootPathName.EndsWith(Path.DirectorySeparatorChar) ? rootPathName : rootPathName + Path.DirectorySeparatorChar)) +
                    (string.IsNullOrEmpty(newFileName) ? fileHeader.FileName : newFileName);

                // test if write file name has a path component
                int ptr = _writeFileName.LastIndexOf(Path.DirectorySeparatorChar);
                if (ptr >= 0)
                {
                    // make sure directory exists
                    if (!Directory.Exists(_writeFileName.Substring(0, ptr)))
                    {
                        // make a new folder
                        if (createPath) Directory.CreateDirectory(_writeFileName.Substring(0, ptr));

                        // error
                        else throw new Exception("Extract file failed. Invalid directory path");
                    }
                }

                // create destination file
                _writeStream = new FileStream(_writeFileName, overWrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);

                // convert stream to binary writer
                _writeFile = new BinaryWriter(_writeStream, Encoding.UTF8);

                // reset crc32 checksum
                _writeCrc32 = 0;

                // switch based on compression method
                switch (_fileHeaderInfo.CompMethod)
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
                        throw new Exception("Unsupported compression method");
                }

                // Zip file checksum is CRC32
                if (_fileHeaderInfo.FileCRC32 != _writeCrc32) throw new Exception("ZIP file CRC test failed");

                // save file length
                WriteTotal = (uint)_writeStream.Length;

                // close write file
                _writeFile.Dispose();
                _writeFile = null;

                // if file times are available set the file time
                if (_fileTimeAvailable)
                {
                    File.SetCreationTime(_writeFileName, _fileCreateTime);
                    File.SetLastWriteTime(_writeFileName, _fileModifyTime);
                    File.SetLastAccessTime(_writeFileName, _fileAccessTime);
                }
                else
                {
                    // convert dos file date and time to DateTime format
                    var fileDosTime = new DateTime(1980 + ((_fileHeaderInfo.FileDate >> 9) & 0x7f),
                        (_fileHeaderInfo.FileDate >> 5) & 0xf, _fileHeaderInfo.FileDate & 0x1f,
                        (_fileHeaderInfo.FileTime >> 11) & 0x1f, (_fileHeaderInfo.FileTime >> 5) & 0x3f, 2 * (_fileHeaderInfo.FileTime & 0x1f));
                    File.SetCreationTime(_writeFileName, fileDosTime);
                    File.SetLastWriteTime(_writeFileName, fileDosTime);
                    File.SetLastAccessTime(_writeFileName, fileDosTime);
                }

                // set file attribute attributes
                if (_fileHeaderInfo.FileAttr != 0) File.SetAttributes(_writeFileName, _fileHeaderInfo.FileAttr);
            }
            catch (Exception)
            {
                // close the write file if it is open
                if (_writeFile != null)
                {
                    _writeFile.Dispose();
                    _writeFile = null;
                }

                throw;
            }
        }

        /// <summary>
        /// Read Len bytes and convert to String
        /// </summary>
        /// <param name="len">The length.</param>
        /// <returns></returns>
        private string GetFileName(int len)
        {
            // empty string
            if (len == 0)
                return (string.Empty);

            // convert byts to char and build a string
            var fileName = new StringBuilder(len)
            {
                Length = len
            };
            for (int ptr = 0; ptr < len; ptr++) fileName[ptr] = (char)_readFile.ReadByte();
            return (fileName.ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Read ZIP file directory
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
            long fileLen = _readStream.Length;
            if (fileLen < 98) throw new Exception("ZIP file is too short");

            // read last 512 byte block at the end of the file
            if (fileLen > 512) _readStream.Position = fileLen - 512;
            byte[] dirSig = _readFile.ReadBytes(512);

            // look for signature
            int ptr;
            for (ptr = dirSig.Length - 20; ptr >= 0 && BitConverter.ToUInt32(dirSig, ptr) != 0x06054b50; ptr--) ;
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

            // number of central directory records on this disk
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
            ZipDirPosition = BitConverter.ToInt32(dirSig, ptr);
            if (ZipDirPosition == 0) throw new Exception("Central directory empty");

            // create result array
            ZipDir = new List<FileHeader>(dirEntriesTotal);

            // position file to central directory
            _readStream.Position = ZipDirPosition;

            // read central directory
            while (dirEntriesTotal-- > 0)
            {
                // file header
                var fh = new FileHeader();

                // Central directory file header signature = 0x02014b50
                int FileDirSig = _readFile.ReadInt32();
                if (FileDirSig != 0x02014b50) throw new Exception("File directory signature error");

                // ReSharper disable once UnusedVariable : Version made by (ignored)
                int versionMadeBy = _readFile.ReadInt16();

                // Low byte is version needed to extract (the low byte should be 20 for version 2.0).
                // High byte is a computer system code that define the extrenal file attribute.
                // If high byte is zero it is DOS compatible.
                fh.Version = _readFile.ReadUInt16();

                //	General purpose bit flag (bit 0 encripted)
                fh.BitFlags = _readFile.ReadUInt16();

                //	Compression method must be deflate or no compression
                fh.CompMethod = _readFile.ReadUInt16();

                //	File last modification time
                fh.FileTime = _readFile.ReadUInt16();

                //	File last modification date
                fh.FileDate = _readFile.ReadUInt16();

                // CRC-32
                fh.FileCRC32 = _readFile.ReadUInt32();

                // Compressed size
                fh.CompSize = _readFile.ReadUInt32();

                // Uncompressed size
                fh.FileSize = _readFile.ReadUInt32();

                // File name length
                int fileNameLen = _readFile.ReadInt16();

                // Extra field length
                int extraFieldLen = _readFile.ReadInt16();

                // File comment length
                int commentLen = _readFile.ReadInt16();

                // Disk number where file starts
                int fileDiskNo = _readFile.ReadInt16();
                if (fileDiskNo != 0) throw new Exception("No support for multi-disk ZIP file");

                // ReSharper disable once UnusedVariable : internal file attributes (ignored)
                int fileIntAttr = _readFile.ReadInt16();

                // external file attributes
                fh.FileAttr = (FileAttributes)_readFile.ReadUInt32();

                // if file system is not FAT or equivalent, erase the attributes
                if ((fh.Version & 0xff00) != 0) fh.FileAttr = 0;

                // file position
                fh.FilePos = _readFile.ReadUInt32();

                // file name
                // read all the bytes of the file name into a byte array
                // extract a string from the byte array using DOS (IBM OEM code page 437)
                // replace the unix forward slash with microsoft back slash
                fh.FileName = Utils.DecodeFilename(_readFile.ReadBytes(fileNameLen));

                // if file attribute is a directory make sure we have terminating backslash
                //			if((FH.FileAttr & FileAttributes.Directory) != 0 && !FH.FileName.EndsWith("\\")) FH.FileName += "\\";

                // find if file name contains a path
                fh.Path = fh.FileName.Contains(Path.DirectorySeparatorChar);

                // if we have a directory, we must have a terminating slash
                if ((fh.FileAttr & FileAttributes.Directory) != 0 && !fh.Path) throw new Exception("Directory name must have a slash");

                // Skip Extra field and File comment
                _readStream.Position += extraFieldLen + commentLen;

                // add file header to zip directory
                ZipDir.Add(fh);
            }

            // sort array
            ZipDir.Sort();
        }

        /// <summary>
        /// ZIP file header
        /// </summary>
        /// <param name="headerPosition">The header position.</param>
        /// <exception cref="UZipDotNet.Exception">
        /// Zip file signature in error
        /// or
        /// File header error
        /// </exception>
        private void ReadFileHeader(uint headerPosition)
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
            _fileTimeAvailable = false;

            // set initial position
            _readStream.Position = headerPosition;

            // local file header signature
            if (_readFile.ReadUInt32() != 0x04034b50) throw new Exception("Zip file signature in error");

            // NOTE: The program uses the file header information from the ZIP directory
            // at the end of the file. The local file header is ignored except for file times.
            // One can skip 22 bytes instead of reading these fields.
            // ReadStream.Position += 22;

            // version needed to extract and file system for external file attributes
            // ReSharper disable once UnusedVariable
            ushort version = _readFile.ReadUInt16();

            // general purpose bit flag
            // ReSharper disable once UnusedVariable
            ushort bitFlags = _readFile.ReadUInt16();

            // compression method
            // ReSharper disable once UnusedVariable
            ushort compMethod = _readFile.ReadUInt16();

            // last mod file time
            // ReSharper disable once UnusedVariable
            ushort fileTime = _readFile.ReadUInt16();

            // last mod file date
            // ReSharper disable once UnusedVariable
            ushort fileDate = _readFile.ReadUInt16();

            // crc-32
            // ReSharper disable once InconsistentNaming
            uint fileCRC32 = _readFile.ReadUInt32();

            // test local file header against zip directory file header
            if (_fileHeaderInfo.FileCRC32 != fileCRC32)
                throw new Exception("File header error");

            // compressed size
            // ReSharper disable once UnusedVariable
            uint compSize = _readFile.ReadUInt32();

            // uncompressed size
            // ReSharper disable once UnusedVariable
            uint fileSize = _readFile.ReadUInt32();

            // file name length
            int fileNameLen = _readFile.ReadInt16();

            // extra field length
            int extraFieldLen = _readFile.ReadInt16();

            // file name
            // read all the bytes of the file name into a byte array
            // extract a string from the byte array using DOS (IBM OEM code page 437)
            // replace the unix forward slash with microsoft back slash
            string fileName = Utils.DecodeFilename(_readFile.ReadBytes(fileNameLen));

            // if extra field length is 36 get file times
            if (extraFieldLen == 36)
            {
                // get the extra field
                byte[] timeField = _readFile.ReadBytes(36);

                // make sure it is NTFS tag of length 32, reserved area of zero, File times tag of 1 with length 24
                if (BitConverter.ToUInt32(timeField, 0) == 0x0020000a &&
                    BitConverter.ToUInt32(timeField, 4) == 0 &&
                    BitConverter.ToUInt32(timeField, 8) == 0x00180001)
                {
                    _fileModifyTime = DateTime.FromFileTime(BitConverter.ToInt64(timeField, 12));
                    _fileAccessTime = DateTime.FromFileTime(BitConverter.ToInt64(timeField, 20));
                    _fileCreateTime = DateTime.FromFileTime(BitConverter.ToInt64(timeField, 28));
                    _fileTimeAvailable = true;
                }
            }

            // skip extra field
            else
            {
                _readStream.Position += extraFieldLen;
            }
        }

        /// <summary>
        ///  Read Bytes Routine
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

            return (_readFile.Read(buffer, pos, len));
        }

        /// <summary>
        /// Write Bytes Routine
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="pos">The position.</param>
        /// <param name="len">The length.</param>
        protected override void WriteBytes(Byte[] buffer, int pos, int len)
        {
            _writeCrc32 = CRC32.Checksum(_writeCrc32, buffer, pos, len);
            _writeFile.Write(buffer, pos, len);
        }

        public override void Dispose()
        {
            // close the read file if it is open
            if (_readFile != null)
            {
                _readFile.Dispose();
                _readFile = null;
            }

            // remove directory
            if (ZipDir != null)
            {
                ZipDir.Clear();
                ZipDir = null;
            }
        }
    }
}