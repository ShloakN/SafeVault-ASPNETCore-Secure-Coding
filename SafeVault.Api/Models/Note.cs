namespace SafeVault.Api.Models;

public sealed class Note
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Content { get; set; } = "";
}
