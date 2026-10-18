namespace Gigtime.Platform;

public interface IServiceInstaller
{
	Task<ServiceInstallation> InstallAsync(string executable, CancellationToken cancellationToken = default);
}

public sealed record ServiceInstallation(bool Changed, bool SocketActivation);
