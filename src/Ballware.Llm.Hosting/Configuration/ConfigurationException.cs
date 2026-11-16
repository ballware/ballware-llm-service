namespace Ballware.Llm.Hosting.Configuration;

public class ConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
