#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Thio_Universal_Agent;


public static class Globals
{
    /// <summary>
    /// Mirrors <see cref="AgentConfig.EnableDebugMode"/>. Set once at startup from config;
    /// read throughout the codebase as a fast static flag.
    /// </summary>
    internal static bool ENABLE_TESTING = false;

    /// <summary>
    /// Mirrors <see cref="GeneralConfig.MaxQueueSize"/>. Set once at startup from config;
    /// read by <see cref="Handlers.AgentActionParser"/> and the prompt builder.
    /// </summary>
    internal static int MAX_QUEUE_SIZE = 5;
}
