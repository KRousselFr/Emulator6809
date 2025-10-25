using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

using Microsoft.Win32;


namespace GuiEmu6809
{
    /// <summary>
    /// Fenêtre de gestion des points d'arrêt.
    /// </summary>
    public partial class BreakpointWindow : Window
    {
        /* =========================== CONSTANTES =========================== */

        // messages affichés
        private const String OFD_BKPT_FILE_TITLE =
                "Sélectionnez le fichier de points d'arrêt à charger";
        private const String SFD_BKPT_FILE_TITLE =
                "Sélectionnez le fichier de points d'arrêt à sauvegarder";

        // autres chaînes (NE PAS TRADUIRE !)
        private const string BREAKPOINT_FILE_DEFAULT_EXT = ".bkpt";
        private const string BREAKPOINT_FILES_FILTER =
                "Fichiers de points d'arrêt (*.bkpt)|*.bkpt|" +
                "Tous les fichiers (*.*)|*.*";


        /* ========================== CHAMPS PRIVÉS ========================= */

        /* liste les conditions d'arrêt actuellement définies */
        private readonly ObservableCollection<DebuggerTrap6809> trapList;


        /* ========================== CONSTRUCTEUR ========================== */

        public BreakpointWindow()
        {
            InitializeComponent();
            this.trapList = new ObservableCollection<DebuggerTrap6809>();
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        /* ~~ Gestionnaires d'évènements ~~ */

        // Ouverture de la fenêtre
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.dgTraps.ItemsSource = this.trapList;
        }



        // Clic sur le bouton de création d'un nouveau point d'arrêt
        private void BtnCreateTrap_Click(object sender, RoutedEventArgs e)
        {
            DebuggerTrap6809 dt = new DebuggerTrap6809 {
                Enabled = false,
                TrapKind = DebuggerTrapKind6809.Breakpoint,
                ReferenceValue = 0
            };
            this.trapList.Add(dt);
        }

        // Clic sur le bouton de suppression du point d'arrêt sélectionné
        private void BtnDeleteTrap_Click(object sender, RoutedEventArgs e)
        {
            int idx = this.dgTraps.SelectedIndex;
            if (idx < 0) return;
            this.trapList.RemoveAt(idx);
        }

        private void BtnLoadTraps_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog {
                AddExtension = true,
                CheckFileExists = true,
                CheckPathExists = true,
                DefaultExt = BREAKPOINT_FILE_DEFAULT_EXT,
                Filter = BREAKPOINT_FILES_FILTER,
                Multiselect = false,
                Title = OFD_BKPT_FILE_TITLE,
                ValidateNames = true
            };
            if (ofd.ShowDialog() != true) return;
            this.trapList.Clear();
            /* charge les définitions de points d'arrêt
               du fichier indiqué */
            using (StreamReader srcFile = File.OpenText(ofd.FileName)) {
                string ligne = srcFile.ReadLine();
                while (ligne != null) {
                    DebuggerTrap6809 dt = DebuggerTrap6809.FromString(ligne);
                    this.trapList.Add(dt);
                    ligne = srcFile.ReadLine();
                }
            }
        }

        private void BtnSaveTraps_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog {
                AddExtension = true,
                CheckPathExists = true,
                OverwritePrompt = true,
                DefaultExt = BREAKPOINT_FILE_DEFAULT_EXT,
                Filter = BREAKPOINT_FILES_FILTER,
                Title = SFD_BKPT_FILE_TITLE,
                ValidateNames = true
            };
            if (sfd.ShowDialog() != true) return;
            /* écrit les définitions de trous les points d'arrêt
               actuels dans le fichier indiqué */
            using (StreamWriter destFile = File.CreateText(sfd.FileName)) {
                foreach (DebuggerTrap6809 dt in this.trapList) {
                    destFile.WriteLine(dt.ToString());
                    destFile.Flush();
                }
            }
        }

        // Clic sur le bouton 'Fermer'
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        // Clic sur le bouton 'OK'
        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            Close();
        }


        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Liste des points d'arrêt définis.
        /// </summary>
        public List<DebuggerTrap6809> Traps
        {
            get {
                return new List<DebuggerTrap6809>(this.trapList);
            }
            set {
                this.trapList.Clear();
                foreach (DebuggerTrap6809 dt in value) {
                    this.trapList.Add(dt);
                }
            }
        }

    }
}
