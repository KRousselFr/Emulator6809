using System;
using System.Text;


namespace Emulator6809
{
    /// <summary>
    /// Classe désassemblant de le code machine du processeur
    /// Motorola 6809.
    /// </summary>
    public class Disasm6809
    {
        /* =========================== CONSTANTES =========================== */

        // messages affichés
        private const String ERR_UNREADABLE_ADDRESS =
                "Impossible de lire le contenu de l'adresse ${0:X4} !";
        private const String ERR_UNKNOWN_OPCODE =
                "Opcode invalide (${1:X2}) rencontré à l'adresse ${0:X4} !";
        private const String ERR_BAD_INDEX_POSTBYTE =
                "Mauvais encodage pour le mode indexé ({1:X2}) à l'adresse ${0:X4} !";
        private const String ERR_BAD_REGISTER_CODE =
                "Mauvais code registre ({0:X2}) pour EXG / TFR !";


        /* ========================== CHAMPS PRIVÉS ========================= */

        // espace-mémoire attaché au processeur
        // (défini une fois pour toutes à la construction)
        private readonly IMemorySpace6809 memSpace;

        // politique vis-à-vis des opcodes invalides
        private UnknownOpcodePolicy uoPolicy;

        // adresse courante de l'instruction en cours de désassemblage
        private int regPC;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur de référence (et unique) de la classe Disasm6809.
        /// </summary>
        /// <param name="memorySpace">
        /// Espace-mémoire où lire le code binaire à desassembler.
        /// </param>
        public Disasm6809(IMemorySpace6809 memorySpace)
        {
            this.memSpace = memorySpace;
            this.uoPolicy = UnknownOpcodePolicy.DoNop;
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

        private byte ReadMem(int addr)
        {
            byte? memval = this.memSpace.ReadMemory((ushort)addr);
            if (!(memval.HasValue)) {
                throw new AddressUnreadableException(
                        addr,
                        String.Format(ERR_UNREADABLE_ADDRESS,
                                      addr));
            }
            return memval.Value;
        }

        /* ~~~~ implantation des modes d'adressage ~~~~ */

        /* mode d'adressage immédiat sur 8 bits : INSTR #$nn  */
        private string AddrModeImmediate()
        {
            byte val = ReadMem(this.regPC);
            this.regPC++;
            return String.Format("#${0:X2}", val);
        }

        /* mode d'adressage immédiat sur 16 bits : INSTR #$nnnn  */
        private string AddrModeImmediate16bit()
        {
            byte hi = ReadMem(this.regPC);
            this.regPC++;
            byte lo = ReadMem(this.regPC);
            this.regPC++;
            ushort val = MakeWord(hi, lo);
            return String.Format("#${0:X4}", val);
        }

        /* mode d'adressage relatif : Bxx ±nnn  */
        private string AddrModeRelative()
        {
            sbyte dpl = (sbyte)(ReadMem(this.regPC));
            this.regPC++;
            ushort addr = (ushort)(this.regPC + dpl);
            return String.Format("{0:+000;-000}  (-> ${1:X4})", dpl, addr);
        }

        /* mode d'adressage relatif long : Bxx ±nnnnn  */
        private string AddrModeLongRelative()
        {
            int dpl = (ReadMem(this.regPC) << 8);
            this.regPC++;
            dpl |= ReadMem(this.regPC);
            this.regPC++;
            ushort addr = (ushort)(this.regPC + (short)dpl);
            return String.Format("{0:+00000;-00000}  (-> ${1:X4}", dpl, addr);
        }

        /* mode d'adressage étendu : INSTR $xxxx  */
        private string AddrModeExtended()
        {
            byte hi = ReadMem(this.regPC);
            this.regPC++;
            byte lo = ReadMem(this.regPC);
            this.regPC++;
            ushort addr = MakeWord(hi, lo);
            return String.Format(">${0:X4}", addr);
        }

        /* mode d'adressage direct : INSTR $xx  */
        private string AddrModeDirect()
        {
            byte lo = ReadMem(this.regPC);
            this.regPC++;
            return String.Format("<${0:X2}", lo);
        }

        /* mode d'adressage indexé / indirect */
        private string AddrModeIndexed()
        {
            byte postByte = ReadMem(this.regPC);
            this.regPC++;
            /* registre utilisé comme index */
            int numReg = (postByte & 0x60) >> 5;
            string idxReg;
            switch (numReg) {
                case 0: idxReg = "X"; break;
                case 1: idxReg = "Y"; break;
                case 2: idxReg = "U"; break;
                case 3: idxReg = "S"; break;
                default:
                    throw new Exception("Erreur interne AddrModeIndexed() !");
            }
            /* bit de poids fort à 0 ? */
            if ((postByte & 0x80) == 0) {
                /* oui => indexé avec déplacement sur 5 bits signé */
                int val = postByte & 0x1f;
                if (val > 0x0f) val |= 0xf0;
                sbyte displ = (sbyte)val;
                return String.Format("{0:+00;-00}, {1}", displ, idxReg);
            }
            /* bit d'indirection */
            bool indirect = ((postByte & 0x10) != 0);
            /* type d'adressage */
            int loNibble = postByte & 0x0f;
            switch (loNibble) {
                case 0x0: {
                    /* post-incrémentation simple */
                    return String.Format(", {0}+", idxReg);
                }
                case 0x1: {
                    /* post-incrémentation double (indirecte ou non) */
                    if (indirect) {
                        return String.Format("[, {0}++]", idxReg);
                    } else {
                        return String.Format(", {0}++", idxReg);
                    }
                }
                case 0x2: {
                    /* pré-décrémentation simple */
                    return String.Format(", -{0}", idxReg);
                }
                case 0x3: {
                    /* pré-décrémentation double (indirecte ou non) */
                    if (indirect) {
                        return String.Format("[, --{0}]", idxReg);
                    } else {
                        return String.Format(", --{0}", idxReg);
                    }
                }
                case 0x4: {
                    /* indexation simple (indirecte ou non) */
                    if (indirect) {
                        return String.Format("[, {0}]", idxReg);
                    } else {
                        return String.Format(", {0}", idxReg);
                    }
                }
                case 0x5: {
                    /* indexation avec déplacement du contenu de B
                       (indirecte ou non) */
                    if (indirect) {
                        return String.Format("[B, {0}]", idxReg);
                    } else {
                        return String.Format("B, {0}", idxReg);
                    }
                }
                case 0x6: {
                    /* indexation avec déplacement du contenu de A
                       (indirecte ou non) */
                    if (indirect) {
                        return String.Format("[A, {0}]", idxReg);
                    } else {
                        return String.Format("A, {0}", idxReg);
                    }
                }
                case 0x8: {
                    /* indexation avec déplacement constant sur 8 bits
                       (indirecte ou non) */
                    sbyte displ = (sbyte)(ReadMem(this.regPC));
                    this.regPC++;
                    if (indirect) {
                        return String.Format("[{0:+000;-000}, {1}]",
                                             displ, idxReg);
                    } else {
                        return String.Format("{0:+000;-000}, {1}",
                                             displ, idxReg);
                    }
                }
                case 0x9: {
                    /* indexation avec déplacement constant sur 16 bits
                       (indirecte ou non) */
                    byte hi = ReadMem(this.regPC);
                    this.regPC++;
                    byte lo = ReadMem(this.regPC);
                    this.regPC++;
                    short displ = (short)(MakeWord(hi, lo));
                    if (indirect) {
                        return String.Format("[{0:+00000;-00000}, {1}]",
                                             displ, idxReg);
                    } else {
                        return String.Format("{0:+00000;-00000}, {1}",
                                             displ, idxReg);
                    }
                }
                case 0xb: {
                    /* indexation avec déplacement du contenu de D
                       (indirecte ou non) */
                    if (indirect) {
                        return String.Format("[D, {0}]", idxReg);
                    } else {
                        return String.Format("D, {0}", idxReg);
                    }
                }
                case 0xc: {
                    /* relatif au PC avec déplacement constant sur 8 bits
                       (indirect ou non) */
                    sbyte displ = (sbyte)(ReadMem(this.regPC));
                    this.regPC++;
                    if (indirect) {
                        return String.Format("[{0:+000;-000}, PC]",
                                             displ);
                    } else {
                        return String.Format("{0:+000;-000}, PC",
                                             displ);
                    }
                }
                case 0xd: {
                    /* relatif au PC avec déplacement constant sur 16 bits
                       (indirect ou non) */
                    byte hi = ReadMem(this.regPC);
                    this.regPC++;
                    byte lo = ReadMem(this.regPC);
                    this.regPC++;
                    short displ = (short)(MakeWord(hi, lo));
                    if (indirect) {
                        return String.Format("[{0:+00000;-00000}, PC]",
                                             displ);
                    } else {
                        return String.Format("{0:+00000;-00000}, PC",
                                             displ);
                    }
                }
                case 0xf: {
                    /* indirect étendu */
                    byte hi = ReadMem(this.regPC);
                    this.regPC++;
                    byte lo = ReadMem(this.regPC);
                    this.regPC++;
                    ushort addr = MakeWord(hi, lo);
                    return String.Format("[${0:X4}]", addr);
                }
            }
            /* si on arrive ici, l'octet décrivant
               le mode indexé est invalide */
            if (this.uoPolicy == UnknownOpcodePolicy.ThrowException) {
                throw new UnknownOpcodeException(this.regPC - 1, postByte,
                        String.Format(ERR_BAD_INDEX_POSTBYTE,
                                      this.regPC - 1, postByte));
            } else {
                return "*** ?!?";
            }
        }

        /* paire de registres, pour les instructions EXG et TFR */
        private string GetRegisterPair()
        {
            byte postByte = ReadMem(this.regPC);
            this.regPC++;
            byte niblSrc = (byte)((postByte & 0xf0) >> 4);
            string src = __nibble2Reg(niblSrc);
            byte niblDest = (byte)(postByte & 0x0f);
            string dest = __nibble2Reg(niblDest);
            return String.Format("{0}, {1}", src, dest);
        }
        private string __nibble2Reg(byte nibble)
        {
            switch (nibble) {
                case 0x0:
                    return "D";
                case 0x1:
                    return "X";
                case 0x2:
                    return "Y";
                case 0x3:
                    return "U";
                case 0x4:
                    return "S";
                case 0x5:
                    return "PC";

                case 0x8:
                    return "A";
                case 0x9:
                    return "B";
                case 0xa:
                    return "CC";
                case 0xb:
                    return "DP";

                default:
                    if (this.uoPolicy == UnknownOpcodePolicy.ThrowException) {
                        throw new ArgumentException(String.Format(
                            ERR_BAD_REGISTER_CODE, nibble));
                    } else {
                        return "*** ?!?";
                    }
            }
        }

        /* liste de registres pour PuSH et PULl */
        private string GetRegisterList(bool userStack)
        {
            byte postByte = ReadMem(this.regPC);
            this.regPC++;
            StringBuilder sb = new StringBuilder();
            if ((postByte & 0x01) != 0) sb.Append("CC/");
            if ((postByte & 0x02) != 0) sb.Append("A/");
            if ((postByte & 0x04) != 0) sb.Append("B/");
            if ((postByte & 0x08) != 0) sb.Append("DP/");
            if ((postByte & 0x10) != 0) sb.Append("X/");
            if ((postByte & 0x20) != 0) sb.Append("Y/");
            if ((postByte & 0x40) != 0) {
                if (userStack) sb.Append("S/");
                else sb.Append("U/");
            }
            if ((postByte & 0x80) != 0) sb.Append("PC/");
            if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
            return sb.ToString();
        }

        /* ~~~~ désassemblage des opcodes ~~~~ */

        /* "Page 1" : opcodes sur un unique octet */
        private string DisasmOpcodeP1(byte opcode)
        {
            string mnemo = null, args = String.Empty;
            switch (opcode) {
                case 0x00:
                    mnemo = "NEG";
                    args = AddrModeDirect();
                    break;
                case 0x03:
                    mnemo = "COM";
                    args = AddrModeDirect();
                    break;
                case 0x04:
                    mnemo = "LSR";
                    args = AddrModeDirect();
                    break;
                case 0x06:
                    mnemo = "ROR";
                    args = AddrModeDirect();
                    break;
                case 0x07:
                    mnemo = "ASR";
                    args = AddrModeDirect();
                    break;
                case 0x08:
                    mnemo = "LSL";   // ASL == LSL
                    args = AddrModeDirect();
                    break;
                case 0x09:
                    mnemo = "ROL";
                    args = AddrModeDirect();
                    break;
                case 0x0a:
                    mnemo = "DEC";
                    args = AddrModeDirect();
                    break;
                case 0x0c:
                    mnemo = "INC";
                    args = AddrModeDirect();
                    break;
                case 0x0d:
                    mnemo = "TST";
                    args = AddrModeDirect();
                    break;
                case 0x0e:
                    mnemo = "JMP";
                    args = AddrModeDirect();
                    break;
                case 0x0f:
                    mnemo = "CLR";
                    args = AddrModeDirect();
                    break;

                case 0x10:
                    // opcodes de la "page 2", gérés ailleurs
                    break;
                case 0x11:
                    // opcodes de la "page 3", gérés ailleurs
                    break;

                case 0x12:
                    mnemo = "NOP";
                    break;
                case 0x13:
                    mnemo = "SYNC";
                    break;
                case 0x16:
                    mnemo = "LBRA";
                    args = AddrModeLongRelative();
                    break;
                case 0x17:
                    mnemo = "LBSR";
                    args = AddrModeLongRelative();
                    break;
                case 0x19:
                    mnemo = "DAA";
                    break;
                case 0x1a:
                    mnemo = "ORCC";
                    args = AddrModeImmediate();
                    break;
                case 0x1c:
                    mnemo = "ANDCC";
                    args = AddrModeImmediate();
                    break;
                case 0x1d:
                    mnemo = "SEX";
                    break;
                case 0x1e:
                    mnemo = "EXG";
                    args = GetRegisterPair();
                    break;
                case 0x1f:
                    mnemo = "TFR";
                    args = GetRegisterPair();
                    break;

                case 0x20:
                    mnemo = "BRA";
                    args = AddrModeRelative();
                    break;
                case 0x21:
                    mnemo = "BRN";
                    args = AddrModeRelative();
                    break;
                case 0x22:
                    mnemo = "BHI";
                    args = AddrModeRelative();
                    break;
                case 0x23:
                    mnemo = "BLS";
                    args = AddrModeRelative();
                    break;
                case 0x24:
                    mnemo = "BCC";   // BHS == BCC
                    args = AddrModeRelative();
                    break;
                case 0x25:
                    mnemo = "BCS";   // BLO == BCS
                    args = AddrModeRelative();
                    break;
                case 0x26:
                    mnemo = "BNE";
                    args = AddrModeRelative();
                    break;
                case 0x27:
                    mnemo = "BEQ";
                    args = AddrModeRelative();
                    break;
                case 0x28:
                    mnemo = "BVC";
                    args = AddrModeRelative();
                    break;
                case 0x29:
                    mnemo = "BVS";
                    args = AddrModeRelative();
                    break;
                case 0x2a:
                    mnemo = "BPL";
                    args = AddrModeRelative();
                    break;
                case 0x2b:
                    mnemo = "BMI";
                    args = AddrModeRelative();
                    break;
                case 0x2c:
                    mnemo = "BGE";
                    args = AddrModeRelative();
                    break;
                case 0x2d:
                    mnemo = "BLT";
                    args = AddrModeRelative();
                    break;
                case 0x2e:
                    mnemo = "BGT";
                    args = AddrModeRelative();
                    break;
                case 0x2f:
                    mnemo = "BLE";
                    args = AddrModeRelative();
                    break;

                case 0x30:
                    mnemo = "LEAX";
                    args = AddrModeIndexed();
                    break;
                case 0x31:
                    mnemo = "LEAY";
                    args = AddrModeIndexed();
                    break;
                case 0x32:
                    mnemo = "LEAS";
                    args = AddrModeIndexed();
                    break;
                case 0x33:
                    mnemo = "LEAU";
                    args = AddrModeIndexed();
                    break;
                case 0x34:
                    mnemo = "PSHS";
                    args = GetRegisterList(false);
                    break;
                case 0x35:
                    mnemo = "PULS";
                    args = GetRegisterList(false);
                    break;
                case 0x36:
                    mnemo = "PSHU";
                    args = GetRegisterList(true);
                    break;
                case 0x37:
                    mnemo = "PULU";
                    args = GetRegisterList(true);
                    break;
                case 0x39:
                    mnemo = "RTS";
                    break;
                case 0x3a:
                    mnemo = "ABX";
                    break;
                case 0x3b:
                    mnemo = "RTI";
                    break;
                case 0x3c:
                    mnemo = "CWAI";
                    args = AddrModeImmediate();
                    break;
                case 0x3d:
                    mnemo = "MUL";
                    break;
                case 0x3f:
                    mnemo = "SWI";
                    break;

                case 0x40:
                    mnemo = "NEG A";
                    break;
                case 0x43:
                    mnemo = "COM A";
                    break;
                case 0x44:
                    mnemo = "LSR A";
                    break;
                case 0x46:
                    mnemo = "ROR A";
                    break;
                case 0x47:
                    mnemo = "ASR A";
                    break;
                case 0x48:   // ASL == LSL
                    mnemo = "LSL A";
                    break;
                case 0x49:
                    mnemo = "ROL A";
                    break;
                case 0x4a:
                    mnemo = "DEC A";
                    break;
                case 0x4c:
                    mnemo = "INC A";
                    break;
                case 0x4d:
                    mnemo = "TST A";
                    break;
                case 0x4f:
                    mnemo = "CLR A";
                    break;

                case 0x50:
                    mnemo = "NEG B";
                    break;
                case 0x53:
                    mnemo = "COM B";
                    break;
                case 0x54:
                    mnemo = "LSR B";
                    break;
                case 0x56:
                    mnemo = "ROR B";
                    break;
                case 0x57:
                    mnemo = "ASR B";
                    break;
                case 0x58:   // ASL == LSL
                    mnemo = "LSL B";
                    break;
                case 0x59:
                    mnemo = "ROL B";
                    break;
                case 0x5a:
                    mnemo = "DEC B";
                    break;
                case 0x5c:
                    mnemo = "INC B";
                    break;
                case 0x5d:
                    mnemo = "TST B";
                    break;
                case 0x5f:
                    mnemo = "CLR B";
                    break;

                case 0x60:
                    mnemo = "NEG";
                    args = AddrModeIndexed();
                    break;
                case 0x63:
                    mnemo = "COM";
                    args = AddrModeIndexed();
                    break;
                case 0x64:
                    mnemo = "LSR";
                    args = AddrModeIndexed();
                    break;
                case 0x66:
                    mnemo = "ROR";
                    args = AddrModeIndexed();
                    break;
                case 0x67:
                    mnemo = "ASR";
                    args = AddrModeIndexed();
                    break;
                case 0x68:
                    mnemo = "LSL";   // ASL == LSL
                    args = AddrModeIndexed();
                    break;
                case 0x69:
                    mnemo = "ROL";
                    args = AddrModeIndexed();
                    break;
                case 0x6a:
                    mnemo = "DEC";
                    args = AddrModeIndexed();
                    break;
                case 0x6c:
                    mnemo = "INC";
                    args = AddrModeIndexed();
                    break;
                case 0x6d:
                    mnemo = "TST";
                    args = AddrModeIndexed();
                    break;
                case 0x6e:
                    mnemo = "JMP";
                    args = AddrModeIndexed();
                    break;
                case 0x6f:
                    mnemo = "CLR";
                    args = AddrModeIndexed();
                    break;

                case 0x70:
                    mnemo = "NEG";
                    args = AddrModeExtended();
                    break;
                case 0x73:
                    mnemo = "COM";
                    args = AddrModeExtended();
                    break;
                case 0x74:
                    mnemo = "LSR";
                    args = AddrModeExtended();
                    break;
                case 0x76:
                    mnemo = "ROR";
                    args = AddrModeExtended();
                    break;
                case 0x77:
                    mnemo = "ASR";
                    args = AddrModeExtended();
                    break;
                case 0x78:
                    mnemo = "LSL";   // ASL == LSL
                    args = AddrModeExtended();
                    break;
                case 0x79:
                    mnemo = "ROL";
                    args = AddrModeExtended();
                    break;
                case 0x7a:
                    mnemo = "DEC";
                    args = AddrModeExtended();
                    break;
                case 0x7c:
                    mnemo = "INC";
                    args = AddrModeExtended();
                    break;
                case 0x7d:
                    mnemo = "TST";
                    args = AddrModeExtended();
                    break;
                case 0x7e:
                    mnemo = "JMP";
                    args = AddrModeExtended();
                    break;
                case 0x7f:
                    mnemo = "CLR";
                    args = AddrModeExtended();
                    break;

                case 0x80:
                    mnemo = "SUBA";
                    args = AddrModeImmediate();
                    break;
                case 0x81:
                    mnemo = "CMPA";
                    args = AddrModeImmediate();
                    break;
                case 0x82:
                    mnemo = "SBCA";
                    args = AddrModeImmediate();
                    break;
                case 0x83:
                    mnemo = "SUBD";
                    args = AddrModeImmediate16bit();
                    break;
                case 0x84:
                    mnemo = "ANDA";
                    args = AddrModeImmediate();
                    break;
                case 0x85:
                    mnemo = "BITA";
                    args = AddrModeImmediate();
                    break;
                case 0x86:
                    mnemo = "LDA";
                    args = AddrModeImmediate();
                    break;
                case 0x88:
                    mnemo = "EORA";
                    args = AddrModeImmediate();
                    break;
                case 0x89:
                    mnemo = "ADCA";
                    args = AddrModeImmediate();
                    break;
                case 0x8a:
                    mnemo = "ORA";
                    args = AddrModeImmediate();
                    break;
                case 0x8b:
                    mnemo = "ADDA";
                    args = AddrModeImmediate();
                    break;
                case 0x8c:
                    mnemo = "CMPX";
                    args = AddrModeImmediate16bit();
                    break;
                case 0x8d:
                    mnemo = "BSR";
                    args = AddrModeRelative();
                    break;
                case 0x8e:
                    mnemo = "LDX";
                    args = AddrModeImmediate16bit();
                    break;

                case 0x90:
                    mnemo = "SUBA";
                    args = AddrModeDirect();
                    break;
                case 0x91:
                    mnemo = "CMPA";
                    args = AddrModeDirect();
                    break;
                case 0x92:
                    mnemo = "SBCA";
                    args = AddrModeDirect();
                    break;
                case 0x93:
                    mnemo = "SUBD";
                    args = AddrModeDirect();
                    break;
                case 0x94:
                    mnemo = "ANDA";
                    args = AddrModeDirect();
                    break;
                case 0x95:
                    mnemo = "BITA";
                    args = AddrModeDirect();
                    break;
                case 0x96:
                    mnemo = "LDA";
                    args = AddrModeDirect();
                    break;
                case 0x97:
                    mnemo = "STA";
                    args = AddrModeDirect();
                    break;
                case 0x98:
                    mnemo = "EORA";
                    args = AddrModeDirect();
                    break;
                case 0x99:
                    mnemo = "ADCA";
                    args = AddrModeDirect();
                    break;
                case 0x9a:
                    mnemo = "ORA";
                    args = AddrModeDirect();
                    break;
                case 0x9b:
                    mnemo = "ADDA";
                    args = AddrModeDirect();
                    break;
                case 0x9c:
                    mnemo = "CMPX";
                    args = AddrModeDirect();
                    break;
                case 0x9d:
                    mnemo = "JSR";
                    args = AddrModeDirect();
                    break;
                case 0x9e:
                    mnemo = "LDX";
                    args = AddrModeDirect();
                    break;
                case 0x9f:
                    mnemo = "STX";
                    args = AddrModeDirect();
                    break;

                case 0xa0:
                    mnemo = "SUBA";
                    args = AddrModeIndexed();
                    break;
                case 0xa1:
                    mnemo = "CMPA";
                    args = AddrModeIndexed();
                    break;
                case 0xa2:
                    mnemo = "SBCA";
                    args = AddrModeIndexed();
                    break;
                case 0xa3:
                    mnemo = "SUBD";
                    args = AddrModeIndexed();
                    break;
                case 0xa4:
                    mnemo = "ANDA";
                    args = AddrModeIndexed();
                    break;
                case 0xa5:
                    mnemo = "BITA";
                    args = AddrModeIndexed();
                    break;
                case 0xa6:
                    mnemo = "LDA";
                    args = AddrModeIndexed();
                    break;
                case 0xa7:
                    mnemo = "STA";
                    args = AddrModeIndexed();
                    break;
                case 0xa8:
                    mnemo = "EORA";
                    args = AddrModeIndexed();
                    break;
                case 0xa9:
                    mnemo = "ADCA";
                    args = AddrModeIndexed();
                    break;
                case 0xaa:
                    mnemo = "ORA";
                    args = AddrModeIndexed();
                    break;
                case 0xab:
                    mnemo = "ADDA";
                    args = AddrModeIndexed();
                    break;
                case 0xac:
                    mnemo = "CMPX";
                    args = AddrModeIndexed();
                    break;
                case 0xad:
                    mnemo = "JSR";
                    args = AddrModeIndexed();
                    break;
                case 0xae:
                    mnemo = "LDX";
                    args = AddrModeIndexed();
                    break;
                case 0xaf:
                    mnemo = "STX";
                    args = AddrModeIndexed();
                    break;

                case 0xb0:
                    mnemo = "SUBA";
                    args = AddrModeExtended();
                    break;
                case 0xb1:
                    mnemo = "CMPA";
                    args = AddrModeExtended();
                    break;
                case 0xb2:
                    mnemo = "SBCA";
                    args = AddrModeExtended();
                    break;
                case 0xb3:
                    mnemo = "SUBD";
                    args = AddrModeExtended();
                    break;
                case 0xb4:
                    mnemo = "ANDA";
                    args = AddrModeExtended();
                    break;
                case 0xb5:
                    mnemo = "BITA";
                    args = AddrModeExtended();
                    break;
                case 0xb6:
                    mnemo = "LDA";
                    args = AddrModeExtended();
                    break;
                case 0xb7:
                    mnemo = "STA";
                    args = AddrModeExtended();
                    break;
                case 0xb8:
                    mnemo = "EORA";
                    args = AddrModeExtended();
                    break;
                case 0xb9:
                    mnemo = "ADCA";
                    args = AddrModeExtended();
                    break;
                case 0xba:
                    mnemo = "ORA";
                    args = AddrModeExtended();
                    break;
                case 0xbb:
                    mnemo = "ADDA";
                    args = AddrModeExtended();
                    break;
                case 0xbc:
                    mnemo = "CMPX";
                    args = AddrModeExtended();
                    break;
                case 0xbd:
                    mnemo = "JSR";
                    args = AddrModeExtended();
                    break;
                case 0xbe:
                    mnemo = "LDX";
                    args = AddrModeExtended();
                    break;
                case 0xbf:
                    mnemo = "STX";
                    args = AddrModeExtended();
                    break;

                case 0xc0:
                    mnemo = "SUBB";
                    args = AddrModeImmediate();
                    break;
                case 0xc1:
                    mnemo = "CMPB";
                    args = AddrModeImmediate();
                    break;
                case 0xc2:
                    mnemo = "SBCB";
                    args = AddrModeImmediate();
                    break;
                case 0xc3:
                    mnemo = "ADDD";
                    args = AddrModeImmediate16bit();
                    break;
                case 0xc4:
                    mnemo = "ANDB";
                    args = AddrModeImmediate();
                    break;
                case 0xc5:
                    mnemo = "BITB";
                    args = AddrModeImmediate();
                    break;
                case 0xc6:
                    mnemo = "LDB";
                    args = AddrModeImmediate();
                    break;
                case 0xc8:
                    mnemo = "EORB";
                    args = AddrModeImmediate();
                    break;
                case 0xc9:
                    mnemo = "ADCB";
                    args = AddrModeImmediate();
                    break;
                case 0xca:
                    mnemo = "ORB";
                    args = AddrModeImmediate();
                    break;
                case 0xcb:
                    mnemo = "ADDB";
                    args = AddrModeImmediate();
                    break;
                case 0xcc:
                    mnemo = "LDD";
                    args = AddrModeImmediate16bit();
                    break;
                case 0xce:
                    mnemo = "LDU";
                    args = AddrModeImmediate16bit();
                    break;

                case 0xd0:
                    mnemo = "SUBB";
                    args = AddrModeDirect();
                    break;
                case 0xd1:
                    mnemo = "CMPB";
                    args = AddrModeDirect();
                    break;
                case 0xd2:
                    mnemo = "SBCB";
                    args = AddrModeDirect();
                    break;
                case 0xd3:
                    mnemo = "ADDD";
                    args = AddrModeDirect();
                    break;
                case 0xd4:
                    mnemo = "ANDB";
                    args = AddrModeDirect();
                    break;
                case 0xd5:
                    mnemo = "BITB";
                    args = AddrModeDirect();
                    break;
                case 0xd6:
                    mnemo = "LDB";
                    args = AddrModeDirect();
                    break;
                case 0xd7:
                    mnemo = "STB";
                    args = AddrModeDirect();
                    break;
                case 0xd8:
                    mnemo = "EORB";
                    args = AddrModeDirect();
                    break;
                case 0xd9:
                    mnemo = "ADCB";
                    args = AddrModeDirect();
                    break;
                case 0xda:
                    mnemo = "ORB";
                    args = AddrModeDirect();
                    break;
                case 0xdb:
                    mnemo = "ADDB";
                    args = AddrModeDirect();
                    break;
                case 0xdc:
                    mnemo = "LDD";
                    args = AddrModeDirect();
                    break;
                case 0xdd:
                    mnemo = "STD";
                    args = AddrModeDirect();
                    break;
                case 0xde:
                    mnemo = "LDU";
                    args = AddrModeDirect();
                    break;
                case 0xdf:
                    mnemo = "STU";
                    args = AddrModeDirect();
                    break;

                case 0xe0:
                    mnemo = "SUBB";
                    args = AddrModeIndexed();
                    break;
                case 0xe1:
                    mnemo = "CMPB";
                    args = AddrModeIndexed();
                    break;
                case 0xe2:
                    mnemo = "SBCB";
                    args = AddrModeIndexed();
                    break;
                case 0xe3:
                    mnemo = "ADDD";
                    args = AddrModeIndexed();
                    break;
                case 0xe4:
                    mnemo = "ANDB";
                    args = AddrModeIndexed();
                    break;
                case 0xe5:
                    mnemo = "BITB";
                    args = AddrModeIndexed();
                    break;
                case 0xe6:
                    mnemo = "LDB";
                    args = AddrModeIndexed();
                    break;
                case 0xe7:
                    mnemo = "STB";
                    args = AddrModeIndexed();
                    break;
                case 0xe8:
                    mnemo = "EORB";
                    args = AddrModeIndexed();
                    break;
                case 0xe9:
                    mnemo = "ADCB";
                    args = AddrModeIndexed();
                    break;
                case 0xea:
                    mnemo = "ORB";
                    args = AddrModeIndexed();
                    break;
                case 0xeb:
                    mnemo = "ADDB";
                    args = AddrModeIndexed();
                    break;
                case 0xec:
                    mnemo = "LDD";
                    args = AddrModeIndexed();
                    break;
                case 0xed:
                    mnemo = "STD";
                    args = AddrModeIndexed();
                    break;
                case 0xee:
                    mnemo = "LDU";
                    args = AddrModeIndexed();
                    break;
                case 0xef:
                    mnemo = "STU";
                    args = AddrModeIndexed();
                    break;

                case 0xf0:
                    mnemo = "SUBB";
                    args = AddrModeExtended();
                    break;
                case 0xf1:
                    mnemo = "CMPB";
                    args = AddrModeExtended();
                    break;
                case 0xf2:
                    mnemo = "SBCB";
                    args = AddrModeExtended();
                    break;
                case 0xf3:
                    mnemo = "ADDD";
                    args = AddrModeExtended();
                    break;
                case 0xf4:
                    mnemo = "ANDB";
                    args = AddrModeExtended();
                    break;
                case 0xf5:
                    mnemo = "BITB";
                    args = AddrModeExtended();
                    break;
                case 0xf6:
                    mnemo = "LDB";
                    args = AddrModeExtended();
                    break;
                case 0xf7:
                    mnemo = "STB";
                    args = AddrModeExtended();
                    break;
                case 0xf8:
                    mnemo = "EORB";
                    args = AddrModeExtended();
                    break;
                case 0xf9:
                    mnemo = "ADCB";
                    args = AddrModeExtended();
                    break;
                case 0xfa:
                    mnemo = "ORB";
                    args = AddrModeExtended();
                    break;
                case 0xfb:
                    mnemo = "ADDB";
                    args = AddrModeExtended();
                    break;
                case 0xfc:
                    mnemo = "LDD";
                    args = AddrModeExtended();
                    break;
                case 0xfd:
                    mnemo = "STD";
                    args = AddrModeExtended();
                    break;
                case 0xfe:
                    mnemo = "LDU";
                    args = AddrModeExtended();
                    break;
                case 0xff:
                    mnemo = "STU";
                    args = AddrModeExtended();
                    break;
            }

            if (mnemo != null) {
                return String.Format("{0} {1}", mnemo, args).Trim();
            }
            return null;
        }

        /* "Page 2" : opcodes sur deux octets, 0x10nn  */
        private string DisasmOpcodeP2(byte opcode)
        {
            string mnemo = null, args = String.Empty;
            switch (opcode) {
                case 0x21:
                    mnemo = "LBRN";
                    args = AddrModeLongRelative();
                    break;
                case 0x22:
                    mnemo = "LBHI";
                    args = AddrModeLongRelative();
                    break;
                case 0x23:
                    mnemo = "LBLS";
                    args = AddrModeLongRelative();
                    break;
                case 0x24:
                    mnemo = "LBCC";   // LBHS == LBCC
                    args = AddrModeLongRelative();
                    break;
                case 0x25:
                    mnemo = "LBCS";   // LBLO == LBCS
                    args = AddrModeLongRelative();
                    break;
                case 0x26:
                    mnemo = "LBNE";
                    args = AddrModeLongRelative();
                    break;
                case 0x27:
                    mnemo = "LBEQ";
                    args = AddrModeLongRelative();
                    break;
                case 0x28:
                    mnemo = "LBVC";
                    args = AddrModeLongRelative();
                    break;
                case 0x29:
                    mnemo = "LBVS";
                    args = AddrModeLongRelative();
                    break;
                case 0x2a:
                    mnemo = "LBPL";
                    args = AddrModeLongRelative();
                    break;
                case 0x2b:
                    mnemo = "LBMI";
                    args = AddrModeLongRelative();
                    break;
                case 0x2c:
                    mnemo = "LBGE";
                    args = AddrModeLongRelative();
                    break;
                case 0x2d:
                    mnemo = "LBLT";
                    args = AddrModeLongRelative();
                    break;
                case 0x2e:
                    mnemo = "LBGT";
                    args = AddrModeLongRelative();
                    break;
                case 0x2f:
                    mnemo = "LBLE";
                    args = AddrModeLongRelative();
                    break;

                case 0x3f:
                    mnemo = "SWI2";
                    break;

                case 0x83:
                    mnemo = "CMPD";
                    args = AddrModeImmediate16bit();
                    break;
                case 0x8c:
                    mnemo = "CMPY";
                    args = AddrModeImmediate16bit();
                    break;
                case 0x8e:
                    mnemo = "LDY";
                    args = AddrModeImmediate16bit();
                    break;

                case 0x93:
                    mnemo = "CMPD";
                    args = AddrModeDirect();
                    break;
                case 0x9c:
                    mnemo = "CMPY";
                    args = AddrModeDirect();
                    break;
                case 0x9e:
                    mnemo = "LDY";
                    args = AddrModeDirect();
                    break;
                case 0x9f:
                    mnemo = "STY";
                    args = AddrModeDirect();
                    break;

                case 0xa3:
                    mnemo = "CMPD";
                    args = AddrModeIndexed();
                    break;
                case 0xac:
                    mnemo = "CMPY";
                    args = AddrModeIndexed();
                    break;
                case 0xae:
                    mnemo = "LDY";
                    args = AddrModeIndexed();
                    break;
                case 0xaf:
                    mnemo = "STY";
                    args = AddrModeIndexed();
                    break;

                case 0xb3:
                    mnemo = "CMPD";
                    args = AddrModeExtended();
                    break;
                case 0xbc:
                    mnemo = "CMPY";
                    args = AddrModeExtended();
                    break;
                case 0xbe:
                    mnemo = "LDY";
                    args = AddrModeExtended();
                    break;
                case 0xbf:
                    mnemo = "STY";
                    args = AddrModeExtended();
                    break;

                case 0xce:
                    mnemo = "LDS";
                    args = AddrModeImmediate16bit();
                    break;

                case 0xde:
                    mnemo = "LDS";
                    args = AddrModeDirect();
                    break;
                case 0xdf:
                    mnemo = "STS";
                    args = AddrModeDirect();
                    break;

                case 0xee:
                    mnemo = "LDS";
                    args = AddrModeIndexed();
                    break;
                case 0xef:
                    mnemo = "STS";
                    args = AddrModeIndexed();
                    break;

                case 0xfe:
                    mnemo = "LDS";
                    args = AddrModeExtended();
                    break;
                case 0xff:
                    mnemo = "STS";
                    args = AddrModeExtended();
                    break;
            }

            if (mnemo != null) {
                return String.Format("{0} {1}", mnemo, args).Trim();
            }
            return null;
        }

        /* "Page 3" : opcodes sur deux octets, 0x11nn  */
        private string DisasmOpcodeP3(byte opcode)
        {
            string mnemo = null, args = String.Empty;
            switch (opcode) {
                case 0x3f:
                    mnemo = "SWI3";
                    break;

                case 0x83:
                    mnemo = "CMPU";
                    args = AddrModeImmediate16bit();
                    break;
                case 0x8c:
                    mnemo = "CMPS";
                    args = AddrModeImmediate16bit();
                    break;

                case 0x93:
                    mnemo = "CMPU";
                    args = AddrModeDirect();
                    break;
                case 0x9c:
                    mnemo = "CMPS";
                    args = AddrModeDirect();
                    break;

                case 0xa3:
                    mnemo = "CMPU";
                    args = AddrModeIndexed();
                    break;
                case 0xac:
                    mnemo = "CMPS";
                    args = AddrModeIndexed();
                    break;

                case 0xb3:
                    mnemo = "CMPU";
                    args = AddrModeExtended();
                    break;
                case 0xbc:
                    mnemo = "CMPS";
                    args = AddrModeExtended();
                    break;
            }

            if (mnemo != null) {
                return String.Format("{0} {1}", mnemo, args).Trim();
            }
            return null;
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Désassemble une instruction en mémoire.
        /// </summary>
        /// <param name="memoryAddress">
        /// Adresse où débute l'instruction à désassembler.
        /// </param>
        /// <returns></returns>
        /// <exception cref="AddressUnreadableException">
        /// Si l'une des adresses-mémoire à traiter est impossible à lire.
        /// </exception>
        public String DisassembleInstructionAt(ushort memoryAddress)
        {
            StringBuilder sbResult = new StringBuilder();
            this.regPC = memoryAddress;

            /* écrit d'abord l'adresse traitée */
            sbResult.Append(String.Format("{0:X4} : ", this.regPC));

            /* analyse l'opcode trouvé à cette adresse */
            string instr;
            bool opcodeDouble = false;
            byte opcode = ReadMem(this.regPC);
            this.regPC++;
            switch (opcode) {
                case 0x10:
                    // opcodes de la "page 2"
                    opcode = ReadMem(this.regPC);
                    this.regPC++;
                    instr = DisasmOpcodeP2(opcode);
                    opcodeDouble = true;
                    break;
                case 0x11:
                    // opcodes de la "page 3"
                    opcode = ReadMem(this.regPC);
                    this.regPC++;
                    instr = DisasmOpcodeP3(opcode);
                    opcodeDouble = true;
                    break;
                default:
                    // opcodes de la "page 1" (par défaut)
                    instr = DisasmOpcodeP1(opcode);
                    break;
            }

            /* opcode invalide ! */
            if (instr == null) {
                switch (this.uoPolicy) {
                    case UnknownOpcodePolicy.DoNop:
                        instr = "?!?";
                        break;
                    case UnknownOpcodePolicy.ThrowException:
                    default:
                        throw new UnknownOpcodeException(
                                this.regPC, opcode,
                                String.Format(ERR_UNKNOWN_OPCODE,
                                              this.regPC, opcode));
                }
            }

            /* liste les octets ainsi traités */
            int nbOct = this.regPC - memoryAddress;
            for (int n = 0; n < nbOct; n++) {
                ushort ad = (ushort)(memoryAddress + n);
                byte b = ReadMem(ad);
                sbResult.Append(String.Format("{0:X2} ", b));
                if ((n == 0) && !opcodeDouble) {
                    sbResult.Append("   ");
                }
            }
            /* aligne le résultat sur 24 colonnes */
            while (sbResult.Length < 24) sbResult.Append(" ");
            sbResult.Append(": ");

            /* enfin, liste l'instruction désassemblée */
            sbResult.Append(instr);

            /* terminé */
            sbResult.Append(" \r\n");
            return sbResult.ToString();
        }

        /// <summary>
        /// Désassemble un nombre donné d'instructions en mémoire.
        /// </summary>
        /// <param name="fromAddress">
        /// Adresse mémoire de la première instruction à désassembler.
        /// </param>
        /// <param name="nbInstr">
        /// Nombre d'instructions consécutives à desassembler.
        /// </param>
        /// <returns>
        /// Chaîne de caractère contenant le désassemblage des instructions
        /// rencontrées à partir de <code>fromAddress</code>.
        /// </returns>
        /// <exception cref="AddressUnreadableException">
        /// Si l'une des adresses-mémoire à traiter est impossible à lire.
        /// </exception>
        public String DisassembleManyInstructionsAt(ushort fromAddress,
                                                    uint nbInstr)
        {
            StringBuilder sbResult = new StringBuilder();
            this.regPC = fromAddress;
            for (uint n = 0; n < nbInstr; n++) {
                string instr = DisassembleInstructionAt(
                        (ushort)(this.regPC));
                sbResult.Append(instr);
            }
            return sbResult.ToString();
        }

        /// <summary>
        /// Désassemble le contenu d'une plage d'adresses en mémoire.
        /// </summary>
        /// <param name="fromAddress">
        /// Adresse mémoire de la première instruction à désassembler.
        /// </param>
        /// <param name="toAddress">
        /// Dernière adresse mémoire à desassembler.
        /// </param>
        /// <returns>
        /// Chaîne de caractère contenant le désassemblage des adresses
        /// de la plage mémoire indiquée.
        /// <br/>
        /// Notez que le désassemblage peut aller légèrement au-delà de
        /// <code>toAddress</code> si une instruction s'étend sur cette
        /// adresse de fin.
        /// </returns>
        /// <exception cref="AddressUnreadableException">
        /// Si l'une des adresses-mémoire à traiter est impossible à lire.
        /// </exception>
        public String DisassembleMemory(ushort fromAddress,
                                        ushort toAddress)
        {
            StringBuilder sbResult = new StringBuilder();
            this.regPC = fromAddress;
            while (this.regPC <= toAddress) {
                string instr = DisassembleInstructionAt(
                        (ushort)(this.regPC));
                sbResult.Append(instr);
            }
            return sbResult.ToString();
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
        /// Politique de prise en charge des opcodes invalides
        /// au désassemblage.
        /// </summary>
        public UnknownOpcodePolicy InvalidOpcodePolicy
        {
            get { return this.uoPolicy; }
            set { this.uoPolicy = value; }
        }

    }
}

