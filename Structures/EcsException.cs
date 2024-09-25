using System;

namespace CodexECS
{
    class EcsException : Exception
    {
        public EcsException(string msg) : base(msg) { }
    }
}