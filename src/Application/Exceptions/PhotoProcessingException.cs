namespace Elysian.Application.Exceptions
{
    /// <summary>
    /// The uploaded file can never be processed (not an image, unsupported, too large), so retrying is pointless.
    /// </summary>
    public class PhotoProcessingException : Exception
    {
        public PhotoProcessingException(string message)
            : base(message)
        {
        }

        public PhotoProcessingException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
