namespace Agent.Application.Common;

public sealed class ResourceNotFoundException(string message) : Exception(message);
public sealed class RequestValidationException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
