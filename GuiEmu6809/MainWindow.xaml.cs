using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

using Microsoft.Win32;

using Emulator6809;


namespace GuiEmu6809
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /* ========================= TYPES INTERNES ========================= */

        private delegate void DelegateUpdateUI(bool complete);


        /* =========================== CONSTANTES =========================== */

        // messages affichés
        const String ERR_TITLE = "Erreur !";
        const String ERR_FMT_INVALID_HEX_VALUE =
                "{0} n'est pas une valeur hexadécimale correcte" +
                " sur {1} bits !";
        const String WARN_TITLE = "Attention !";
        const String WARN_REINIT_EMULATOR =
                "Voulez-vous vraiment réinitialiser l'émulateur ?\r\n" +
                "Tout le travail en cours sera perdu !";
        const String WARN_QUIT_EMULATOR =
                "Voulez-vous vraiment quitter l'émulateur ?\r\n" +
                "Tout le travail en cours sera perdu !";
        const String OFD_BIN_FILE_TITLE =
                "Sélectionnez le fichier binaire à charger";
        const String SFD_BIN_FILE_TITLE =
                "Sélectionnez le fichier binaire à sauvegarder";
        const String INFO_TITLE = "Information";
        const String INFO_FMT_MEMORY_SAVED =
                "Mémoire sauvegardée dans le fichier '{0}'.";
        const String SFD_TRACE_FILE_TITLE =
                "Sélectionnez le fichier de traçage à sauvegarder";

        // autres chaînes (NE PAS TRADUIRE !)
        private const string MEM_FILE_DEFAULT_EXT = ".bin";
        private const string MEM_FILES_FILTER =
                "Fichiers binaires (*.bin)|*.bin|" +
                "Fichiers 65xx assemblés (*.65p)|*.65p|" +
                "Tous les fichiers (*.*)|*.*";
        private const string TRACE_FILE_DEFAULT_EXT = ".txt";
        private const string TRACE_FILES_FILTER =
                "Fichier texte (*.txt)|*.txt|" +
                "Tous les fichiers (*.*)|*.*";

        // valeurs numériques
        private const int POS_MNEMO_DISASM_LINE = 18;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // "flag" signalant que l'interface est contruite
        private bool guiDone = false;

        // processeur émulé
        private CPU6809 processor;

        // espace-mémoire attaché au processeur
        // (défini une fois pour toutes à la construction)
        private BasicMemorySpace6809 memSpace;

        // points d'arrêt (pour le déboguage)
        private List<DebuggerTrap6809> debugTraps;

        // outil de désassemblage
        private Disasm6809 disasm;

        // outil de formatage de la mémoire
        private MemoryFormatter_8bit memFmt;

        // outil de formatage de la pile
        private StackFormatter6809 stkFmt;


        // "délégué" de mise à jour de l'affichage
        private DelegateUpdateUI uiUpdater;

        // tâche d'exécution du processeur en arrière-plan
        private BackgroundWorker cpuRunner;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur par défaut (et unique) de cette classe.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            /* créé l'espace-mémoire émulé */
            this.memSpace = new BasicMemorySpace6809();
            /* créé le processeur émulé */
            this.processor = new CPU6809(this.memSpace);
            this.processor.Reset();

            /* outils / utilitaires */
            this.disasm = new Disasm6809(this.memSpace);
            this.memFmt = new MemoryFormatter_8bit(this.memSpace);
            this.stkFmt = new StackFormatter6809(this.memSpace);

            /* liste des points d'arrêt */
            this.debugTraps = new List<DebuggerTrap6809>();
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        /* ~~ Méthodes utilitaires ~~ */

        private static byte HexToByte(string hex)
        {
            try {
                return Convert.ToByte(hex, 16);
            } catch (Exception) {
                MessageBox.Show(App.Current.MainWindow,
                                String.Format(ERR_FMT_INVALID_HEX_VALUE,
                                              hex, 8),
                                ERR_TITLE,
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return 0xff;
            }
        }

        private static ushort HexToAddr(string hex)
        {
            try {
                return Convert.ToUInt16(hex, 16);
            } catch (Exception) {
                MessageBox.Show(App.Current.MainWindow,
                                String.Format(ERR_FMT_INVALID_HEX_VALUE,
                                              hex, 16),
                                ERR_TITLE,
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return 0xffff;
            }
        }


        /* ~~ Gestion des points d'arrêt ~~ */

        private bool CheckForTrap()
        {
            foreach (DebuggerTrap6809 dt in this.debugTraps) {
                if (!(dt.Enabled)) continue;
                switch (dt.TrapKind) {
                    case DebuggerTrapKind6809.Breakpoint:
                        if (this.processor.RegisterPC == dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.SPunderflow:
                        // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                        break;
                    case DebuggerTrapKind6809.Aequals:
                        if (this.processor.RegisterA == dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.AlessThan:
                        if (this.processor.RegisterA < dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.AmoreThan:
                        if (this.processor.RegisterA > dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.Xequals:
                        if (this.processor.RegisterX == dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.XlessThan:
                        if (this.processor.RegisterX < dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.XmoreThan:
                        if (this.processor.RegisterX > dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.Yequals:
                        if (this.processor.RegisterY == dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.YlessThan:
                        if (this.processor.RegisterY < dt.ReferenceValue)
                            return true;
                        break;
                    case DebuggerTrapKind6809.YmoreThan:
                        if (this.processor.RegisterY > dt.ReferenceValue)
                            return true;
                        break;
                }
            }
            return false;
        }

        /* ~~ Gestion des contrôles ~~ */

        private void UpdateRegisterView()
        {
            this.tbRegA.Text = this.processor.RegisterA.ToString("X2");
            this.tbRegB.Text = this.processor.RegisterB.ToString("X2");
            this.tbRegX.Text = this.processor.RegisterX.ToString("X4");
            this.tbRegY.Text = this.processor.RegisterY.ToString("X4");
            this.tbRegS.Text = this.processor.RegisterS.ToString("X4");
            this.tbRegU.Text = this.processor.RegisterU.ToString("X4");
            this.tbRegDP.Text = this.processor.RegisterDP.ToString("X2");
            this.tbRegPC.Text = this.processor.RegisterPC.ToString("X4");
            string nextInstr = this.disasm.DisassembleInstructionAt(
                    this.processor.RegisterPC);
            nextInstr = nextInstr.Substring(POS_MNEMO_DISASM_LINE).Trim();
            this.txtNextInstr.Text = nextInstr;
            this.tbRegCC.Text = this.processor.RegisterCC.ToString("X2");
            this.cbFlagE.IsChecked = this.processor.FlagE;
            this.cbFlagF.IsChecked = this.processor.FlagF;
            this.cbFlagH.IsChecked = this.processor.FlagH;
            this.cbFlagI.IsChecked = this.processor.FlagI;
            this.cbFlagN.IsChecked = this.processor.FlagN;
            this.cbFlagZ.IsChecked = this.processor.FlagZ;
            this.cbFlagV.IsChecked = this.processor.FlagV;
            this.cbFlagC.IsChecked = this.processor.FlagC;
            this.tbCycles.Text = this.processor.ElapsedCycles.ToString();
        }

        private void UpdateStackView()
        {
            string stackContent =
                    this.stkFmt.ListStackValues(0X2000,   // TODO ! En faire une variable !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                                                this.processor.RegisterS);
            this.tbStackView.Text = stackContent;
        }

        private void UpdateMemoryView()
        {
            this.Cursor = Cursors.Wait;
            try {
                ushort from = HexToAddr(this.tbMemoryFrom.Text);
                ushort to = HexToAddr(this.tbMemoryTo.Text);
                string memContent = this.memFmt.ListMemoryValues(from, to);
                this.tbMemoryView.Text = memContent;
            } finally {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void UpdateDisasmView()
        {
            this.Cursor = Cursors.Wait;
            try {
                ushort from = HexToAddr(this.tbDisasmFrom.Text);
                ushort to = HexToAddr(this.tbDisasmTo.Text);
                string disasm = this.disasm.DisassembleMemory(from, to);
                this.tbDisasmView.Text = disasm;
            } finally {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void UpdateUI(bool complete)
        {
            UpdateRegisterView();
            UpdateStackView();
            if (complete) {
                UpdateMemoryView();
                UpdateDisasmView();
            }
        }


        /* ~~ Gestionnaires d'évènements ~~ */

        /* gère l'ouverture de la fenêtre = lancement du programme */
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            /* initialise les contrôles */
            this.tbDisasmView.Text = String.Empty;
            this.tbMemoryView.Text = String.Empty;
            this.tbStackView.Text = String.Empty;

            /* tâche de fond pour l'exécution */
            this.cpuRunner = new BackgroundWorker {
                WorkerSupportsCancellation = true
            };
            this.cpuRunner.DoWork += CpuRunner_DoWork;
            this.cpuRunner.RunWorkerCompleted += CpuRunner_RunWorkerCompleted;

            /* "délégué" pour la mise à jour de l'interface */
            this.uiUpdater = UpdateUI;

            /* prêt à traiter les évènements */
            this.guiDone = true;

            /* met à jour le contenu des fenêtres */
            UpdateRegisterView();
            UpdateStackView();
            UpdateMemoryView();
            UpdateDisasmView();
        }


        private void TbDisasmLimits_TextChanged(object sender,
                                                TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* régénère le désassemblage */
            UpdateDisasmView();
        }

        private void TbMemoryLimits_TextChanged(object sender,
                                                TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* régénère la vue de la mémoire */
            UpdateMemoryView();
        }


        private void TbRegA_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre A (accumulateur principal) */
            this.processor.RegisterA = HexToByte(this.tbRegA.Text);
            UpdateRegisterView();
        }


        private void TbRegB_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre B (accumulateur secondaire) */
            this.processor.RegisterB = HexToByte(this.tbRegB.Text);
            UpdateRegisterView();
        }

        private void TbRegX_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre X */
            this.processor.RegisterX = HexToAddr(this.tbRegX.Text);
            UpdateRegisterView();
        }

        private void TbRegY_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre Y */
            this.processor.RegisterY = HexToAddr(this.tbRegY.Text);
            UpdateRegisterView();
        }

        private void TbRegS_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre S (pointeur de pile) */
            this.processor.RegisterS = HexToAddr(this.tbRegS.Text);
            UpdateRegisterView();
            /* MàJ de la vue de la pile */
            UpdateStackView();
        }

        private void TbRegU_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre S (pointeur de pile) */
            this.processor.RegisterU = HexToAddr(this.tbRegU.Text);
            UpdateRegisterView();
            /* MàJ de la vue de la pile */
            //UpdateStackView();
        }

        private void TbRegDP_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre Y */
            this.processor.RegisterDP = HexToByte(this.tbRegDP.Text);
            UpdateRegisterView();
        }

        private void TbRegPC_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du registre A (accumulateur) */
            this.processor.RegisterPC = HexToAddr(this.tbRegPC.Text);
            UpdateRegisterView();
        }

        private void CbFlagC_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag C ("Carry") */
            this.processor.FlagC = this.cbFlagC.IsChecked.Value;
            UpdateRegisterView();
        }

        private void CbFlagV_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag V ("oVerflow") */
            this.processor.FlagV = this.cbFlagV.IsChecked.Value;
            UpdateRegisterView();
        }

        private void CbFlagZ_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag Z (Zéro) */
            this.processor.FlagZ = this.cbFlagZ.IsChecked.Value;
            UpdateRegisterView();
        }

        private void CbFlagN_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag N (Négatif) */
            this.processor.FlagN = this.cbFlagN.IsChecked.Value;
            UpdateRegisterView();
        }

        private void CbFlagI_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag I (Interruptions masquées) */
            this.processor.FlagI = this.cbFlagI.IsChecked.Value;
            UpdateRegisterView();
        }

        private void CbFlagH_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag N (Négatif) */
            this.processor.FlagH = this.cbFlagH.IsChecked.Value;
            UpdateRegisterView();
        }

        private void CbFlagF_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag N (Négatif) */
            this.processor.FlagF = this.cbFlagF.IsChecked.Value;
            UpdateRegisterView();
        }

        private void CbFlagE_Checked(object sender, RoutedEventArgs e)
        {
            if (!(this.guiDone)) return;
            /* change la valeur du flag N (Négatif) */
            this.processor.FlagE = this.cbFlagE.IsChecked.Value;
            UpdateRegisterView();
        }



        private void CommandNew_Executed(object sender,
                                         ExecutedRoutedEventArgs e)
        {
            MessageBoxResult res = MessageBox.Show(
                    this,
                    WARN_REINIT_EMULATOR,
                    WARN_TITLE,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
            if (res != MessageBoxResult.Yes) return;
            /* réinitialise l'espace mémoire et le CPU */
            this.memSpace.Clear();
            this.processor.Reset();
            /* MàJ complète de l'interface */
            Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    uiUpdater,
                    true);
        }

        private void CommandOpen_Executed(object sender,
                                          ExecutedRoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog {
                AddExtension = true,
                CheckFileExists = true,
                CheckPathExists = true,
                DefaultExt = MEM_FILE_DEFAULT_EXT,
                Filter = MEM_FILES_FILTER,
                Multiselect = false,
                Title = OFD_BIN_FILE_TITLE,
                ValidateNames = true
            };
            if (ofd.ShowDialog() != true) return;
            string srcFilePath = ofd.FileName;
            this.memSpace.LoadFromFile(srcFilePath);
            /* réinit le processeur */
            this.processor.Reset();
            /* MàJ complète de l'interface */
            Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    uiUpdater,
                    true);
        }

        private void CommandSaveAs_Executed(object sender,
                                            ExecutedRoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog {
                AddExtension = true,
                CheckPathExists = true,
                OverwritePrompt = true,
                DefaultExt = MEM_FILE_DEFAULT_EXT,
                Filter = MEM_FILES_FILTER,
                Title = SFD_BIN_FILE_TITLE,
                ValidateNames = true
            };
            if (sfd.ShowDialog() != true) return;
            string destFilePath = sfd.FileName;
            this.memSpace.SaveToFile(destFilePath);
            MessageBox.Show(this,
                            String.Format(INFO_FMT_MEMORY_SAVED,
                                          destFilePath),
                            INFO_TITLE,
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        private void CommandClose_Executed(object sender,
                                           ExecutedRoutedEventArgs e)
        {
            MessageBoxResult res = MessageBox.Show(
                    this,
                    WARN_QUIT_EMULATOR,
                    WARN_TITLE,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
            if (res != MessageBoxResult.Yes) return;
            /* quitte le programme en fermant cette fenêtre */
            Close();
        }

        private void CommandResetCPU_CanExecute(object sender,
                                                CanExecuteRoutedEventArgs e)
        {
            /* commande disponible HORS exécution en tâche de fond */
            e.CanExecute = !(this.cpuRunner.IsBusy);
        }

        private void CommandResetCPU_Executed(object sender,
                                              ExecutedRoutedEventArgs e)
        {
            this.processor.Reset();
            /* MàJ complète de l'interface */
            Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    uiUpdater,
                    true);
        }

        private void CommandStepCPU_CanExecute(object sender,
                                               CanExecuteRoutedEventArgs e)
        {
            /* commande disponible HORS exécution en tâche de fond */
            e.CanExecute = !(this.cpuRunner.IsBusy);
        }

        private void CommandStepCPU_Executed(object sender,
                                             ExecutedRoutedEventArgs e)
        {
            try {
                this.processor.Step();
            } catch (Exception exc) {
                MessageBox.Show(this,
                                exc.Message,
                                exc.GetType().Name,
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            /* MàJ complète de l'interface */
            Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    uiUpdater,
                    true);
        }

        private void CommandRunCycles_CanExecute(object sender,
                                                 CanExecuteRoutedEventArgs e)
        {
            /* commande disponible HORS exécution en tâche de fond */
            e.CanExecute = !(this.cpuRunner.IsBusy);
        }

        private void CommandRunCycles_Executed(object sender,
                                               ExecutedRoutedEventArgs e)
        {
            MessageBox.Show(this,
                            "A implanter !",
                            null,
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
            // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void CommandRunCPU_CanExecute(object sender,
                                              CanExecuteRoutedEventArgs e)
        {
            /* commande disponible HORS exécution en tâche de fond */
            e.CanExecute = !(this.cpuRunner.IsBusy);
        }

        private void CommandRunCPU_Executed(object sender,
                                            ExecutedRoutedEventArgs e)
        {
            /* lance l'exécution en tâche de fond */
            this.cpuRunner.RunWorkerAsync();
        }

        private void CommandStopCPU_CanExecute(object sender,
                                               CanExecuteRoutedEventArgs e)
        {
            /* commande disponible quand l'exécution est en cours */
            e.CanExecute = (this.cpuRunner.IsBusy);
        }

        private void CommandStopCPU_Executed(object sender,
                                             ExecutedRoutedEventArgs e)
        {
            /* demande l'arrêt de l'exécution en tâche de fond */
            this.cpuRunner.CancelAsync();
        }

        private void CommandBkpts_CanExecute(object sender,
                                             CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
            // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void CommandBkpts_Executed(object sender,
                                           ExecutedRoutedEventArgs e)
        {
            BreakpointWindow bw = new BreakpointWindow {
                Traps = this.debugTraps
            };
            bool? ok = bw.ShowDialog();
            if (ok != true) return;
            this.debugTraps = bw.Traps;
        }

        private void CommandTraceOn_CanExecute(object sender,
                                               CanExecuteRoutedEventArgs e)
        {
            /* commande disponible HORS exécution en tâche de fond */
            e.CanExecute = (!(this.cpuRunner.IsBusy) &&
                              (this.processor.TraceFileWriter == null));
        }

        private void CommandTraceOn_Executed(object sender,
                                             ExecutedRoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog() {
                AddExtension = true,
                CheckPathExists = true,
                OverwritePrompt = true,
                DefaultExt = TRACE_FILE_DEFAULT_EXT,
                Filter = TRACE_FILES_FILTER,
                Title = SFD_TRACE_FILE_TITLE,
                ValidateNames = true
            };
            if (sfd.ShowDialog() != true) return;
            this.processor.TraceFileWriter =
                    File.CreateText(sfd.FileName);
        }

        private void CommandTraceOff_CanExecute(object sender,
                                                CanExecuteRoutedEventArgs e)
        {
            /* commande disponible HORS exécution en tâche de fond */
            e.CanExecute = (!(this.cpuRunner.IsBusy) &&
                              (this.processor.TraceFileWriter != null));
        }

        private void CommandTraceOff_Executed(object sender,
                                              ExecutedRoutedEventArgs e)
        {
            this.processor.TraceFileWriter = null;
        }

        /* ~~ Exécution du processeur en tâche de fond ~~ */

        private void CpuRunner_DoWork(object sender,
                                      DoWorkEventArgs e)
        {
            while (!(this.cpuRunner.CancellationPending)) {
                if (CheckForTrap()) {
                    this.cpuRunner.CancelAsync();
                    break;
                }
                this.processor.Step();
                /* MàJ rapide de l'interface */
                Application.Current.Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        uiUpdater,
                        false);
                Thread.Sleep(20);
            }
        }

        private void CpuRunner_RunWorkerCompleted(object sender,
                                                  RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null) {
                MessageBox.Show(this,
                                e.Error.Message,
                                e.Error.GetType().Name,
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            /* MàJ complète de l'interface */
            Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    uiUpdater,
                    true);
        }



        /* ======================= MÉTHODES PUBLIQUES ======================= */

    }
}


