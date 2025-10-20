using System;


namespace Emulator6809
{
    /// <summary>
    /// Exception lancée lorsqu'un opcode invalide est rencontré à l'exécution.
    /// </summary>
    public class UnknownOpcodeException : Exception
    {
        /* ========================= CHAMPS PRIVÉS ========================== */

        private readonly int addr;
        private readonly byte code;

        /* ========================= CONSTRUCTEURS ========================== */

        public UnknownOpcodeException(int address, Byte opcode) : base()
        {
            this.addr = address;
            this.code = opcode;
        }

        public UnknownOpcodeException(int address, Byte opcode, string message) : base(message)
        {
            this.addr = address;
            this.code = opcode;
        }

        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Adresse-mémoire où l'opcode invalide a été lu.
        /// (Propriété en lecture seule.)
        /// </summary>
        public UInt16 MemoryAddress
        {
            get { return (ushort)(this.addr); }
        }

        /// <summary>
        /// Opcode invalide lu en mémoire.
        /// (Propriété en lecture seule.)
        /// </summary>
        public Byte Opcode
        {
            get { return this.code; }
        }

    }
}


