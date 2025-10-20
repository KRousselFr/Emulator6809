using System;


namespace Emulator6809
{
    /// <summary>
    /// Interface définissant l'accès d'un processeur 6809 émulé
    /// à l'espace mémoire qui lui est attaché.
    /// <br/>
    /// On rappelle que pour ce processeur, l'espace-mémoire inclut aussi
    /// (en plus de la mémoire proprement dite) les périphériques et
    /// autres entrées / sorties.
    /// </summary>
    public interface IMemorySpace6809
    {
        /// <summary>
        /// Lit la valeur d'un octet en mémoire (ou entrée de périphérique).
        /// </summary>
        /// <param name="address">Adresse-mémoire de l'octet à lire.</param>
        /// <returns>
        /// La valeur lue à l'adresse donnée.
        /// <br/>
        /// Renvoie <code>null</code> si l'adresse en question n'est pas
        /// accessible en lecture.
        /// </returns>
        Byte? ReadMemory(UInt16 address);

        /// <summary>
        /// Écrit la valeur d'un octet en mémoire (ou sortie de périphérique).
        /// </summary>
        /// <param name="address">Adresse-mémoire de l'octet à écrire.</param>
        /// <param name="value">Valeur de l'octet à écrire.</param>
        /// <returns>
        /// Renvoie <code>true</code> si l'écriture a réussi ;
        /// renvoie <code>false</code> en cas de problème (par exemple :
        /// si l'adresse en question n'est pas accessible en écriture).
        /// </returns>
        Boolean WriteMemory(UInt16 address, Byte value);
    }
}

