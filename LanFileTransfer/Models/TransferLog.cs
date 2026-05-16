namespace LanFileTransfer.Models;

public class TransferLog
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string FileName { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public string Icon => Direction == "发送" ? "📤" : "📥";

    public string StatusIcon => Success ? "✅" : "❌";

    public string FileSizeDisplay
    {
        get
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = FileSize;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}