// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Api.Gax.Grpc;
using Google.Cloud.Firestore.V1;
using Grpc.Core;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Escucha en vivo la marca de cambios de la nube (usuarios/{uid}/estado/cambios) con una conexión permanente
    /// a Firestore, igual que el celular: cuando otro dispositivo escribe, el PC se entera al instante y sincroniza.
    /// Solo cuesta una lectura por cambio. Si la conexión se cae se reconecta sola; mientras tanto la revisión
    /// periódica de la marca sigue de respaldo (MainWindow).
    /// </summary>
    public static partial class SincronizacionCelular
    {
        /// <summary>Hay una conexión en vivo abierta con la nube.</summary>
        public static bool EnVivo { get; private set; }

        /// <summary>Se lanza (en cualquier hilo) cuando la marca de cambios de la nube cambió.</summary>
        public static event Action? CambioEnLaNube;

        private static CancellationTokenSource? _vivo;

        /// <summary>Empieza a escuchar la nube en vivo (si ya escucha o no hay sesión, no hace nada).</summary>
        public static void IniciarEnVivo()
        {
            if (_vivo != null || _sesion == null || !Configurado) return;
            _vivo = new CancellationTokenSource();
            _ = EscucharAsync(_vivo.Token);
        }

        public static void DetenerEnVivo()
        {
            _vivo?.Cancel();
            _vivo = null;
            EnVivo = false;
        }

        private static async Task EscucharAsync(CancellationToken fin)
        {
            var espera = TimeSpan.FromSeconds(2);
            var baseDatos = $"projects/{ProjectId}/databases/(default)";
            while (!fin.IsCancellationRequested)
            {
                var sesion = _sesion;
                if (sesion == null) break;
                try
                {
                    // El token de la cuenta (el mismo de las demás llamadas) va en cada conexión
                    var credencial = CallCredentials.FromInterceptor(async (contexto, datos) =>
                    {
                        var token = await TokenAsync(sesion);
                        if (token != null) datos.Add("authorization", "Bearer " + token);
                        datos.Add("google-cloud-resource-prefix", baseDatos);
                    });
                    var cliente = await new FirestoreClientBuilder
                    {
                        ChannelCredentials = ChannelCredentials.Create(new SslCredentials(), credencial)
                    }.BuildAsync(fin);

                    using var flujo = cliente.Listen(CallSettings.FromCancellationToken(fin));
                    await flujo.WriteAsync(new ListenRequest
                    {
                        Database = baseDatos,
                        AddTarget = new Target
                        {
                            TargetId = 1,
                            Documents = new Target.Types.DocumentsTarget { Documents = { $"{baseDatos}/documents/usuarios/{sesion.Uid}/estado/cambios" } }
                        }
                    });

                    var abierta = DateTime.UtcNow;
                    var respuestas = flujo.GetResponseStream();
                    while (await respuestas.MoveNextAsync(fin))
                    {
                        if (!EnVivo) { EnVivo = true; espera = TimeSpan.FromSeconds(2); }
                        if (respuestas.Current.DocumentChange != null) CambioEnLaNube?.Invoke();
                        // El token dura 1 hora: se reabre la conexión antes de que venza
                        if (DateTime.UtcNow - abierta > TimeSpan.FromMinutes(50)) break;
                    }
                }
                catch (OperationCanceledException) when (fin.IsCancellationRequested) { break; }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && fin.IsCancellationRequested) { break; }
                catch (Exception ex) { Anotar(ex); }

                EnVivo = false;
                try { await Task.Delay(espera, fin); } catch (OperationCanceledException) { break; }
                espera = TimeSpan.FromSeconds(Math.Min(60, espera.TotalSeconds * 2)); // sin conexión: reintenta cada vez más espaciado
            }
            EnVivo = false;
        }
    }
}
