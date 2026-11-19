namespace OpcUa.NetBridge.Exceptions;

/// <summary>
/// Server Error
/// </summary>
internal class ServerException : Exception
{
    /// <summary>
    /// Main method
    /// </summary>
    /// <param name="message">
    /// Error message
    /// </param>
    public ServerException(string? message) : base(message)
    {
    }

    /// <summary>
    /// Main method with inner exception
    /// </summary>
    /// <param name="message">
    /// Error message
    /// </param>
    /// <param name="innerException">
    /// Inner exception cause
    /// </param>
    public ServerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
