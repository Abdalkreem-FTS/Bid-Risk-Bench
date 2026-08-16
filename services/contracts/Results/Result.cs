using System.ComponentModel;
using System.Text.Json.Serialization;

namespace BidRisk.Contracts.Results;

public static class Result
{
    public static Success Success => default;
    public static Created Created => default;
    public static Deleted Deleted => default;
    public static Updated Updated => default;
}

// A value or a list of errors, never both. The implicit conversions let a method just
// `return foo;` or `return SomeError;`.
public sealed class Result<TValue> : IResult<TValue>
{
    private readonly List<Error>? _errors;
    private readonly TValue? _value;

    [JsonConstructor]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("For serializer only.", true)]
    public Result(TValue? value, List<Error>? errors, bool isSuccess)
    {
        if (isSuccess)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
            _errors = [];
            IsSuccess = true;
        }
        else
        {
            if (errors == null || errors.Count == 0)
            {
                throw new ArgumentException("Provide at least one error.", nameof(errors));
            }

            _errors = errors;
            _value = default!;
            IsSuccess = false;
        }
    }

    private Result(Error error)
    {
        _errors = [error];
    }

    private Result(List<Error> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            throw new ArgumentException(
                "Cannot create a Result<TValue> from an empty collection of errors. Provide at least one error.",
                nameof(errors));
        }

        _errors = errors;

        IsSuccess = false;
    }

    private Result(TValue value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _value = value;

        IsSuccess = true;
    }

    public bool IsError => !IsSuccess;

    public Error TopError => _errors?.Count > 0 ? _errors[0] : default;

    public bool IsSuccess { get; }

    public List<Error> Errors => IsError ? _errors! : [];

    public TValue Value => IsSuccess ? _value! : default!;

    public static Result<TValue> From(TValue value) => new(value);

    public TNextValue Match<TNextValue>(Func<TValue, TNextValue> onValue, Func<List<Error>, TNextValue> onError)
    {
        return IsSuccess ? onValue(Value) : onError(Errors);
    }

    public static implicit operator Result<TValue>(TValue value)
    {
        return new Result<TValue>(value);
    }

    public static implicit operator Result<TValue>(Error error)
    {
        return new Result<TValue>(error);
    }

    public static implicit operator Result<TValue>(List<Error> errors)
    {
        return new Result<TValue>(errors);
    }
}

public readonly record struct Success;

public readonly record struct Created;

public readonly record struct Deleted;

public readonly record struct Updated;
