namespace SafeVault.Api.Models;

public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record CreateNoteRequest(string Content);
public sealed record NoteResponse(int Id, string Content);
