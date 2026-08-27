namespace DimoTalk.Maui.Models;

public class MemoryHit
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Distance { get; set; }
    public double Confidence { get; set; } = 1.0;
    public DateTime LastAccessedAt { get; set; }
}
