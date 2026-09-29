namespace LMMs.Api.Application.Exceptions;

public sealed class ResourceNotFoundException(string message) : Exception(message);
public sealed class RequestValidationException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
