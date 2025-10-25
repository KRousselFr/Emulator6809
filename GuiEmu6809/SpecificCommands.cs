using System.Windows.Input;


namespace GUIEmu6809.Commands
{
    /// <summary>
    /// Déclarations des commandes WPF personnalisées
    /// pour ce programme.
    /// </summary>
    public class SpecificCommands
    {
        /* ~~~~ Commandes WPF perso ~~~~ */

        public static readonly RoutedUICommand ResetCPUCommand =
                new RoutedUICommand("Reset the CPU",
                                    "ResetCPU",
                                    typeof(SpecificCommands));
        public static readonly RoutedUICommand StepCPUCommand =
                new RoutedUICommand("Executes one instruction on the CPU",
                                    "StepCPU",
                                    typeof(SpecificCommands));
        public static readonly RoutedUICommand RunCyclesCommand =
                new RoutedUICommand("Executes many instructions on the CPU",
                                    "RunCyclesCPU",
                                    typeof(SpecificCommands));
        public static readonly RoutedUICommand RunCPUCommand =
                new RoutedUICommand("Starts execution on the CPU",
                                    "RunCPU",
                                    typeof(SpecificCommands));
        public static readonly RoutedUICommand StopCPUCommand =
                new RoutedUICommand("Stops execution on the CPU",
                                    "StopCPU",
                                    typeof(SpecificCommands));
        public static readonly RoutedUICommand BreakpointsCommand =
                new RoutedUICommand("Defines the breakpoints",
                                    "Breakpoints",
                                    typeof(SpecificCommands));
        public static readonly RoutedUICommand TraceOnCommand =
                new RoutedUICommand("Starts the trace of the execution on the CPU",
                                    "TraceOn",
                                    typeof(SpecificCommands));
        public static readonly RoutedUICommand TraceOffCommand =
                new RoutedUICommand("Ends the trace of the execution on the CPU",
                                    "TraceOff",
                                    typeof(SpecificCommands));

    }
}

