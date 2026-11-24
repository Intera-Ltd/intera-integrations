namespace Intera.Eurobank.Api.Infrastructure;

public sealed class EurobankAuthorizationException(string message) : Exception(message);

public sealed class EurobankStateException(string message) : Exception(message);
