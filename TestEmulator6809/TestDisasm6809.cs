using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Emulator6809;


namespace TestEmulator6809
{
    /// <summary>
    /// Classe de test du désassembleur de code binaire 6809.
    /// </summary>
    [TestClass]
    public class TestDisasm6809 : IMemorySpace6809
    {
        /* =========================== CONSTANTES =========================== */

        /* ~~ Noms / chemins de fichiers ~~ */
        private const string TEST_6809_BIN_FILE = "MO5-ROM-v1.0.bin";
        private const string DISASSEMBLY_TEXT_FILE = "6809_Disasm.txt";

        // taille de l'espace-mémoire du 6809
        private const int MEM_SPACE_SIZE = 65536;
        // taille de la ROM à désassembler (en octets)
        private const int ROM_BYTE_SIZE = 16384;
        // adresse de base de la ROM dans l'espace-mémoire 6809
        private const ushort ROM_BASE_ADDRESS = 0xf000;
        // adresse de fin de la ROM
        private const ushort ROM_END_ADDRESS = 0xffff;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // espace-mémoire émulé
        private byte[] memSpace;


        /* ================= MÉTHODES PRIVÉES (UTILITAIRES) ================= */

        /* charge le fichier des opcodes à désassembler */
        private void LoadROMBinFile(string binFilePath)
        {
            int fileSize = (int)(new FileInfo(binFilePath).Length);
            this.memSpace = new byte[MEM_SPACE_SIZE];
            using (FileStream fs = File.OpenRead(binFilePath)) {
                fs.Read(this.memSpace, 0, fileSize);
            }
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /* ~~ Méthodes héritées (de IMemorySpaceAVR8) ~~ */

        public byte? ReadMemory(ushort address)
        {
            return this.memSpace[address];
        }

        public bool WriteMemory(ushort address, byte value)
        {
            /* inutile pour tester le désassembleur */
            return false;
        }

        /* ~~ Méthodes de test (= points d'entrée) ~~ */

        /// <summary>
        /// Teste le désassemblage de la ROM (v 1.1) du Thomson MO5.
        /// </summary>
        [TestMethod]
        public void TestDisasmROMMO5()
        {
            if (!(File.Exists(TEST_6809_BIN_FILE))) {
                throw new FileNotFoundException(TEST_6809_BIN_FILE);
            }
            LoadROMBinFile(TEST_6809_BIN_FILE);
            GC.Collect();

            Disasm6809 disasm = new Disasm6809(this) {
                InvalidOpcodePolicy = UnknownOpcodePolicy.DoNop
            };
            string disassembly = disasm.DisassembleMemory(
                    ROM_BASE_ADDRESS,
                    ROM_END_ADDRESS);
            using (StreamWriter sw = File.CreateText(DISASSEMBLY_TEXT_FILE)) {
                sw.WriteLine(disassembly);
                sw.Flush();
            }
        }

    }
}


