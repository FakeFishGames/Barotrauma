namespace HomeAndHarbor;

/// <summary>
/// Helper class to manage periodic validation timing (eg. for Events), avoiding repeated checks every frame.
/// </summary>
internal class PeriodicValidationTimer
{
    private readonly float retryDelay;
    private float timeSinceLastCheck; 

    public PeriodicValidationTimer()
    {
        retryDelay = 5.0f;
        timeSinceLastCheck = retryDelay;
    }
    
    public PeriodicValidationTimer(float delay)
    {
        if (delay is float overridingDelay)
        {
            retryDelay = overridingDelay;
        }
        
        // Start with the check first, in case the validation can already pass
        timeSinceLastCheck = retryDelay;
    }
    
    /// <summary>
    /// Call this every frame. Returns true when enough time has passed to perform another validation check.
    /// </summary>
    public bool ShouldCheckNow(float deltaTime)
    {
        timeSinceLastCheck += deltaTime;
        if (timeSinceLastCheck < retryDelay)
        {
            return false;
        }
        timeSinceLastCheck = 0.0f;
        return true;
    }

    /// <summary>
    /// Reset the timer to check immediately on next call to ShouldCheckNow.
    /// </summary>
    public void Reset()
    {
        timeSinceLastCheck = retryDelay;
    }
}
