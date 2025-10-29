using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCAdvanced.Core.Model.Emun
{
    public enum EbdsMessageType : byte
    {
        Type1_OmnibusCommand = 0x1,
        Type2_OmnibusReply = 0x2,
        Type6_Auxiliary = 0x6,
        Type7_Extended = 0x7,
    }
}
