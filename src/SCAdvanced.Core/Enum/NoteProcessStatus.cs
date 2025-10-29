using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCAdvanced.Core.Emun
{
    public enum NoteProcessStatus
    {
        Accepted,     // empilhada  e creditada
        Returned,     // devolvida 
        Rejected,     // rejeitada por política (ex.: troco) ou pelo validador
        Error         // erro 
    }
}
