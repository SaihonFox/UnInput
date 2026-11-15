namespace UniversalInput.Core.Backends;

/// <summary>Backend, зарегистрированный в Core. <paramref name="Required"/> важен в режиме FailFast.</summary>
public sealed record BackendRegistration(IInputBackend Backend, bool Required = false);
