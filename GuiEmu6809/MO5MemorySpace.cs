using System;
using System.IO;

using Emulator6809;


namespace GuiEmu6809
{
    /// <summary>
    /// Classe implantant l'espace-mémoire (virtuel) d'un ordinateur
    /// Thomson MO5.
    /// </summary>
    class MO5MemorySpace : IMemorySpace6809
    {
        /* =========================== CONSTANTES =========================== */

        /* ~~~~ messages d'erreur ~~~~ */
        private const string ERR_SHORT_FILE_LOAD =
                "Seuls {1} octets ont été lus du fichier '{2}'" +
                " (au lieu de {0}) !\r\n" +
                "Le fichier est-il trop court ?";
        private const string WARN_UNUSED_IO_ADDRESS_READ =
                "[A] : accès à une adresse E / S non attribuée (${0:X4}) !";
        private const string WARN_UNUSED_IO_ADDRESS_WRITE =
                "[A] : écriture à une adresse E / S non attribuée" +
                " ($${1:X2} à ${0:X4}) !";
        private const string WARN_UNUSED_MEM_ADDRESS_READ =
                "[A] : accès à une adresse mémoire non attribuée (${0:X4}) !";
        private const string WARN_UNUSED_MEM_ADDRESS_WRITE =
                "[A] : écriture à une adresse mémoire non attribuée" +
                " ($${1:X2} à ${0:X4}) !";

        /* ~~~~ valeurs numériques ~~~~ */

        // nombre d'octets réservés pour la RAM dans l'espace-mémoire
        public const int RAM_ADDRESS_SPACE_SIZE = 40960;

        // nombre d'octets réservés pour la RAM vidéo dans l'espace-mémoire
        public const int VIDEO_ADDRESS_SPACE_SIZE = 8192;

        // tout ce qui est en-dessous de cette adresse est de la RAM ;
        // à partir de cette adresse : c'est de la ROM ou des E / S
        public const ushort GLOBAL_ROM_START = 0xa000;

        // nombre d'octets réservés pour la ROM dans l'espace-mémoire
        public const int TOTAL_ROM_ADDRESS_SPACE = 24576;

        // adresses réservées aux périphériques (E / S)
        public const ushort IO_START_ADDRESS = 0xa7c0;
        public const ushort IO_END_ADDRESS = 0xa7ff;

        // adresse de chargement des cartouches (Mémo5)
        public const ushort MEMO5_ROM_START = 0xb000;
        public const int MEMO5_ROM_SIZE = 16384;

        // caractéristiques de la ROM intégrée au MO5
        public const int BUILT_IN_ROM_SIZE = 16384;
        public const ushort BUILT_IN_ROM_START = 0xc000;
        public const ushort MONITOR_FIRMWARE_START = 0xf000;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // périphériques d'entrée-sorties
        private readonly PIA6820 systemPIA;
        private readonly PIA6820 musicGamesPIA;
        private readonly PIA6820 parallelPIA;
        // tableau représentant la ROM intégrée du MO5
        private readonly byte[] builtInROM;
        // cartouche Mémo5, le cas échéant
        private bool memo5Present;
        private byte[] memo5ROM;
        private int memo5Size;
        // tableaux représentant la RAM
        private byte[] pointsVRAM;
        private byte[] colorsVRAM;
        private byte[] userRAM;

