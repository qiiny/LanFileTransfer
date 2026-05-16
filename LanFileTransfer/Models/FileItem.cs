using System.IO;

namespace LanFileTransfer.Models;

public class FileItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public DateTime AddedTime { get; set; } = DateTime.Now;

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

    public string FileTypeIcon
    {
        get
        {
            var ext = Path.GetExtension(FileName).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "🖼",
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "🎬",
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "🎵",
                ".pdf" => "📄",
                ".doc" or ".docx" => "📝",
                ".xls" or ".xlsx" => "📊",
                ".ppt" or ".pptx" => "📽",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
                ".txt" or ".md" or ".log" => "📃",
                ".exe" or ".dll" or ".msi" => "⚙",
                ".apk" => "📱",
                _ => "📎"
            };
        }
    }
}