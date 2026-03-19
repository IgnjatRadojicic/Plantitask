namespace Plantitask.Core.Common
{
    public class Result<T>
    {
        public T? Value { get; }
        public Error? Error { get; }
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;

        private Result(T value)
        {
            Value = value;
            IsSuccess = true;
        }

        private Result(Error error)
        {
            Error = error;
            IsSuccess = false;
        }

        public static Result<T> Success(T value) => new(value);
        public static Result<T> Failure(Error error) => new(error);

        public static implicit operator Result<T>(T value) => Success(value);
        public static implicit operator Result<T>(Error error) => Failure(error);
    }

    public class Result
    {
        public Error? Error { get; }
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;

        private Result() { IsSuccess = true; }
        private Result(Error error) { Error = error; IsSuccess = false; }

        public static Result Success() => new();
        public static Result Failure(Error error) => new(error);

        public static implicit operator Result(Error error) => Failure(error);
    }
}