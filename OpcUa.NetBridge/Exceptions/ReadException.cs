namespace OpcUa.NetBridge.Exceptions;

/// <summary>
/// Error reading value
/// </summary>
public class ReadException : Exception
{

    /// <summary>
    /// Main method
    /// </summary>
    /// <param name="message">
    /// Error message
    /// </param>
    public ReadException(string? message) : base(message)
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
    public ReadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
