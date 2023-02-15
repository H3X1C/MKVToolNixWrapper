namespace MKVToolNixWrapper.Dtos
{
    public enum FileStatusEnum
    {
        Unprocessed = 0,    // No colour
        PassedAnalysis = 1, // No colour
        FailedAnalysis = 2, // Red
        WritingFile = 3,    // Orange
        WrittenFile = 4,    // Green
        Error = 5           // Dark Red
    }
}