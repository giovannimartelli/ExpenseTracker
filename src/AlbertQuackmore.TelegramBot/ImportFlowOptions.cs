namespace AlbertQuackmore.TelegramBot;

public class ImportFlowOptions
{
    public int MinYear { get; set; } = 2020;
    public int MaxYear { get; set; } = 2100;
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB
    public string[] AllowedFileExtensions { get; set; } = [".xlsx"];
}
