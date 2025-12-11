using System;
using System.IO;
using System.Threading.Tasks;
using Remotier.Services.Network;

namespace Remotier.Services;

public class FileTransferService
{
    // For receiving
    private string _receivingFileName;
    private long _receivingFileSize;
    private FileStream _fileStream;
    private long _receivedBytes;
    private string _tempPath;

    // Events
    public event Action<string, long> TransferStarted = delegate { };
    public event Action<double> TransferProgress = delegate { }; // 0.0 to 1.0
    public event Action<string> TransferCompleted = delegate { };
    public event Action<string> TransferFailed = delegate { };

    public bool IsReceiving => _fileStream != null;

    public void StartReceiving(string fileName, long fileSize)
    {
        try
        {
            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }

            _receivingFileName = fileName;
            _receivingFileSize = fileSize;
            _receivedBytes = 0;

            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            // Ensure unique name
            string safeName = Path.GetFileName(fileName);
            string fullPath = Path.Combine(downloadsPath, safeName);

            // Rename if exists
            int counter = 1;
            while (File.Exists(fullPath))
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(safeName);
                string ext = Path.GetExtension(safeName);
                fullPath = Path.Combine(downloadsPath, $"{nameNoExt} ({counter}){ext}");
                counter++;
            }

            _tempPath = fullPath + ".tmp"; // Write to .tmp first
            _fileStream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            TransferStarted?.Invoke(Path.GetFileName(fullPath), fileSize);
        }
        catch (Exception ex)
        {
            TransferFailed?.Invoke($"Start failed: {ex.Message}");
            _fileStream = null;
        }
    }

    public void ReceiveChunk(byte[] chunk)
    {
        if (_fileStream == null) return;

        try
        {
            _fileStream.Write(chunk, 0, chunk.Length);
            _receivedBytes += chunk.Length;

            double progress = (double)_receivedBytes / _receivingFileSize;
            TransferProgress?.Invoke(progress);
        }
        catch (Exception ex)
        {
            CancelReceive($"Write failed: {ex.Message}");
        }
    }

    public void FinishReceiving()
    {
        if (_fileStream == null) return;

        try
        {
            _fileStream.Close();
            _fileStream.Dispose();
            _fileStream = null;

            // Rename from .tmp to final
            string finalPath = _tempPath.Substring(0, _tempPath.Length - 4);
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(_tempPath, finalPath);

            TransferCompleted?.Invoke(finalPath);
        }
        catch (Exception ex)
        {
            CancelReceive($"Finish failed: {ex.Message}");
        }
    }

    private void CancelReceive(string reason)
    {
        if (_fileStream != null)
        {
            _fileStream.Close();
            _fileStream.Dispose();
            _fileStream = null;
        }
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
        TransferFailed?.Invoke(reason);
    }

    // Sender logic
    public async Task SendFile(string path, Func<byte[], Task> sendChunkCallback, Func<Task> endCallback)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[32 * 1024]; // 32KB chunks
                int read;
                long totalSent = 0;
                long totalSize = fs.Length;

                while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (read == buffer.Length)
                    {
                        await sendChunkCallback(buffer);
                    }
                    else
                    {
                        byte[] partial = new byte[read];
                        Array.Copy(buffer, partial, read);
                        await sendChunkCallback(partial);
                    }

                    totalSent += read;
                    // Optional: Report sending progress?
                }
                await endCallback();
            }
        }
        catch (Exception ex)
        {
            // Log?
            System.Diagnostics.Debug.WriteLine("Send File Error: " + ex.Message);
        }
    }
}
