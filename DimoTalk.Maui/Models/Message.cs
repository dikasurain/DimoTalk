namespace DimoTalk.Maui.Models;

public enum MessageRole { System, User, Assistant }

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string RoleName => Role.ToString().ToLowerInvariant();

    public override string ToString() => $"{Role}: {Content}";
}
