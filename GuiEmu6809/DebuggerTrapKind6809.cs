namespace GuiEmu6809
{
    /// <summary>
    /// Enumération des différents types de conditions d'arrêt.
    /// </summary>
    public enum DebuggerTrapKind6809
    {
        /// Point d'arrêt (sur une valeur du registre PC.
        Breakpoint,

        /// Débordement du registre S.
        SPunderflow,

        /// Valeur-cible pour le registre A (accumulateur).
        Aequals,

        /// Valeur inférieure à une référence pour le registre A (accumulateur).
        AlessThan,

        /// Valeur supérieure à une référence pour le registre A (accumulateur).
        AmoreThan,

        /// Valeur-cible pour le registre X.
        Xequals,

        /// Valeur inférieure à une référence pour le registre X.
        XlessThan,

        /// Valeur supérieure à une référence pour le registre X.
        XmoreThan,

        /// Valeur-cible pour le registre Y.
        Yequals,

        /// Valeur inférieure à une référence pour le registre Y.
        YlessThan,

        /// Valeur supérieure à une référence pour le registre Y.
        YmoreThan
    }
}


