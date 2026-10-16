// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.ComponentModel.DataAnnotations;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Registro que vive también en la nube (Firestore: usuarios/{uid}/{colección}/{Uid}).
    /// La nube es la base principal: el PC y el celular leen y escriben ahí; si los dos cambian
    /// el mismo registro, gana el cambio más reciente (ActualizadaUtc).
    /// </summary>
    public interface IReplicable
    {
        /// <summary>Identidad del registro, igual en el PC y en la nube (el Id numérico es solo de este PC).</summary>
        string Uid { get; set; }
        /// <summary>Último cambio, en UTC.</summary>
        DateTime ActualizadaUtc { get; set; }
    }

    /// <summary>Cambio hecho en el PC que falta subir a la nube (uno por registro; se envía la versión actual).</summary>
    public class CambioReplica
    {
        [Key] public int Id { get; set; }
        public string Coleccion { get; set; } = string.Empty;
        public string Uid { get; set; } = string.Empty;
        public bool Borrado { get; set; }
        public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Hasta qué cambio (fecha "actualizada" de la nube) se descargó cada colección.</summary>
    public class EstadoReplica
    {
        [Key] public string Coleccion { get; set; } = string.Empty;
        public DateTime HastaUtc { get; set; }
    }
}
