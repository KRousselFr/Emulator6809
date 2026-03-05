using System;


namespace Emulator6809
{
    /// <summary>
    /// Classe émulant un circuit PIA ("Peripheral Interface Adapter")
    /// de type Motorola 6820 / 6821, ou MOS 6520 / WDC 65C21.
    /// </summary>
    public class PIA6820
    {
        /* =========================== CONSTANTES =========================== */

        /* ~~~~ masques de bits pour les registres de contrôle ~~~~ */
        public const byte CR_MASK_IRQ1 = 0x80;
        public const byte CR_MASK_IRQ2 = 0x40;
        public const byte CR_MASK_C2_DIR = 0x20;
        public const byte CR_MASK_C2_CTRL = 0x38;
        public const byte CR_MASK_DDR_ACCESS = 0x04;
        public const byte CR_MASK_C1_CTRL = 0x03;


        /* ========================== CHAMPS PRIVÉS ========================= */

        /* ~~~~ registres ~~~~ */

        // registres de données (ports A et B)
        private byte regPDA;
        private byte regPDB;
        // "flag" de modification des registres de données
        private bool donneesAmodif;
        private bool donneesBmodif;
        // registres de direction de lignes (ports A et B)
        private byte regDDRA;
        private byte regDDRB;
        // registres de contrôle (ports A et B)
        private byte regCRA;
        private byte regCRB;
        // "flag" d'accès à un registre quelconque
        private bool accesPIA;

        /* ~~ ports et lignes externes */

