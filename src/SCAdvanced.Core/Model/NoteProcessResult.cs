using SCAdvanced.Core.Emun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCAdvanced.Core.Model
{
    public class NoteProcessResult
    {
        public NoteProcessStatus Status { get; init; }
        public string Currency { get; init; } = "BRL";
        public decimal Value { get; init; }
        public string? ErrorCode { get; init; }     
        public string? Message { get; init; }     
    }
}
