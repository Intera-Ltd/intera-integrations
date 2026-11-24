namespace Intera.Revolut.Api.Models;

public sealed record RevolutBalanceDto(
    string? AccountId,
    string? AccountName,
    string? Currency,
    decimal? Balance,
    string? State);
