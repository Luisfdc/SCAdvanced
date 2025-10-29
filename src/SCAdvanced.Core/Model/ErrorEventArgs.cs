using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCAdvanced.Core.Model
{
    public class ErrorEventArgs : EventArgs
    {
        public string Code { get; }
        public string Message { get; }
        public ErrorEventArgs(string code, string message) { Code = code; Message = message; }
    }
}
