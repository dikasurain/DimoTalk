namespace DimoTalk.Maui.Models;

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = "新对话";
    public List<Message> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public void AddMessage(Message msg)
    {
        Messages.Add(msg);
        UpdatedAt = DateTime.Now;
    }
}
