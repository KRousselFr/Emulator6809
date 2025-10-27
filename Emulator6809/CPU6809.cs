using System;
using System.IO;


namespace Emulator6809
{
    /// <summary>
    /// Classe émulant un processeur Motorola 6809 / 6809E.
    /// </summary>
    public class CPU6809
    {
        /* =========================== CONSTANTES =========================== */

        // messages affichés
        private const String ERR_UNREADABLE_ADDRESS =
                "Impossible de lire le contenu de l'adresse ${0:X4} !";
        private const String ERR_UNWRITABLE_ADDRESS =
                "Impossible d'écrire la valeur $1:X2 à l'adresse ${0:X4} !";
        private const String ERR_UNKNOWN_OPCODE =
                "Opcode invalide (${1:X2}) rencontré à l'adresse ${0:X4} !";
        private const String ERR_BAD_INDEX_POSTBYTE =
                "Mauvais encodage pour le mode indexé (${1:X2}) à l'adresse ${0:X4} !";
        private const String ERR_BAD_EXG_TFR_POSTBYTE =
                "Mauvais encodage des paramètres (${1:X2}) pour l'instruction" +
                " {2} à l'adresse ${0:X4} !";

        // valeur binaire des "flags" dans le registre CC
        const byte FLAG_C = 0x01;
        const byte FLAG_V = 0x02;
        const byte FLAG_Z = 0x04;
        const byte FLAG_N = 0x08;
        const byte FLAG_I = 0x10;
        const byte FLAG_H = 0x20;
        const byte FLAG_F = 0x40;
        const byte FLAG_E = 0x80;

        // adresses particulières
        const ushort RESET_VECTOR = 0xFFFE;
        const ushort NMI_VECTOR = 0xFFFC;
        const ushort SWI_VECTOR = 0xFFFA;
        const ushort IRQ_VECTOR = 0xFFF8;
        const ushort FIRQ_VECTOR = 0xFFF6;
        const ushort SWI2_VECTOR = 0xFFF4;
        const ushort SWI3_VECTOR = 0xFFF2;
        const ushort RSRVD_VECTOR = 0xFFF0;

        // masques de sélection de bit
        const byte BYTE_MSB_MASK = 0x80;
        const byte BYTE_ABS_MASK = 0x7f;
        const byte BYTE_BCD_MASK = 0x08;
        const byte BYTE_LSB_MASK = 0x01;
        const ushort WORD_MSB_MASK = 0x8000;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // espace-mémoire attaché au processeur
        // (défini une fois pour toutes à la construction)
        private readonly IMemorySpace6809 memSpace;

        // registres du processeur
        private byte regA;
        private byte regB;
        private byte regDP;
        private ushort regX;
        private ushort regY;
        private ushort regU;
        private ushort regS;
        private ushort regPC;
        // "flags" composant le "registre P" (état du processeur)
        private bool flagC;
        private bool flagV;
        private bool flagZ;
        private bool flagN;
        private bool flagI;
        private bool flagH;
        private bool flagF;
        private bool flagE;

        // comptage des cycles écoulés
        private ulong cycles;

        // lignes de requêtes d'interruption
        private bool resetLine;
        private bool nmiLine;
        private bool nmiTrig;   // "flag" interne de déclenchement de NMI
        private bool irqLine;
        private bool firqLine;

        // processeur en attente d'interruption
        private bool stopped;

        // politique vis-à-vis des opcodes invalides
        private UnknownOpcodePolicy uoPolicy;


