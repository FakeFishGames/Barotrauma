namespace Barotrauma
{
    public readonly record struct DlcDownloadProgress(bool IsActive, bool IsValid, float Progress);
}