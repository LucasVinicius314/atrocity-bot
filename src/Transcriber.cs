using System.Diagnostics;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace atrocity_bot
{
  // Local speech-to-text using whisper.cpp (via Whisper.net). Audio is converted to
  // 16kHz mono WAV with ffmpeg first, since Discord voice notes are ogg/opus.
  class Transcriber
  {
    readonly string modelPath;
    readonly GgmlType modelType;
    readonly SemaphoreSlim initLock = new SemaphoreSlim(1, 1);
    // whisper.cpp processors are not thread safe, so transcriptions run one at a time.
    readonly SemaphoreSlim runLock = new SemaphoreSlim(1, 1);
    WhisperFactory? factory;

    public Transcriber(string modelPath, GgmlType modelType)
    {
      this.modelPath = modelPath;
      this.modelType = modelType;
    }

    public async Task<string> TranscribeAsync(byte[] audio, CancellationToken cancellationToken = default)
    {
      var wavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

      try
      {
        await ConvertToWavAsync(audio, wavPath, cancellationToken);

        var whisperFactory = await GetFactoryAsync(cancellationToken);

        await runLock.WaitAsync(cancellationToken);

        try
        {
          using var processor = whisperFactory.CreateBuilder().WithLanguage("auto").Build();
          using var wav = File.OpenRead(wavPath);

          var text = new StringBuilder();

          await foreach (var segment in processor.ProcessAsync(wav, cancellationToken))
          {
            text.Append(segment.Text);
          }

          return text.ToString().Trim();
        }
        finally
        {
          runLock.Release();
        }
      }
      finally
      {
        File.Delete(wavPath);
      }
    }

    async Task<WhisperFactory> GetFactoryAsync(CancellationToken cancellationToken)
    {
      await initLock.WaitAsync(cancellationToken);

      try
      {
        if (factory != null)
        {
          return factory;
        }

        if (!File.Exists(modelPath))
        {
          Console.WriteLine($"Downloading whisper model {modelType} to {modelPath}.");

          Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(modelPath))!);

          using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType, cancellationToken: cancellationToken);
          using var file = File.Create(modelPath);
          await modelStream.CopyToAsync(file, cancellationToken);
        }

        factory = WhisperFactory.FromPath(modelPath);

        return factory;
      }
      finally
      {
        initLock.Release();
      }
    }

    static async Task ConvertToWavAsync(byte[] audio, string wavPath, CancellationToken cancellationToken)
    {
      var inputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.input");

      try
      {
        await File.WriteAllBytesAsync(inputPath, audio, cancellationToken);

        var startInfo = new ProcessStartInfo("ffmpeg")
        {
          RedirectStandardError = true,
          RedirectStandardOutput = true,
          UseShellExecute = false,
        };

        foreach (var arg in new[] { "-y", "-i", inputPath, "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wavPath })
        {
          startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ffmpeg.");

        // Drain both streams so ffmpeg never blocks on a full pipe.
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await stdout;

        if (process.ExitCode != 0)
        {
          throw new InvalidOperationException($"ffmpeg failed: {await stderr}");
        }
      }
      finally
      {
        File.Delete(inputPath);
      }
    }
  }
}