        // ports d'entrées et/ou sorties sur 8 bits
        private byte portA;
        private byte portB;
        // sorties pour demande d'interruption
        private bool irqA;
        private bool irqB;
        // entrées
        private bool ca1, ca1old;
        private bool cb1, cb1old;
        // entrées-sorties
        private bool ca2, ca2old;
        private bool cb2, cb2old;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur de référence (et unique) de la classe PIA6820.
        /// </summary>
        public PIA6820()
        {
            Reset();
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        /* ~~~~ utilitaires ~~~~ */

        private void UpdateLines()
        {
            byte modif;
            /* définit la valeur des bits des ports en sortie */
            this.portA &= (byte)(~this.regDDRA);
            modif = (byte)(this.regPDA & this.regDDRA);
            this.portA |= modif;
            this.portB &= (byte)(~this.regDDRB);
            modif = (byte)(this.regPDB & this.regDDRB);
            this.portB |= modif;

            /* valeurs des bits CR7 et CR6, et des lignes CA2 et CB2 */
            byte modeCA1 = (byte)(this.regCRA & CR_MASK_C1_CTRL);
            switch (modeCA1) {
                case 0:
                case 1:
                    /* valeurs 0 et 1 : bit 7 de CRA activé par
                       une transition négative de la ligne de CA1 */
                    if (!this.ca1 && this.ca1old) {
                        this.regCRA |= CR_MASK_IRQ1;
                    }
                    break;
                case 2:
                case 3:
                    /* valeurs 2 et 3 : bit 7 de CRA activé par
                       une transition positive de la ligne de CA1 */
                    if (this.ca1 && !this.ca1old) { 
                        this.regCRA |= CR_MASK_IRQ1;
                    }
                    break;
            }
            byte modeCB1 = (byte)(this.regCRB & CR_MASK_C1_CTRL);
            switch (modeCB1) {
                case 0:
                case 1:
                    /* valeurs 0 et 1 : bit 7 de CRB activé par
                       une transition négative de la ligne de CB1 */
                    if (!this.cb1 && this.cb1old) {
                        this.regCRB |= CR_MASK_IRQ1;
                    }
                    break;
                case 2:
                case 3:
                    /* valeurs 2 et 3 : bit 7 de CRB activé par
                       une transition positive de la ligne de CB1 */
                    if (this.cb1 && !this.cb1old) {
                        this.regCRB |= CR_MASK_IRQ1;
                    }
                    break;
            }
            byte modeCA2 = (byte)((this.regCRA & CR_MASK_C2_CTRL) >> 3);
            switch (modeCA2) {
                case 0:
                case 1:
                    /* valeurs 0 et 1 : bit 6 de CRA activé par
                       une transition négative de la ligne de CA2 */
                    if (!this.ca2 && this.ca2old) {
                        this.regCRA |= CR_MASK_IRQ2;
                    }
                    break;
                case 2:
                case 3:
                    /* valeurs 2 et 3 : bit 6 de CRA activé par
                       une transition positive de la ligne de CA2 */
                    if (this.ca2 && !this.ca2old) {
                        this.regCRA |= CR_MASK_IRQ2;
                    }
                    break;
                case 4:
                    /* valeur 4 : CA2 désactivée par une modif 
                       du registre de données du port A ;
                       CA2 activée par une activation de CA1 */
                    if (this.donneesAmodif) this.ca2 = false;
                    if (this.ca1 && !this.ca1old) this.ca2 = true;
                    break;
                case 5:
                    /* valeur 5 : CA2 désactivée par une modif 
                       du registre de données du port A ;
                       CA2 activée par l'accès aux registres du PIA */
                    if (this.donneesAmodif) this.ca2 = false;
                    if (this.accesPIA) this.ca2 = true;
                    break;
                case 6:
                    /* valeur 6 : CA2 mis à 0 */
                    this.ca2 = false;
                    break;
                case 7:
                    /* valeur 7 : CA2 mis à 1 */
                    this.ca2 = true;
                    break;
            }
            byte modeCB2 = (byte)((this.regCRB & CR_MASK_C2_CTRL) >> 3);
            switch (modeCB2) {
                case 0:
                case 1:
                    /* valeurs 0 et 1 : bit 6 de CRB activé par
                       une transition négative de la ligne de CB2 */
                    if (!this.cb2 && this.cb2old) {
                        this.regCRB |= CR_MASK_IRQ2;
                    }
                    break;
                case 2:
                case 3:
                    /* valeurs 2 et 3 : bit 6 de CRB activé par
                       une transition positive de la ligne de CB2 */
                    if (this.cb2 && !this.cb2old) {
                        this.regCRB |= CR_MASK_IRQ2;
                    }
                    break;
                case 4:
                    /* valeur 4 : CB2 désactivée par une modif 
                       du registre de données du port B ;
                       CB2 activée par une activation de CB1 */
                    if (this.donneesBmodif) this.cb2 = false;
                    if (this.cb1 && !this.cb1old) this.cb2 = true;
                    break;
                case 5:
                    /* valeur 5 : CB2 désactivée par une modif 
                       du registre de données du port B ;
                       CB2 activée par l'accès aux registres du PIA */
                    if (this.donneesBmodif) this.cb2 = false;
                    if (this.accesPIA) this.cb2 = true;
                    break;
                case 6:
                    /* valeur 6 : CB2 mis à 0 */
                    this.cb2 = false;
                    break;
                case 7:
                    /* valeur 7 : CB2 mis à 1 */
                    this.cb2 = true;
                    break;
            }

            /* mise à jour des anciennes valeurs des lignes */
            this.ca1old = this.ca1;
            this.cb1old = this.cb1;
            this.ca2old = this.ca2;
            this.cb2old = this.cb2;

            /* définit les lignes IRQA / IRQB en fonction de CRA / CRB */
            this.irqA = false;
            switch (modeCA1) {
                case 1:
                case 3:
                    /* valeur 1 ou 3 : IRQ lancée par activation de CRA(7) */
                    if ((this.regCRA & CR_MASK_IRQ1) != 0)
                        this.irqA = true;
                    break;
                default:
                    /* valeurs 0 et 2 : IRQ désactivée */
                    break;
            }
            switch (modeCA2) {
                case 1:
                case 3:
                    /* valeur 1 ou 3 : IRQ lancée par activation de CRA(6) */
                    if ((this.regCRA & CR_MASK_IRQ2) != 0)
                        this.irqA = true;
                    break;
                default:
                    /* valeurs 0 et 2 : IRQ désactivée */
                    break;
            }
            this.irqB = false;
            switch (modeCB1) {
                case 1:
                case 3:
                    /* valeur 1 ou 3 : IRQ lancée par activation de CRB(7) */
                    if ((this.regCRB & CR_MASK_IRQ1) != 0)
                        this.irqB = true;
                    break;
                default:
                    /* valeurs 0 et 2 : IRQ désactivée */
                    break;
            }
            switch (modeCA2) {
                case 1:
                case 3:
                    /* valeur 1 ou 3 : IRQ lancée par activation de CRB(6) */
                    if ((this.regCRB & CR_MASK_IRQ2) != 0)
                        this.irqB = true;
                    break;
                default:
                    /* valeurs 0 et 2 : IRQ désactivée */
                    break;
            }

            /* RàZ des "flags" */
            this.donneesAmodif = this.donneesBmodif = false;
            this.accesPIA = false;
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Réinitialise la PIA.
        /// </summary>
        public void Reset()
        {
            /* RàZ de tous les registres */
            this.regPDA  = this.regPDB  = 0;
            this.regDDRA = this.regDDRB = 0;
            this.regCRA  = this.regCRB  = 0;
            /* RàZ des "flags" */
            this.donneesAmodif = this.donneesBmodif = false;
            this.accesPIA = false;
            /* RAZ des ports */
            this.portA = this.portB = 0;
            /* RàZ des lignes liées aux interruptions */
            this.ca1 = this.ca1old = false;
            this.cb1 = this.cb1old = false;
            this.ca2 = this.ca2old = false;
            this.cb2 = this.cb2old = false;
            this.irqA = false;
            this.irqB = false;
        }


        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Accès aux registreS DDRA ou PDA du circuit PIA.
        /// </summary>
        public Byte BaseRegisterA
        {
            get {
                this.accesPIA = true;
                UpdateLines();
                if ((this.regCRA & CR_MASK_DDR_ACCESS) != 0) {
                    return this.regPDA;
                } else {
                    return this.regDDRA;
                }
            }
            set {
                this.accesPIA = true;
                if ((this.regCRA & CR_MASK_DDR_ACCESS) != 0) {
                    this.regPDA = value;
                } else {
                    this.regDDRA = value;
                }
                UpdateLines();
            }
        }

        /// <summary>
        /// Accès au registre CRA du circuit PIA.
        /// </summary>
        public Byte ControlRegisterA
        {
            get {
                this.accesPIA = true;
                UpdateLines();
                return this.regCRA;
            }
            set {
                this.accesPIA = true;
                // les deux bits de poids fort sont en lecture seule
                this.regCRA &= 0xc0;
                byte modif = (byte)(value & 0x3f);
                this.regCRA |= modif;
                UpdateLines();
            }
        }

        /// <summary>
        /// Accès aux registreS DDRB ou PDB du circuit PIA.
        /// </summary>
        public Byte BaseRegisterB
        {
            get {
                this.accesPIA = true;
                UpdateLines();
                if ((this.regCRB & CR_MASK_DDR_ACCESS) != 0) {
                    return this.regPDB;
                } else {
                    return this.regDDRB;
                }
            }
            set {
                this.accesPIA = true;
                if ((this.regCRB & CR_MASK_DDR_ACCESS) != 0) {
                    this.regPDB = value;
                } else {
                    this.regDDRB = value;
                }
                UpdateLines();
            }
        }

        /// <summary>
        /// Accès au registre CRB du circuit PIA.
        /// </summary>
        public Byte ControlRegisterB
        {
            get {
                this.accesPIA = true;
                UpdateLines();
                return this.regCRB;
            }
            set {
                this.accesPIA = true;
                // les deux bits de poids fort sont en lecture seule
                this.regCRB &= 0xc0;
                byte modif = (byte)(value & 0x3f);
                this.regCRB |= modif;
                UpdateLines();
            }
        }


        /// <summary>
        /// Valeur binaire brute des lignes du port A.
        /// </summary>
        public Byte PortAvalue
        {
            get { return this.portA; }
            set {
                // conserve la valeur des bits mis en sortie
                this.portA &= this.regDDRA;
                // on définit seulement les bits mis en entrée
                byte modif = (byte)(value & ~this.regDDRA);
                this.portA |= modif;
            }
        }

        /// <summary>
        /// Valeur binaire brute des lignes du port B.
        /// </summary>
        public Byte PortBvalue
        {
            get { return this.portB; }
            set {
                // conserve la valeur des bits mis en sortie
                this.portB &= this.regDDRB;
                // on définit seulement les bits mis en entrée
                byte modif = (byte)(value & ~this.regDDRB);
                this.portB |= modif;
            }
        }

        /// <summary>
        /// Valeur active / inactive de la ligne CA1
        /// (en entrée uniquement).
        /// </summary>
        public Boolean CA1line
        {
            get { return this.ca1; }
            set {
                this.ca1 = value;
                UpdateLines();
            }
        }

        /// <summary>
        /// Valeur active / inactive de la ligne CB1
        /// (en entrée uniquement).
        /// </summary>
        public Boolean CB1line
        {
            get { return this.ca2; }
            set {
                this.ca2 = value;
                UpdateLines();
            }
        }

        /// <summary>
        /// Valeur active / inactive de la ligne CA2
        /// (en entrée ou sortie : si la ligne CA2 est définie
        ///  comme sortie, définir la valeur ici n'aura aucun effet).
        /// </summary>
        public Boolean CA2line
        {
            get { return this.ca2; }
            set {
                // si CA2 est mise en entrée...
                if ((this.regCRA & CR_MASK_C2_DIR) == 0) {
                    // ... la modifier
                    this.ca2 = value;
                    UpdateLines();
                }
                // sinon, ne rien faire !
            }
        }

        /// <summary>
        /// Valeur active / inactive de la ligne CB2
        /// (en entrée ou sortie : si la ligne CB2 est définie
        ///  comme sortie, définir la valeur ici n'aura aucun effet).
        /// </summary>
        public Boolean CB2line
        {
            get { return this.cb2; }
            set {
                // si CA2 est mise en entrée...
                if ((this.regCRB & CR_MASK_C2_DIR) == 0) {
                    // ... la modifier
                    this.cb2 = value;
                    UpdateLines();
                }
                // sinon, ne rien faire !
            }
        }

        /// <summary>
        /// Valeur active / inactive de la ligne IRQA
        /// (sortie uniquement, définie par l'état du PIA).
        /// </summary>
        public Boolean IRQAline
        {
            get { return this.irqA; }
        }

        /// <summary>
        /// Valeur active / inactive de la ligne IRQB
        /// (sortie uniquement, définie par l'état du PIA).
        /// </summary>
        public Boolean IRQBline
        {
            get { return this.irqB; }
        }

    }
}

