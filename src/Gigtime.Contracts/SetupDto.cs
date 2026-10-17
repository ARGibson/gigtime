namespace Gigtime.Contracts;

public sealed record SetupDto(
	string ServiceUnit,
	bool ServiceInstalled,
	string SocketUnit,
	bool SocketInstalled,
	string SocketPath,
	bool SocketExists,
	string DatabasePath,
	bool Healthy,
	bool Changed,
	bool SocketActivation);
