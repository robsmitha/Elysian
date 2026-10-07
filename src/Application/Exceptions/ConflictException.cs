namespace Elysian.Application.Exceptions
{
    /// <summary>
    /// The request can't be done in the resource's current state. The message is written for the client and is safe
    /// to return, e.g. "Only guest accounts can be deleted."
    /// </summary>
    public class ConflictException(string message) : Exception(message);
}
