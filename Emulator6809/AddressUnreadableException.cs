using System;


namespace Emulator6809
{
    /// <summary>
    /// Exception lancée quand une opération de lecture dans l'espace-mémoire
    /// échoue, bloquant ainsi une opération critique.
    /// </summary>
    class AddressUnreadableException : Exception
    {
        /* ========================= CHAMPS PRIVÉS ========================== */

        private readonly int addr;

        /* ========================= CONSTRUCTEURS ========================== */

        public AddressUnreadableException(int address) : base()
        {
            this.addr = address;
        }

        public AddressUnreadableException(int address, string message) : base(message)
        {
            this.addr = address;
        }

        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Adresse-mémoire n'ayant pu être lue.
        /// (Propriété en lecture seule.)
        /// </summary>
        public UInt16 MemoryAddress
        {
            get { return (ushort)(this.addr); }
        }

    }
}

