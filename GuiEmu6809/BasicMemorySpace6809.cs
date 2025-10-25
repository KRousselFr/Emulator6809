using System;
using System.IO;
using System.Windows;

using Emulator6809;


namespace GuiEmu6809
{
    /// <summary>
    /// Classe émulant un espace-mémoire basique pour
    /// l'émulation d'un processeur Motorola 6809.
    /// </summary>
    internal class BasicMemorySpace6809 : IMemorySpace6809
    {
        /* =========================== CONSTANTES =========================== */

        // messages d'erreur
        const string ERR_WRITE_IN_ROM =
                "Erreur, tentative d'écriture en ROM (adresse ${0:X4}) !";
        const string WARN_SHORT_FILE_LOAD =
                "Seuls {1} octets ont été lus du fichier '{2}'" +
                " (au lieu de {0}) !\r\n" +
                "Le fichier est-il trop court ?";

        // valeurs numériques
        public const int MEMORY_SIZE = 65536;
        public const ushort DEFAULT_ROM_START = 0xd000;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // tableau représentant la mémoire proprement dite
        private readonly byte[] mem;
        // adresse où commence la ROM (avant : RAM)
        private ushort RomStartAt;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur par défaut (et unique).
        /// </summary>
        public BasicMemorySpace6809()
        {
            this.mem = new byte[MEMORY_SIZE];
            this.RomStartAt = DEFAULT_ROM_START;
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        private void ShowExcept(Exception exc)
        {
            MessageBox.Show(exc.Message,
                            exc.GetType().Name,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Vide toute la mémoire de son contenu (remise à zéro).
        /// </summary>
        public void Clear()
        {
            for (int n = 0; n < MEMORY_SIZE; n++) {
                mem[n] = 0x00;
            }
        }

        /// <summary>
        /// Lit le contenu de la mémoire depuis le fichier indiqué.
        /// </summary>
        /// <param name="filePath">
        /// Chemin du fichier contenant l'espace-mémoire à lire.
        /// </param>
        public void LoadFromFile(string filePath)
        {
            using (FileStream src = File.OpenRead(filePath)) {
                int bytesRead = src.Read(this.mem, 0, MEMORY_SIZE);
                if (bytesRead < MEMORY_SIZE) {
                    MessageBox.Show(
                            String.Format(WARN_SHORT_FILE_LOAD,
                                          MEMORY_SIZE,
                                          bytesRead,
                                          filePath),
                            null,
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Sauve l'intégralité du contenu de la mémoire
        /// dans le fichier indiqué.
        /// </summary>
        /// <param name="filePath">
        /// Chemin du fichier où écrire l'espace-mémoire.
        /// </param>
        public void SaveToFile(string filePath)
        {
            using (FileStream dest = File.OpenWrite(filePath)) {
                dest.Write(this.mem, 0, MEMORY_SIZE);
                dest.Flush(true);
            }
        }

        /* ~~ Méthodes héritées de IMemorySpace6809 ~~ */

        /// <summary>
        /// Lit et renvoie le contenu d'un octet en mémoire.
        /// </summary>
        /// <param name="address">
        /// Adresse de l'octet à lire.
        /// </param>
        /// <returns>
        /// Contenu de l'octet à l'adresse donnée.
        /// Renvoie <code>null</code> en cas de problème.
        /// </returns>
        public byte? ReadMemory(ushort address)
        {
            try {
                return mem[address];
            } catch (Exception exc) {
                ShowExcept(exc);
                return null;
            }
        }

        /// <summary>
        /// crit le contenu d'un octet en mémoire.
        /// </summary>
        /// <param name="address">
        /// Adresse de l'octet à écrire.
        /// </param>
        /// <param name="value">
        /// Nouvelle valeur à écrire pour l'octet.
        /// </param>
        /// <returns>
        /// <code>true</code> si l'écriture a réussi ;
        /// <code>false</code> en cas d'erreur.
        /// </returns>
        public bool WriteMemory(ushort address, byte value)
        {
            try {
                if (address >= this.RomStartAt) {
                    throw new ArgumentException(String.Format(
                            ERR_WRITE_IN_ROM,
                            address));
                }
                mem[address] = value;
                return true;
            } catch (Exception exc) {
                ShowExcept(exc);
                return false;
            }
        }


        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Adresse à partir de laquelle se trouve la ROM :
        /// mémoire ne pouvant être écrite par le processeur.
        /// </summary>
        public UInt16 ROMStartAddress
        {
            get { return this.RomStartAt; }
            set { this.RomStartAt = value; }
        }

    }
}


