using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCAdvanced.Core.Model
{
    public class NoteEventArgs : EventArgs
    {
        public Note Note { get; }
        public NoteEventArgs(Note note) => Note = note;
    }
}
