namespace Intera.Revolut.Api.Infrastructure;

public sealed class RevolutAuthorizationException(string message) : Exception(message);

public sealed class RevolutStateException(string message) : Exception(message);
