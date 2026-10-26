namespace Kvx.Server;

/// <summary>A setting kvx cannot work with; its message names the setting and is shown as is, without a stack trace.</summary>
public sealed class ConfigurationException(string message) : InvalidOperationException(message);