        // objet d'écriture dans le fichier de traçage
        private StreamWriter traceFile;
        // désassembleur pour le traçage
        private Disasm6809 traceDisasm;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Contructeur de référence (et unique) de la classe CPU6809.
        /// </summary>
        /// <param name="memorySpace">
        /// Espace-mémoire à attacher à ce nouveau processeur.
        /// </param>
        public CPU6809(IMemorySpace6809 memorySpace)
        {
            this.memSpace = memorySpace;
            this.cycles = 0L;
            this.resetLine = false;
            this.nmiLine = this.nmiTrig = false;
            this.irqLine = false;
            this.firqLine = false;
            this.stopped = false;
            this.uoPolicy = UnknownOpcodePolicy.ThrowException;
            this.traceFile = null;
            this.traceDisasm = null;
            Reset();
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        /* ~~~~ utilitaires statiques ~~~~ */

        private static byte HiByte(ushort word)
        {
            return (byte)((word >> 8) & 0x00ff);
        }

        private static byte LoByte(ushort word)
        {
            return (byte)(word & 0x00ff);
        }

        private static ushort MakeWord(byte hi, byte lo)
        {
            return (ushort)((hi << 8) | lo);
        }

        /* ~~~~ accès à l'espace mémoire ~~~~ */

        private byte ReadMem(ushort addr)
        {
            byte? memval = this.memSpace.ReadMemory(addr);
            if (!(memval.HasValue)) {
                throw new AddressUnreadableException(
                        addr,
                        String.Format(ERR_UNREADABLE_ADDRESS,
                                      addr));
            }
            this.cycles++;
            return memval.Value;
        }

        private void WriteMem(ushort addr, byte val)
        {
            bool ok = this.memSpace.WriteMemory(addr, val);
            if (!ok) {
                throw new AddressUnwritableException(
                        addr,
                        String.Format(ERR_UNWRITABLE_ADDRESS,
                                      addr, val));
            }
            this.cycles++;
        }

        /* ~~~~ implantation des modes d'adressage ~~~~ */

        /* mode d'adressage immédiat sur 8 bits : INSTR #$nn  */
        private byte AddrModeImmediateValue()
        {
            byte val = ReadMem(this.regPC);
            this.regPC++;
            return val;
        }

        /* mode d'adressage immédiat sur 16 bits : INSTR #$nnnn  */
        private ushort AddrModeImmediate16bitValue()
        {
            byte hi = ReadMem(this.regPC);
            this.regPC++;
            byte lo = ReadMem(this.regPC);
            this.regPC++;
            ushort val = MakeWord(hi, lo);
            return val;
        }

        /* mode d'adressage relatif : Bxx ±nnn  */
        private ushort AddrModeRelativeAddress()
        {
            sbyte dpl = (sbyte)(ReadMem(this.regPC));
            this.regPC++;
            ushort addr = (ushort)(this.regPC + dpl);
            return addr;
        }

        /* mode d'adressage relatif long : Bxx ±nnnnn  */
        private ushort AddrModeLongRelativeAddress()
        {
            int dpl = (ReadMem(this.regPC) << 8);
            this.regPC++;
            dpl |= ReadMem(this.regPC);
            this.regPC++;
            ushort addr = (ushort)(this.regPC + (short)dpl);
            return addr;
        }

        /* mode d'adressage étendu : INSTR $xxxx  */
        private ushort AddrModeExtendedAddress()
        {
            byte hi = ReadMem(this.regPC);
            this.regPC++;
            byte lo = ReadMem(this.regPC);
            this.regPC++;
            ushort addr = MakeWord(hi, lo);
            return addr;
        }
        private byte AddrModeExtendedValue()
        {
            return ReadMem(AddrModeExtendedAddress());
        }
        private ushort AddrModeExtended16bitValue()
        {
            ushort addr = AddrModeExtendedAddress();
            byte hi = ReadMem(addr);
            addr++;
            byte lo = ReadMem(addr);
            ushort val = MakeWord(hi, lo);
            return val;
        }

        /* mode d'adressage direct : INSTR $xx  */
        private ushort AddrModeDirectAddress()
        {
            byte lo = ReadMem(this.regPC);
            this.regPC++;
            ushort addr = MakeWord(this.regDP, lo);
            return addr;
        }
        private byte AddrModeDirectValue()
        {
            return ReadMem(AddrModeDirectAddress());
        }
        private ushort AddrModeDirect16bitValue()
        {
            ushort addr = AddrModeDirectAddress();
            byte hi = ReadMem(addr);
            addr++;
            byte lo = ReadMem(addr);
            ushort val = MakeWord(hi, lo);
            return val;
        }

        /* mode d'adressage indexé / indirect */
        private ushort AddrModeIndexedAddress()
        {
            byte postByte = ReadMem(this.regPC);
            this.regPC++;
            /* registre utilisé comme index */
            int numReg = (postByte & 0x60) >> 5;
            ushort baseAddr;
            switch (numReg) {
                case 0: baseAddr = this.regX; break;
                case 1: baseAddr = this.regY; break;
                case 2: baseAddr = this.regU; break;
                case 3: baseAddr = this.regS; break;
                default:
                    throw new Exception(
                            "Erreur interne AddrModeIndexedAddress() !");
            }
            /* bit de poids fort à 0 ? */
            if ((postByte & 0x80) == 0) {
                /* oui => indexé avec déplacement sur 5 bits signé */
                int val = postByte & 0x1f;
                if (val > 0x0f) val |= 0xf0;
                sbyte displ = (sbyte)val;
                ushort addr = (ushort)(baseAddr + val);
                return addr;
            }
            /* bit d'indirection */
            bool indirect = ((postByte & 0x10) != 0);
            /* type d'adressage */
            int loNibble = postByte & 0x0f;
            switch (loNibble) {
                case 0x0: {
                    /* post-incrémentation simple */
                    switch (numReg) {
                        case 0: this.regX++; break;
                        case 1: this.regY++; break;
                        case 2: this.regU++; break;
                        case 3: this.regS++; break;
                    }
                    return baseAddr;
                }
                case 0x1: {
                    /* post-incrémentation double (indirecte ou non) */
                    switch (numReg) {
                        case 0: this.regX += 2; break;
                        case 1: this.regY += 2; break;
                        case 2: this.regU += 2; break;
                        case 3: this.regS += 2; break;
                    }
                    return baseAddr;
                }
                case 0x2: {
                    /* pré-décrémentation simple */
                    switch (numReg) {
                        case 0:
                            this.regX--;
                            baseAddr = this.regX;
                            break;
                        case 1:
                            this.regY--;
                            baseAddr = this.regY;
                            break;
                        case 2:
                            this.regU--;
                            baseAddr = this.regU;
                            break;
                        case 3:
                            this.regS--;
                            baseAddr = this.regS;
                            break;
                    }
                    return baseAddr;
                }
                case 0x3: {
                    /* pré-décrémentation double (indirecte ou non) */
                    switch (numReg) {
                        case 0:
                            this.regX -= 2;
                            baseAddr = this.regX;
                            break;
                        case 1:
                            this.regY -= 2;
                            baseAddr = this.regY;
                            break;
                        case 2:
                            this.regU -= 2;
                            baseAddr = this.regU;
                            break;
                        case 3:
                            this.regS -= 2;
                            baseAddr = this.regS;
                            break;
                    }
                    return baseAddr;
                }
                case 0x4: {
                    /* indexation simple (indirecte ou non) */
                    ushort addr = baseAddr;
                    if (indirect) {
                        byte hi = ReadMem(addr);
                        addr++;
                        byte lo = ReadMem(addr);
                        addr = MakeWord(hi, lo);
                    }
                    return addr;
                }
                case 0x5: {
                    /* indexation avec déplacement du contenu de B
                       (indirecte ou non) */
                    ushort addr = (ushort)(baseAddr + (sbyte)(this.regB));
                    if (indirect) {
                        byte hi = ReadMem(addr);
                        addr++;
                        byte lo = ReadMem(addr);
                        addr = MakeWord(hi, lo);
                    }
                    return addr;
                }
                case 0x6: {
                    /* indexation avec déplacement du contenu de A
                       (indirecte ou non) */
                    ushort addr = (ushort)(baseAddr + (sbyte)(this.regA));
                    if (indirect) {
                        byte hi = ReadMem(addr);
                        addr++;
                        byte lo = ReadMem(addr);
                        addr = MakeWord(hi, lo);
                    }
                    return addr;
                }
                case 0x8: {
                    /* indexation avec déplacement constant sur 8 bits
                       (indirecte ou non) */
                    sbyte displ = (sbyte)(ReadMem(this.regPC));
                    this.regPC++;
                    ushort addr = (ushort)(baseAddr + displ);
                    if (indirect) {
                        byte hi = ReadMem(addr);
                        addr++;
                        byte lo = ReadMem(addr);
                        addr = MakeWord(hi, lo);
                    }
                    return addr;
                }
                case 0x9: {
                    /* indexation avec déplacement constant sur 16 bits
                       (indirecte ou non) */
                    byte hi = ReadMem(this.regPC);
                    this.regPC++;
                    byte lo = ReadMem(this.regPC);
                    this.regPC++;
                    short displ = (short)(MakeWord(hi, lo));
                    ushort addr = (ushort)(baseAddr + displ);
                    if (indirect) {
                        hi = ReadMem(addr);
                        addr++;
                        lo = ReadMem(addr);
                        addr = MakeWord(hi, lo);
                    }
                    return addr;
                }
                case 0xb: {
                    /* indexation avec déplacement du contenu de D
                       (indirecte ou non) */
                    ushort addr = (ushort)(baseAddr + (short)(this.RegisterD));
                    if (indirect) {
                        byte hi = ReadMem(addr);
                        addr++;
                        byte lo = ReadMem(addr);
                        addr = MakeWord(hi, lo);
                    }
                    return addr;
                }
                case 0xc: {
                    /* relatif au PC avec déplacement constant sur 8 bits
                       (indirect ou non) */
                    sbyte displ = (sbyte)(ReadMem(this.regPC));
                    this.regPC++;
                    ushort addr = (ushort)(this.regPC + displ);
                    return addr;
                }
                case 0xd: {
                    /* relatif au PC avec déplacement constant sur 16 bits
                       (indirect ou non) */
                    byte hi = ReadMem(this.regPC);
                    this.regPC++;
                    byte lo = ReadMem(this.regPC);
                    this.regPC++;
                    short displ = (short)(MakeWord(hi, lo));
                    ushort addr = (ushort)(this.regPC + displ);
                    return addr;
                }
                case 0xf: {
                    /* indirect étendu */
                    byte hi = ReadMem(this.regPC);
                    this.regPC++;
                    byte lo = ReadMem(this.regPC);
                    this.regPC++;
                    baseAddr = MakeWord(hi, lo);
                    hi = ReadMem(baseAddr);
                    baseAddr++;
                    lo = ReadMem(baseAddr);
                    ushort addr = MakeWord(hi, lo);
                    return addr;
                }
            }
            /* si on arrive ici, l'octet décrivant
               le mode indexé est invalide */
            throw new UnknownOpcodeException(this.regPC - 1, postByte,
                    String.Format(ERR_BAD_INDEX_POSTBYTE,
                                  this.regPC - 1, postByte));
        }
        private byte AddrModeIndexedValue()
        {
            return ReadMem(AddrModeIndexedAddress());
        }
        private ushort AddrModeIndexed16bitValue()
        {
            ushort addr = AddrModeIndexedAddress();
            byte hi = ReadMem(addr);
            addr++;
            byte lo = ReadMem(addr);
            ushort val = MakeWord(hi, lo);
            return val;
        }

        /* ~~~~ accès à la pile ~~~~ */

        private void PushByte(byte val)
        {
            this.regS--;
            WriteMem(this.regS, val);
        }

        private void PushWord(ushort val)
        {
            PushByte(LoByte(val));
            PushByte(HiByte(val));
        }

        private byte PullByte()
        {
            byte val = ReadMem(this.regS);
            this.regS++;
            return val;
        }

        private ushort PullWord()
        {
            byte hi = PullByte();
            byte lo = PullByte();
            return MakeWord(hi, lo);
        }

        private void PushRegsForInterrupt(bool fastIRQ)
        {
            this.flagE = !fastIRQ;
            PushWord(this.regPC);
            if (this.flagE) {
                PushWord(this.regU);
                PushWord(this.regY);
                PushWord(this.regX);
                PushByte(this.regDP);
                PushByte(this.regB);
                PushByte(this.regA);
            }
            PushByte(this.RegisterCC);
        }

        private void PushUserByte(byte val)
        {
            this.regU--;
            WriteMem(this.regU, val);
        }

        private byte PullUserByte()
        {
            byte val = ReadMem(this.regU);
            this.regU++;
            return val;
        }
        
        /* ~~~~ gestion des "flags" ~~~~ */

        private void SetNZ(byte val)
        {
            this.flagZ = (val == 0x00);
            this.flagN = ((val & BYTE_MSB_MASK) != 0);
        }

        private void SetNZ16bit(ushort val)
        {
            this.flagZ = (val == 0x0000);
            this.flagN = ((val & WORD_MSB_MASK) != 0);
        }

        /* ~~~~ implantation des opérations de l'ALU ~~~~ */

        private byte Do8bitLoad(byte val)
        {
            SetNZ(val);
            this.flagV = false;
            // renvoie le résultat
            return val;
        }

        private ushort Do16bitLoad(ushort val)
        {
            SetNZ16bit(val);
            this.flagV = false;
            // renvoie le résultat
            return val;
        }

        private void Do8bitStore(ushort addr, byte val)
        {
            SetNZ(val);
            this.flagV = false;
            WriteMem(addr, val);
        }

        private void Do16bitStore(ushort addr, ushort val)
        {
            SetNZ16bit(val);
            this.flagV = false;
            WriteMem(addr, HiByte(val));
            addr++;
            WriteMem(addr, LoByte(val));
        }

        private byte Do8bitAdd(byte baseVal, byte add, bool useC)
        {
            // addition proprement dite
            byte res = (byte)(baseVal + add);
            if (useC && this.flagC) res++;
            // flags
            SetNZ(res);
            this.flagC = ( ((baseVal & BYTE_MSB_MASK) != 0) &&
                           ((add & BYTE_MSB_MASK) != 0))
                      || ( ((add & BYTE_MSB_MASK) != 0) &&
                           ((res & BYTE_MSB_MASK) == 0))
                      || ( ((res & BYTE_MSB_MASK) == 0) &&
                           ((baseVal & BYTE_MSB_MASK) != 0));
            this.flagH = ( ((baseVal & BYTE_BCD_MASK) != 0) &&
                           ((add & BYTE_BCD_MASK) != 0))
                      || ( ((add & BYTE_BCD_MASK) != 0) &&
                           ((res & BYTE_BCD_MASK) == 0))
                      || ( ((res & BYTE_BCD_MASK) == 0) &&
                           ((baseVal & BYTE_BCD_MASK) != 0));
            /*
             * V est activé :
             * - si la somme de deux positifs donne un négatif, ou :
             * - si la somme de deux négatifs donne un positif
             */
            this.flagV = ( ((baseVal & BYTE_MSB_MASK) != 0) &&
                           ((add & BYTE_MSB_MASK) != 0) &&
                           ((res & BYTE_MSB_MASK) == 0))
                      || ( ((baseVal & BYTE_MSB_MASK) == 0) &&
                           ((add & BYTE_MSB_MASK) == 0) &&
                           ((res & BYTE_MSB_MASK) != 0));
            // renvoie le résultat
            return res;
        }

        private ushort Do16bitAdd(ushort baseVal, ushort add)
        {
            // addition proprement dite
            int sum = baseVal + add;
            ushort res = (ushort)sum;
            // cycle supplémentaire
            this.cycles++;
            // flags
            SetNZ16bit(res);
            this.flagC = (sum > 0xffff);
            /*
             * V est activé :
             * - si la somme de deux positifs donne un négatif, ou :
             * - si la somme de deux négatifs donne un positif
             */
            this.flagV = ( ((baseVal & WORD_MSB_MASK) != 0) &&
                           ((add & WORD_MSB_MASK) != 0) &&
                           ((res & WORD_MSB_MASK) == 0))
                      || ( ((baseVal & WORD_MSB_MASK) == 0) &&
                           ((add & WORD_MSB_MASK) == 0) &&
                           ((res & WORD_MSB_MASK) != 0));
            // renvoie le résultat
            return res;
        }

        private byte DoArithmeticShiftRight(byte val)
        {
            // décalage arith. à droite et MàJ des flags
            bool neg = ((val & BYTE_MSB_MASK) != 0);
            this.flagC = ((val & BYTE_LSB_MASK) != 0);
            val >>= 1;
            if (neg) {
                // régénère le bit 7
                val |= BYTE_MSB_MASK;
            }
            SetNZ(val);
            // renvoie le résultat
            return val;
        }

        private byte DoClear()
        {
            // flags
            this.flagN = false;
            this.flagZ = true;
            this.flagC = false;
            this.flagV = false;
            // renvoie le résultat
            return 0x00;
        }

        private byte DoComplement(byte val)
        {
            // inversion binaire
            val = (byte)(~val);
            // flags
            SetNZ(val);
            this.flagC = true;
            this.flagV = false;
            // renvoie le résultat
            return val;
        }

        private byte DoDecrement(byte val)
        {
            // décrémentation et MàJ des flags
            this.flagV = (val == 0x80);
            val--;
            SetNZ(val);
            // renvoie le résultat
            return val;
        }

        private byte DoIncrement(byte val)
        {
            // incrémentation et MàJ des flags
            this.flagV = (val == 0x7f);
            val++;
            SetNZ(val);
            // renvoie le résultat
            return val;
        }

        private byte DoLogicShiftRight(byte val)
        {
            // décalage logique à droite et MàJ des flags
            this.flagC = ((val & BYTE_LSB_MASK) != 0);
            val >>= 1;
            val &= BYTE_ABS_MASK;   // met le bit 7 à 0
            SetNZ(val);
            // renvoie le résultat
            return val;
        }

        private byte DoRotateLeft(byte val)
        {
            // rotation à droite et MàJ des flags
            this.flagV = ((val & BYTE_MSB_MASK) != 0)
                       ^ ((val & 0x40) != 0);
            bool toCarry = ((val & BYTE_MSB_MASK) != 0);
            val <<= 1;
            if (this.flagC) {
                val |= BYTE_LSB_MASK;
            } else {
                val &= 0xfe;
            }
            SetNZ(val);
            this.flagC = toCarry;
            // renvoie le résultat
            return val;
        }

        private byte DoRotateRight(byte val)
        {
            // rotation à droite et MàJ des flags
            bool toCarry = ((val & BYTE_LSB_MASK) != 0);
            val >>= 1;
            if (this.flagC) {
                val |= BYTE_MSB_MASK;
            } else {
                val &= BYTE_ABS_MASK;
            }
            SetNZ(val);
            this.flagC = toCarry;
            // renvoie le résultat
            return val;
        }

        private void DoTestByte(byte val)
        {
            SetNZ(val);
            this.flagV = false;
        }

        private byte DoBinaryAnd(byte baseVal, byte oper)
        {
            // opération logique
            byte res = (byte)(baseVal & oper);
            // flags
            SetNZ(res);
            this.flagV = false;
            // renvoie le résultat
            return res;
        }

        private byte DoExclusiveOr(byte baseVal, byte oper)
        {
            // opération logique
            byte res = (byte)(baseVal ^ oper);
            // flags
            SetNZ(res);
            this.flagV = false;
            // renvoie le résultat
            return res;
        }

        private byte DoBinaryOr(byte baseVal, byte oper)
        {
            // opération logique
            byte res = (byte)(baseVal | oper);
            // flags
            SetNZ(res);
            this.flagV = false;
            // renvoie le résultat
            return res;
        }

        /* ~~~~ implantation des instructions ~~~~ */

        private void InstrABX()
        {
            this.regX += this.regB;
            /* cycles supplémentaires */
            this.cycles += 2;
        }

        private void InstrADCA(byte val)
        {
            this.regA = Do8bitAdd(this.regA, val, true);
        }

        private void InstrADCB(byte val)
        {
            this.regB = Do8bitAdd(this.regB, val, true);
        }

        private void InstrADDA(byte val)
        {
            this.regA = Do8bitAdd(this.regA, val, false);
        }

        private void InstrADDB(byte val)
        {
            this.regB = Do8bitAdd(this.regB, val, false);
        }

        private void InstrADDD(ushort val)
        {
            this.RegisterD = Do16bitAdd(this.RegisterD, val);
        }

        private void InstrANDA(byte val)
        {
            this.regA = DoBinaryAnd(this.regA, val);
        }

        private void InstrANDB(byte val)
        {
            this.regB = DoBinaryAnd(this.regB, val);
        }

        private void InstrANDCC(byte val)
        {
            // opération logique sur le registre d'état
            this.RegisterCC &= val;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrASRA()
        {
            this.regA = DoArithmeticShiftRight(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrASRB()
        {
            this.regB = DoArithmeticShiftRight(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrASR(ushort addr)
        {
            byte val = ReadMem(addr);
            val = DoArithmeticShiftRight(val);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrBCC(ushort addr)   /* alias BHS */
        {
            if (!(this.flagC)) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBCS(ushort addr)   /* alias BLO */
        {
            if (this.flagC) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBEQ(ushort addr)
        {
            if (this.flagZ) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBGE(ushort addr)
        {
            if (!(this.flagN ^ this.flagV)) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBGT(ushort addr)
        {
            if ( (!(this.flagZ || this.flagN || this.flagV)) ||
                 (!(this.flagZ) && this.flagN && this.flagV) )
                this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBHI(ushort addr)
        {
            if (!(this.flagC || this.flagZ)) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBITA(byte val)
        {
            // opération logique
            int res = this.regA & val;
            // flags
            SetNZ((byte)res);
            this.flagV = false;
        }

        private void InstrBITB(byte val)
        {
            // opération logique
            int res = this.regB & val;
            // flags
            SetNZ((byte)res);
            this.flagV = false;
        }

        private void InstrBLE(ushort addr)
        {
            if ( (this.flagN && !(this.flagZ) && !(this.flagV)) ||
                 (!(this.flagN) && this.flagZ && !(this.flagV)) ||
                 (!(this.flagN) && !(this.flagZ) && this.flagV) ||
                 (this.flagN && this.flagZ && this.flagV) )
                this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBLS(ushort addr)
        {
            if (this.flagC ^ this.flagZ) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBLT(ushort addr)
        {
            if (this.flagN ^ this.flagV) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBMI(ushort addr)
        {
            if (this.flagN) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBNE(ushort addr)
        {
            if (!(this.flagZ)) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBPL(ushort addr)
        {
            if (!(this.flagN)) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBRA(ushort addr)
        {
            this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBRN(ushort addr)
        {
            /* opération nulle sur 3 cycles */
            this.cycles++;
        }

        private void InstrBSR(ushort addr)
        {
            PushWord(this.regPC);
            this.regPC = addr;
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrBVC(ushort addr)
        {
            if (!(this.flagV)) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrBVS(ushort addr)
        {
            if (this.flagV) this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrCLRA()
        {
            this.regA = DoClear();
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrCLRB()
        {
            this.regB = DoClear();
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrCLR(ushort addr)
        {
            ReadMem(addr);
            // le manuel du 6809 précise qu'une lecture inutile
            // de l'adresse mémoire a lieu avant son effacement,
            // et que cela peut influer sur les E / S
            // flags
            byte val = DoClear();
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrCMPA(byte val)
        {
            // soustraction = addition du nombre opposé
            byte add = (byte)-val;
            // n'enregistre pas le résultat
            Do8bitAdd(this.regA, add, false);
        }

        private void InstrCMPB(byte val)
        {
            // soustraction = addition du nombre opposé
            byte add = (byte)-val;
            // n'enregistre pas le résultat
            Do8bitAdd(this.regB, add, false);
        }

        private void InstrCMPD(ushort val)
        {
            // soustraction = addition du nombre opposé
            ushort add = (ushort)-val;
            // n'enregistre pas le résultat
            Do16bitAdd(this.RegisterD, add);
        }

        private void InstrCMPS(ushort val)
        {
            // soustraction = addition du nombre opposé
            ushort add = (ushort)-val;
            // n'enregistre pas le résultat
            Do16bitAdd(this.regS, add);
        }

        private void InstrCMPU(ushort val)
        {
            // soustraction = addition du nombre opposé
            ushort add = (ushort)-val;
            // n'enregistre pas le résultat
            Do16bitAdd(this.regU, add);
        }

        private void InstrCMPX(ushort val)
        {
            // soustraction = addition du nombre opposé
            ushort add = (ushort)-val;
            // n'enregistre pas le résultat
            Do16bitAdd(this.regX, add);
        }

        private void InstrCMPY(ushort val)
        {
            // soustraction = addition du nombre opposé
            ushort add = (ushort)-val;
            // n'enregistre pas le résultat
            Do16bitAdd(this.regY, add);
        }

        private void InstrCOMA()
        {
            this.regA = DoComplement(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrCOMB()
        {
            this.regB = DoComplement(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrCOM(ushort addr)
        {
            byte val = ReadMem(addr);
            val = DoComplement(val);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrCWAI(byte val)
        {
            // opération logique sur le registre d'état
            this.RegisterCC &= val;
            // sauve tous les registres du processeur sur la pile
            PushRegsForInterrupt(false);
            // cycles supplémentaires
            this.cycles += 6;
            // le processeur se met en attente d'une interruption
            this.stopped = true;
        }

        private void InstrDAA()
        {
            // calcul du facteur de correction
            byte corr = 0x00;
            byte loNibble = (byte)(this.regA & 0x0f);
            if (this.flagH || (loNibble > 0x9)) {
                corr = 0x06;
            }
            byte hiNibble = (byte)((this.regA >> 4) & 0x0f);
            if (this.flagC || (hiNibble > 0x9)
                           || ((hiNibble > 0x8) && (loNibble > 0x9)) )
            {
                corr += 0x60;
            }
            // addition du facteur de correction à A
            int res = this.regA + corr;
            SetNZ((byte)res);
            this.flagC = (res > 0xff);
            this.flagV = false;
            this.regA = (byte)res;
        }

        private void InstrDECA()
        {
            this.regA = DoDecrement(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrDECB()
        {
            this.regB = DoDecrement(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrDEC(ushort addr)
        {
            byte val = ReadMem(addr);
            val = DoDecrement(val);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrEORA(byte val)
        {
            this.regA = DoExclusiveOr(this.regA, val);
        }

        private void InstrEORB(byte val)
        {
            this.regB = DoExclusiveOr(this.regB, val);
        }

        private void InstrEXG(byte postByte)
        {
            ushort val16;
            byte val8;
            switch (postByte) {
                case 0x00: /* NOP */ break;
                case 0x01:
                    val16 = this.RegisterD;
                    this.RegisterD = this.regX;
                    this.regX = val16;
                    break;
                case 0x02:
                    val16 = this.RegisterD;
                    this.RegisterD = this.regY;
                    this.regY = val16;
                    break;
                case 0x03:
                    val16 = this.RegisterD;
                    this.RegisterD = this.regU;
                    this.regU = val16;
                    break;
                case 0x04:
                    val16 = this.RegisterD;
                    this.RegisterD = this.regS;
                    this.regS = val16;
                    break;
                case 0x05:
                    val16 = this.RegisterD;
                    this.RegisterD = this.regPC;
                    this.regPC = val16;
                    break;

                case 0x10:
                    val16 = this.regX;
                    this.regX = this.RegisterD;
                    this.RegisterD = val16;
                    break;
                case 0x11: /* NOP */ break;
                case 0x12:
                    val16 = this.regX;
                    this.regX = this.regY;
                    this.regY = val16;
                    break;
                case 0x13:
                    val16 = this.regX;
                    this.regX = this.regU;
                    this.regU = val16;
                    break;
                case 0x14:
                    val16 = this.regX;
                    this.regX = this.regS;
                    this.regS = val16;
                    break;
                case 0x15:
                    val16 = this.regX;
                    this.regX = this.regPC;
                    this.regPC = val16;
                    break;

                case 0x20:
                    val16 = this.regY;
                    this.regY = this.RegisterD;
                    this.RegisterD = val16;
                    break;
                case 0x21:
                    val16 = this.regY;
                    this.regY = this.regX;
                    this.regX = val16;
                    break;
                case 0x22: /* NOP */ break;
                case 0x23:
                    val16 = this.regY;
                    this.regY = this.regU;
                    this.regU = val16;
                    break;
                case 0x24:
                    val16 = this.regY;
                    this.regY = this.regS;
                    this.regS = val16;
                    break;
                case 0x25:
                    val16 = this.regY;
                    this.regY = this.regPC;
                    this.regPC = val16;
                    break;

                case 0x30:
                    val16 = this.regU;
                    this.regU = this.RegisterD;
                    this.RegisterD = val16;
                    break;
                case 0x31:
                    val16 = this.regU;
                    this.regU = this.regX;
                    this.regX = val16;
                    break;
                case 0x32:
                    val16 = this.regU;
                    this.regU = this.regY;
                    this.regY = val16;
                    break;
                case 0x33: /* NOP */ break;
                case 0x34:
                    val16 = this.regU;
                    this.regU = this.regS;
                    this.regS = val16;
                    break;
                case 0x35:
                    val16 = this.regU;
                    this.regU = this.regPC;
                    this.regPC = val16;
                    break;

                case 0x40:
                    val16 = this.regS;
                    this.regS = this.RegisterD;
                    this.RegisterD = val16;
                    break;
                case 0x41:
                    val16 = this.regS;
                    this.regS = this.regX;
                    this.regX = val16;
                    break;
                case 0x42:
                    val16 = this.regS;
                    this.regS = this.regY;
                    this.regY = val16;
                    break;
                case 0x43:
                    val16 = this.regS;
                    this.regS = this.regU;
                    this.regU = val16;
                    break;
                case 0x44: /* NOP */ break;
                case 0x45:
                    val16 = this.regS;
                    this.regS = this.regPC;
                    this.regPC = val16;
                    break;

                case 0x50:
                    val16 = this.regPC;
                    this.regPC = this.RegisterD;
                    this.RegisterD = val16;
                    break;
                case 0x51:
                    val16 = this.regPC;
                    this.regPC = this.regX;
                    this.regX = val16;
                    break;
                case 0x52:
                    val16 = this.regPC;
                    this.regPC = this.regY;
                    this.regY = val16;
                    break;
                case 0x53:
                    val16 = this.regPC;
                    this.regPC = this.regU;
                    this.regU = val16;
                    break;
                case 0x54:
                    val16 = this.regPC;
                    this.regPC = this.regS;
                    this.regS = val16;
                    break;
                case 0x55: /* NOP */ break;

                case 0x88: /* NOP */ break;
                case 0x89:
                    val8 = this.regA;
                    this.regA = this.regB;
                    this.regB = val8;
                    break;
                case 0x8a:
                    val8 = this.regA;
                    this.regA = this.RegisterCC;
                    this.RegisterCC = val8;
                    break;
                case 0x8b:
                    val8 = this.regA;
                    this.regA = this.regDP;
                    this.regDP = val8;
                    break;

                case 0x98:
                    val8 = this.regB;
                    this.regB = this.regA;
                    this.regA = val8;
                    break;
                case 0x99: /* NOP */ break;
                case 0x9a:
                    val8 = this.regB;
                    this.regB = this.RegisterCC;
                    this.RegisterCC = val8;
                    break;
                case 0x9b:
                    val8 = this.regB;
                    this.regB = this.regDP;
                    this.regDP = val8;
                    break;

                case 0xa8:
                    val8 = this.RegisterCC;
                    this.RegisterCC = this.regA;
                    this.regA = val8;
                    break;
                case 0xa9:
                    val8 = this.RegisterCC;
                    this.RegisterCC = this.regB;
                    this.regB = val8;
                    break;
                case 0xaa: /* NOP */ break;
                case 0xab:
                    val8 = this.RegisterCC;
                    this.RegisterCC = this.regDP;
                    this.regDP = val8;
                    break;

                case 0xb8:
                    val8 = this.regDP;
                    this.regDP = this.regA;
                    this.regA = val8;
                    break;
                case 0xb9:
                    val8 = this.regDP;
                    this.regDP = this.regB;
                    this.regB = val8;
                    break;
                case 0xba:
                    val8 = this.regDP;
                    this.regDP = this.RegisterCC;
                    this.RegisterCC = val8;
                    break;
                case 0xbb: /* NOP */ break;

                default:
                    throw new UnknownOpcodeException(
                            this.regPC - 1, postByte,
                            String.Format(ERR_BAD_EXG_TFR_POSTBYTE,
                                          this.regPC - 1, postByte, "EXG"));
            }
            // cycles supplémentaires
            this.cycles += 6;
        }

        private void InstrINCA()
        {
            this.regA = DoIncrement(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrINCB()
        {
            this.regB = DoIncrement(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrINC(ushort addr)
        {
            byte val = ReadMem(addr);
            val = DoIncrement(val);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrJMP(ushort addr)
        {
            this.regPC = addr;
        }

        private void InstrJSR(ushort addr)
        {
            PushWord(this.regPC);
            this.regPC = addr;
        }

        private void InstrLBCC(ushort addr)   /* alias LBHS */
        {
            if (!(this.flagC)) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBCS(ushort addr)   /* alias LBLO */
        {
            if (this.flagC) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBEQ(ushort addr)
        {
            if (this.flagZ) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBGE(ushort addr)
        {
            if (!(this.flagN ^ this.flagV)) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBGT(ushort addr)
        {
            if ( (!(this.flagZ || this.flagN || this.flagV)) ||
                 (!(this.flagZ) && this.flagN && this.flagV) )
            {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBHI(ushort addr)
        {
            if (!(this.flagC || this.flagZ)) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBLE(ushort addr)
        {
            if ( (this.flagN && !(this.flagZ) && !(this.flagV)) ||
                 (!(this.flagN) && this.flagZ && !(this.flagV)) ||
                 (!(this.flagN) && !(this.flagZ) && this.flagV) ||
                 (this.flagN && this.flagZ && this.flagV) )
            {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBLS(ushort addr)
        {
            if (this.flagC ^ this.flagZ) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBLT(ushort addr)
        {
            if (this.flagN ^ this.flagV) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBMI(ushort addr)
        {
            if (this.flagN) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBNE(ushort addr)
        {
            if (!(this.flagZ)) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBPL(ushort addr)
        {
            if (!(this.flagN)) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBRA(ushort addr)
        {
            this.regPC = addr;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBRN(ushort addr)
        {
            /* opération nulle sur 5 cycles */
            this.cycles++;
        }

        private void InstrLBSR(ushort addr)
        {
            PushWord(this.regPC);
            this.regPC = addr;
            // cycles supplémentaires
            this.cycles += 4;
        }

        private void InstrLBVC(ushort addr)
        {
            if (!(this.flagV)) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLBVS(ushort addr)
        {
            if (this.flagV) {
                this.regPC = addr;
                // cycle supplémentaire si branchement
                this.cycles++;
            }
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLDA(byte val)
        {
            this.regA = Do8bitLoad(val);
        }

        private void InstrLDB(byte val)
        {
            this.regB = Do8bitLoad(val);
        }

        private void InstrLDD(ushort val)
        {
            this.RegisterD = Do16bitLoad(val);
        }

        private void InstrLDS(ushort val)
        {
            this.regS = Do16bitLoad(val);
        }

        private void InstrLDU(ushort val)
        {
            this.regU = Do16bitLoad(val);
        }

        private void InstrLDX(ushort val)
        {
            this.regX = Do16bitLoad(val);
        }

        private void InstrLDY(ushort val)
        {
            this.regY = Do16bitLoad(val);
        }

        private void InstrLEAS(ushort addr)
        {
            this.regS = addr;
        }

        private void InstrLEAU(ushort addr)
        {
            this.regU = addr;
        }

        private void InstrLEAX(ushort addr)
        {
            this.regX = addr;
            this.flagZ = (addr == 0);
        }

        private void InstrLEAY(ushort addr)
        {
            this.regY = addr;
            this.flagZ = (addr == 0);
        }

        private void InstrLSLA()   /* alias ASLA */
        {
            /* décalage à gauche = multiplication par deux
                                 = addition du nombre à lui-même */
            this.regA = Do8bitAdd(this.regA, this.regA, false);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLSLB()   /* alias ASLB */
        {
            /* décalage à gauche = multiplication par deux
                                 = addition du nombre à lui-même */
            this.regB = Do8bitAdd(this.regB, this.regB, false);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLSL(ushort addr)   /* alias ASL */
        {
            byte val = ReadMem(addr);
            /* décalage à gauche = multiplication par deux
                                 = addition du nombre à lui-même */
            val = Do8bitAdd(val, val, false);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrLSRA()
        {
            this.regA = DoLogicShiftRight(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLSRB()
        {
            this.regB = DoLogicShiftRight(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrLSR(ushort addr)
        {
            byte val = ReadMem(addr);
            val = DoLogicShiftRight(val);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrMUL()
        {
            // multiplication non signée
            this.RegisterD = (ushort)(this.regA * this.regB);
            this.flagZ = (this.RegisterD == 0);
            this.flagC = ((this.regB & BYTE_MSB_MASK) != 0);
            // cycles supplémentaires
            this.cycles += 10;
        }

        private void InstrNEGA()
        {
            // négation = addition du nombre opposé à zéro
            byte add = (byte)-this.regA;
            this.regA = Do8bitAdd(0, add, false);
        }

        private void InstrNEGB()
        {
            // négation = addition du nombre opposé à zéro
            byte add = (byte)-this.regB;
            this.regB = Do8bitAdd(0, add, false);
        }

        private void InstrNEG(ushort addr)
        {
            byte val = ReadMem(addr);
            // négation = addition du nombre opposé à zéro
            byte add = (byte)-val;
            val = Do8bitAdd(0, add, false);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrNOP()
        {
            /* ne rien faire, sinon passer des cycles */
            this.cycles++;
        }

        private void InstrORA(byte val)
        {
            this.regA = DoBinaryOr(this.regA, val);
        }

        private void InstrORB(byte val)
        {
            this.regB = DoBinaryOr(this.regB, val);
        }

        private void InstrORCC(byte val)
        {
            // opération logique sur le registre d'état
            this.RegisterCC |= val;
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrPSHS(byte postByte)
        {
            if ((postByte & 0x80) != 0) PushWord(this.regPC);
            if ((postByte & 0x40) != 0) PushWord(this.regU);
            if ((postByte & 0x20) != 0) PushWord(this.regY);
            if ((postByte & 0x10) != 0) PushWord(this.regX);
            if ((postByte & 0x08) != 0) PushByte(this.regDP);
            if ((postByte & 0x04) != 0) PushByte(this.regB);
            if ((postByte & 0x02) != 0) PushByte(this.regA);
            if ((postByte & 0x01) != 0) PushByte(this.RegisterCC);
            // cycles supplémentaires
            this.cycles += 3;
        }

        private void InstrPSHU(byte postByte)
        {
            if ((postByte & 0x80) != 0) {
                PushUserByte(LoByte(this.regPC));
                PushUserByte(HiByte(this.regPC));
            }
            if ((postByte & 0x40) != 0) {
                PushUserByte(LoByte(this.regS));
                PushUserByte(HiByte(this.regS));
            }
            if ((postByte & 0x20) != 0) {
                PushUserByte(LoByte(this.regY));
                PushUserByte(HiByte(this.regY));
            }
            if ((postByte & 0x10) != 0) {
                PushUserByte(LoByte(this.regX));
                PushUserByte(HiByte(this.regX));
            }
            if ((postByte & 0x08) != 0) PushUserByte(this.regDP);
            if ((postByte & 0x04) != 0) PushUserByte(this.regB);
            if ((postByte & 0x02) != 0) PushUserByte(this.regA);
            if ((postByte & 0x01) != 0) PushUserByte(this.RegisterCC);
            // cycles supplémentaires
            this.cycles += 3;
        }

        private void InstrPULS(byte postByte)
        {
            if ((postByte & 0x01) != 0) this.RegisterCC = PullByte();
            if ((postByte & 0x02) != 0) this.regA = PullByte();
            if ((postByte & 0x04) != 0) this.regB = PullByte();
            if ((postByte & 0x08) != 0) this.regDP = PullByte();
            if ((postByte & 0x10) != 0) this.regX = PullWord();
            if ((postByte & 0x20) != 0) this.regY = PullWord();
            if ((postByte & 0x40) != 0) this.regU = PullWord();
            if ((postByte & 0x80) != 0) this.regPC = PullWord();
            // cycles supplémentaires
            this.cycles += 3;
        }

        private void InstrPULU(byte postByte)
        {
            if ((postByte & 0x01) != 0) this.RegisterCC = PullUserByte();
            if ((postByte & 0x02) != 0) this.regA = PullUserByte();
            if ((postByte & 0x04) != 0) this.regB = PullUserByte();
            if ((postByte & 0x08) != 0) this.regDP = PullUserByte();
            if ((postByte & 0x10) != 0) {
                byte hi = PullUserByte();
                byte lo = PullUserByte();
                this.regX = MakeWord(hi, lo);
            }
            if ((postByte & 0x20) != 0) {
                byte hi = PullUserByte();
                byte lo = PullUserByte();
                this.regY = MakeWord(hi, lo);
            }
            if ((postByte & 0x40) != 0) {
                byte hi = PullUserByte();
                byte lo = PullUserByte();
                this.regS = MakeWord(hi, lo);
            }
            if ((postByte & 0x80) != 0) {
                byte hi = PullUserByte();
                byte lo = PullUserByte();
                this.regPC = MakeWord(hi, lo);
            }
            // cycles supplémentaires
            this.cycles += 3;
        }

        private void InstrROLA()
        {
            this.regA = DoRotateLeft(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrROLB()
        {
            this.regB = DoRotateLeft(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrROL(ushort addr)
        {
            byte val = ReadMem(addr);
            val = DoRotateLeft(val);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrRORA()
        {
            this.regA = DoRotateRight(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrRORB()
        {
            this.regB = DoRotateRight(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrROR(ushort addr)
        {
            byte val = ReadMem(addr);
            val = DoRotateRight(val);
            // enregistre le résultat en mémoire
            WriteMem(addr, val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrRTI()
        {
            this.RegisterCC = PullByte();
            if (this.flagE) {
                this.regA = PullByte();
                this.regB = PullByte();
                this.regDP = PullByte();
                this.regX = PullWord();
                this.regY = PullWord();
                this.regU = PullWord();
            }
            this.regPC = PullWord();
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrRTS()
        {
            this.regPC = PullWord();
            // cycles supplémentaires
            this.cycles += 2;
        }

        private void InstrSBCA(byte val)
        {
            // soustraction = addition du nombre opposé
            byte add = (byte)-val;
            this.regA = Do8bitAdd(this.regA, add, true);
            // TODO ! Vérifier la bonne prise en charge de C !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void InstrSBCB(byte val)
        {
            // soustraction = addition du nombre opposé
            byte add = (byte)-val;
            this.regB = Do8bitAdd(this.regB, add, true);
            // TODO ! Vérifier la bonne prise en charge de C !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void InstrSEX()
        {
            SetNZ(this.regB);
            if (this.flagN) {
                this.regA = 0xff;
            } else {
                this.regA = 0x00;
            }
        }

        private void InstrSTA(ushort addr)
        {
            Do8bitStore(addr, this.regA);
        }

        private void InstrSTB(ushort addr)
        {
            Do8bitStore(addr, this.regB);
        }

        private void InstrSTD(ushort addr)
        {
            Do16bitStore(addr, this.RegisterD);
        }

        private void InstrSTS(ushort addr)
        {
            Do16bitStore(addr, this.regS);
        }

        private void InstrSTU(ushort addr)
        {
            Do16bitStore(addr, this.regU);
        }

        private void InstrSTX(ushort addr)
        {
            Do16bitStore(addr, this.regX);
        }

        private void InstrSTY(ushort addr)
        {
            Do16bitStore(addr, this.regY);
        }

        private void InstrSUBA(byte val)
        {
            // soustraction = addition du nombre opposé
            byte add = (byte)-val;
            this.regA = Do8bitAdd(this.regA, add, false);
        }

        private void InstrSUBB(byte val)
        {
            // soustraction = addition du nombre opposé
            byte add = (byte)-val;
            this.regB = Do8bitAdd(this.regB, add, false);
        }

        private void InstrSUBD(ushort val)
        {
            // soustraction = addition du nombre opposé
            ushort add = (ushort)-val;
            this.RegisterD = Do16bitAdd(this.RegisterD, add);
        }

        private void InstrSWI()
        {
            PushRegsForInterrupt(false);
            this.flagI = true;
            this.flagF = true;
            byte hi = ReadMem(SWI_VECTOR);
            byte lo = ReadMem(SWI_VECTOR + 1);
            this.regPC = MakeWord(hi, lo);
            // cycles supplémentaires
            this.cycles += 4;
        }

        private void InstrSWI2()
        {
            PushRegsForInterrupt(false);
            byte hi = ReadMem(SWI2_VECTOR);
            byte lo = ReadMem(SWI2_VECTOR + 1);
            this.regPC = MakeWord(hi, lo);
            // cycles supplémentaires
            this.cycles += 4;
        }

        private void InstrSWI3()
        {
            PushRegsForInterrupt(false);
            byte hi = ReadMem(SWI3_VECTOR);
            byte lo = ReadMem(SWI3_VECTOR + 1);
            this.regPC = MakeWord(hi, lo);
            // cycles supplémentaires
            this.cycles += 4;
        }

        private void InstrSYNC()
        {
            // cycles supplémentaires
            this.cycles += 3;
            // le processeur se met en attente d'une interruption
            this.stopped = true;
        }

        private void InstrTFR(byte postByte)
        {
            switch (postByte) {
                case 0x00: /* NOP */ break;
                case 0x01:
                    this.regX = this.RegisterD;
                    break;
                case 0x02:
                    this.regY = this.RegisterD;
                    break;
                case 0x03:
                    this.regU = this.RegisterD;
                    break;
                case 0x04:
                    this.regS = this.RegisterD;
                    break;
                case 0x05:
                    this.regPC = this.RegisterD;
                    break;

                case 0x10:
                    this.RegisterD = this.regX;
                    break;
                case 0x11: /* NOP */ break;
                case 0x12:
                    this.regY = this.regX;
                    break;
                case 0x13:
                    this.regU = this.regX;
                    break;
                case 0x14:
                    this.regS = this.regX;
                    break;
                case 0x15:
                    this.regPC = this.regX;
                    break;

                case 0x20:
                    this.RegisterD = this.regY;
                    break;
                case 0x21:
                    this.regX = this.regY;
                    break;
                case 0x22: /* NOP */ break;
                case 0x23:
                    this.regU = this.regY;
                    break;
                case 0x24:
                    this.regS = this.regY;
                    break;
                case 0x25:
                    this.regPC = this.regY;
                    break;

                case 0x30:
                    this.RegisterD = this.regU;
                    break;
                case 0x31:
                    this.regX = this.regU;
                    break;
                case 0x32:
                    this.regY = this.regU;
                    break;
                case 0x33: /* NOP */ break;
                case 0x34:
                    this.regS = this.regU;
                    break;
                case 0x35:
                    this.regPC = this.regU;
                    break;

                case 0x40:
                    this.RegisterD = this.regS;
                    break;
                case 0x41:
                    this.regX = this.regS;
                    break;
                case 0x42:
                    this.regY = this.regS;
                    break;
                case 0x43:
                    this.regU = this.regS;
                    break;
                case 0x44: /* NOP */ break;
                case 0x45:
                    this.regPC = this.regS;
                    break;

                case 0x50:
                    this.RegisterD = this.regPC;
                    break;
                case 0x51:
                    this.regX = this.regPC;
                    break;
                case 0x52:
                    this.regY = this.regPC;
                    break;
                case 0x53:
                    this.regU = this.regPC;
                    break;
                case 0x54:
                    this.regS = this.regPC;
                    break;
                case 0x55: /* NOP */ break;

                case 0x88: /* NOP */ break;
                case 0x89:
                    this.regB = this.regA;
                    break;
                case 0x8a:
                    this.RegisterCC = this.regA;
                    break;
                case 0x8b:
                    this.regDP = this.regA;
                    break;

                case 0x98:
                    this.regA = this.regB;
                    break;
                case 0x99: /* NOP */ break;
                case 0x9a:
                    this.RegisterCC = this.regB;
                    break;
                case 0x9b:
                    this.regDP = this.regB;
                    break;

                case 0xa8:
                    this.regA = this.RegisterCC;
                    break;
                case 0xa9:
                    this.regB = this.RegisterCC;
                    break;
                case 0xaa: /* NOP */ break;
                case 0xab:
                    this.regDP = this.RegisterCC;
                    break;

                case 0xb8:
                    this.regA = this.regDP;
                    break;
                case 0xb9:
                    this.regB = this.regDP;
                    break;
                case 0xba:
                    this.RegisterCC = this.regDP;
                    break;
                case 0xbb: /* NOP */ break;

                default:
                    throw new UnknownOpcodeException(
                            this.regPC - 1, postByte,
                            String.Format(ERR_BAD_EXG_TFR_POSTBYTE,
                                          this.regPC - 1, postByte, "TFR"));
            }
            // cycles supplémentaires
            this.cycles += 4;
        }

        private void InstrTSTA()
        {
            DoTestByte(this.regA);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrTSTB()
        {
            DoTestByte(this.regB);
            // cycle supplémentaire
            this.cycles++;
        }

        private void InstrTST(ushort addr)
        {
            byte val = ReadMem(addr);
            DoTestByte(val);
            // cycles supplémentaires
            this.cycles += 2;
        }

        /* ~~~~ analyse des opcodes ~~~~ */

        private bool ExecOpcodeP1(byte opcode)
        {
            byte val8, post;
            ushort addr, val16;
            switch (opcode) {
                case 0x00:
                    addr = AddrModeDirectAddress();
                    InstrNEG(addr);
                    return true;
                case 0x03:
                    addr = AddrModeDirectAddress();
                    InstrCOM(addr);
                    return true;
                case 0x04:
                    addr = AddrModeDirectAddress();
                    InstrLSR(addr);
                    return true;
                case 0x06:
                    addr = AddrModeDirectAddress();
                    InstrROR(addr);
                    return true;
                case 0x07:
                    addr = AddrModeDirectAddress();
                    InstrASR(addr);
                    return true;
                case 0x08:
                    addr = AddrModeDirectAddress();
                    InstrLSL(addr);
                    return true;
                case 0x09:
                    addr = AddrModeDirectAddress();
                    InstrROL(addr);
                    return true;
                case 0x0a:
                    addr = AddrModeDirectAddress();
                    InstrDEC(addr);
                    return true;
                case 0x0c:
                    addr = AddrModeDirectAddress();
                    InstrINC(addr);
                    return true;
                case 0x0d:
                    addr = AddrModeDirectAddress();
                    InstrTST(addr);
                    return true;
                case 0x0e:
                    addr = AddrModeDirectAddress();
                    InstrJMP(addr);
                    return true;
                case 0x0f:
                    addr = AddrModeDirectAddress();
                    InstrCLR(addr);
                    return true;

                case 0x10:
                    // opcodes de la "page 2", gérés ailleurs
                    break;
                case 0x11:
                    // opcodes de la "page 3", gérés ailleurs
                    break;

                case 0x12:
                    InstrNOP();
                    return true;
                case 0x13:
                    InstrSYNC();
                    return true;
                case 0x16:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBRA(addr);
                    return true;
                case 0x17:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBSR(addr);
                    return true;
                case 0x19:
                    InstrDAA();
                    return true;
                case 0x1a:
                    val8 = AddrModeImmediateValue();
                    InstrORCC(val8);
                    return true;
                case 0x1c:
                    val8 = AddrModeImmediateValue();
                    InstrANDCC(val8);
                    return true;
                case 0x1d:
                    InstrSEX();
                    return true;
                case 0x1e:
                    post = AddrModeImmediateValue();
                    InstrEXG(post);
                    return true;
                case 0x1f:
                    post = AddrModeImmediateValue();
                    InstrTFR(post);
                    return true;

                case 0x20:
                    addr = AddrModeRelativeAddress();
                    InstrBRA(addr);
                    return true;
                case 0x21:
                    addr = AddrModeRelativeAddress();
                    InstrBRN(addr);
                    return true;
                case 0x22:
                    addr = AddrModeRelativeAddress();
                    InstrBHI(addr);
                    return true;
                case 0x23:
                    addr = AddrModeRelativeAddress();
                    InstrBLS(addr);
                    return true;
                case 0x24:
                    addr = AddrModeRelativeAddress();
                    InstrBCC(addr);
                    return true;
                case 0x25:
                    addr = AddrModeRelativeAddress();
                    InstrBCS(addr);
                    return true;
                case 0x26:
                    addr = AddrModeRelativeAddress();
                    InstrBNE(addr);
                    return true;
                case 0x27:
                    addr = AddrModeRelativeAddress();
                    InstrBEQ(addr);
                    return true;
                case 0x28:
                    addr = AddrModeRelativeAddress();
                    InstrBVC(addr);
                    return true;
                case 0x29:
                    addr = AddrModeRelativeAddress();
                    InstrBVS(addr);
                    return true;
                case 0x2a:
                    addr = AddrModeRelativeAddress();
                    InstrBPL(addr);
                    return true;
                case 0x2b:
                    addr = AddrModeRelativeAddress();
                    InstrBMI(addr);
                    return true;
                case 0x2c:
                    addr = AddrModeRelativeAddress();
                    InstrBGE(addr);
                    return true;
                case 0x2d:
                    addr = AddrModeRelativeAddress();
                    InstrBLT(addr);
                    return true;
                case 0x2e:
                    addr = AddrModeRelativeAddress();
                    InstrBGT(addr);
                    return true;
                case 0x2f:
                    addr = AddrModeRelativeAddress();
                    InstrBLE(addr);
                    return true;

                case 0x30:
                    addr = AddrModeIndexedAddress();
                    InstrLEAX(addr);
                    return true;
                case 0x31:
                    addr = AddrModeIndexedAddress();
                    InstrLEAY(addr);
                    return true;
                case 0x32:
                    addr = AddrModeIndexedAddress();
                    InstrLEAS(addr);
                    return true;
                case 0x33:
                    addr = AddrModeIndexedAddress();
                    InstrLEAU(addr);
                    return true;
                case 0x34:
                    post = AddrModeImmediateValue();
                    InstrPSHS(post);
                    return true;
                case 0x35:
                    post = AddrModeImmediateValue();
                    InstrPULS(post);
                    return true;
                case 0x36:
                    post = AddrModeImmediateValue();
                    InstrPSHU(post);
                    return true;
                case 0x37:
                    post = AddrModeImmediateValue();
                    InstrPULU(post);
                    return true;
                case 0x39:
                    InstrRTS();
                    return true;
                case 0x3a:
                    InstrABX();
                    return true;
                case 0x3b:
                    InstrRTI();
                    return true;
                case 0x3c:
                    val8 = AddrModeImmediateValue();
                    InstrCWAI(val8);
                    return true;
                case 0x3d:
                    InstrMUL();
                    return true;
                case 0x3f:
                    InstrSWI();
                    return true;

                case 0x40:
                    InstrNEGA();
                    return true;
                case 0x43:
                    InstrCOMA();
                    return true;
                case 0x44:
                    InstrLSRA();
                    return true;
                case 0x46:
                    InstrRORA();
                    return true;
                case 0x47:
                    InstrASRA();
                    return true;
                case 0x48:
                    InstrLSLA();
                    return true;
                case 0x49:
                    InstrROLA();
                    return true;
                case 0x4a:
                    InstrDECA();
                    return true;
                case 0x4c:
                    InstrINCA();
                    return true;
                case 0x4d:
                    InstrTSTA();
                    return true;
                case 0x4f:
                    InstrCLRA();
                    return true;

                case 0x50:
                    InstrNEGB();
                    return true;
                case 0x53:
                    InstrCOMB();
                    return true;
                case 0x54:
                    InstrLSRB();
                    return true;
                case 0x56:
                    InstrRORB();
                    return true;
                case 0x57:
                    InstrASRB();
                    return true;
                case 0x58:
                    InstrLSLB();
                    return true;
                case 0x59:
                    InstrROLB();
                    return true;
                case 0x5a:
                    InstrDECB();
                    return true;
                case 0x5c:
                    InstrINCB();
                    return true;
                case 0x5d:
                    InstrTSTB();
                    return true;
                case 0x5f:
                    InstrCLRB();
                    return true;

                case 0x60:
                    addr = AddrModeIndexedAddress();
                    InstrNEG(addr);
                    return true;
                case 0x63:
                    addr = AddrModeIndexedAddress();
                    InstrCOM(addr);
                    return true;
                case 0x64:
                    addr = AddrModeIndexedAddress();
                    InstrLSR(addr);
                    return true;
                case 0x66:
                    addr = AddrModeIndexedAddress();
                    InstrROR(addr);
                    return true;
                case 0x67:
                    addr = AddrModeIndexedAddress();
                    InstrASR(addr);
                    return true;
                case 0x68:
                    addr = AddrModeIndexedAddress();
                    InstrLSL(addr);
                    return true;
                case 0x69:
                    addr = AddrModeIndexedAddress();
                    InstrROL(addr);
                    return true;
                case 0x6a:
                    addr = AddrModeIndexedAddress();
                    InstrDEC(addr);
                    return true;
                case 0x6c:
                    addr = AddrModeIndexedAddress();
                    InstrINC(addr);
                    return true;
                case 0x6d:
                    addr = AddrModeIndexedAddress();
                    InstrTST(addr);
                    return true;
                case 0x6e:
                    addr = AddrModeIndexedAddress();
                    InstrJMP(addr);
                    return true;
                case 0x6f:
                    addr = AddrModeIndexedAddress();
                    InstrCLR(addr);
                    return true;

                case 0x70:
                    addr = AddrModeExtendedAddress();
                    InstrNEG(addr);
                    return true;
                case 0x73:
                    addr = AddrModeExtendedAddress();
                    InstrCOM(addr);
                    return true;
                case 0x74:
                    addr = AddrModeExtendedAddress();
                    InstrLSR(addr);
                    return true;
                case 0x76:
                    addr = AddrModeExtendedAddress();
                    InstrROR(addr);
                    return true;
                case 0x77:
                    addr = AddrModeExtendedAddress();
                    InstrASR(addr);
                    return true;
                case 0x78:
                    addr = AddrModeExtendedAddress();
                    InstrLSL(addr);
                    return true;
                case 0x79:
                    addr = AddrModeExtendedAddress();
                    InstrROL(addr);
                    return true;
                case 0x7a:
                    addr = AddrModeExtendedAddress();
                    InstrDEC(addr);
                    return true;
                case 0x7c:
                    addr = AddrModeExtendedAddress();
                    InstrINC(addr);
                    return true;
                case 0x7d:
                    addr = AddrModeExtendedAddress();
                    InstrTST(addr);
                    return true;
                case 0x7e:
                    addr = AddrModeExtendedAddress();
                    InstrJMP(addr);
                    return true;
                case 0x7f:
                    addr = AddrModeExtendedAddress();
                    InstrCLR(addr);
                    return true;

                case 0x80:
                    val8 = AddrModeImmediateValue();
                    InstrSUBA(val8);
                    return true;
                case 0x81:
                    val8 = AddrModeImmediateValue();
                    InstrCMPA(val8);
                    return true;
                case 0x82:
                    val8 = AddrModeImmediateValue();
                    InstrSBCA(val8);
                    return true;
                case 0x83:
                    val16 = AddrModeImmediate16bitValue();
                    InstrSUBD(val16);
                    return true;
                case 0x84:
                    val8 = AddrModeImmediateValue();
                    InstrANDA(val8);
                    return true;
                case 0x85:
                    val8 = AddrModeImmediateValue();
                    InstrBITA(val8);
                    return true;
                case 0x86:
                    val8 = AddrModeImmediateValue();
                    InstrLDA(val8);
                    return true;
                case 0x88:
                    val8 = AddrModeImmediateValue();
                    InstrEORA(val8);
                    return true;
                case 0x89:
                    val8 = AddrModeImmediateValue();
                    InstrADCA(val8);
                    return true;
                case 0x8a:
                    val8 = AddrModeImmediateValue();
                    InstrORA(val8);
                    return true;
                case 0x8b:
                    val8 = AddrModeImmediateValue();
                    InstrADDA(val8);
                    return true;
                case 0x8c:
                    val16 = AddrModeImmediate16bitValue();
                    InstrCMPX(val16);
                    return true;
                case 0x8d:
                    addr = AddrModeRelativeAddress();
                    InstrBSR(addr);
                    return true;
                case 0x8e:
                    val16 = AddrModeImmediate16bitValue();
                    InstrLDX(val16);
                    return true;

                case 0x90:
                    val8 = AddrModeDirectValue();
                    InstrSUBA(val8);
                    return true;
                case 0x91:
                    val8 = AddrModeDirectValue();
                    InstrCMPA(val8);
                    return true;
                case 0x92:
                    val8 = AddrModeDirectValue();
                    InstrSBCA(val8);
                    return true;
                case 0x93:
                    val16 = AddrModeDirect16bitValue();
                    InstrSUBD(val16);
                    return true;
                case 0x94:
                    val8 = AddrModeDirectValue();
                    InstrANDA(val8);
                    return true;
                case 0x95:
                    val8 = AddrModeDirectValue();
                    InstrBITA(val8);
                    return true;
                case 0x96:
                    val8 = AddrModeDirectValue();
                    InstrLDA(val8);
                    return true;
                case 0x97:
                    addr = AddrModeDirectAddress();
                    InstrSTA(addr);
                    return true;
                case 0x98:
                    val8 = AddrModeDirectValue();
                    InstrEORA(val8);
                    return true;
                case 0x99:
                    val8 = AddrModeDirectValue();
                    InstrADCA(val8);
                    return true;
                case 0x9a:
                    val8 = AddrModeDirectValue();
                    InstrORA(val8);
                    return true;
                case 0x9b:
                    val8 = AddrModeDirectValue();
                    InstrADDA(val8);
                    return true;
                case 0x9c:
                    val16 = AddrModeDirect16bitValue();
                    InstrCMPX(val16);
                    return true;
                case 0x9d:
                    addr = AddrModeDirectAddress();
                    InstrJSR(addr);
                    return true;
                case 0x9e:
                    val16 = AddrModeDirect16bitValue();
                    InstrLDX(val16);
                    return true;
                case 0x9f:
                    addr = AddrModeDirectAddress();
                    InstrSTX(addr);
                    return true;

                case 0xa0:
                    val8 = AddrModeIndexedValue();
                    InstrSUBA(val8);
                    return true;
                case 0xa1:
                    val8 = AddrModeIndexedValue();
                    InstrCMPA(val8);
                    return true;
                case 0xa2:
                    val8 = AddrModeIndexedValue();
                    InstrSBCA(val8);
                    return true;
                case 0xa3:
                    val16 = AddrModeIndexed16bitValue();
                    InstrSUBD(val16);
                    return true;
                case 0xa4:
                    val8 = AddrModeIndexedValue();
                    InstrANDA(val8);
                    return true;
                case 0xa5:
                    val8 = AddrModeIndexedValue();
                    InstrBITA(val8);
                    return true;
                case 0xa6:
                    val8 = AddrModeIndexedValue();
                    InstrLDA(val8);
                    return true;
                case 0xa7:
                    addr = AddrModeIndexedAddress();
                    InstrSTA(addr);
                    return true;
                case 0xa8:
                    val8 = AddrModeIndexedValue();
                    InstrEORA(val8);
                    return true;
                case 0xa9:
                    val8 = AddrModeIndexedValue();
                    InstrADCA(val8);
                    return true;
                case 0xaa:
                    val8 = AddrModeIndexedValue();
                    InstrORA(val8);
                    return true;
                case 0xab:
                    val8 = AddrModeIndexedValue();
                    InstrADDA(val8);
                    return true;
                case 0xac:
                    val16 = AddrModeIndexed16bitValue();
                    InstrCMPX(val16);
                    return true;
                case 0xad:
                    addr = AddrModeIndexedAddress();
                    InstrJSR(addr);
                    return true;
                case 0xae:
                    val16 = AddrModeIndexed16bitValue();
                    InstrLDX(val16);
                    return true;
                case 0xaf:
                    addr = AddrModeIndexedAddress();
                    InstrSTX(addr);
                    return true;

                case 0xb0:
                    val8 = AddrModeExtendedValue();
                    InstrSUBA(val8);
                    return true;
                case 0xb1:
                    val8 = AddrModeExtendedValue();
                    InstrCMPA(val8);
                    return true;
                case 0xb2:
                    val8 = AddrModeExtendedValue();
                    InstrSBCA(val8);
                    return true;
                case 0xb3:
                    val16 = AddrModeExtended16bitValue();
                    InstrSUBD(val16);
                    return true;
                case 0xb4:
                    val8 = AddrModeExtendedValue();
                    InstrANDA(val8);
                    return true;
                case 0xb5:
                    val8 = AddrModeExtendedValue();
                    InstrBITA(val8);
                    return true;
                case 0xb6:
                    val8 = AddrModeExtendedValue();
                    InstrLDA(val8);
                    return true;
                case 0xb7:
                    addr = AddrModeExtendedAddress();
                    InstrSTA(addr);
                    return true;
                case 0xb8:
                    val8 = AddrModeExtendedValue();
                    InstrEORA(val8);
                    return true;
                case 0xb9:
                    val8 = AddrModeExtendedValue();
                    InstrADCA(val8);
                    return true;
                case 0xba:
                    val8 = AddrModeExtendedValue();
                    InstrORA(val8);
                    return true;
                case 0xbb:
                    val8 = AddrModeExtendedValue();
                    InstrADDA(val8);
                    return true;
                case 0xbc:
                    val16 = AddrModeExtended16bitValue();
                    InstrCMPX(val16);
                    return true;
                case 0xbd:
                    addr = AddrModeExtendedAddress();
                    InstrJSR(addr);
                    return true;
                case 0xbe:
                    val16 = AddrModeExtended16bitValue();
                    InstrLDX(val16);
                    return true;
                case 0xbf:
                    addr = AddrModeExtendedAddress();
                    InstrSTX(addr);
                    return true;

                case 0xc0:
                    val8 = AddrModeImmediateValue();
                    InstrSUBB(val8);
                    return true;
                case 0xc1:
                    val8 = AddrModeImmediateValue();
                    InstrCMPB(val8);
                    return true;
                case 0xc2:
                    val8 = AddrModeImmediateValue();
                    InstrSBCB(val8);
                    return true;
                case 0xc3:
                    val16 = AddrModeImmediate16bitValue();
                    InstrADDD(val16);
                    return true;
                case 0xc4:
                    val8 = AddrModeImmediateValue();
                    InstrANDB(val8);
                    return true;
                case 0xc5:
                    val8 = AddrModeImmediateValue();
                    InstrBITB(val8);
                    return true;
                case 0xc6:
                    val8 = AddrModeImmediateValue();
                    InstrLDB(val8);
                    return true;
                case 0xc8:
                    val8 = AddrModeImmediateValue();
                    InstrEORB(val8);
                    return true;
                case 0xc9:
                    val8 = AddrModeImmediateValue();
                    InstrADCB(val8);
                    return true;
                case 0xca:
                    val8 = AddrModeImmediateValue();
                    InstrORB(val8);
                    return true;
                case 0xcb:
                    val8 = AddrModeImmediateValue();
                    InstrADDB(val8);
                    return true;
                case 0xcc:
                    val16 = AddrModeImmediate16bitValue();
                    InstrLDD(val16);
                    return true;
                case 0xce:
                    val16 = AddrModeImmediate16bitValue();
                    InstrLDU(val16);
                    return true;

                case 0xd0:
                    val8 = AddrModeDirectValue();
                    InstrSUBB(val8);
                    return true;
                case 0xd1:
                    val8 = AddrModeDirectValue();
                    InstrCMPB(val8);
                    return true;
                case 0xd2:
                    val8 = AddrModeDirectValue();
                    InstrSBCB(val8);
                    return true;
                case 0xd3:
                    val16 = AddrModeDirect16bitValue();
                    InstrADDD(val16);
                    return true;
                case 0xd4:
                    val8 = AddrModeDirectValue();
                    InstrANDB(val8);
                    return true;
                case 0xd5:
                    val8 = AddrModeDirectValue();
                    InstrBITB(val8);
                    return true;
                case 0xd6:
                    val8 = AddrModeDirectValue();
                    InstrLDB(val8);
                    return true;
                case 0xd7:
                    addr = AddrModeDirectAddress();
                    InstrSTB(addr);
                    return true;
                case 0xd8:
                    val8 = AddrModeDirectValue();
                    InstrEORB(val8);
                    return true;
                case 0xd9:
                    val8 = AddrModeDirectValue();
                    InstrADCB(val8);
                    return true;
                case 0xda:
                    val8 = AddrModeDirectValue();
                    InstrORB(val8);
                    return true;
                case 0xdb:
                    val8 = AddrModeDirectValue();
                    InstrADDB(val8);
                    return true;
                case 0xdc:
                    val16 = AddrModeDirect16bitValue();
                    InstrLDD(val16);
                    return true;
                case 0xdd:
                    addr = AddrModeDirectAddress();
                    InstrSTD(addr);
                    return true;
                case 0xde:
                    val16 = AddrModeDirect16bitValue();
                    InstrLDU(val16);
                    return true;
                case 0xdf:
                    addr = AddrModeDirectAddress();
                    InstrSTU(addr);
                    return true;

                case 0xe0:
                    val8 = AddrModeIndexedValue();
                    InstrSUBB(val8);
                    return true;
                case 0xe1:
                    val8 = AddrModeIndexedValue();
                    InstrCMPB(val8);
                    return true;
                case 0xe2:
                    val8 = AddrModeIndexedValue();
                    InstrSBCB(val8);
                    return true;
                case 0xe3:
                    val16 = AddrModeIndexed16bitValue();
                    InstrADDD(val16);
                    return true;
                case 0xe4:
                    val8 = AddrModeIndexedValue();
                    InstrANDB(val8);
                    return true;
                case 0xe5:
                    val8 = AddrModeIndexedValue();
                    InstrBITB(val8);
                    return true;
                case 0xe6:
                    val8 = AddrModeIndexedValue();
                    InstrLDB(val8);
                    return true;
                case 0xe7:
                    addr = AddrModeIndexedAddress();
                    InstrSTB(addr);
                    return true;
                case 0xe8:
                    val8 = AddrModeIndexedValue();
                    InstrEORB(val8);
                    return true;
                case 0xe9:
                    val8 = AddrModeIndexedValue();
                    InstrADCB(val8);
                    return true;
                case 0xea:
                    val8 = AddrModeIndexedValue();
                    InstrORB(val8);
                    return true;
                case 0xeb:
                    val8 = AddrModeIndexedValue();
                    InstrADDB(val8);
                    return true;
                case 0xec:
                    val16 = AddrModeIndexed16bitValue();
                    InstrLDD(val16);
                    return true;
                case 0xed:
                    addr = AddrModeIndexedAddress();
                    InstrSTD(addr);
                    return true;
                case 0xee:
                    val16 = AddrModeIndexed16bitValue();
                    InstrLDU(val16);
                    return true;
                case 0xef:
                    addr = AddrModeIndexedAddress();
                    InstrSTU(addr);
                    return true;

                case 0xf0:
                    val8 = AddrModeExtendedValue();
                    InstrSUBB(val8);
                    return true;
                case 0xf1:
                    val8 = AddrModeExtendedValue();
                    InstrCMPB(val8);
                    return true;
                case 0xf2:
                    val8 = AddrModeExtendedValue();
                    InstrSBCB(val8);
                    return true;
                case 0xf3:
                    val16 = AddrModeExtended16bitValue();
                    InstrADDD(val16);
                    return true;
                case 0xf4:
                    val8 = AddrModeExtendedValue();
                    InstrANDB(val8);
                    return true;
                case 0xf5:
                    val8 = AddrModeExtendedValue();
                    InstrBITB(val8);
                    return true;
                case 0xf6:
                    val8 = AddrModeExtendedValue();
                    InstrLDB(val8);
                    return true;
                case 0xf7:
                    addr = AddrModeExtendedAddress();
                    InstrSTB(addr);
                    return true;
                case 0xf8:
                    val8 = AddrModeExtendedValue();
                    InstrEORB(val8);
                    return true;
                case 0xf9:
                    val8 = AddrModeExtendedValue();
                    InstrADCB(val8);
                    return true;
                case 0xfa:
                    val8 = AddrModeExtendedValue();
                    InstrORB(val8);
                    return true;
                case 0xfb:
                    val8 = AddrModeExtendedValue();
                    InstrADDB(val8);
                    return true;
                case 0xfc:
                    val16 = AddrModeExtended16bitValue();
                    InstrLDD(val16);
                    return true;
                case 0xfd:
                    addr = AddrModeExtendedAddress();
                    InstrSTD(addr);
                    return true;
                case 0xfe:
                    val16 = AddrModeExtended16bitValue();
                    InstrLDU(val16);
                    return true;
                case 0xff:
                    addr = AddrModeExtendedAddress();
                    InstrSTU(addr);
                    return true;
            }
            /* si on arrive ici, l'opcode est invalide */
            return false;
        }

        private bool ExecOpcodeP2(byte opcode)
        {
            ushort addr, val16;
            switch (opcode) {
                case 0x21:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBRN(addr);
                    return true;
                case 0x22:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBHI(addr);
                    return true;
                case 0x23:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBLS(addr);
                    return true;
                case 0x24:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBCC(addr);
                    return true;
                case 0x25:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBCS(addr);
                    return true;
                case 0x26:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBNE(addr);
                    return true;
                case 0x27:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBEQ(addr);
                    return true;
                case 0x28:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBVC(addr);
                    return true;
                case 0x29:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBVS(addr);
                    return true;
                case 0x2a:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBPL(addr);
                    return true;
                case 0x2b:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBMI(addr);
                    return true;
                case 0x2c:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBGE(addr);
                    return true;
                case 0x2d:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBLT(addr);
                    return true;
                case 0x2e:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBGT(addr);
                    return true;
                case 0x2f:
                    addr = AddrModeLongRelativeAddress();
                    InstrLBLE(addr);
                    return true;

                case 0x3f:
                    InstrSWI2();
                    return true;

                case 0x83:
                    val16 = AddrModeImmediate16bitValue();
                    InstrCMPD(val16);
                    return true;
                case 0x8c:
                    val16 = AddrModeImmediate16bitValue();
                    InstrCMPY(val16);
                    return true;
                case 0x8e:
                    val16 = AddrModeImmediate16bitValue();
                    InstrLDY(val16);
                    return true;

                case 0x93:
                    val16 = AddrModeDirect16bitValue();
                    InstrCMPD(val16);
                    return true;
                case 0x9c:
                    val16 = AddrModeDirect16bitValue();
                    InstrCMPY(val16);
                    return true;
                case 0x9e:
                    val16 = AddrModeDirect16bitValue();
                    InstrLDY(val16);
                    return true;
                case 0x9f:
                    addr = AddrModeDirectAddress();
                    InstrSTY(addr);
                    return true;

                case 0xa3:
                    val16 = AddrModeIndexed16bitValue();
                    InstrCMPD(val16);
                    return true;
                case 0xac:
                    val16 = AddrModeIndexed16bitValue();
                    InstrCMPY(val16);
                    return true;
                case 0xae:
                    val16 = AddrModeIndexed16bitValue();
                    InstrLDY(val16);
                    return true;
                case 0xaf:
                    addr = AddrModeIndexedAddress();
                    InstrSTY(addr);
                    return true;

                case 0xb3:
                    val16 = AddrModeExtended16bitValue();
                    InstrCMPD(val16);
                    return true;
                case 0xbc:
                    val16 = AddrModeExtended16bitValue();
                    InstrCMPY(val16);
                    return true;
                case 0xbe:
                    val16 = AddrModeExtended16bitValue();
                    InstrLDY(val16);
                    return true;
                case 0xbf:
                    addr = AddrModeExtendedAddress();
                    InstrSTY(addr);
                    return true;

                case 0xce:
                    val16 = AddrModeImmediate16bitValue();
                    InstrLDS(val16);
                    return true;

                case 0xde:
                    val16 = AddrModeDirect16bitValue();
                    InstrLDS(val16);
                    return true;
                case 0xdf:
                    addr = AddrModeDirectAddress();
                    InstrSTS(addr);
                    return true;

                case 0xee:
                    val16 = AddrModeIndexed16bitValue();
                    InstrLDS(val16);
                    return true;
                case 0xef:
                    addr = AddrModeIndexedAddress();
                    InstrSTS(addr);
                    return true;

                case 0xfe:
                    val16 = AddrModeExtended16bitValue();
                    InstrLDS(val16);
                    return true;
                case 0xff:
                    addr = AddrModeExtendedAddress();
                    InstrSTS(addr);
                    return true;
            }
            /* si on arrive ici, l'opcode est invalide */
            return false;
        }

        private bool ExecOpcodeP3(byte opcode)
        {
            ushort val16;
            switch (opcode) {
                case 0x3f:
                    InstrSWI3();
                    return true;

                case 0x83:
                    val16 = AddrModeImmediate16bitValue();
                    InstrCMPU(val16);
                    return true;
                case 0x8c:
                    val16 = AddrModeImmediate16bitValue();
                    InstrCMPS(val16);
                    return true;

                case 0x93:
                    val16 = AddrModeDirect16bitValue();
                    InstrCMPU(val16);
                    return true;
                case 0x9c:
                    val16 = AddrModeDirect16bitValue();
                    InstrCMPS(val16);
                    return true;

                case 0xa3:
                    val16 = AddrModeIndexed16bitValue();
                    InstrCMPU(val16);
                    return true;
                case 0xac:
                    val16 = AddrModeIndexed16bitValue();
                    InstrCMPS(val16);
                    return true;

                case 0xb3:
                    val16 = AddrModeExtended16bitValue();
                    InstrCMPU(val16);
                    return true;
                case 0xbc:
                    val16 = AddrModeExtended16bitValue();
                    InstrCMPS(val16);
                    return true;
            }
            /* si on arrive ici, l'opcode est invalide */
            return false;
        }

        /* ~~~~ traçage ~~~~ */

        private void DoTrace()
        {
            this.traceFile.WriteLine(
                    "=> PC=${0:X4} DP=${1:X2}" +
                    " A=${2:X2} B=${3:X2}" +
                    " X=${4:X4} Y=${5:X4}" +
                    " U=${6:X4} S=${7:X4}" +
                    " CC=${8:X2}" +
                    " (E={9} F={10} H={11} I={12} N={13} Z={14} V={15} C={16})",
                    this.regPC,
                    this.regDP,
                    this.regA,
                    this.regB,
                    this.regX,
                    this.regY,
                    this.regU,
                    this.regS,
                    this.RegisterCC,
                    (this.flagE ? 1 : 0),
                    (this.flagF ? 1 : 0),
                    (this.flagH ? 1 : 0),
                    (this.flagI ? 1 : 0),
                    (this.flagN ? 1 : 0),
                    (this.flagZ ? 1 : 0),
                    (this.flagV ? 1 : 0),
                    (this.flagC ? 1 : 0));
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Réinitialise le processeur.
        /// </summary>
        /// <exception cref="AddressUnreadableException">
        /// Si une adresse-mémoire (vecteur RESET ou sa cible)
        /// ne peut pas être lue.
        /// </exception>
        public void Reset()
        {
            // débloque le processeur
            this.stopped = false;
            // initialisation interne des circuits
            this.cycles = 7;
            // désactive toute interruption masquable
            this.flagI = true;
            this.flagF = true;
            // RàZ du registre DP
            this.regDP = 0x00;
            // lecture du vecteur RESET
            byte hi = ReadMem(RESET_VECTOR);
            byte lo = ReadMem(RESET_VECTOR + 1);
            // saute au vecteur ainsi lu
            this.regPC = MakeWord(hi, lo);
            // traçage si besoin est
            if (this.traceFile != null) {
                this.traceFile.WriteLine("\n\n*** RESET! ***\n");
                DoTrace();
            }
        }

        /// <summary>
        /// Lance une interruption matérielle non-masquable (NMI).
        /// </summary>
        /// <exception cref="AddressUnreadableException">
        /// Si une adresse-mémoire (vecteur NMI ou sa cible)
        /// ne peut pas être lue.
        /// </exception>
        public void TriggerNMI()
        {
            this.nmiTrig = true;
        }

        /// <summary>
        /// Exécute l'instruction actuellement pointée par le registre PC.
        /// </summary>
        /// <returns>
        /// Nombre de cycles écoulés pour l'exécution de l'instruction.
        /// </returns>
        /// <exception cref="AddressUnreadableException">
        /// Si le contenu d'une adresse-mémoire nécessaire au travail
        /// du processeur ne peut pas être lu.
        /// </exception>
        public ulong Step()
        {
            ulong cycBegin = this.cycles;

            // la ligne reset empêche le processeur de travailler
            if (this.resetLine) return 0L;

            // l'état stoppé force le processeur à ne rien faire
            if (this.stopped) {
                this.cycles += 2;
                return 2L;
            }

            // une interruption est-elle signalée ?
            if (this.nmiTrig) {
                // NMI : sensible à la transition
                this.nmiTrig = false;
                if (this.traceFile != null) {
                    this.traceFile.WriteLine("*** NMI! ***");
                }
                // débloque le processeur
                this.stopped = false;
                // lance la réponse à l'interruption
                this.cycles += 7;
                // enregistre le contexte actuel
                PushRegsForInterrupt(false);
                // désactive toute interruption masquable
                this.flagI = true;
                this.flagF = true;
                // lecture du vecteur IRQ/BRK
                byte lo = ReadMem(NMI_VECTOR);
                byte hi = ReadMem(NMI_VECTOR + 1);
                // saute au vecteur ainsi lu
                this.regPC = MakeWord(hi, lo);
            } else if (this.irqLine) {
                // interruption masquable
                if (!(this.flagI)) {
                    if (this.traceFile != null) {
                        this.traceFile.WriteLine("*** IRQ! ***");
                    }
                    // débloque le processeur
                    this.stopped = false;
                    // lance la réponse à l'interruption
                    this.cycles += 7;
                    // enregistre le contexte actuel
                    PushRegsForInterrupt(false);
                    // désactive toute autre interruption masquable
                    // *SAUF* les interruptions rapides
                    this.flagI = true;
                    // lecture du vecteur IRQ
                    byte lo = ReadMem(IRQ_VECTOR);
                    byte hi = ReadMem(IRQ_VECTOR + 1);
                    // saute au vecteur ainsi lu
                    this.regPC = MakeWord(hi, lo);
                }
            } else if (this.firqLine) {
                // interruption rapide masquable
                if (!(this.flagF)) {
                    if (this.traceFile != null) {
                        this.traceFile.WriteLine("*** FIRQ! ***");
                    }
                    // débloque le processeur
                    this.stopped = false;
                    // lance la réponse à l'interruption
                    this.cycles += 7;
                    // enregistre le contexte actuel
                    PushRegsForInterrupt(true);
                    // désactive toute autre interruption masquable
                    this.flagI = true;
                    this.flagF = true;
                    // lecture du vecteur FIRQ
                    byte lo = ReadMem(FIRQ_VECTOR);
                    byte hi = ReadMem(FIRQ_VECTOR + 1);
                    // saute au vecteur ainsi lu
                    this.regPC = MakeWord(hi, lo);
                }
            }

            // désassemblage si traçage
            if (this.traceFile != null) {
                this.traceFile.Write(
                        this.traceDisasm.DisassembleInstructionAt(this.regPC));
            }

            // lit, décode et exécute le prochain opcode
            bool ok;
            byte opcode = ReadMem(this.regPC);
            this.cycles++;
            this.regPC++;
            switch (opcode) {
                case 0x10:
                    // opcodes de la "page 2"
                    opcode = ReadMem(this.regPC);
                    this.regPC++;
                    ok = ExecOpcodeP2(opcode);
                    break;
                case 0x11:
                    // opcodes de la "page 3"
                    opcode = ReadMem(this.regPC);
                    this.regPC++;
                    ok = ExecOpcodeP3(opcode);
                    break;
                default:
                    // opcodes de la "page 1" (par défaut)
                    ok = ExecOpcodeP1(opcode);
                    break;
            }

            // opcode invalide !
            if (!ok) {
                switch (this.uoPolicy) {
                    case UnknownOpcodePolicy.ThrowException:
                        throw new UnknownOpcodeException(
                                this.regPC,
                                opcode,
                                String.Format(ERR_UNKNOWN_OPCODE,
                                              this.regPC, opcode));
                    case UnknownOpcodePolicy.DoNop:
                        InstrNOP();
                        break;
                }
            }

            // traçage de l'exécution si besoin est
            if (this.traceFile != null) {
                DoTrace();
            }

            // comptage des cycles écoulés
            ulong cycEnd = this.cycles;
            return cycEnd - cycBegin;
        }

        /// <summary>
        /// Lance l'exécution du processeur pendant AU MOINS
        /// le nombre de cycles passé en paramètre.
        /// <br/>
        /// En effet : toute instruction entamée est terminée
        /// (y compris les éventuelles réponses aux interruptions).
        /// Ainsi, le nombre de cycles exécutés peut être égal ou
        /// supérieur au nombre voulu.
        /// </summary>
        /// <param name="numCycles">
        /// Nombre de cycles processeur à exécuter.
        /// </param>
        /// <returns>
        /// Le nombre de cycles processeur réellement exécutés.
        /// </returns>
        public ulong Run(ulong numCycles)
        {
            ulong cycCount = 0L;

            while (cycCount < numCycles) {
                cycCount += Step();
            }

            return cycCount;
        }


        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Objet espace-mémoire attaché à ce processeur lors de sa création.
        /// (Propriété en lecture seule.)
        /// </summary>
        public IMemorySpace6809 MemorySpace
        {
            get { return this.memSpace; }
        }


        /// <summary>
        /// Accès au registre A (1er Accumulateur) du processeur.
        /// </summary>
        public Byte RegisterA
        {
            get { return this.regA; }
            set { this.regA = value; }
        }

        /// <summary>
        /// Accès au registre B (2ème Accumulateur) du processeur.
        /// </summary>
        public Byte RegisterB
        {
            get { return this.regB; }
            set { this.regB = value; }
        }

        public UInt16 RegisterD
        {
            get { return MakeWord(this.regA, this.regB); }
            set {
                this.regA = HiByte(value);
                this.regB = LoByte(value);
            }
        }

        /// <summary>
        /// Accès au registre DP ("Direct Page") du processeur.
        /// </summary>
        public Byte RegisterDP
        {
            get { return this.regDP; }
            set { this.regDP = value; }
        }

        /// <summary>
        /// Accès au registre d'index X du processeur.
        /// </summary>
        public UInt16 RegisterX
        {
            get { return this.regX; }
            set { this.regX = value; }
        }

        /// <summary>
        /// Accès au registre d'index Y du processeur.
        /// </summary>
        public UInt16 RegisterY
        {
            get { return this.regY; }
            set { this.regY = value; }
        }

        /// <summary>
        /// Accès au registre U ("User stack pointer",
        /// pointeur de pile utilisateur) du processeur.
        /// </summary>
        public UInt16 RegisterU
        {
            get { return this.regU; }
            set { this.regU = value; }
        }

        /// <summary>
        /// Accès au registre S ("System stack pointer",
        /// pointeur de pile système) du processeur.
        /// </summary>
        public UInt16 RegisterS
        {
            get { return this.regS; }
            set { this.regS = value; }
        }

        /// <summary>
        /// Accès au registre PC ("Program Counter", compteur programme
        /// alias compteur ordinal) du processeur.
        /// </summary>
        public UInt16 RegisterPC
        {
            get { return this.regPC; }
            set { this.regPC = value; }
        }

        /// <summary>
        /// Accès au registre CC ("Condition Codes", registre de statut)
        /// du processeur.
        /// </summary>
        public Byte RegisterCC
        {
            // le contenu de ce registre est calculé à la volée
            // en fonction des "flags"
            get {
                byte cc = 0x00;
                if (this.flagE) cc |= FLAG_E;
                if (this.flagF) cc |= FLAG_F;
                if (this.flagH) cc |= FLAG_H;
                if (this.flagI) cc |= FLAG_I;
                if (this.flagN) cc |= FLAG_N;
                if (this.flagZ) cc |= FLAG_Z;
                if (this.flagV) cc |= FLAG_V;
                if (this.flagC) cc |= FLAG_C;
                return cc;
            }
            set {
                this.flagE = ((value & FLAG_E) != 0);
                this.flagF = ((value & FLAG_F) != 0);
                this.flagH = ((value & FLAG_H) != 0);
                this.flagI = ((value & FLAG_I) != 0);
                this.flagN = ((value & FLAG_N) != 0);
                this.flagZ = ((value & FLAG_Z) != 0);
                this.flagV = ((value & FLAG_V) != 0);
                this.flagC = ((value & FLAG_C) != 0);
            }
        }

        /// <summary>
        /// Flag C ("Carry", retenue) dans le registre de statut du processeur.
        /// </summary>
        public Boolean FlagC
        {
            get { return this.flagC; }
            set { this.flagC = value; }
        }

        /// <summary>
        /// Flag V ("oVerflow", débordement) dans le registre de statut
        /// du processeur.
        /// </summary>
        public Boolean FlagV
        {
            get { return this.flagV; }
            set { this.flagV = value; }
        }

        /// <summary>
        /// Flag Z (Zéro) dans le registre de statut du processeur.
        /// </summary>
        public Boolean FlagZ
        {
            get { return this.flagZ; }
            set { this.flagZ = value; }
        }

        /// <summary>
        /// Flag N (Négatif) dans le registre de statut du processeur.
        /// </summary>
        public Boolean FlagN
        {
            get { return this.flagN; }
            set { this.flagN = value; }
        }

        /// <summary>
        /// Flag I (Interruptions masquées) dans le registre de statut
        /// du processeur.
        /// </summary>
        public Boolean FlagI
        {
            get { return this.flagI; }
            set { this.flagI = value; }
        }

        /// <summary>
        /// Flag H ("Half-carry", demi-retenue) dans le registre de
        /// statut du processeur.
        /// </summary>
        public Boolean FlagH
        {
            get { return this.flagH; }
            set { this.flagH = value; }
        }

        /// <summary>
        /// Flag F ("Fast-interrupt" masquées) dans le registre de statut
        /// du processeur.
        /// </summary>
        public Boolean FlagF
        {
            get { return this.flagF; }
            set { this.flagF = value; }
        }

        /// <summary>
        /// Flag E ("Entire status", statut entier empilé) dans le registre
        /// de statut du processeur.
        /// </summary>
        public Boolean FlagE
        {
            get { return this.flagE; }
            set { this.flagE = value; }
        }


        /// <summary>
        /// Nombre de cycles écoulés lors du fonctionnement du processeur.
        /// (Propriété en lecture seule.)
        /// </summary>
        public UInt64 ElapsedCycles
        {
            get { return this.cycles; }
        }


        /// <summary>
        /// Ligne de réinitialisation du processeur.
        /// Cette ligne est sensible au niveau.
        /// </summary>
        public Boolean ResetLine
        {
            get { return this.resetLine; }
            set {
                if (value) Reset();
                this.resetLine = value;
            }
        }

        /// <summary>
        /// Ligne de requête d'interruption matérielle non-masquable.
        /// Cette ligne est sensible à la transition.
        /// </summary>
        public Boolean NMILine
        {
            get { return this.nmiLine; }
            set {
                if (value & !nmiLine) TriggerNMI();
                this.nmiLine = value;
            }
        }

        /// <summary>
        /// Ligne de requête d'interruption matérielle (masquable).
        /// Cette ligne est sensible au niveau.
        /// </summary>
        public Boolean IRQLine
        {
            get { return this.irqLine; }
            set { this.irqLine = value; }
        }

        /// <summary>
        /// Ligne de requête d'interruption rapide (masquable).
        /// Cette ligne est sensible au niveau.
        /// </summary>
        public Boolean FIRQLine
        {
            get { return this.firqLine; }
            set { this.firqLine = value; }
        }


        /// <summary>
        /// Indique si le processeur est "stoppé",
        /// suite à une instruction SYNC ou CWAIT.
        /// (Propriété en lecture seule,
        ///  utiliser une interruption ou un reset
        ///  pour relancer le processeur.)
        /// </summary>
        public Boolean IsStopped
        {
            get { return this.stopped; }
        }


        /// <summary>
        /// Politique de prise en charge des opcodes invalides à l'exécution.
        /// </summary>
        public UnknownOpcodePolicy InvalidOpcodePolicy
        {
            get { return this.uoPolicy; }
            set { this.uoPolicy = value; }
        }


        /// <summary>
        /// Objet d'écriture dans le fichier de traçage
        /// à employer pour l'exécution de ce processeur.
        /// <br/>
        /// Mettre à <code>null</code> pour ne pas faire de trace.
        /// </summary>
        public StreamWriter TraceFileWriter
        {
            get { return this.traceFile; }
            set {
                if (this.traceFile != null) {
                    this.traceFile.Flush();
                }
                this.traceFile = value;
                if (this.traceFile != null) {
                    this.traceDisasm = new Disasm6809(this.memSpace) {
                        InvalidOpcodePolicy =
                            this.uoPolicy
                    };
                } else {
                    this.traceDisasm = null;
                }
            }
        }

    }
}

