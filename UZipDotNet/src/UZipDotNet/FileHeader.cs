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
        public string FileName;		    // file name with or without path
        // file name will not start with drive letter or server name
        // in other words D:\ or \ or \\server-name\ are invalid
        public bool Path;			    // File name contains path
        public uint FilePos;		    // file header position within the zip file
        public ushort FileTime;		    // file time in dos format hhhhhmmmmmmsssss (seconds are in 2 sec increments)
        public ushort FileDate;		    // file date in dos format yyyyyyymmmmddddd (years since 1980)
        public FileAttributes FileAttr; // file attributes (read only=1, hidden=2, system= 4, directory=8)
        public uint FileSize;		    // uncompressed file size (4GB max)
        public uint CompSize;		    // compressed file size. This nnumber does not include the file header.
        public uint FileCRC32;		    // uncompressed file CRC32 checksum
        public ushort CompMethod;		// compression method. This program supports 0-no compression and 8-deflate method
        public ushort BitFlags;		    // This program expects zero (see PKWARE ZIP file format specifications section "J. Explanation of fields"
        public ushort Version;		    // low byte=version, high byte=file system

        // This program was design to read and write ZIP files compatible with
        // Microsoft's Windows explorer Send To Compressed (Zipped) folder function.
        // Windows explorer sets the version to 20 and file system to 0.
        // According to PKWARE this corresponds to version 2.0.
        // File system: MS-DOS and OS/2 (FAT / VFAT / FAT32 file systems)

        /// <summary>
        /// Initializes a new instance of the <see cref="FileHeader"/> class.
        /// </summary>
        public FileHeader()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileHeader"/> class.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="lastWriteTime">The last write time.</param>
        /// <param name="fileAttr">The file attribute.</param>
        /// <param name="filePos">The file position.</param>
        /// <param name="fileSize">Size of the file.</param>
        /// <exception cref="Exception">
        /// Invalid file name
        /// or
        /// No support for files over 4GB
        /// or
        /// No support for files over 4GB
        /// </exception>
        public FileHeader(string fileName, DateTime lastWriteTime, FileAttributes fileAttr, long filePos, long fileSize)
        {
            // test file name (error if file starts with c:\ or \ or \\name\)
            if (fileName[0] == '\\' || fileName[1] == ':')
            {
                throw new ArgumentException("Invalid file name", "fileName");
            }

            // file name				
            FileName = fileName;

            // file name has a path component
            Path = FileName.Contains("\\");

            // last mod file time
            FileTime = (ushort)((lastWriteTime.Hour << 11) | (lastWriteTime.Minute << 5) | (lastWriteTime.Second / 2));

            // last mod file date
            FileDate = (ushort)(((lastWriteTime.Year - 1980) << 9) | (lastWriteTime.Month << 5) | lastWriteTime.Day);

            // file attributes
            FileAttr = fileAttr & (FileAttributes.Archive | FileAttributes.Directory |
                FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);

            // file position
            if (filePos > 0xffffffff) throw new Exception("No support for files over 4GB"); // TODO throw IOException ?
            FilePos = (uint)filePos;

            // file length
            if (fileSize > 0xffffffff) throw new Exception("No support for files over 4GB"); // TODO throw IOException ?
            FileSize = (uint)fileSize;

            // version
            Version = 20;
        }

        /// <summary>
        /// Compare To for sort by name
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has the following meanings: Value Meaning Less than zero This object is less than the <paramref name="other" /> parameter.Zero This object is equal to <paramref name="other" />. Greater than zero This object is greater than <paramref name="other" />.
        /// </returns>
        public Int32 CompareTo(FileHeader other)
        {
            if (Path != other.Path)
            {
                return (Path ? 1 : -1);
            }

            // TODO : string.Compare is Culture Specific
            return string.Compare(FileName, other.FileName);
        }

        /// <summary>
        /// Compare To for sort by position
        /// </summary>
        /// <param name="one">The one.</param>
        /// <param name="other">The other.</param>
        /// <returns></returns>
        public static int CompareByPosition(FileHeader one, FileHeader other)
        {
            // this should not happen
            if (one.FilePos == other.FilePos)
            {
                return 0;
            }

            // FilePos is UInt32 this is the reason for not using One.FilePos - Other.FilePos
            return one.FilePos > other.FilePos ? 1 : -1;
        }
    }
}