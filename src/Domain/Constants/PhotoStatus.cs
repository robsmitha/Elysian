namespace Elysian.Domain.Constants
{
    public enum PhotoStatus
    {
        /// <summary>
        /// Row created and an upload URL issued; the original may not be in storage yet.
        /// </summary>
        Uploaded,
        Processing,
        Ready,
        Failed
    }
}
