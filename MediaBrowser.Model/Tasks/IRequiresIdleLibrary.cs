namespace MediaBrowser.Model.Tasks
{
    /// <summary>
    /// Marks a scheduled task that must not run while a library scan is in progress.
    /// </summary>
    /// <remarks>
    /// Tasks that walk the whole library and write item rows back contend with the scan for the same
    /// data, which makes both slower and invites lock timeouts. The task runner checks this before
    /// starting the task and skips the run; the task's own triggers then schedule the next attempt as
    /// usual, so a task marked with this may be passed over entirely while scans keep overlapping it.
    /// </remarks>
    public interface IRequiresIdleLibrary
    {
    }
}
