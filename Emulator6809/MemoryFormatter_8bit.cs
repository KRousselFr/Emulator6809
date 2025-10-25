using System;
using System.Text;


namespace Emulator6809
{
    /// <summary>
    /// Classe formatant le contenu de la mémoire
    /// sous une forme texte claire, octet par octet.
    /// </summary>
    public class MemoryFormatter_8bit
    {
        /* =========================== CONSTANTES =========================== */

        // messages affichés
        private const String ERR_UNREADABLE_ADDRESS =
                "Impossible de lire le contenu de l'adresse ${0:X4} !";

        // valeurs numériques
        private const int LINE_SIZE_IN_BYTES = 16;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // espace-mémoire attaché au processeur
        // (défini une fois pour toutes à la construction)
        private readonly IMemorySpace6809 memSpace;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur de référence (et unique) de la classe
        /// MemoryFormatter_8bit.
        /// </summary>
        /// <param name="memorySpace">
        /// Espace-mémoire où lire les valeurs à présenter.
        /// </param>
        public MemoryFormatter_8bit(IMemorySpace6809 memorySpace)
        {
            this.memSpace = memorySpace;
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        /* ~~~~ affichage d'un octet sous forme de caractère ~~~~ */

        private static char ByteToChar(byte b)
        {
            switch (b) {
                /* ~~ codes ASCII ~~ */
                case 0x20: return ' ';
                case 0x21: return '!';
                case 0x22: return '"';
                case 0x23: return '#';
                case 0x24: return '$';
                case 0x25: return '%';
                case 0x26: return '&';
                case 0x27: return '\'';
                case 0x28: return '(';
                case 0x29: return ')';
                case 0x2a: return '*';
                case 0x2b: return '+';
                case 0x2c: return ',';
                case 0x2d: return '-';
                case 0x2e: return '.';
                case 0x2f: return '/';

                case 0x30: return '0';
                case 0x31: return '1';
                case 0x32: return '2';
                case 0x33: return '3';
                case 0x34: return '4';
                case 0x35: return '5';
                case 0x36: return '6';
                case 0x37: return '7';
                case 0x38: return '8';
                case 0x39: return '9';
                case 0x3a: return ':';
                case 0x3b: return ';';
                case 0x3c: return '<';
                case 0x3d: return '=';
                case 0x3e: return '>';
                case 0x3f: return '?';

                case 0x40: return '@';
                case 0x41: return 'A';
                case 0x42: return 'B';
                case 0x43: return 'C';
                case 0x44: return 'D';
                case 0x45: return 'E';
                case 0x46: return 'F';
                case 0x47: return 'G';
                case 0x48: return 'H';
                case 0x49: return 'I';
                case 0x4a: return 'J';
                case 0x4b: return 'K';
                case 0x4c: return 'L';
                case 0x4d: return 'M';
                case 0x4e: return 'N';
                case 0x4f: return 'O';

                case 0x50: return 'P';
                case 0x51: return 'Q';
                case 0x52: return 'R';
                case 0x53: return 'S';
                case 0x54: return 'T';
                case 0x55: return 'U';
                case 0x56: return 'V';
                case 0x57: return 'W';
                case 0x58: return 'X';
                case 0x59: return 'Y';
                case 0x5a: return 'Z';
                case 0x5b: return '[';
                case 0x5c: return '\\';
                case 0x5d: return ']';
                case 0x5e: return '^';
                case 0x5f: return '_';

                case 0x60: return '`';
                case 0x61: return 'a';
                case 0x62: return 'b';
                case 0x63: return 'c';
                case 0x64: return 'd';
                case 0x65: return 'e';
                case 0x66: return 'f';
                case 0x67: return 'g';
                case 0x68: return 'h';
                case 0x69: return 'i';
                case 0x6a: return 'j';
                case 0x6b: return 'k';
                case 0x6c: return 'l';
                case 0x6d: return 'm';
                case 0x6e: return 'n';
                case 0x6f: return 'o';

                case 0x70: return 'p';
                case 0x71: return 'q';
                case 0x72: return 'r';
                case 0x73: return 's';
                case 0x74: return 't';
                case 0x75: return 'u';
                case 0x76: return 'v';
                case 0x77: return 'w';
                case 0x78: return 'x';
                case 0x79: return 'y';
                case 0x7a: return 'z';
                case 0x7b: return '{';
                case 0x7c: return '|';
                case 0x7d: return '}';
                case 0x7e: return '~';

                /* ~~ codes CEI_8859-15 ~~ */

                case 0xa0: return ' ';
                case 0xa1: return '¡';
                case 0xa2: return '¢';
                case 0xa3: return '£';
                case 0xa4: return '€';
                case 0xa5: return '¥';
                case 0xa6: return 'Š';
                case 0xa7: return '§';
                case 0xa8: return 'š';
                case 0xa9: return '©';
                case 0xaa: return 'ª';
                case 0xab: return '«';
                case 0xac: return '¬';
                case 0xad: return '-';
                case 0xae: return '®';
                case 0xaf: return '¯';

                case 0xb0: return '°';
                case 0xb1: return '±';
                case 0xb2: return '²';
                case 0xb3: return '³';
                case 0xb4: return 'Ž';
                case 0xb5: return 'µ';
                case 0xb6: return '¶';
                case 0xb7: return '·';
                case 0xb8: return 'ž';
                case 0xb9: return '¹';
                case 0xba: return 'º';
                case 0xbb: return '»';
                case 0xbc: return 'Œ';
                case 0xbd: return 'œ';
                case 0xbe: return 'Ÿ';
                case 0xbf: return '¿';

                case 0xc0: return 'À';
                case 0xc1: return 'Á';
                case 0xc2: return 'Â';
                case 0xc3: return 'Ã';
                case 0xc4: return 'Ä';
                case 0xc5: return 'Å';
                case 0xc6: return 'Æ';
                case 0xc7: return 'Ç';
                case 0xc8: return 'È';
                case 0xc9: return 'É';
                case 0xca: return 'Ê';
                case 0xcb: return 'Ë';
                case 0xcc: return 'Ì';
                case 0xcd: return 'Í';
                case 0xce: return 'Î';
                case 0xcf: return 'Ï';

                case 0xd0: return 'Ð';
                case 0xd1: return 'Ñ';
                case 0xd2: return 'Ò';
                case 0xd3: return 'Ó';
                case 0xd4: return 'Ô';
                case 0xd5: return 'Õ';
                case 0xd6: return 'Ö';
                case 0xd7: return '×';
                case 0xd8: return 'Ø';
                case 0xd9: return 'Ù';
                case 0xda: return 'Ú';
                case 0xdb: return 'Û';
                case 0xdc: return 'Ü';
                case 0xdd: return 'Ý';
                case 0xde: return 'Þ';
                case 0xdf: return 'ß';

                case 0xe0: return 'à';
                case 0xe1: return 'á';
                case 0xe2: return 'â';
                case 0xe3: return 'ã';
                case 0xe4: return 'ä';
                case 0xe5: return 'å';
                case 0xe6: return 'æ';
                case 0xe7: return 'ç';
                case 0xe8: return 'è';
                case 0xe9: return 'é';
                case 0xea: return 'ê';
                case 0xeb: return 'ë';
                case 0xec: return 'ì';
                case 0xed: return 'í';
                case 0xee: return 'î';
                case 0xef: return 'ï';

                case 0xf0: return 'ð';
                case 0xf1: return 'ñ';
                case 0xf2: return 'ò';
                case 0xf3: return 'ó';
                case 0xf4: return 'ô';
                case 0xf5: return 'õ';
                case 0xf6: return 'ö';
                case 0xf7: return '÷';
                case 0xf8: return 'ø';
                case 0xf9: return 'ù';
                case 0xfa: return 'ú';
                case 0xfb: return 'û';
                case 0xfc: return 'ü';
                case 0xfd: return 'ý';
                case 0xfe: return 'þ';
                case 0xff: return 'ÿ';

                /* ~~ caractère non imprimable ~~ */

                default:
                    return '.';
            }
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

        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Liste et présente les valeurs dans la mémoire du proceseur.
        /// </summary>
        /// <param name="fromAddr">
        /// Adresse de départ à traiter en mémoire.
        /// </param>
        /// <param name="toAddr">
        /// Adresse de fin à traiter en mémoire.
        /// </param>
        /// <returns>
        /// Les valeurs de la plage mémoire indiquée sous forme texte,
        /// formatées de façon adéquate.
        /// </returns>
        /// <exception cref="AddressUnreadableException">
        /// Si l'une des adresses-mémoire à traiter est impossible à lire.
        /// </exception>
        public String ListMemoryValues(ushort fromAddr, ushort toAddr)
        {
            StringBuilder sbResult = new StringBuilder();

            /* aligne la valeur de départ sur la taille de ligne à afficher */
            fromAddr -= (ushort)(fromAddr % LINE_SIZE_IN_BYTES);

            /* affiche le contenu des adresses par ordre croissant */
            for (int currAddr = fromAddr;
                 currAddr <= toAddr;
                 currAddr += LINE_SIZE_IN_BYTES)
            {
                /* adresse de départ de la ligne */
                sbResult.Append(String.Format("{0:X4} : ", currAddr));

                /* lit le nombre de valeurs voulues */
                byte[] vals = new byte[LINE_SIZE_IN_BYTES];
                for (int n = 0; n < LINE_SIZE_IN_BYTES; n++) {
                    vals[n] = ReadMem(currAddr + n);
                }

                /* affiche les valeurs sous forme hexadécimale */
                for (int n = 0; n < LINE_SIZE_IN_BYTES; n++) {
                    sbResult.Append(String.Format("{0:X2} ", vals[n]));
                }
                sbResult.Append("  ");

                /* affiche les mêmes valeurs sous forme de caractères */
                for (int n = 0; n < LINE_SIZE_IN_BYTES; n++) {
                    sbResult.Append(ByteToChar(vals[n]));
                }
                sbResult.Append("\r\n");
            }

            /* terminé */
            return sbResult.ToString();
        }


        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Objet espace-mémoire attaché au processeur lors de sa création.
        /// (Propriété en lecture seule.)
        /// </summary>
        public IMemorySpace6809 MemorySpace
        {
            get { return this.memSpace; }
        }

    }
}

