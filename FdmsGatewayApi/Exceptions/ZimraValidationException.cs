namespace FdmsGatewayApi.Exceptions;

public class ZimraValidationException : Exception
{
    public string ErrorCode { get; }

    public ZimraValidationException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public ZimraValidationException(string errorCode, string message, Exception innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
