namespace Gigtime.Platform;

public interface IServiceInstaller
{
	Task InstallAsync(CancellationToken cancellationToken = default);
	Task UninstallAsync(CancellationToken cancellationToken = default);
}
