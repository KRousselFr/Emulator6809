using System;


namespace Emulator6809
{
    /// <summary>
    /// Exception lancée quand une opération d'écriture dans l'espace-mémoire
    /// échoue, bloquant ainsi une opération critique.
    /// </summary>
    class AddressUnwritableException : Exception
    {
        /* ========================= CHAMPS PRIVÉS ========================== */

        private readonly int addr;

        /* ========================= CONSTRUCTEURS ========================== */

        public AddressUnwritableException(int address) : base()
        {
            this.addr = address;
        }

        public AddressUnwritableException(int address, string message) : base(message)
        {
            this.addr = address;
        }

        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Adresse-mémoire n'ayant pu être écrite.
        /// (Propriété en lecture seule.)
        /// </summary>
        public UInt16 MemoryAddress
        {
            get { return (ushort)(this.addr); }
        }

    }
}

