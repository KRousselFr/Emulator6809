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

        /// Valeur-cible pour le registre A (accumulateur principal).
        Aequals,

        /// Valeur inférieure à une référence pour le registre A
        /// (premier accumulateur).
        AlessThan,

        /// Valeur supérieure à une référence pour le registre A
        /// (premier accumulateur).
        AmoreThan,

        /// Valeur-cible pour le registre B (deuxième accumulateur).
        Bequals,

        /// Valeur inférieure à une référence pour le registre B
        /// (deuxième accumulateur).
        BlessThan,

        /// Valeur supérieure à une référence pour le registre B
        /// (deuxième accumulateur).
        BmoreThan,

        /// Valeur-cible pour le registre S (pointeur de pile système).
        Sequals,

        /// Valeur inférieure à une référence pour le registre S
        /// (pointeur de pile système).
        SlessThan,

        /// Valeur supérieure à une référence pour le registre S
        /// (pointeur de pile système).
        SmoreThan,

        /// Valeur-cible pour le registre U (pointeur de pile utilisateur).
        Uequals,

        /// Valeur inférieure à une référence pour le registre U
        /// (pointeur de pile utilisateur).
        UlessThan,

        /// Valeur supérieure à une référence pour le registre U
        /// (pointeur de pile utilisateur).
        UmoreThan,

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



