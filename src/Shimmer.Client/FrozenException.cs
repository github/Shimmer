using System;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace Shimmer.Client
{
    // this is actually an expensive operation
    // but we want to preserve the stack trace
    // and rethrow it later

    // unfortunately we don't have .NET45 here
    // and Observable.Throw doesn't care
    // so we need to snapshot and hold onto it
    public abstract class FrozenException : Exception
    {
        readonly string stackTrace;

        protected FrozenException()
        {
            var trace = new StackTrace(2, true);
            stackTrace = trace.ToString();
        }

        protected FrozenException(string message)
            : base(message)
        {
            var trace = new StackTrace(2, true);
            stackTrace = trace.ToString();
        }

        protected FrozenException(string message, Exception innerException)
            : base(message, innerException)
        {
            var trace = new StackTrace(2, true);
            stackTrace = trace.ToString();
        }

        protected FrozenException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            var trace = new StackTrace(2, true);
            stackTrace = trace.ToString();
        }

        public override string StackTrace
        {
            get { return stackTrace; }
        }
    }
}