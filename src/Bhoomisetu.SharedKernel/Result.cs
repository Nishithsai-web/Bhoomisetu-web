namespace Bhoomisetu.SharedKernel;

/// <summary>A domain error with a stable machine-readable code and a human message (English; the web app localises by code).</summary>
public sealed record Error(string Code, string Message);

/// <summary>Outcome of an operation that can fail for an expected business reason.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = null;
    }

    private Result(Error error)
    {
        _value = default;
        Error = error;
    }

    public bool IsSuccess => Error is null;

    public Error? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result failed with {Error!.Code}; check IsSuccess before reading Value.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(string code, string message) => new(new Error(code, message));

    public static Result<T> Failure(Error error) => new(error);
}
