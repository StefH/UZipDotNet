/////////////////////////////////////////////////////////////////////
//
//	UZipDotNet
//	ZIP File processing
//
//	ExceptionReport.cs
//	Class designed to produce a meaningful error message from
//	Exception class. The compress and decompress routines are
//	enclosed by a try block. If the code generate an exception
//	the catch clause will produce an Exception object. The
//	ExceptionReport class translates this message to an error
//	message including the source module and line number that
//	generated the throw command.
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

namespace UZipDotNet
{
public static class ExceptionReport
	{
	/////////////////////////////////////////////////////////////////////
	// Get exception message and exception stack
	/////////////////////////////////////////////////////////////////////

	public static String[] GetMessageAndStack
			(
			Object			Sender,
			Exception		Ex
			)
		{
		// get system stack at the time of exception
		String StackTraceStr = Ex.StackTrace;

		// break it into individual lines
		String[] StackTraceLines = StackTraceStr.Split(new Char[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);

		// count all lines containing the name space of this program
		Int32 Count = 0;
		foreach(String Line in StackTraceLines) if(Line.Contains(Sender.GetType().Namespace)) Count++;

		// create a new array of trace lines
		String[] StackTrace = new String[Count + 1];

		// exception error message
		StackTrace[0] = Ex.Message;
		Trace.Write(Ex.Message);

		// add trace lines
		Int32 Index = 0;
		foreach(String Line in StackTraceLines) if(Line.Contains(Sender.GetType().Namespace))
			{
			StackTrace[++Index] = Line;
			Trace.Write(Line);
			}

		// error exit
		return(StackTrace);
		}
	}
}
