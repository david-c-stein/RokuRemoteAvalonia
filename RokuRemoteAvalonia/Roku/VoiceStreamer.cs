using OpenTK.Audio.OpenAL;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace RRemote.Roku
{
    /// <summary>
    /// Captures microphone audio as 16 kHz / 16-bit / mono PCM and streams the raw
    /// bytes to a custom on-device Roku app over a WebSocket.
    /// Cross-platform via OpenAL (OpenTK); Avalonia itself has no audio API.
    /// </summary>
    public sealed class VoiceStreamer : IAsyncDisposable
    {
        private const int SampleRate = 16000;          // matches JS: sampleRate: 16000
        private const ALFormat Format = ALFormat.Mono16; // 16-bit mono PCM

        private ALCaptureDevice _captureDevice;
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private Task? _pumpTask;

        public bool IsStreaming { get; private set; }

        public async Task StartAsync(Uri webSocketUri, CancellationToken cancellationToken = default)
        {
            if (IsStreaming) return;

            // Open the default capture device with a ~1 second internal buffer.
            _captureDevice = ALC.CaptureOpenDevice(null, SampleRate, Format, SampleRate);
            if (_captureDevice == ALCaptureDevice.Null)
                throw new InvalidOperationException("Unable to open a microphone capture device.");

            // The TV opens the audio port shortly after the Search keypress,
            // so retry the connection for a few seconds before giving up.
            const int maxAttempts = 25;   // ~5s at 200ms
            _ws = new ClientWebSocket();
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await _ws.ConnectAsync(webSocketUri, cancellationToken);
                    break;
                }
                catch (WebSocketException) when (attempt < maxAttempts)
                {
                    _ws.Dispose();
                    _ws = new ClientWebSocket();
                    await Task.Delay(200, cancellationToken);
                }
            }

            ALC.CaptureStart(_captureDevice);
            IsStreaming = true;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pumpTask = Task.Run(() => PumpAsync(_cts.Token));
        }

        private async Task PumpAsync(CancellationToken token)
        {
            var samples = new short[SampleRate]; // up to 1s of audio per read
            var bytes = new byte[samples.Length * sizeof(short)];

            try
            {
                while (!token.IsCancellationRequested && _ws is { State: WebSocketState.Open })
                {
                    int available = GetAvailableSamples();
                    if (available <= 0)
                    {
                        await Task.Delay(10, token); // let the buffer fill
                        continue;
                    }

                    int toRead = Math.Min(available, samples.Length);
                    ALC.CaptureSamples(_captureDevice, ref samples[0], toRead);

                    int byteCount = toRead * sizeof(short);
                    Buffer.BlockCopy(samples, 0, bytes, 0, byteCount); // little-endian PCM

                    await _ws.SendAsync(
                        new ArraySegment<byte>(bytes, 0, byteCount),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        token);
                }
            }
            catch (OperationCanceledException) { /* normal on stop */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceStreamer] {ex.Message}");
            }
        }

        private int GetAvailableSamples()
        {
            int[] result = new int[1];
            ALC.GetInteger(new ALDevice(_captureDevice.Handle), AlcGetInteger.CaptureSamples, 1, result);
            return result[0];
        }

        public async Task StopAsync()
        {
            if (!IsStreaming) return;
            IsStreaming = false;

            _cts?.Cancel();
            if (_pumpTask is not null)
            {
                try { await _pumpTask; } catch { /* ignore */ }
                _pumpTask = null;
            }

            if (_captureDevice != ALCaptureDevice.Null)
            {
                ALC.CaptureStop(_captureDevice);
                ALC.CaptureCloseDevice(_captureDevice);
                _captureDevice = ALCaptureDevice.Null;
            }

            if (_ws is not null)
            {
                if (_ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                    }
                    catch { /* ignore */ }
                }
                _ws.Dispose();
                _ws = null;
            }

            _cts?.Dispose();
            _cts = null;
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
}