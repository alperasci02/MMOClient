// NetConnection.cs — Faz 2 (Docs/PRODUCTION_TIMELINE.md, İstemci sağlığı) gün 1 dilimi.
// NetClient.cs'ten çıkarılan bağlantı katmanı: soket, retry/backoff, healthz kontrolü.
// Oyun mantığından tamamen habersiz — sadece bağlanır, byte kuyrukları sağlar.
// Davranış NetClient.cs'teki önceki haliyle BİREBİR aynı kalacak şekilde taşındı
// (Faz 1 gün 3'te canlı doğrulanan retry/backoff/healthz mantığı değişmedi).
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MMO
{
    public class NetConnection
    {
        public readonly ConcurrentQueue<byte[]> Inbound = new ConcurrentQueue<byte[]>();
        public readonly ConcurrentQueue<byte[]> Outbound = new ConcurrentQueue<byte[]>();

        public bool IsConnected { get; private set; }
        public string Status { get; private set; } = "";
        public bool DbModeChecked { get; private set; }
        public bool DbPersistent { get; private set; } = true; // henüz kontrol edilmediyse iyimser varsay

        public bool SocketOpen => ws != null && ws.State == WebSocketState.Open;

        readonly MonoBehaviour host; // StartCoroutine için (CheckPersistenceMode)
        readonly string url;
        readonly int maxRetries;
        readonly float maxRetryDelaySec;
        ClientWebSocket ws;
        CancellationTokenSource cts;

        public NetConnection(MonoBehaviour host, string url, int maxRetries, float maxRetryDelaySec)
        {
            this.host = host;
            this.url = url;
            this.maxRetries = maxRetries;
            this.maxRetryDelaySec = maxRetryDelaySec;
        }

        // İsim ekranından çağrılır: kalıcılık-modu kontrolünü ve bağlantı döngüsünü başlatır.
        public void Begin(string playerName)
        {
            cts = new CancellationTokenSource();
            host.StartCoroutine(CheckPersistenceMode());
            _ = ConnectLoop(playerName);
        }

        // Faz 1 (Foundation): bağlanamazsa üstel gecikmeyle tekrar dener — SADECE başlangıçta.
        // Oyun-içi bağlantı kopması sonrası otomatik yeniden bağlanma Faz 2'nin gün 6 işidir (A-10).
        async Task ConnectLoop(string playerName)
        {
            float delay = 1f;
            int attempt = 0;
            while (!cts.IsCancellationRequested)
            {
                attempt++;
                Status = attempt == 1 ? "Sunucuya bağlanılıyor..." : ("Yeniden deneniyor... (" + attempt + ". deneme)");
                ws = new ClientWebSocket();
                try
                {
                    await ws.ConnectAsync(new Uri(url), cts.Token);
                    Debug.Log("[NetConnection] bağlandı: " + url + " olarak '" + playerName + "'");
                    IsConnected = true;
                    Status = "";
                    Outbound.Enqueue(Protocol.EncodeJoin(playerName));
                    _ = ReceiveLoop();
                    _ = SendLoop();
                    return;
                }
                catch (Exception e)
                {
                    if (cts.IsCancellationRequested) return;
                    Debug.LogWarning("[NetConnection] bağlanılamadı (" + attempt + ". deneme): " + e.Message);
                    if (maxRetries > 0 && attempt >= maxRetries)
                    {
                        Status = "Sunucuya bağlanılamadı (" + attempt + " deneme). Sunucuyu başlat (scripts\\start-game.ps1) ve sahneyi yeniden oynat.";
                        Debug.LogError("[NetConnection] " + Status);
                        return;
                    }
                    Status = "Sunucu bulunamadı — " + Mathf.CeilToInt(delay) + " sn sonra tekrar denenecek. (Sunucu açık mı? scripts\\start-game.ps1)";
                    try { await Task.Delay(TimeSpan.FromSeconds(delay), cts.Token); }
                    catch { return; }
                    delay = Mathf.Min(delay * 2f, maxRetryDelaySec);
                }
            }
        }

        // Faz 1 (Foundation): sunucunun /healthz'inden kalıcılık modunu (DB var mı) okur (A-05).
        // Tel protokolüne dokunmaz — ayrı, tek seferlik bir HTTP isteğidir.
        IEnumerator CheckPersistenceMode()
        {
            string healthUrl = ToHealthzUrl(url);
            if (healthUrl == null) yield break;
            using (var req = UnityWebRequest.Get(healthUrl))
            {
                req.timeout = 3;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    DbPersistent = req.downloadHandler.text.Contains("\"db\":true");
                    DbModeChecked = true;
                }
            }
        }

        static string ToHealthzUrl(string wsUrl)
        {
            if (string.IsNullOrEmpty(wsUrl)) return null;
            string h = wsUrl.Replace("wss://", "https://").Replace("ws://", "http://");
            int idx = h.IndexOf("/ws", StringComparison.Ordinal);
            if (idx >= 0) h = h.Substring(0, idx);
            return h.TrimEnd('/') + "/healthz";
        }

        async Task ReceiveLoop()
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    using (var mem = new System.IO.MemoryStream())
                    {
                        WebSocketReceiveResult res;
                        do
                        {
                            res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                            if (res.MessageType == WebSocketMessageType.Close) return;
                            mem.Write(buf, 0, res.Count);
                        } while (!res.EndOfMessage);
                        Inbound.Enqueue(mem.ToArray());
                    }
                }
            }
            finally
            {
                if (!cts.IsCancellationRequested)
                {
                    IsConnected = false;
                    Status = "Bağlantı koptu. (Otomatik yeniden bağlanma Faz 2'de eklenecek — şimdilik sahneyi yeniden başlat.)";
                    Debug.LogWarning("[NetConnection] " + Status);
                }
            }
        }

        async Task SendLoop()
        {
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                if (Outbound.TryDequeue(out var frame))
                    await ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, cts.Token);
                else
                    await Task.Delay(5, cts.Token);
            }
        }

        // OnDestroy'dan çağrılır: soketi düzgün kapatır (sunucu-taraflı leave/flush tetikler).
        public async Task CloseGracefully()
        {
            try { cts?.Cancel(); } catch { }
            if (ws != null && ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { }
            }
            ws?.Dispose();
        }
    }
}
