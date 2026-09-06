namespace NavisFavorites.Services
{
    public class OperationResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public int ResolvedCount { get; set; }

        public int InvalidCount { get; set; }

        public static OperationResult Fail(string message) => new OperationResult
        {
            Success = false,
            Message = message
        };

        public static OperationResult Ok(string message, int resolvedCount = 0, int invalidCount = 0) => new OperationResult
        {
            Success = true,
            Message = message,
            ResolvedCount = resolvedCount,
            InvalidCount = invalidCount
        };
    }
}
