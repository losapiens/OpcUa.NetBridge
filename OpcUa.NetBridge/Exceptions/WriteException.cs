namespace OpcUa.NetBridge.Exceptions;

/// <summary>
/// Error writing value
/// </summary>
internal class WriteException : Exception
{

    /// <summary>
    /// Main method
    /// </summary>
    /// <param name="message">
    /// Error message
    /// </param>
    public WriteException(string? message) : base(message)
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
    public WriteException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
