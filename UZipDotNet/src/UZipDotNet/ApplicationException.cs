using System;

namespace UZipDotNet
{
    public class ApplicationException : Exception
    {
        public ApplicationException(string message) : base(message)
        {
            
        }
    }
}