        // (éventuel) fichier de sortie pour le déboguage
        private StreamWriter debugFile;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur de référence (et unique).
        /// </summary>
        /// <param name="romImageFilePath">
        /// Chemin vers le fichier contenant l'image de la ROM du MO5.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Si <code>romImageFilePath</code> est <code>null</code>.
        /// </exception>
        /// <exception cref="FileLoadException">
        /// Si le fichier <code>romImageFilePath</code> ne peut être lu,
        /// ou est trop court (il doit avoir une taille de 16 Kio).
        /// </exception>
        public MO5MemorySpace(String romImageFilePath)
        {
            /* création des objets périphériques */
            this.systemPIA = new PIA6820();
            this.musicGamesPIA = new PIA6820();
            this.parallelPIA = new PIA6820();
            /* initialise la RAM */
            ClearRAM();
            /* lit le contenu de la ROM intégrée du MO5 */
            if (romImageFilePath == null) {
                throw new ArgumentNullException("romImageFilePath");
            }
            using (FileStream romImg = File.OpenRead(romImageFilePath)) {
                this.builtInROM = new byte[BUILT_IN_ROM_SIZE];
                int lu = romImg.Read(this.builtInROM, 0, BUILT_IN_ROM_SIZE);
                if (lu < BUILT_IN_ROM_SIZE) {
                    throw new FileLoadException(String.Format(
                            ERR_SHORT_FILE_LOAD,
                            BUILT_IN_ROM_SIZE, lu,
                            romImageFilePath));
                }
            }
            /* pas de cartouche Mémo5 présente par défaut */
            EjectMemo5();
            /* aucun fichier de déboguage par défaut */
            this.debugFile = null;
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        private byte? Input(ushort address)
        {
            switch (address) {
                /* PIA système */
                case 0xa7c0:
                    return this.systemPIA.BaseRegisterA;
                case 0xa7c1:
                    return this.systemPIA.BaseRegisterB;
                case 0xa7c2:
                    return this.systemPIA.ControlRegisterA;
                case 0xa7c3:
                    return this.systemPIA.ControlRegisterB;

                /* PIA musique et jeux */
                case 0xa7cc:
                    return this.musicGamesPIA.BaseRegisterA;
                case 0xa7cd:
                    return this.musicGamesPIA.BaseRegisterB;
                case 0xa7ce:
                    return this.musicGamesPIA.ControlRegisterA;
                case 0xa7cf:
                    return this.musicGamesPIA.ControlRegisterB;

                /* contrôleur de disquettes */
                case 0xa7d0:
                case 0xa7d1:
                case 0xa7d2:
                case 0xa7d3:
                case 0xa7d4:
                case 0xa7d5:
                case 0xa7d6:
                case 0xa7d7:
                case 0xa7d8:
                case 0xa7d9:
                case 0xa7da:
                case 0xa7db:
                case 0xa7dc:
                case 0xa7dd:
                case 0xa7de:
                case 0xa7df:
                    // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                    break;
                
                /* PIA interface parallèle */
                case 0xa7e0:
                    return this.parallelPIA.BaseRegisterA;
                case 0xa7e1:
                    return this.parallelPIA.BaseRegisterB;
                case 0xa7e2:
                    return this.parallelPIA.ControlRegisterA;
                case 0xa7e3:
                    return this.parallelPIA.ControlRegisterB;

                /* "gate array" du MO5 */
                case 0xa7e4:
                case 0xa7e5:
                case 0xa7e6:
                case 0xa7e7:
                    // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                    break;
            }
            /* si on arrive ici, on cherche à lire
               une adresse E / S non attribuée */
            if (this.debugFile != null) {
                this.debugFile.WriteLine(WARN_UNUSED_IO_ADDRESS_READ,
                                         address);
            }
            return 0x00;
        }

        private void Output(ushort address, byte value)
        {
            switch (address) {
                /* PIA système */
                case 0xa7c0:
                    this.systemPIA.BaseRegisterA = value;
                    return;
                case 0xa7c1:
                    this.systemPIA.BaseRegisterB = value;
                    return;
                case 0xa7c2:
                    this.systemPIA.ControlRegisterA = value;
                    return;
                case 0xa7c3:
                    this.systemPIA.ControlRegisterB = value;
                    return;

                /* PIA musique et jeux */
                case 0xa7cc:
                    this.musicGamesPIA.BaseRegisterA = value;
                    return;
                case 0xa7cd:
                    this.musicGamesPIA.BaseRegisterB = value;
                    return;
                case 0xa7ce:
                    this.musicGamesPIA.ControlRegisterA = value;
                    return;
                case 0xa7cf:
                    this.musicGamesPIA.ControlRegisterB = value;
                    return;

                /* contrôleur de disquettes */
                case 0xa7d0:
                case 0xa7d1:
                case 0xa7d2:
                case 0xa7d3:
                case 0xa7d4:
                case 0xa7d5:
                case 0xa7d6:
                case 0xa7d7:
                case 0xa7d8:
                case 0xa7d9:
                case 0xa7da:
                case 0xa7db:
                case 0xa7dc:
                case 0xa7dd:
                case 0xa7de:
                case 0xa7df:
                    // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                    return;

                /* PIA interface parallèle */
                case 0xa7e0:
                    this.parallelPIA.BaseRegisterA = value;
                    return;
                case 0xa7e1:
                    this.parallelPIA.BaseRegisterB = value;
                    return;
                case 0xa7e2:
                    this.parallelPIA.ControlRegisterA = value;
                    return;
                case 0xa7e3:
                    this.parallelPIA.ControlRegisterB = value;
                    return;

                /* "gate array" du MO5 */
                case 0xa7e4:
                case 0xa7e5:
                case 0xa7e6:
                case 0xa7e7:
                    // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                    return;
            }
            /* si on arrive ici, on cherche à lire
               une adresse E / S non attribuée */
            if (this.debugFile != null) {
                this.debugFile.WriteLine(WARN_UNUSED_IO_ADDRESS_WRITE,
                                         address, value);
            }
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Remet tout le contenu de la RAM à zéro.
        /// </summary>
        public void ClearRAM()
        {
            this.pointsVRAM = new byte[VIDEO_ADDRESS_SPACE_SIZE];
            this.colorsVRAM = new byte[VIDEO_ADDRESS_SPACE_SIZE];
            this.userRAM = new byte[RAM_ADDRESS_SPACE_SIZE
                                  - VIDEO_ADDRESS_SPACE_SIZE];
            GC.Collect();
        }

        /// <summary>
        /// Insère la cartouche Mémo5 indiquée dans l'espace-mémoire.
        /// </summary>
        /// <param name="imageFilePath">
        /// Chemin vers le fichier image de la cartouche Mémo5 voulue.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Si <code>imageFilePath</code> est <code>null</code>.
        /// </exception>
        /// <exception cref="FileLoadException">
        /// Si le fichier <code>imageFilePath</code> ne peut être lu,
        /// ou est trop court (il doit avoir une taille de 16 Kio).
        /// </exception>
        public void LoadMemo5(string imageFilePath)
        {
            if (imageFilePath == null) {
                throw new ArgumentNullException("romImageFilePath");
            }
            /* tente de lire le contenu de la Mémo5 indiquée */
            try {
                using (FileStream romImg = File.OpenRead(imageFilePath)) {
                    this.memo5ROM = new byte[this.memo5Size];
                    int lu = romImg.Read(this.memo5ROM, 0, MEMO5_ROM_SIZE);
                    if (lu < this.memo5Size) {
                        throw new FileLoadException(String.Format(
                                ERR_SHORT_FILE_LOAD,
                                MEMO5_ROM_SIZE, lu,
                                imageFilePath));
                    }
                }
                this.memo5Size = MEMO5_ROM_SIZE;
                this.memo5Present = true;
            } catch {
                EjectMemo5();
                throw;
            }
        }

        /// <summary>
        /// Ejecte toute cartouche Mémo5 éventuellement présente.
        /// (Si aucune Mémo5 n'est présente, cette méthode ne fait rien.)
        /// </summary>
        public void EjectMemo5()
        {
            this.memo5Present = false;
            this.memo5ROM = null;
            this.memo5Size = 0;
            GC.Collect();
        }

        /// <summary>
        /// Lecture d'un octet en mémoire
        /// (méthode héritée de <code>IMemorySpace6809</code>).
        /// </summary>
        /// <param name="address">
        /// Adresse mémoire de l'octet à lire.
        /// </param>
        /// <returns>
        /// Valeur de l'octet situé en <code>address</code>,
        /// ou <code>null</code> en cas d'erreur.
        /// </returns>
        public byte? ReadMemory(ushort address)
        {
            /* lecture de la RAM */
            if (address < VIDEO_ADDRESS_SPACE_SIZE) {
                // la RAM vidéo se situe pile au début de l'espace-mémoire
                if ((Input(0xa7c0) & 0x01) != 0) {
                    return this.pointsVRAM[address];
                } else {
                    return this.colorsVRAM[address];
                }
            }
            if (address < GLOBAL_ROM_START) {
                // la RAM utilisateur se situe juste après la vidéo
                return this.userRAM[address - VIDEO_ADDRESS_SPACE_SIZE];
            }
            /* lecture de la ROM */
                if (address >= MONITOR_FIRMWARE_START) {
                // le moniteur se situe à la toute fin de l'espace-mémoire
                // et est toujours accessible
                return this.builtInROM[address - BUILT_IN_ROM_START];
            }
            if (this.memo5Present) {
                if (address >= MEMO5_ROM_START) {
                    // lecture de la Mémo5 présente
                    return this.memo5ROM[address - MEMO5_ROM_START];
                }
            } else {
                if (address >= BUILT_IN_ROM_START) {
                    // la ROM intégrée se situe à la fin de l'espace-mémoire
                    return this.builtInROM[address - BUILT_IN_ROM_START];
                }
            }
            /* lecture des périphériques (entrée) */
            if ( (address >= IO_START_ADDRESS) &&
                 (address <= IO_END_ADDRESS) )
            {
                return Input(address);
            }
            /* si on arrive ici, on tente de lire une adresse "vide" */
            if (this.debugFile != null) {
                this.debugFile.WriteLine(WARN_UNUSED_MEM_ADDRESS_READ,
                                         address);
            }
            return 0x00;
        }

        /// <summary>
        /// Ecrit la valeur d'un octet en mémoire
        /// (méthode héritée de <code>IMemorySpace6809</code>).
        /// </summary>
        /// <param name="address">
        /// Adresse de l'octet à écrire.
        /// </param>
        /// <param name="value">
        /// Nouvelle valeur à donner à l'octet indiqué.
        /// </param>
        /// <returns>
        /// <code>true</code> si l'écriture a réussi ;
        /// ou <code>false</code> en cas d'erreur.
        /// </returns>
        public bool WriteMemory(ushort address, byte value)
        {
            /* écriture en mémoire vidéo */
            if (address < VIDEO_ADDRESS_SPACE_SIZE) {
                // la RAM vidéo se situe pile au début de l'espace-mémoire
                if ((Input(0xa7c0) & 0x01) != 0) {
                    this.pointsVRAM[address] = value;
                } else {
                    this.colorsVRAM[address] = value;
                }
                return true;
            }
            /* écriture normale en RAM "générale" */
            if (address < GLOBAL_ROM_START) {
                this.userRAM[address - VIDEO_ADDRESS_SPACE_SIZE] = value;
                return true;
            }
            /* écriture dans les périphériques (sortie) */
            if ( (address >= IO_START_ADDRESS) &&
                 (address <= IO_END_ADDRESS) )
            {
                Output(address, value);
                return true;
            }
            /* si on arrive ici, on tente d'écrire en ROM :
               sur MO5, cela ne provoque rien, même pas d'erreur */
            if (this.debugFile != null) {
                this.debugFile.WriteLine(WARN_UNUSED_MEM_ADDRESS_WRITE,
                                         address, value);
            }
            return true;
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Accès textuel en écriture (<code>StreamWriter</code>)
        /// à un éventuel fichier d'informations pour le déboguage.
        /// </summary>
        public StreamWriter DebugFileStreamWriter
        {
            get {
                return this.debugFile;
            }
            set {
                this.debugFile = value;
            }
        }

    }
}


