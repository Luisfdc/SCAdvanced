using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SCAdvanced.Core.Contracts
{
    public interface IBillAcceptorPort : IDisposable
    {
        void Open();
        void Close();

        Model.DeviceInfo GetInfo();

        void ConfigureInhibits(int[] acceptedDenominations); 
        void EnableExtendedEscrowPupA();
        void EnableAccept();
        void DisableAccept();

        void Stack();
        void Return();

        event EventHandler<Model.NoteEventArgs> Escrow;   
        event EventHandler<Model.NoteEventArgs> Stacked;  
        event EventHandler<Model.ErrorEventArgs> Error;   
    }
}
